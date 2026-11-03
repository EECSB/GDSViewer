using System.Text;
using static GdsII.GDS;

namespace GdsII
{
    public static partial class AgentTools
    {
        ///<summary>The shape kinds find_shapes can be asked for.</summary>
        private static readonly string[] Kinds = { "any", "rectangle", "polygon", "path", "label", "placement" };

        ///<summary>The most shapes measure compares at once - every pair of ten is already forty-five lines.</summary>
        private const int MostMeasured = 10;

        ///<summary>How many cells list_cells lists before saying how many more there are.</summary>
        private const int MostCellRows = 300;

        private static void addReading(List<AgentTool> tools)
        {
            tools.Add(new AgentTool(
                "list_layers",
                "Lists the layers in the file: each one's name and layer/datatype numbers, what it is for in connectivity "
                + "(conductor or via) when the layermap says, and how many shapes the cell being worked in has on it. "
                + "Call this before drawing, to know what the layers are called.",
                false,
                Array.Empty<AgentParameter>(),
                listLayers));

            tools.Add(new AgentTool(
                "list_cells",
                "Lists the cells in the library as a tree, outermost first: which are top cells, what each places, how "
                + "many times each is placed, and which one is being worked in.",
                false,
                Array.Empty<AgentParameter>(),
                listCells));

            tools.Add(new AgentTool(
                "open_cell",
                "Chooses the cell to work in. Drawing tools put new shapes in it, and find_shapes looks in it. A change "
                + "to a cell shows everywhere the cell is placed. Coordinates are always the cell's own.",
                false,
                new[] { needs("name", AgentValue.Text, "The cell's name, as list_cells writes it.") },
                openCell));

            tools.Add(new AgentTool(
                "find_shapes",
                "Lists the shapes in the cell being worked in, with their numbers, layers, sizes and positions in microns. "
                + "Narrow it by area, layer and kind; with nothing given it lists every shape, up to the limit. Use the "
                + "numbers it gives (#12) to name shapes to the editing tools.",
                false,
                new[]
                {
                    may("area", AgentValue.Box, "Only shapes touching this box, [left, bottom, right, top] in microns."),
                    may("layer", AgentValue.Text, "Only shapes on this layer, by name or numbers."),
                    oneOf("kind", false, "Only shapes of this kind. A placement is an instance or an array of another cell.", Kinds),
                    may("limit", AgentValue.Integer, "The most to list, 1 to 500. 50 when not given.")
                },
                findShapes));

            tools.Add(new AgentTool(
                "measure",
                "Measures shapes: each one's size, area and box, and with two or more, how far apart each pair is at the "
                + "closest - or that they touch or overlap. Up to 10 shapes, all in one cell.",
                false,
                new[] { shapesParameter("The shapes to measure, by number.") },
                measure));

            tools.Add(new AgentTool(
                "check_rules",
                "Checks the layout against the loaded design rule deck and lists what it breaks, rule by rule, with where. "
                + "Checks the cell being worked in, in its coordinates, unless everything is true; density and antenna "
                + "rules are only checked over everything or a whole cell, never inside an area.",
                false,
                new[]
                {
                    may("everything", AgentValue.Flag, "Check the whole layout as it is drawn, in the top cell's coordinates, rather than the cell being worked in."),
                    may("area", AgentValue.Box, "Only report what lies in this box, [left, bottom, right, top] in microns.")
                },
                checkRules));
        }

        private static AgentResult listLayers(AgentArguments arguments, AgentSession session)
        {
            var layers = session.Gds.AdditionalInformation.Layers.Values.OrderBy(layer => layer.Key).ToList();

            if (layers.Count == 0)
                return answered("The file has no layers yet. Draw on one by its numbers, like 68/20, and it is added.");

            var cell = session.CellIfAny();
            var counts = new Dictionary<LayerKey, int>();

            if (cell is not null)
            {
                foreach (var element in cell.Elements)
                {
                    if (ElementRecords.LayerOf(element) is LayerKey key)
                    {
                        counts.TryGetValue(key, out int already);
                        counts[key] = already + 1;
                    }
                }
            }

            var said = new StringBuilder($"{layers.Count} layers");

            if (cell is not null)
                said.Append($", with the shapes each has in {Hierarchy.NameOf(cell)}");

            said.Append(':');

            foreach (var layer in layers)
            {
                said.Append('\n').Append(layer.DisplayName);

                if (layer.Role != LayerRole.None)
                    said.Append(", ").Append(layer.Role.ToString().ToLowerInvariant());

                if (cell is null)
                    continue;

                if (counts.TryGetValue(layer.Key, out int count))
                    said.Append(", ").Append(shapes(count));
                else
                    said.Append(", none");
            }

            if (layers.All(layer => string.IsNullOrWhiteSpace(layer.Name)))
                said.Append("\nNo layer has a name, so no layermap is loaded: name layers by their numbers.");

            return answered(said.ToString());
        }

        private static AgentResult listCells(AgentArguments arguments, AgentSession session)
        {
            var rows = Hierarchy.Tree(session.Gds);

            if (rows.Count == 0)
                return answered("The library has no cells. Make one with make_cell.");

            string editing = "";

            if (session.CellIfAny() is StructureModel cell)
                editing = Hierarchy.NameOf(cell);

            var said = new StringBuilder($"{Hierarchy.Names(session.Gds).Count} cells, outermost first:");

            foreach (var row in rows.Take(MostCellRows))
            {
                var summary = row.Cell;

                said.Append('\n').Append(new string(' ', row.Depth * 2)).Append(summary.Name);

                if (row.Repeats)
                {
                    said.Append(" (again - see above)");

                    continue;
                }

                if (summary.IsTop)
                    said.Append(": top cell");
                else if (summary.PlacedBy == 1)
                    said.Append(": placed once");
                else
                    said.Append($": placed {summary.PlacedBy} times");

                said.Append($", {shapes(summary.Elements - summary.Places)}");

                if (summary.Places > 0)
                    said.Append($", {summary.Places} placements");

                if (summary.Name == editing)
                    said.Append(" - being worked in");
            }

            if (rows.Count > MostCellRows)
                said.Append($"\nand {rows.Count - MostCellRows} more rows.");

            return answered(said.ToString());
        }

        ///
        ///A cell by the name a model gave it: the name as written first, and then the one cell it can only mean -
        ///a model is often loose about case. Refused with the names there are.
        ///
        private static StructureModel cellCalled(GDS gds, string asked)
        {
            string wanted = asked.Trim();

            if (Hierarchy.Named(gds, wanted) is StructureModel exact)
                return exact;

            var names = Hierarchy.Names(gds);
            var alike = names.Where(name => string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)).ToList();

            if (alike.Count == 1 && Hierarchy.Named(gds, alike[0]) is StructureModel near)
                return near;

            if (names.Count == 0)
                throw new AgentProblem($"There is no cell called \"{wanted}\" - the library has no cells at all.");

            string more = "";

            if (names.Count > AgentSession.MostListed)
                more = $" and {names.Count - AgentSession.MostListed} more";

            throw new AgentProblem($"There is no cell called \"{wanted}\". The cells are {string.Join(", ", names.Take(AgentSession.MostListed))}{more}.");
        }

        private static AgentResult openCell(AgentArguments arguments, AgentSession session)
        {
            var gds = session.Gds;
            var structure = cellCalled(gds, arguments.Text("name"));

            string name = Hierarchy.NameOf(structure);

            session.Cell = name;

            int places = Hierarchy.Places(structure).Count;
            int placed = Hierarchy.PlacementsOf(gds, name);
            var flattened = GdsFlattener.Flatten(gds, name, session.MostElements);
            var box = Measure.BoundsOf(flattened);

            var said = new StringBuilder($"Now working in {name}: {shapes(structure.Elements.Count - places)}");

            if (places > 0)
                said.Append($" and {places} placements of other cells");

            if (!box.IsEmpty)
                said.Append($", covering {session.Span(box)}");

            if (!box.IsEmpty && flattened.Stopped)
                said.Append(AgentSession.CutShort(flattened.Elements.Count));

            said.Append('.');

            if (placed == 0)
                said.Append(" Nothing places it: it is a top cell.");
            else if (placed == 1)
                said.Append(" It is placed once, and a change to it shows there.");
            else
                said.Append($" It is placed {placed} times, and a change to it shows at every one.");

            return answered(said.ToString());
        }

        ///<summary>What kind of shape an element is, in the words find_shapes takes.</summary>
        private static string kindOf(ElementModel model)
        {
            var element = model.Element;

            if (element is SrefModel || element is ArefModel)
                return "placement";

            if (element is TextModel)
                return "label";

            if (element is PathModel)
                return "path";

            if (element is NodeModel)
                return "node";

            if (AgentSession.IsRectangle(AgentSession.CornersOf(model)))
                return "rectangle";

            return "polygon";
        }

        private static AgentResult findShapes(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            string name = Hierarchy.NameOf(cell);

            LayerKey? only = null;

            if (arguments.OptionalText("layer") is string layerName)
                only = session.LayerNamed(layerName);

            string kind = arguments.OptionalText("kind") ?? "any";

            Bounds? within = null;

            if (arguments.OptionalBox("area") is (double left, double bottom, double right, double top))
            {
                within = new Bounds(
                    session.Units(left, "The area's left edge"),
                    session.Units(bottom, "The area's bottom edge"),
                    session.Units(right, "The area's right edge"),
                    session.Units(top, "The area's top edge"));
            }

            int limit = arguments.OptionalInteger("limit") ?? 50;

            if (limit < 1 || limit > 500)
                throw new AgentProblem($"\"limit\" has to be from 1 to 500, and is {limit}.");

            var lines = new List<string>();
            int matched = 0;

            foreach (var element in cell.Elements)
            {
                if (only is LayerKey wanted && ElementRecords.LayerOf(element) != wanted)
                    continue;

                if (kind != "any" && kindOf(element) != kind)
                    continue;

                if (within is Bounds box && !session.BoxOf(element).Intersects(box))
                    continue;

                matched++;

                if (lines.Count < limit)
                    lines.Add(session.Line(element, cell));
            }

            if (matched == 0)
            {
                if (cell.Elements.Count == 0)
                    return answered($"{name} is empty.");

                return answered($"None of the {shapes(cell.Elements.Count)} in {name} match.");
            }

            var said = new StringBuilder($"{matched} of the {shapes(cell.Elements.Count)} in {name} match");

            if (matched > lines.Count)
                said.Append($"; the first {lines.Count}");

            said.Append(':');

            foreach (string line in lines)
                said.Append('\n').Append(line);

            return answered(said.ToString());
        }

        private static AgentResult measure(AgentArguments arguments, AgentSession session)
        {
            var found = session.Shapes(arguments.Shapes("shapes"));

            if (found.Count > MostMeasured)
                throw new AgentProblem($"measure takes up to {MostMeasured} shapes at once, and was given {found.Count}.");

            double square = session.MicronsPerUnit * session.MicronsPerUnit;
            var said = new StringBuilder();

            foreach (var shape in found)
            {
                said.Append(session.Line(shape.Model, shape.Cell));

                if (GdsFlattener.OutlineOf(shape.Model) is List<Element.Point> outline && outline.Count >= 3)
                    said.Append($"; area {session.Plain(Math.Abs(Measure.AreaOf(outline)) * square)} µm²");

                var box = session.BoxOf(shape.Model);

                if (!box.IsEmpty)
                    said.Append($"; {session.Microns(box.Width)} by {session.Microns(box.Height)} µm across");

                said.Append('\n');
            }

            for (int i = 0; i < found.Count; i++)
            {
                for (int j = i + 1; j < found.Count; j++)
                {
                    said.Append($"#{found[i].Id} and #{found[j].Id}: ");
                    said.Append(apart(session, found[i].Model, found[j].Model));
                    said.Append('\n');
                }
            }

            return answered(said.ToString().TrimEnd());
        }

        ///
        ///How far apart two shapes are at their closest, as a phrase.
        ///
        ///Edge to edge rather than box to box - two L shapes can have boxes that overlap and edges a micron apart.
        ///A label is its anchor point and a placement is its box, since neither has an outline of its own.
        ///
        private static string apart(AgentSession session, ElementModel one, ElementModel other)
        {
            var first = AgentGeometry.OutlineOrBox(session, one);
            var second = AgentGeometry.OutlineOrBox(session, other);

            if (first.Count == 0 || second.Count == 0)
                return "one of them has nothing to measure from";

            if (first.Count >= 3 && second.Count >= 3)
            {
                var shared = Booleans.Combine(new[] { first }, new[] { second }, BooleanOperation.And);

                if (shared.Any(ring => Math.Abs(Measure.AreaOf(ring)) > 0))
                    return "they overlap";
            }

            double closest = AgentGeometry.Closest(first, second);

            if (closest == 0)
                return "they touch";

            return $"{session.Microns(closest)} µm apart at the closest";
        }

        private static AgentResult checkRules(AgentArguments arguments, AgentSession session)
        {
            if (session.Deck is not DrcDeck deck)
                throw new AgentProblem($"No rule deck is loaded, so there is nothing to check against. {session.HowToLoadRules}".TrimEnd());

            bool everything = arguments.Flag("everything", false);

            FlattenedLayout layout;
            string over;

            if (everything)
            {
                layout = GdsFlattener.Flatten(session.Gds, session.MostElements);
                over = "the whole layout";
            }
            else
            {
                var cell = session.CellToEdit();

                layout = GdsFlattener.Flatten(session.Gds, Hierarchy.NameOf(cell), session.MostElements);
                over = Hierarchy.NameOf(cell);
            }

            var off = new HashSet<string>(StringComparer.Ordinal);
            Bounds? within = null;
            FlattenedLayout? cutFrom = null;

            if (arguments.OptionalBox("area") is (double left, double bottom, double right, double top))
            {
                var box = new Bounds(
                    session.Units(left, "The area's left edge"),
                    session.Units(bottom, "The area's bottom edge"),
                    session.Units(right, "The area's right edge"),
                    session.Units(top, "The area's top edge"));

                within = box;

                //A window of a layout is not a layout, as far as a density or a net is concerned.
                foreach (var rule in deck.Rules)
                {
                    if (rule.Check == DrcCheck.Density || rule.Check == DrcCheck.Antenna)
                        off.Add(rule.Id);
                }

                long reach = AgentSession.ReachOf(deck);

                var picked = new FlattenedLayout();

                foreach (int at in layout.Index.Within(box.Grown((int)Math.Min(AgentSession.Reach, (reach * 2) + 1))))
                    picked.Elements.Add(layout.Elements[at]);

                cutFrom = layout;
                layout = picked;
                over += $" within {session.Span(box)}";
            }

            //And what the person has switched off in the host's own rules panel, which is not checked there either.
            var skipped = new HashSet<string>(session.RulesOff, StringComparer.Ordinal);

            skipped.UnionWith(off);

            var result = Drc.Check(deck, layout, skipped);
            var violations = result.Violations;

            //A window of a layout that stopped flattening, or cut a loop short, is a window of part of it - which the
            //flatten it was cut from says, and the window cannot.
            if (cutFrom is not null)
                result.NotChecked.AddRange(Drc.NotCheckedOf(cutFrom));

            if (within is Bounds area)
                violations = violations.Where(violation => violation.Bounds.Intersects(area)).ToList();

            //Looked at and let stand there, so not counted here either - see AgentSession.Waived.
            int waived = violations.Count(violation => session.Waived.Contains(violation.Key));

            if (waived > 0)
                violations = violations.Where(violation => !session.Waived.Contains(violation.Key)).ToList();

            int checkedRules = deck.Rules.Count(rule => !skipped.Contains(rule.Id));
            int switchedOff = deck.Rules.Count(rule => session.RulesOff.Contains(rule.Id));

            var said = new StringBuilder($"Checked {counted(checkedRules, "rule")} over {over}");

            if (violations.Count == 0)
            {
                said.Append(": nothing broken.");
            }
            else
            {
                var byRule = DrcResult.ByRule(violations);

                said.Append($": {counted(violations.Count, "violation")} of {counted(byRule.Count, "rule")}.");

                foreach (var rule in byRule.Take(25))
                {
                    var first = rule.First();
                    int count = rule.Count();

                    said.Append('\n').Append(session.RuleText(first.RuleId, first.Check, first.Limit, first.Description));

                    if (count == 1)
                        said.Append(", once: ");
                    else
                        said.Append($", {count} times: ");
                    said.Append(string.Join("; ", rule.Take(3).Select(violation => session.Span(violation.Bounds))));

                    if (count > 3)
                        said.Append("; ...");
                }

                if (byRule.Count > 25)
                    said.Append($"\nand {byRule.Count - 25} more rules.");
            }

            foreach (string missing in result.NotChecked)
                said.Append("\nNot checked: ").Append(missing);

            foreach (string notRun in result.NotRun.Take(10))
                said.Append("\nNot run: ").Append(notRun);

            foreach (string problem in result.Problems.Take(5))
                said.Append("\nDeck problem: ").Append(problem);

            if (off.Count > 0)
                said.Append($"\nDensity and antenna rules ({off.Count}) are not checked inside an area.");

            if (waived > 0)
                said.Append($"\nLeft out: {counted(waived, "violation")} the person has waived, looked at and accepted.");

            if (switchedOff > 0)
                said.Append($"\nLeft out: {counted(switchedOff, "rule")} the person has switched off.");

            return answered(said.ToString());
        }
    }
}
