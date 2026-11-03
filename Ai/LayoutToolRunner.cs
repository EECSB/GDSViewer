using System.Text.Json;
using System.Text.Json.Nodes;
using GdsII;

namespace GDSViewer.Ai
{
    ///<summary>What the model asked to see: a cell, and the part of it in microns - or all of it when no area was given.</summary>
    public sealed record LookRequest(string Cell, (double Left, double Bottom, double Right, double Top)? Area, double MicronsPerUnit);

    ///
    ///The layout's tools, as a model is given them: every one of <see cref="AgentTools"/>, and a picture.
    ///
    ///**The layout's own tools, not a second set.** Each call goes through <see cref="AgentTools.Call(AgentSession,
    ///string, string)"/> - the same tools `gds mcp` will serve - so a change made from the chat is one step on the
    ///undo history the person uses, applied whole or not at all, and a refused call leaves the file as it was. This is
    ///what GraphDBViewer's query runner is to a database.
    ///
    ///**Plus one the library cannot have: <c>look</c>.** A picture is drawn by the browser, which is the one thing a
    ///library with no dependencies cannot do, so it is the host's to supply - <see cref="LookRequest"/> in, a PNG out -
    ///and left out where there is no host to draw it.
    ///
    public sealed class LayoutToolRunner : ILlmToolRunner, IEditCapableToolRunner
    {
        public const string LookName = AgentTools.LookName;

        private readonly AgentSession session;
        private readonly Func<LookRequest, CancellationToken, Task<LlmToolOutput>>? look;

        public LayoutToolRunner(AgentSession session, Func<LookRequest, CancellationToken, Task<LlmToolOutput>>? look = null)
        {
            this.session = session;
            this.look = look;
        }

        ///<summary>See <see cref="IEditCapableToolRunner"/>: off until the approval gate turns it on.</summary>
        public bool AllowEdits { get; set; }

        ///
        ///Told after every call that changed the layout, so the host can draw it again.
        ///
        ///Per call rather than once at the end of the answer: the person is watching, and a rectangle that appears
        ///when it is drawn is how they see what the model is doing while it is still doing it.
        ///
        public Func<AgentResult, Task>? Changed { get; set; }

        ///<summary>Whether any call through this runner has changed the layout.</summary>
        public bool ChangedAnything { get; private set; }

        ///
        ///Whether a change says what it did to the file's text, for its card to show as a diff - see TextChange. The
        ///assistant's window asks for it; nothing else that runs these tools has a card to show it on.
        ///
        public bool DiffsText { get; init; }

        ///
        ///The most records the whole file's text is written for, before and after a change. Past it - a chip, whose text
        ///is tens of megabytes, written twice a change - only the cell being worked in is, which is where nearly every
        ///change lands.
        ///
        public int MostRecordsWhole { get; init; } = 100_000;

        ///<summary>The tools switched off in the window's tool picker, by name: not offered, and refused when called anyway - see AiChat.ToolsOff.</summary>
        public IReadOnlySet<string> Off { get; init; } = new HashSet<string>();

        ///
        ///The PDK whose rules and layers read_rules, add_rules, read_layers and set_layers work on, or null where there is
        ///none to write into - the host's to give, as the picture is. See <see cref="RuleTools"/> and <see cref="LayerTools"/>.
        ///
        public RuleBook? Rules { get; init; }

        ///<summary>The examples the app ships, which list_examples and read_example read - see <see cref="ExampleTools"/>. Null where there are none.</summary>
        public ExampleLibrary? Examples { get; init; }

        ///<summary>The person's PDKs, which use_pdk puts in use - see <see cref="PdkTools"/>. Null where there are none to choose from.</summary>
        public PdkShelf? Pdks { get; init; }

        ///<summary>The examples read_example has opened, kept for asking about again.</summary>
        private readonly Dictionary<string, GDS> examplesRead = new(StringComparer.Ordinal);

        public IReadOnlyList<LlmTool> Tools
        {
            get { return ToolsFor(AllowEdits, look is not null, Rules is not null, Off, Examples is not null, Pdks is not null); }
        }

        ///<summary>The tools a runner offers, without one to ask - what the window weighs the next request by before there is a session to run them in.</summary>
        public static IReadOnlyList<LlmTool> ToolsFor(bool allowEdits, bool withLook)
        {
            return ToolsFor(allowEdits, withLook, new HashSet<string>());
        }

        public static IReadOnlyList<LlmTool> ToolsFor(bool allowEdits, bool withLook, IReadOnlySet<string> off)
        {
            return ToolsFor(allowEdits, withLook, false, off);
        }

        public static IReadOnlyList<LlmTool> ToolsFor(bool allowEdits, bool withLook, bool withRules, IReadOnlySet<string> off, bool withExamples = false, bool withPdks = false)
        {
            var tools = new List<LlmTool>();

            foreach (var tool in AgentTools.All)
            {
                if (tool.Edits && !allowEdits)
                    continue;

                if (off.Contains(tool.Name))
                    continue;

                tools.Add(new LlmTool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchemaJson = tool.InputSchema().ToJsonString(),
                    Changes = tool.Edits
                });
            }

            if (withLook && !off.Contains(LookName))
                tools.Add(LookTool);

            if (withRules && !off.Contains(RuleTools.ReadName))
                tools.Add(RuleTools.ReadTool);

            if (withRules && allowEdits && !off.Contains(RuleTools.AddName))
                tools.Add(RuleTools.AddTool);

            if (withRules && !off.Contains(LayerTools.ReadName))
                tools.Add(LayerTools.ReadTool);

            if (withRules && allowEdits && !off.Contains(LayerTools.SetName))
                tools.Add(LayerTools.SetTool);

            if (withExamples && !off.Contains(ExampleTools.ListName))
                tools.Add(ExampleTools.ListTool);

            if (withExamples && !off.Contains(ExampleTools.ReadName))
                tools.Add(ExampleTools.ReadTool);

            if (withPdks && allowEdits && !off.Contains(PdkTools.UseName))
                tools.Add(PdkTools.UseTool);

            return tools;
        }

        ///
        ///The picture tool, described for the model.
        ///
        ///Its one argument is written through <see cref="AgentParameter"/>, so its schema keeps to the keywords every
        ///provider takes - the same reason the library's own tools are.
        ///
        public static LlmTool LookTool
        {
            get
            {
                //The library's declaration of it - `gds mcp`'s is the same one - in the provider's shape.
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject { [AgentTools.LookArea.Name] = AgentTools.LookArea.Schema() }
                };

                return new LlmTool
                {
                    Name = LookName,
                    Description = AgentTools.LookDescription,
                    InputSchemaJson = schema.ToJsonString()
                };
            }
        }

        public async Task<LlmToolOutput> RunToolAsync(string name, string argumentsJson, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            //Switched off, so not offered - and a model that calls one anyway, from what the conversation shows of it, is
            //told why in words.
            if (Off.Contains(name))
                return refused($"{name} is switched off in this conversation: the person turned it off in the window's tool picker. Say what you would have used it for, and that it can be switched back on there.");

            if (name == LookName && look is not null)
                return await lookAsync(argumentsJson, ct);

            if (name == RuleTools.ReadName && Rules is not null)
                return await RuleTools.ReadAsync(Rules);

            if (name == RuleTools.AddName && Rules is not null)
            {
                if (!AllowEdits)
                    return refused($"{name} changes the PDK's rules, and changes are not allowed here.");

                return await RuleTools.AddAsync(Rules, argumentsJson);
            }

            if (name == LayerTools.ReadName && Rules is not null)
                return await LayerTools.ReadAsync(Rules);

            if (name == ExampleTools.ListName && Examples is not null)
                return ExampleTools.List(Examples, argumentsJson);

            if (name == ExampleTools.ReadName && Examples is not null)
                return await ExampleTools.ReadAsync(Examples, examplesRead, argumentsJson, ct);

            if (name == LayerTools.SetName && Rules is not null)
            {
                if (!AllowEdits)
                    return refused($"{name} changes the PDK's layers, and changes are not allowed here.");

                return await LayerTools.SetAsync(Rules, argumentsJson);
            }

            if (name == PdkTools.UseName && Pdks is not null)
            {
                if (!AllowEdits)
                    return refused($"{name} puts a PDK in use, and changes are not allowed here.");

                var used = await PdkTools.UseAsync(Pdks, argumentsJson);

                //Its rules may have come with it: the rest of this answer's changes are checked against them.
                session.Deck = Pdks.Deck();

                return used;
            }

            //Not offered, so not honored either: a model that names a changing tool it was never given is refused in
            //words rather than obeyed.
            if (AgentTools.Named(name) is AgentTool tool && tool.Edits && !AllowEdits)
                return new LlmToolOutput { Text = $"{name} changes the layout, and changes are not allowed here.", IsError = true };

            TextBefore? before = null;

            if (DiffsText && AgentTools.Named(name) is AgentTool editing && editing.Edits)
                before = textBefore();

            var result = AgentTools.Call(session, name, argumentsJson);
            TextChange? change = null;

            if (result.Changed)
            {
                ChangedAnything = true;

                if (before is not null)
                    change = textChangeSince(before);

                if (Changed is not null)
                    await Changed(result);
            }

            return new LlmToolOutput { Text = result.Text, IsError = result.IsError, TextChange = change };
        }

        ///<summary>The text before a change: the whole file's, or one cell's from the line it starts on.</summary>
        private sealed record TextBefore(string Text, int FirstLine, string? Cell);

        private TextBefore? textBefore()
        {
            if (session.Gds.Records.Count <= MostRecordsWhole)
                return new TextBefore(session.Gds.AsText(), 1, null);

            if (WorkingCell(session) is not string cell || cellText(cell) is not (string text, int firstLine))
                return null;

            return new TextBefore(text, firstLine, cell);
        }

        private TextChange? textChangeSince(TextBefore before)
        {
            if (before.Cell is null)
                return TextChange.Between(before.Text, session.Gds.AsText());

            if (cellText(before.Cell) is not (string after, _))
                return null;

            return TextChange.Between(before.Text, after, before.FirstLine);
        }

        ///<summary>One cell's lines of the file's text, its BGNSTR to its ENDSTR, and the line they start on - or null for a cell not in the file.</summary>
        private (string Text, int FirstLine)? cellText(string cell)
        {
            var gds = session.Gds;

            if (Hierarchy.Named(gds, cell) is not { } structure)
                return null;

            int start = gds.Records.IndexOf(structure.BGNSTR);
            int end = gds.Records.IndexOf(structure.ENDSTR);

            if (start < 0 || end < start)
                return null;

            return (gds.AsText(start, end - start + 1), start + 1);
        }

        private async Task<LlmToolOutput> lookAsync(string argumentsJson, CancellationToken ct)
        {
            if (look is null)
                return refused("There is nothing here to draw a picture with.");

            if (WorkingCell(session) is not string cell)
                return refused("No cell is being worked in, and the library has more than one top cell. Choose one with open_cell first.");

            (double, double, double, double)? area;

            try
            {
                area = areaIn(argumentsJson);
            }
            catch (JsonException wrong)
            {
                return refused($"The arguments to look are not JSON: {wrong.Message}");
            }
            catch (FormatException wrong)
            {
                return refused(wrong.Message);
            }

            return await look(new LookRequest(cell, area, session.MicronsPerUnit), ct);
        }

        ///
        ///The cell the tools work in: the one chosen, or the library's only top cell when none was.
        ///
        ///What <see cref="AgentSession"/> settles on before every call, worked out the same way here for the one tool
        ///that is not the library's. Null when it cannot be told - no cell chosen and several tops to choose from.
        ///
        public static string? WorkingCell(AgentSession session)
        {
            if (session.Cell is string chosen && Hierarchy.Named(session.Gds, chosen) is not null)
                return chosen;

            string? only = null;

            foreach (var summary in Hierarchy.Summarize(session.Gds))
            {
                if (!summary.IsTop)
                    continue;

                if (only is not null)
                    return null;

                only = summary.Name;
            }

            return only;
        }

        ///<summary>The area a look asked for, or null for the whole cell. Throws FormatException, saying what is wrong, for one that is not an area.</summary>
        private static (double, double, double, double)? areaIn(string argumentsJson)
        {
            string text = argumentsJson;

            if (string.IsNullOrWhiteSpace(text))
                return null;

            using var document = JsonDocument.Parse(text);

            var root = document.RootElement;

            //Some providers send the arguments as a JSON string holding the JSON - read through, the way AgentTools does.
            if (root.ValueKind == JsonValueKind.String)
                return areaIn(root.GetString() ?? "");

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("area", out var area) || area.ValueKind == JsonValueKind.Null)
                return null;

            if (area.ValueKind != JsonValueKind.Array || area.GetArrayLength() != 4)
                throw new FormatException("area is four numbers, [left, bottom, right, top], in microns.");

            var sides = new double[4];

            for (int i = 0; i < 4; i++)
            {
                if (area[i].ValueKind != JsonValueKind.Number || !area[i].TryGetDouble(out sides[i]) || !double.IsFinite(sides[i]))
                    throw new FormatException("area is four numbers, [left, bottom, right, top], in microns.");
            }

            if (sides[2] <= sides[0] || sides[3] <= sides[1])
                throw new FormatException("area's right has to be past its left, and its top above its bottom.");

            return (sides[0], sides[1], sides[2], sides[3]);
        }

        private static LlmToolOutput refused(string why)
        {
            return new LlmToolOutput { Text = why, IsError = true };
        }
    }
}
