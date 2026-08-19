namespace GdsII
{
    ///
    ///Turning the curves a drawing has into the straight runs a layout can hold.
    ///
    ///**A layout format has no curves and never has.** GDSII holds integer points and edges between them, so
    ///every arc, circle, ellipse and spline in a DXF has to become a polyline on the way in. The only
    ///question is how finely, and that is a real decision rather than a detail: too coarse and a round hole
    ///is visibly a polygon, too fine and a drawing of a few hundred circles becomes a file of a few million
    ///points.
    ///
    ///**Answered as an error rather than as a count.** Everything here flattens until the straight edge sits
    ///within <see cref="Tolerance"/> of the true curve - the sagitta, the gap at the middle of the chord -
    ///so a large circle gets the segments it needs and a small one does not pay for them. A fixed count
    ///cannot do that: sixty-four sides is a tenth of a micron out on a half-millimeter circle and eight
    ///wasted points on a hundred-nanometer one.
    ///
    ///The tolerance itself is given in drawing units by the caller, which is what lets it be stated once in
    ///database units and hold whatever the drawing is measured in - see <see cref="DxfReader"/>.
    ///
    ///**Nothing here knows what a DXF is.** These take numbers and return points, which is what makes them
    ///testable against the closed forms rather than against whatever the reader happened to produce.
    ///
    public static class DxfCurves
    {
        #region Constants *******************************************************************

        ///
        ///The fewest and the most straight sides a full turn is ever given.
        ///
        ///The floor stops a curve smaller than the tolerance collapsing to a line - at a tolerance of a
        ///nanometer that needs a circle about two nanometers across, which is not a thing anybody drew, but
        ///a shape that silently becomes an edge is worth one line of code to prevent.
        ///
        ///The ceiling is what stops a circle the size of a die asking for tens of thousands of points. It
        ///binds at about a millimeter across: at half a millimeter the error is still inside a nanometer, at
        ///five millimeters it is twenty-four - which is the trade, and past that the tolerance is what gives
        ///rather than the file size.
        ///
        public const int FewestSegments = 8;
        public const int MostSegments = 1024;

        #endregion **************************************************************************



        #region Coordinate systems **********************************************************

        ///
        ///A point in an entity's own coordinate system, as a point in the drawing's.
        ///
        ///**Most DXF coordinates are not in the drawing's coordinate system.** An entity carries an
        ///extrusion vector - group codes 210, 220 and 230 - and its points are measured in a plane
        ///perpendicular to it. The vector is (0, 0, 1) almost every time, which is why a reader can ignore
        ///it for years and then be silently wrong: an entity drawn on a face pointing the other way has an
        ///extrusion of (0, 0, -1), and taking its X and Y as written mirrors it.
        ///
        ///**The Arbitrary Axis Algorithm**, which is the format's own name for it and is this short: pick a
        ///world axis that is not nearly parallel to the extrusion, cross it with the extrusion to get the
        ///entity's X, cross that back to get its Y. The sixty-fourth is the format's threshold, not a
        ///rounding - it is what decides which of the two world axes to start from.
        ///
        public static (double X, double Y) ToWorld(double normalX, double normalY, double normalZ, double x, double y)
        {
            double length = Math.Sqrt((normalX * normalX) + (normalY * normalY) + (normalZ * normalZ));

            //No extrusion written at all, which is the same as the one that changes nothing.
            if (length == 0)
                return (x, y);

            double nx = normalX / length;
            double ny = normalY / length;
            double nz = normalZ / length;

            //The overwhelmingly common case, kept exact rather than run through the arithmetic below.
            if (nx == 0 && ny == 0 && nz == 1)
                return (x, y);

            double ax;
            double ay;
            double az;

            //Nearly parallel to the world Z, so the world Y is what to start from - crossing with something
            //nearly parallel to yourself gives a vector of nearly no length, and then a normalize of noise.
            if (Math.Abs(nx) < 1.0 / 64 && Math.Abs(ny) < 1.0 / 64)
                (ax, ay, az) = Cross(0, 1, 0, nx, ny, nz);
            else
                (ax, ay, az) = Cross(0, 0, 1, nx, ny, nz);

            (ax, ay, az) = Unit(ax, ay, az);

            (double bx, double by, double bz) = Unit(Cross(nx, ny, nz, ax, ay, az));

            //The Z of the point is not carried: this reads a layout, and a layout is flat. What the third
            //axis would contribute is a height, and there is nowhere to put one.
            return ((ax * x) + (bx * y), (ay * x) + (by * y));
        }

        private static (double X, double Y, double Z) Cross(double ax, double ay, double az, double bx, double by, double bz)
        {
            return ((ay * bz) - (az * by), (az * bx) - (ax * bz), (ax * by) - (ay * bx));
        }

        private static (double X, double Y, double Z) Unit((double X, double Y, double Z) vector)
        {
            return Unit(vector.X, vector.Y, vector.Z);
        }

        private static (double X, double Y, double Z) Unit(double x, double y, double z)
        {
            double length = Math.Sqrt((x * x) + (y * y) + (z * z));

            if (length == 0)
                return (0, 0, 0);

            return (x / length, y / length, z / length);
        }

        #endregion **************************************************************************



        #region Arcs ************************************************************************

        ///
        ///How many straight segments a sweep of a circle needs to stay within the tolerance.
        ///
        ///The sagitta of a chord subtending an angle a on radius r is r(1 - cos(a/2)), so the widest angle
        ///one segment may span is 2·acos(1 - tolerance/r). Everything else here is clamping.
        ///
        public static int SegmentsFor(double radius, double sweepRadians, double tolerance)
        {
            double sweep = Math.Abs(sweepRadians);

            if (sweep <= 0)
                return 1;

            //A tolerance at or past the diameter cannot be exceeded by anything, so the floor decides.
            double widest;

            if (radius <= 0 || tolerance >= 2 * radius)
                widest = Math.PI;
            else
                widest = 2 * Math.Acos(1 - (tolerance / radius));

            //Both bounds are scaled by how much of a turn this is, so a ninety-degree arc gets a quarter of
            //the floor and a quarter of the ceiling rather than a full turn's worth of either.
            double turns = sweep / (2 * Math.PI);

            int fewest = Math.Max(1, (int)Math.Ceiling(FewestSegments * turns));
            int most = Math.Max(fewest, (int)Math.Ceiling(MostSegments * turns));

            int needed = (int)Math.Ceiling(sweep / widest);

            return Math.Clamp(needed, fewest, most);
        }

        ///
        ///An arc, as the points that stand in for it - both ends included.
        ///
        ///Angles in radians and counterclockwise, which is the direction DXF measures in. A negative sweep
        ///runs the other way, which is what a negative bulge means.
        ///
        public static List<(double X, double Y)> Arc(
            double centerX,
            double centerY,
            double radius,
            double fromRadians,
            double sweepRadians,
            double tolerance)
        {
            var points = new List<(double, double)>();

            if (radius <= 0)
                return points;

            int steps = SegmentsFor(radius, sweepRadians, tolerance);

            for (int i = 0; i <= steps; i++)
            {
                double angle = fromRadians + (sweepRadians * i / steps);

                points.Add((centerX + (radius * Math.Cos(angle)), centerY + (radius * Math.Sin(angle))));
            }

            return points;
        }

        ///
        ///The arc a bulge describes, between the two points it sits between.
        ///
        ///**A bulge is the tangent of a quarter of the sweep.** That is the whole definition, and it is why
        ///one is 1 for a semicircle - tan(90°) is not 1, tan(45°) is, and a semicircle sweeps 180. The sign
        ///is the direction: positive counterclockwise, negative clockwise.
        ///
        ///Everything else follows. The chord and the sweep give the radius; the radius and half the sweep
        ///give how far off the chord's middle the center sits, on the left of the run for a positive bulge.
        ///
        ///The returned run **excludes its last point**, so a polyline can concatenate one segment's arc
        ///after another without repeating the vertex they share. A bulge of zero returns just the start,
        ///which is the straight segment it means.
        ///
        public static List<(double X, double Y)> Bulge(
            (double X, double Y) from,
            (double X, double Y) to,
            double bulge,
            double tolerance)
        {
            var points = new List<(double, double)> { from };

            if (bulge == 0)
                return points;

            double dx = to.X - from.X;
            double dy = to.Y - from.Y;

            double chord = Math.Sqrt((dx * dx) + (dy * dy));

            //Two vertices in the same place have no chord to bow, whatever the bulge says.
            if (chord <= 0)
                return points;

            double sweep = 4 * Math.Atan(bulge);
            double half = sweep / 2;

            double sine = Math.Sin(half);

            if (sine == 0)
                return points;

            double radius = chord / (2 * sine);

            //Out along the chord's own left normal by the apothem, from the middle of it. Signed throughout,
            //so a clockwise bulge puts the center on the other side without a case for it.
            double apothem = radius * Math.Cos(half);

            double centerX = from.X + (dx / 2) - (dy / chord * apothem);
            double centerY = from.Y + (dy / 2) + (dx / chord * apothem);

            double start = Math.Atan2(from.Y - centerY, from.X - centerX);

            var arc = Arc(centerX, centerY, Math.Abs(radius), start, sweep, tolerance);

            //The last point is the vertex this segment ends on, which the next one starts from.
            if (arc.Count > 1)
                arc.RemoveAt(arc.Count - 1);

            return arc;
        }

        ///
        ///A run of vertices where any of them may bow into an arc, as one flat run of points.
        ///
        ///The bulge on a vertex describes the segment **leaving** it, which is the part that is easy to get
        ///backwards: the last vertex's bulge belongs to the closing segment on a closed run and to nothing
        ///at all on an open one.
        ///
        public static List<(double X, double Y)> Bulged(
            List<(double X, double Y)> vertices,
            List<double> bulges,
            bool closed,
            double tolerance)
        {
            var points = new List<(double, double)>();

            if (vertices.Count == 0)
                return points;

            if (vertices.Count == 1)
            {
                points.Add(vertices[0]);

                return points;
            }

            int last;

            if (closed)
                last = vertices.Count;
            else
                last = vertices.Count - 1;

            for (int i = 0; i < last; i++)
            {
                double bulge = 0;

                if (i < bulges.Count)
                    bulge = bulges[i];

                points.AddRange(Bulge(vertices[i], vertices[(i + 1) % vertices.Count], bulge, tolerance));
            }

            //An open run ends on its last vertex, which no segment leaves and so nothing has added yet.
            if (!closed)
                points.Add(vertices[^1]);

            return points;
        }

        #endregion **************************************************************************



        #region Ellipses ********************************************************************

        ///
        ///An elliptical arc, from the shape DXF describes one in: a center, the vector to the end of the
        ///major axis, how long the minor one is as a fraction of it, and the parameters to run between.
        ///
        ///**The parameter is not the angle.** A point at parameter t is center + major·cos t + minor·sin t,
        ///which sweeps the ellipse but not at a constant angular rate - t of 45° is not 45° round the shape
        ///unless it happens to be a circle.
        ///
        ///Stepped as though it were a circle of the major radius, which is the conservative reading: the
        ///curvature is highest at the ends of the major axis and the step that is fine enough there is more
        ///than fine enough everywhere else. It costs points on a very flat ellipse and it cannot be wrong,
        ///which is the right way round for a tolerance.
        ///
        public static List<(double X, double Y)> Ellipse(
            double centerX,
            double centerY,
            double majorX,
            double majorY,
            double ratio,
            double fromParameter,
            double sweepParameter,
            double tolerance)
        {
            var points = new List<(double, double)>();

            double major = Math.Sqrt((majorX * majorX) + (majorY * majorY));

            if (major <= 0)
                return points;

            //The minor axis is the major turned a quarter turn and shortened. A ratio that is missing or
            //nonsense is taken as a circle, which is what an ellipse with no minor axis would otherwise be:
            //a line.
            if (ratio <= 0 || ratio > 1)
                ratio = 1;

            double minorX = -majorY * ratio;
            double minorY = majorX * ratio;

            int steps = SegmentsFor(major, sweepParameter, tolerance);

            for (int i = 0; i <= steps; i++)
            {
                double t = fromParameter + (sweepParameter * i / steps);

                double cos = Math.Cos(t);
                double sin = Math.Sin(t);

                points.Add((
                    centerX + (majorX * cos) + (minorX * sin),
                    centerY + (majorY * cos) + (minorY * sin)));
            }

            return points;
        }

        #endregion **************************************************************************



        #region Splines *********************************************************************

        ///
        ///How deep the subdivision below is allowed to go. Ten halvings is a thousand segments out of one,
        ///which is past the point where the cap on a full turn would have stopped an arc.
        ///
        private const int MostSubdivisions = 10;

        ///
        ///A NURBS curve, flattened.
        ///
        ///**By subdivision rather than by a step count**, because a spline has no single radius to derive a
        ///step from - it can be nearly straight over one span and turn sharply over the next. Each span is
        ///halved until the curve's own midpoint is within the tolerance of the straight line between its
        ///ends, which is the same sagitta test the arcs use and gives the same guarantee without needing to
        ///know the curvature in advance.
        ///
        ///`knots` and `weights` are the file's own. A curve with no weights is not rational and every
        ///control point counts the same; one with the wrong number of knots is not a curve this can
        ///evaluate, and comes back as the control polygon rather than as nothing - which is the shape
        ///somebody drew, drawn coarsely, and is a great deal more use than a missing entity.
        ///
        public static List<(double X, double Y)> Spline(
            List<(double X, double Y)> controlPoints,
            List<double> knots,
            List<double> weights,
            int degree,
            bool closed,
            double tolerance)
        {
            var points = new List<(double, double)>();

            if (controlPoints.Count == 0)
                return points;

            if (controlPoints.Count == 1)
            {
                points.Add(controlPoints[0]);

                return points;
            }

            //A degree the file did not give, or one the control points cannot support.
            if (degree < 1)
                degree = 3;

            if (degree > controlPoints.Count - 1)
                degree = controlPoints.Count - 1;

            //A B-spline of degree d over n control points needs n + d + 1 knots. Anything else is a curve
            //this cannot evaluate, so the control polygon stands in.
            if (knots.Count != controlPoints.Count + degree + 1)
                return Polygon(controlPoints, closed);

            double first = knots[degree];
            double last = knots[controlPoints.Count];

            if (!(last > first))
                return Polygon(controlPoints, closed);

            //The ends are exact; everything between them is found by halving.
            var start = At(controlPoints, knots, weights, degree, first);
            var finish = At(controlPoints, knots, weights, degree, last);

            points.Add(start);

            subdivide(points, controlPoints, knots, weights, degree, first, last, start, finish, tolerance, 0);

            points.Add(finish);

            return points;
        }

        ///<summary>The control points as the run they outline, which is what a curve too broken to evaluate
        ///still says about where it goes.</summary>
        private static List<(double X, double Y)> Polygon(List<(double X, double Y)> controlPoints, bool closed)
        {
            var points = new List<(double, double)>(controlPoints);

            if (closed && points.Count > 1)
                points.Add(points[0]);

            return points;
        }

        ///
        ///Halves a span until the middle of the curve sits within the tolerance of the middle of the chord,
        ///adding what it finds in order and excluding both ends - the caller owns those.
        ///
        private static void subdivide(
            List<(double X, double Y)> into,
            List<(double X, double Y)> controlPoints,
            List<double> knots,
            List<double> weights,
            int degree,
            double from,
            double to,
            (double X, double Y) start,
            (double X, double Y) finish,
            double tolerance,
            int depth)
        {
            double middle = (from + to) / 2;

            var at = At(controlPoints, knots, weights, degree, middle);

            if (depth >= MostSubdivisions || Near(at, start, finish, tolerance))
            {
                into.Add(at);

                return;
            }

            subdivide(into, controlPoints, knots, weights, degree, from, middle, start, at, tolerance, depth + 1);

            into.Add(at);

            subdivide(into, controlPoints, knots, weights, degree, middle, to, at, finish, tolerance, depth + 1);
        }

        ///<summary>How far a point sits off the straight line between two others, against the tolerance.</summary>
        private static bool Near((double X, double Y) point, (double X, double Y) from, (double X, double Y) to, double tolerance)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;

            double length = Math.Sqrt((dx * dx) + (dy * dy));

            //A chord of no length means the two ends have met, which happens on a closed curve's whole span
            //- the distance to the point is the only measure left.
            if (length <= 0)
            {
                double ax = point.X - from.X;
                double ay = point.Y - from.Y;

                return Math.Sqrt((ax * ax) + (ay * ay)) <= tolerance;
            }

            double across = Math.Abs(((point.X - from.X) * dy) - ((point.Y - from.Y) * dx)) / length;

            return across <= tolerance;
        }

        ///
        ///One point on the curve, by de Boor's algorithm.
        ///
        ///Rational when there are weights: the control points are lifted into homogeneous coordinates, the
        ///same recursion runs over them, and the result is divided back down - which is the whole of what
        ///the R in NURBS is.
        ///
        public static (double X, double Y) At(
            List<(double X, double Y)> controlPoints,
            List<double> knots,
            List<double> weights,
            int degree,
            double parameter)
        {
            int span = SpanOf(knots, controlPoints.Count, degree, parameter);

            //The d + 1 control points the span depends on, in homogeneous form.
            var x = new double[degree + 1];
            var y = new double[degree + 1];
            var w = new double[degree + 1];

            for (int i = 0; i <= degree; i++)
            {
                int index = span - degree + i;

                double weight = 1;

                if (index >= 0 && index < weights.Count && weights[index] > 0)
                    weight = weights[index];

                x[i] = controlPoints[index].X * weight;
                y[i] = controlPoints[index].Y * weight;
                w[i] = weight;
            }

            for (int level = 1; level <= degree; level++)
            {
                for (int i = degree; i >= level; i--)
                {
                    int index = span - degree + i;

                    double lower = knots[index];
                    double upper = knots[index + degree - level + 1];

                    double alpha;

                    if (upper > lower)
                        alpha = (parameter - lower) / (upper - lower);
                    else
                        alpha = 0;

                    x[i] = ((1 - alpha) * x[i - 1]) + (alpha * x[i]);
                    y[i] = ((1 - alpha) * y[i - 1]) + (alpha * y[i]);
                    w[i] = ((1 - alpha) * w[i - 1]) + (alpha * w[i]);
                }
            }

            if (w[degree] == 0)
                return (x[degree], y[degree]);

            return (x[degree] / w[degree], y[degree] / w[degree]);
        }

        ///<summary>Which knot span a parameter falls in, clamped to the ones the curve is defined over.</summary>
        private static int SpanOf(List<double> knots, int controlPoints, int degree, double parameter)
        {
            //The domain is [knots[degree], knots[controlPoints]]; the end belongs to the last span rather
            //than to a span of its own.
            if (parameter >= knots[controlPoints])
                return controlPoints - 1;

            if (parameter <= knots[degree])
                return degree;

            int low = degree;
            int high = controlPoints;
            int middle = (low + high) / 2;

            while (parameter < knots[middle] || parameter >= knots[middle + 1])
            {
                if (parameter < knots[middle])
                    high = middle;
                else
                    low = middle;

                middle = (low + high) / 2;
            }

            return middle;
        }

        #endregion **************************************************************************
    }
}
