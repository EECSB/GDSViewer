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
        ///What "closer than" means in ordinary speech, and the default a rule gets when it says nothing.
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
    ///know which way is out without carrying a flag, and it is why <see cref="Booleans.MergeToRings"/> is
    ///used rather than <see cref="Booleans.Merge"/> - a keyholed ring has the channel's two sides lying on
    ///top of each other, and neither of them knows which side the material is on.
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
        }

        ///<summary>
        ///Every edge of a merged layer, wound so the material is on the left of each.
        ///
        ///Merged first, because two overlapping shapes have edges running through each other's insides and
        ///every one of those is a false pair - a rule is about the boundary of what is covered, not about
        ///how the designer happened to draw it.
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

            return edges;
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
            var rings = Booleans.MergeToRings(shapes);

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
            var rings = Booleans.MergeToRings(shapes);

            return pairs(EdgesOf(rings), rings, limit, metric, outward: false);
        }

        ///<summary>
        ///The walk itself.
        ///
        ///Each edge asks the index for what is near it and tests only those. The pair is kept once rather
        ///than twice - a facing is mutual, and reporting both halves would double every count and disagree
        ///with every other tool.
        ///</summary>
        private static List<DrcEdgePair> pairs(List<Edge> edges, List<Booleans.Ring> rings, long limit, DrcMetric metric, bool outward)
        {
            var found = new List<DrcEdgePair>();

            if (edges.Count == 0 || limit <= 0)
                return found;

            var grid = EdgeGrid.Of(edges, limit);

            for (int i = 0; i < edges.Count; i++)
            {
                foreach (int j in grid.Near(edges[i], limit))
                {
                    //Once per pair. The index offers both directions, so the lower number keeps it.
                    if (j <= i)
                        continue;

                    if (!faces(edges[i], edges[j], outward))
                        continue;

                    //A wedge closes to a point, so it is a width at any limit and there is no distance to
                    //measure - what is reported is the run over which it is too narrow.
                    if (Corner(edges[i], edges[j]) is double interior)
                    {
                        found.Add(wedge(edges[i], edges[j], interior, limit));

                        continue;
                    }

                    if (!Measured(edges[i], edges[j], metric, out double distance, out var span))
                        continue;

                    //And what lies between them has to be what the check is about: material for a width,
                    //empty ground for a spacing. Two edges on opposite sides of a shape with a third
                    //between them face each other and are the width of nothing.
                    if (!Sees(edges[i], edges[j], rings, outward))
                        continue;

                    //At the limit is legal. A minimum of 140 forbids 139 and allows 140, and a checker that
                    //reported every minimum-width wire would be useless - drawing to minimum is what layout
                    //is. Unlike the sizing checks this is exact at every limit, odd or even.
                    if (distance >= limit)
                        continue;

                    found.Add(new DrcEdgePair
                    {
                        AFrom = span.AFrom,
                        ATo = span.ATo,
                        BFrom = span.BFrom,
                        BTo = span.BTo,
                        Distance = distance
                    });
                }
            }

            return Distinct(found);
        }

        ///<summary>
        ///One report per place, rather than one per pair of edges that happens to reach it.
        ///
        ///**Two edges meet at every corner, and both of them face whatever is across from that corner.** So
        ///a nearest approach that lands on a corner is found twice - once from each edge meeting there -
        ///and reported twice, at the same two points and the same distance. It is one fault.
        ///
        ///This was the whole of the remaining disagreement with KLayout on Euclidean counts. Measured on
        ///the bundled transistor's poly at a limit of 300, KLayout reported two pairs and this reported
        ///three, of which two were the same corner-to-corner approach written out twice.
        ///
        ///Keyed on the geometry rather than on which edges produced it, because that is what makes them the
        ///same fault: the same ground, measured the same distance apart.
        ///</summary>
        private static List<DrcEdgePair> Distinct(List<DrcEdgePair> found)
        {
            var kept = new List<DrcEdgePair>();
            var seen = new HashSet<(int, int, int, int, int, int, int, int)>();

            foreach (var pair in found)
            {
                if (seen.Add(keyOf(pair)))
                    kept.Add(pair);
            }

            return kept;
        }

        ///<summary>
        ///What makes two reports the same one: the four corners, with the two sides put in a fixed order.
        ///
        ///Ordered, because the same approach found from opposite directions names its two sides the other
        ///way round - and two spellings of one fault is exactly what this is here to collapse.
        ///</summary>
        private static (int, int, int, int, int, int, int, int) keyOf(DrcEdgePair pair)
        {
            var a = (pair.AFrom.X, pair.AFrom.Y, pair.ATo.X, pair.ATo.Y);
            var b = (pair.BFrom.X, pair.BFrom.Y, pair.BTo.X, pair.BTo.Y);

            if (a.CompareTo(b) <= 0)
                return (a.Item1, a.Item2, a.Item3, a.Item4, b.Item1, b.Item2, b.Item3, b.Item4);

            return (b.Item1, b.Item2, b.Item3, b.Item4, a.Item1, a.Item2, a.Item3, a.Item4);
        }

        ///<summary>
        ///Whether two edges face each other, through ground or through material.
        ///
        ///**Each has to be on the correct side of the other, not just one of them.** A wire running past
        ///the end of another has one edge facing it and is not facing back, and counting that pair reports
        ///a gap that is nobody's spacing. Tested with the cross product against the other edge's midpoint,
        ///which is the cheapest thing that answers it.
        ///
        ///Material is on the left of every edge, so for a spacing the other edge must lie to the *right*.
        ///
        ///**Edges sharing a corner are decided by the angle between them**, which took two goes to get
        ///right. Excluding every such pair stops a plain square reporting four width faults at its own
        ///corners - and it also loses a sharp spike, where the two edges genuinely do close to a point and
        ///the material between them genuinely is narrower than any limit. KLayout, asked the same question,
        ///reports three faults on a wedge and none on a square.
        ///
        ///So the test is the corner's own interior angle: a right angle or wider is a corner, anything
        ///narrower is a wedge and a wedge is a width. The threshold is ninety degrees, which is KLayout's
        ///own default for `angle_limit` and what makes a rectilinear layout report nothing extra.
        ///</summary>
        private static bool faces(Edge a, Edge b, bool outward)
        {
            if (Corner(a, b) is double interior)
            {
                if (outward)
                    return 360 - interior < AngleLimit;

                return interior < AngleLimit;
            }

            return onExpectedSide(a, midpoint(b), outward) && onExpectedSide(b, midpoint(a), outward);
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

        private static bool onExpectedSide(Edge edge, (double X, double Y) point, bool outward)
        {
            //Positive is to the left of the edge's direction, which is where the material is.
            double side = (edge.DeltaX * (point.Y - edge.From.Y)) - (edge.DeltaY * (point.X - edge.From.X));

            if (outward)
                return side < 0;

            return side > 0;
        }

        private static (double X, double Y) midpoint(Edge edge)
        {
            return (edge.From.X + (edge.DeltaX / 2.0), edge.From.Y + (edge.DeltaY / 2.0));
        }

        #endregion **************************************************************************



        #region Measuring *******************************************************************

        ///<summary>The four corners a pair is reported as.</summary>
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
        ///Tested halfway between the two edges' own middles, rather than at their nearest approach. The
        ///nearest approach of two facing edges is often corner to corner, and the point halfway along it
        ///lands exactly *on* the boundary - where a ray-cast containment test is answering a question with
        ///no answer, and a plain narrow rectangle stopped being reported. Two midpoints are interior to the
        ///run that actually faces.
        ///</summary>
        private static bool Sees(Edge a, Edge b, List<Booleans.Ring> rings, bool outward)
        {
            var (ax, ay) = midpoint(a);
            var (bx, by) = midpoint(b);

            var middle = new Element.Point
            {
                X = (int)Math.Round((ax + bx) / 2),
                Y = (int)Math.Round((ay + by) / 2)
            };

            bool inside = Covers(rings, middle);

            //A width wants material between them; a spacing wants ground.
            if (outward)
                return !inside;

            return inside;
        }

        ///<summary>
        ///Whether a point lies on the layer, holes taken off.
        ///
        ///Ray casting to the right, counting crossings: odd means inside the outer ring, and a hole is
        ///counted the same way and flips it back out again.
        ///</summary>
        public static bool Covers(List<Booleans.Ring> rings, Element.Point point)
        {
            foreach (var ring in rings)
            {
                if (!crosses(ring.Boundary, point))
                    continue;

                bool inHole = false;

                foreach (var hole in ring.Holes)
                {
                    if (crosses(hole, point))
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

        private static bool crosses(List<Element.Point> ring, Element.Point point)
        {
            bool inside = false;

            for (int i = 0; i < ring.Count; i++)
            {
                var one = ring[i];
                var other = ring[(i + 1) % ring.Count];

                //Half-open on the vertical, so a ray passing exactly through a corner counts it once.
                if ((one.Y > point.Y) == (other.Y > point.Y))
                    continue;

                double at = one.X + (((double)point.Y - one.Y) / ((double)other.Y - one.Y) * ((double)other.X - one.X));

                if (at > point.X)
                    inside = !inside;
            }

            return inside;
        }

        ///<summary>
        ///How far apart two facing edges are, by the metric asked for, and which part of each was measured.
        ///
        ///False when the metric says this is not a pair at all - which only <see cref="DrcMetric.Projection"/>
        ///ever says, and is the whole reason it exists: two edges that do not overlap when projected onto
        ///each other are not running alongside each other, and a rule about parallel edges has nothing to
        ///say about them.
        ///</summary>
        private static bool Measured(Edge a, Edge b, DrcMetric metric, out double distance, out Span span)
        {
            distance = 0;
            span = default;

            if (metric == DrcMetric.Projection)
                return projected(a, b, out distance, out span);

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

            if (best == double.MaxValue)
                return false;

            distance = best;

            //Two points rather than two runs. A Euclidean pair is the nearest approach and that is a single
            //line - where a projected pair is a stretch of two edges running alongside each other, and has
            //four distinct corners to report.
            span = new Span(onA, onA, onB, onB);

            return true;
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
        }

        #endregion **************************************************************************
    }
}
