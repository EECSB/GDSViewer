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
            var points = withoutRepeats(centerline);

            //A single point encloses nothing, and a zero or negative width has no outline to build. In
            //both cases hand back what came in, so the path still draws as a line rather than vanishing.
            if (points.Count < 2 || width <= 0)
                return new List<Element.Point>(centerline);

            double half = width / 2.0;

            applyEndExtensions(points, pathType, half, beginExtension, endExtension);

            var left = offsetSide(points, half);
            var right = offsetSide(points, -half);

            var outline = new List<Element.Point>();

            foreach (var point in left)
                outline.Add(round(point));

            if (pathType == 1)
                appendRoundCap(outline, points[^1], points[^2], half);

            for (int i = right.Count - 1; i >= 0; i--)
                outline.Add(round(right[i]));

            if (pathType == 1)
                appendRoundCap(outline, points[0], points[1], half);

            return outline;
        }

        #region Geometry ********************************************************************

        ///<summary>
        ///Walks one side of the centerline, offset by <paramref name="distance"/> - negative for the other
        ///side. Interior corners are mitered: the two offset edges are extended until they meet, which is
        ///what keeps the outline a single unbroken loop.
        ///</summary>
        private static List<Vector> offsetSide(List<Vector> centerline, double distance)
        {
            var side = new List<Vector>();

            for (int i = 0; i < centerline.Count - 1; i++)
            {
                var direction = (centerline[i + 1] - centerline[i]).Normalized();

                //The first segment contributes its start. Every later one contributes only its join to the
                //previous segment - the point where they meet stands in for both segments' shared end.
                if (i == 0)
                {
                    side.Add(centerline[0] + (direction.LeftNormal() * distance));

                    continue;
                }

                var previous = (centerline[i] - centerline[i - 1]).Normalized();

                appendCorner(side, centerline[i], previous, direction, distance);
            }

            //The far end of the last segment closes this side.
            var last = (centerline[^1] - centerline[^2]).Normalized();

            side.Add(centerline[^1] + (last.LeftNormal() * distance));

            return side;
        }

        private static void appendCorner(List<Vector> side, Vector corner, Vector incoming, Vector outgoing, double distance)
        {
            var incomingOffset = corner + (incoming.LeftNormal() * distance);
            var outgoingOffset = corner + (outgoing.LeftNormal() * distance);

            double denominator = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);

            //Parallel edges, so the path runs straight through and the two offsets already coincide.
            if (Math.Abs(denominator) < 1e-9)
                return;

            var gap = outgoingOffset - incomingOffset;
            double along = ((gap.X * outgoing.Y) - (gap.Y * outgoing.X)) / denominator;
            var miter = incomingOffset + (incoming * along);

            //Too sharp to miter: cut the corner off square by keeping both offsets instead of their
            //intersection, which is the bevel join.
            if ((miter - corner).Length() > MiterLimit * Math.Abs(distance))
            {
                side.Add(incomingOffset);
                side.Add(outgoingOffset);

                return;
            }

            side.Add(miter);
        }

        ///<summary>
        ///Extends the ends outwards for the path types that draw past their endpoints: type 2 by half the
        ///width, type 4 by the amounts BGNEXTN and ENDEXTN give. Types 0 and 1 end on their endpoint.
        ///</summary>
        private static void applyEndExtensions(List<Vector> points, int pathType, double half, int beginExtension, int endExtension)
        {
            double begin = 0;
            double end = 0;

            if (pathType == 2)
            {
                begin = half;
                end = half;
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
        ///</summary>
        private static List<Vector> withoutRepeats(IReadOnlyList<Element.Point> centerline)
        {
            var points = new List<Vector>();

            foreach (var point in centerline)
            {
                var next = new Vector(point.X, point.Y);

                if (points.Count > 0 && (next - points[^1]).Length() < 1e-9)
                    continue;

                points.Add(next);
            }

            return points;
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
