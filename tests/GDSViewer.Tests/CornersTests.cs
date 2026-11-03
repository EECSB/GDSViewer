using GdsII;

namespace GDSViewer.Tests;

///
///Cutting and rounding corners, measured against what a corner should come out as.
///
///Areas against the arithmetic - a square of side s cut by c loses four triangles of c²/2, rounded to r it
///loses four times r² less a quarter circle - and every point of an arc against the circle it is meant to be
///on. The awkward cases are the ones a real shape has: a radius too big for a thin wire, a corner that points
///inwards, a point that sits on a straight edge, a ring that is closed and one that is not.
///
public class CornersTests
{
    #region Shapes ********************************************************************

    private static List<Element.Point> Ring(params int[] xy)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < xy.Length; i += 2)
            points.Add(new Element.Point(xy[i], xy[i + 1]));

        points.Add(points[0]);

        return points;
    }

    private static List<Element.Point> Square(int side)
    {
        return Ring(0, 0, side, 0, side, side, 0, side);
    }

    ///<summary>An L: a square of 200 with its top right quarter missing, so one corner points inwards.</summary>
    private static List<Element.Point> El()
    {
        return Ring(0, 0, 200, 0, 200, 100, 100, 100, 100, 200, 0, 200);
    }

    private static double Area(IReadOnlyList<Element.Point> ring)
    {
        return Math.Abs(Measure.AreaOf(ring));
    }

    private static double Distance(Element.Point point, double x, double y)
    {
        return Math.Sqrt(((point.X - x) * (point.X - x)) + ((point.Y - y) * (point.Y - y)));
    }

    #endregion ***********************************************************************



    #region Cut ***********************************************************************

    [Fact]
    public void A_cut_square_has_eight_corners_and_loses_four_triangles()
    {
        var cut = Corners.Cut(Square(1000), 100);

        //Eight corners and the point that closes them.
        Assert.Equal(9, cut.Count);
        Assert.Equal(cut[0], cut[^1]);

        Assert.Equal((1000.0 * 1000) - (4 * 100.0 * 100 / 2), Area(cut));
    }

    [Fact]
    public void Each_cut_lands_the_size_asked_for_along_both_edges()
    {
        var cut = Corners.Cut(Square(1000), 100);

        //The corner at the origin, which is the first corner of the ring, is cut back to these two.
        Assert.Contains(new Element.Point(0, 100), cut);
        Assert.Contains(new Element.Point(100, 0), cut);
        Assert.DoesNotContain(new Element.Point(0, 0), cut);
    }

    #endregion ***********************************************************************



    #region Round *********************************************************************

    [Fact]
    public void A_rounded_square_loses_the_corners_a_circle_does_not_fill()
    {
        const int side = 10_000;
        const int radius = 1_000;

        var round = Corners.Round(Square(side), radius, 64);

        double expected = ((double)side * side) - (4 * ((double)radius * radius) * (1 - (Math.PI / 4)));

        //Within what sixty-four straight steps a quarter turn, and rounding to whole units, leave off a circle.
        Assert.InRange(Area(round), expected - 2_000, expected + 2_000);
    }

    [Fact]
    public void Every_point_of_an_arc_is_on_its_circle()
    {
        const int radius = 5_000;

        var round = Corners.Round(Square(100_000), radius, 16);

        //The corner at the origin rounds about a center the radius in from both edges. The point that closes
        //the ring is the first of this arc again, and is left out of the count.
        var near = round.Take(round.Count - 1).Where(point => point.X <= radius && point.Y <= radius).ToList();

        Assert.Equal(17, near.Count);

        foreach (var point in near)
            Assert.InRange(Distance(point, radius, radius), radius - 1, radius + 1);
    }

    [Fact]
    public void A_right_angle_takes_the_steps_asked_for()
    {
        var round = Corners.Round(Square(100_000), 5_000, 8);

        //Four corners of nine points each - eight steps - and the point that closes them.
        Assert.Equal((4 * 9) + 1, round.Count);
    }

    ///
    ///**A radius bigger than the shape can take becomes the biggest it can.** A wire twenty wide rounded at
    ///fifty would have arcs crossing each other; each corner takes at most half of each edge, so the ends
    ///come out as half circles and nothing crosses.
    ///
    [Fact]
    public void A_radius_too_big_for_the_shape_is_the_biggest_that_fits()
    {
        var wire = Ring(0, 0, 1000, 0, 1000, 20, 0, 20);

        var round = Corners.Round(wire, 50, 16);

        Assert.All(round, point =>
        {
            Assert.InRange(point.X, 0, 1000);
            Assert.InRange(point.Y, 0, 20);
        });

        //Two half circles of radius ten on a strip 980 long: what is left of a 1000 by 20 bar.
        double expected = (980.0 * 20) + (Math.PI * 10 * 10);

        Assert.InRange(Area(round), expected - 40, expected + 40);
    }

    ///<summary>An inside corner is rounded too, which fills it rather than cutting it - the L gains area there.</summary>
    [Fact]
    public void An_inside_corner_is_filled()
    {
        var round = Corners.Round(El(), 30, 16);

        //The inside corner is at (100, 100): its arc bulges into the notch, where the L had nothing.
        Assert.Contains(round, point => point.X > 100 && point.Y > 100 && point.X < 130 && point.Y < 130);

        var cut = Corners.Cut(El(), 30);

        Assert.Contains(new Element.Point(130, 100), cut);
        Assert.Contains(new Element.Point(100, 130), cut);
    }

    #endregion ***********************************************************************



    #region What goes in **************************************************************

    ///<summary>A point on a straight edge is no corner, and a point given twice is one point.</summary>
    [Fact]
    public void Points_on_straight_edges_and_repeats_change_nothing()
    {
        var plain = Corners.Round(Square(1000), 100, 8);
        var busy = Corners.Round(Ring(0, 0, 500, 0, 1000, 0, 1000, 0, 1000, 1000, 0, 1000, 0, 400), 100, 8);

        Assert.Equal(Area(plain), Area(busy));
        Assert.Equal(plain.Count, busy.Count);
    }

    [Fact]
    public void A_closed_outline_comes_back_closed_and_an_open_one_open()
    {
        var closed = Corners.Cut(Square(1000), 100);
        var open = Corners.Cut(Square(1000).Take(4).ToList(), 100);

        Assert.Equal(closed[0], closed[^1]);
        Assert.NotEqual(open[0], open[^1]);
        Assert.Equal(closed.Count - 1, open.Count);
    }

    [Fact]
    public void Nothing_asked_for_changes_nothing()
    {
        Assert.Equal(Square(1000), Corners.Cut(Square(1000), 0));
        Assert.Equal(Square(1000), Corners.Round(Square(1000), 0, 8));

        //Two points enclose nothing and have no corner to work on.
        var line = new List<Element.Point> { new Element.Point(0, 0), new Element.Point(100, 0) };

        Assert.Equal(line, Corners.Round(line, 10, 8));
    }

    ///<summary>The result is still a shape a GDSII boundary can hold: no point twice in a row.</summary>
    [Fact]
    public void No_point_comes_out_twice_in_a_row()
    {
        foreach (var made in new[] { Corners.Round(El(), 30, 4), Corners.Cut(El(), 30), Corners.Round(Square(40), 30, 64) })
        {
            for (int i = 1; i < made.Count; i++)
                Assert.NotEqual(made[i - 1], made[i]);
        }
    }

    #endregion ***********************************************************************
}
