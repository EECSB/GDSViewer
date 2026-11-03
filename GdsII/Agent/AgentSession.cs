using System.Globalization;
using System.Text;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>A shape a call named, found: its number, the cell holding it now, and the element itself.</summary>
    internal sealed record AgentShape(int Id, StructureModel Cell, ElementModel Model);

    ///
    ///What an assistant's tools work on: one open library, the undo history its changes go onto, and the cell
    ///new shapes go into.
    ///
    ///**One of these per open file and per conversation.** It is what gives a shape a number the model can
    ///say back - `#12` - and a number only means something to the session that handed it out. Opening another
    ///file is a new session.
    ///
    ///**Sizes in microns and layers by name**, because that is how a person asks and how a PDK is written: "a
    ///2 µm met1 strap", not "2000 units on 68/20". Everything is turned into database units and layer pairs
    ///here and nowhere else, so every tool rounds the same way and refuses the same names in the same words.
    ///
    public sealed class AgentSession
    {
        public AgentSession(GDS gds, EditHistory history)
        {
            Gds = gds;
            History = history;
        }

        public GDS Gds { get; }

        ///
        ///The history every change goes onto - the app's own, so a change the assistant makes is one press of
        ///the same undo button as one made by hand, and sits among them in the order they happened.
        ///
        public EditHistory History { get; }

        ///
        ///The cell new shapes go into, by name.
        ///
        ///Null until something says, and then the library's top cell is taken when it has exactly one. With
        ///several, a drawing tool refuses and names them - guessing which of two chips a shape was meant for is
        ///not a guess worth making on somebody's file.
        ///
        ///**Followed through a rename.** The name read back is the cell's own, whatever it has been called since
        ///- by rename_cell, by the person, or by an undo of either - so renaming the cell being worked in does not
        ///leave the session pointing at a name nothing has.
        ///
        public string? Cell
        {
            get
            {
                if (workingIn is not null && Gds.StreamFormat.Structures.Contains(workingIn))
                    return Hierarchy.NameOf(workingIn);

                return cell;
            }
            set
            {
                cell = value;
                workingIn = null;

                if (value is not null)
                    workingIn = Hierarchy.Named(Gds, value);
            }
        }

        private string? cell;

        ///<summary>The cell <see cref="Cell"/> names, once it has been found - the cell itself, which a rename does not change.</summary>
        private StructureModel? workingIn;

        ///<summary>The rule deck changes are checked against, or null when none is loaded.</summary>
        public DrcDeck? Deck { get; set; }

        ///
        ///Rules the person has switched off, by id: left out of every check here, as the host's own check
        ///leaves them out. A model told what a rule nobody is checking says would set about fixing it.
        ///
        public IReadOnlySet<string> RulesOff { get; set; } = new HashSet<string>();

        ///
        ///Faults the person has looked at and let stand, by <see cref="DrcViolation.Key"/>: left out of what the
        ///rules are said to have found, as the host's own count leaves them out.
        ///
        ///**The host's to say**, like the deck - the app's rules panel waives them. The keys are in the
        ///coordinates of the host's check, which is over the whole layout, so they match a check here over the
        ///top cell, which is the one nearly everything is drawn in.
        ///
        public IReadOnlySet<string> Waived { get; set; } = new HashSet<string>();

        ///
        ///Whether a change is followed by a check of the rules around it, when there is a deck.
        ///
        ///On unless a host turns it off. It costs two flattens of the cell per change, which is nothing on a
        ///cell and something on a whole chip - see <see cref="RulesAround"/>.
        ///
        public bool CheckAfterEdits { get; set; } = true;

        ///
        ///What check_rules adds when there is no deck: where this host's person or model gets one.
        ///
        ///**The host's to say**, because only the host knows: the app has a rules panel, `gds mcp` a tool that
        ///loads a deck from its folder, and a test has neither. Empty says only that none is loaded.
        ///
        public string HowToLoadRules { get; set; } = "";

        ///
        ///How many elements a flatten for these tools goes to before it stops: a cell's extent, a rule check, the check
        ///after a change.
        ///
        ///**The host's to say**, like the deck. <see cref="GdsFlattener.MostElements"/> is set by what a browser tab's
        ///memory holds, which is where the app's assistant runs; `gds mcp` runs on a desktop and gives the command
        ///line's ceiling instead. A flatten that stops all the same is said by whatever answered from it.
        ///
        public int MostElements { get; set; } = GdsFlattener.MostElements;

        ///
        ///What follows an extent worked out from a flatten that stopped - " as far as its first 1,500,000 elements go,
        ///where flattening it stopped" - so the part of a cell a flatten got to is not given as all of it.
        ///
        internal static string CutShort(int reached)
        {
            return FormattableString.Invariant($" as far as its first {reached:N0} elements go, where flattening it stopped");
        }

        #region Shape numbers ***************************************************************

        private readonly Dictionary<ElementModel, int> numbers = new Dictionary<ElementModel, int>(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<int, (ElementModel Model, StructureModel? Cell)> numbered = new Dictionary<int, (ElementModel, StructureModel?)>();
        private int lastNumber;

        ///
        ///The number a shape goes by in this conversation, given the first time it is asked for.
        ///
        ///**Its own for good.** The number is tied to the element itself rather than to where it sits in its
        ///cell, so it survives every other shape being added or deleted around it - and an undo puts the very
        ///element back (see <see cref="DeleteElement"/>), so it survives that too. A shape that is gone keeps
        ///its number unused, rather than handing it to something else that would then be edited by mistake.
        ///
        ///Public so a host can say what is selected - "the selection is #3 and #7" - in the same numbers the
        ///tools answer in.
        ///
        public int IdOf(ElementModel model)
        {
            if (numbers.TryGetValue(model, out int id))
                return id;

            return IdOf(model, cellHolding(model, null));
        }

        ///<summary>The same, for a caller that already knows which cell holds it - which saves a walk over the library.</summary>
        internal int IdOf(ElementModel model, StructureModel? cell)
        {
            if (numbers.TryGetValue(model, out int id))
                return id;

            id = ++lastNumber;

            numbers[model] = id;
            numbered[id] = (model, cell);

            return id;
        }

        ///<summary>The element a number was given to, or null when it was never given or the shape has gone.</summary>
        public ElementModel? ShapeNumbered(int id)
        {
            return find(id)?.Model;
        }

        private AgentShape? find(int id)
        {
            if (!numbered.TryGetValue(id, out var known))
                return null;

            if (cellHolding(known.Model, known.Cell) is not StructureModel cell)
                return null;

            return new AgentShape(id, cell, known.Model);
        }

        ///
        ///The cell an element is in now, asking the one it was last seen in first.
        ///
        ///Elements do not move between cells - making a cell copies their records into a new one and deletes
        ///these - so the first guess is nearly always right, and the walk over the whole library is for a shape
        ///that has been deleted.
        ///
        private StructureModel? cellHolding(ElementModel model, StructureModel? guess)
        {
            if (guess is not null && holds(guess, model) && Gds.StreamFormat.Structures.Contains(guess))
                return guess;

            foreach (var structure in Gds.StreamFormat.Structures)
            {
                if (holds(structure, model))
                    return structure;
            }

            return null;
        }

        private static bool holds(StructureModel structure, ElementModel model)
        {
            foreach (var element in structure.Elements)
            {
                if (ReferenceEquals(element, model))
                    return true;
            }

            return false;
        }

        ///
        ///The shapes a call named, found - all in one cell, or refused.
        ///
        ///**One cell per call**, because an edit happens in one cell's coordinates and a move of 1 µm means
        ///something different in a cell placed turned. Shapes from two cells in one call is almost always a
        ///model that has lost track of which cell it is in, and saying so is worth more than guessing.
        ///
        internal List<AgentShape> Shapes(IReadOnlyList<int> ids)
        {
            var found = new List<AgentShape>();

            foreach (int id in ids)
            {
                if (!numbered.ContainsKey(id))
                    throw new AgentProblem($"There is no shape #{id}. Shape numbers come from find_shapes and from what each drawing tool answers.");

                if (find(id) is not AgentShape shape)
                    throw new AgentProblem($"Shape #{id} is not in the layout any more - it was deleted or replaced.");

                found.Add(shape);
            }

            foreach (var shape in found)
            {
                if (!ReferenceEquals(shape.Cell, found[0].Cell))
                {
                    throw new AgentProblem(
                        $"#{found[0].Id} is in {Hierarchy.NameOf(found[0].Cell)} and #{shape.Id} is in {Hierarchy.NameOf(shape.Cell)}. One call works in one cell.");
                }
            }

            return found;
        }

        #endregion **************************************************************************



        #region Units ***********************************************************************

        ///
        ///How many microns one database unit is, from the file's UNITS record.
        ///
        ///A micron when the file gives nothing usable, which is what the 2D view assumes too - so a model and the
        ///ruler on screen agree about how long something is even on a file whose units are broken.
        ///
        public double MicronsPerUnit
        {
            get
            {
                return Gds.MicronsPerDatabaseUnit ?? 1;
            }
        }

        ///
        ///How far from the origin anything may be put, in database units.
        ///
        ///A quarter of what a coordinate can hold rather than all of it, so that a box's width, a shape grown by
        ///a rule distance or two coordinates added together all stay in range - the same margin the 2D view
        ///keeps for what is typed into it.
        ///
        internal const int Reach = int.MaxValue / 4;

        ///<summary>A coordinate or a signed distance, in microns, as database units.</summary>
        internal int Units(double microns, string what)
        {
            double units = Math.Round(microns / MicronsPerUnit, MidpointRounding.AwayFromZero);

            if (!double.IsFinite(units) || Math.Abs(units) > Reach)
                throw new AgentProblem($"{what} is {Given(microns)} µm, further than a layout reaches: {Microns(Reach)} µm either way of the origin.");

            return (int)units;
        }

        ///<summary>A size, in microns, which has to come out at least one database unit.</summary>
        internal int Length(double microns, string what)
        {
            if (microns <= 0)
                throw new AgentProblem($"{what} has to be more than zero, and is {Given(microns)} µm.");

            int units = Units(microns, what);

            if (units < 1)
                throw new AgentProblem($"{what} of {Given(microns)} µm is less than one database unit, which is {Microns(1)} µm in this file.");

            return units;
        }

        ///
        ///A number the model sent, quoted back as it was sent rather than on the file's grid - a size refused for
        ///being under one unit would otherwise be quoted as 0, which is not what was asked for. Public for `gds
        ///mcp`, which quotes microns the same way in its own answers.
        ///
        public static string Given(double microns)
        {
            return microns.ToString("0.#########", CultureInfo.InvariantCulture);
        }

        internal Element.Point At((double X, double Y) microns, string what)
        {
            return new Element.Point(Units(microns.X, what), Units(microns.Y, what));
        }

        ///<summary>A number of database units in microns, to as many places as the file's grid has.</summary>
        internal string Microns(double units)
        {
            return Plain(units * MicronsPerUnit);
        }

        ///<summary>A point in database units, in microns: "(1.5, -0.2)".</summary>
        internal string Point(double x, double y)
        {
            return $"({Microns(x)}, {Microns(y)})";
        }

        internal string Point(Element.Point point)
        {
            return Point(point.X, point.Y);
        }

        ///<summary>A box in database units, as its two corners in microns.</summary>
        internal string Span(Bounds box)
        {
            return $"{Point(box.Left, box.Bottom)} to {Point(box.Right, box.Top)}";
        }

        ///
        ///A number of microns written plainly: invariant, to as many places as one database unit needs and no
        ///more, and never "-0".
        ///
        ///**The file's grid decides the places.** A nanometer grid needs three, and a fourth would be a digit
        ///no coordinate in the file can have; a file on a tenth-of-a-nanometer grid needs four, and three would
        ///round two different corners to the same number.
        ///
        internal string Plain(double microns)
        {
            int places = (int)Math.Clamp(Math.Ceiling(-Math.Log10(MicronsPerUnit) - 1e-9), 0, 6);

            string format = "0";

            if (places > 0)
                format = "0." + new string('#', places);

            string written = microns.ToString(format, CultureInfo.InvariantCulture);

            if (written == "-0")
                return "0";

            return written;
        }

        #endregion **************************************************************************



        #region The manufacturing grid ******************************************************

        ///
        ///The grid a layer's corners go on, in database units: what the deck's off-grid rules ask of that layer,
        ///or one with no deck or no such rule - see <see cref="DrcDeck.GridFor"/>.
        ///
        ///**For the corners a tool works out, not the ones it is given.** A circle's corners, a bend's, the strokes
        ///of lettering: the model chose none of those numbers and cannot put them on a grid, so the tool that makes
        ///them does. A rectangle's corners are the model's own and go where it said - the check after the change
        ///says when they are off, and a corner moved without asking is one it would look for where it put it.
        ///
        internal int GridFor(LayerKey layer)
        {
            if (Deck is null)
                return 1;

            return Deck.GridFor(layer);
        }

        ///<summary>The grid a layer's corners went on, the way an answer says it: "the deck's 0.005 µm grid (grid.1)".</summary>
        internal string GridSaid(LayerKey layer)
        {
            var rules = new List<string>();

            if (Deck is not null)
                rules = Deck.GridRulesFor(layer).Select(rule => rule.Id).ToList();

            return $"the deck's {Microns(GridFor(layer))} µm grid ({string.Join(" and ", rules)})";
        }

        #endregion **************************************************************************



        #region Layers **********************************************************************

        ///
        ///A layer as somebody would name it: by its name from the layermap, or by its numbers.
        ///
        ///"met1", "68/20", "68" when the file has only one datatype on 68, and "met1 (68/20)" - which is how
        ///every answer here writes a layer, so a model quoting one back is understood.
        ///
        ///**A name the file does not know is refused, with the names it does.** Numbers are the way to a layer
        ///the file has nothing on yet, which is a real thing to want and a deliberate thing to ask for - a typo
        ///in a name should not quietly start a new layer.
        ///
        internal LayerKey LayerNamed(string asked)
        {
            string wanted = asked.Trim();

            if (wanted.Length == 0)
                throw new AgentProblem("Say which layer: by its name, like met1, or by its numbers, like 68/20.");

            int open = wanted.LastIndexOf('(');

            if (open > 0 && wanted.EndsWith(')'))
                wanted = wanted.Substring(open + 1, wanted.Length - open - 2).Trim();

            if (layerByNumbers(wanted) is LayerKey key)
                return key;

            var matches = new List<LayerKey>();

            foreach (var layer in Gds.AdditionalInformation.Layers.Values)
            {
                if (layer.Name is string name && string.Equals(name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    matches.Add(layer.Key);
            }

            matches.Sort();

            if (matches.Count == 1)
                return matches[0];

            if (matches.Count > 1)
                throw new AgentProblem($"\"{asked}\" names {matches.Count} layers: {string.Join(", ", matches.Select(LayerName))}. Say which by its numbers.");

            throw new AgentProblem(unknownLayer(asked, wanted));
        }

        ///<summary>"68/20", or "68" when the file uses one datatype on it; null for anything that is not numbers.</summary>
        private LayerKey? layerByNumbers(string text)
        {
            //A pair by the one grammar for it - see LayerKey.TryParse, which also reads the "68/?" list_layers
            //prints for shapes whose data type could not be read, and so names them the way they were listed.
            if (text.Contains('/'))
            {
                if (LayerKey.TryParse(text, out LayerKey pair))
                    return pair;

                return null;
            }

            if (!short.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out short number))
                return null;

            var onIt = Gds.AdditionalInformation.Layers.Keys.Where(key => key.Number == number).OrderBy(key => key.DataType).ToList();

            if (onIt.Count == 0)
                return new LayerKey(number, 0);

            if (onIt.Count == 1)
                return onIt[0];

            throw new AgentProblem($"Layer {number} has {onIt.Count} datatypes in this file: {string.Join(", ", onIt.Select(LayerName))}. Say which, like {onIt[0]}.");
        }

        private string unknownLayer(string asked, string wanted)
        {
            var named = Gds.AdditionalInformation.Layers.Values
                .Where(layer => !string.IsNullOrWhiteSpace(layer.Name))
                .OrderBy(layer => layer.Key)
                .ToList();

            var said = new StringBuilder($"There is no layer called \"{asked}\" in this file.");

            var near = named
                .Where(layer => layer.Name!.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    || wanted.Contains(layer.Name!.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            if (near.Count > 0)
                said.Append($" Did you mean {string.Join(" or ", near.Select(layer => layer.DisplayName))}?");

            if (named.Count == 0)
            {
                said.Append(" None of its layers has a name - there is no layermap loaded - so name them by their numbers");

                var keys = Gds.AdditionalInformation.Layers.Keys.OrderBy(key => key).Take(MostListed).ToList();

                if (keys.Count > 0)
                    said.Append($": {string.Join(", ", keys)}");

                said.Append('.');
            }
            else
            {
                said.Append($" Its named layers are {string.Join(", ", named.Take(MostListed).Select(layer => layer.DisplayName))}");

                if (named.Count > MostListed)
                    said.Append($" and {named.Count - MostListed} more");

                said.Append('.');
            }

            said.Append(" A layer the file has nothing on yet can be drawn on by its numbers, like 70/20.");

            return said.ToString();
        }

        ///<summary>How many names an answer lists before saying how many more there are.</summary>
        internal const int MostListed = 60;

        ///<summary>A layer the way every answer writes one: "met1 (68/20)", or "68/20" when it has no name.</summary>
        internal string LayerName(LayerKey key)
        {
            if (Gds.AdditionalInformation.Layers.TryGetValue(key, out var layer))
                return layer.DisplayName;

            return key.ToString();
        }

        #endregion **************************************************************************



        #region The cell being edited *******************************************************

        ///<summary>The cell new shapes go into, or a refusal that says how to choose one.</summary>
        internal StructureModel CellToEdit()
        {
            if (cellOrWhy(out string why) is StructureModel cell)
                return cell;

            throw new AgentProblem(why);
        }

        ///<summary>The same without refusing, for an answer that only mentions the cell when there is one.</summary>
        internal StructureModel? CellIfAny()
        {
            return cellOrWhy(out _);
        }

        ///
        ///Settles which cell is being worked in, if nothing has said and the library leaves no doubt.
        ///
        ///**At the first call, before anything is made.** Making an empty cell makes a second top - nothing
        ///places it yet - and a library with two tops is one where a drawing tool has to ask which. Settled
        ///first, the cell somebody was obviously working in stays the one they are working in.
        ///
        internal void Settle()
        {
            if (Cell is null)
                cellOrWhy(out _);
        }

        private StructureModel? cellOrWhy(out string why)
        {
            why = "";

            if (Cell is string named)
            {
                if (Hierarchy.Named(Gds, named) is StructureModel chosen)
                {
                    workingIn = chosen;

                    return chosen;
                }

                why = $"The cell being edited, {named}, is not in the library any more. Choose another with open_cell.";

                return null;
            }

            var tops = Hierarchy.Summarize(Gds).Where(cell => cell.IsTop).Select(cell => cell.Name).ToList();

            if (tops.Count == 1)
            {
                Cell = tops[0];

                return Hierarchy.Named(Gds, tops[0]);
            }

            if (Gds.StreamFormat.Structures.Count == 0)
            {
                why = "This library has no cells. Make one with make_cell.";

                return null;
            }

            if (tops.Count == 0)
            {
                why = "Every cell in this library is placed by another, so none is the top. Choose one with open_cell.";

                return null;
            }

            why = $"This library has {tops.Count} top cells - {string.Join(", ", tops.Take(MostListed))} - so say which to work in with open_cell.";

            return null;
        }

        #endregion **************************************************************************



        #region What a shape is *************************************************************

        ///<summary>The box each placed cell covers in its own coordinates, kept for one call; see <see cref="Fresh"/>.</summary>
        private readonly Dictionary<string, Bounds> cellBoxes = new Dictionary<string, Bounds>(StringComparer.Ordinal);

        ///<summary>The placed cells whose flatten stopped before <see cref="cellBox"/> had all of them, and where it stopped.</summary>
        private readonly Dictionary<string, int> cutShort = new Dictionary<string, int>(StringComparer.Ordinal);

        ///
        ///Forgets what was worked out during the last call.
        ///
        ///Every call starts here, because the last one may have changed what a cell holds - and a box kept from
        ///before a change is a box that lies about where things are.
        ///
        internal void Fresh()
        {
            cellBoxes.Clear();
            cutShort.Clear();
        }

        ///
        ///The box an element covers in its own cell's coordinates: its outline for a shape, its anchor for a
        ///label, and for a placement everything the placed cell draws, where it is drawn.
        ///
        internal Bounds BoxOf(ElementModel model)
        {
            if (Hierarchy.PlacedBy(model) is string placed)
                return placementBox(model, placed);

            if (GdsFlattener.OutlineOf(model) is List<Element.Point> outline && outline.Count > 0)
                return Bounds.Of(outline);

            return Bounds.Of(CornersOf(model));
        }

        ///<summary>An element's coordinates as points, the closing repeat of a boundary included.</summary>
        internal static List<Element.Point> CornersOf(ElementModel model)
        {
            var points = new List<Element.Point>();

            if (model.Element.XY?.Data is not Int4Data xy)
                return points;

            for (int i = 0; i + 1 < xy.Values.Length; i += 2)
                points.Add(new Element.Point(xy.Values[i], xy.Values[i + 1]));

            return points;
        }

        private Bounds placementBox(ElementModel model, string placed)
        {
            var inside = cellBox(placed);
            var corners = CornersOf(model);

            if (inside.IsEmpty || corners.Count == 0)
                return Bounds.Empty;

            (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(model);

            var origins = new List<(double X, double Y)> { (corners[0].X, corners[0].Y) };

            //An array covers what its four corner instances cover, since every instance is the same box moved.
            if (model.Element is ArefModel && ArrayOf(model) is (int columns, int rows, double acrossX, double acrossY, double downX, double downY))
            {
                origins.Add((corners[0].X + ((columns - 1) * acrossX), corners[0].Y + ((columns - 1) * acrossY)));
                origins.Add((corners[0].X + ((rows - 1) * downX), corners[0].Y + ((rows - 1) * downY)));
                origins.Add((corners[0].X + ((columns - 1) * acrossX) + ((rows - 1) * downX), corners[0].Y + ((columns - 1) * acrossY) + ((rows - 1) * downY)));
            }

            var reached = new List<Element.Point>();

            foreach (var (x, y) in origins)
            {
                var placement = Transform.ForPlacement(mirrored, magnification, angle, x, y);

                reached.Add(placement.Apply(inside.Left, inside.Bottom));
                reached.Add(placement.Apply(inside.Right, inside.Bottom));
                reached.Add(placement.Apply(inside.Right, inside.Top));
                reached.Add(placement.Apply(inside.Left, inside.Top));
            }

            return Bounds.Of(reached);
        }

        private Bounds cellBox(string name)
        {
            if (cellBoxes.TryGetValue(name, out var known))
                return known;

            var flattened = GdsFlattener.Flatten(Gds, name, MostElements);
            var box = Measure.BoundsOf(flattened);

            cellBoxes[name] = box;

            if (flattened.Stopped)
                cutShort[name] = flattened.Elements.Count;

            return box;
        }

        ///
        ///An array's counts and its two steps, from the three points the format stores - the first instance,
        ///where the columns reach one step past the last, and the same for the rows.
        ///
        internal static (int Columns, int Rows, double AcrossX, double AcrossY, double DownX, double DownY)? ArrayOf(ElementModel model)
        {
            if (model.Element is not ArefModel aref)
                return null;

            if (aref.COLROW?.Data is not Int2Data counts || counts.Values.Length < 2)
                return null;

            var corners = CornersOf(model);

            int columns = counts.Values[0];
            int rows = counts.Values[1];

            if (corners.Count < 3 || columns < 1 || rows < 1)
                return null;

            return (
                columns,
                rows,
                (corners[1].X - corners[0].X) / (double)columns,
                (corners[1].Y - corners[0].Y) / (double)columns,
                (corners[2].X - corners[0].X) / (double)rows,
                (corners[2].Y - corners[0].Y) / (double)rows);
        }

        ///<summary>One shape as a line of an answer: "#12 met1 (68/20) rectangle 2 by 0.5 µm from (0, 0) to (2, 0.5)".</summary>
        internal string Line(ElementModel model, StructureModel cell)
        {
            return $"#{IdOf(model, cell)} {Describe(model)}";
        }

        ///<summary>What a shape is, in a phrase: its layer, its kind, its size and where it is.</summary>
        internal string Describe(ElementModel model)
        {
            var element = model.Element;

            string layer = "";

            if (ElementRecords.LayerOf(model) is LayerKey key)
                layer = LayerName(key) + " ";

            var corners = CornersOf(model);

            if (element is TextModel)
            {
                string says = RetextElement.TextOf(model) ?? "";

                if (corners.Count == 0)
                    return $"{layer}label \"{says}\"";

                return $"{layer}label \"{says}\" at {Point(corners[0])}";
            }

            if (element is SrefModel || element is ArefModel)
                return describePlacement(model, corners);

            var box = BoxOf(model);

            if (element is PathModel)
            {
                (int width, Paths.Ends ends) = Paths.Of(model) ?? (0, Paths.Ends.Flush);

                string finished = "";

                if (ends != Paths.Ends.Flush)
                    finished = $", {ends.ToString().ToLowerInvariant()} ends";

                if (corners.Count == 0)
                    return $"{layer}path {Microns(width)} µm wide";

                return $"{layer}path {Microns(width)} µm wide through {corners.Count} points from {Point(corners[0])} to {Point(corners[^1])}{finished}";
            }

            if (element is NodeModel)
                return $"{layer}node within {Span(box)}";

            if (IsRectangle(corners))
                return $"{layer}rectangle {Microns(box.Width)} by {Microns(box.Height)} µm from {Span(box)}";

            return $"{layer}polygon of {cornerCount(corners)} corners within {Span(box)}";
        }

        private string describePlacement(ElementModel model, List<Element.Point> corners)
        {
            string name = Hierarchy.PlacedBy(model) ?? "";

            (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(model);

            var how = new StringBuilder();

            if (mirrored)
                how.Append(", mirrored");

            if (angle != 0)
                how.Append($", turned {angle.ToString("0.###", CultureInfo.InvariantCulture)}°");

            if (magnification != 1)
                how.Append($", magnified {magnification.ToString("0.####", CultureInfo.InvariantCulture)} times");

            var box = BoxOf(model);
            string covering = "";

            if (!box.IsEmpty)
                covering = $", covering {Span(box)}";

            if (!box.IsEmpty && cutShort.TryGetValue(name, out int reached))
                covering += CutShort(reached);

            if (Hierarchy.Named(Gds, name) is null)
                covering = ", which names a cell the library does not have";

            if (corners.Count == 0)
                return $"placement of {name}{how}";

            if (model.Element is ArefModel && ArrayOf(model) is (int columns, int rows, double acrossX, double acrossY, double downX, double downY))
            {
                return $"array of {name}, {columns} columns by {rows} rows from {Point(corners[0])}, "
                    + $"columns {Point(acrossX, acrossY)} apart and rows {Point(downX, downY)} apart{how}{covering}";
            }

            return $"placement of {name} at {Point(corners[0])}{how}{covering}";
        }

        ///<summary>A ring's corners, not counting the repeat that closes it.</summary>
        private static int cornerCount(List<Element.Point> corners)
        {
            if (corners.Count > 1 && corners[0].Equals(corners[^1]))
                return corners.Count - 1;

            return corners.Count;
        }

        ///<summary>Four corners square to the axes - closed or not - which is what a rectangle is in a file.</summary>
        internal static bool IsRectangle(IReadOnlyList<Element.Point> corners)
        {
            int count = corners.Count;

            if (count == 5 && corners[0].Equals(corners[4]))
                count = 4;

            if (count != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                var from = corners[i];
                var to = corners[(i + 1) % 4];

                if (from.X != to.X && from.Y != to.Y)
                    return false;

                if (from.Equals(to))
                    return false;
            }

            return true;
        }

        #endregion **************************************************************************



        #region The assistant's own steps ***************************************************

        private readonly List<(LayoutEdit Edit, bool FollowsMine)> mine = new List<(LayoutEdit, bool)>();

        ///<summary>The history's version after the last thing this session did to it.</summary>
        private int sawVersion = -1;

        private bool topIsMine;

        ///<summary>Notes a step this session put on the history, and whether the one under it was its own too.</summary>
        internal void Made(LayoutEdit step, int versionBefore)
        {
            bool follows = topIsMine && versionBefore == sawVersion;

            mine.Add((step, follows));

            sawVersion = History.Version;
            topIsMine = true;
        }

        ///
        ///Takes back this session's most recent change, and says what it was.
        ///
        ///**Only while it is still the last thing on the history.** The history is shared with the person, and
        ///if anything happened since - their own edit, an undo, a redo - then undoing now takes back something
        ///else, which is not what the model asked for and not something it can see happening.
        ///
        internal string UndoMine()
        {
            if (mine.Count == 0 || !topIsMine)
                throw new AgentProblem("There is no change of mine left to undo - the step on top of the undo history is not one I made.");

            if (History.Version != sawVersion)
                throw new AgentProblem("The layout has been changed since my last change, so undoing now would take back somebody else's step. Ask for Undo in the app instead.");

            var (step, follows) = mine[^1];

            mine.RemoveAt(mine.Count - 1);

            History.Undo();

            sawVersion = History.Version;
            topIsMine = follows && mine.Count > 0;

            return step.Description;
        }

        #endregion **************************************************************************



        #region Rules around a change *******************************************************

        ///<summary>What the rules found near a change, at one moment - before it, or after.</summary>
        internal sealed class RuleSnapshot
        {
            public required List<DrcViolation> Found { get; init; }

            public required Bounds Near { get; init; }

            public List<string> Skipped { get; init; } = new List<string>();

            ///<summary>What of the cell the check could not see, because its flatten stopped - see <see cref="Drc.NotCheckedOf"/>.</summary>
            public List<string> NotChecked { get; init; } = new List<string>();
        }

        ///
        ///The rule violations within reach of an area of a cell, or null when nothing is being checked.
        ///
        ///**Around the change, not over the chip.** The cell is flattened and only what lies within twice the
        ///deck's longest distance of the area is checked - enough to see a new shape too close to an old one,
        ///and nothing like the cost of checking a whole layout after every rectangle. Density and antenna rules
        ///are left out: both are about a whole layout or a whole net, and a window cut out of one would report
        ///things that are not true of it.
        ///
        ///Taken once before a change and once after, so what goes back to the model is what the change did -
        ///not every violation that was already sitting near it.
        ///
        internal RuleSnapshot? RulesAround(StructureModel cell, Bounds area)
        {
            if (Deck is null || !CheckAfterEdits || area.IsEmpty)
                return null;

            int reach = ReachOf(Deck);
            long wide = Math.Min(Reach, (2 * (long)reach) + 1);

            var near = area.Grown(reach);
            var window = area.Grown((int)wide);

            var layout = GdsFlattener.Flatten(Gds, Hierarchy.NameOf(cell), MostElements);
            var picked = new FlattenedLayout();

            foreach (int at in layout.Index.Within(window))
                picked.Elements.Add(layout.Elements[at]);

            var off = new HashSet<string>(RulesOff, StringComparer.Ordinal);
            var skipped = new List<string>();

            foreach (var rule in Deck.Rules)
            {
                if (rule.Check == DrcCheck.Density || rule.Check == DrcCheck.Antenna)
                {
                    off.Add(rule.Id);
                    skipped.Add(rule.Id);
                }
            }

            var result = Drc.Check(Deck, picked, off);

            var found = new List<DrcViolation>();

            foreach (var violation in result.Violations)
            {
                if (violation.Bounds.Intersects(near) && !Waived.Contains(violation.Key))
                    found.Add(violation);
            }

            //Asked of the cell's flatten, since the window has no flatten of its own to have stopped.
            return new RuleSnapshot { Found = found, Near = near, Skipped = skipped, NotChecked = Drc.NotCheckedOf(layout) };
        }

        ///<summary>The longest distance any rule in the deck measures, in database units - how far a change can matter.</summary>
        internal static int ReachOf(DrcDeck deck)
        {
            long longest = 1;

            foreach (var rule in deck.Rules)
            {
                if (rule.Check == DrcCheck.Width
                    || rule.Check == DrcCheck.Space
                    || rule.Check == DrcCheck.Notch
                    || rule.Check == DrcCheck.Enclosure)
                    longest = Math.Max(longest, rule.Value);
            }

            return (int)Math.Min(longest, Reach);
        }

        ///<summary>What changed between the two snapshots, as a line or two of an answer.</summary>
        internal string RulesSay(RuleSnapshot before, RuleSnapshot after)
        {
            var was = new HashSet<string>(before.Found.Select(keyOf), StringComparer.Ordinal);
            var now = new HashSet<string>(after.Found.Select(keyOf), StringComparer.Ordinal);

            var broken = after.Found.Where(violation => !was.Contains(keyOf(violation))).ToList();
            int mended = before.Found.Count(violation => !now.Contains(keyOf(violation)));

            var said = new StringBuilder();

            if (broken.Count == 0)
            {
                said.Append("Rules: nothing broken around this change.");
            }
            else
            {
                said.Append($"Rules: this change broke {broken.Count} - ");
                said.Append(string.Join("; ", broken.Take(MostViolations).Select(ViolationText)));

                if (broken.Count > MostViolations)
                    said.Append($"; and {broken.Count - MostViolations} more");

                said.Append('.');
            }

            if (mended > 0)
                said.Append($" It cleared {mended} that were there before.");

            //A change past where the flatten stopped is not in what was checked at all, and "nothing broken" says nothing
            //about it unless this follows.
            foreach (string missing in after.NotChecked)
                said.Append(" Not checked: ").Append(missing);

            if (after.Skipped.Count > 0)
                said.Append(" Density and antenna rules are only checked by check_rules.");

            return said.ToString();
        }

        ///<summary>How many violations one answer spells out.</summary>
        internal const int MostViolations = 8;

        private static string keyOf(DrcViolation violation)
        {
            return $"{violation.RuleId}@{violation.Bounds}";
        }

        ///<summary>One violation as a phrase: the rule, what it asks, and where.</summary>
        internal string ViolationText(DrcViolation violation)
        {
            return $"{RuleText(violation.RuleId, violation.Check, violation.Limit, violation.Description)} at {Span(violation.Bounds)}";
        }

        ///<summary>A rule as a phrase, in microns: "met1.2 (space under 0.14 µm)".</summary>
        internal string RuleText(string id, DrcCheck check, long limit, string description)
        {
            string measured;

            if (check == DrcCheck.Area || check == DrcCheck.HoleArea)
                measured = $"{check.ToString().ToLowerInvariant()} under {Plain(limit * MicronsPerUnit * MicronsPerUnit)} µm²";
            else if (check == DrcCheck.Density)
                measured = $"density under {(limit / 10.0).ToString("0.#", CultureInfo.InvariantCulture)}%";
            else if (check == DrcCheck.OffGrid)
                measured = $"off the {Microns(limit)} µm grid";
            else if (check == DrcCheck.Antenna)
                measured = $"antenna ratio over {limit.ToString(CultureInfo.InvariantCulture)}";
            else
                measured = $"{check.ToString().ToLowerInvariant()} under {Microns(limit)} µm";

            if (description.Length > 0)
                return $"{id} ({measured}: {description})";

            return $"{id} ({measured})";
        }

        #endregion **************************************************************************
    }
}
