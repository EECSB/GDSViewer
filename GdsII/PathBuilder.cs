namespace GdsII
{
    ///
    ///A route built a segment at a time - go straight, bend, curve - and turned into geometry at the end.
    ///
    ///**Because a wire is described by where it goes, not by where its corners are.** Writing a bus by hand
    ///means working out the corner coordinates of every bend, and then working them all out again when the
    ///thing it connects to moves. Here the route is the description: a heading, a length, a turn.
    ///
    ///**It carries a heading, and every segment leaves one behind.** `Straight` goes the way the last
    ///segment was pointing and `BendDeg` turns from there, so the pieces join smoothly without the caller
    ///tracking an angle. That is the whole difference between this and a list of points.
    ///
    ///The route is a centerline. <see cref="Centerline"/> hands it back as one, for a `PATH` element or for
    ///another builder to work from, and <see cref="BuildPolygon"/> outlines it at the width - through the
    ///same outliner a drawn path uses, so the two agree about corners.
    ///
    ///Angles are degrees and **positive turns left**, which is the direction mathematics turns and the
    ///direction the y axis in a layout points. Coordinates accumulate in double and round once, at the end.
    ///
    public sealed class PathBuilder
    {
        ///<summary>
        ///How many pieces a bend is cut into per quarter turn when nobody says.
        ///
        ///Eight per quarter is a corner about 0.24% inside the radius at the middle of a piece, which on a
        ///half-micron bend is a nanometer - under the grid every file this writes for is on.
        ///</summary>
        public const int DefaultBendVertices = 8;

        ///<summary>
        ///The most vertices one element is meant to carry, which is what <see cref="Build"/> cuts at.
        ///
        ///The format's own limit, and the one every reader is written against. Real files exceed it and
        ///most readers cope, which is why this is a default rather than a rule - but a file that stays
        ///inside it is a file nothing has an opinion about.
        ///</summary>
        public const int MostVerticesPerElement = 200;

        private readonly List<(double X, double Y)> route = new List<(double, double)>();

        ///<summary>
        ///The width at each point of the route, so a wire can narrow along it.
        ///
        ///Kept per point rather than per builder because that is what a taper is - and because the outliner
        ///takes it in exactly this shape. A route started with no width carries zeroes and takes its width
        ///from <see cref="BuildPolygon(int)"/> instead.
        ///</summary>
        private readonly List<int> widths = new List<int>();

        private double headingRadians;

        private double atX;

        private double atY;

        private int width;

        ///
        ///Starts a route at a point, pointing along <paramref name="headingDegrees"/> - 0 being the x axis.
        ///
        ///<paramref name="width"/> is what the route is wide to begin with, and every segment can change it.
        ///Leaving it at zero is the simpler case: the route is a centerline and nothing else, and the width
        ///is given once to <see cref="BuildPolygon(int)"/> at the end.
        ///
        public PathBuilder(Element.Point start, double headingDegrees = 0, int width = 0)
        {
            atX = start.X;
            atY = start.Y;
            headingRadians = headingDegrees * Math.PI / 180;

            this.width = Math.Max(0, width);

            arrive();
        }

        ///<summary>Records where the route has reached, at the width it is currently at.</summary>
        private void arrive()
        {
            route.Add((atX, atY));
            widths.Add(width);
        }

        ///
        ///Records a point part-way through a segment, at a width stepped towards where the segment ends.
        ///
        ///The taper is spread over the points the segment already has rather than the segment being cut
        ///into more of them: a bend is already a run of points, and a straight is two. So a taper along a
        ///straight is a straight-sided wedge, which is what one is, and a taper round a bend narrows as it
        ///turns.
        ///
        private void arrive(int from, int to, double through)
        {
            width = (int)Math.Round(from + ((to - from) * through));

            arrive();
        }

        ///<summary>The width the route is currently at, which the next segment starts from.</summary>
        public int Width
        {
            get { return width; }
        }

        ///<summary>Where the route has reached, and which way it is pointing, mid-build.</summary>
        public Element.Point At
        {
            get { return new Element.Point((int)Math.Round(atX), (int)Math.Round(atY)); }
        }

        public double HeadingDegrees
        {
            get { return headingRadians * 180 / Math.PI; }
        }

        ///<summary>
        ///Carries on the way it is pointing, for a distance.
        ///
        ///<paramref name="widthEnd"/> tapers to a new width by the end of this segment, and that width is
        ///what the next segment starts from. Null keeps the width the route is at.
        ///</summary>
        public PathBuilder Straight(double length, int? widthEnd = null)
        {
            atX += Math.Cos(headingRadians) * length;
            atY += Math.Sin(headingRadians) * length;

            width = widthEnd ?? width;

            arrive();

            return this;
        }

        ///
        ///Bends by an angle, around a circle of the given radius, and comes out pointing the new way.
        ///
        ///**A radius rather than a corner**, because a wire that turns a square corner is a wire with a
        ///current crowding at the outside of it - and because a bend of a stated radius is what a rule deck
        ///has something to say about. A radius of zero is a square corner, which is a legitimate thing to
        ///ask for and is what happens without this.
        ///
        ///`vertices` is per quarter turn, so a right angle and a half turn are cut equally finely rather
        ///than the second being twice as coarse.
        ///
        public PathBuilder BendDeg(double degrees, double radius, int? widthEnd = null, int vertices = DefaultBendVertices)
        {
            if (degrees == 0)
                return this;

            double sweep = degrees * Math.PI / 180;
            int from = width;
            int to = widthEnd ?? width;

            if (radius <= 0)
            {
                //No arc to walk: the route turns on the spot, which is the square corner. A width asked for
                //here lands on the corner itself, since there is no arc to spread it over.
                headingRadians += sweep;
                width = to;

                return this;
            }

            //The center of the turn is a quarter turn off the heading, on the side being turned towards.
            double quarter = -Math.PI / 2;

            if (sweep > 0)
                quarter = Math.PI / 2;

            double toCenter = headingRadians + quarter;

            double centerX = atX + (Math.Cos(toCenter) * radius);
            double centerY = atY + (Math.Sin(toCenter) * radius);

            //The angle from the center back out to where the route currently is.
            double fromCenter = toCenter + Math.PI;

            int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) * Math.Max(1, vertices)));

            for (int i = 1; i <= steps; i++)
            {
                double angle = fromCenter + (sweep * i / steps);

                atX = centerX + (Math.Cos(angle) * radius);
                atY = centerY + (Math.Sin(angle) * radius);

                arrive(from, to, (double)i / steps);
            }

            headingRadians += sweep;

            return this;
        }

        ///<summary>The same, in radians, for a caller already working in them.</summary>
        public PathBuilder Bend(double radians, double radius, int? widthEnd = null, int vertices = DefaultBendVertices)
        {
            return BendDeg(radians * 180 / Math.PI, radius, widthEnd, vertices);
        }

        ///
        ///Follows a Bézier curve, **placed relative to where the route has reached and which way it points**.
        ///
        ///The control points are given in the curve's own coordinates, as though the route were at the
        ///origin pointing along x - so the same curve can be dropped into a route at any angle without its
        ///numbers changing. Its first control point lands where the route is; the heading afterwards is the
        ///direction the curve was going when it ended.
        ///
        ///
        ///<paramref name="width"/> is the width along the curve, as a function of how far along it is - 0 at
        ///the start and 1 at the end. Null keeps the width the route is at. It is a function rather than an
        ///end value because a curve is where a width most often wants to do something other than ramp: a
        ///taper that follows the curvature, or one that pinches in the middle and comes back.
        ///
        public PathBuilder Bezier(
            Action<BezierBuilder> shape,
            Func<double, double>? width = null,
            int vertices = BezierBuilder.DefaultVertices)
        {
            var builder = new BezierBuilder();

            shape(builder);

            if (builder.Count < 2)
                return this;

            double cos = Math.Cos(headingRadians);
            double sin = Math.Sin(headingRadians);

            var start = builder.At(0);

            int steps = Math.Max(2, vertices);

            //Where the curve begins, so the walk below is a run of offsets from it.
            double fromX = atX;
            double fromY = atY;

            //Placed by the difference from its own first point, so the curve begins where the route is
            //rather than jumping to wherever it was drawn.
            for (int i = 1; i < steps; i++)
            {
                double through = (double)i / (steps - 1);

                var on = builder.At(through);

                double dx = on.X - start.X;
                double dy = on.Y - start.Y;

                atX = fromX + (dx * cos) - (dy * sin);
                atY = fromY + (dx * sin) + (dy * cos);

                if (width is not null)
                    this.width = (int)Math.Round(Math.Max(0, width(through)));

                arrive();
            }

            var last = route[^1];
            var before = route[^2];

            headingRadians = Math.Atan2(last.Y - before.Y, last.X - before.X);

            return this;
        }

        ///<summary>The route as it stands, rounded to database units, with repeated points dropped.</summary>
        public List<Element.Point> Centerline()
        {
            return walk().Points;
        }

        ///<summary>
        ///The width at each point of <see cref="Centerline"/>, for a caller outlining it themselves.
        ///
        ///The same length as the centerline and in the same order, which is the shape
        ///<see cref="PathOutline.Build(IReadOnlyList{Element.Point}, IReadOnlyList{int}, int, int, int)"/>
        ///takes.
        ///</summary>
        public List<int> Widths()
        {
            return walk().Widths;
        }

        ///
        ///The route rounded to database units, with repeated points - and their widths - dropped.
        ///
        ///**Both together, because they have to stay the same length.** Rounding can land two steps of a
        ///fine bend on the same unit, and a repeated point is a zero-length segment the outliner has no
        ///direction for. Dropping the point without its width would put every later width on the wrong
        ///point, so a taper would be drawn over the wrong part of the route.
        ///
        private (List<Element.Point> Points, List<int> Widths) walk()
        {
            var points = new List<Element.Point>();
            var carried = new List<int>();

            for (int i = 0; i < route.Count; i++)
            {
                var at = new Element.Point((int)Math.Round(route[i].X), (int)Math.Round(route[i].Y));

                if (points.Count > 0 && points[^1].X == at.X && points[^1].Y == at.Y)
                    continue;

                points.Add(at);
                carried.Add(widths[i]);
            }

            return (points, carried);
        }

        ///<summary>
        ///The route outlined at one width, as one closed shape.
        ///
        ///Through <see cref="PathOutline"/>, the same one a drawn path and a read `PATH` go through. This
        ///overrides whatever widths the route is carrying, which is what a route built without any wants.
        ///</summary>
        public List<Element.Point> BuildPolygon(int width)
        {
            var centerline = Centerline();

            if (centerline.Count < 2 || width <= 0)
                return centerline;

            return PathOutline.Build(centerline, width, 0, 0, 0);
        }

        ///
        ///The route outlined at the widths it is carrying, as one closed shape.
        ///
        ///For a route that was given a width to start with and changed it along the way. A route carrying no
        ///widths outlines to nothing here and wants <see cref="BuildPolygon(int)"/> instead - which is the
        ///honest failure, since a width of nothing has no shape and guessing one would draw a wire at a size
        ///nobody chose.
        ///
        public List<Element.Point> BuildPolygon()
        {
            var walked = walk();

            if (walked.Points.Count < 2)
                return walked.Points;

            return PathOutline.Build(walked.Points, walked.Widths, 0, 0, 0);
        }

        ///
        ///The route as centerlines short enough to be elements, each carrying on from the last.
        ///
        ///**Because one element has a vertex limit and a route does not.** A long bus flattened into a
        ///single boundary is a boundary readers are entitled to refuse; cut into pieces it is the same
        ///drawing in several elements. The pieces overlap by one point so the run is continuous rather than
        ///dotted - each begins where the one before it ended.
        ///
        public List<List<Element.Point>> Build(int maxVertices = MostVerticesPerElement)
        {
            var pieces = new List<List<Element.Point>>();
            var centerline = Centerline();

            if (centerline.Count == 0)
                return pieces;

            int most = Math.Max(2, maxVertices);

            for (int at = 0; at < centerline.Count - 1; at += most - 1)
            {
                int take = Math.Min(most, centerline.Count - at);

                pieces.Add(centerline.GetRange(at, take));
            }

            //A route of one point encloses nothing and goes nowhere, but it is still where somebody put it.
            if (pieces.Count == 0)
                pieces.Add(centerline);

            return pieces;
        }
    }
}
