using System.Text;
using System.Text.Json;
using static GdsII.GDS;

namespace GdsII
{
    ///
    ///What one call to an editing tool changes, worked out and not yet applied.
    ///
    ///The tool builds this and the runner applies it, so every tool's change goes onto the history the same
    ///way: as one step, all of it or none, with the rules checked around it.
    ///
    internal sealed class AgentChange
    {
        ///<summary>What the undo button calls the step.</summary>
        public required string Label { get; init; }

        ///<summary>The cell the change happens in, which is where its area is - or null for a change to the library, like a new empty cell.</summary>
        public required StructureModel? Cell { get; init; }

        ///<summary>The answer, given the numbers of the shapes the change added - which only exist once it has run.</summary>
        public required Func<IReadOnlyList<int>, string> Says { get; init; }

        public List<LayoutEdit> Edits { get; } = new List<LayoutEdit>();

        ///<summary>The edits among those that add a shape, whose new shapes get numbers.</summary>
        public List<AddElement> Adding { get; } = new List<AddElement>();

        ///<summary>Everywhere the change touches, before and after, in the cell's coordinates - what the rules are checked around.</summary>
        public Bounds Area { get; private set; } = Bounds.Empty;

        public void Add(AddElement adding)
        {
            Edits.Add(adding);
            Adding.Add(adding);
        }

        public void Covers(Bounds box)
        {
            Area = Area.Union(box);
        }

        public void Covers(IReadOnlyList<Element.Point> outline)
        {
            if (outline.Count > 0)
                Covers(Bounds.Of(outline));
        }
    }

    ///
    ///The tools an assistant edits a layout with: drawing, editing and reading, thirty-one of them, each
    ///over the edits this library already has.
    ///
    ///**Typed tools rather than GDSII text.** A model asked to write records gets a byte count, a datatype or a
    ///closing corner wrong somewhere in a hundred lines, and the file it produces is one nothing can open - or
    ///worse, one that opens. A model calling `draw_rectangle` with a layer name and two corners in microns has
    ///nothing to get wrong that cannot be checked before anything is written, and what it is told when it does
    ///is what it needs to try again.
    ///
    ///**Every change is one step on the history the person undoes with.** However many shapes one call adds
    ///or replaces, it is one press of Undo - see <see cref="apply"/>.
    ///
    ///**Hosts are thin.** A chat panel, an MCP server and a test all do the same three things: hand
    ///<see cref="All"/> to the model in its provider's format, pass each call it makes to
    ///<see cref="Call(AgentSession, string, string)"/>, and send the text back. <see cref="Conventions"/> is
    ///the paragraph that belongs in the system prompt beside them.
    ///
    public static partial class AgentTools
    {
        ///
        ///What a model has to know that no single tool's description says: the units, the cell, the shape
        ///numbers and the undo. For a host's system prompt.
        ///
        public const string Conventions =
            "The layout is a GDSII library of cells, and the tools work in one cell at a time: the one open_cell chose, "
            + "or the library's only top cell. Coordinates and sizes are microns, in that cell's own coordinates, x to "
            + "the right and y up. Layers go by name, like met1, or by numbers, like 68/20; list_layers says which there "
            + "are. Shapes are named by number, like #12 - find_shapes and every drawing tool give them out, and a "
            + "number stays the shape's for the whole conversation, through moves and undo. Every change is one step "
            + "the person can undo, and undo_last_change takes back your own last one. When a rule deck is loaded, "
            + "each change is followed by a check of the rules around it, and the answer says what it broke. When the "
            + "deck sets a manufacturing grid, the corners a tool works out - of curves, bends, lettering, booleans and "
            + "grows - are put on it, and the corners you give are drawn where you give them.";

        ///
        ///Every tool, reading ones first, in the order a model is shown them.
        ///
        ///Built on first use rather than by an initializer, because the tools are spread over several files of
        ///one partial class and the order their static initializers run in is not something C# promises.
        ///
        public static IReadOnlyList<AgentTool> All
        {
            get
            {
                built ??= build();

                return built;
            }
        }

        private static List<AgentTool>? built;

        private static List<AgentTool> build()
        {
            var tools = new List<AgentTool>();

            addReading(tools);
            addStructureList(tools);
            addDrawing(tools);
            addStructurePlacing(tools);
            addEditing(tools);

            return tools;
        }

        public static AgentTool? Named(string name)
        {
            foreach (var tool in All)
            {
                if (tool.Name == name)
                    return tool;
            }

            return null;
        }

        ///
        ///The `look` tool, which this library cannot run - a picture needs a browser to draw it - but declares, so
        ///the AI assistant's and `gds mcp`'s are one declaration: the name, what a model is told it is, and its one
        ///argument. Each host adds where its picture comes from.
        ///
        public const string LookName = "look";

        public const string LookDescription = "A picture of the cell being worked in, as the person's 2D view draws it, with what is drawn where and in "
            + "which colors. For seeing a layout's shape and arrangement at a glance; for exact numbers use find_shapes and measure.";

        public static AgentParameter LookArea
        {
            get { return may("area", AgentValue.Box, "The part to look at, as [left, bottom, right, top] in microns in the cell's own coordinates. Leave it out to see the whole cell."); }
        }

        ///<summary>A count and its noun, singular for one: "1 rule", "2 rules". For every host's answers, so a count reads the same wherever it is said.</summary>
        public static string Counted(int count, string noun)
        {
            if (count == 1)
                return $"1 {noun}";

            return FormattableString.Invariant($"{count} {noun}s");
        }

        ///
        ///Runs one call, with its arguments as the JSON text a provider sends.
        ///
        ///**Never throws for anything the model did.** A name that is not a tool, arguments that are not JSON, a
        ///layer that does not exist - each comes back as an error result saying so, because the model is the
        ///one who has to act on it and an exception reaches only the host. Arguments sent as a JSON string
        ///holding the JSON, which some providers do, are read through.
        ///
        public static AgentResult Call(AgentSession session, string name, string arguments)
        {
            string text = arguments;

            if (string.IsNullOrWhiteSpace(text))
                text = "{}";

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind == JsonValueKind.String && document.RootElement.GetString() is string inner)
                {
                    document.Dispose();
                    document = JsonDocument.Parse(inner);
                }
            }
            catch (JsonException wrong)
            {
                return refused($"The arguments to {name} are not JSON: {wrong.Message}");
            }

            using (document)
            {
                return Call(session, name, document.RootElement);
            }
        }

        ///<summary>The same, with the arguments already parsed - for a host whose provider SDK hands them over as JSON.</summary>
        public static AgentResult Call(AgentSession session, string name, JsonElement arguments)
        {
            if (Named(name) is not AgentTool tool)
                return refused($"There is no tool called {name}. The tools are {string.Join(", ", All.Select(each => each.Name))}.");

            session.Fresh();
            session.Settle();

            try
            {
                return tool.Run(new AgentArguments(tool, arguments), session);
            }
            catch (AgentProblem problem)
            {
                return refused(problem.Message);
            }
            catch (Exception failed) when (failed is InvalidOperationException
                || failed is ArgumentException
                || failed is InvalidDataException
                || failed is OverflowException)
            {
                //A fault of ours rather than the model's, said as one - the model can still try another way.
                return refused($"{tool.Name} failed: {failed.Message}");
            }
        }

        private static AgentResult refused(string why)
        {
            return new AgentResult { Text = why, IsError = true };
        }

        private static AgentResult answered(string text)
        {
            return new AgentResult { Text = text };
        }

        ///
        ///Applies a change as one step on the history, checks the rules around it, and says what it did.
        ///
        ///**All of it or none.** The parts are applied one at a time, and a part that fails takes back the ones
        ///before it - so a refusal leaves the layout exactly as it was, rather than half changed with nothing on
        ///the stack to undo it by. Only once every part is in is the whole recorded, as one step.
        ///
        private static AgentResult apply(AgentSession session, AgentChange change)
        {
            if (change.Edits.Count == 0)
                throw new AgentProblem("That would change nothing.");

            AgentSession.RuleSnapshot? before = null;

            if (change.Cell is not null)
                before = session.RulesAround(change.Cell, change.Area);

            int version = session.History.Version;
            var applied = new List<LayoutEdit>();

            try
            {
                foreach (var edit in change.Edits)
                {
                    edit.Apply();
                    applied.Add(edit);
                }
            }
            catch (InvalidOperationException failed)
            {
                for (int i = applied.Count - 1; i >= 0; i--)
                    applied[i].Revert();

                throw new AgentProblem($"The change could not be made, and nothing was changed: {failed.Message}");
            }

            var step = new CompoundEdit(change.Label, change.Edits);

            session.History.Record(step);
            session.Made(step, version);

            var made = new List<int>();

            foreach (var adding in change.Adding)
            {
                if (adding.Made is ElementModel model)
                    made.Add(session.IdOf(model, change.Cell));
            }

            var said = new StringBuilder(change.Says(made));

            if (before is not null
                && change.Cell is not null
                && session.RulesAround(change.Cell, change.Area) is AgentSession.RuleSnapshot after)
                said.Append('\n').Append(session.RulesSay(before, after));

            return new AgentResult { Text = said.ToString(), Edit = step, Made = made, Changed = true };
        }

        ///
        ///Shape numbers as an answer writes them: "#3", "#3 and #4", "#3, #4 and #9" - and a run of three or
        ///more in a row as "#40 to #52", since writing a word as shapes hands out a run of them.
        ///
        private static string numbered(IReadOnlyList<int> ids)
        {
            var written = new List<string>();
            int at = 0;

            while (at < ids.Count)
            {
                int end = at;

                while (end + 1 < ids.Count && ids[end + 1] == ids[end] + 1)
                    end++;

                if (end - at >= 2)
                {
                    written.Add($"#{ids[at]} to #{ids[end]}");
                }
                else
                {
                    for (int i = at; i <= end; i++)
                        written.Add($"#{ids[i]}");
                }

                at = end + 1;
            }

            if (written.Count == 0)
                return "none";

            if (written.Count == 1)
                return written[0];

            return $"{string.Join(", ", written.Take(written.Count - 1))} and {written[^1]}";
        }

        ///<summary>"1 shape", "3 shapes".</summary>
        private static string shapes(int count)
        {
            return counted(count, "shape");
        }

        private static string counted(int count, string noun)
        {
            return Counted(count, noun);
        }

        #region Parameters ******************************************************************

        private static AgentParameter needs(string name, AgentValue value, string description, int fewest = 0)
        {
            return new AgentParameter { Name = name, Value = value, Description = description, Required = true, Fewest = fewest };
        }

        private static AgentParameter may(string name, AgentValue value, string description, int fewest = 0)
        {
            return new AgentParameter { Name = name, Value = value, Description = description, Fewest = fewest };
        }

        private static AgentParameter oneOf(string name, bool required, string description, params string[] choices)
        {
            return new AgentParameter
            {
                Name = name,
                Value = AgentValue.Text,
                Description = description,
                Required = required,
                Choices = choices
            };
        }

        private static AgentParameter layer(string description)
        {
            return needs("layer", AgentValue.Text, description);
        }

        private static AgentParameter shapesParameter(string description, int fewest = 1)
        {
            return needs("shapes", AgentValue.Shapes, description, fewest);
        }

        #endregion **************************************************************************
    }
}
