namespace GdsII
{
    ///
    ///A Bézier curve, built from control points and flattened into corners a layout can hold.
    ///
    ///**By de Casteljau rather than by the NURBS evaluator next door.** <see cref="DxfCurves.At"/> can
    ///evaluate a Bézier - one is a NURBS with a clamped uniform knot vector - but it needs that knot vector
    ///handed to it, and a caller placing four control points should not have to know what a knot is. The
    ///recursion here is six lines and says what it does.
    ///
    ///**The curve passes through the first and last point and no others.** That is what a Bézier is, and it
    ///is the thing people are most often surprised by: the middle points pull the curve towards themselves
    ///without being on it.
    ///
    ///Coordinates are taken as doubles and rounded once, at the end, so a long curve does not walk off its
    ///true path a unit at a time.
    ///
    public sealed class BezierBuilder
    {
        ///
        ///How many control points one curve may carry.
        ///
        ///A Bernstein basis of degree n needs binomial coefficients up to n choose n/2, and past about
        ///twenty those stop being exactly representable while the curve itself stops being controllable -
        ///moving one point of a twenty-point curve changes the whole of it. Sixteen is where the other
        ///implementations of this stop, so a curve written for one is a curve this takes.
        ///
        public const int MostControlPoints = 16;

        ///<summary>How many straight pieces a curve is cut into when nobody says.</summary>
        public const int DefaultVertices = 64;

        private readonly List<(double X, double Y)> controls = new List<(double, double)>();

        ///<summary>
        ///Adds a control point. The first and last are on the curve; the ones between pull it towards them.
        ///</summary>
        public BezierBuilder AddPoint(double x, double y)
        {
            if (controls.Count >= MostControlPoints)
                throw new InvalidOperationException($"A Bézier curve takes at most {MostControlPoints} control points.");

            controls.Add((x, y));

            return this;
        }

        ///<summary>The same, for a caller already holding layout coordinates.</summary>
        public BezierBuilder AddPoint(Element.Point at)
        {
            return AddPoint(at.X, at.Y);
        }

        ///<summary>How many control points have been placed, for a caller assembling one in a loop.</summary>
        public int Count
        {
            get { return controls.Count; }
        }

        ///
        ///The curve as a centerline: an open run of points from the first control point to the last.
        ///
        ///`vertices` is how many points come out, not how many pieces - so two is the straight line between
        ///the ends and anything below that is refused rather than quietly rounded up into a shape nobody
        ///asked for.
        ///
        public List<Element.Point> BuildCenterline(int vertices = DefaultVertices)
        {
            var points = new List<Element.Point>();

            if (controls.Count == 0)
                return points;

            if (controls.Count == 1)
            {
                points.Add(new Element.Point((int)Math.Round(controls[0].X), (int)Math.Round(controls[0].Y)));

                return points;
            }

            int steps = Math.Max(2, vertices);

            for (int i = 0; i < steps; i++)
            {
                var at = At((double)i / (steps - 1));

                points.Add(new Element.Point((int)Math.Round(at.X), (int)Math.Round(at.Y)));
            }

            return points;
        }

        ///
        ///The curve as a closed outline of a given width - a ribbon along it.
        ///
        ///**Through the same outliner a drawn path goes through**, so a curve built here and a path drawn
        ///with the mouse are mitered, capped and wound the same way. Building a second offsetter here would
        ///be a second set of answers about what a corner does, and the two would eventually differ on the
        ///sharp ones.
        ///
        ///Recommended over keeping the curve as a `PATH`: a path's width is applied by whatever reads the
        ///file, and readers differ about the ends, where an outline is the shape itself and cannot be read
        ///two ways.
        ///
        public List<Element.Point> BuildPolygon(int width, int vertices = DefaultVertices)
        {
            var centerline = BuildCenterline(vertices);

            if (centerline.Count < 2 || width <= 0)
                return centerline;

            //PATHTYPE 0 and no extensions: the ribbon stops exactly on the end points, which is where the
            //curve stops. Anything else would make the shape longer than the curve it was built from.
            return PathOutline.Build(centerline, width, 0, 0, 0);
        }

        ///
        ///One point on the curve, at <paramref name="t"/> from 0 at the first control point to 1 at the last.
        ///
        ///De Casteljau's algorithm: repeatedly take each neighbouring pair of points and step
        ///<paramref name="t"/> of the way between them, until one point is left. It is slower than
        ///evaluating the Bernstein polynomials and it is numerically stable at every degree, which matters
        ///more here than the arithmetic does.
        ///
        public (double X, double Y) At(double t)
        {
            if (controls.Count == 0)
                throw new InvalidOperationException("A curve with no control points has no points on it.");

            var working = new (double X, double Y)[controls.Count];

            controls.CopyTo(working);

            for (int level = working.Length - 1; level > 0; level--)
            {
                for (int i = 0; i < level; i++)
                {
                    working[i] = (
                        working[i].X + ((working[i + 1].X - working[i].X) * t),
                        working[i].Y + ((working[i + 1].Y - working[i].Y) * t));
                }
            }

            return working[0];
        }
    }
}
