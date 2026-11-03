using System.Globalization;
using System.Text;

namespace GdsII
{
    public static partial class AgentTools
    {
        public const string ListStructuresName = "list_structures";

        public const string PlaceStructureName = "place_structure";

        ///
        ///**The Generator's structures, as tools** (2026-11-01, asked for): what the 2D view's Draw menu makes from a few
        ///numbers - see <see cref="Marks"/> - put down whole from the same numbers, where an assistant otherwise drew an
        ///inductor or a coupler shape by shape. One tool lists them and says what each number means; the other puts one
        ///down, on one layer and round one point, the way the panel's Place does.
        ///
        private static void addStructureList(List<AgentTool> tools)
        {
            tools.Add(new AgentTool(
                ListStructuresName,
                "Lists the structures place_structure puts down whole from a few numbers - what the app's Generator makes. "
                + "Without kind, every kind by its key, in groups, with the names of its numbers. With kind, what that one is "
                + "and each of its numbers: what it means, its unit, and what it is when not given.",
                false,
                new[]
                {
                    oneOf("kind", false, "One kind, by its key, for what it is and what its numbers mean.", structureKeys())
                },
                listStructures));
        }

        private static void addStructurePlacing(List<AgentTool> tools)
        {
            tools.Add(new AgentTool(
                PlaceStructureName,
                "Puts down a structure made from its numbers, as the app's Generator does: a die frame, alignment marks and "
                + "test structures, spiral and loop inductors, capacitors, resistors, guard rings, couplers, filters, "
                + "resonators, antennas, a clock tree, power rails, pads, fill, and photonic and microfluidic structures - "
                + "list_structures says which there are and what each takes. On one layer, its middle at a point, as one step; "
                + "use it rather than drawing one of these shape by shape. A structure on more than one layer is a kind for "
                + "each layer, made from the same numbers and put down at the same point: a spiral inductor, then its "
                + "underpass on the metal below and its vias on the via layer between.",
                true,
                new[]
                {
                    oneOf("kind", true, "Which structure, by its key from list_structures.", structureKeys()),
                    layer("The layer to put it on, by name or numbers."),
                    needs("at", AgentValue.Point, "Where its middle goes, [x, y] in microns: the middle of the box round it, or for a kind put down with another, that one's middle."),
                    may("numbers", AgentValue.Text, "Its numbers, as name=value pairs - like \"radius=700, ring=11\" - in microns, degrees or a count, as list_structures says. Each one left out is the kind's own."),
                    oneOf("turn", false, "Degrees counterclockwise, about its middle. 0 when not given.", "0", "90", "180", "270"),
                    may("mirror", AgentValue.Flag, "Mirror it top to bottom, before turning it.")
                },
                placeStructure));
        }

        private static string[] structureKeys()
        {
            return Marks.All.Select(maker => maker.Key).ToArray();
        }

        private static AgentResult listStructures(AgentArguments arguments, AgentSession session)
        {
            if (arguments.OptionalText("kind") is string key && Marks.Named(key) is MarkMaker maker)
                return answered(structureSaid(maker));

            var said = new StringBuilder("The structures place_structure puts down, by key, with the names of their numbers. list_structures with a kind says what one is and what each of its numbers means.");

            foreach (var group in Marks.All.GroupBy(each => each.Group))
            {
                said.Append('\n').Append(group.Key).Append(": ");
                said.Append(string.Join("; ", group.Select(each => $"{each.Key} - {each.Name} ({string.Join(", ", each.Settings.Select(setting => setting.Name))})")));
            }

            return answered(said.ToString());
        }

        ///<summary>One kind in full: what it is, each number with its meaning, unit and default, and what goes down with it.</summary>
        private static string structureSaid(MarkMaker maker)
        {
            var said = new StringBuilder($"{maker.Key} - {maker.Name}, under {maker.Group}. {maker.Says}.");

            said.Append("\nIts numbers:");

            foreach (var setting in maker.Settings)
                said.Append($"\n- {setting.Name}: {setting.Says}. {kindOf(setting)}, {valueSaid(setting, setting.Start)} when not given.");

            if (familyOf(maker) is string family)
                said.Append($"\nMade from the same numbers and put down at the same point, each on its own layer: {family}.");

            string note = maker.NoteFor(new Dictionary<string, double>());

            if (note.Length > 0)
                said.Append($"\nFrom its own numbers: {note}.");

            return said.ToString();
        }

        private static AgentResult placeStructure(AgentArguments arguments, AgentSession session)
        {
            var cell = session.CellToEdit();
            var onLayer = session.LayerNamed(arguments.Text("layer"));
            var maker = Marks.Named(arguments.Text("kind")) ?? throw new AgentProblem("There is no such structure. list_structures names them.");
            var at = session.At(arguments.Point("at"), "at");
            var numbers = numbersFor(maker, arguments.Pairs("numbers"));

            //Every length at least a unit of this file, said as the number it is, before the structure is worked out from it.
            foreach (var setting in maker.Settings)
            {
                if (setting.Whole || setting.Angle)
                    continue;

                if (!numbers.TryGetValue(setting.Name, out double length))
                    length = setting.Start;

                session.Length(length, $"The {setting.Name}");
            }

            var made = maker.Make(numbers, microns => session.Units(microns, "A length"));

            if (made.Problem is string problem)
                throw new AgentProblem($"{problem} Nothing was put down.");

            int quarters = int.Parse(arguments.OptionalText("turn") ?? "0", CultureInfo.InvariantCulture) / 90;
            bool mirrored = arguments.Flag("mirror", false);
            var outlines = new List<List<Element.Point>>();
            var worked = new List<Element.Point>();

            foreach (var outline in made.Outlines)
            {
                var placed = new List<Element.Point>(outline.Count);

                foreach (var corner in outline)
                {
                    long x = corner.X;
                    long y = corner.Y;

                    if (mirrored)
                        y = -y;

                    for (int each = 0; each < quarters; each++)
                        (x, y) = (-y, x);

                    x += at.X;
                    y += at.Y;

                    if (Math.Abs(x) > AgentSession.Reach || Math.Abs(y) > AgentSession.Reach)
                        throw new AgentProblem($"Put down at {session.Point(at)}, it runs past the furthest a layout reaches, {session.Microns(AgentSession.Reach)} µm either way of the origin. Nothing was put down.");

                    placed.Add(new Element.Point((int)x, (int)y));
                }

                worked.AddRange(placed);
                outlines.Add(boundary(session, onGrid(session, placed, onLayer, "make it larger."), gridOf(session, onLayer), null));
            }

            string from = string.Join(", ", maker.Settings.Select(setting => $"{setting.Name} {valueSaid(setting, valueOf(numbers, setting))}"));
            var lies = new List<string>();

            if (mirrored)
                lies.Add("mirrored top to bottom");

            if (quarters > 0)
                lies.Add($"turned {quarters * 90}° counterclockwise");

            string how = "";

            if (lies.Count > 0)
                how = $", {string.Join(" and ", lies)}";

            string note = maker.NoteFor(numbers);

            if (note.Length > 0)
                note = $" {note}.";

            string rest = "";

            if (familyOf(maker) is string family)
                rest = $" From the same numbers and the same point, each on its own layer: {family}.";

            string onTheGrid = gridNote(session, onLayer, worked, "Its worked-out corners are");

            return drawOutlines(
                session,
                cell,
                onLayer,
                outlines,
                $"AI place {lowered(maker.Name)}",
                drawn => $"Put down the {lowered(maker.Name)} on {session.LayerName(onLayer)} in {Hierarchy.NameOf(cell)}, its middle at {session.Point(at)}{how}, "
                    + $"from {from}, as {shapes(drawn.Count)}: {numbered(drawn)}.{note}{onTheGrid}{rest}");
        }

        ///
        ///The numbers given, by the names the kind gives them - whatever their case, and with an underscore for a space,
        ///since "wide_run" is how a model writes "wide run". One the kind does not have is refused with the ones it has.
        ///
        private static Dictionary<string, double> numbersFor(MarkMaker maker, Dictionary<string, double> given)
        {
            var numbers = new Dictionary<string, double>();

            foreach (var (asked, value) in given)
            {
                string name = asked.Replace('_', ' ').Trim();
                var setting = maker.Settings.FirstOrDefault(each => string.Equals(each.Name, name, StringComparison.OrdinalIgnoreCase));

                if (setting is null)
                    throw new AgentProblem($"The {lowered(maker.Name)} has no number called {asked}. Its numbers are {string.Join(", ", maker.Settings.Select(each => each.Name))}.");

                numbers[setting.Name] = value;
            }

            return numbers;
        }

        private static double valueOf(Dictionary<string, double> numbers, MarkSetting setting)
        {
            if (numbers.TryGetValue(setting.Name, out double value))
                return value;

            return setting.Start;
        }

        ///<summary>A number with its unit: "700 µm", "60°", "3".</summary>
        private static string valueSaid(MarkSetting setting, double value)
        {
            string number = AgentSession.Given(value);

            if (setting.Whole)
                return number;

            if (setting.Angle)
                return number + "°";

            return number + " µm";
        }

        private static string kindOf(MarkSetting setting)
        {
            if (setting.Whole)
                return "A count";

            if (setting.Angle)
                return "In degrees";

            return "In microns";
        }

        ///<summary>The other kinds made from the same numbers, by key, or null when it shares them with none.</summary>
        private static string? familyOf(MarkMaker maker)
        {
            var others = Marks.All.Where(each => each.Family == maker.Family && each.Key != maker.Key).ToList();

            if (others.Count == 0)
                return null;

            return string.Join(", ", others.Select(each => $"{each.Key} ({lowered(each.Name)})"));
        }

        ///<summary>A kind's name inside a sentence: "rat-race coupler", but "CPW resonator" and "clock H-tree" as they are.</summary>
        private static string lowered(string name)
        {
            if (name.Length > 1 && char.IsUpper(name[1]))
                return name;

            return char.ToLowerInvariant(name[0]) + name[1..];
        }
    }
}
