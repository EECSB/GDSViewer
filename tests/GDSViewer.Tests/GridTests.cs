using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Recovering the grid a layout was drawn on.
///
///Nothing in a GDSII file records it, so it is read back out of the coordinates - see <see cref="Grid"/>.
///What matters here is that the arithmetic is the greatest common divisor of the coordinates themselves
///rather than of anything else, and that the answers nobody can act on come back as one rather than as a
///number that would move geometry.
///</summary>
public class GridTests
{
    ///<summary>A library with one boundary on the coordinates given, which is enough to ask the question.</summary>
    private static GDS WithCoordinates(params int[] coordinates)
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        gds.StreamFormat.Structures.Clear();

        var made = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var structure = made.StreamFormat.Structures[0];

        structure.Elements.RemoveRange(1, structure.Elements.Count - 1);
        structure.Elements[0].Element.XY!.Data = new Int4Data(coordinates);

        gds.StreamFormat.Structures.Add(structure);

        return gds;
    }

    [Fact]
    public void Coordinates_that_all_divide_by_five_are_a_grid_of_five()
    {
        Assert.Equal(5, Grid.Of(WithCoordinates(0, 0, 10, 0, 10, 15, 0, 15, 0, 0)));
    }

    ///<summary>One odd coordinate is enough: everything after it would be moved by a coarser grid.</summary>
    [Fact]
    public void A_single_coordinate_off_the_grid_takes_it_back_to_one()
    {
        Assert.Equal(1, Grid.Of(WithCoordinates(0, 0, 10, 0, 10, 15, 3, 15, 0, 0)));
    }

    ///<summary>Everything divides zero, so a shape at the origin says nothing about the grid.</summary>
    [Fact]
    public void Zeros_say_nothing_and_do_not_force_it_to_one()
    {
        Assert.Equal(20, Grid.Of(WithCoordinates(0, 0, 20, 0, 20, 40, 0, 40, 0, 0)));
    }

    ///<summary>Nothing to go on is one, not zero - a pitch of zero is a line at every integer.</summary>
    [Fact]
    public void Nothing_at_all_is_a_grid_of_one()
    {
        Assert.Equal(1, Grid.Of(null));
        Assert.Equal(1, Grid.Of(WithCoordinates(0, 0, 0, 0, 0, 0)));
    }

    ///<summary>Negative coordinates divide the same way; Mosfet has plenty of them.</summary>
    [Fact]
    public void Negative_coordinates_count_by_their_size()
    {
        Assert.Equal(25, Grid.Of(WithCoordinates(-50, -25, 25, -25, 25, 50, -50, 50, -50, -25)));
    }

    ///
    ///The hand-made example, which is the file every other spec measures against.
    ///
    ///Asserted rather than described because it is the number the 2D view's default pitch comes from: a
    ///grid of one micron over this file is a thousand database units, and its geometry does not sit on
    ///anything like that - which is why a grid drawn at a round number crossed the shapes instead of
    ///running along them.
    ///
    [Fact]
    public void Mosfet_sits_on_its_own_grid_rather_than_on_a_round_number()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        //Five database units, which on this file's UNITS is five nanometers. A grid of one micron is a
        //thousand of them, so every line but one in two hundred fell between the geometry rather than on
        //it - which is exactly what "the grid does not line up" looked like.
        Assert.Equal(5, Grid.Of(gds));
    }

    ///
    ///The pitch a file opens on, which is its own grid raised by tens until it is worth drawing.
    ///
    ///Snapping to the file grid directly is right and *drawing* it is not: five units across the bundled
    ///cell is a line every 0.73 pixels at the opening fit, which is a wash of color and 178 of them. So the
    ///pitch stays a whole multiple of the file grid - nothing is ever placed off what the file already sits
    ///on - and the multiple is chosen against how big the layout is.
    ///
    [Theory]
    //Mosfet: drawn on five, 2,800 units across. A five-hundredth of that is 5.6, so five is not enough and
    //fifty is - which puts roughly five heavy lines across the view, about what a micron gave.
    [InlineData(5, 2800, 50)]
    //Already coarse enough for the layout it is on, so it is left alone.
    [InlineData(50, 2800, 50)]
    //A die: the same five-unit grid, ten millimeters across, opens on fifty microns rather than fifty
    //nanometers. The rule is about the layout, not the file grid alone.
    [InlineData(5, 10000000, 50000)]
    //A file with nothing in common between its coordinates still gets a pitch it can draw.
    [InlineData(1, 2800, 10)]
    public void The_opening_pitch_is_the_file_grid_raised_until_it_is_worth_drawing(int own, long across, int expected)
    {
        var gds = WithCoordinates(0, 0, own, 0, own, own, 0, own, 0, 0);

        Assert.Equal(own, Grid.Of(gds));
        Assert.Equal(expected, Grid.Opening(gds, across));
    }

    ///<summary>A layout with no size to go on leaves the pitch at the grid itself rather than dividing by it.</summary>
    [Fact]
    public void A_layout_with_no_extent_opens_on_the_file_grid()
    {
        var gds = WithCoordinates(0, 0, 5, 0, 5, 5, 0, 5, 0, 0);

        Assert.Equal(5, Grid.Opening(gds, 0));
        Assert.Equal(5, Grid.Opening(gds, -1));
    }

    #region Corners on a grid ****************************************************************

    private static List<Element.Point> Points(params int[] coordinates)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < coordinates.Length; i += 2)
            points.Add(new Element.Point(coordinates[i], coordinates[i + 1]));

        return points;
    }

    ///<summary>The nearest step, and half a step away from zero on either side of it - as a database unit rounds.</summary>
    [Theory]
    [InlineData(12, 10)]
    [InlineData(13, 15)]
    [InlineData(12.5, 15)]
    [InlineData(-12.5, -15)]
    [InlineData(-12, -10)]
    [InlineData(0, 0)]
    public void A_number_goes_to_the_nearest_step_and_a_half_goes_away_from_zero(double units, int expected)
    {
        Assert.Equal(expected, Grid.Snap(units, 5));
    }

    ///<summary>A grid of one is the database unit, and nothing finer than one is a grid.</summary>
    [Fact]
    public void A_grid_of_one_or_less_is_the_database_unit()
    {
        Assert.Equal(13, Grid.Snap(12.5, 1));
        Assert.Equal(13, Grid.Snap(12.5, 0));
        Assert.Equal(new Element.Point(13, -13), Grid.Snap(new Element.Point(13, -13), 1));
    }

    ///
    ///Corners that land on one point are one corner - two arc corners a fraction of a step apart - and an
    ///outline that came closed goes back closed, with the repeat it closes on.
    ///
    [Fact]
    public void An_outline_on_a_grid_drops_the_repeats_and_closes_as_it_came()
    {
        var outline = Points(0, 0, 11, 1, 12, 2, 20, 0, 20, 20, 0, 20, 0, 0);

        Assert.Equal(Points(0, 0, 10, 0, 20, 0, 20, 20, 0, 20, 0, 0), Grid.Snap(outline, 5));
    }

    ///<summary>One that only ends where it began once its corners are moved was not closed, and is not closed by it.</summary>
    [Fact]
    public void An_open_outline_stays_open()
    {
        Assert.Equal(Points(0, 0, 10, 0, 10, 10), Grid.Snap(Points(0, 0, 10, 0, 10, 10, 1, 1), 5));
    }

    ///<summary>On a grid of one the outline is handed back as it came, repeats and all: nothing was moved, so nothing landed on anything.</summary>
    [Fact]
    public void On_a_grid_of_one_the_outline_is_as_it_was()
    {
        var outline = Points(0, 0, 3, 0, 3, 0, 3, 3);

        Assert.Equal(outline, Grid.Snap(outline, 1));
    }

    ///<summary>A bow tie crosses itself; a keyhole - two edges on top of each other, how GDSII writes a hole - does not.</summary>
    [Fact]
    public void A_bow_tie_crosses_itself_and_a_keyhole_does_not()
    {
        Assert.Equal((0, 2), Grid.Crossing(Points(0, 0, 10, 10, 10, 0, 0, 10)));

        var keyhole = Points(0, 0, 30, 0, 30, 30, 0, 30, 0, 15, 10, 15, 10, 20, 20, 20, 20, 10, 10, 10, 10, 15, 0, 15);

        Assert.Null(Grid.Crossing(keyhole));
        Assert.Null(Grid.Crossing(Points(0, 0, 10, 0, 10, 10, 0, 10)));
    }

    ///
    ///**What the grid can do to a shape**, which is why the two go together: a corner within a step of an edge it
    ///is not on can be moved across it. A sliver a step or two across, whole as the arithmetic made it, goes onto
    ///a grid of ten with its first edge crossing its third - found by search, since an outline whose edges all
    ///run along the axes keeps its corners in order and cannot do it.
    ///
    [Fact]
    public void A_grid_can_fold_an_outline_that_did_not_cross()
    {
        var sliver = Points(57, 60, 41, 13, 44, 21, 58, 31, 64, 40);

        Assert.Null(Grid.Crossing(sliver));
        Assert.Equal(Points(60, 60, 40, 10, 40, 20, 60, 30, 60, 40), Grid.Snap(sliver, 10));
        Assert.Equal((0, 2), Grid.Crossing(Grid.Snap(sliver, 10)));
    }

    #endregion **************************************************************************



    #region What a boolean or a grow made, on a grid ****************************************

    private static bool OnTheGrid(IEnumerable<Element.Point> corners, int grid)
    {
        return corners.All(corner => corner.X % grid == 0 && corner.Y % grid == 0);
    }

    ///<summary>A circle of radius 1,000 on a grid of five, as the drawing tools put one there.</summary>
    private static List<Element.Point> Circle()
    {
        return Grid.Snap(Shapes.Ellipse(0, 0, 1000, 1000, 64), 5);
    }

    ///
    ///A circle cut by a rectangle: where the rectangle's edge crosses the circle, the corners are worked out, and
    ///at the nearest database unit they are off the grid both circle and rectangle are on.
    ///
    [Fact]
    public void Where_a_boolean_crosses_edges_the_corners_go_on_the_grid()
    {
        var cut = Booleans.Combine(new[] { Circle() }, new[] { Shapes.Between(-2000, 300, 2000, 2000) }, BooleanOperation.Not);

        Assert.False(OnTheGrid(cut.SelectMany(outline => outline), 5));

        var snapped = Grid.SnapAll(cut, 5);

        Assert.Single(snapped.Outlines);
        Assert.True(OnTheGrid(snapped.Outlines[0], 5));
        Assert.True(snapped.Moved);
        Assert.Equal(0, snapped.Flattened);
        Assert.Null(snapped.Fold);
    }

    ///<summary>A grow moves every edge, and on a curve every corner it moves is off the grid until it is put on it.</summary>
    [Fact]
    public void A_grown_circle_goes_on_the_grid_whole()
    {
        var grown = Booleans.Grow(new[] { Circle() }, 100);

        Assert.False(OnTheGrid(grown[0], 5));

        var snapped = Grid.SnapAll(grown, 5);

        Assert.True(OnTheGrid(snapped.Outlines.Single(), 5));
        Assert.Null(snapped.Fold);
    }

    ///<summary>Outlines already on it are handed back as they were - closed, as this one came - and said not to have moved.</summary>
    [Fact]
    public void Outlines_on_the_grid_already_are_left_alone()
    {
        var square = Points(0, 0, 10, 0, 10, 10, 0, 10, 0, 0);

        var snapped = Grid.SnapAll(new[] { square }, 5);

        Assert.Equal(square, snapped.Outlines.Single());
        Assert.False(snapped.Moved);
    }

    ///<summary>On a grid of one, every outline is handed back as it came, corners off any coarser grid and all.</summary>
    [Fact]
    public void On_a_grid_of_one_every_outline_is_as_it_was()
    {
        var outlines = new[] { Points(0, 0, 13, 1, 7, 9), Points(1, 1, 2, 2, 3, 1) };

        var snapped = Grid.SnapAll(outlines, 1);

        Assert.Equal(outlines, snapped.Outlines);
        Assert.False(snapped.Moved);
    }

    ///<summary>A sliver narrower than a step all along is a line on the grid, and is left out and counted - a long one as much as a short one.</summary>
    [Fact]
    public void A_piece_narrower_than_a_step_is_left_out_and_counted()
    {
        var square = Points(0, 0, 10, 0, 10, 10, 0, 10);
        var sliver = Points(100, 0, 1100, 2, 1100, 0);
        var needle = Points(0, 50, 500, 51, 1000, 50, 500, 49);

        var snapped = Grid.SnapAll(new[] { square, sliver, needle }, 5);

        Assert.Equal(square, snapped.Outlines.Single());
        Assert.Equal(2, snapped.Flattened);
        Assert.Null(snapped.Fold);
    }

    ///
    ///A corner within a step of an edge it is not on, moved across it - found by search, as the one above was - is
    ///a fold: said with where, and the outline kept out rather than written crossing itself.
    ///
    [Fact]
    public void A_fold_is_said_and_the_folded_outline_kept_out()
    {
        var folding = Points(9, 34, 32, 36, 31, 20, 5, 17, 3, 11);

        Assert.Null(Grid.Crossing(folding));

        var snapped = Grid.SnapAll(new[] { folding, Points(100, 100, 110, 100, 110, 110) }, 5);

        Assert.Equal((new Element.Point(30, 20), new Element.Point(5, 10)), snapped.Fold);
        Assert.Single(snapped.Outlines);
    }

    ///
    ///**A corner another one lands on starts an edge that is new.** Here (2, -2) lands on (0, 0), and the edge from
    ///(0, 0) to (100, 5) that replaces the two through it crosses the one down x = 20 - though neither of its ends
    ///moved. Looking only along the edges whose ends moved has to count that corner as moved, or it misses this.
    ///
    [Fact]
    public void A_corner_another_lands_on_counts_as_moved()
    {
        var outline = Points(0, 0, 2, -2, 100, 5, 100, 100, 20, 100, 20, 0);

        Assert.Null(Grid.Crossing(outline));
        Assert.NotNull(Grid.SnapAll(new[] { outline }, 5).Fold);
    }

    ///<summary>And the same where the last corner lands on the first, which the closing edge then starts from.</summary>
    [Fact]
    public void A_last_corner_landing_on_the_first_counts_it_as_moved()
    {
        var outline = Points(0, 0, 20, 0, 20, 100, 100, 100, 100, 5, 2, -2);

        Assert.Null(Grid.Crossing(outline));
        Assert.NotNull(Grid.SnapAll(new[] { outline }, 5).Fold);
    }

    ///
    ///A crossing the grid did not make is not its to find: an outline from a set operation never crossed itself
    ///before. A lopsided bow tie, since one with halves alike covers nothing by the arithmetic.
    ///
    [Fact]
    public void A_crossing_the_grid_did_not_make_is_left_to_whoever_made_it()
    {
        var bowTie = Points(0, 0, 20, 20, 20, 0, 0, 10);

        var snapped = Grid.SnapAll(new[] { bowTie }, 5);

        Assert.Null(snapped.Fold);
        Assert.Equal(bowTie, snapped.Outlines.Single());
    }

    #endregion **************************************************************************



    #region A path's outline, when the grid cannot hold the path ****************************

    ///<summary>What a path is checked by: its outline, as the flattener builds it.</summary>
    private static List<Element.Point> OutlineOf(List<Element.Point> centerline, int width, Paths.Ends ends)
    {
        return PathOutline.Build(centerline, width, (int)ends, 0, 0);
    }

    ///
    ///A slanted run's outline is offset half the width along a normal that is not square to the axes, so its corners
    ///land between grid points wherever the centerline is - and the path is that outline, which is handed back.
    ///
    [Fact]
    public void A_slanted_path_is_its_outline()
    {
        var centerline = Points(0, 0, 100, 100);

        var off = Grid.PathOutlineOff(centerline, 10, Paths.Ends.Flush, 5);

        Assert.NotNull(off);
        Assert.Equal(OutlineOf(centerline, 10, Paths.Ends.Flush), off.Value.Outline);
        Assert.False(OnTheGrid(off.Value.Outline, 5));
        Assert.Equal("slanted runs", off.Value.Parts);
    }

    ///<summary>A round end's corners are on a half circle, whatever the runs between them do.</summary>
    [Fact]
    public void A_round_ended_path_is_its_outline()
    {
        Assert.Equal("round ends", Grid.PathOutlineOff(Points(0, 0, 100, 0), 10, Paths.Ends.Round, 5)?.Parts);
        Assert.Equal("slanted runs and round ends", Grid.PathOutlineOff(Points(0, 0, 100, 100), 10, Paths.Ends.Round, 5)?.Parts);
    }

    ///
    ///Square to the axes on numbers on the grid, the outline is on it too. Off it by a width of an odd number of
    ///steps, it is off by a number somebody gave rather than by anything worked out - so a path either way.
    ///
    [Fact]
    public void A_square_path_stays_a_path_on_the_grid_or_off_it()
    {
        Assert.Null(Grid.PathOutlineOff(Points(0, 0, 100, 0, 100, 100), 10, Paths.Ends.Flush, 5));
        Assert.Null(Grid.PathOutlineOff(Points(0, 0, 100, 0, 100, 100), 10, Paths.Ends.Extended, 5));

        Assert.False(OnTheGrid(OutlineOf(Points(0, 0, 100, 0), 15, Paths.Ends.Flush), 5));
        Assert.Null(Grid.PathOutlineOff(Points(0, 0, 100, 0), 15, Paths.Ends.Flush, 5));
    }

    ///<summary>A slant whose outline lands on the grid anyway - three across and four up, so a normal of four and three - is a path.</summary>
    [Fact]
    public void A_slant_whose_outline_is_on_the_grid_stays_a_path()
    {
        Assert.True(OnTheGrid(OutlineOf(Points(0, 0, 300, 400), 50, Paths.Ends.Flush), 5));
        Assert.Null(Grid.PathOutlineOff(Points(0, 0, 300, 400), 50, Paths.Ends.Flush, 5));
    }

    ///
    ///No grid, or no width - a hairline, which a check reads by its centerline, so by points somebody gave even where
    ///they are off the grid - and there is no outline to put on one.
    ///
    [Fact]
    public void With_no_grid_or_no_width_it_is_a_path()
    {
        Assert.Null(Grid.PathOutlineOff(Points(0, 0, 100, 100), 10, Paths.Ends.Round, 1));
        Assert.Null(Grid.PathOutlineOff(Points(0, 0, 101, 99), 0, Paths.Ends.Round, 5));
    }

    ///
    ///Every run round a bend's arc slants, so a bent path's slants are read from the corners it was bent from: square
    ///to the axes here, so its outline is off the grid along its bends and nothing else.
    ///
    [Fact]
    public void A_bent_path_is_off_by_its_bends_and_its_slants_are_read_from_its_corners()
    {
        var corners = Points(0, 0, 100, 0, 100, 100);
        var bent = Points(0, 0, 90, 0, 97, 3, 100, 10, 100, 100);

        Assert.Equal("bends", Grid.PathOutlineOff(bent, 10, Paths.Ends.Flush, 5, corners)?.Parts);

        var slanted = Points(0, 0, 50, 50, 100, 100);
        var bentToo = Points(0, 0, 40, 40, 50, 55, 100, 100);

        Assert.Equal("bends, slanted runs and round ends", Grid.PathOutlineOff(bentToo, 10, Paths.Ends.Round, 5, slanted)?.Parts);
    }

    #endregion **************************************************************************
}
