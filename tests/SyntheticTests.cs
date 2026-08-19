using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///The layouts the benchmark measures against.
///
///**A fixture that is wrong makes every number taken against it wrong**, and quietly - a generator that
///produced half the elements it claimed, or shapes that never overlap, would report a comfortable figure for
///a problem it was not exercising. So what is checked here is that the thing generated is the thing asked
///for: the count, the arrangement, and that it is a real file another reader would take.
///
public class SyntheticTests
{
    #region A file another reader would take ****************************************

    [Fact]
    public void A_generated_layout_survives_being_written_and_read_back()
    {
        var reopened = new GDS(Synthetic.Layout(100).Serialize());

        Assert.Equal(100, GdsFlattener.Flatten(reopened).Elements.Count);
    }

    [Fact]
    public void It_is_a_well_formed_library()
    {
        var types = new GDS(Synthetic.Layout(10).Serialize()).Records.Select(record => record.Type).ToList();

        Assert.Equal(RecordType.HEADER, types[0]);
        Assert.Equal(RecordType.BGNLIB, types[1]);
        Assert.Equal(RecordType.LIBNAME, types[2]);
        Assert.Equal(RecordType.UNITS, types[3]);
        Assert.Equal(RecordType.ENDLIB, types[^1]);
    }

    ///<summary>Every boundary closed, which is what the corpus test holds every real file to.</summary>
    [Fact]
    public void Every_shape_is_closed()
    {
        var gds = new GDS(Synthetic.Layout(50, corners: 6).Serialize());

        foreach (var structure in gds.StreamFormat.Structures)
        {
            foreach (var element in structure.Elements)
            {
                if (element.Element.XY?.Data is not Int4Data xy)
                    continue;

                Assert.Equal(xy.Values[0], xy.Values[^2]);
                Assert.Equal(xy.Values[1], xy.Values[^1]);
            }
        }
    }

    #endregion **********************************************************************



    #region The size asked for ******************************************************

    [Theory]
    [InlineData(1, 1, 1, 1)]
    [InlineData(100, 1, 1, 100)]
    [InlineData(10, 5, 4, 200)]
    [InlineData(1000, 3, 3, 9000)]
    public void It_draws_the_number_of_elements_it_says(int perCell, int columns, int rows, int expected)
    {
        Assert.Equal(expected, Synthetic.Drawn(perCell, columns, rows));

        var gds = new GDS(Synthetic.Layout(perCell, columns, rows).Serialize());

        Assert.Equal(expected, GdsFlattener.Flatten(gds).Elements.Count);
    }

    ///
    ///**The whole point of the arrayed shape: a tiny file that draws an enormous amount.**
    ///
    ///This is what makes file size the wrong metric, and the case the plan is aimed at - so if the generator
    ///ever started writing the copies out flat, the benchmark would be measuring a different problem.
    ///
    [Fact]
    public void An_arrayed_layout_stays_small_however_much_it_draws()
    {
        var arrayed = Synthetic.Layout(500, 20, 20);

        byte[] bytes = arrayed.Serialize();

        Assert.Equal(200_000, GdsFlattener.Flatten(new GDS(bytes)).Elements.Count);

        //Well under a hundred kilobytes for two hundred thousand elements.
        Assert.True(bytes.Length < 100_000, $"{bytes.Length:N0} bytes is not a small file");
    }

    ///<summary>And the flat shape is the opposite: the file is as large as what it draws.</summary>
    [Fact]
    public void A_flat_layout_is_as_large_as_what_it_draws()
    {
        Assert.True(Synthetic.Layout(20000).Serialize().Length > 1_000_000);
    }

    [Fact]
    public void An_arrayed_layout_is_one_cell_placed_once()
    {
        var gds = new GDS(Synthetic.Layout(10, 4, 4).Serialize());

        Assert.Equal(new List<string> { Synthetic.LeafCell, Synthetic.TopCell }, Hierarchy.Names(gds));
        Assert.Equal(1, Hierarchy.PlacementsOf(gds, Synthetic.LeafCell));
    }

    #endregion **********************************************************************



    #region The shape of the work ***************************************************

    [Fact]
    public void Shapes_are_spread_over_the_layers_asked_for()
    {
        var gds = new GDS(Synthetic.Layout(400, layers: 5).Serialize());

        var used = GdsFlattener.Flatten(gds).Elements.Select(element => element.Layer.Number).Distinct().ToList();

        Assert.Equal(5, used.Count);
    }

    ///
    ///**Shapes on one layer overlap**, which is what gives the merge something to do.
    ///
    ///A layer whose shapes never touch is a layer `MergeByLayer` finishes instantly - so a generator that
    ///spaced them out would hide the one measured cliff from the benchmark built to find it. Checked by
    ///merging: overlapping neighbours come back as fewer outlines than there were shapes.
    ///
    [Fact]
    public void Neighbours_on_a_layer_overlap()
    {
        var layout = GdsFlattener.Flatten(new GDS(Synthetic.Layout(400, layers: 4).Serialize()));

        var merged = Booleans.MergeByLayer(layout.Elements);

        Assert.True(merged.Count < layout.Elements.Count, $"{merged.Count} outlines from {layout.Elements.Count} shapes is not overlap");
    }

    [Fact]
    public void More_corners_makes_longer_coordinate_lists_and_not_more_elements()
    {
        var few = GdsFlattener.Flatten(new GDS(Synthetic.Layout(100, corners: 4).Serialize()));
        var many = GdsFlattener.Flatten(new GDS(Synthetic.Layout(100, corners: 32).Serialize()));

        Assert.Equal(few.Elements.Count, many.Elements.Count);
        Assert.True(many.Elements.Sum(element => element.Points.Count) > few.Elements.Sum(element => element.Points.Count) * 5);
    }

    ///<summary>Nonsense arguments are clamped rather than throwing - this is a tool, not an input form.</summary>
    [Fact]
    public void Nothing_sensible_is_asked_for_and_nothing_throws()
    {
        Assert.Single(GdsFlattener.Flatten(Synthetic.Layout(0, 0, 0, 0, 0)).Elements);
        Assert.Single(GdsFlattener.Flatten(Synthetic.Layout(-5, -5, -5, -5, -5)).Elements);
    }

    #endregion **********************************************************************

    #region The ceiling ************************************************************

    ///
    ///**Breadth has a limit, where depth already had one.**
    ///
    ///One AREF of a thousand-shape cell, a hundred by a hundred, is ten million elements out of a sixty
    ///kilobyte file, and nothing in the format limits the counts. Measured, half a million elements is half
    ///a gigabyte of managed heap on a desktop - in an address space that is thirty-two bits in a browser.
    ///Without a ceiling the tab does not fail, it dies.
    ///
    [Fact]
    public void Flattening_stops_at_the_ceiling_rather_than_running_out_of_memory()
    {
        int was = GdsFlattener.MostElements;

        try
        {
            GdsFlattener.MostElements = 500;

            var drawn = GdsFlattener.Flatten(Synthetic.Layout(100, 10, 10));

            Assert.True(drawn.Stopped);
            Assert.InRange(drawn.Elements.Count, 500, 600);
        }
        finally
        {
            GdsFlattener.MostElements = was;
        }
    }

    ///<summary>And says nothing about a layout that fits, which is every file anyone will open.</summary>
    [Fact]
    public void A_layout_that_fits_is_not_marked_as_stopped()
    {
        var drawn = GdsFlattener.Flatten(Synthetic.Layout(200));

        Assert.False(drawn.Stopped);
        Assert.Equal(200, drawn.Elements.Count);
    }

    ///
    ///Stopped rather than thrown, which is the difference between a viewer and a reader.
    ///
    ///OasisReader refuses a file past its own limit, and that is right for a reader - a half-read file is
    ///not a file. A layout somebody can see most of, with the app saying so, is more use than one that will
    ///not open. What must never happen is the quiet version.
    ///
    [Fact]
    public void What_was_flattened_before_stopping_is_still_usable()
    {
        int was = GdsFlattener.MostElements;

        try
        {
            GdsFlattener.MostElements = 300;

            var drawn = GdsFlattener.Flatten(Synthetic.Layout(50, 8, 8));

            Assert.True(drawn.Stopped);
            Assert.NotEmpty(drawn.Elements);
            Assert.All(drawn.Elements, element => Assert.NotEmpty(element.Points));
        }
        finally
        {
            GdsFlattener.MostElements = was;
        }
    }

    #endregion **********************************************************************
}
