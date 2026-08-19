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
}
