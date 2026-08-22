using GdsII;

namespace GDSViewer.Tests
{
    ///
    ///The flattener, against the closed forms rather than against whatever the reader happened to produce.
    ///
    ///**Accuracy rather than point counts.** A test that pins a curve to sixteen points is a test of the
    ///implementation, and it fails the day the implementation gets better - which is exactly what happened
    ///when the fixed sixty-four sides became a tolerance. What is worth asserting is the promise: no edge
    ///sits further off the true curve than it said it would.
    ///
    public class DxfCurveTests
    {
        ///<summary>A nanometer in a drawing measured in microns, which is what the reader hands down.</summary>
        private const double Tolerance = 0.001;

        ///<summary>The furthest any edge's middle sits from the circle it stands in for.</summary>
        private static double WorstSagitta(List<(double X, double Y)> points, double centerX, double centerY, double radius)
        {
            double worst = 0;

            for (int i = 0; i + 1 < points.Count; i++)
            {
                double midX = (points[i].X + points[i + 1].X) / 2;
                double midY = (points[i].Y + points[i + 1].Y) / 2;

                double away = Math.Sqrt(((midX - centerX) * (midX - centerX)) + ((midY - centerY) * (midY - centerY)));

                worst = Math.Max(worst, Math.Abs(radius - away));
            }

            return worst;
        }

        private static double Distance((double X, double Y) from, (double X, double Y) to)
        {
            return Math.Sqrt(((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));
        }


        #region Arcs ************************************************************

        ///
        ///The promise the whole thing is built on: whatever the radius, no edge is further off than asked.
        ///
        [Theory]
        [InlineData(0.05)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(100)]
        [InlineData(200)]
        public void A_circle_is_flattened_to_within_the_tolerance(double radius)
        {
            var points = DxfCurves.Arc(0, 0, radius, 0, 2 * Math.PI, Tolerance);

            Assert.True(WorstSagitta(points, 0, 0, radius) <= Tolerance * 1.000001,
                $"radius {radius} came out {WorstSagitta(points, 0, 0, radius)} off with {points.Count} points");
        }

        ///
        ///Past the cap the tolerance is the thing that gives, and this is what it costs.
        ///
        ///At a nanometer the cap starts binding on a radius of about two hundred microns - a circle needs
        ///π/√(2·tolerance/radius) sides to hold the error, so a five-millimeter one would want five thousand
        ///of them. It gets a thousand and twenty-four and comes out twenty-four nanometers off, which is the
        ///trade the cap exists to make, named here rather than discovered by somebody measuring a hole.
        ///
        [Fact]
        public void Past_the_cap_it_is_the_tolerance_that_gives()
        {
            var points = DxfCurves.Arc(0, 0, 5000, 0, 2 * Math.PI, Tolerance);

            Assert.Equal(DxfCurves.MostSegments + 1, points.Count);
            Assert.InRange(WorstSagitta(points, 0, 0, 5000), Tolerance, 0.03);
        }

        ///
        ///And it is a tolerance rather than a count: a bigger circle gets more sides, which a fixed sixty-four
        ///did not.
        ///
        [Fact]
        public void A_bigger_circle_is_given_more_sides()
        {
            int small = DxfCurves.Arc(0, 0, 1, 0, 2 * Math.PI, Tolerance).Count;
            int large = DxfCurves.Arc(0, 0, 1000, 0, 2 * Math.PI, Tolerance).Count;

            Assert.True(large > small, $"{large} sides for the large one against {small} for the small");
        }

        ///<summary>Neither can run away: a half-millimeter circle at a nanometer would want thousands.</summary>
        [Fact]
        public void No_curve_is_given_more_sides_than_the_cap()
        {
            var points = DxfCurves.Arc(0, 0, 100000, 0, 2 * Math.PI, Tolerance);

            Assert.True(points.Count <= DxfCurves.MostSegments + 1, $"{points.Count} points");
        }

        ///<summary>Nor collapse: a curve smaller than the tolerance is still a shape.</summary>
        [Fact]
        public void A_curve_finer_than_the_tolerance_is_still_a_shape()
        {
            var points = DxfCurves.Arc(0, 0, 0.0002, 0, 2 * Math.PI, Tolerance);

            Assert.True(points.Count >= DxfCurves.FewestSegments, $"{points.Count} points");
        }

        ///<summary>A quarter of a turn asks for about a quarter of the sides, not a whole turn's worth.</summary>
        [Fact]
        public void A_short_arc_is_not_given_a_whole_circles_worth()
        {
            int quarter = DxfCurves.SegmentsFor(10, Math.PI / 2, Tolerance);
            int whole = DxfCurves.SegmentsFor(10, 2 * Math.PI, Tolerance);

            Assert.InRange(quarter, whole / 4 - 1, whole / 4 + 1);
        }

        #endregion **************************************************************


        #region Bulges **********************************************************

        ///
        ///A bulge of one is a semicircle - the definition, and the case every other one is checked against.
        ///
        ///**Under the chord, and that is not a typo.** A positive bulge is counterclockwise, and going
        ///counterclockwise around a circle from its leftmost point sets off downwards - the tangent at angle
        ///π is straight down. So the arc bows to the right of the direction of travel, which for a run along
        ///+x is downwards. Worth pinning, because the first reading of "positive" is "upwards" and it is
        ///wrong for half the runs in any drawing.
        ///
        [Fact]
        public void A_bulge_of_one_is_a_semicircle()
        {
            var points = DxfCurves.Bulge((0, 0), (10, 0), 1, Tolerance);

            //The chord is 10 across, so the arc is a half circle of radius 5 about the middle of it.
            Assert.True(WorstSagitta(points, 5, 0, 5) <= Tolerance * 1.000001);

            double lowest = points.Min(one => one.Y);

            Assert.InRange(lowest, -5, -5 + Tolerance);
            Assert.True(points.Max(one => one.Y) <= Tolerance);
        }

        ///<summary>And a negative one is the same arc the other way, which is over the top.</summary>
        [Fact]
        public void A_negative_bulge_bows_the_other_way()
        {
            var points = DxfCurves.Bulge((0, 0), (10, 0), -1, Tolerance);

            Assert.True(WorstSagitta(points, 5, 0, 5) <= Tolerance * 1.000001);

            double highest = points.Max(one => one.Y);

            Assert.InRange(highest, 5 - Tolerance, 5);
            Assert.True(points.Min(one => one.Y) >= -Tolerance);
        }

        ///<summary>A quarter turn: tan of a quarter of ninety degrees.</summary>
        [Fact]
        public void A_bulge_of_a_quarter_turn_bows_by_the_right_amount()
        {
            double bulge = Math.Tan(Math.PI / 8);

            var points = DxfCurves.Bulge((0, 0), (1, 1), bulge, Tolerance);

            //The center of that arc is at (0, 1) with radius 1, which is the whole claim.
            Assert.True(WorstSagitta(points, 0, 1, 1) <= Tolerance * 1.000001);

            foreach (var point in points)
                Assert.InRange(Distance(point, (0, 1)), 1 - Tolerance, 1 + Tolerance);
        }

        ///<summary>Zero is a straight segment, and costs nothing.</summary>
        [Fact]
        public void A_bulge_of_zero_is_a_straight_segment()
        {
            var points = DxfCurves.Bulge((0, 0), (10, 0), 0, Tolerance);

            Assert.Single(points);
            Assert.Equal((0, 0), points[0]);
        }

        ///
        ///A run where only one vertex bows: the bulge belongs to the segment leaving that vertex, which is
        ///the thing most easily got one place out.
        ///
        [Fact]
        public void A_bulge_belongs_to_the_segment_leaving_its_vertex()
        {
            var vertices = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10) };
            var bulges = new List<double> { 0, 1, 0 };

            var points = DxfCurves.Bulged(vertices, bulges, closed: false, Tolerance);

            //The first segment is straight, so nothing between (0,0) and (10,0) leaves the x axis.
            foreach (var point in points)
            {
                if (point.X < 10 - Tolerance)
                    Assert.Equal(0, point.Y, 6);
            }

            //The second bows out to the right of the straight line between (10,0) and (10,10).
            double furthest = points.Max(one => one.X);

            Assert.InRange(furthest, 15 - Tolerance, 15);
        }

        ///<summary>And the last vertex of a closed run bows the segment back to the first.</summary>
        [Fact]
        public void The_last_bulge_of_a_closed_run_closes_it()
        {
            var vertices = new List<(double X, double Y)> { (0, 0), (10, 0) };
            var bulges = new List<double> { 1, 1 };

            var points = DxfCurves.Bulged(vertices, bulges, closed: true, Tolerance);

            //Two semicircles back to back, which is a circle: everything is 5 from the middle of the chord.
            foreach (var point in points)
                Assert.InRange(Distance(point, (5, 0)), 5 - Tolerance, 5 + Tolerance);

            //Both halves are there rather than one.
            Assert.True(points.Max(one => one.Y) > 4.9);
            Assert.True(points.Min(one => one.Y) < -4.9);
        }

        ///<summary>An open run ends on its last vertex, which no segment leaves.</summary>
        [Fact]
        public void An_open_run_keeps_its_last_vertex()
        {
            var vertices = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10) };

            var points = DxfCurves.Bulged(vertices, new List<double>(), closed: false, Tolerance);

            Assert.Equal(3, points.Count);
            Assert.Equal((10.0, 10.0), points[^1]);
        }

        ///<summary>A run with no bulges at all is the run itself, which is what it was before any of this.</summary>
        [Fact]
        public void A_run_with_no_bulges_is_the_run()
        {
            var vertices = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (0, 10) };

            var points = DxfCurves.Bulged(vertices, new List<double>(), closed: true, Tolerance);

            Assert.Equal(vertices, points);
        }

        #endregion **************************************************************
    }
}
