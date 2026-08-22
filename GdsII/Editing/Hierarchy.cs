using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///What a library's cells know about each other: which ones name which, and what a placement is made of.
    ///
    ///Reading rather than changing. The edits that add a cell or put an instance of one down are in
    ///<see cref="LayoutEdit"/>; what is here is the questions they have to ask first - above all whether a
    ///placement would make a cell contain itself, which the format has no way to refuse and a reader has no
    ///way to finish.
    ///</summary>
    public static class Hierarchy
    {
        ///
        ///One cell, as much as a list of them needs to say: what it is called, how many placements name it,
        ///and how much is in it.
        ///
        ///Counted per element rather than per shape drawn, because that is what the cell holds - a cell with
        ///one placement in it holds one thing however much that placement draws.
        ///
        public sealed record CellSummary(string Name, int PlacedBy, int Elements, int Places)
        {
            ///<summary>Nothing names it, so the flattener draws it as a top of its own.</summary>
            public bool IsTop
            {
                get { return PlacedBy == 0; }
            }
        }

        ///<summary>
        ///Every cell, with what places it and what is in it, in the order the file defines them.
        ///
        ///The file's order rather than sorted: it is the order the cells were written, which in a library
        ///built by a tool usually means leaves first and the thing you want last.
        ///</summary>
        public static List<CellSummary> Summarize(GDS gds)
        {
            var summaries = new List<CellSummary>();

            foreach (var structure in gds.StreamFormat.Structures)
            {
                string name = NameOf(structure);

                if (name.Length == 0)
                    continue;

                summaries.Add(new CellSummary(
                    name,
                    PlacementsOf(gds, name),
                    structure.Elements.Count,
                    Places(structure).Count));
            }

            return summaries;
        }

        ///
        ///One line of the library seen as a tree: a cell, and how far in it sits.
        ///
        ///<see cref="Depth"/> is what a list draws as indentation. Flat rather than nested because that is
        ///what rendering wants - a nested shape would be walked back into a flat one to draw it.
        ///
        public sealed record CellRow(CellSummary Cell, int Depth, bool Repeats)
        {
            ///<summary>Whether anything is placed inside it, which is what a folder with a twisty has.</summary>
            public bool HasChildren
            {
                get { return Cell.Places > 0; }
            }
        }

        ///
        ///The library as a tree, outermost cells first and everything they place indented beneath them.
        ///
        ///**A cell placed in two parents appears under both**, which is where this parts company with a
        ///folder tree. A directory is in one place; a GDS cell is genuinely shared, and showing it once
        ///would mean picking a parent to call the real one. Repeats are marked rather than hidden, so a
        ///reader can tell "this is the same cell again" from "there are two of these".
        ///
        ///Which also means the tree can be enormous for a library that shares heavily - a standard cell used
        ///a thousand times is a thousand rows. The second and later times a cell is reached its children are
        ///left out: the shape below is identical to the first, and the first is the one worth walking.
        ///
        ///Cycles cannot recur. A file that says A places B and B places A is illegal and exists anyway - the
        ///flattener already guards against it, and a list that hung on one would be the same bug in a
        ///different place. A name already on the path from the root is drawn once and not descended into.
        ///
        ///Ordered as the file defines its cells at the top, and as each cell places them below - both of
        ///which are the order somebody reading the file would meet them.
        ///
        public static List<CellRow> Tree(GDS gds)
        {
            var rows = new List<CellRow>();
            var summaries = Summarize(gds);

            if (summaries.Count == 0)
                return rows;

            var byName = new Dictionary<string, CellSummary>();

            foreach (var summary in summaries)
                byName[summary.Name] = summary;

            var seen = new HashSet<string>();

            void walk(CellSummary cell, int depth, HashSet<string> onThePath)
            {
                bool repeats = !seen.Add(cell.Name);

                rows.Add(new CellRow(cell, depth, repeats));

                //Already drawn somewhere above, so what is under it has been drawn there too.
                if (repeats)
                    return;

                if (Named(gds, cell.Name) is not StructureModel structure)
                    return;

                //Distinct, because a cell placed four times by one parent is one child of it - the count of
                //placements is already on the row, and four identical lines would say it worse.
                var children = new List<string>();

                foreach (string placed in Places(structure))
                {
                    if (!children.Contains(placed))
                        children.Add(placed);
                }

                foreach (string placed in children)
                {
                    //A name that points at nothing is a broken reference, which the notice above the view
                    //reports - there is no row to draw for a cell the file does not contain.
                    if (!byName.TryGetValue(placed, out var child))
                        continue;

                    //On the path already: a loop. Drawn once so it can be seen, never descended into.
                    if (onThePath.Contains(placed))
                    {
                        rows.Add(new CellRow(child, depth + 1, true));

                        continue;
                    }

                    onThePath.Add(placed);

                    walk(child, depth + 1, onThePath);

                    onThePath.Remove(placed);
                }
            }

            foreach (var summary in summaries)
            {
                if (!summary.IsTop)
                    continue;

                walk(summary, 0, new HashSet<string> { summary.Name });
            }

            //
            //Anything a walk from the tops could not reach, at the root.
            //
            //Which happens for a library that is all loop - every cell is placed by something, so nothing is
            //a top, and a tree built only from tops would be empty. Better a flat list than no list.
            //
            foreach (var summary in summaries)
            {
                if (seen.Contains(summary.Name))
                    continue;

                walk(summary, 0, new HashSet<string> { summary.Name });
            }

            return rows;
        }

        ///<summary>What a row of the full tree stands for.</summary>
        public enum TreeRowKind
        {
            Cell,
            Layer,
            Shape,

            ///<summary>How many shapes on a layer were not drawn. See Tree's mostShapes.</summary>
            Rest
        }

        ///
        ///One row of the tree, at whichever of the three levels it belongs to.
        ///
        ///One record for all of them rather than three, because what a tree is drawn from is a flat list in
        ///order - the depth is what makes it a tree - and a list of one type is what a renderer can walk.
        ///The fields that do not apply to a kind are left at their defaults; Kind says which are real.
        ///
        public sealed record TreeRow
        {
            public required TreeRowKind Kind { get; init; }

            public required int Depth { get; init; }

            ///
            ///Where this row is in the tree, which is what an opened or folded set holds.
            ///
            ///A path rather than a name, because a cell placed by two parents is two rows and folding one
            ///should not fold the other - the same reason the tree draws it twice in the first place.
            ///
            public required string Key { get; init; }

            ///<summary>Set on a Cell row.</summary>
            public CellSummary? Cell { get; init; }

            ///<summary>The cell a Layer or a Shape belongs to.</summary>
            public string Structure { get; init; } = "";

            ///<summary>Set on a Layer row, and on a Shape row for the layer it is on.</summary>
            public LayerKey Layer { get; init; }

            ///<summary>The file's own element, which is what selecting a Shape row has to find.</summary>
            public ElementModel? Shape { get; init; }

            ///<summary>Shapes on a Layer row; shapes left undrawn on a Rest row.</summary>
            public int Count { get; init; }

            ///<summary>This cell is drawn above as well, so what is under it is not repeated here.</summary>
            public bool Repeats { get; init; }

            ///<summary>Whether a press on it would open or shut something.</summary>
            public bool Folds { get; init; }

            ///<summary>Whether what it folds is showing.</summary>
            public bool Open { get; init; }
        }

        ///<summary>How many shapes a layer lists before the rest become one line saying how many.</summary>
        public const int MostShapesListed = 200;

        ///
        ///The whole tree: cells, the layers each one draws on, and the shapes on those layers.
        ///
        ///The cells are Tree(gds)'s, walked the same way and with the same answers for a shared cell, a
        ///loop and a cell nothing reaches - see it for why each of those comes out as it does.
        ///
        ///**What folds, and what it starts as.** A cell is open unless it is in `folded`, because the tree
        ///has always shown the whole hierarchy at once and that is the thing it is for. A layer is shut
        ///unless it is in `opened`, because the shapes are the level that can be a hundred thousand rows and
        ///nobody arrives wanting all of them. So the two sets are the two defaults, and neither is a set of
        ///"everything" that has to be built before anything can be drawn.
        ///
        ///**A layer lists at most mostShapes of them**, then one row saying how many are left. A cell of
        ///forty thousand boundaries is a real file, not a corner case, and a tree that tried to draw them
        ///would take the page down - the cap is what makes this level safe to open at all. The row that says
        ///so is there because a list that silently stopped would be a lie about the file.
        ///
        ///Layers before placements, both under the cell: what a cell is made of, then what it holds.
        ///
        public static List<TreeRow> Tree(GDS gds, IReadOnlySet<string> folded, IReadOnlySet<string> opened, int mostShapes = MostShapesListed)
        {
            var rows = new List<TreeRow>();
            var summaries = Summarize(gds);

            if (summaries.Count == 0)
                return rows;

            var byName = new Dictionary<string, CellSummary>();

            foreach (var summary in summaries)
                byName[summary.Name] = summary;

            var seen = new HashSet<string>();

            void walk(CellSummary cell, int depth, string path, HashSet<string> onThePath)
            {
                bool repeats = !seen.Add(cell.Name);
                var structure = Named(gds, cell.Name);

                //Distinct, because a cell placed four times by one parent is one child of it.
                var children = new List<string>();

                if (structure is not null && !repeats)
                {
                    foreach (string placed in Places(structure))
                    {
                        if (!children.Contains(placed))
                            children.Add(placed);
                    }
                }

                var layers = LayersIn(structure);
                bool folds = !repeats && (layers.Count > 0 || children.Count > 0);
                bool open = folds && !folded.Contains(path);

                rows.Add(new TreeRow
                {
                    Kind = TreeRowKind.Cell,
                    Depth = depth,
                    Key = path,
                    Cell = cell,
                    Structure = cell.Name,
                    Repeats = repeats,
                    Folds = folds,
                    Open = open
                });

                //Already drawn somewhere above, so what is under it has been drawn there too.
                if (repeats || !open || structure is null)
                    return;

                foreach (var (layer, shapes) in layers)
                {
                    string layerPath = $"{path}{layer}";
                    bool showing = opened.Contains(layerPath);

                    rows.Add(new TreeRow
                    {
                        Kind = TreeRowKind.Layer,
                        Depth = depth + 1,
                        Key = layerPath,
                        Structure = cell.Name,
                        Layer = layer,
                        Count = shapes.Count,
                        Folds = shapes.Count > 0,
                        Open = showing
                    });

                    if (!showing)
                        continue;

                    for (int at = 0; at < shapes.Count && at < mostShapes; at++)
                    {
                        rows.Add(new TreeRow
                        {
                            Kind = TreeRowKind.Shape,
                            Depth = depth + 2,
                            Key = $"{layerPath}{at}",
                            Structure = cell.Name,
                            Layer = layer,
                            Shape = shapes[at]
                        });
                    }

                    if (shapes.Count > mostShapes)
                    {
                        rows.Add(new TreeRow
                        {
                            Kind = TreeRowKind.Rest,
                            Depth = depth + 2,
                            Key = $"{layerPath}…",
                            Structure = cell.Name,
                            Layer = layer,
                            Count = shapes.Count - mostShapes
                        });
                    }
                }

                foreach (string placed in children)
                {
                    //A name that points at nothing is a broken reference, which the notice above the view
                    //reports - there is no row to draw for a cell the file does not contain.
                    if (!byName.TryGetValue(placed, out var child))
                        continue;

                    string childPath = $"{path}/{placed}";

                    //On the path already: a loop. Drawn once so it can be seen, never descended into.
                    if (onThePath.Contains(placed))
                    {
                        rows.Add(new TreeRow
                        {
                            Kind = TreeRowKind.Cell,
                            Depth = depth + 1,
                            Key = childPath,
                            Cell = child,
                            Structure = child.Name,
                            Repeats = true
                        });

                        continue;
                    }

                    onThePath.Add(placed);

                    walk(child, depth + 1, childPath, onThePath);

                    onThePath.Remove(placed);
                }
            }

            foreach (var summary in summaries)
            {
                if (!summary.IsTop)
                    continue;

                walk(summary, 0, summary.Name, new HashSet<string> { summary.Name });
            }

            //
            //Anything a walk from the tops could not reach, at the root - see Tree(gds).
            //
            //**Reachability, not what was drawn.** A folded cell stops the walk, so asking "did a row get
            //emitted for it" would call every child of a folded cell unreachable and list it again at the
            //margin: folding TOP put LEAF back as a root of its own. Whether a cell is reachable is a fact
            //about the file and nothing to do with which twisties are shut, so it is answered separately.
            //
            var reachable = ReachableFromTops(gds);

            foreach (var summary in summaries)
            {
                if (reachable.Contains(summary.Name))
                    continue;

                walk(summary, 0, summary.Name, new HashSet<string> { summary.Name });
            }

            return rows;
        }

        ///
        ///Every cell a walk from the tops arrives at, however deep.
        ///
        ///What is left over is a cell nothing places and nothing reaches - which happens when a library is
        ///all loop, since then no cell is a top and a tree built only from tops would come out empty.
        ///
        public static HashSet<string> ReachableFromTops(GDS gds)
        {
            var reached = new HashSet<string>();

            void walk(string name)
            {
                if (!reached.Add(name))
                    return;

                if (Named(gds, name) is not StructureModel structure)
                    return;

                foreach (string placed in Places(structure))
                    walk(placed);
            }

            foreach (var summary in Summarize(gds))
            {
                if (summary.IsTop)
                    walk(summary.Name);
            }

            return reached;
        }

        ///
        ///The layer/datatype pairs a cell draws on, each with the elements on it, in the file's own order.
        ///
        ///The cell's own elements rather than the hierarchy's: a placement's layers belong to the cell it
        ///places, which has a row of its own further down. Adding them here would say a cell draws on a
        ///layer it never touches, and would say it once per level of nesting.
        ///
        ///SREF and AREF carry no layer and are left out - what they place is a cell, and the tree already
        ///has a row for that.
        ///
        public static List<(LayerKey Layer, List<ElementModel> Shapes)> LayersIn(StructureModel? structure)
        {
            var found = new List<(LayerKey Layer, List<ElementModel> Shapes)>();

            if (structure is null)
                return found;

            var at = new Dictionary<LayerKey, int>();

            foreach (var element in structure.Elements)
            {
                if (element.Element is not IHasLayer onLayer)
                    continue;

                var key = GdsFlattener.KeyOf(onLayer);

                if (!at.TryGetValue(key, out int where))
                {
                    where = found.Count;
                    at[key] = where;

                    found.Add((key, new List<ElementModel>()));
                }

                found[where].Shapes.Add(element);
            }

            //Sorted, unlike the cells above: a layer is a number, numbers have an order everybody already
            //knows, and a file's own order for them is whichever shape happened to be written first.
            found.Sort((left, right) => left.Layer.CompareTo(right.Layer));

            return found;
        }

        ///<summary>Every cell in the library, by name, in the order the file defines them.</summary>
        public static List<string> Names(GDS gds)
        {
            var names = new List<string>();

            foreach (var structure in gds.StreamFormat.Structures)
            {
                string name = NameOf(structure);

                if (name.Length > 0)
                    names.Add(name);
            }

            return names;
        }

        public static string NameOf(StructureModel structure)
        {
            if (structure.STRNAME?.Data is AsciiData name)
                return name.Value;

            return "";
        }

        public static StructureModel? Named(GDS gds, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return gds.StreamFormat.Structures.FirstOrDefault(each => NameOf(each) == name);
        }

        ///<summary>The cells one cell places directly, by name and with repeats, one per placement.</summary>
        public static List<string> Places(StructureModel structure)
        {
            var placed = new List<string>();

            foreach (var element in structure.Elements)
            {
                if (PlacedBy(element) is string name)
                    placed.Add(name);
            }

            return placed;
        }

        ///<summary>The cell an element places, or null for an element that is geometry rather than a reference.</summary>
        public static string? PlacedBy(ElementModel element)
        {
            if (SnameOf(element)?.Data is AsciiData name)
                return name.Value;

            return null;
        }

        ///<summary>
        ///The record carrying that name, for an edit that has to write a new one into it.
        ///
        ///Both kinds of reference spell it the same way and mean the same thing - one instance or a grid of
        ///them - so anything asking "which cell" or setting it should not have to know which it is holding.
        ///</summary>
        public static Record? SnameOf(ElementModel element)
        {
            if (element.Element is SrefModel sref)
                return sref.SNAME;

            if (element.Element is ArefModel aref)
                return aref.SNAME;

            return null;
        }

        ///
        ///Whether <paramref name="outer"/> reaches <paramref name="inner"/> through any chain of placements,
        ///counting a cell as reaching itself.
        ///
        ///**The question a placement has to be refused on.** Putting a cell inside something it already
        ///contains makes a hierarchy with no bottom: the format cannot say it is wrong, every writer will
        ///happily store it, and every reader hits its own depth limit and gives up somewhere different. The
        ///only place to catch it is before it is written.
        ///
        ///Walks with a set of what it has seen, so a library that *already* holds a cycle - which is a file
        ///this app can be handed - is answered rather than followed forever.
        ///
        public static bool Reaches(GDS gds, string outer, string inner)
        {
            if (outer == inner)
                return true;

            var seen = new HashSet<string>();
            var toVisit = new Stack<string>();

            toVisit.Push(outer);

            while (toVisit.Count > 0)
            {
                string name = toVisit.Pop();

                if (!seen.Add(name))
                    continue;

                if (Named(gds, name) is not StructureModel structure)
                    continue;

                foreach (string placed in Places(structure))
                {
                    if (placed == inner)
                        return true;

                    toVisit.Push(placed);
                }
            }

            return false;
        }

        ///<summary>How many placements across the whole library name a cell, which is what deleting one costs.</summary>
        public static int PlacementsOf(GDS gds, string name)
        {
            int found = 0;

            foreach (var structure in gds.StreamFormat.Structures)
            {
                foreach (string placed in Places(structure))
                {
                    if (placed == name)
                        found++;
                }
            }

            return found;
        }

        ///
        ///A name nothing in the library is using yet, built from a stem.
        ///
        ///Numbered from one upwards rather than from a count of what is there, because cells get deleted:
        ///counting would hand back a name that is free today and taken again the moment anything is undone.
        ///
        public static string UnusedName(GDS gds, string stem)
        {
            var taken = new HashSet<string>(Names(gds));

            if (!taken.Contains(stem))
                return stem;

            for (int i = 1; i < int.MaxValue; i++)
            {
                string tried = stem + i;

                if (!taken.Contains(tried))
                    return tried;
            }

            return stem;
        }

        ///
        ///The records of a placement: an SREF naming a cell, at a point in the placing cell's coordinates.
        ///
        ///No STRANS at all when it is neither turned nor mirrored, which is what the format's own default
        ///means and what every writer produces for a plain instance - a record saying "no transform" is a
        ///record every reader has to read to learn nothing.
        ///
        public static List<Record> PlacementRecords(string name, Element.Point at, bool mirrored, double angle)
        {
            var records = new List<Record>
            {
                Make(RecordType.SREF, null),
                Make(RecordType.SNAME, new AsciiData(name))
            };

            if (mirrored || angle != 0)
            {
                //Bit 0 counted from the left is the reflection, which is the top bit of the first byte.
                byte first = 0;

                if (mirrored)
                    first = 0x80;

                records.Add(Make(RecordType.STRANS, new BitArrayData(new byte[] { first, 0x00 })));

                if (angle != 0)
                    records.Add(Make(RecordType.ANGLE, new Real8Data(angle)));
            }

            records.Add(Make(RecordType.XY, new Int4Data(new int[] { at.X, at.Y })));
            records.Add(Make(RecordType.ENDEL, null));

            return records;
        }

        ///
        ///What a placement is written with: whether it is mirrored, how far it is turned, and how much it is
        ///scaled by.
        ///
        ///The format applies the reflection first and the rotation after it, which is the order everything
        ///about a placement has to be composed in - see <see cref="Turned"/>.
        ///
        public static (bool Mirrored, double Angle, double Magnification) TransformOf(ElementModel element)
        {
            StransModel? strans = null;

            if (element.Element is SrefModel sref)
                strans = sref.Strans;
            else if (element.Element is ArefModel aref)
                strans = aref.Strans;

            if (strans is null)
                return (false, 0, 1);

            bool mirrored = Strans.From(strans.STRANS?.Data).ReflectAboutX;

            double angle = 0;
            double magnification = 1;

            if (strans.ANGLE?.Data is Real8Data turned && turned.Values.Length > 0)
                angle = turned.Value;

            if (strans.MAG?.Data is Real8Data scaled && scaled.Values.Length > 0 && scaled.Value != 0)
                magnification = scaled.Value;

            return (mirrored, angle, magnification);
        }

        ///
        ///The same placement, of the same cell, written with a different transform and in a different place.
        ///
        ///Rebuilt rather than edited in place because a plain instance carries no STRANS at all: turning one
        ///has to *add* records, which is not something an element can be asked to grow. What it names comes
        ///off the old records; everything else is written afresh.
        ///
        public static List<Record>? WithTransform(
            IReadOnlyList<Record> placement,
            Element.Point at,
            bool mirrored,
            double angle,
            double magnification)
        {
            string? names = null;

            foreach (var record in placement)
            {
                if (record.Type == RecordType.SNAME && record.Data is AsciiData name)
                    names = name.Value;
            }

            if (names is null)
                return null;

            var made = new List<Record>
            {
                Make(RecordType.SREF, null),
                Make(RecordType.SNAME, new AsciiData(names))
            };

            bool scaled = magnification != 1 && magnification > 0;

            if (mirrored || angle != 0 || scaled)
            {
                var flags = new Strans(mirrored, false, false);

                made.Add(Make(RecordType.STRANS, new BitArrayData(flags.Encode())));

                //MAG before ANGLE, which is the order the format reads them in.
                if (scaled)
                    made.Add(Make(RecordType.MAG, new Real8Data(magnification)));

                if (angle != 0)
                    made.Add(Make(RecordType.ANGLE, new Real8Data(angle)));
            }

            made.Add(Make(RecordType.XY, new Int4Data(new int[] { at.X, at.Y })));
            made.Add(Make(RecordType.ENDEL, null));

            return made;
        }

        ///
        ///What a placement's own reflection and angle become when the thing it draws is turned or mirrored.
        ///
        ///**Composed rather than added to.** The format's transform is a reflection about X and then a
        ///rotation, so turning the *drawn result* by a quarter is a quarter added to the angle - but
        ///mirroring it is not a reflection added to the reflection. Working it through:
        ///a mirror across leaves `180 - angle` and flips the reflection, and a mirror down leaves `-angle`
        ///and flips it too. Guessing either of those gives a placement that draws, and draws wrong.
        ///
        public static (bool Mirrored, double Angle) Turned(bool mirrored, double angle, Turn turn)
        {
            if (turn == Turn.Quarter)
                return (mirrored, Settled(angle + 90));

            if (turn == Turn.ThreeQuarters)
                return (mirrored, Settled(angle - 90));

            if (turn == Turn.FlipX)
                return (!mirrored, Settled(180 - angle));

            return (!mirrored, Settled(-angle));
        }

        ///
        ///What a placement should be written with to draw the way this transform draws it.
        ///
        ///**The way back from a composed matrix.** Turning an instance where it sits is composition - out to
        ///the layout, turned there, and back into the cell that holds it - and what comes back is a matrix,
        ///where what has to be written is a reflection, an angle and a point. Doing it this way rather than
        ///by case analysis is what makes it right inside a cell that is itself placed turned or mirrored,
        ///which is the case every hand-worked version of this gets backwards.
        ///
        public static (bool Mirrored, double Angle, double Magnification, Element.Point At) Placement(Transform transform)
        {
            return (
                transform.Mirrored,
                Settled(transform.AngleInDegrees),
                Steadied(transform.Scale),
                new Element.Point((int)Math.Round(transform.Dx), (int)Math.Round(transform.Dy)));
        }

        ///
        ///An angle kept inside one turn, and settled onto a whole number when it is within a rounding error
        ///of one.
        ///
        ///A right angle that has been through a cosine and back comes home as 89.99999999999999, which is the
        ///same orientation written a different way - and a placement whose angle churns between two runs that
        ///meant the same thing is a file nobody can usefully diff.
        ///
        public static double Settled(double angle)
        {
            double kept = angle % 360;

            if (kept < 0)
                kept += 360;

            double whole = Math.Round(kept);

            if (Math.Abs(kept - whole) < 1e-9)
                kept = whole;

            if (kept >= 360)
                kept = 0;

            return kept;
        }

        ///<summary>And the same for a magnification, so a composed identity comes back as exactly one.</summary>
        public static double Steadied(double magnification)
        {
            if (Math.Abs(magnification - 1) < 1e-9)
                return 1;

            return magnification;
        }

        ///
        ///One placement turned into a grid of them: the same records, with the counts and two step vectors.
        ///
        ///**Built from the placement's own records rather than from what it means.** Whatever the instance
        ///was written with - a reflection, an angle, a magnification - comes across untouched, and the array
        ///stands where the one instance stood. Rebuilding it from a transform would round every one of those
        ///back into numbers, and lose anything the format carries that this does not read.
        ///
        ///**Three points, not a pitch.** The format stores where the first instance sits, where the columns
        ///would reach one step past the last, and the same down the rows - and a reader divides by the counts
        ///to get the step back. Writing the pitch there instead is a grid a tenth the size it should be, and
        ///it draws, which is the worst way to be wrong.
        ///
        ///One record where copying would be one element per place: a hundred by a hundred is a single AREF
        ///rather than ten thousand boundaries.
        ///
        public static List<Record>? AsArray(
            IReadOnlyList<Record> placement,
            int columns,
            int rows,
            int acrossX,
            int acrossY,
            int downX,
            int downY)
        {
            if (columns < 1 || rows < 1 || columns > short.MaxValue || rows > short.MaxValue)
                return null;

            var made = new List<Record>();
            bool laidOut = false;

            foreach (var record in placement)
            {
                if (record.Type == RecordType.SREF)
                {
                    made.Add(Make(RecordType.AREF, null));

                    continue;
                }

                if (record.Type == RecordType.XY)
                {
                    if (record.Data is not Int4Data xy || xy.Values.Length < 2)
                        return null;

                    int x = xy.Values[0];
                    int y = xy.Values[1];

                    //COLROW comes before the coordinates, which is where the format puts it.
                    made.Add(Make(RecordType.COLROW, new Int2Data((short)columns, (short)rows)));

                    made.Add(Make(RecordType.XY, new Int4Data(new int[]
                    {
                        x, y,
                        x + (columns * acrossX), y + (columns * acrossY),
                        x + (rows * downX), y + (rows * downY)
                    })));

                    laidOut = true;

                    continue;
                }

                made.Add(Make(record.Type, record.Data));
            }

            if (!laidOut)
                return null;

            return made;
        }

        ///<summary>
        ///One record, from its type and what it carries. Public because the edits that take records - a new
        ///cell, a placement, a copied element - are public too, and a caller with no way to build one could
        ///only reach them by taking records off something that already exists.
        ///</summary>
        public static Record Make(RecordType type, RecordData? data)
        {
            return new Record((short)type, data?.Encode() ?? Array.Empty<byte>());
        }
    }
}
