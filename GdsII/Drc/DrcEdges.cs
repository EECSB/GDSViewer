namespace GdsII
{
    ///<summary>
    ///How a distance between two edges is measured.
    ///
    ///**A real rule names one**, and the three disagree by more than rounding. KLayout offers the same
    ///three under the same names, which is deliberate: a deck written against one tool should mean the same
    ///thing here.
    ///</summary>
    public enum DrcMetric
    {
        ///<summary>
        ///The true shortest distance between the two segments, corners included.
        ///
        ///What "closer than" means in ordinary speech, and the default these methods take when a caller names
        ///none. A deck's rule that names no metric goes to the sizing engine instead - see <see cref="DrcChecks"/>.
        ///</summary>
        Euclidean,

        ///<summary>
        ///Measured with a square rather than a circle, so a diagonal approach counts as the larger of its
        ///two axis distances.
        ///
        ///What the region-based checks in <see cref="DrcChecks"/> measure in, because mitered sizing on
        ///rectilinear geometry is exactly the Minkowski sum with a square. Offered here so the two engines
        ///can be asked the same question.
        ///</summary>
        Square,

        ///<summary>
        ///Only where the two edges face each other, and only the perpendicular distance across that facing.
        ///
        ///**This is the one that makes a rule like sky130's poly.4 expressible.** "Spacing of poly on field
        ///to diff, parallel edges only" means the rule is about two edges running alongside each other and
        ///says nothing about a corner approaching an edge end-on. Measured Euclidean, every perpendicular
        ///approach in a cell is a violation and the real ones are buried; measured this way, a pair whose
        ///spans do not overlap when projected onto each other is not a pair at all.
        ///</summary>
        Projection
    }

    ///<summary>
    ///Two edges that are too close, and the ground between them.
    ///
    ///**The thing a region-based check cannot produce.** An opening says "this area is too narrow"; this
    ///says "these two edges are 130 apart", which is what a rule qualified by edge direction is written
    ///against and what KLayout's own markers are.
    ///
    ///Each side is the part of its edge that is too close to the other, not the whole edge - KLayout's edge
    ///pair, corner for corner. A wedge is the exception: two edges closing to a point are reported from the
    ///point out to where they are wide enough.
    ///</summary>
    public sealed class DrcEdgePair
    {
        public required Element.Point AFrom { get; init; }
        public required Element.Point ATo { get; init; }
        public required Element.Point BFrom { get; init; }
        public required Element.Point BTo { get; init; }

        ///<summary>How far apart they were found to be, in database units, by the rule's own metric.</summary>
        public required double Distance { get; init; }

        ///<summary>
        ///The four corners of the ground between them, for drawing.
        ///
        ///Wound so the quadrilateral does not cross itself: the second edge is taken backwards, because two
        ///edges facing each other run in opposite directions and joining them end to end in order would tie
        ///a bow.
        ///</summary>
        public List<Element.Point> Marker()
        {
            return new List<Element.Point> { AFrom, ATo, BFrom, BTo };
        }
    }

    ///<summary>
    ///Design rule checks that answer in edge pairs.
    ///
    ///**Why this exists beside <see cref="DrcChecks"/>.** That one measures by sizing and set operations,
    ///which is exact in the square metric, reports regions, and cannot express a rule qualified by edge
    ///direction - three of sky130's rules are refused by name because of it. This one walks the edges
    ///themselves, so it can measure Euclidean, it can be told to consider only edges that face each other,
    ///and what it reports is the pair rather than the area.
    ///
    ///**It is not a scanline.** A real engine sweeps a line across the layout, which is the way to do this
    ///when the edges number in the millions. What is here indexes the edges into a uniform grid and asks
    ///each one about its neighbors, which is the same complexity for layout-shaped input - edges are spread
    ///over the extent rather than piled up - and a great deal less code to be wrong in. The measured cost is
    ///in `docs/DRC.md`.
    ///
    ///**Material is always to the left.** Every ring is wound so that walking it keeps the shape's inside
    ///on the left: outer rings counter-clockwise, holes clockwise. That one invariant is what lets an edge
    ///know which way is out without carrying a flag, and it is why the edges are taken from rings -
    ///<see cref="Booleans.MergeToRings"/>, or a layer <see cref="DrcLayers"/> kept as rings - rather than from
    ///<see cref="Booleans.Merge(IEnumerable{IReadOnlyList{Element.Point}})"/>: a keyholed ring has the
    ///channel's two sides lying on top of each other, and neither of them knows which side the material is on.
    ///</summary>
    public static class DrcEdges
    {
        #region Edges ***********************************************************************

        ///<summary>One directed edge of a ring, with the material on its left.</summary>
        private readonly record struct Edge(Element.Point From, Element.Point To)
        {
            public long DeltaX
            {
                get { return (long)To.X - From.X; }
            }

            public long DeltaY
            {
                get { return (long)To.Y - From.Y; }
            }

            public Bounds Box
            {
                get { return Bounds.Of(new[] { From, To }); }
            }

            ///<summary>The same edge walked the other way, so what was on its left is on its right.</summary>
            public Edge Reversed
            {
                get { return new Edge(To, From); }
            }
        }

        ///<summary>
        ///Every edge of a merged layer, wound so the material is on the left of each.
        ///
        ///Merged first, because two overlapping shapes have edges running through each other's insides and
        ///every one of those is a false pair - a rule is about the boundary of what is covered, not about
        ///how the designer happened to draw it.
        ///
        ///And then straightened, because the rings a merge hands back are not always that boundary drawn the
        ///simplest way - see <see cref="straightened"/>.
        ///</summary>
        private static List<Edge> EdgesOf(List<Booleans.Ring> rings)
        {
            var edges = new List<Edge>();

            foreach (var ring in rings)
            {
                //An outer ring counter-clockwise and a hole clockwise both leave the material on the left,
                //which is the whole point of normalizing them differently.
                appendRing(edges, ring.Boundary, wantCounterClockwise: true);

                foreach (var hole in ring.Holes)
                    appendRing(edges, hole, wantCounterClockwise: false);
            }

            return straightened(edges);
        }

        ///<summary>
        ///The edges with each straight run of boundary made one edge, and whatever is walked both ways along a
        ///line taken out.
        ///
        ///**A merge leaves two things on a line that are no part of the boundary's shape**, and IHP's SG13G2
        ///showed both. Clipper keeps a corner wherever the corner of a shape that went into the merge lands on
        ///a straight side: sg13g2_and2_2 draws its NWell as three rectangles whose union is one, and the merge
        ///comes back with three extra corners along its top and bottom. A side cut there is two edges, and the
        ///piece away from the corner no longer shares that corner with the side across it - so the two were
        ///measured against each other along the boundary itself, 480 nm, and called a width.
        ///
        ///**And a merge does not always undo a keyhole.** A 60 GHz amplifier's Metal3 is a frame with two
        ///holes whose tops lie on one line; folded into keyholes and merged again, it came back with one
        ///channel still in it - its two sides walked in opposite directions along 14.5 µm with metal on both,
        ///a slit that bounds nothing, whose end read as a wedge closing to a point.
        ///
        ///So the edges on each line are added up along it, one way counting for and the other against. Where
        ///they cancel the line has material on both sides of it, and is no boundary; where they do not, one
        ///edge runs from where the count starts to where it stops. A line whose edges come out as they went
        ///in - a single edge, which is nearly every line, or several that never touch - keeps them where they
        ///were, so a layout with nothing to straighten is walked exactly as it was before this existed.
        ///</summary>
        private static List<Edge> straightened(List<Edge> edges)
        {
            //The first edge on each line, and a list only for a line some other edge shares - nearly every
            //line holds one edge, and a list apiece was most of what this cost.
            var firstOn = new Dictionary<(long, long, Int128), int>(edges.Count);
            var shared = new Dictionary<(long, long, Int128), List<int>>();
            var lineOfEdge = new (long, long, Int128)[edges.Count];

            for (int i = 0; i < edges.Count; i++)
            {
                var line = lineOf(edges[i]);

                lineOfEdge[i] = line;

                if (firstOn.TryAdd(line, i))
                    continue;

                if (!shared.TryGetValue(line, out var on))
                {
                    on = new List<int> { firstOn[line] };

                    shared.Add(line, on);
                }

                on.Add(i);
            }

            //What each line comes to, kept only where that is not what was there.
            var replaced = new Dictionary<(long, long, Int128), List<Edge>>();

            foreach (var line in shared)
            {
                //Edges that do not touch along their line - a row of contacts with their tops in line - add up
                //to themselves.
                if (inOrderApart(edges, line.Value))
                    continue;

                var runs = runsAlong(edges, line.Value);

                if (!sameAs(runs, edges, line.Value))
                    replaced.Add(line.Key, runs);
            }

            if (replaced.Count == 0)
                return edges;

            var result = new List<Edge>(edges.Count);

            for (int i = 0; i < edges.Count; i++)
            {
                if (!replaced.TryGetValue(lineOfEdge[i], out var runs))
                {
                    result.Add(edges[i]);

                    continue;
                }

                //In place of the line's first edge, and nothing in place of the rest of them.
                if (firstOn[lineOfEdge[i]] == i)
                    result.AddRange(runs);
            }

            return result;
        }

        ///<summary>
        ///Puts a line's edges in order along it, and says whether each ends before the next begins.
        ///</summary>
        private static bool inOrderApart(List<Edge> edges, List<int> on)
        {
            bool upright = edges[on[0]].DeltaX == 0;

            long low(int index)
            {
                var edge = edges[index];

                if (upright)
                    return Math.Min(edge.From.Y, edge.To.Y);

                return Math.Min(edge.From.X, edge.To.X);
            }

            long high(int index)
            {
                var edge = edges[index];

                if (upright)
                    return Math.Max(edge.From.Y, edge.To.Y);

                return Math.Max(edge.From.X, edge.To.X);
            }

            on.Sort((one, other) => low(one).CompareTo(low(other)));

            for (int i = 1; i < on.Count; i++)
            {
                if (low(on[i]) <= high(on[i - 1]))
                    return false;
            }

            return true;
        }

        ///<summary>
        ///Which line an edge lies on, the same for every edge on that line whichever way it runs: its
        ///direction in lowest terms, turned to point right or straight up, and how far the line is from the
        ///origin in units of that direction - a whole number, so two edges are on one line exactly or not.
        ///
        ///In 128 bits, because a slanted edge reaching across a 32-bit layout makes products a bit wider
        ///than 64, and two different lines wrapped to one would be added up together.
        ///</summary>
        private static (long, long, Int128) lineOf(Edge edge)
        {
            long dx = edge.DeltaX;
            long dy = edge.DeltaY;

            long divisor = greatestCommonDivisor(Math.Abs(dx), Math.Abs(dy));

            dx /= divisor;
            dy /= divisor;

            if (dx < 0 || (dx == 0 && dy < 0))
            {
                dx = -dx;
                dy = -dy;
            }

            Int128 across = ((Int128)dx * edge.From.Y) - ((Int128)dy * edge.From.X);

            return (dx, dy, across);
        }

        private static long greatestCommonDivisor(long one, long other)
        {
            while (other != 0)
            {
                long rest = one % other;

                one = other;
                other = rest;
            }

            return one;
        }

        ///<summary>
        ///The edges of one line once they are added up, in order along it.
        ///
        ///Each counts one along the stretch it covers, plus or minus by which way it runs, and a run is kept
        ///wherever the count is not nothing, pointing the way its sign says. Two pieces of one side meet end
        ///to end with the same sign and come out as one edge; a channel's two sides cancel to nothing; and
        ///where an edge runs on past where the slit along it ends, what is left of it is the part that is
        ///boundary.
        ///</summary>
        private static List<Edge> runsAlong(List<Edge> edges, List<int> on)
        {
            //Where along the line a corner is: its X, or its Y on an upright line. Either one rises the same
            //way along every edge of the line, which is all a position along it has to do.
            bool upright = edges[on[0]].DeltaX == 0;

            long along(Element.Point point)
            {
                if (upright)
                    return point.Y;

                return point.X;
            }

            var corners = new Dictionary<long, Element.Point>();
            var steps = new List<(long At, int Change)>();

            foreach (int index in on)
            {
                var edge = edges[index];

                long from = along(edge.From);
                long to = along(edge.To);

                corners[from] = edge.From;
                corners[to] = edge.To;

                if (from < to)
                {
                    steps.Add((from, 1));
                    steps.Add((to, -1));
                }
                else
                {
                    steps.Add((to, -1));
                    steps.Add((from, 1));
                }
            }

            steps.Sort((one, other) => one.At.CompareTo(other.At));

            var runs = new List<Edge>();

            int count = 0;
            long start = 0;
            int next = 0;

            while (next < steps.Count)
            {
                long at = steps[next].At;
                int before = Math.Sign(count);

                //Every change at one place taken together, so a run that ends where another begins with the
                //same sign carries straight on through.
                while (next < steps.Count && steps[next].At == at)
                {
                    count += steps[next].Change;
                    next++;
                }

                int after = Math.Sign(count);

                if (after == before)
                    continue;

                if (before > 0)
                    runs.Add(new Edge(corners[start], corners[at]));
                else if (before < 0)
                    runs.Add(new Edge(corners[at], corners[start]));

                start = at;
            }

            return runs;
        }

        ///<summary>
        ///Whether a line's runs are the edges it already had, the same corners the same way round.
        ///
        ///Both are in order along the line - the edges put so by <see cref="inOrderApart"/> - so they either
        ///match one for one or are not the same.
        ///</summary>
        private static bool sameAs(List<Edge> runs, List<Edge> edges, List<int> on)
        {
            if (runs.Count != on.Count)
                return false;

            for (int i = 0; i < runs.Count; i++)
            {
                var had = edges[on[i]];

                if (!same(runs[i].From, had.From) || !same(runs[i].To, had.To))
                    return false;
            }

            return true;
        }

        private static void appendRing(List<Edge> edges, List<Element.Point> ring, bool wantCounterClockwise)
        {
            var points = new List<Element.Point>(ring);

            //A closing point repeated from the first is how GDSII writes a ring and is not a corner.
            if (points.Count > 1 && points[0].X == points[^1].X && points[0].Y == points[^1].Y)
                points.RemoveAt(points.Count - 1);

            if (points.Count < 3)
                return;

            if (isCounterClockwise(points) != wantCounterClockwise)
                points.Reverse();

            for (int i = 0; i < points.Count; i++)
            {
                var from = points[i];
                var to = points[(i + 1) % points.Count];

                //A zero-length edge has no direction, so it can neither face anything nor be faced.
                if (from.X == to.X && from.Y == to.Y)
                    continue;

                edges.Add(new Edge(from, to));
            }
        }

        ///<summary>
        ///Whether a ring runs counter-clockwise, by the sign of twice its area.
        ///
        ///Measured relative to the first corner, for the reason <see cref="Measure.AreaOf"/> gives: a shape
        ///a few hundred units across can sit a hundred million out from the origin, and the products of raw
        ///coordinates there lose the precision the answer is made of.
        ///</summary>
        private static bool isCounterClockwise(List<Element.Point> ring)
        {
            var origin = ring[0];

            double twice = 0;

            for (int i = 0; i < ring.Count; i++)
            {
                var here = ring[i];
                var next = ring[(i + 1) % ring.Count];

                double x1 = here.X - (double)origin.X;
                double y1 = here.Y - (double)origin.Y;
                double x2 = next.X - (double)origin.X;
                double y2 = next.Y - (double)origin.Y;

                twice += (x1 * y2) - (x2 * y1);
            }

            return twice > 0;
        }

        #endregion **************************************************************************



        #region The checks ******************************************************************

        ///<summary>
        ///Where a layer comes closer to itself across empty space than the limit - its spacing.
        ///
        ///Two edges pair when each lies on the *outside* of the other: they face each other through the
        ///ground neither covers. An edge and the one across a piece of material from it face each other
        ///through the material instead, which is a width and not a spacing.
        ///</summary>
        public static List<DrcEdgePair> Space(
            IEnumerable<IReadOnlyList<Element.Point>> shapes,
            long limit,
            DrcMetric metric = DrcMetric.Euclidean)
        {
            return Space(Booleans.AsRings(shapes), limit, metric);
        }

        ///<summary>The same over a layer kept as rings - see <see cref="DrcChecks"/> on why a check has both.</summary>
        public static List<DrcEdgePair> Space(IEnumerable<Booleans.Ring> layer, long limit, DrcMetric metric = DrcMetric.Euclidean)
        {
            var rings = Booleans.Merge(layer);

            return pairs(EdgesOf(rings), rings, limit, metric, outward: true);
        }

        ///<summary>
        ///Where a layer is narrower than the limit - its width.
        ///
        ///The same walk with the facing reversed: two edges pair when each lies on the *inside* of the
        ///other, so the gap between them is material rather than ground.
        ///</summary>
        public static List<DrcEdgePair> Width(
            IEnumerable<IReadOnlyList<Element.Point>> shapes,
            long limit,
            DrcMetric metric = DrcMetric.Euclidean)
        {
            return Width(Booleans.AsRings(shapes), limit, metric);
        }

        ///<summary>The same over a layer kept as rings.</summary>
        public static List<DrcEdgePair> Width(IEnumerable<Booleans.Ring> layer, long limit, DrcMetric metric = DrcMetric.Euclidean)
        {
            var rings = Booleans.Merge(layer);

            return pairs(EdgesOf(rings), rings, limit, metric, outward: false);
        }

        ///<summary>
        ///The walk itself.
        ///
        ///Each edge asks the index for what is near it and tests only those, and each pair is tested once - a
        ///facing is mutual, and reporting both halves would double every count and disagree with every other
        ///tool. What is reported is the pair as KLayout reports it: the part of each edge that is too close to
        ///the other.
        ///</summary>
        private static List<DrcEdgePair> pairs(List<Edge> edges, List<Booleans.Ring> rings, long limit, DrcMetric metric, bool outward)
        {
            var found = new List<DrcEdgePair>();

            if (edges.Count == 0 || limit <= 0)
                return found;

            var grid = EdgeGrid.Of(edges, limit);

            //The box round each ring, so a point asked about is cast only against the rings it could be in.
            var boxes = new Bounds[rings.Count];

            for (int r = 0; r < rings.Count; r++)
                boxes[r] = Bounds.Of(rings[r].Boundary);

            for (int i = 0; i < edges.Count; i++)
            {
                foreach (int j in grid.Near(edges[i], limit))
                {
                    //Once per pair. The index offers both directions, so the lower number keeps it.
                    if (j <= i)
                        continue;

                    //A wedge closes to a point, so it is a width at any limit and there is no distance to
                    //measure - what is reported is the run over which it is too narrow.
                    if (Corner(edges[i], edges[j]) is double interior)
                    {
                        if (closes(interior, outward))
                            found.Add(wedge(edges[i], edges[j], interior, limit));

                        continue;
                    }

                    //Facing each other and too close - and at the limit is not too close. A minimum of 140 forbids
                    //139 and allows 140, and a checker that reported every minimum-width wire would be useless -
                    //drawing to minimum is what layout is. Unlike the sizing checks this is exact at every limit,
                    //odd or even.
                    if (!Facing(edges[i], edges[j], limit, metric, outward, out var partOfA, out var partOfB))
                        continue;

                    double distance = Measured(partOfA, partOfB, metric, out var span);

                    //And what lies between them has to be what the check is about: material for a width,
                    //empty ground for a spacing. Two edges on opposite sides of a shape with a third
                    //between them face each other and are the width of nothing. Asked only of a pair that is
                    //too close, since the answer walks the layer's rings.
                    if (!Sees(partOfA, partOfB, span, rings, boxes, grid, outward))
                        continue;

                    found.Add(new DrcEdgePair
                    {
                        AFrom = partOfA.From,
                        ATo = partOfA.To,
                        BFrom = partOfB.From,
                        BTo = partOfB.To,
                        Distance = distance
                    });
                }
            }

            return found;
        }

        ///<summary>
        ///Whether the two edges of a corner close to a wedge - a width at any limit - or, for a spacing, whether
        ///the ground between them does.
        ///
        ///**Decided by the corner's own interior angle, which took two goes to get right.** Excluding every pair
        ///of edges that meet stops a plain square reporting four width faults at its own corners - and it also
        ///loses a sharp spike, where the two edges genuinely do close to a point and the material between them
        ///genuinely is narrower than any limit. KLayout, asked the same question, reports three faults on a
        ///wedge and none on a square.
        ///
        ///So a right angle or wider is a corner, anything narrower is a wedge and a wedge is a width. The
        ///threshold is ninety degrees, which is KLayout's own default for `angle_limit` and what makes a
        ///rectilinear layout report nothing extra. It is the answer <see cref="Facing"/> gives as well, asked
        ///of a corner: two edges that meet are within any limit of each other at the corner itself.
        ///</summary>
        private static bool closes(double interior, bool outward)
        {
            if (outward)
                return 360 - interior < AngleLimit;

            return interior < AngleLimit;
        }

        ///<summary>
        ///Whether two edges that do not meet face each other within the limit, and the part of each that does.
        ///
        ///**Each has to be on the correct side of the other, not just one of them.** A wire running past the end
        ///of another has one edge facing it and is not facing back, and counting that pair reports a gap that is
        ///nobody's spacing. Material is on the left of every edge, so for a width the other edge lies to the
        ///left, and for a spacing to the *right*.
        ///
        ///**Asked of the part of each edge within the limit, which is how KLayout asks it**, and what it answers
        ///with is those parts. It was asked of each edge's midpoint until 2026-11-02, the cheapest thing that
        ///answers it and the same answer wherever the two run alongside each other or are short beside the
        ///limit. Where they are slanted at a limit near a shape's own size it was not: two overlapping
        ///octagons from `gds generate` are joined by a V-shaped neck, and a side of the V reaches across the neck
        ///to the other octagon's lower side, 192 nm corner to corner inside the shape, while the middle of that side
        ///lies beyond the V side's line. KLayout reports the pair, from the part of the side that is inside the
        ///limit and inside the line; the midpoint never did.
        ///
        ///**And each is held to the angle limit, met or not**, the way KLayout holds it: one edge within ninety
        ///degrees of facing the other squarely, which is nothing across a wire and ninety between a side and an
        ///end. Held only where edges met, it let a corner's two sides through as soon as anything came between
        ///them: a step beside a corner where two overlapping rectangles' tops are 10 nm apart, every tread of
        ///IHP's 5 nm poly stairs against the risers further along, and on the bundled transistor the stripe's
        ///side against the base's bottom, 460 nm straight down through poly - two edges at ninety degrees that
        ///KLayout never pairs. Asked in whole numbers, because slanted edges a hundredth of a degree either side
        ///of ninety are common, and a rounding there is a different answer.
        ///</summary>
        private static bool Facing(Edge a, Edge b, long limit, DrcMetric metric, bool outward, out Edge partOfA, out Edge partOfB)
        {
            partOfA = a;
            partOfB = b;

            //Pointing more against each other than along: under ninety degrees from facing squarely.
            if (dotSign(a, b) >= 0)
                return false;

            //KLayout keeps the part on the right of an edge. The material is on the left of every edge here, so
            //a width turns both round, and a spacing - whose ground is on the right already - takes them as
            //they are. Either way they are then the very edges KLayout measures, so even its rounding lands
            //where it lands there.
            var one = a;
            var other = b;

            if (!outward)
            {
                one = a.Reversed;
                other = b.Reversed;
            }

            if (!nearPart(one, other, limit, metric, out var onOther) || !nearPart(other, one, limit, metric, out var onOne))
                return false;

            if (!outward)
            {
                onOne = onOne.Reversed;
                onOther = onOther.Reversed;
            }

            partOfA = onOne;
            partOfB = onOther;

            return true;
        }

        ///<summary>
        ///Beyond this, two edges meeting at a point are a corner rather than a narrowing.
        ///
        ///Ninety degrees, which is KLayout's default and the reason a rectilinear layout - which is nearly
        ///all layout - reports nothing at its own corners.
        ///</summary>
        private const double AngleLimit = 90;

        ///<summary>
        ///The interior angle where two edges meet, in degrees, or null when they do not meet.
        ///
        ///Measured through the material. Every ring is wound so the inside is on the left, so the turn from
        ///one edge's direction to the next's is the *exterior* angle and the interior is what is left of a
        ///straight line. A square corner comes out at ninety; the tip of a wedge at whatever it is.
        ///</summary>
        private static double? Corner(Edge a, Edge b)
        {
            //In ring order: a arrives at the corner and b leaves it, or the other way round.
            if (same(a.To, b.From))
                return interiorAngle(a, b);

            if (same(b.To, a.From))
                return interiorAngle(b, a);

            //Two edges meeting head to head or tail to tail belong to different rings that touch, which is
            //not a corner of anything - they are two shapes at a point and the ordinary facing test decides.
            if (same(a.From, b.From) || same(a.To, b.To))
                return null;

            return null;
        }

        private static double interiorAngle(Edge arriving, Edge leaving)
        {
            double cross = (arriving.DeltaX * (double)leaving.DeltaY) - (arriving.DeltaY * (double)leaving.DeltaX);
            double dot = (arriving.DeltaX * (double)leaving.DeltaX) + (arriving.DeltaY * (double)leaving.DeltaY);

            double turn = Math.Atan2(cross, dot) * 180 / Math.PI;

            return 180 - turn;
        }

        private static bool same(Element.Point one, Element.Point other)
        {
            return one.X == other.X && one.Y == other.Y;
        }

        private static (double X, double Y) midpoint(Edge edge)
        {
            return (edge.From.X + (edge.DeltaX / 2.0), edge.From.Y + (edge.DeltaY / 2.0));
        }

        #endregion **************************************************************************



        #region The part within the limit ***************************************************

        ///<summary>
        ///The part of another edge that lies inside this one - to its right - and within the limit of it, or
        ///false when no part does.
        ///
        ///**KLayout's own, step for step**: `euclidian_near_part_of_edge`, and `var_near_part_of_edge` for the
        ///other two metrics, from its dbEdgePairRelations.cc as of 0.30.9, rounding and all. Where two engines
        ///are to agree pair for pair, a part that comes out a database unit long has to come out that long in
        ///both, not merely nearly - and where a slanted edge is cut at another's line, the cut lands on the grid
        ///only by rounding.
        ///
        ///Inside is to the right, as KLayout has it; <see cref="Facing"/> turns a width's edges round first.
        ///</summary>
        private static bool nearPart(Edge edge, Edge other, long limit, DrcMetric metric, out Edge part)
        {
            part = other;

            int fromSide = sideOf(edge, other.From);
            int toSide = sideOf(edge, other.To);

            //On the line counts as inside only between two edges that touch without being one corner's two
            //sides - on a merged layer, two shapes meeting at a point. KLayout's default, and the reason it
            //reports kissing corners as a spacing of nothing.
            int outside = -1;

            if (touchesAside(edge, other))
                outside = 0;

            if (fromSide > outside && toSide > outside)
                return false;

            var inside = other;

            if (toSide > outside)
                inside = new Edge(other.From, cutPoint(other, edge));
            else if (fromSide > outside)
                inside = new Edge(cutPoint(other, edge), other.To);

            if (metric == DrcMetric.Euclidean)
                return euclideanPart(edge, inside, limit, outside, out part);

            //The square metric is the projected one with the edge carried on past each end by the limit.
            long carried = 0;

            if (metric == DrcMetric.Square)
                carried = limit;

            return projectedPart(edge, inside, limit, carried, outside, out part);
        }

        ///<summary>
        ///The part of an edge, already cut to the inside of another, that comes within the limit of it measured
        ///Euclidean: the stretch inside the band the limit deep along the other edge, joined to whatever lies
        ///inside the circle the limit round either of its ends.
        ///</summary>
        private static bool euclideanPart(Edge edge, Edge other, long limit, int outside, out Edge part)
        {
            part = other;

            double reach = limit;

            //Cut down to a point, which only a cut rounded onto the corner does.
            if (same(other.From, other.To))
                return pointWithin(edge, other.From, reach, outside);

            double low = double.MaxValue;
            double high = -double.MaxValue;

            if (parallel(edge, other))
            {
                //Alongside, it is all in the band or none of it; the ends below say how far along. Measured
                //rounded to a whole unit, as KLayout measures it.
                if (Math.Abs((double)distanceFrom(edge, other.From)) >= reach)
                    return false;
            }
            else
            {
                double scale = 1.0 / lengthOf(edge);
                double normalX = scale * edge.DeltaY;
                double normalY = -scale * edge.DeltaX;

                //Where the other edge crosses this one's line, and the line the limit inside it.
                double offsetX = edge.From.X + (normalX * reach);
                double offsetY = edge.From.Y + (normalY * reach);

                double across = crossOf(other.DeltaX, other.DeltaY, edge.DeltaX, edge.DeltaY);
                double atOffset = crossOf(offsetX - other.From.X, offsetY - other.From.Y, edge.DeltaX, edge.DeltaY) / across;
                double atLine = crossOf((long)edge.From.X - other.From.X, (long)edge.From.Y - other.From.Y, edge.DeltaX, edge.DeltaY) / across;

                if (atOffset > atLine)
                    (atOffset, atLine) = (atLine, atOffset);

                if (dotSign(edge, other) == 0)
                {
                    //Square to this edge, so wherever it passes between this edge's ends the band is all of it.
                    if (sideOf(other, edge.From) * sideOf(other, edge.To) <= 0)
                    {
                        low = atOffset;
                        high = atLine;
                    }
                }
                else
                {
                    //And where it crosses the square through each end of this edge.
                    double turned = crossOf(other.DeltaX, other.DeltaY, normalX, normalY);
                    double atFrom = crossOf((long)edge.From.X - other.From.X, (long)edge.From.Y - other.From.Y, normalX, normalY) / turned;
                    double atTo = crossOf((long)edge.To.X - other.From.X, (long)edge.To.Y - other.From.Y, normalX, normalY) / turned;

                    if (atFrom > atTo)
                        (atFrom, atTo) = (atTo, atFrom);

                    double from = larger(atOffset, atFrom);
                    double to = smaller(atLine, atTo);

                    if (from <= to)
                    {
                        low = from;
                        high = to;
                    }
                }
            }

            //And the circle round each end of this edge.
            for (int i = 0; i < 2; i++)
            {
                var end = edge.From;

                if (i == 1)
                    end = edge.To;

                double lengthSquared = squaredLengthOf(other);
                double b = dotOf((long)other.From.X - end.X, (long)other.From.Y - end.Y, other) / lengthSquared;
                double c = (squaredDistance(other.From, end) - (reach * reach)) / lengthSquared;
                double s = (b * b) - c;

                if (s >= -Epsilon)
                {
                    low = smaller(low, -b - Math.Sqrt(s));
                    high = larger(high, -b + Math.Sqrt(s));
                }
            }

            return partBetween(other, low, high, false, out part);
        }

        ///<summary>Whether a point inside an edge comes within the limit of it, measured Euclidean.</summary>
        private static bool pointWithin(Edge edge, Element.Point point, double reach, int outside)
        {
            if (sideOf(edge, point) > outside)
                return false;

            double lengthSquared = squaredLengthOf(edge);
            double b = dotOf((long)edge.From.X - point.X, (long)edge.From.Y - point.Y, edge) / lengthSquared;
            double c = (squaredDistance(edge.From, point) - (reach * reach)) / lengthSquared;
            double s = (b * b) - c;

            if (s < -Epsilon)
                return false;

            double low = larger(0.0, -b - Math.Sqrt(s));
            double high = smaller(1.0, -b + Math.Sqrt(s));

            return low <= high;
        }

        ///<summary>
        ///The part of an edge, already cut to the inside of another, that comes within the limit of it measured
        ///square to it: inside the band the limit deep, and between the lines square to the other edge through
        ///its two ends - carried on past them by the limit for the square metric, and not at all for projection.
        ///</summary>
        private static bool projectedPart(Edge edge, Edge other, long limit, long carried, int outside, out Edge part)
        {
            part = other;

            double reach = limit;
            double past = carried * lengthOf(edge);

            if (same(other.From, other.To))
            {
                var point = other.From;

                if (sideOf(edge, point) > outside)
                    return false;

                if (dotOf((long)point.X - edge.From.X, (long)point.Y - edge.From.Y, edge) < -past)
                    return false;

                if (dotOf((long)edge.To.X - point.X, (long)edge.To.Y - point.Y, edge) < -past)
                    return false;

                return distanceFrom(edge, point) > -reach;
            }

            double low = double.MinValue;
            double high = double.MaxValue;

            double scale = 1.0 / lengthOf(edge);
            double alongX = scale * edge.DeltaX;
            double alongY = scale * edge.DeltaY;
            double normalX = scale * edge.DeltaY;
            double normalY = -scale * edge.DeltaX;

            if (parallel(edge, other))
            {
                if (Math.Abs((double)distanceFrom(edge, other.From)) >= reach)
                    return false;
            }
            else
            {
                double offsetX = edge.From.X + (normalX * reach);
                double offsetY = edge.From.Y + (normalY * reach);

                double across = crossOf(other.DeltaX, other.DeltaY, edge.DeltaX, edge.DeltaY);
                double atOffset = crossOf(offsetX - other.From.X, offsetY - other.From.Y, edge.DeltaX, edge.DeltaY) / across;
                double atLine = crossOf((long)edge.From.X - other.From.X, (long)edge.From.Y - other.From.Y, edge.DeltaX, edge.DeltaY) / across;

                if (atOffset > atLine)
                    (atOffset, atLine) = (atLine, atOffset);

                low = atOffset;
                high = atLine;
            }

            //Square to this edge, the other one faces it at a single point, which is allowed to be all there is.
            bool single = false;

            if (dotSign(edge, other) == 0)
            {
                if (dotOf((long)other.From.X - edge.From.X, (long)other.From.Y - edge.From.Y, edge) < -past
                    || dotOf((long)edge.To.X - other.From.X, (long)edge.To.Y - other.From.Y, edge) < -past)
                    return false;

                double at = ((((double)edge.From.X - other.From.X) * other.DeltaX) + (((double)edge.From.Y - other.From.Y) * other.DeltaY)) / squaredLengthOf(other);
                double spread = carried / lengthOf(other);

                low = at - spread;
                high = at + spread;

                single = true;
            }
            else
            {
                double turned = crossOf(other.DeltaX, other.DeltaY, normalX, normalY);
                double atFrom = crossOf((long)edge.From.X - other.From.X - (alongX * carried), (long)edge.From.Y - other.From.Y - (alongY * carried), normalX, normalY) / turned;
                double atTo = crossOf((long)edge.To.X - other.From.X + (alongX * carried), (long)edge.To.Y - other.From.Y + (alongY * carried), normalX, normalY) / turned;

                if (atFrom > atTo)
                    (atFrom, atTo) = (atTo, atFrom);

                low = larger(low, atFrom);
                high = smaller(high, atTo);
            }

            return partBetween(other, low, high, single, out part);
        }

        ///<summary>
        ///The stretch of an edge between two fractions of the way along it, clipped to the edge, or false when
        ///nothing of it is left - a single point counting only where it is allowed to.
        ///</summary>
        private static bool partBetween(Edge other, double low, double high, bool single, out Edge part)
        {
            part = other;

            low = larger(0.0, low);
            high = smaller(1.0, high);

            if (single)
            {
                if (low > high + Epsilon)
                    return false;
            }
            else if (low > high - Epsilon)
                return false;

            part = new Edge(pointAt(other, low), pointAt(other, high));

            return true;
        }

        ///<summary>
        ///Whether two edges touch without being one corner's two sides, when one ends on the other's line - the
        ///case where on the line counts as inside. KLayout's `include_zero_flag` under its default,
        ///`IncludeZeroDistanceWhenTouching`.
        ///</summary>
        private static bool touchesAside(Edge edge, Edge other)
        {
            int fromSide = sideOf(edge, other.From);
            int toSide = sideOf(edge, other.To);

            if (fromSide == 0 && toSide == 0)
                return intersects(edge, other);

            if ((fromSide == 0 || toSide == 0) && !same(edge.From, other.To) && !same(edge.To, other.From))
                return intersects(edge, other);

            return false;
        }

        ///<summary>Whether two edges share a point, ends included. KLayout's `Edge::intersect`.</summary>
        private static bool intersects(Edge one, Edge other)
        {
            if (Math.Max(one.From.X, one.To.X) < Math.Min(other.From.X, other.To.X)
                || Math.Max(other.From.X, other.To.X) < Math.Min(one.From.X, one.To.X)
                || Math.Max(one.From.Y, one.To.Y) < Math.Min(other.From.Y, other.To.Y)
                || Math.Max(other.From.Y, other.To.Y) < Math.Min(one.From.Y, one.To.Y))
                return false;

            //Two upright or flat edges whose boxes touch share a point.
            if ((one.DeltaX == 0 || one.DeltaY == 0) && (other.DeltaX == 0 || other.DeltaY == 0))
                return true;

            return crossedBy(one, other) && crossedBy(other, one);
        }

        ///<summary>Whether the other edge's ends lie on opposite sides of this one's line, or one of them on it.</summary>
        private static bool crossedBy(Edge line, Edge other)
        {
            int fromSide = sideOf(line, other.From);
            int toSide = sideOf(line, other.To);

            if (fromSide == 0 || toSide == 0)
                return true;

            return fromSide != toSide;
        }

        ///<summary>
        ///Where one edge's line crosses another's, on the grid: KLayout's `Edge::cut_point`, worked out along the
        ///second edge and rounded half away from zero.
        ///</summary>
        private static Element.Point cutPoint(Edge cut, Edge line)
        {
            Int128 turn = ((Int128)line.DeltaX * cut.DeltaY) - ((Int128)line.DeltaY * cut.DeltaX);
            Int128 offset = (((Int128)line.From.X - cut.From.X) * cut.DeltaY) - (((Int128)line.From.Y - cut.From.Y) * cut.DeltaX);

            double fraction = (double)offset / (double)turn;

            return new Element.Point
            {
                X = (int)(line.From.X - roundedAway(line.DeltaX * fraction)),
                Y = (int)(line.From.Y - roundedAway(line.DeltaY * fraction))
            };
        }

        ///<summary>The point a fraction of the way along an edge, on the grid the way KLayout puts it there.</summary>
        private static Element.Point pointAt(Edge edge, double fraction)
        {
            return new Element.Point
            {
                X = (int)(edge.From.X + roundedAway(edge.DeltaX * fraction)),
                Y = (int)(edge.From.Y + roundedAway(edge.DeltaY * fraction))
            };
        }

        ///<summary>Rounded half away from zero, which is how KLayout puts a coordinate on the grid.</summary>
        private static long roundedAway(double value)
        {
            if (value > 0)
                return (long)(value + 0.5);

            return (long)(value - 0.5);
        }

        ///<summary>
        ///How far a point is from an edge's line, positive on its left, rounded to a whole unit - KLayout's
        ///`Edge::distance`, which a parallel pair is measured by.
        ///</summary>
        private static long distanceFrom(Edge edge, Element.Point point)
        {
            Int128 cross = ((Int128)edge.DeltaX * ((long)point.Y - edge.From.Y)) - ((Int128)edge.DeltaY * ((long)point.X - edge.From.X));

            return roundedAway((double)cross / lengthOf(edge));
        }

        ///<summary>Which side of an edge's line a point is on: 1 left, -1 right, 0 on it. Exact.</summary>
        private static int sideOf(Edge edge, Element.Point point)
        {
            Int128 cross = ((Int128)edge.DeltaX * ((long)point.Y - edge.From.Y)) - ((Int128)edge.DeltaY * ((long)point.X - edge.From.X));

            return Int128.Sign(cross);
        }

        ///<summary>The sign of two edges' dot product: negative where they point more against each other than along. Exact.</summary>
        private static int dotSign(Edge one, Edge other)
        {
            return Int128.Sign(((Int128)one.DeltaX * other.DeltaX) + ((Int128)one.DeltaY * other.DeltaY));
        }

        ///<summary>Whether two edges run the same way or opposite ways along parallel lines. Exact.</summary>
        private static bool parallel(Edge one, Edge other)
        {
            return ((Int128)one.DeltaX * other.DeltaY) - ((Int128)one.DeltaY * other.DeltaX) == 0;
        }

        private static double crossOf(double ax, double ay, double bx, double by)
        {
            return (ax * by) - (ay * bx);
        }

        ///<summary>A whole-number vector dotted with an edge's direction, as KLayout's integer `sprod` does it.</summary>
        private static double dotOf(long x, long y, Edge edge)
        {
            return (x * edge.DeltaX) + (y * edge.DeltaY);
        }

        private static double squaredLengthOf(Edge edge)
        {
            return ((double)edge.DeltaX * edge.DeltaX) + ((double)edge.DeltaY * edge.DeltaY);
        }

        private static double lengthOf(Edge edge)
        {
            return Math.Sqrt(squaredLengthOf(edge));
        }

        private static double squaredDistance(Element.Point one, Element.Point other)
        {
            double dx = (double)other.X - one.X;
            double dy = (double)other.Y - one.Y;

            return (dx * dx) + (dy * dy);
        }

        ///<summary>C++'s std::max, which keeps the first when the second is not a number - KLayout's arithmetic relies on it near a tangent.</summary>
        private static double larger(double first, double second)
        {
            if (first < second)
                return second;

            return first;
        }

        ///<summary>C++'s std::min, kept for the same reason.</summary>
        private static double smaller(double first, double second)
        {
            if (second < first)
                return second;

            return first;
        }

        ///<summary>KLayout's `db::epsilon`, what its near-part arithmetic forgives.</summary>
        private const double Epsilon = 1e-10;

        #endregion **************************************************************************



        #region Measuring *******************************************************************

        ///<summary>
        ///What a pair was measured across, as the four corners of the ground it crosses: the nearest point on
        ///each side, each given twice, or the two runs of a projected pair.
        ///</summary>
        private readonly record struct Span(Element.Point AFrom, Element.Point ATo, Element.Point BFrom, Element.Point BTo);

        ///<summary>
        ///The pair a wedge is reported as: the two edges from the corner out to where it reaches the limit.
        ///
        ///A wedge of interior angle t is 2*d*sin(t/2) across at distance d from its point, so it is under
        ///the limit until d reaches limit / (2 sin(t/2)). Past that it is wide enough, and reporting the
        ///whole of both edges would mark ground that is not at fault.
        ///</summary>
        private static DrcEdgePair wedge(Edge a, Edge b, double interior, long limit)
        {
            //Which end of each is the corner they share.
            bool aEnds = same(a.To, b.From) || same(a.To, b.To);

            Element.Point corner = a.From;
            Element.Point farOnA = a.To;

            if (aEnds)
            {
                corner = a.To;
                farOnA = a.From;
            }

            //And the same question of b, asked against the corner rather than against a's own ends.
            Element.Point farOnB = b.From;

            if (same(b.From, corner))
                farOnB = b.To;

            double half = Math.Sin(interior / 2 * Math.PI / 180);

            double reach = limit;

            if (half > 0)
                reach = limit / (2 * half);

            return new DrcEdgePair
            {
                AFrom = corner,
                ATo = along(corner, farOnA, reach),
                BFrom = along(corner, farOnB, reach),
                BTo = corner,

                //It closes to a point, so nothing is the honest answer to how far apart they get.
                Distance = 0
            };
        }

        ///<summary>A point the given distance from one corner towards another, or the other corner itself.</summary>
        private static Element.Point along(Element.Point from, Element.Point towards, double distance)
        {
            double dx = (double)towards.X - from.X;
            double dy = (double)towards.Y - from.Y;

            double length = Math.Sqrt((dx * dx) + (dy * dy));

            if (length <= distance || length == 0)
                return towards;

            return new Element.Point
            {
                X = (int)Math.Round(from.X + (dx / length * distance)),
                Y = (int)Math.Round(from.Y + (dy / length * distance))
            };
        }

        ///<summary>
        ///Whether the ground between two edges is what the check is about.
        ///
        ///**The over-report this was written to remove.** A facing pair is any two edges on opposite sides
        ///of the material, which on a shape with a third edge between them is not a width of anything -
        ///measured against KLayout on the bundled transistor at a limit twice the shape's own size, this
        ///engine reported twelve pairs where KLayout reported seven, and the five extra were all of that
        ///kind.
        ///
        ///**Tested along the line the distance was measured across** - nearest point to nearest point, or
        ///across the middle of the run two edges face - which may lie along the boundary but may not cross to
        ///the wrong side of it anywhere. It was tested at one point, halfway between the two edges' own
        ///middles, until IHP's standard cells showed where that lands somewhere else: a poly or metal jog, a
        ///corner diagonally across it from another and a side running away from each, measured 141 nm
        ///corner to corner straight through the jog's own poly while the point between the two sides'
        ///middles sat in the empty ground beside it. Forty-two such spacing faults in twenty-two of IHP's 84
        ///cells, where KLayout reports none.
        ///
        ///**Along the boundary is allowed** because a plain narrow rectangle is measured corner to corner
        ///along its own end, where a single point on the line is answering a question with no answer - it
        ///stopped being reported the first time the test went there. So the line is cut wherever it meets an
        ///edge, and the middle of each stretch that is not along one is asked: each of those is plainly on
        ///one side or the other.
        ///
        ///**And two edges running alongside each other are asked across the middle of that run as well.**
        ///They are nearest all along it, and the line is drawn at one end - where it can run along the foot
        ///of a third shape standing between them, touching it and never crossing it. Across the middle it
        ///crosses, and the pair is two shapes with a wall between, which KLayout does not report either.
        ///</summary>
        private static bool Sees(Edge a, Edge b, Span span, List<Booleans.Ring> rings, Bounds[] boxes, EdgeGrid grid, bool outward)
        {
            //Two edges touching measure nothing to walk along, so the place to ask is the one this always
            //asked: halfway between their middles. Twice each side's middle, so the sums stay whole.
            long acrossX = (long)span.BFrom.X + span.BTo.X - span.AFrom.X - span.ATo.X;
            long acrossY = (long)span.BFrom.Y + span.BTo.Y - span.AFrom.Y - span.ATo.Y;

            if (acrossX == 0 && acrossY == 0)
            {
                var (ax, ay) = midpoint(a);
                var (bx, by) = midpoint(b);

                var middle = new Element.Point
                {
                    X = (int)Math.Round((ax + bx) / 2),
                    Y = (int)Math.Round((ay + by) / 2)
                };

                return Covers(rings, boxes, middle.X, middle.Y) != outward;
            }

            if (!clearAcross(span, rings, boxes, grid, outward))
                return false;

            //The run they face, unless that is what was measured already.
            if (!projected(a, b, out _, out var facing) || facing == span)
                return true;

            return clearAcross(facing, rings, boxes, grid, outward);
        }

        ///<summary>
        ///Whether the line from the middle of a span's one side to the middle of its other stays off the
        ///wrong side of the boundary: a width wants material all the way across, a spacing wants ground.
        ///</summary>
        private static bool clearAcross(Span span, List<Booleans.Ring> rings, Bounds[] boxes, EdgeGrid grid, bool outward)
        {
            double fromX = (span.AFrom.X + (double)span.ATo.X) / 2;
            double fromY = (span.AFrom.Y + (double)span.ATo.Y) / 2;
            double toX = (span.BFrom.X + (double)span.BTo.X) / 2;
            double toY = (span.BFrom.Y + (double)span.BTo.Y) / 2;

            foreach (var (x, y) in grid.OffTheBoundary(fromX, fromY, toX, toY))
            {
                if (Covers(rings, boxes, x, y) == outward)
                    return false;
            }

            return true;
        }

        ///<summary>
        ///Whether a point lies on the layer, holes taken off.
        ///
        ///Ray casting to the right, counting crossings: odd means inside the outer ring, and a hole is
        ///counted the same way and flips it back out again.
        ///</summary>
        public static bool Covers(List<Booleans.Ring> rings, Element.Point point)
        {
            return Covers(rings, null, point.X, point.Y);
        }

        ///<summary>
        ///The same at a point between the grid's own, such as the middle of a measured line - and, given the
        ///box round each ring, cast only against the rings whose box holds the point, since no other can.
        ///</summary>
        private static bool Covers(List<Booleans.Ring> rings, Bounds[]? boxes, double x, double y)
        {
            for (int r = 0; r < rings.Count; r++)
            {
                var ring = rings[r];

                if (boxes is not null && (x < boxes[r].Left || x > boxes[r].Right || y < boxes[r].Bottom || y > boxes[r].Top))
                    continue;

                if (!crosses(ring.Boundary, x, y))
                    continue;

                bool inHole = false;

                foreach (var hole in ring.Holes)
                {
                    if (crosses(hole, x, y))
                    {
                        inHole = true;

                        break;
                    }
                }

                if (!inHole)
                    return true;
            }

            return false;
        }

        private static bool crosses(List<Element.Point> ring, double x, double y)
        {
            bool inside = false;

            for (int i = 0; i < ring.Count; i++)
            {
                var one = ring[i];
                var other = ring[(i + 1) % ring.Count];

                //Half-open on the vertical, so a ray passing exactly through a corner counts it once.
                if ((one.Y > y) == (other.Y > y))
                    continue;

                double at = one.X + ((y - one.Y) / ((double)other.Y - one.Y) * ((double)other.X - one.X));

                if (at > x)
                    inside = !inside;
            }

            return inside;
        }

        ///<summary>
        ///How far apart the facing parts of two edges are, by the metric asked for, and the line that was
        ///measured across.
        ///
        ///Only ever an answer: whether the two are a pair at all was settled by <see cref="Facing"/>, which
        ///for <see cref="DrcMetric.Projection"/> is the whole reason it exists - two edges that do not overlap
        ///when projected onto each other are not running alongside each other, and a rule about parallel
        ///edges has nothing to say about them.
        ///</summary>
        private static double Measured(Edge a, Edge b, DrcMetric metric, out Span span)
        {
            if (metric == DrcMetric.Projection && projected(a, b, out double across, out span))
                return across;

            //The shortest distance between two segments is between an endpoint of one and the other, once
            //they are known not to cross - and two edges of a merged boundary do not cross.
            double best = double.MaxValue;

            Element.Point onA = a.From;
            Element.Point onB = b.From;

            consider(a.From, b, metric, ref best, ref onA, ref onB, a.From);
            consider(a.To, b, metric, ref best, ref onA, ref onB, a.To);

            //And the other way round, since the nearest point may be an endpoint of b instead.
            considerReversed(b.From, a, metric, ref best, ref onA, ref onB);
            considerReversed(b.To, a, metric, ref best, ref onA, ref onB);

            //Two points rather than two runs: the nearest approach is a single line.
            span = new Span(onA, onA, onB, onB);

            return best;
        }

        private static void consider(
            Element.Point point,
            Edge other,
            DrcMetric metric,
            ref double best,
            ref Element.Point onA,
            ref Element.Point onB,
            Element.Point source)
        {
            var near = nearestOn(other, point);

            double distance = between(point, near, metric);

            if (distance >= best)
                return;

            best = distance;
            onA = source;
            onB = rounded(near);
        }

        private static void considerReversed(
            Element.Point point,
            Edge other,
            DrcMetric metric,
            ref double best,
            ref Element.Point onA,
            ref Element.Point onB)
        {
            var near = nearestOn(other, point);

            double distance = between(point, near, metric);

            if (distance >= best)
                return;

            best = distance;
            onA = rounded(near);
            onB = point;
        }

        ///<summary>
        ///The perpendicular distance across the part of two edges that face each other, or false when no
        ///part of them does.
        ///
        ///The overlap is worked out along the first edge's own direction: both edges are projected onto it,
        ///and what is left is the run over which they are alongside each other. Zero-length overlap is not
        ///an overlap - two edges meeting at a corner touch at a point and run alongside each other for
        ///nothing at all.
        ///</summary>
        private static bool projected(Edge a, Edge b, out double distance, out Span span)
        {
            distance = 0;
            span = default;

            double length = Math.Sqrt(((double)a.DeltaX * a.DeltaX) + ((double)a.DeltaY * a.DeltaY));

            if (length == 0)
                return false;

            double ux = a.DeltaX / length;
            double uy = a.DeltaY / length;

            double at(Element.Point point)
            {
                return (((double)point.X - a.From.X) * ux) + (((double)point.Y - a.From.Y) * uy);
            }

            double bFrom = at(b.From);
            double bTo = at(b.To);

            double low = Math.Max(0, Math.Min(bFrom, bTo));
            double high = Math.Min(length, Math.Max(bFrom, bTo));

            if (high <= low)
                return false;

            //Perpendicular distance from the other edge's line, taken at the middle of the overlap so a
            //slightly skewed pair reports the distance across the part that actually faces.
            double middle = (low + high) / 2;

            var onA = new Element.Point
            {
                X = (int)Math.Round(a.From.X + (ux * middle)),
                Y = (int)Math.Round(a.From.Y + (uy * middle))
            };

            distance = between(onA, nearestOn(b, onA), DrcMetric.Euclidean);

            //The two ends of the overlap, so the marker covers the facing run rather than a single point.
            var lowOnA = new Element.Point
            {
                X = (int)Math.Round(a.From.X + (ux * low)),
                Y = (int)Math.Round(a.From.Y + (uy * low))
            };

            var highOnA = new Element.Point
            {
                X = (int)Math.Round(a.From.X + (ux * high)),
                Y = (int)Math.Round(a.From.Y + (uy * high))
            };

            span = new Span(lowOnA, highOnA, rounded(nearestOn(b, highOnA)), rounded(nearestOn(b, lowOnA)));

            return true;
        }

        ///<summary>The point on an edge nearest a given one, clamped to the segment rather than its line.</summary>
        private static (double X, double Y) nearestOn(Edge edge, Element.Point point)
        {
            double lengthSquared = ((double)edge.DeltaX * edge.DeltaX) + ((double)edge.DeltaY * edge.DeltaY);

            if (lengthSquared == 0)
                return (edge.From.X, edge.From.Y);

            double along = ((((double)point.X - edge.From.X) * edge.DeltaX) + (((double)point.Y - edge.From.Y) * edge.DeltaY)) / lengthSquared;

            along = Math.Clamp(along, 0, 1);

            return (edge.From.X + (along * edge.DeltaX), edge.From.Y + (along * edge.DeltaY));
        }

        private static double between(Element.Point one, (double X, double Y) other, DrcMetric metric)
        {
            double dx = Math.Abs(one.X - other.X);
            double dy = Math.Abs(one.Y - other.Y);

            if (metric == DrcMetric.Square)
                return Math.Max(dx, dy);

            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static Element.Point rounded((double X, double Y) point)
        {
            return new Element.Point { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) };
        }

        #endregion **************************************************************************



        #region Finding what is near ********************************************************

        ///
        ///The edges bucketed by where they are, so each one only looks at its neighbors.
        ///
        ///The same uniform grid the violation attribution uses, and for the same reason: layout is spread
        ///over its extent, so a grid is an array index where anything cleverer is a walk. Sized to the
        ///limit rather than to the edge count, because what a query asks for is "everything within the
        ///limit" and a bucket near that size makes it a look at nine of them.
        ///
        private sealed class EdgeGrid
        {
            private const int MostCells = 1024;

            private readonly List<Edge> all;
            private readonly List<int>[] buckets;
            private readonly int[] seen;
            private readonly Bounds extent;
            private readonly int side;

            private int query;

            private EdgeGrid(List<Edge> all, Bounds extent, int side)
            {
                this.all = all;
                this.extent = extent;
                this.side = side;

                buckets = new List<int>[side * side];
                seen = new int[all.Count];

                for (int i = 0; i < buckets.Length; i++)
                    buckets[i] = new List<int>();

                for (int i = 0; i < all.Count; i++)
                {
                    var box = all[i].Box;

                    each(box, (x, y) => buckets[(y * side) + x].Add(i));
                }
            }

            public static EdgeGrid Of(List<Edge> edges, long limit)
            {
                var extent = Bounds.Empty;

                foreach (var edge in edges)
                    extent = extent.Union(edge.Box);

                long across = Math.Max(extent.Width, extent.Height);

                //A bucket about the size of the limit, so a query touches a handful whatever the layout is.
                int side = 1;

                if (limit > 0 && across > 0)
                    side = (int)Math.Clamp(across / Math.Max(1, limit), 1, MostCells);

                return new EdgeGrid(edges, extent, side);
            }

            private void each(Bounds box, Action<int, int> at)
            {
                int left = column(box.Left, extent.Left, extent.Width);
                int right = column(box.Right, extent.Left, extent.Width);
                int bottom = column(box.Bottom, extent.Bottom, extent.Height);
                int top = column(box.Top, extent.Bottom, extent.Height);

                for (int y = bottom; y <= top; y++)
                {
                    for (int x = left; x <= right; x++)
                        at(x, y);
                }
            }

            private int column(long at, long from, long across)
            {
                if (across <= 0)
                    return 0;

                return (int)Math.Clamp((at - from) * side / across, 0, side - 1);
            }

            ///<summary>Every edge whose extent comes within the limit of this one, each offered once.</summary>
            public IEnumerable<int> Near(Edge edge, long limit)
            {
                query++;

                var reach = edge.Box.Grown((int)Math.Min(limit, int.MaxValue / 4));

                var offered = new List<int>();

                each(reach, (x, y) =>
                {
                    foreach (int index in buckets[(y * side) + x])
                    {
                        if (seen[index] == query)
                            continue;

                        seen[index] = query;

                        if (all[index].Box.Intersects(reach))
                            offered.Add(index);
                    }
                });

                return offered;
            }

            ///<summary>
            ///The middle of each stretch of a line that is off the boundary: the line cut wherever it meets an
            ///edge, and the stretches that run along one left out. Each point is plainly on one side of the
            ///boundary or the other, which is the only question a ray cast can answer.
            ///
            ///Exact about whether an edge lies along the line: the line's ends are halves of corners at the
            ///finest, and its run times a coordinate stays far inside the 53 bits a double holds for any limit
            ///a rule carries on any layout a chip is drawn in. Where along the line a crossing falls is only
            ///arithmetic, and all it does is put the cuts in order.
            ///</summary>
            public List<(double X, double Y)> OffTheBoundary(double fromX, double fromY, double toX, double toY)
            {
                query++;

                double dx = toX - fromX;
                double dy = toY - fromY;
                double lengthSquared = (dx * dx) + (dy * dy);

                var cuts = new List<double> { 0, 1 };
                var along = new List<(double From, double To)>();

                var box = new Bounds(
                    (int)Math.Floor(Math.Min(fromX, toX)),
                    (int)Math.Floor(Math.Min(fromY, toY)),
                    (int)Math.Ceiling(Math.Max(fromX, toX)),
                    (int)Math.Ceiling(Math.Max(fromY, toY)));

                each(box, (column, row) =>
                {
                    foreach (int index in buckets[(row * side) + column])
                    {
                        if (seen[index] == query)
                            continue;

                        seen[index] = query;

                        var edge = all[index];

                        double ex = edge.DeltaX;
                        double ey = edge.DeltaY;
                        double wx = edge.From.X - fromX;
                        double wy = edge.From.Y - fromY;

                        double across = (dx * ey) - (dy * ex);

                        if (across == 0)
                        {
                            //Parallel, and only worth anything lying on the line itself.
                            if ((dx * wy) - (dy * wx) != 0)
                                continue;

                            double one = ((wx * dx) + (wy * dy)) / lengthSquared;
                            double other = (((edge.To.X - fromX) * dx) + ((edge.To.Y - fromY) * dy)) / lengthSquared;

                            double low = Math.Max(0, Math.Min(one, other));
                            double high = Math.Min(1, Math.Max(one, other));

                            if (high <= low)
                                continue;

                            along.Add((low, high));
                            cuts.Add(low);
                            cuts.Add(high);

                            continue;
                        }

                        double at = ((wx * ey) - (wy * ex)) / across;
                        double onEdge = ((wx * dy) - (wy * dx)) / across;

                        if (at >= 0 && at <= 1 && onEdge >= 0 && onEdge <= 1)
                            cuts.Add(at);
                    }
                });

                cuts.Sort();

                var middles = new List<(double X, double Y)>();
                double length = Math.Sqrt(lengthSquared);

                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    double low = cuts[i];
                    double high = cuts[i + 1];

                    //Shorter than a database unit is where an end was rounded onto the grid beside an edge
                    //rather than onto it, not ground anybody drew.
                    if ((high - low) * length < 1)
                        continue;

                    double middle = (low + high) / 2;

                    if (along.Exists(stretch => stretch.From <= middle && middle <= stretch.To))
                        continue;

                    middles.Add((fromX + (dx * middle), fromY + (dy * middle)));
                }

                return middles;
            }
        }

        #endregion **************************************************************************
    }
}
