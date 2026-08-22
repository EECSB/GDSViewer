using GdsII;

namespace GDSViewer.Tests;

///
///Building geometry from code: shapes, curves and routes.
///
///**A layout format has no curves**, so everything here ends as a list of corners and the interesting part
///is how faithful that list is to the thing it stands for. Counting the corners says almost nothing - a
///circle with the right number of points in the wrong places counts the same - so what is asserted is the
///shape: where its extremes are, how much area it encloses against the closed form, and where a route
///actually arrives.
///
public class ShapeBuilderTests
{
    ///<summary>How far a point is from the middle of the shape it belongs to.</summary>
    private static double DistanceFrom(int centerX, int centerY, Element.Point at)
    {
        double dx = at.X - centerX;
        double dy = at.Y - centerY;

        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    #region Shapes **********************************************************************

    ///<summary>A rectangle is its four corners, about the point it was centered on.</summary>
    [Fact]
    public void A_rectangle_is_centered_on_the_point_it_was_given()
    {
        var corners = Shapes.Rectangle(1000, 2000, 400, 200);

        Assert.Equal(4, corners.Count);

        var box = Bounds.Of(corners);

        Assert.Equal(800, box.Left);
        Assert.Equal(1200, box.Right);
        Assert.Equal(1900, box.Bottom);
        Assert.Equal(2100, box.Top);
    }

    ///
    ///An odd size keeps the size rather than losing a unit to the middle.
    ///
    ///A width of 401 cannot be split evenly about a whole-numbered center, and the two ways to be wrong are
    ///a shape a unit narrow or a shape a half-unit off its center. A layout cares about the first: a wire
    ///that is 139 wide fails a 140 rule, and one that is centered a half-unit off does not fail anything.
    ///
    [Fact]
    public void An_odd_size_stays_the_size_that_was_asked_for()
    {
        var box = Bounds.Of(Shapes.Rectangle(0, 0, 401, 201));

        Assert.Equal(401, box.Right - box.Left);
        Assert.Equal(201, box.Top - box.Bottom);
    }

    ///<summary>Corner to corner, whichever way round the two corners arrive.</summary>
    [Theory]
    [InlineData(0, 0, 100, 50)]
    [InlineData(100, 50, 0, 0)]
    [InlineData(0, 50, 100, 0)]
    public void Between_takes_its_corners_either_way_round(int x1, int y1, int x2, int y2)
    {
        var box = Bounds.Of(Shapes.Between(x1, y1, x2, y2));

        Assert.Equal(0, box.Left);
        Assert.Equal(100, box.Right);
        Assert.Equal(0, box.Bottom);
        Assert.Equal(50, box.Top);
    }

    ///
    ///**Every corner of a circle is on it**, to within the database unit a file stores coordinates in.
    ///
    ///Inscribed rather than circumscribed is the reading that matters in a layout: the polygon's edges run
    ///inside the radius, so it does not bulge past the spacing the radius was chosen to satisfy. The unit of
    ///slack is rounding and nothing else - a corner at 45° on a radius of 500 is at 353.553, and the nearest
    ///whole coordinate is 0.63 further out. Rounding inward would buy the stronger claim by shrinking every
    ///shape systematically, which is the worse trade.
    ///
    [Fact]
    public void Every_corner_of_a_circle_is_on_it_to_within_a_database_unit()
    {
        var corners = Shapes.Circle(0, 0, 500, 64);

        Assert.Equal(64, corners.Count);

        foreach (var at in corners)
        {
            double from = DistanceFrom(0, 0, at);

            Assert.True(Math.Abs(from - 500) <= 1, $"{at.X},{at.Y} is {from} from the middle, not 500");
        }
    }

    ///
    ///And it encloses what an inscribed polygon of that many sides encloses.
    ///
    ///Not πr², which it is deliberately a little under: an n-sided inscribed polygon has area
    ///½·n·r²·sin(2π/n), and asserting against that rather than against the circle is what tells "the corners
    ///are in the right places" from "there are the right number of them".
    ///
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    public void A_circle_encloses_what_an_inscribed_polygon_of_its_sides_encloses(int sides)
    {
        double area = Math.Abs(Measure.AreaOf(Shapes.Circle(0, 0, 1000, sides)));
        double inscribed = 0.5 * sides * 1000.0 * 1000.0 * Math.Sin(2 * Math.PI / sides);

        Assert.Equal(inscribed, area, inscribed * 0.001);
    }

    ///<summary>An ellipse reaches its own radius on each axis and no further.</summary>
    [Fact]
    public void An_ellipse_reaches_its_radius_on_each_axis()
    {
        var box = Bounds.Of(Shapes.Ellipse(0, 0, 800, 300, 64));

        Assert.Equal(-800, box.Left);
        Assert.Equal(800, box.Right);
        Assert.Equal(-300, box.Bottom);
        Assert.Equal(300, box.Top);
    }

    ///<summary>A shape with no size draws nothing rather than a point or a line.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void A_circle_of_no_size_is_no_shape(int radius)
    {
        Assert.Empty(Shapes.Circle(0, 0, radius));
    }

    ///<summary>Fewer than three corners cannot enclose anything, so three is the floor.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void A_curve_asked_for_too_few_corners_gets_the_fewest_that_enclose_anything(int asked)
    {
        Assert.Equal(Shapes.FewestVertices, Shapes.Circle(0, 0, 500, asked).Count);
    }

    ///<summary>A hexagon has a corner where the turn puts it.</summary>
    [Fact]
    public void A_regular_polygon_starts_at_the_turn_it_was_given()
    {
        var flat = Shapes.RegularPolygon(0, 0, 1000, 6);
        var pointy = Shapes.RegularPolygon(0, 0, 1000, 6, 90);

        Assert.Equal(6, flat.Count);

        //Untraveled, the first corner is out along x. Turned a quarter, it is up along y.
        Assert.Equal(new Element.Point(1000, 0), flat[0]);
        Assert.Equal(new Element.Point(0, 1000), pointy[0]);
    }

    #endregion **************************************************************************



    #region Bézier curves ***************************************************************

    ///
    ///**A Bézier passes through its first and last control point and no others.**
    ///
    ///The thing people are most often surprised by, and the one behavior a wrong implementation is most
    ///likely to get wrong in a way that still looks like a curve.
    ///
    [Fact]
    public void A_curve_runs_between_its_first_and_last_control_points()
    {
        var curve = new BezierBuilder()
            .AddPoint(0, 0)
            .AddPoint(0, 1000)
            .AddPoint(1000, 1000)
            .AddPoint(1000, 0);

        var run = curve.BuildCenterline(64);

        Assert.Equal(new Element.Point(0, 0), run[0]);
        Assert.Equal(new Element.Point(1000, 0), run[^1]);

        //And not through the two that pull it: the curve stays well below y of 1000.
        Assert.All(run, at => Assert.True(at.Y < 800, $"the curve reached {at.Y}, which is on its hull rather than under it"));
    }

    ///<summary>A curve of two points is the straight line between them.</summary>
    [Fact]
    public void A_curve_of_two_points_is_a_straight_line()
    {
        var run = new BezierBuilder().AddPoint(0, 0).AddPoint(1000, 500).BuildCenterline(9);

        Assert.Equal(9, run.Count);

        //Every point on the line, which for this one means y is half of x - within the rounding to whole
        //database units, since the midpoint of this line is at 250, 125 and the eighths between are not.
        foreach (var at in run)
            Assert.InRange(at.Y, (at.X / 2.0) - 1, (at.X / 2.0) + 1);
    }

    ///<summary>The parameter runs 0 to 1 from one end to the other.</summary>
    [Fact]
    public void The_curves_parameter_runs_from_the_first_point_to_the_last()
    {
        var curve = new BezierBuilder().AddPoint(10, 20).AddPoint(0, 1000).AddPoint(900, 800);

        Assert.Equal((10.0, 20.0), curve.At(0));
        Assert.Equal((900.0, 800.0), curve.At(1));
    }

    ///<summary>Outlined, it is a closed shape with area rather than a line with none.</summary>
    [Fact]
    public void A_curve_outlined_at_a_width_encloses_something()
    {
        var curve = new BezierBuilder()
            .AddPoint(0, 0)
            .AddPoint(0, 1000)
            .AddPoint(1000, 1000)
            .AddPoint(1000, 0);

        var outline = curve.BuildPolygon(width: 200, vertices: 64);

        Assert.True(outline.Count > 64, "an outline has both sides of the curve in it");

        //Roughly the curve's length times its width. Loose on purpose: what is being asserted is that this
        //is a ribbon rather than a hairline, not the exact arc length of a cubic.
        double area = Math.Abs(Measure.AreaOf(outline));

        Assert.InRange(area, 200 * 1500, 200 * 3500);
    }

    ///<summary>More control points than one curve can carry is refused rather than quietly dropped.</summary>
    [Fact]
    public void A_curve_refuses_more_control_points_than_it_can_carry()
    {
        var curve = new BezierBuilder();

        for (int i = 0; i < BezierBuilder.MostControlPoints; i++)
            curve.AddPoint(i, i);

        Assert.Throws<InvalidOperationException>(() => curve.AddPoint(99, 99));
    }

    #endregion **************************************************************************



    #region Routes **********************************************************************

    ///<summary>Straight goes the way the route is pointing.</summary>
    [Theory]
    [InlineData(0, 1000, 0)]
    [InlineData(90, 0, 1000)]
    [InlineData(180, -1000, 0)]
    public void A_straight_run_goes_the_way_the_route_points(double heading, int x, int y)
    {
        var route = new PathBuilder(new Element.Point(0, 0), heading).Straight(1000);

        Assert.Equal(new Element.Point(x, y), route.At);
    }

    ///
    ///**A half turn ends two radii to the side, pointing back the way it came.**
    ///
    ///The closed form of a bend, and the one that catches a center computed on the wrong side: turning left
    ///about a center on the right walks the route away rather than round, and every intermediate assertion
    ///about counts and headings still passes.
    ///
    [Fact]
    public void A_half_turn_ends_two_radii_to_the_side()
    {
        var left = new PathBuilder(new Element.Point(0, 0), 0).BendDeg(180, 500);

        Assert.Equal(0, left.At.X);
        Assert.Equal(1000, left.At.Y);
        Assert.Equal(180, Math.Round(left.HeadingDegrees));

        //And the other way is the mirror of it, rather than the same turn again.
        var right = new PathBuilder(new Element.Point(0, 0), 0).BendDeg(-180, 500);

        Assert.Equal(0, right.At.X);
        Assert.Equal(-1000, right.At.Y);
    }

    ///<summary>A quarter turn ends one radius along and one across, facing the new way.</summary>
    [Fact]
    public void A_quarter_turn_ends_one_radius_along_and_one_across()
    {
        var route = new PathBuilder(new Element.Point(0, 0), 0).BendDeg(90, 500);

        Assert.Equal(new Element.Point(500, 500), route.At);
        Assert.Equal(90, Math.Round(route.HeadingDegrees));
    }

    ///<summary>A bend of no radius is a square corner: the heading turns, the route does not move.</summary>
    [Fact]
    public void A_bend_of_no_radius_turns_on_the_spot()
    {
        var route = new PathBuilder(new Element.Point(100, 200), 0).BendDeg(90, 0).Straight(500);

        Assert.Equal(new Element.Point(100, 700), route.At);
    }

    ///
    ///The segments join: a straight after a bend leaves where the bend arrived, pointing where it pointed.
    ///
    ///What makes this a route rather than a list of shapes - and the property that would let a route look
    ///right in every individual assertion while coming apart at the joins.
    ///
    [Fact]
    public void Each_segment_carries_on_from_the_one_before_it()
    {
        var route = new PathBuilder(new Element.Point(0, 0), 0)
            .Straight(1000)
            .BendDeg(90, 500)
            .Straight(1000);

        //Along x by 1000, round the bend to (1500, 500), then up y by 1000.
        Assert.Equal(new Element.Point(1500, 1500), route.At);

        var run = route.Centerline();

        //No jumps: every step of the walk is short, which is what says the pieces are joined rather than
        //placed. The longest is a straight, which is the 1000 it was asked for.
        for (int i = 1; i < run.Count; i++)
        {
            double step = DistanceFrom(run[i - 1].X, run[i - 1].Y, run[i]);

            Assert.True(step <= 1001, $"a step of {step} is a gap rather than a segment");
        }
    }

    ///<summary>A curve dropped into a route starts where the route is, whatever angle it is at.</summary>
    [Fact]
    public void A_curve_in_a_route_is_placed_where_the_route_has_reached()
    {
        var route = new PathBuilder(new Element.Point(4000, 5000), 90)
            .Bezier(b => b.AddPoint(0, 0).AddPoint(1000, 0).AddPoint(1000, 1000));

        var run = route.Centerline();

        //Its first point is where the route already was, rather than the curve's own origin.
        Assert.Equal(new Element.Point(4000, 5000), run[0]);

        //And it was turned with the route: a curve drawn along x, entered pointing up y, ends up the page.
        Assert.True(route.At.Y > 5000, "the curve was not rotated into the route's heading");
    }

    ///
    ///Cut into elements, the pieces overlap by a point so the run is continuous.
    ///
    ///**Because a cut that does not overlap is a dotted line.** Ending one piece where the next begins draws
    ///the same route in several elements; ending one *before* the next begins leaves the segment between
    ///them undrawn, which on a wire is an open circuit that looks like a rendering artefact.
    ///
    [Fact]
    public void A_route_cut_into_elements_joins_at_the_cuts()
    {
        var route = new PathBuilder(new Element.Point(0, 0), 0);

        for (int i = 0; i < 50; i++)
            route.Straight(100);

        var whole = route.Centerline();
        var pieces = route.Build(maxVertices: 10);

        Assert.True(pieces.Count > 1, "a route past the limit is cut");

        foreach (var piece in pieces)
            Assert.True(piece.Count <= 10, $"a piece of {piece.Count} is past the limit it was cut at");

        //Each piece begins where the one before it ended.
        for (int i = 1; i < pieces.Count; i++)
            Assert.Equal(pieces[i - 1][^1], pieces[i][0]);

        //And the pieces put back together are the route, with the shared points counted once.
        int carried = pieces.Sum(piece => piece.Count) - (pieces.Count - 1);

        Assert.Equal(whole.Count, carried);
    }

    ///<summary>Outlined, a route is a closed shape with the area its length and width imply.</summary>
    [Fact]
    public void A_straight_route_outlines_to_its_length_times_its_width()
    {
        var outline = new PathBuilder(new Element.Point(0, 0), 0).Straight(2000).BuildPolygon(200);

        Assert.Equal(2000 * 200, Math.Abs(Measure.AreaOf(outline)), 1);
    }

    #endregion **************************************************************************



    #region Into a file *****************************************************************

    ///
    ///And everything built here goes into a library and comes back out of it.
    ///
    ///The check that the corners are not merely plausible but are coordinates a GDSII file can hold: whole
    ///numbers inside the four-byte field the format writes them in, in an element the reader accepts.
    ///
    [Fact]
    public void What_the_builders_make_survives_being_written_and_read()
    {
        var gds = GDS.NewLibrary("BUILT");
        var top = Hierarchy.Named(gds, "TOP")!;
        var metal = new LayerKey(68, 20);

        new AddElement(gds, top, metal, Shapes.Circle(0, 0, 500)).Apply();
        new AddElement(gds, top, metal, Shapes.Rectangle(3000, 0, 400, 200)).Apply();

        new AddElement(gds, top, metal, new BezierBuilder()
            .AddPoint(0, 3000)
            .AddPoint(0, 4000)
            .AddPoint(1000, 4000)
            .AddPoint(1000, 3000)
            .BuildPolygon(200)).Apply();

        new AddElement(gds, top, metal, new PathBuilder(new Element.Point(0, -3000), 0)
            .Straight(2000)
            .BendDeg(-45, 500)
            .Straight(1000)
            .BuildPolygon(140)).Apply();

        var reopened = new GDS(gds.Serialize());

        Assert.Equal(4, GdsFlattener.Flatten(reopened).Elements.Count);
    }

    #endregion **************************************************************************

    #region A width that changes along the route *****************************************

    ///
    ///**A taper outlines to the trapezoid its two widths and its length describe.**
    ///
    ///The closed form, which is what tells a taper from a wire that is merely not constant: a ribbon whose
    ///sides splay at the wrong rate has the right ends and the wrong area, and every assertion about its
    ///endpoints still passes.
    ///
    [Fact]
    public void A_taper_outlines_to_the_trapezoid_its_widths_describe()
    {
        var wedge = new PathBuilder(new Element.Point(0, 0), 0, width: 200).Straight(1000, widthEnd: 50);

        var outline = wedge.BuildPolygon();

        //Four corners: half of each width at each end.
        Assert.Equal(4, outline.Count);
        Assert.Equal((200 + 50) / 2.0 * 1000, Math.Abs(Measure.AreaOf(outline)), 1);

        //And it is the width that changed rather than the route - it still ends where it was sent.
        Assert.Equal(new Element.Point(1000, 0), wedge.At);
    }

    ///
    ///**A constant width through the tapering path is the constant path**, which is the point of there
    ///being one outliner.
    ///
    ///The variable case is a generalization of the constant one and the constant one goes through it, so
    ///this is what says the generalization did not change the answer. Two offsetters would have drifted
    ///apart on exactly the corners nobody looks at.
    ///
    [Fact]
    public void A_width_that_never_changes_outlines_the_way_a_constant_width_does()
    {
        var route = new PathBuilder(new Element.Point(0, 0), 0)
            .Straight(1000)
            .BendDeg(90, 400)
            .Straight(700)
            .BendDeg(-120, 250);

        var centerline = route.Centerline();
        var widths = new int[centerline.Count];

        Array.Fill(widths, 140);

        Assert.Equal(
            PathOutline.Build(centerline, 140, 0, 0, 0),
            PathOutline.Build(centerline, widths, 0, 0, 0));
    }

    ///<summary>A width given at the end of a segment is where the next one starts from.</summary>
    [Fact]
    public void A_width_carries_into_the_segment_after_it()
    {
        var route = new PathBuilder(new Element.Point(0, 0), 0, width: 100)
            .Straight(500, widthEnd: 300)
            .Straight(500);

        Assert.Equal(300, route.Width);

        var widths = route.Widths();

        Assert.Equal(new[] { 100, 300, 300 }, widths);
    }

    ///<summary>And a bend narrows as it turns, rather than all at one end of the arc.</summary>
    [Fact]
    public void A_bend_spreads_its_taper_around_the_arc()
    {
        var widths = new PathBuilder(new Element.Point(0, 0), 0, width: 100)
            .BendDeg(90, 500, widthEnd: 300)
            .Widths();

        Assert.Equal(100, widths[0]);
        Assert.Equal(300, widths[^1]);

        //Every step is at least as wide as the one before it, and the middle is between the two ends -
        //which is what says the taper is spread rather than applied at a step.
        for (int i = 1; i < widths.Count; i++)
            Assert.True(widths[i] >= widths[i - 1], $"the width went from {widths[i - 1]} back to {widths[i]}");

        Assert.InRange(widths[widths.Count / 2], 101, 299);
    }

    ///<summary>A width function runs along a curve, from 0 at its start to 1 at its end.</summary>
    [Fact]
    public void A_curve_takes_its_width_as_a_function_along_it()
    {
        var widths = new PathBuilder(new Element.Point(0, 0), 0, width: 250)
            .Bezier(
                b => b.AddPoint(0, 0).AddPoint(0, 1000).AddPoint(2000, 1000).AddPoint(1000, 0),
                t => 250 - ((250 - 50) * t))
            .Widths();

        Assert.Equal(250, widths[0]);
        Assert.Equal(50, widths[^1]);
        Assert.InRange(widths[widths.Count / 2], 100, 200);
    }

    ///
    ///**The widths stay the same length as the centerline**, through the rounding that drops points.
    ///
    ///A fine bend can land two steps on the same database unit, and the repeat is dropped because a
    ///zero-length segment has no direction. Dropping the point and keeping its width would put every later
    ///width on the wrong point - a taper drawn over the wrong part of the route, which looks deliberate.
    ///
    [Fact]
    public void The_widths_and_the_centerline_stay_the_same_length()
    {
        //A radius of one unit with plenty of steps, so rounding certainly collapses some of them.
        var route = new PathBuilder(new Element.Point(0, 0), 0, width: 100)
            .BendDeg(360, 1, widthEnd: 200, vertices: 40)
            .Straight(500, widthEnd: 50);

        Assert.Equal(route.Centerline().Count, route.Widths().Count);

        //And the outline is still built rather than refused, which is what a mismatch would cost.
        Assert.NotEmpty(route.BuildPolygon());
    }

    ///<summary>A route carrying no width outlines at the one it is given instead.</summary>
    [Fact]
    public void A_route_with_no_width_of_its_own_takes_the_one_it_is_given()
    {
        var route = new PathBuilder(new Element.Point(0, 0), 0).Straight(2000);

        Assert.Equal(2000 * 200, Math.Abs(Measure.AreaOf(route.BuildPolygon(200))), 1);

        //And outlines to nothing on its own, rather than guessing a width nobody chose.
        Assert.Equal(route.Centerline(), route.BuildPolygon());
    }

    #endregion **************************************************************************
}
