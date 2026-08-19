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
        ///What a renderer wants. The file format cannot hold this - see <see cref="Merge"/> for the
        ///keyhole a GDSII boundary has to become - but a triangulator can take an outline and its holes
        ///directly, and that is far safer than handing it a ring whose channel doubles back on itself.
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
            var tree = new PolyTree64();

            Clipper.BooleanOp(clipTypeOf(operation), toClipper(subject), toClipper(clip), tree, FillRule.NonZero);

            return fromClipper(tree);
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
            var tree = new PolyTree64();

            Clipper.BooleanOp(ClipType.Union, toClipper(shapes), null, tree, FillRule.NonZero);

            return fromClipper(tree);
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
        ///**Because a hole is sometimes the thing being asked about.** <see cref="Merge"/> folds them into
        ///the boundary as keyholes, which is what GDSII needs and what a renderer can live with - but a rule
        ///about the *area of a hole* cannot be answered once the hole has been spliced into the ring around
        ///it. Real rule decks state those separately from the area of the shape, so the two have to stay
        ///apart this far down.
        ///
        ///The same walk <see cref="MergeByLayer"/> does, without the layer: that one takes elements and
        ///groups them, this one takes the shapes it is given and answers about those - which is what a
        ///derived layer is, since it belongs to no layer in the file.
        ///</summary>
        public static List<Ring> MergeToRings(IEnumerable<IReadOnlyList<Element.Point>> shapes)
        {
            var tree = new PolyTree64();

            Clipper.BooleanOp(ClipType.Union, toClipper(shapes), null, tree, FillRule.NonZero);

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
            var paths = toClipper(shapes);

            if (by == 0)
                return Merge(shapes);

            var grown = Clipper.InflatePaths(paths, by, JoinType.Miter, EndType.Polygon, MiterLimit);

            var tree = new PolyTree64();

            //Through a union afterwards rather than taken as it comes: growing two shapes until they touch
            //leaves two overlapping outlines, and this is also where the holes are found.
            Clipper.BooleanOp(ClipType.Union, grown, null, tree, FillRule.NonZero);

            return fromClipper(tree);
        }

        ///<summary>Clipper's own default. A right-angled corner needs 1.42, so this leaves room.</summary>
        private const double MiterLimit = 2.0;

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
        ///</summary>
        public static List<Outline> MergeByLayer(IEnumerable<Element> elements)
        {
            var merged = new List<Outline>();

            //
            //Labels and open runs left out, for the same reason: neither encloses anything.
            //
            //**An open run would be closed by the union and come back as area that is not there.** A path of
            //no width is handed through as its centerline - see Element.IsOpen - so unioning it joins its two
            //ends and fills the shape between them. On a straight line that is nothing; on an arc it is a
            //solid segment. Everything that measures or extrudes a layer comes through here, so leaving it
            //in would put that phantom area into a number somebody quotes and into a slab somebody looks at.
            //
            foreach (var layer in elements.Where(element => element.Text is null && !element.IsOpen).GroupBy(element => element.Layer.Key))
            {
                var tree = new PolyTree64();

                Clipper.BooleanOp(ClipType.Union, toClipper(layer.Select(element => element.Points)), null, tree, FillRule.NonZero);

                //The layer object itself, not a copy: the renderers read the color, the height and the
                //thickness off it, and all three are changed from the settings popup while the merged
                //geometry stays exactly as valid as it was.
                var owner = layer.First().Layer;

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

            return merged;
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
        private static Paths64 toClipper(IEnumerable<IReadOnlyList<Element.Point>> shapes)
        {
            var paths = new Paths64();

            foreach (var shape in shapes)
            {
                if (shape.Count < 3)
                    continue;

                var path = new Path64(shape.Count);

                foreach (var point in shape)
                    path.Add(new Point64(point.X, point.Y));

                //A closing point repeated from the first is how GDSII writes a ring and is not a vertex.
                if (path.Count > 3 && path[0] == path[^1])
                    path.RemoveAt(path.Count - 1);

                if (path.Count < 3)
                    continue;

                if (!Clipper.IsPositive(path))
                    path.Reverse();

                paths.Add(path);
            }

            return paths;
        }

        private static List<List<Element.Point>> fromClipper(PolyTree64 tree)
        {
            var shapes = new List<List<Element.Point>>();

            foreach (var raw in collect(tree))
                shapes.Add(toPoints(cutKeyholes(raw.Boundary, raw.Holes)));

            return shapes;
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
