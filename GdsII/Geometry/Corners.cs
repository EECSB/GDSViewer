namespace GdsII
{
    ///<summary>
    ///Cutting the corners off a shape, or rounding them: a chamfer and a fillet.
    ///
    ///**A sharp corner is something a process cannot make and a design often does not want.** Light rounds
    ///it on the way through a mask, a wet etch rounds it again, a channel with square corners traps bubbles
    ///and a waveguide that turns sharply leaks. So drawing it round, or cut, is how somebody says what they
    ///actually mean - and it is the most-asked-for edit a layout tool has that this one did not.
    ///
    ///**Corner by corner, not by growing and shrinking.** The other way to round a shape is to shrink it and
    ///grow it back with round joins, which Clipper would do in two calls - and which also takes away every
    ///part of the shape narrower than the radius, so a wire narrower than its corners disappears. Here each
    ///corner is worked on where it stands, and a shape keeps everything it had but its corners.
    ///
    ///**Each corner takes at most half of each edge it sits on**, so two corners at the ends of a short edge
    ///meet in its middle rather than crossing - a radius bigger than a shape can take becomes the biggest it
    ///can, which is what somebody asking for "round" means. Inside corners are rounded the same way, which
    ///fills them rather than cutting them.
    ///
    ///Coordinates in, coordinates out: whole database units, closed if the outline came in closed.
    ///</summary>
    public static class Corners
    {
        #region Constants *******************************************************************

        ///<summary>
        ///How nearly straight a corner has to be to be no corner at all. A sine this small is a turn of a
        ///hundredth of a degree, which is a point on a straight edge drawn with rounding in it.
        ///</summary>
        private const double Straight = 1.8e-4;

        ///<summary>The most steps a quarter turn is cut into, so a request cannot ask for millions of points.</summary>
        public const int MostStepsPerQuarter = 256;

        #endregion **************************************************************************



        #region Cutting and rounding ********************************************************

        ///
        ///Every corner cut off straight, <paramref name="size"/> along each edge from where it was.
        ///
        public static List<Element.Point> Cut(IReadOnlyList<Element.Point> outline, int size)
        {
            return reshape(outline, size, (corner, along) => cut(corner, along));
        }

        ///
        ///Every corner rounded to an arc of <paramref name="radius"/>, in
        ///<paramref name="stepsPerQuarter"/> straight steps for every quarter turn the arc makes.
        ///
        ///A right angle gets exactly that many steps; a shallower corner, fewer, so a gentle bend is not
        ///drawn with as many points as a sharp one. The format has no curves, so the arc is the polygon
        ///through those points, every one of them on the circle.
        ///
        public static List<Element.Point> Round(IReadOnlyList<Element.Point> outline, int radius, int stepsPerQuarter)
        {
            int steps = Math.Clamp(stepsPerQuarter, 1, MostStepsPerQuarter);

            return reshape(outline, radius, (corner, along) => round(corner, along, steps));
        }

        #endregion **************************************************************************



        #region One corner ******************************************************************

        ///<summary>A corner: where it is, the unit directions back along the edge before it and on along the edge after it, and how much of each edge it may have.</summary>
        private readonly record struct Corner(double X, double Y, double BackX, double BackY, double OnX, double OnY, double Room);

        private static List<(double X, double Y)> cut(Corner corner, double size)
        {
            double along = Math.Min(size, corner.Room);

            return new List<(double X, double Y)>
            {
                (corner.X + (corner.BackX * along), corner.Y + (corner.BackY * along)),
                (corner.X + (corner.OnX * along), corner.Y + (corner.OnY * along))
            };
        }

        ///
        ///The arc that meets both edges, tangent to each.
        ///
        ///The corner's two edges are a turn of θ apart, so the arc touches each a distance r / tan(θ/2) from
        ///the corner and is centered on the line halfway between them, r / sin(θ/2) out. Where that distance
        ///is more than the edges can give, the radius comes down until it fits.
        ///
        private static List<(double X, double Y)> round(Corner corner, double radius, int stepsPerQuarter)
        {
            double cosine = (corner.BackX * corner.OnX) + (corner.BackY * corner.OnY);
            double angle = Math.Acos(Math.Clamp(cosine, -1, 1));
            double half = angle / 2;

            //A spike that turns straight back has no arc that fits inside it, and the arithmetic below would
            //divide nothing by nothing. Left as the point it is.
            if (half < 1e-9)
                return new List<(double X, double Y)> { (corner.X, corner.Y) };

            double touches = Math.Min(radius / Math.Tan(half), corner.Room);
            double fitted = touches * Math.Tan(half);

            double middleX = corner.BackX + corner.OnX;
            double middleY = corner.BackY + corner.OnY;
            double middleLength = Math.Sqrt((middleX * middleX) + (middleY * middleY));

            double outward = fitted / Math.Sin(half);
            double centerX = corner.X + (middleX / middleLength * outward);
            double centerY = corner.Y + (middleY / middleLength * outward);

            double fromX = corner.X + (corner.BackX * touches);
            double fromY = corner.Y + (corner.BackY * touches);
            double toX = corner.X + (corner.OnX * touches);
            double toY = corner.Y + (corner.OnY * touches);

            double start = Math.Atan2(fromY - centerY, fromX - centerX);
            double end = Math.Atan2(toY - centerY, toX - centerX);

            //The short way round, which is the turn the corner makes - always less than half a circle.
            double sweep = end - start;

            while (sweep > Math.PI)
                sweep -= 2 * Math.PI;

            while (sweep < -Math.PI)
                sweep += 2 * Math.PI;

            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) * stepsPerQuarter - 1e-9));

            var points = new List<(double X, double Y)>(steps + 1) { (fromX, fromY) };

            for (int step = 1; step < steps; step++)
            {
                double at = start + (sweep * step / steps);

                points.Add((centerX + (fitted * Math.Cos(at)), centerY + (fitted * Math.Sin(at))));
            }

            points.Add((toX, toY));

            return points;
        }

        #endregion **************************************************************************



        #region The walk ********************************************************************

        ///
        ///Every corner of the outline replaced by what <paramref name="replace"/> makes of it.
        ///
        ///The outline is cleaned first - its closing point, repeated points and points on a straight edge
        ///taken out - because each of those is a corner of no angle, and an arc fitted into one is a division
        ///by zero. Fewer than three corners left is no shape at all, and comes back as it went in.
        ///
        private static List<Element.Point> reshape(
            IReadOnlyList<Element.Point> outline,
            int size,
            Func<Corner, double, List<(double X, double Y)>> replace)
        {
            bool closed = outline.Count > 1 && outline[0].Equals(outline[^1]);
            var ring = cornersOf(outline);

            if (size <= 0 || ring.Count < 3)
                return new List<Element.Point>(outline);

            var made = new List<Element.Point>();

            for (int i = 0; i < ring.Count; i++)
            {
                var before = ring[(i + ring.Count - 1) % ring.Count];
                var at = ring[i];
                var after = ring[(i + 1) % ring.Count];

                double backX = before.X - (double)at.X;
                double backY = before.Y - (double)at.Y;
                double onX = after.X - (double)at.X;
                double onY = after.Y - (double)at.Y;

                double back = Math.Sqrt((backX * backX) + (backY * backY));
                double on = Math.Sqrt((onX * onX) + (onY * onY));

                var corner = new Corner(at.X, at.Y, backX / back, backY / back, onX / on, onY / on, Math.Min(back, on) / 2);

                foreach (var (x, y) in replace(corner, size))
                    addOnce(made, new Element.Point((int)Math.Round(x), (int)Math.Round(y)));
            }

            //The last of the last corner may have rounded onto the first of the first.
            while (made.Count > 1 && made[^1].Equals(made[0]))
                made.RemoveAt(made.Count - 1);

            if (closed && made.Count > 0)
                made.Add(made[0]);

            return made;
        }

        ///<summary>The outline's real corners: no closing point, no point twice in a row, none on a straight edge.</summary>
        private static List<Element.Point> cornersOf(IReadOnlyList<Element.Point> outline)
        {
            var points = new List<Element.Point>();

            foreach (var point in outline)
                addOnce(points, point);

            while (points.Count > 1 && points[^1].Equals(points[0]))
                points.RemoveAt(points.Count - 1);

            //Taken out until none is left, since taking one out can leave its neighbor on a straight edge.
            bool removed = true;

            while (removed && points.Count >= 3)
            {
                removed = false;

                for (int i = 0; i < points.Count && points.Count >= 3; i++)
                {
                    var before = points[(i + points.Count - 1) % points.Count];
                    var at = points[i];
                    var after = points[(i + 1) % points.Count];

                    if (!isStraight(before, at, after))
                        continue;

                    points.RemoveAt(i);
                    removed = true;
                    i--;
                }
            }

            return points;
        }

        ///<summary>
        ///Whether a point lies on the straight line through its neighbors, carrying on in the same direction -
        ///a spike, which turns straight back, is a corner, if a very sharp one.
        ///</summary>
        private static bool isStraight(Element.Point before, Element.Point at, Element.Point after)
        {
            double backX = before.X - (double)at.X;
            double backY = before.Y - (double)at.Y;
            double onX = after.X - (double)at.X;
            double onY = after.Y - (double)at.Y;

            double back = Math.Sqrt((backX * backX) + (backY * backY));
            double on = Math.Sqrt((onX * onX) + (onY * onY));

            if (back == 0 || on == 0)
                return true;

            double cross = ((backX * onY) - (backY * onX)) / (back * on);
            double dot = ((backX * onX) + (backY * onY)) / (back * on);

            return Math.Abs(cross) < Straight && dot < 0;
        }

        private static void addOnce(List<Element.Point> points, Element.Point point)
        {
            if (points.Count == 0 || !points[^1].Equals(point))
                points.Add(point);
        }

        #endregion **************************************************************************
    }
}
