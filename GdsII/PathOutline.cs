namespace GdsII
{
    ///<summary>
    ///Turns a PATH's centerline into the closed outline it actually occupies.
    ///
    ///A GDSII path is a polyline plus a width, not a shape. Drawing its XY list as a polygon encloses no
    ///area, which is why wires used to show as hairlines. This offsets the centerline by half the width
    ///to either side and joins the two sides into one polygon.
    ///
    ///One polygon rather than a rectangle per segment, deliberately: the 2D view fills at partial opacity,
    ///so overlapping quads at every corner would show through each other as darker patches.
    ///</summary>
    public static class PathOutline
    {
        ///<summary>
        ///How far a mitered corner may reach from the centerline, in half-widths, before it is cut off
        ///square instead. Without a limit a nearly-reversing path grows an arbitrarily long spike. Four
        ///is the same default SVG and Postscript use.
        ///</summary>
        private const double MiterLimit = 4;

        ///<summary>Segments used per semicircular end cap, for PATHTYPE 1.</summary>
        private const int RoundCapSegments = 8;

        ///<summary>
        ///Builds the outline. Coordinates are the path's own, so the caller transforms the result rather
        ///than the input - a magnified placement has to scale the width along with everything else.
        ///</summary>
        public static List<Element.Point> Build(
            IReadOnlyList<Element.Point> centerline,
            int width,
            int pathType,
            int beginExtension,
            int endExtension)
        {
            if (width <= 0)
                return new List<Element.Point>(centerline);

            var widths = new int[centerline.Count];

            Array.Fill(widths, width);

            return Build(centerline, widths, pathType, beginExtension, endExtension);
        }

        ///
        ///The same, for a centerline whose width changes along it - one width per point.
        ///
        ///**A tapering path is not a GDSII path.** The format's `WIDTH` is one number for the whole element,
        ///so a wire that narrows has to be written as a boundary; this is what builds that boundary. It is
        ///the same walk the constant case takes, and the constant case goes through it - the alternative
        ///was a second offsetter, and two of them would eventually disagree about what a sharp corner does.
        ///
        ///**Where the width changes along a segment, that segment's offset edge is not parallel to it.** It
        ///runs from the start point offset by the start's half-width to the end point offset by the end's,
        ///which is a line at a slight angle to the centerline. Everything else follows: the corner between
        ///two segments is where their two offset edges meet, whether or not those edges are parallel to
        ///anything.
        ///
        ///A width in the middle of the list is the width *at that point*, and the taper between two points
        ///is linear - which is what makes a taper drawn in two steps the same shape as one drawn in ten.
        ///
        public static List<Element.Point> Build(
            IReadOnlyList<Element.Point> centerline,
            IReadOnlyList<int> widths,
            int pathType,
            int beginExtension,
            int endExtension)
        {
            var points = withoutRepeats(centerline, widths, out var halves);

            //A single point encloses nothing, and no width anywhere has no outline to build. In both cases
            //hand back what came in, so the path still draws as a line rather than vanishing.
            if (points.Count < 2 || halves.All(half => half <= 0))
                return new List<Element.Point>(centerline);

            applyEndExtensions(points, pathType, halves[0], halves[^1], beginExtension, endExtension);

            var left = offsetSide(points, halves, 1);
            var right = offsetSide(points, halves, -1);

            var outline = new List<Element.Point>();

            foreach (var point in left)
                outline.Add(round(point));

            if (pathType == 1)
                appendRoundCap(outline, points[^1], points[^2], halves[^1]);

            for (int i = right.Count - 1; i >= 0; i--)
                outline.Add(round(right[i]));

            if (pathType == 1)
                appendRoundCap(outline, points[0], points[1], halves[0]);

            return outline;
        }

        #region Geometry ********************************************************************

        ///<summary>
        ///Walks one side of the centerline, offset by <paramref name="distance"/> - negative for the other
        ///side. Interior corners are mitered: the two offset edges are extended until they meet, which is
        ///what keeps the outline a single unbroken loop.
        ///</summary>
        private static List<Vector> offsetSide(List<Vector> centerline, double[] halves, double sign)
        {
            int segments = centerline.Count - 1;

            //
            //Each segment's own offset edge, start and end.
            //
            //**Built first rather than derived at each corner**, because with a width that changes along a
            //segment the edge is not parallel to the segment and cannot be reconstructed from the segment's
            //direction and one distance. Its two ends are offset by the half-width at each end, which is
            //what makes it lean.
            //
            var starts = new Vector[segments];
            var ends = new Vector[segments];

            for (int i = 0; i < segments; i++)
            {
                var normal = (centerline[i + 1] - centerline[i]).Normalized().LeftNormal();

                starts[i] = centerline[i] + (normal * (halves[i] * sign));
                ends[i] = centerline[i + 1] + (normal * (halves[i + 1] * sign));
            }

            var side = new List<Vector> { starts[0] };

            for (int i = 1; i < segments; i++)
                appendCorner(side, centerline[i], starts[i - 1], ends[i - 1], starts[i], ends[i], halves[i]);

            //The far end of the last segment closes this side.
            side.Add(ends[^1]);

            return side;
        }

        ///<summary>
        ///Where two offset edges meet, which is the corner of the outline between them.
        ///
        ///Taken as the intersection of the two edges as *lines*, so it is the same answer whether the edges
        ///run parallel to their segments (constant width) or lean (tapering).
        ///</summary>
        private static void appendCorner(
            List<Vector> side,
            Vector corner,
            Vector fromStart,
            Vector fromEnd,
            Vector toStart,
            Vector toEnd,
            double half)
        {
            var incoming = (fromEnd - fromStart).Normalized();
            var outgoing = (toEnd - toStart).Normalized();

            double denominator = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);

            //Parallel edges, so the path runs straight through and the two offsets already coincide.
            if (Math.Abs(denominator) < 1e-9)
                return;

            var gap = toStart - fromEnd;
            double along = ((gap.X * outgoing.Y) - (gap.Y * outgoing.X)) / denominator;
            var miter = fromEnd + (incoming * along);

            //Too sharp to miter: cut the corner off square by keeping both offsets instead of their
            //intersection, which is the bevel join.
            if ((miter - corner).Length() > MiterLimit * Math.Abs(half))
            {
                side.Add(fromEnd);
                side.Add(toStart);

                return;
            }

            side.Add(miter);
        }

        ///<summary>
        ///Extends the ends outwards for the path types that draw past their endpoints: type 2 by half the
        ///width, type 4 by the amounts BGNEXTN and ENDEXTN give. Types 0 and 1 end on their endpoint.
        ///</summary>
        private static void applyEndExtensions(
            List<Vector> points,
            int pathType,
            double halfAtBegin,
            double halfAtEnd,
            int beginExtension,
            int endExtension)
        {
            double begin = 0;
            double end = 0;

            //Each end by its own half-width, which is the same number twice on a path of constant width.
            if (pathType == 2)
            {
                begin = halfAtBegin;
                end = halfAtEnd;
            }
            else if (pathType == 4)
            {
                begin = beginExtension;
                end = endExtension;
            }

            if (begin != 0)
            {
                var direction = (points[1] - points[0]).Normalized();

                points[0] = points[0] - (direction * begin);
            }

            if (end != 0)
            {
                var direction = (points[^1] - points[^2]).Normalized();

                points[^1] = points[^1] + (direction * end);
            }
        }

        ///<summary>
        ///Adds the arc of a round end cap. Both caps sweep a half turn in the same direction, because the
        ///outline is traced up one side and back down the other.
        ///</summary>
        private static void appendRoundCap(List<Element.Point> outline, Vector tip, Vector previous, double half)
        {
            var direction = (tip - previous).Normalized();
            double start = Math.Atan2(direction.Y, direction.X) + (Math.PI / 2);

            //The endpoints of the arc are already in the outline from the two sides, so only the points
            //between them are added.
            for (int i = 1; i < RoundCapSegments; i++)
            {
                double angle = start - (Math.PI * i / RoundCapSegments);

                outline.Add(round(new Vector(
                    tip.X + (half * Math.Cos(angle)),
                    tip.Y + (half * Math.Sin(angle)))));
            }
        }

        ///<summary>
        ///Drops repeated points. A zero-length segment has no direction, so it would poison every normal
        ///and miter derived from it.
        ///
        ///**The widths are dropped with them**, or the two lists stop describing the same path: a width
        ///list that still has an entry for a point that is gone puts every later width on the wrong point,
        ///and a taper drawn over the wrong half of a route is the kind of wrong that looks deliberate.
        ///
        ///A width list shorter than the centerline is padded with its last entry, and an empty one is no
        ///width at all - so a caller that has fewer numbers than points gets a sensible path rather than an
        ///exception from inside a renderer.
        ///</summary>
        private static List<Vector> withoutRepeats(
            IReadOnlyList<Element.Point> centerline,
            IReadOnlyList<int> widths,
            out double[] halves)
        {
            var points = new List<Vector>();
            var kept = new List<double>();

            for (int i = 0; i < centerline.Count; i++)
            {
                var next = new Vector(centerline[i].X, centerline[i].Y);

                if (points.Count > 0 && (next - points[^1]).Length() < 1e-9)
                    continue;

                points.Add(next);
                kept.Add(halfAt(widths, i));
            }

            halves = kept.ToArray();

            return points;
        }

        private static double halfAt(IReadOnlyList<int> widths, int at)
        {
            if (widths.Count == 0)
                return 0;

            if (at >= widths.Count)
                return Math.Max(0, widths[^1]) / 2.0;

            return Math.Max(0, widths[at]) / 2.0;
        }

        private static Element.Point round(Vector point)
        {
            return new Element.Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }

        #endregion *************************************************************************



        #region Vector **********************************************************************

        ///<summary>A point or direction in path space, kept in doubles so offsets do not round mid-calculation.</summary>
        private readonly struct Vector
        {
            public Vector(double x, double y)
            {
                X = x;
                Y = y;
            }

            public double X { get; }
            public double Y { get; }

            public static Vector operator +(Vector a, Vector b)
            {
                return new Vector(a.X + b.X, a.Y + b.Y);
            }

            public static Vector operator -(Vector a, Vector b)
            {
                return new Vector(a.X - b.X, a.Y - b.Y);
            }

            public static Vector operator *(Vector a, double scale)
            {
                return new Vector(a.X * scale, a.Y * scale);
            }

            public double Length()
            {
                return Math.Sqrt((X * X) + (Y * Y));
            }

            public Vector Normalized()
            {
                double length = Length();

                if (length < 1e-9)
                    return new Vector(0, 0);

                return new Vector(X / length, Y / length);
            }

            ///<summary>The perpendicular a quarter turn counterclockwise, which is the path's left side.</summary>
            public Vector LeftNormal()
            {
                return new Vector(-Y, X);
            }
        }

        #endregion *************************************************************************
    }
}
