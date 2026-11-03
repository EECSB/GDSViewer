using static GdsII.GDS;

namespace GdsII
{
    ///
    ///The geometric question the tools ask that nothing else in the library does: how close two outlines come.
    ///Whether one crosses itself is <see cref="Grid.Crossing"/>, which the app asks too.
    ///
    internal static class AgentGeometry
    {
        ///<summary>An element's outline; a label's anchor point; a placement's box as four corners.</summary>
        public static List<Element.Point> OutlineOrBox(AgentSession session, ElementModel model)
        {
            if (Hierarchy.PlacedBy(model) is not null)
            {
                var box = session.BoxOf(model);

                if (box.IsEmpty)
                    return new List<Element.Point>();

                return Shapes.Between(box.Left, box.Bottom, box.Right, box.Top);
            }

            if (GdsFlattener.OutlineOf(model) is List<Element.Point> outline && outline.Count > 0)
                return outline;

            return AgentSession.CornersOf(model);
        }

        ///
        ///The closest two sets of points come, edge to edge - zero where they touch, cross, or one point lies
        ///inside the other's ring.
        ///
        ///Three points or more are a ring and closed for the measuring; two are a segment and one is a point,
        ///which is what a label is. Every edge against every edge, which is quadratic and fine for what a
        ///person measures - two wires, a via and its metal - and the reason measure takes ten shapes at most.
        ///
        public static double Closest(IReadOnlyList<Element.Point> one, IReadOnlyList<Element.Point> other)
        {
            if (inside(one[0], other) || inside(other[0], one))
                return 0;

            var edges = edgesOf(one);
            var others = edgesOf(other);
            double closest = double.MaxValue;

            foreach (var (a, b) in edges)
            {
                foreach (var (c, d) in others)
                {
                    closest = Math.Min(closest, segments(a, b, c, d));

                    if (closest == 0)
                        return 0;
                }
            }

            return closest;
        }

        private static List<(Element.Point From, Element.Point To)> edgesOf(IReadOnlyList<Element.Point> points)
        {
            var edges = new List<(Element.Point, Element.Point)>();

            if (points.Count == 1)
            {
                edges.Add((points[0], points[0]));

                return edges;
            }

            for (int i = 0; i + 1 < points.Count; i++)
                edges.Add((points[i], points[i + 1]));

            if (points.Count >= 3 && !points[0].Equals(points[^1]))
                edges.Add((points[^1], points[0]));

            return edges;
        }

        ///<summary>Whether a point is inside a ring, by counting crossings to its right. A ring of fewer than three corners holds nothing.</summary>
        private static bool inside(Element.Point point, IReadOnlyList<Element.Point> ring)
        {
            if (ring.Count < 3)
                return false;

            bool within = false;

            foreach (var (from, to) in edgesOf(ring))
            {
                if ((from.Y > point.Y) == (to.Y > point.Y))
                    continue;

                double crossesAt = from.X + ((double)(point.Y - from.Y) * (to.X - from.X) / (to.Y - from.Y));

                if (point.X < crossesAt)
                    within = !within;
            }

            return within;
        }

        private static double segments(Element.Point a, Element.Point b, Element.Point c, Element.Point d)
        {
            if (cross(a, b, c, d))
                return 0;

            return Math.Min(
                Math.Min(toSegment(a, c, d), toSegment(b, c, d)),
                Math.Min(toSegment(c, a, b), toSegment(d, a, b)));
        }

        private static double toSegment(Element.Point point, Element.Point from, Element.Point to)
        {
            double dx = to.X - (double)from.X;
            double dy = to.Y - (double)from.Y;
            double length = (dx * dx) + (dy * dy);

            double along = 0;

            if (length > 0)
                along = Math.Clamp((((point.X - (double)from.X) * dx) + ((point.Y - (double)from.Y) * dy)) / length, 0, 1);

            double x = from.X + (along * dx) - point.X;
            double y = from.Y + (along * dy) - point.Y;

            return Math.Sqrt((x * x) + (y * y));
        }

        ///<summary>Which side of the line through a and b a point is on: positive left, negative right, zero on it.</summary>
        private static long side(Element.Point a, Element.Point b, Element.Point point)
        {
            return ((long)(b.X - a.X) * (point.Y - a.Y)) - ((long)(b.Y - a.Y) * (point.X - a.X));
        }

        ///<summary>Whether two segments meet anywhere, touching included.</summary>
        private static bool cross(Element.Point a, Element.Point b, Element.Point c, Element.Point d)
        {
            long one = side(a, b, c);
            long two = side(a, b, d);
            long three = side(c, d, a);
            long four = side(c, d, b);

            if (((one > 0 && two < 0) || (one < 0 && two > 0)) && ((three > 0 && four < 0) || (three < 0 && four > 0)))
                return true;

            return (one == 0 && onSegment(a, b, c))
                || (two == 0 && onSegment(a, b, d))
                || (three == 0 && onSegment(c, d, a))
                || (four == 0 && onSegment(c, d, b));
        }

        ///<summary>Whether a point already known to be on the line through a and b lies between them.</summary>
        private static bool onSegment(Element.Point a, Element.Point b, Element.Point point)
        {
            return point.X >= Math.Min(a.X, b.X)
                && point.X <= Math.Max(a.X, b.X)
                && point.Y >= Math.Min(a.Y, b.Y)
                && point.Y <= Math.Max(a.Y, b.Y);
        }
    }
}
