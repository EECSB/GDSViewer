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

            //
            //**Every placement counted in one walk of the library**, rather than a walk per cell. This asked
            //PlacementsOf of each cell in turn, and each of those reads every element of every cell - so a
            //library of a few hundred cells was read a few hundred times over, every time a file opened. On
            //the RGB mixer that was most of the 1.7 seconds the 2D view spent setting up a file it had already
            //flattened, before it had drawn anything.
            //
            var placed = new Dictionary<string, int>();

            foreach (var structure in gds.StreamFormat.Structures)
            {
                foreach (string name in Places(structure))
                {
                    placed.TryGetValue(name, out int already);
                    placed[name] = already + 1;
                }
            }

            foreach (var structure in gds.StreamFormat.Structures)
            {
                string name = NameOf(structure);

                if (name.Length == 0)
                    continue;

                placed.TryGetValue(name, out int placements);

                summaries.Add(new CellSummary(
                    name,
                    placements,
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

            ///<summary>One SREF or AREF of a cell, in the cell placing it. See Tree.</summary>
            Placement,

            ///<summary>How many shapes on a layer, or placements of a cell, were not listed. See Tree's caps.</summary>
            Rest
        }

        ///
        ///One row of the tree, at whichever of its levels it belongs to.
        ///
        ///One record for all of them rather than one each, because what a tree is drawn from is a flat list in
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

            ///<summary>A Cell row's own cell, the cell a Layer or a Shape belongs to, and the cell a Placement places.</summary>
            public string Structure { get; init; } = "";

            ///<summary>The cell placing a Cell row's cell, or holding a Placement. Empty for a cell at the top.</summary>
            public string Inside { get; init; } = "";

            ///<summary>The cell at the top of the row's branch: where the drawing starts for what the row stands for.</summary>
            public string Top { get; init; } = "";

            ///<summary>Set on a Layer row, and on a Shape row for the layer it is on.</summary>
            public LayerKey Layer { get; init; }

            ///<summary>The file's own element, which is what selecting a Shape row has to find.</summary>
            public ElementModel? Shape { get; init; }

            ///<summary>Set on a Placement row: the SREF or AREF itself.</summary>
            public ElementModel? Placement { get; init; }

            ///
            ///The placements a row is reached through from Top, outermost first - none for a cell at the top and what
            ///it holds itself. A cell placed four hundred times draws each of its shapes four hundred times, and this
            ///says which of them a row is; see DrawnThrough.
            ///
            public IReadOnlyList<ElementModel> Via { get; init; } = Array.Empty<ElementModel>();

            ///
            ///The coordinates of the cell holding what the row stands for, out to Top's: composed through Via, an
            ///array at its first place. Identity at the top, where a cell's own coordinates are the drawing's.
            ///
            public Transform Drawn { get; init; } = Transform.Identity;

            ///<summary>Shapes on a Layer row; shapes or placements left unlisted on a Rest row.</summary>
            public int Count { get; init; }

            ///<summary>This cell is already on the path above it - a loop - so nothing is listed under it.</summary>
            public bool Repeats { get; init; }

            ///<summary>Whether a press on it would open or shut something.</summary>
            public bool Folds { get; init; }

            ///<summary>Whether what it folds is showing.</summary>
            public bool Open { get; init; }

            ///
            ///Whether it starts open, so `folded` is what shuts it: a cell at the top. Everything else starts shut,
            ///and `opened` is what opens it - see Tree.
            ///
            public bool StartsOpen => Kind == TreeRowKind.Cell && Depth == 0;

            ///
            ///Equal when every field is, Via by the placements it holds rather than by which list holds them.
            ///
            ///A record compares a list by reference, so two trees built from the same file, row for row the same,
            ///came out different the moment a row carried placements - and two builds agreeing is exactly what the
            ///view's kept rows rest on (see HierarchyTests' edits that keep every row). A field added above has to
            ///be added here.
            ///
            public bool Equals(TreeRow? other)
            {
                if (other is null)
                    return false;

                if (ReferenceEquals(this, other))
                    return true;

                return Kind == other.Kind
                    && Depth == other.Depth
                    && Key == other.Key
                    && Equals(Cell, other.Cell)
                    && Structure == other.Structure
                    && Inside == other.Inside
                    && Top == other.Top
                    && Layer.Equals(other.Layer)
                    && ReferenceEquals(Shape, other.Shape)
                    && ReferenceEquals(Placement, other.Placement)
                    && Via.SequenceEqual(other.Via)
                    && Drawn.Equals(other.Drawn)
                    && Count == other.Count
                    && Repeats == other.Repeats
                    && Folds == other.Folds
                    && Open == other.Open;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Kind, Depth, Key, Count);
            }
        }

        ///<summary>How many shapes a layer lists before the rest become one line saying how many.</summary>
        public const int MostShapesListed = 200;

        ///
        ///How many of one cell's placements in another are listed before the rest become one line saying how many.
        ///A filler cell can be placed thousands of times, and a row each is a list nobody reads past. The text view's
        ///cell list lists them by the same number.
        ///
        public const int MostPlacementsListed = 200;

        ///
        ///The whole tree: the cells, where each is placed, and what each placement draws.
        ///
        ///**A cell at the top lists what it holds itself** - the layers it draws on, with the shapes on them, then
        ///each cell it places. **A placed cell lists its placements in the cell placing it**, one row each, where it
        ///is and how it is turned or arrayed, as the text view's cell list does. **A placement lists what it
        ///draws**: the placed cell's layers and shapes, and the cells that one places in turn. Since 2026-10-17 -
        ///before, a placed cell went straight to its layers, and nothing in the tree said where any instance of it
        ///was.
        ///
        ///A shape under a placement is the cell's own, said where that placement draws it - see TreeRow.Drawn - so
        ///the same shape under two placements says two places, and a press on either finds the one it says.
        ///
        ///**What folds, and what it starts as.** A cell at the top is open unless it is in `folded`, so the tree
        ///arrives showing what the file holds. Everything else is shut unless it is in `opened`: a layer, because
        ///the shapes are the level that can be a hundred thousand rows; a placed cell and a placement, because a
        ///standard cell placed four hundred times would be four hundred rows under every cell placing it, and
        ///everything under each of those again. So the two sets are the two defaults, and neither is a set of
        ///"everything" that has to be built before anything can be drawn.
        ///
        ///**A layer lists at most mostShapes, and a cell at most mostPlacements**, then one row saying how many are
        ///left. A cell of forty thousand boundaries is a real file, not a corner case, and a tree that tried to
        ///draw them would take the page down - the cap is what makes the level safe to open at all. The row that
        ///says so is there because a list that silently stopped would be a lie about the file.
        ///
        ///A cell placed by two parents is under both, with its placements in each. A loop is drawn once and never
        ///descended into, and a cell nothing reaches is at the root - see Tree(gds) for both.
        ///
        ///Layers before placed cells, both under what holds them: what a cell is made of, then what it places.
        ///
        public static List<TreeRow> Tree(
            GDS gds,
            IReadOnlySet<string> folded,
            IReadOnlySet<string> opened,
            int mostShapes = MostShapesListed,
            int mostPlacements = MostPlacementsListed)
        {
            var rows = new List<TreeRow>();
            var summaries = Summarize(gds);

            if (summaries.Count == 0)
                return rows;

            var byName = new Dictionary<string, CellSummary>();

            foreach (var summary in summaries)
                byName[summary.Name] = summary;

            //
            //What a cell holds, under the row standing for it - a cell at the top, or a placement of one: its layers
            //and their shapes, then each cell it places, with that cell's placements in it when its row is open, and
            //under each placement opened the same again.
            //
            void holds(StructureModel structure, string top, int depth, string path, IReadOnlyList<ElementModel> via, Transform drawn, HashSet<string> onThePath)
            {
                string name = NameOf(structure);

                foreach (var (layer, shapes) in LayersIn(structure))
                {
                    string layerPath = $"{path}\u001F{layer}";
                    bool showing = opened.Contains(layerPath);

                    rows.Add(new TreeRow
                    {
                        Kind = TreeRowKind.Layer,
                        Depth = depth,
                        Key = layerPath,
                        Structure = name,
                        Top = top,
                        Layer = layer,
                        Via = via,
                        Drawn = drawn,
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
                            Depth = depth + 1,
                            Key = $"{layerPath}\u001F{at}",
                            Structure = name,
                            Top = top,
                            Layer = layer,
                            Shape = shapes[at],
                            Via = via,
                            Drawn = drawn
                        });
                    }

                    if (shapes.Count > mostShapes)
                    {
                        rows.Add(new TreeRow
                        {
                            Kind = TreeRowKind.Rest,
                            Depth = depth + 1,
                            Key = $"{layerPath}\u001F…",
                            Structure = name,
                            Top = top,
                            Layer = layer,
                            Count = shapes.Count - mostShapes
                        });
                    }
                }

                foreach (var (placed, placements) in PlacementsByCell(structure))
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
                            Depth = depth,
                            Key = childPath,
                            Cell = child,
                            Structure = placed,
                            Inside = name,
                            Top = top,
                            Via = via,
                            Drawn = drawn,
                            Repeats = true
                        });

                        continue;
                    }

                    bool listing = opened.Contains(childPath);

                    rows.Add(new TreeRow
                    {
                        Kind = TreeRowKind.Cell,
                        Depth = depth,
                        Key = childPath,
                        Cell = child,
                        Structure = placed,
                        Inside = name,
                        Top = top,
                        Via = via,
                        Drawn = drawn,
                        Folds = true,
                        Open = listing
                    });

                    if (!listing)
                        continue;

                    //Whether a placement of it has anything to open to: a shape on a layer, or a cell placed.
                    var inner = Named(gds, placed);
                    bool draws = inner is not null && inner.Elements.Any(element => element.Element is IHasLayer || PlacedBy(element) is not null);

                    for (int at = 0; at < placements.Count && at < mostPlacements; at++)
                    {
                        var placement = placements[at];
                        string placementPath = $"{childPath}\u001F#{at}";
                        bool showing = draws && opened.Contains(placementPath);

                        rows.Add(new TreeRow
                        {
                            Kind = TreeRowKind.Placement,
                            Depth = depth + 1,
                            Key = placementPath,
                            Structure = placed,
                            Inside = name,
                            Top = top,
                            Placement = placement,
                            Via = via,
                            Drawn = drawn,
                            Folds = draws,
                            Open = showing
                        });

                        if (!showing || inner is null)
                            continue;

                        //
                        //Drawn where the placement puts it, an array at its first place - the one the flattener
                        //draws first, and where the array is written.
                        //
                        var through = new List<ElementModel>(via) { placement };
                        var placedAt = drawn;

                        if (InstancesOf(placement, 1) is { Count: > 0 } first)
                            placedAt = first[0].Then(drawn);

                        onThePath.Add(placed);

                        holds(inner, top, depth + 2, placementPath, through, placedAt, onThePath);

                        onThePath.Remove(placed);
                    }

                    if (placements.Count > mostPlacements)
                    {
                        rows.Add(new TreeRow
                        {
                            Kind = TreeRowKind.Rest,
                            Depth = depth + 1,
                            Key = $"{childPath}\u001F#…",
                            Structure = placed,
                            Inside = name,
                            Top = top,
                            Count = placements.Count - mostPlacements
                        });
                    }
                }
            }

            void root(CellSummary cell)
            {
                var structure = Named(gds, cell.Name);
                bool folds = structure is not null && structure.Elements.Any(element => element.Element is IHasLayer || PlacedBy(element) is not null);
                bool open = folds && !folded.Contains(cell.Name);

                rows.Add(new TreeRow
                {
                    Kind = TreeRowKind.Cell,
                    Depth = 0,
                    Key = cell.Name,
                    Cell = cell,
                    Structure = cell.Name,
                    Top = cell.Name,
                    Folds = folds,
                    Open = open
                });

                if (open && structure is not null)
                    holds(structure, cell.Name, 1, cell.Name, Array.Empty<ElementModel>(), Transform.Identity, new HashSet<string> { cell.Name });
            }

            foreach (var summary in summaries)
            {
                if (summary.IsTop)
                    root(summary);
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
                if (!reachable.Contains(summary.Name))
                    root(summary);
            }

            return rows;
        }

        ///
        ///What a cell places, a cell at a time: each cell it places, in the order the file first places it, with every
        ///SREF and AREF naming it in the order the file holds them.
        ///
        ///One walk of the cell for all of them. Asking PlacementsIn of each would walk it once per cell placed, which
        ///on the RGB mixer is fifty walks of 22,725 elements to list what one walk finds.
        ///
        public static List<(string Cell, List<ElementModel> Placements)> PlacementsByCell(StructureModel structure)
        {
            var found = new List<(string Cell, List<ElementModel> Placements)>();
            var at = new Dictionary<string, int>();

            foreach (var element in structure.Elements)
            {
                if (PlacedBy(element) is not string name)
                    continue;

                if (!at.TryGetValue(name, out int where))
                {
                    where = found.Count;
                    at[name] = where;

                    found.Add((name, new List<ElementModel>()));
                }

                found[where].Placements.Add(element);
            }

            return found;
        }

        ///
        ///Whether a drawn shape was reached from `top` through exactly these placements, in turn, and perhaps further
        ///- an array through any of its places. Which of the many shapes a cell placed several times draws is the
        ///one a tree row stands for; see TreeRow.Via.
        ///
        ///**By where each level puts the next, not by the transform as a whole.** Composing the placements here
        ///again would have to agree with the flattener to the last bit of a double; asking each placement whether
        ///it puts an instance where the drawing found one is what PlacementAt already answers, and it is asked the
        ///same way. So two placements putting one cell at the same point are one place to this, as they are to
        ///PlacementAt and so to the selection - the drawing records where a shape is, not which record put it there.
        ///
        public static bool DrawnThrough(IReadOnlyList<PlacementLevel> levels, string top, IReadOnlyList<ElementModel> via)
        {
            if (levels.Count < via.Count + 1 || levels[0].Structure != top)
                return false;

            for (int at = 0; at < via.Count; at++)
            {
                if (PlacedBy(via[at]) != levels[at + 1].Structure)
                    return false;

                if (levels[at].Placement.Inverse() is not Transform outOfParent)
                    return false;

                if (!puts(via[at], levels[at + 1].Placement.Then(outOfParent)))
                    return false;
            }

            return true;
        }

        ///
        ///What a tree row stands for in a drawn layout, as indexes into its elements: **for a placement, every shape
        ///drawn through it**, however deep, and **for a shape, the one drawn under the placements above its row** - see
        ///DrawnThrough. What a press on either goes to, in the 2D view and the 3D one alike. Nothing for any other row.
        ///
        ///A shape found under none of them - the file and the drawing disagreeing - is the first one drawn, which is
        ///what a press on its row found before the tree knew where each placement was.
        ///
        public static List<int> DrawnFor(FlattenedLayout layout, TreeRow row)
        {
            var found = new List<int>();

            if (row.Kind == TreeRowKind.Shape && row.Shape is not null)
            {
                int? first = null;

                for (int at = 0; at < layout.Elements.Count; at++)
                {
                    if (layout.Elements[at].Source is not ElementSource source || !ReferenceEquals(source.Model, row.Shape))
                        continue;

                    first ??= at;

                    if (source.Depth == row.Via.Count && DrawnThrough(source.Ancestry, row.Top, row.Via))
                    {
                        found.Add(at);

                        return found;
                    }
                }

                if (first is int drawn)
                    found.Add(drawn);

                return found;
            }

            if (row.Kind != TreeRowKind.Placement || row.Placement is null)
                return found;

            var through = new List<ElementModel>(row.Via) { row.Placement };

            for (int at = 0; at < layout.Elements.Count; at++)
            {
                //The cheap questions first: drawn under it means at least as deep, and in the cell it places at its depth.
                if (layout.Elements[at].Source is not ElementSource source || source.Depth < through.Count)
                    continue;

                if (source.Path[0] != row.Top || source.Path[through.Count] != row.Structure)
                    continue;

                if (DrawnThrough(source.Ancestry, row.Top, through))
                    found.Add(at);
            }

            return found;
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
                //An array has its grid already. The format has no array of arrays, and a second set of counts
                //written into one is an element nothing can read back.
                if (record.Type == RecordType.AREF || record.Type == RecordType.COLROW)
                    return null;

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

        ///
        ///An element's records with what it draws put through a transform: every coordinate moved, and whatever
        ///the element says about which way it faces composed with the transform's own turn, mirror and size.
        ///
        ///**Records rather than a model**, so whatever else the element carries comes across untouched - its
        ///flags, its properties, a path's ends, a label's justification, an array's counts. Only the records that
        ///say where it is and how it faces are written afresh.
        ///
        ///**A placement is a point and an orientation**, and moving the point alone is the mistake this is here
        ///to make impossible: the point lands in the right place and the cell it places is drawn facing the way
        ///it faced before - unturned, unmirrored, at its own size. Its reflection, angle and magnification are
        ///composed with the transform's, the way <see cref="Placement"/> reads them back off a matrix, and a
        ///label's the same way. An array's three points are all places, so each goes through as one and the grid
        ///turns with them.
        ///
        ///A path's width and its extensions are lengths, so a magnification grows them.
        ///
        ///Null when a coordinate would land outside what a file can hold.
        ///
        public static List<Record>? Through(IReadOnlyList<Record> element, Transform transform)
        {
            return Through(element, transform, Transform.Identity);
        }

        ///
        ///The same, with an absolute angle or magnification read against <paramref name="against"/>: the
        ///transform the element was drawn inside.
        ///
        ///**Which is what flattening needs.** A flag marking either as absolute means the parent's own is divided
        ///out when the element is drawn, so one moved out of a placement into the cell above is written with what
        ///it was drawn with - its own value less what the placement contributed - and the flag is dropped, since
        ///that value is now in the cell's own terms. Exact for a cell drawn at the top, which is how the
        ///flattener draws the cell being flattened into whenever it is the top.
        ///
        internal static List<Record>? Through(IReadOnlyList<Record> element, Transform transform, Transform against)
        {
            if (element.Count == 0)
                return null;

            var kind = element[0].Type;
            bool faces = kind == RecordType.SREF || kind == RecordType.AREF || kind == RecordType.TEXT;

            var flags = Strans.Default;
            double angle = 0;
            double magnification = 1;

            foreach (var record in element)
            {
                if (record.Type == RecordType.STRANS)
                    flags = Strans.From(record.Data);
                else if (record.Type == RecordType.MAG && record.Data is Real8Data mag && mag.Values.Length > 0)
                    magnification = mag.Value;
                else if (record.Type == RecordType.ANGLE && record.Data is Real8Data turned && turned.Values.Length > 0)
                    angle = turned.Value;
            }

            if (flags.AbsoluteMagnification && against.Scale != 0)
                magnification /= against.Scale;

            if (flags.AbsoluteAngle)
                angle -= against.AngleInDegrees;

            //Only the transform's turn, mirror and size act on which way something faces; its shift moves the point.
            var turning = new Transform(transform.XX, transform.XY, transform.YX, transform.YY, 0, 0);
            var facing = Transform.ForPlacement(flags.ReflectAboutX, magnification, angle, 0, 0).Then(turning);

            double scale = transform.Scale;
            bool placed = false;
            var moved = new List<Record>(element.Count + 3);

            foreach (var record in element)
            {
                if (record.Type == RecordType.STRANS || record.Type == RecordType.MAG || record.Type == RecordType.ANGLE)
                    continue;

                //Back where the format reads it: after the name, in front of an array's counts or the coordinates.
                if (faces && !placed && (record.Type == RecordType.COLROW || record.Type == RecordType.XY))
                {
                    moved.AddRange(orientationRecords(facing));
                    placed = true;
                }

                if (record.Type == RecordType.XY)
                {
                    if (record.Data is not Int4Data xy || throughPoints(xy.Values, transform) is not int[] points)
                        return null;

                    moved.Add(Make(RecordType.XY, new Int4Data(points)));

                    continue;
                }

                if (isLength(kind, record.Type) && scale != 1 && record.Data is Int4Data length && length.Values.Length > 0)
                {
                    double grown = Math.Round(length.Values[0] * scale);

                    if (!fits(grown))
                        return null;

                    moved.Add(Make(record.Type, new Int4Data((int)grown)));

                    continue;
                }

                moved.Add(Make(record.Type, record.Data));
            }

            return moved;
        }

        ///<summary>A reflection, an angle and a magnification as the records that say them, or none for none of them.</summary>
        private static List<Record> orientationRecords(Transform facing)
        {
            var written = new List<Record>();

            bool mirrored = facing.Mirrored;
            double angle = Settled(facing.AngleInDegrees);
            double magnification = Steadied(facing.Scale);

            if (!mirrored && angle == 0 && magnification == 1)
                return written;

            written.Add(Make(RecordType.STRANS, new BitArrayData(new Strans(mirrored, false, false).Encode())));

            //MAG before ANGLE, which is the order the format reads them in.
            if (magnification != 1)
                written.Add(Make(RecordType.MAG, new Real8Data(magnification)));

            if (angle != 0)
                written.Add(Make(RecordType.ANGLE, new Real8Data(angle)));

            return written;
        }

        ///
        ///Which records are lengths rather than places: a path's width and its two extensions, and a label's
        ///width. A negative width - the format's absolute one - is grown the same way, which is how the flattener
        ///draws it.
        ///
        private static bool isLength(RecordType kind, RecordType type)
        {
            if (type == RecordType.WIDTH)
                return kind == RecordType.PATH || kind == RecordType.TEXT;

            if (type == RecordType.BGNEXTN || type == RecordType.ENDEXTN)
                return kind == RecordType.PATH;

            return false;
        }

        ///<summary>Every coordinate pair through a transform, rounded once - or null when one lands past what a coordinate holds.</summary>
        private static int[]? throughPoints(int[] values, Transform transform)
        {
            var moved = new int[values.Length];

            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                (double x, double y) = transform.ApplyTo(values[i], values[i + 1]);

                double roundX = Math.Round(x);
                double roundY = Math.Round(y);

                if (!fits(roundX) || !fits(roundY))
                    return null;

                moved[i] = (int)roundX;
                moved[i + 1] = (int)roundY;
            }

            //An odd trailing value would be a malformed record; carried across rather than dropped.
            if (values.Length % 2 != 0)
                moved[^1] = values[^1];

            return moved;
        }

        ///<summary>Whether a whole number fits a four-byte coordinate - false for one that is not a number at all.</summary>
        private static bool fits(double value)
        {
            return value >= int.MinValue && value <= int.MaxValue;
        }

        ///
        ///The placement in a cell that puts an instance of <paramref name="named"/> where
        ///<paramref name="place"/> puts one: a single instance there, or an array with one of its places there.
        ///
        ///**By where it puts things, not only by what it names.** A cell placed several times in one parent has
        ///several placements, and only one of them is the one a click came through. An array is one placement
        ///with many places and a click lands on one of them - any of them, not only the first, whose point is the
        ///one the array is written at. Finding only that one made every other place of an array look like nothing
        ///anybody could act on.
        ///
        public static ElementModel? PlacementAt(StructureModel inside, string named, Transform place)
        {
            foreach (var element in inside.Elements)
            {
                if (PlacedBy(element) == named && puts(element, place))
                    return element;
            }

            return null;
        }

        ///<summary>Whether a placement puts an instance where `place` puts one: at its point, or at one of an array's places.</summary>
        private static bool puts(ElementModel placement, Transform place)
        {
            if (placement.Element is ArefModel)
                return hasPlaceAt(placement, place.Dx, place.Dy);

            if (placement.Element.XY?.Data is Int4Data xy && xy.Values.Length >= 2)
                return Math.Abs(place.Dx - xy.Values[0]) < 1 && Math.Abs(place.Dy - xy.Values[1]) < 1;

            return false;
        }

        ///
        ///Whether one of an array's places is at a point, to within a unit.
        ///
        ///Solved for the column and the row rather than walked, since an array can have a thousand million places.
        ///A grid that lies along one line - a single row, or two steps pointing the same way - has nothing to
        ///solve, and is walked, which is at most a row's worth for any array anybody draws.
        ///
        private static bool hasPlaceAt(ElementModel array, double x, double y)
        {
            if (gridOf(array) is not (int columns, int rows, double originX, double originY, double acrossX, double acrossY, double downX, double downY))
                return false;

            double offX = x - originX;
            double offY = y - originY;
            double determinant = (acrossX * downY) - (acrossY * downX);

            if (Math.Abs(determinant) > 1e-9)
            {
                double column = Math.Round(((offX * downY) - (offY * downX)) / determinant);
                double row = Math.Round(((acrossX * offY) - (acrossY * offX)) / determinant);

                if (column < 0 || column >= columns || row < 0 || row >= rows)
                    return false;

                return Math.Abs((column * acrossX) + (row * downX) - offX) < 1 && Math.Abs((column * acrossY) + (row * downY) - offY) < 1;
            }

            if (columns * (long)rows > MostWalked)
                return false;

            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    if (Math.Abs((column * acrossX) + (row * downX) - offX) < 1 && Math.Abs((column * acrossY) + (row * downY) - offY) < 1)
                        return true;
                }
            }

            return false;
        }

        ///<summary>The most places of a grid along one line that are looked through one by one.</summary>
        private const int MostWalked = 1_000_000;

        ///
        ///An array's counts, its first place and its two steps, from the three points the format stores - the
        ///first place, where the columns reach one step past the last, and the same for the rows. Null for
        ///anything that is not a well-formed array.
        ///
        private static (int Columns, int Rows, double OriginX, double OriginY, double AcrossX, double AcrossY, double DownX, double DownY)? gridOf(ElementModel array)
        {
            if (array.Element is not ArefModel aref || aref.COLROW?.Data is not Int2Data counts || counts.Values.Length < 2)
                return null;

            if (aref.XY?.Data is not Int4Data xy || xy.Values.Length < 6)
                return null;

            int columns = counts.Values[0];
            int rows = counts.Values[1];

            if (columns < 1 || rows < 1)
                return null;

            double originX = xy.Values[0];
            double originY = xy.Values[1];

            return (
                columns,
                rows,
                originX,
                originY,
                (xy.Values[2] - originX) / columns,
                (xy.Values[3] - originY) / columns,
                (xy.Values[4] - originX) / rows,
                (xy.Values[5] - originY) / rows);
        }

        ///
        ///Where `child` is placed in `parent`: every SREF and AREF there that names it, in the order the file holds
        ///them. The text view's cell list opens a cell's row, under the parent that places it, to one row each - and
        ///so does the 2D tree, from PlacementsByCell, which finds every cell's at once.
        ///
        public static List<ElementModel> PlacementsIn(StructureModel parent, string child)
        {
            var found = new List<ElementModel>();

            foreach (var element in parent.Elements)
            {
                if (PlacedBy(element) == child)
                    found.Add(element);
            }

            return found;
        }

        ///
        ///A placement in a line, as the text view's cell list and the 2D tree say it: where it is, in the file's own
        ///units as its XY record has them, then whatever it is turned, mirrored or scaled by, and an array's columns
        ///by its rows - "(1200, 3400)  R90  M  3 × 2". Only what differs from a plain placement is said.
        ///
        public static string DescribePlacement(ElementModel placement)
        {
            var said = new System.Text.StringBuilder();

            if (placement.Element.XY?.Data is Int4Data xy && xy.Values.Length >= 2)
                said.Append(FormattableString.Invariant($"({xy.Values[0]}, {xy.Values[1]})"));

            var (mirrored, angle, magnification) = TransformOf(placement);

            if (angle != 0)
                said.Append(FormattableString.Invariant($"  R{angle:0.###}"));

            if (mirrored)
                said.Append("  M");

            if (magnification != 1)
                said.Append(FormattableString.Invariant($"  ×{magnification:0.###}"));

            if (placement.Element is ArefModel array && array.COLROW?.Data is Int2Data counts && counts.Values.Length >= 2)
                said.Append(FormattableString.Invariant($"  {counts.Values[0]} × {counts.Values[1]}"));

            return said.ToString();
        }

        ///<summary>How many instances a placement makes: one, or an array's columns times its rows - none for a malformed one.</summary>
        public static long InstanceCount(ElementModel placement)
        {
            if (placement.Element is SrefModel)
                return 1;

            if (gridOf(placement) is (int columns, int rows, _, _, _, _, _, _))
                return columns * (long)rows;

            return 0;
        }

        ///
        ///Each instance a placement makes, as the transform that draws it in the cell holding the placement: one
        ///for a single instance, and one per place for an array, columns first - the order the flattener draws
        ///them in.
        ///
        ///**With its reflection, angle and magnification as they are written.** One marked absolute is measured
        ///against the world rather than the cell holding it, and the two are the same thing when that cell is
        ///drawn at the top.
        ///
        ///At most `most` of them: an array can have a thousand million places, and the tree wants its first.
        ///
        public static List<Transform> InstancesOf(ElementModel placement, int most = int.MaxValue)
        {
            var instances = new List<Transform>();

            StransModel? strans = null;

            if (placement.Element is SrefModel sref)
                strans = sref.Strans;
            else if (placement.Element is ArefModel aref)
                strans = aref.Strans;

            var flags = Strans.From(strans?.STRANS?.Data);
            double magnification = 1;
            double angle = 0;

            if (strans?.MAG?.Data is Real8Data mag && mag.Values.Length > 0)
                magnification = mag.Value;

            if (strans?.ANGLE?.Data is Real8Data turned && turned.Values.Length > 0)
                angle = turned.Value;

            if (placement.Element is SrefModel)
            {
                if (placement.Element.XY?.Data is Int4Data xy && xy.Values.Length >= 2)
                    instances.Add(Transform.ForPlacement(flags.ReflectAboutX, magnification, angle, xy.Values[0], xy.Values[1]));

                return instances;
            }

            if (gridOf(placement) is not (int columns, int rows, double originX, double originY, double acrossX, double acrossY, double downX, double downY))
                return instances;

            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    if (instances.Count >= most)
                        return instances;

                    double x = originX + (column * acrossX) + (row * downX);
                    double y = originY + (column * acrossY) + (row * downY);

                    instances.Add(Transform.ForPlacement(flags.ReflectAboutX, magnification, angle, x, y));
                }
            }

            return instances;
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
