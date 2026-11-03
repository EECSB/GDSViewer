using Clipper2Lib;

namespace GdsII
{
    ///<summary>Which way two sets of shapes are combined.</summary>
    public enum BooleanOperation
    {
        ///<summary>Where both cover - the intersection.</summary>
        And,

        ///<summary>Either, merged into one outline - the union.</summary>
        Or,

        ///<summary>The first with the second taken out of it - the difference.</summary>
        Not,

        ///<summary>Where exactly one covers, and not where both do.</summary>
        Xor
    }

    ///<summary>
    ///Set operations on layout geometry: the four booleans, and growing or shrinking a shape.
    ///
    ///**These are what a PDK is written in.** A transistor gate is not a drawn layer - it is
    ///`poly AND diff`, the region where polysilicon crosses diffusion - and a design rule is a size
    ///followed by a boolean: "closer than 200nm" is answered by growing one shape by 200 and asking
    ///whether it now touches the other.
    ///
    ///**The arithmetic is Clipper2's**, vendored under [Clipper2](Clipper2/README.md). Robust polygon
    ///clipping is not something to write: coincident edges, self-intersections and rounding all have to be
    ///right at once, and everyone who needs it uses the same library. What is here is the translation - our
    ///points in database units to its, and its result back into something GDSII can hold.
    ///
    ///**Coordinates stay whole.** Layout is on an integer grid and Clipper works in 64-bit integers, so
    ///nothing is scaled and nothing is rounded on the way in.
    ///</summary>
    public static class Booleans
    {
        ///<summary>
        ///One merged shape: its outer boundary, and the holes in it kept as holes.
        ///
        ///What a renderer wants. The file format cannot hold this - see
        ///<see cref="Merge(IEnumerable{IReadOnlyList{Element.Point}})"/> for the keyhole a GDSII boundary has
        ///to become - but a triangulator can take an outline and its holes directly, and that is far safer than
        ///handing it a ring whose channel doubles back on itself.
        ///</summary>
        public sealed class Outline
        {
            ///<summary>
            ///The layer this came from, by reference rather than by value: its color, its height and its
            ///thickness are all changed from the settings popup, and the merged geometry stays exactly as
            ///valid through every one of them.
            ///</summary>
            public required Layer Layer { get; init; }

            public required List<Element.Point> Boundary { get; init; }

            public List<List<Element.Point>> Holes { get; init; } = new List<List<Element.Point>>();
        }

        #region Operations ******************************************************************

        ///<summary>
        ///Combines two sets of shapes. The result is hole-free, whatever the operation produced - see
        ///<see cref="cutKeyholes"/>.
        ///</summary>
        public static List<List<Element.Point>> Combine(
            IEnumerable<IReadOnlyList<Element.Point>> subject,
            IEnumerable<IReadOnlyList<Element.Point>> clip,
            BooleanOperation operation)
        {
            return fromClipper(combined(toClipper(subject), toClipper(clip), operation));
        }

        ///
        ///The operation over a whole set of shapes rather than over two of them.
        ///
        ///**What each one means for more than two is a decision, not a detail.**
        ///
        ///`Or` is the merge of all of them. `Not` is the *first* with all the others taken out of it, because
        ///subtraction is the one operation with a side: somebody who chose three shapes and pressed it meant
        ///to cut the second two out of the first, not to ask something about all three at once.
        ///
        ///`And` and `Xor` fold through every shape in turn, which is the reading that is about all of them:
        ///the region three shapes *all* cover is not the region the first shares with any of them, and those
        ///two answers differ the moment the second and third overlap somewhere the first does not.
        ///
        ///The result is hole-free whatever came out; see <see cref="cutKeyholes"/>.
        ///
        public static List<List<Element.Point>> CombineAll(
            IReadOnlyList<IReadOnlyList<Element.Point>> shapes,
            BooleanOperation operation)
        {
            if (shapes.Count == 0)
                return new List<List<Element.Point>>();

            //One shape is its own answer to every one of these, merged so it comes back in the same shape
            //anything else would.
            if (shapes.Count == 1 || operation == BooleanOperation.Or)
                return Merge(shapes);

            var rest = new List<IReadOnlyList<Element.Point>>();

            for (int i = 1; i < shapes.Count; i++)
                rest.Add(shapes[i]);

            if (operation == BooleanOperation.Not)
                return Combine(new[] { shapes[0] }, rest, BooleanOperation.Not);

            var result = Merge(new[] { shapes[0] });

            foreach (var next in rest)
                result = Combine(result, new[] { next }, operation);

            return result;
        }

        ///<summary>
        ///Merges a set of shapes into as few outlines as cover the same area. The union of a set with
        ///itself, which is common enough on its own to be worth naming: overlapping shapes on one layer are
        ///what a hierarchy flattens into, and most measurements of a layer are wrong until they are merged.
        ///</summary>
        public static List<List<Element.Point>> Merge(IEnumerable<IReadOnlyList<Element.Point>> shapes)
        {
            return fromClipper(united(toClipper(shapes)));
        }

        ///<summary>An outer boundary and the holes in it, kept apart rather than folded together.</summary>
        public sealed class Ring
        {
            public required List<Element.Point> Boundary { get; init; }

            public List<List<Element.Point>> Holes { get; init; } = new List<List<Element.Point>>();
        }

        ///<summary>
        ///Merges a set of shapes and hands back each outline with its holes still holes.
        ///
        ///**Because a hole is sometimes the thing being asked about.**
        ///<see cref="Merge(IEnumerable{IReadOnlyList{Element.Point}})"/> folds them into the boundary as
        ///keyholes, which is what GDSII needs and what a renderer can live with - but a rule about the *area of
        ///a hole* cannot be answered once the hole has been spliced into the ring around it. Real rule decks
        ///state those separately from the area of the shape, so the two have to stay apart this far down.
        ///
        ///The same walk <see cref="MergeByLayer"/> does, without the layer: that one takes elements and
        ///groups them, this one takes the shapes it is given and answers about those - which is what a
        ///derived layer is, since it belongs to no layer in the file.
        ///
        ///**And the form to keep geometry in between one operation and the next** - see the ring operations
        ///below for what handing a keyhole back to Clipper costs.
        ///</summary>
        public static List<Ring> MergeToRings(IEnumerable<IReadOnlyList<Element.Point>> shapes)
        {
            return toRings(united(toClipper(shapes)));
        }

        ///<summary>
        ///Moves every edge outwards by <paramref name="by"/> database units, or inwards for a negative
        ///distance.
        ///
        ///Mitered corners, because layout is drawn on a grid and rounding a right angle would put every
        ///corner off it. The limit is Clipper's own: past it a very sharp corner is cut square rather than
        ///drawn out into a spike several times longer than the offset.
        ///
        ///Shrinking a shape by more than half its narrowest part makes it disappear, which is not an error -
        ///it is how a minimum-width check is written.
        ///</summary>
        public static List<List<Element.Point>> Grow(IEnumerable<IReadOnlyList<Element.Point>> shapes, int by)
        {
            if (by == 0)
                return Merge(shapes);

            return fromClipper(grown(toClipper(shapes), by));
        }

        ///<summary>
        ///Grown by one distance and then by another, rounded to whole units once, at the end: an opening when
        ///the first shrinks and the second grows back, a closing the other way round.
        ///
        ///**Not two calls to <see cref="Grow(IEnumerable{IReadOnlyList{Element.Point}}, int)"/>.** Each of
        ///those rounds its outline to whole units, and the second then offsets the edges of the first one's
        ///rounded outline. A short slanted edge with half a unit gone from each end points a little another
        ///way, and seventy units out that is more than a unit: a corner of a 2 by 1 µm rectangle rounded to
        ///0.3 µm on sky130's grid, shrunk by 69 and grown back by 70, came back two units short of itself in
        ///one place, and the width check called the sliver between too narrow. Here the outline in between
        ///keeps a hundredth of a unit, so the second offset starts from where the first one really put it.
        ///
        ///What is left is the one rounding at the end, under a unit, which is what the width and spacing
        ///checks' slack of one unit is for - see <see cref="DrcChecks"/>.
        ///</summary>
        public static List<List<Element.Point>> Grow(IEnumerable<IReadOnlyList<Element.Point>> shapes, int by, int thenBy)
        {
            return fromClipper(grown(toClipper(shapes), by, thenBy));
        }

        ///<summary>How much finer than a database unit the outline between two grows is kept: a hundredth.</summary>
        private const double Finer = 100;

        ///<summary>Clipper's own default. A right-angled corner needs 1.42, so this leaves room.</summary>
        private const double MiterLimit = 2.0;

        ///
        ///The area some rings fill together, by a fill rule - the way a drawing program fills one shape made of
        ///several outlines.
        ///
        ///**Both rules, because a drawing says which it meant.** Two rings one inside the other are a washer
        ///under even-odd, and under nonzero they are a washer only if they run opposite ways - and a drawing
        ///that came out of somebody's editor will have been looked at under whichever its file names. Filled
        ///as a whole rather than ring by ring, which is what makes the inner ring a hole at all.
        ///
        ///Hole-free, like everything here.
        ///
        public static List<List<Element.Point>> Fill(IEnumerable<IReadOnlyList<Element.Point>> rings, bool evenOdd)
        {
            var rule = FillRule.NonZero;

            if (evenOdd)
                rule = FillRule.EvenOdd;

            var tree = new PolyTree64();

            //As they were drawn: under nonzero, which way a ring runs is what decides whether it is a hole.
            Clipper.BooleanOp(ClipType.Union, asDrawn(rings, 3, rings: true), null, tree, rule);

            return fromClipper(tree);
        }

        ///<summary>How the ends of an open line are drawn: stopped at the end, rounded past it, or squared off past it.</summary>
        public enum StrokeEnd
        {
            Butt,
            Round,
            Square
        }

        ///<summary>How a line is drawn round a corner.</summary>
        public enum StrokeJoin
        {
            Miter,
            Round,
            Bevel
        }

        ///
        ///The area a line <paramref name="width"/> wide covers, drawn along each run - what a pen paints.
        ///
        ///A closed run is drawn all the way round, so it comes out as a band with its middle a hole; an open
        ///one is drawn from end to end with the ends as asked. <paramref name="tolerance"/> is how far a round
        ///end or corner may sit off its circle, in the same units as everything else. The runs are unioned, so
        ///where a line crosses itself it is one area rather than two laid over each other.
        ///
        public static List<List<Element.Point>> Stroke(
            IEnumerable<(IReadOnlyList<Element.Point> Run, bool Closed)> runs,
            double width,
            StrokeJoin join,
            StrokeEnd end,
            double miterLimit,
            double tolerance)
        {
            var closed = new List<IReadOnlyList<Element.Point>>();
            var open = new List<IReadOnlyList<Element.Point>>();

            foreach (var (run, isClosed) in runs)
            {
                if (isClosed)
                    closed.Add(run);
                else
                    open.Add(run);
            }

            var joinType = JoinType.Miter;

            if (join == StrokeJoin.Round)
                joinType = JoinType.Round;
            else if (join == StrokeJoin.Bevel)
                joinType = JoinType.Bevel;

            var endType = EndType.Butt;

            if (end == StrokeEnd.Round)
                endType = EndType.Round;
            else if (end == StrokeEnd.Square)
                endType = EndType.Square;

            var offset = new ClipperOffset(Math.Max(1, miterLimit), Math.Max(0.25, tolerance));

            offset.AddPaths(asDrawn(closed, 3, rings: true), joinType, EndType.Joined);
            offset.AddPaths(asDrawn(open, 2, rings: false), joinType, endType);

            var drawn = new Paths64();

            offset.Execute(width / 2, drawn);

            var tree = new PolyTree64();

            Clipper.BooleanOp(ClipType.Union, drawn, null, tree, FillRule.NonZero);

            return fromClipper(tree);
        }

        ///<summary>
        ///Merges each layer's geometry and hands back the outlines with their holes still holes.
        ///
        ///For a renderer rather than for a file. A keyhole is what GDSII needs and the worst thing to give
        ///a triangulator: the channel's two edges lie on top of each other, which is exactly the degenerate
        ///case an ear-clipper is worst at. Anything drawing this can say "here is an outline and here are
        ///its holes" instead, which is what both the browser's extruder and the exporter's tessellator
        ///actually want.
        ///
        ///Labels are dropped - they are an anchor and a string rather than an area. The caller keeps its
        ///own.
        ///
        ///**A layer is merged a connected group at a time, not all at once.** Merging each layer whole took the
        ///RGB mixer 1.2 s here and 2.1 s in the browser, most of its opening in the 3D view: Clipper sweeps a
        ///line up the layer and keeps every edge that line crosses in a list it walks once per shape added, so
        ///its time grows with how many shapes sit side by side across the layout as well as with how many there
        ///are. Two shapes whose boxes neither overlap nor touch cannot change each other's union, so each group
        ///that does touch is merged on its own and the list only ever holds a group's edges - see
        ///<see cref="apart"/> - and a group one of its own rectangles covers is not swept at all - see
        ///<see cref="coveringRectangle"/>. The same layers merge in 0.46 s here.
        ///
        ///**The same ground, outline for outline and hole for hole.** What can differ is which corners in the
        ///middle of a straight side come out, which depends on what else is in the sweep. Measured on every
        ///bundled design and cell: every outline and every hole the same as the whole-layer merge made, once
        ///those corners are set aside. The order is the order the groups first appear in.
        ///</summary>
        public static List<Outline> MergeByLayer(IEnumerable<Element> elements)
        {
            var merged = new List<Outline>();

            foreach (var (owner, groups) in layersApart(elements))
                mergeGroups(owner, groups, 0, groups.Count, merged);

            return merged;
        }

        ///
        ///The same merge, offering to stop between one connected group and the next: <paramref name="pause"/> is
        ///awaited after each, so a caller on a single thread can let its page answer while a real chip's layers are
        ///merged - and throw from it to give the merge up. See <see cref="Pauses"/>.
        ///
        ///**A group is as fine as it gets.** One group is one Clipper call, and a supply net that runs across the
        ///whole chip is one group, so that one merge runs through - the pause comes after it.
        ///
        ///The same steps as <see cref="MergeByLayer"/>, a group at a time, so the two cannot merge differently.
        ///
        public static async ValueTask<List<Outline>> MergeByLayerAsync(IEnumerable<Element> elements, Func<ValueTask> pause)
        {
            var merged = new List<Outline>();

            foreach (var (owner, groups) in layersApart(elements))
            {
                await pause();

                for (int group = 0; group < groups.Count; group++)
                {
                    mergeGroups(owner, groups, group, group + 1, merged);

                    await pause();
                }
            }

            return merged;
        }

        ///
        ///Each layer's shapes, as the groups that touch - see <see cref="apart"/> - a layer at a time as they are asked
        ///for, in the order the layers first appear.
        ///
        private static IEnumerable<(Layer Owner, List<Paths64> Groups)> layersApart(IEnumerable<Element> elements)
        {
            //
            //Labels and open runs left out, for the same reason: neither encloses anything.
            //
            //**An open run would be closed by the union and come back as area that is not there.** A path of
            //no width is handed through as its centerline - see Element.IsOpen - so unioning it joins its two
            //ends and fills the shape between them. On a straight line that is nothing; on an arc it is a
            //solid segment. What extrudes a layer comes through here, and what measures one carries the same
            //guard itself (Measure.CoveredAreaOf), so leaving it in would put that phantom area into a number
            //somebody quotes or a slab somebody looks at.
            //
            foreach (var layer in elements.Where(element => element.Text is null && !element.IsOpen).GroupBy(element => element.Layer.Key))
            {
                //The layer object itself, not a copy: the renderers read the color, the height and the
                //thickness off it, and all three are changed from the settings popup while the merged
                //geometry stays exactly as valid as it was.
                var owner = layer.First().Layer;

                yield return (owner, apart(toClipper(layer.Select(element => element.Points))));
            }
        }

        ///<summary>The groups from <paramref name="from"/> up to <paramref name="to"/> of one layer, each merged into <paramref name="merged"/>.</summary>
        private static void mergeGroups(Layer owner, List<Paths64> groups, int from, int to, List<Outline> merged)
        {
            for (int g = from; g < to; g++)
            {
                var group = groups[g];
                var covering = coveringRectangle(group);

                if (covering is not null)
                {
                    merged.Add(new Outline
                    {
                        Layer = owner,
                        Boundary = unitedRectangle(covering)
                    });

                    continue;
                }

                var tree = new PolyTree64();

                Clipper.BooleanOp(ClipType.Union, group, null, tree, FillRule.NonZero);

                foreach (var raw in collect(tree))
                {
                    merged.Add(new Outline
                    {
                        Layer = owner,
                        Boundary = toPoints(raw.Boundary),
                        Holes = raw.Holes.Select(toPoints).ToList()
                    });
                }
            }
        }

        //
        //The Clipper calls themselves, shared by the hole-free operations above and the ring ones below, so the
        //two cannot come to work differently: what they differ in is only what goes in and what comes out.
        //
        private static PolyTree64 united(Paths64 paths)
        {
            var tree = new PolyTree64();

            Clipper.BooleanOp(ClipType.Union, paths, null, tree, FillRule.NonZero);

            return tree;
        }

        private static PolyTree64 combined(Paths64 subject, Paths64 clip, BooleanOperation operation)
        {
            var tree = new PolyTree64();

            Clipper.BooleanOp(clipTypeOf(operation), subject, clip, tree, FillRule.NonZero);

            return tree;
        }

        private static PolyTree64 grown(Paths64 paths, int by)
        {
            var grown = Clipper.InflatePaths(paths, by, JoinType.Miter, EndType.Polygon, MiterLimit);

            //Through a union afterwards rather than taken as it comes: growing two shapes until they touch
            //leaves two overlapping outlines, and this is also where the holes are found.
            return united(grown);
        }

        private static PolyTree64 grown(Paths64 paths, int by, int thenBy)
        {
            var fine = Clipper.ScalePaths(paths, Finer);

            var once = Clipper.InflatePaths(fine, by * Finer, JoinType.Miter, EndType.Polygon, MiterLimit);
            var twice = Clipper.InflatePaths(once, thenBy * Finer, JoinType.Miter, EndType.Polygon, MiterLimit);

            //Rounded back to whole units first, and through a union after it for the same reasons as one grow:
            //what touches merges, and the holes are found.
            return united(Clipper.ScalePaths(twice, 1 / Finer));
        }

        private static ClipType clipTypeOf(BooleanOperation operation)
        {
            switch (operation)
            {
                case BooleanOperation.And: return ClipType.Intersection;
                case BooleanOperation.Or: return ClipType.Union;
                case BooleanOperation.Not: return ClipType.Difference;
                case BooleanOperation.Xor: return ClipType.Xor;
            }

            throw new ArgumentOutOfRangeException(nameof(operation), operation, "Not a boolean operation.");
        }

        #endregion **************************************************************************



        #region Holes kept as holes *********************************************************

        //
        //The operations again, for geometry that goes from one of them into the next rather than out to a file.
        //
        //**A keyhole is for a file, and the worst thing to hand back to Clipper.** Everything above answers
        //hole-free, which is what GDSII can hold - but a design rule check is a chain of operations, and handing
        //each one's keyholed answer to the next makes Clipper take every channel apart again. The ground comes
        //out right; the time does not. Each channel comes back as a join that is really a split, and to build
        //its tree Clipper asks which half of each split lies inside the other by walking corners of one round
        //the whole of the other - and the two halves share the channel, so corner after corner answers "on the
        //boundary" and the walk goes on. One layer of a real analog layout merges into an outline of 32,203
        //corners carrying 2,516 holes: as that one keyholed outline it took 214 seconds to merge again, and as a
        //boundary and its holes 0.07. See docs/DRC.md, What it costs.
        //
        //**Nor is a channel only slow.** Shrunk, its two sides move apart: the same outline shrunk by 115 came
        //out as 14 shapes and 7.5% smaller, every hole opened onto the outside, where the boundary and its
        //holes shrank into the one shape it is.
        //
        //So these take rings and hand back rings, a boundary wound one way and its holes the other - see
        //toClipper - and only what leaves the chain is keyholed, by Keyholed.
        //

        ///<summary>
        ///Shapes as rings, each its own boundary with no hole in it, and nothing merged.
        ///
        ///What lets plain shapes - markers, the shapes of one net - take part in the ring operations without a
        ///union being run only to change their type. An outline that is already keyholed stays one ring whose
        ///channel is part of its boundary: nothing here looks for keyholes, which is why the ring operations
        ///exist - to never make one.
        ///</summary>
        public static List<Ring> AsRings(IEnumerable<IReadOnlyList<Element.Point>> shapes)
        {
            var rings = new List<Ring>();

            foreach (var shape in shapes)
                rings.Add(new Ring { Boundary = new List<Element.Point>(shape) });

            return rings;
        }

        ///<summary>
        ///Merges rings into as few as cover the same ground, holes still holes - the ring form of
        ///<see cref="Merge(IEnumerable{IReadOnlyList{Element.Point}})"/>.
        ///</summary>
        public static List<Ring> Merge(IEnumerable<Ring> rings)
        {
            return toRings(united(toClipper(rings)));
        }

        ///<summary>
        ///Combines two sets of rings, and hands back rings - the ring form of
        ///<see cref="Combine(IEnumerable{IReadOnlyList{Element.Point}}, IEnumerable{IReadOnlyList{Element.Point}}, BooleanOperation)"/>.
        ///</summary>
        public static List<Ring> Combine(IEnumerable<Ring> subject, IEnumerable<Ring> clip, BooleanOperation operation)
        {
            return toRings(combined(toClipper(subject), toClipper(clip), operation));
        }

        ///<summary>
        ///The ring form of <see cref="Grow(IEnumerable{IReadOnlyList{Element.Point}}, int)"/>.
        ///
        ///A hole is wound the other way round from its boundary, so the same offset moves its edges the other
        ///way: growing the ground around a hole closes it in, which is what growing a shape with a hole means.
        ///</summary>
        public static List<Ring> Grow(IEnumerable<Ring> rings, int by)
        {
            if (by == 0)
                return Merge(rings);

            return toRings(grown(toClipper(rings), by));
        }

        ///<summary>The ring form of <see cref="Grow(IEnumerable{IReadOnlyList{Element.Point}}, int, int)"/>, an opening or a closing.</summary>
        public static List<Ring> Grow(IEnumerable<Ring> rings, int by, int thenBy)
        {
            return toRings(grown(toClipper(rings), by, thenBy));
        }

        ///<summary>
        ///Each ring with its holes folded into its outline - the hole-free shape a file can hold and a marker is
        ///drawn from.
        ///
        ///**Exactly what <see cref="Merge(IEnumerable{IReadOnlyList{Element.Point}})"/> would have handed back**
        ///for the same tree: rings come out of Clipper with their boundary and holes already wound the way
        ///<see cref="cutKeyholes"/> splices them, so winding them again changes nothing, and the holes go in
        ///in the order the tree gave them.
        ///</summary>
        public static List<List<Element.Point>> Keyholed(IEnumerable<Ring> rings)
        {
            var shapes = new List<List<Element.Point>>();

            foreach (var ring in rings)
            {
                if (wound(ring.Boundary, counterClockwise: true) is not Path64 boundary)
                    continue;

                var holes = new List<Path64>();

                foreach (var hole in ring.Holes)
                {
                    if (wound(hole, counterClockwise: false) is Path64 path)
                        holes.Add(path);
                }

                shapes.Add(toPoints(cutKeyholes(boundary, holes)));
            }

            return shapes;
        }

        #endregion **************************************************************************



        #region Crossing the line *********************************************************

        ///<summary>
        ///Our polygons as Clipper's.
        ///
        ///**Every one is wound the same way on the way in.** A GDSII file says nothing about which
        ///direction a boundary runs and real files carry both, but the non-zero rule this uses counts
        ///windings - so two overlapping shapes drawn in opposite directions cancel each other out and
        ///leave a hole where there is solid metal. Normalizing first is what makes the rule mean "area
        ///covered by anything" rather than "area covered an odd number of times".
        ///
        ///It only shows when both shapes are in the *same* set, which merging a layer is: Clipper sorts
        ///out a subject against a clip by itself, so a test that put one on each side of a union passed
        ///with this removed.
        ///</summary>
        ///
        ///Runs as they were drawn: their own direction, and as few points as a run can have and still be one.
        ///
        ///<see cref="toClipper(IEnumerable{IReadOnlyList{Element.Point}})"/> turns every ring the same way
        ///round, which is right for layout - a shape is a shape whichever way its corners were listed - and wrong
        ///for a drawing filled by the nonzero rule, where the direction is the whole difference between a hole
        ///and an overlap. And a line drawn with a pen is a run of two points, which is no ring but is something
        ///to stroke.
        ///
        ///A ring's closing point, repeating its first, is not a corner and is dropped; an open run's last point
        ///is where the line stops, wherever that is, and is kept.
        ///
        private static Paths64 asDrawn(IEnumerable<IReadOnlyList<Element.Point>> runs, int fewest, bool rings)
        {
            var paths = new Paths64();

            foreach (var run in runs)
            {
                var path = new Path64(run.Count);

                foreach (var point in run)
                {
                    var corner = new Point64(point.X, point.Y);

                    if (path.Count == 0 || path[^1] != corner)
                        path.Add(corner);
                }

                if (rings && path.Count > 3 && path[0] == path[^1])
                    path.RemoveAt(path.Count - 1);

                if (path.Count >= fewest)
                    paths.Add(path);
            }

            return paths;
        }

        private static Paths64 toClipper(IEnumerable<IReadOnlyList<Element.Point>> shapes)
        {
            var paths = new Paths64();

            foreach (var shape in shapes)
            {
                if (wound(shape, counterClockwise: true) is Path64 path)
                    paths.Add(path);
            }

            return paths;
        }

        ///<summary>
        ///Rings as Clipper's: every boundary wound counter-clockwise and every hole clockwise.
        ///
        ///**Which is what lets a hole be handed back as a hole.** Under the non-zero rule a point inside a hole
        ///is counted once by the boundary around it and once the other way by the hole, which is nothing - so
        ///the ground the rings cover is exactly the ground they mean, with no channel reaching in to say so.
        ///Wound here by what each ring is rather than taken as it came, for the reason the shapes are: a hole
        ///running the same way as its boundary would count its own ground twice instead of not at all.
        ///
        ///A boundary with too few corners to enclose anything takes its holes with it, since they are holes in
        ///nothing.
        ///</summary>
        private static Paths64 toClipper(IEnumerable<Ring> rings)
        {
            var paths = new Paths64();

            foreach (var ring in rings)
            {
                if (wound(ring.Boundary, counterClockwise: true) is not Path64 boundary)
                    continue;

                paths.Add(boundary);

                foreach (var hole in ring.Holes)
                {
                    if (wound(hole, counterClockwise: false) is Path64 path)
                        paths.Add(path);
                }
            }

            return paths;
        }

        ///<summary>One ring as a Clipper path running the way asked, or null when it has too few corners to enclose anything.</summary>
        private static Path64? wound(IReadOnlyList<Element.Point> shape, bool counterClockwise)
        {
            if (shape.Count < 3)
                return null;

            var path = new Path64(shape.Count);

            foreach (var point in shape)
                path.Add(new Point64(point.X, point.Y));

            //A closing point repeated from the first is how GDSII writes a ring and is not a vertex.
            if (path.Count > 3 && path[0] == path[^1])
                path.RemoveAt(path.Count - 1);

            if (path.Count < 3)
                return null;

            if (Clipper.IsPositive(path) != counterClockwise)
                path.Reverse();

            return path;
        }

        private static List<List<Element.Point>> fromClipper(PolyTree64 tree)
        {
            var shapes = new List<List<Element.Point>>();

            foreach (var raw in collect(tree))
                shapes.Add(toPoints(cutKeyholes(raw.Boundary, raw.Holes)));

            return shapes;
        }

        private static List<Ring> toRings(PolyTree64 tree)
        {
            var rings = new List<Ring>();

            foreach (var raw in collect(tree))
            {
                rings.Add(new Ring
                {
                    Boundary = toPoints(raw.Boundary),
                    Holes = raw.Holes.Select(toPoints).ToList()
                });
            }

            return rings;
        }

        ///<summary>An outer boundary and the holes in it, still Clipper's.</summary>
        private readonly record struct RawOutline(Path64 Boundary, List<Path64> Holes);

        ///<summary>
        ///Walks the tree into outlines. A hole's own children are outlines again - an island in a lake -
        ///and come out as shapes of their own rather than being folded into the ring around them.
        ///</summary>
        private static List<RawOutline> collect(PolyTree64 tree)
        {
            var outlines = new List<RawOutline>();

            foreach (var child in tree)
                collectInto(outlines, (PolyPath64)child);

            return outlines;
        }

        private static void collectInto(List<RawOutline> outlines, PolyPath64 outline)
        {
            var holes = new List<Path64>();

            foreach (var child in outline)
            {
                var hole = (PolyPath64)child;

                holes.Add(hole.Polygon!);

                foreach (var island in hole)
                    collectInto(outlines, (PolyPath64)island);
            }

            outlines.Add(new RawOutline(outline.Polygon!, holes));
        }

        private static List<Element.Point> toPoints(Path64 path)
        {
            var points = new List<Element.Point>(path.Count);

            foreach (var point in path)
                points.Add(new Element.Point { X = checked((int)point.X), Y = checked((int)point.Y) });

            return points;
        }

        #endregion **************************************************************************



        #region Groups that merge apart *****************************************************

        ///<summary>How many boxes a bucket of the grid in <see cref="apart"/> is aimed at holding.</summary>
        private const int PerBucket = 4;

        ///<summary>A ceiling on that grid's side, so a huge layer cannot ask for a huge array.</summary>
        private const int MostBuckets = 512;

        ///
        ///The shapes split into groups no union can join: within a group every box overlaps or touches
        ///another's, and between two groups no box does.
        ///
        ///**Which is what makes merging them apart the same as merging them together.** Two shapes whose boxes
        ///are apart share no area and no edge, so neither can change what the other comes out as. A shape in a
        ///hole is in the box of whatever surrounds the hole, but the bars of a ring each have a box of their
        ///own and an island can sit clear of all four - and then it is a group of its own, which is right: the
        ///union of a ring and an island it does not touch is the ring and the island. Touching counts, since two
        ///shapes meeting along an edge come out as one outline.
        ///
        ///**Found through a grid**, the way Drc's CandidateGrid finds what a marker is near. Each box goes into
        ///every bucket it covers, the boxes sharing a bucket are compared, and a union-find joins the ones that
        ///meet. Layout is spread fairly evenly over its own extent, so a bucket holds a handful and a rail across
        ///the whole layout is one row of buckets.
        ///
        ///**A box over more than four rows' worth of buckets is compared with everything instead.** In the grid
        ///it would sit in most of the buckets, and a few hundred like it - a fill, a stack of die-sized frames -
        ///would put every one of them in every bucket and compare each with all the others once per bucket.
        ///
        ///Groups come back in the order their first shape was given, each with its shapes in their own order.
        ///
        private static List<Paths64> apart(Paths64 paths)
        {
            var groups = new List<Paths64>();
            int count = paths.Count;

            if (count == 0)
                return groups;

            if (count == 1)
            {
                groups.Add(paths);

                return groups;
            }

            var boxes = new Rect64[count];

            long left = long.MaxValue;
            long bottom = long.MaxValue;
            long right = long.MinValue;
            long top = long.MinValue;

            for (int i = 0; i < count; i++)
            {
                //Clipper names a box's sides for a Y that runs down, so its top is the smaller Y.
                boxes[i] = Clipper.GetBounds(paths[i]);

                left = Math.Min(left, boxes[i].left);
                right = Math.Max(right, boxes[i].right);
                bottom = Math.Min(bottom, boxes[i].top);
                top = Math.Max(top, boxes[i].bottom);
            }

            int side = (int)Math.Clamp(Math.Ceiling(Math.Sqrt(count / (double)PerBucket)), 1, MostBuckets);
            long width = Math.Max(1, right - left);
            long height = Math.Max(1, top - bottom);

            int columnOf(long x)
            {
                return (int)Math.Clamp((x - left) * side / width, 0, side - 1);
            }

            int rowOf(long y)
            {
                return (int)Math.Clamp((y - bottom) * side / height, 0, side - 1);
            }

            //Each shape a group of its own to begin with, named by the first shape in it.
            var parent = new int[count];

            for (int i = 0; i < count; i++)
                parent[i] = i;

            int rootOf(int at)
            {
                while (parent[at] != at)
                {
                    parent[at] = parent[parent[at]];
                    at = parent[at];
                }

                return at;
            }

            void joinIfMeeting(int one, int other)
            {
                if (!meet(boxes[one], boxes[other]))
                    return;

                one = rootOf(one);
                other = rootOf(other);

                if (one != other)
                    parent[Math.Max(one, other)] = Math.Min(one, other);
            }

            //
            //Every box's buckets, in one array: counted first, so each bucket's run of it is known before
            //anything is written. The big boxes are set aside here.
            //
            int buckets = side * side;
            long most = side * 4L;
            var starts = new int[buckets + 1];
            var big = new List<int>();
            var inGrid = new bool[count];

            for (int i = 0; i < count; i++)
            {
                var box = boxes[i];
                long covered = (long)(columnOf(box.right) - columnOf(box.left) + 1) * (rowOf(box.bottom) - rowOf(box.top) + 1);

                if (covered > most)
                {
                    big.Add(i);

                    continue;
                }

                inGrid[i] = true;

                for (int row = rowOf(box.top); row <= rowOf(box.bottom); row++)
                {
                    for (int column = columnOf(box.left); column <= columnOf(box.right); column++)
                        starts[(row * side) + column + 1]++;
                }
            }

            for (int bucket = 0; bucket < buckets; bucket++)
                starts[bucket + 1] += starts[bucket];

            var members = new int[starts[buckets]];
            var next = (int[])starts.Clone();

            for (int i = 0; i < count; i++)
            {
                if (!inGrid[i])
                    continue;

                var box = boxes[i];

                for (int row = rowOf(box.top); row <= rowOf(box.bottom); row++)
                {
                    for (int column = columnOf(box.left); column <= columnOf(box.right); column++)
                        members[next[(row * side) + column]++] = i;
                }
            }

            for (int bucket = 0; bucket < buckets; bucket++)
            {
                for (int one = starts[bucket]; one < starts[bucket + 1]; one++)
                {
                    for (int other = one + 1; other < starts[bucket + 1]; other++)
                        joinIfMeeting(members[one], members[other]);
                }
            }

            foreach (int one in big)
            {
                for (int other = 0; other < count; other++)
                {
                    if (other != one)
                        joinIfMeeting(one, other);
                }
            }

            var groupOf = new int[count];

            Array.Fill(groupOf, -1);

            for (int i = 0; i < count; i++)
            {
                int root = rootOf(i);

                if (groupOf[root] < 0)
                {
                    groupOf[root] = groups.Count;
                    groups.Add(new Paths64());
                }

                groups[groupOf[root]].Add(paths[i]);
            }

            return groups;
        }

        ///
        ///The rectangle in a group that covers all of it, or null when none does.
        ///
        ///**The group's union is then that rectangle, and nothing has to be swept to know it.** Every shape lies
        ///inside its own box and every box inside the group's, so a rectangle that is the group's box has the
        ///whole group in it. Most of a real layer is like this: a contact, a via, a strip of metal touching
        ///nothing else on its layer, each a group of one - and where two cells meet, the same contact twice, once
        ///from each, on top of itself. On the RGB mixer that is every one of 55,866 contacts on mcon and 43,365 on
        ///licon, and a union apiece was a whole sweep set up for the one shape.
        ///
        private static Path64? coveringRectangle(Paths64 group)
        {
            var box = Clipper.GetBounds(group);

            foreach (var ring in group)
            {
                if (!isRectangle(ring))
                    continue;

                var own = Clipper.GetBounds(ring);

                if (own.left == box.left && own.right == box.right && own.top == box.top && own.bottom == box.bottom)
                    return ring;
            }

            return null;
        }

        ///<summary>Whether two of Clipper's boxes overlap or touch - a shared side or corner counts.</summary>
        private static bool meet(Rect64 one, Rect64 other)
        {
            return one.left <= other.right
                && one.right >= other.left
                && one.top <= other.bottom
                && one.bottom >= other.top;
        }

        ///
        ///Whether a ring is a rectangle square to the axes: four corners, each side along one axis, and the
        ///next side along the other.
        ///
        ///The turn is what rules out four corners in a line, each side along the same axis, which enclose
        ///nothing and are not a rectangle of any size.
        ///
        private static bool isRectangle(Path64 ring)
        {
            if (ring.Count != 4)
                return false;

            for (int i = 0; i < 4; i++)
            {
                var from = ring[i];
                var to = ring[(i + 1) % 4];
                var after = ring[(i + 2) % 4];

                bool across = from.Y == to.Y && from.X != to.X;
                bool up = from.X == to.X && from.Y != to.Y;

                if (!across && !up)
                    return false;

                bool nextAcross = to.Y == after.Y && to.X != after.X;

                if (across == nextAcross)
                    return false;
            }

            return true;
        }

        ///
        ///A rectangle as the union of it alone comes back: from the corner furthest right and up, and
        ///counter-clockwise from there - see <see cref="coveringRectangle"/> for when that is a whole group's.
        ///
        ///**Exactly**, rather than merely covering the same ground, so writing it out is not a second opinion
        ///about what a merge makes. Every ring reaches the union turned counter-clockwise - see toClipper - and
        ///a rectangle turned that way comes out like this whichever corner it started at. Checked against
        ///Clipper on every lone rectangle in the three largest bundled designs, 193,397 of them, and pinned by a
        ///test against it. Over a group the rectangle covers, a sweep could also have kept a corner partway
        ///along a side, where something inside meets it; that is the one difference, and it covers nothing.
        ///
        private static List<Element.Point> unitedRectangle(Path64 rectangle)
        {
            long left = rectangle[0].X;
            long right = rectangle[0].X;
            long bottom = rectangle[0].Y;
            long top = rectangle[0].Y;

            for (int i = 1; i < 4; i++)
            {
                left = Math.Min(left, rectangle[i].X);
                right = Math.Max(right, rectangle[i].X);
                bottom = Math.Min(bottom, rectangle[i].Y);
                top = Math.Max(top, rectangle[i].Y);
            }

            return new List<Element.Point>(4)
            {
                new Element.Point { X = (int)right, Y = (int)top },
                new Element.Point { X = (int)left, Y = (int)top },
                new Element.Point { X = (int)left, Y = (int)bottom },
                new Element.Point { X = (int)right, Y = (int)bottom }
            };
        }

        #endregion **************************************************************************



        #region Holes ***********************************************************************

        ///<summary>
        ///Folds a shape's holes into its outline, so what comes out is one ring with no hole in it.
        ///
        ///**GDSII has no hole.** A boundary is a filled outline and nothing else, so a shape with a hole
        ///has to be written as one that reaches in and comes back out along the same line - a keyhole. That
        ///is what every tool emits and what every tool expects; a hole written as a second boundary on the
        ///same layer would be drawn as solid.
        ///
        ///Each hole is bridged by casting a ray to the right from its rightmost corner and cutting in at
        ///the first edge the ray meets. The holes are taken rightmost-first and each is spliced into the
        ///ring the last one left behind, so a ray that would have crossed another hole meets the boundary
        ///of the hole already folded in instead.
        ///</summary>
        private static Path64 cutKeyholes(Path64 outline, List<Path64> holes)
        {
            if (holes.Count == 0)
                return outline;

            var ring = new Path64(outline);

            //Rightmost first. The ray goes right, so a hole further right can never need to cross one
            //further left - and the ones it could cross are already part of the ring by the time it does.
            foreach (var hole in holes.OrderByDescending(rightmostOf))
                ring = spliceHole(ring, hole);

            return ring;
        }

        private static long rightmostOf(Path64 path)
        {
            long most = long.MinValue;

            foreach (var point in path)
                most = Math.Max(most, point.X);

            return most;
        }

        private static Path64 spliceHole(Path64 ring, Path64 hole)
        {
            int from = rightmostIndex(hole);
            Point64 start = hole[from];

            if (!castRight(ring, start, out int edge, out Point64 landing))
            {
                //Nothing to the right of a hole means the shape is not what it claimed to be. Left as it
                //was rather than thrown: a drawing missing one hole is better than an operation that
                //refuses a file.
                return ring;
            }

            var spliced = new Path64(ring.Count + hole.Count + 3);

            //Out to the edge the ray met...
            for (int i = 0; i <= edge; i++)
                spliced.Add(ring[i]);

            spliced.Add(landing);

            //...in along the channel, all the way round the hole, and back out the same way.
            for (int i = 0; i < hole.Count; i++)
                spliced.Add(hole[(from + i) % hole.Count]);

            spliced.Add(start);
            spliced.Add(landing);

            for (int i = edge + 1; i < ring.Count; i++)
                spliced.Add(ring[i]);

            return spliced;
        }

        private static int rightmostIndex(Path64 path)
        {
            int best = 0;

            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].X > path[best].X)
                    best = i;
            }

            return best;
        }

        ///<summary>
        ///The first edge of the ring a rightward ray from <paramref name="from"/> meets, and where.
        ///
        ///The landing point is rounded to the grid like everything else, which moves it along that edge by
        ///less than one database unit - the boundary bulges by under a nanometer at one point, and the ring
        ///stays simple because the point is still inserted between the two corners it lies between.
        ///</summary>
        private static bool castRight(Path64 ring, Point64 from, out int edge, out Point64 landing)
        {
            edge = -1;
            landing = default;

            double nearest = double.MaxValue;

            for (int i = 0; i < ring.Count; i++)
            {
                Point64 a = ring[i];
                Point64 b = ring[(i + 1) % ring.Count];

                //An edge the ray's own line does not cross cannot be hit. Half-open on purpose, so a ray
                //passing exactly through a corner meets one of the two edges rather than both or neither.
                if ((a.Y > from.Y) == (b.Y > from.Y))
                    continue;

                double at = a.X + ((double)(from.Y - a.Y) / (b.Y - a.Y) * (b.X - a.X));

                if (at < from.X || at >= nearest)
                    continue;

                nearest = at;
                edge = i;
            }

            if (edge < 0)
                return false;

            landing = new Point64((long)Math.Round(nearest), from.Y);

            return true;
        }

        #endregion **************************************************************************
    }
}
