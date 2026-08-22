using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Bounding boxes and area.
///
///**The pair of area figures is the point of this.** Summing the shapes on a layer and merging them first
///give different answers whenever anything overlaps, and which one somebody wants depends on what they
///asked - so the tests that matter here are the ones that pin the two apart rather than the ones that
///check a square is a hundred by a hundred.
///</summary>
public class MeasureTests
{
    private static Element.Point At(int x, int y)
    {
        return new Element.Point(x, y);
    }

    ///<summary>A closed square, the way a GDSII boundary writes one - first point repeated at the end.</summary>
    private static List<Element.Point> Square(int left, int bottom, int size)
    {
        return new List<Element.Point>
        {
            At(left, bottom),
            At(left + size, bottom),
            At(left + size, bottom + size),
            At(left, bottom + size),
            At(left, bottom)
        };
    }

    private static FlattenedLayout LayoutOf(params (LayerKey Key, List<Element.Point> Points)[] shapes)
    {
        var layout = new FlattenedLayout();

        foreach (var shape in shapes)
            layout.Elements.Add(new Element { Layer = new Layer(shape.Key, "#000000"), Points = shape.Points });

        return layout;
    }

    #region Bounds **********************************************************************

    [Fact]
    public void A_box_is_built_whichever_way_round_its_corners_are_given()
    {
        var one = new Bounds(0, 0, 100, 60);
        var other = new Bounds(100, 60, 0, 0);

        Assert.Equal(one, other);
        Assert.Equal(0, one.Left);
        Assert.Equal(100, one.Right);
    }

    [Fact]
    public void A_box_measures_itself()
    {
        var bounds = new Bounds(-50, -20, 150, 80);

        Assert.Equal(200, bounds.Width);
        Assert.Equal(100, bounds.Height);
        Assert.Equal(20000, bounds.Area);
        Assert.Equal(At(50, 30), bounds.Center);
    }

    ///<summary>
    ///The middle of a box either side of the origin, which integer division rounds towards zero and so
    ///puts a unit out of place depending on which side of it the box sits.
    ///</summary>
    [Fact]
    public void A_centre_rounds_the_same_way_on_both_sides_of_the_origin()
    {
        Assert.Equal(At(2, 0), new Bounds(0, 0, 5, 0).Center);
        Assert.Equal(At(-3, 0), new Bounds(-5, 0, 0, 0).Center);
    }

    [Fact]
    public void The_box_around_nothing_is_empty_and_stays_out_of_the_way()
    {
        var empty = Bounds.Empty;
        var real = new Bounds(0, 0, 10, 10);

        Assert.True(empty.IsEmpty);
        Assert.True(Bounds.Of(new List<Element.Point>()).IsEmpty);

        //Union with it gives the other back, either way round.
        Assert.Equal(real, empty.Union(real));
        Assert.Equal(real, real.Union(empty));

        //And nothing is in it or touches it.
        Assert.False(empty.Intersects(real));
        Assert.False(real.Intersects(empty));
        Assert.False(empty.Contains(At(0, 0)));
    }

    [Fact]
    public void A_box_is_drawn_round_the_points_given()
    {
        var bounds = Bounds.Of(Square(10, 20, 100));

        Assert.Equal(new Bounds(10, 20, 110, 120), bounds);
    }

    ///<summary>
    ///Touching counts as intersecting. These decide what to draw, and a shape whose edge is exactly on the
    ///edge of the view is on screen.
    ///</summary>
    [Fact]
    public void Boxes_that_only_touch_still_intersect()
    {
        var left = new Bounds(0, 0, 100, 100);
        var right = new Bounds(100, 0, 200, 100);
        var apart = new Bounds(101, 0, 200, 100);

        Assert.True(left.Intersects(right));
        Assert.False(left.Intersects(apart));
    }

    [Fact]
    public void A_box_knows_what_is_inside_it()
    {
        var bounds = new Bounds(0, 0, 100, 100);

        Assert.True(bounds.Contains(At(50, 50)));
        Assert.True(bounds.Contains(At(0, 100)));
        Assert.False(bounds.Contains(At(101, 50)));

        Assert.True(bounds.Contains(new Bounds(10, 10, 90, 90)));
        Assert.False(bounds.Contains(new Bounds(10, 10, 110, 90)));
    }

    [Fact]
    public void Growing_moves_every_edge_and_shrinking_past_nothing_gives_nothing()
    {
        var bounds = new Bounds(0, 0, 100, 100);

        Assert.Equal(new Bounds(-10, -10, 110, 110), bounds.Grown(10));
        Assert.Equal(new Bounds(10, 10, 90, 90), bounds.Grown(-10));
        Assert.True(bounds.Grown(-60).IsEmpty);
    }

    ///<summary>
    ///A box prints the same in any culture.
    ///
    ///Found by the CLI's own hostile-culture test rather than by this file: coordinates are routinely
    ///negative, and a culture is free to write a negative with something that is not a minus - so this
    ///printed "(!1350, 0) to (1450, 1500)" for the bundled transistor. Pinned here as well, because it is
    ///a property of the type and not of the one command that happened to surface it.
    ///</summary>
    [Fact]
    public void A_box_prints_its_coordinates_the_same_in_any_culture()
    {
        var bounds = new Bounds(-1350, 0, 1450, 1500);

        string invariant = bounds.ToString();

        var was = System.Globalization.CultureInfo.CurrentCulture;

        try
        {
            System.Globalization.CultureInfo.CurrentCulture = GdsTestData.HostileCulture();

            Assert.Equal(invariant, bounds.ToString());
            Assert.Equal("(-1350, 0) to (1450, 1500)", bounds.ToString());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = was;
        }
    }

    ///<summary>
    ///A layout can span most of the coordinate range, which is where a width computed in int stops being a
    ///width. Two billion units across is not a real chip, but it is a real GDSII file.
    ///</summary>
    [Fact]
    public void A_box_across_the_whole_coordinate_range_still_measures()
    {
        var bounds = new Bounds(int.MinValue, int.MinValue, int.MaxValue, int.MaxValue);

        Assert.Equal(4294967295L, bounds.Width);
        Assert.True(bounds.Width > int.MaxValue);

        //And growing one that already reaches the edge stays at the edge rather than wrapping past it.
        Assert.Equal(bounds, bounds.Grown(1000));
    }

    #endregion **************************************************************************



    #region Area ************************************************************************

    [Fact]
    public void A_square_is_its_side_squared()
    {
        Assert.Equal(10000, Measure.AreaOf(Square(0, 0, 100)));
    }

    ///<summary>
    ///Both windings, because nothing in GDSII says which way a boundary runs and both are in the corpus.
    ///Signed, half a layer would come back negative.
    ///</summary>
    [Fact]
    public void Area_does_not_depend_on_which_way_round_the_outline_runs()
    {
        var clockwise = Square(0, 0, 100);
        var counterClockwise = new List<Element.Point>(clockwise);

        counterClockwise.Reverse();

        Assert.Equal(Measure.AreaOf(clockwise), Measure.AreaOf(counterClockwise));
        Assert.Equal(10000, Measure.AreaOf(counterClockwise));
    }

    [Fact]
    public void Something_with_no_area_has_none()
    {
        Assert.Equal(0, Measure.AreaOf(new List<Element.Point>()));
        Assert.Equal(0, Measure.AreaOf(new List<Element.Point> { At(0, 0), At(100, 0) }));

        //Three points on one line enclose nothing.
        Assert.Equal(0, Measure.AreaOf(new List<Element.Point> { At(0, 0), At(50, 0), At(100, 0) }));
    }

    ///<summary>
    ///A shape far from the origin, which is where measuring from raw coordinates stops working.
    ///
    ///**A billion, not a hundred million.** Measured: the two ways of doing this agree up to 10^8 and part
    ///company at 5x10^8, where the products of raw coordinates reach 10^17 and a double's steps there are
    ///wider than the hundred-unit answer is precise. Raw gives 9,984 for a 100x100 square at 5x10^8 and
    ///10,240 at 2x10^9 - wrong in both directions, and never by enough to look wrong.
    ///
    ///Well inside what a GDSII coordinate holds, which is a signed 32-bit integer: a billion is half of it.
    ///</summary>
    [Theory]
    [InlineData(100)]
    [InlineData(100_000_000)]
    [InlineData(500_000_000)]
    [InlineData(1_000_000_000)]
    [InlineData(2_000_000_000)]
    public void A_shape_far_from_the_origin_measures_exactly(int at)
    {
        Assert.Equal(10000, Measure.AreaOf(Square(at, at, 100)));
    }

    ///<summary>
    ///**The one that matters.** Two squares overlapping by a quarter: drawn area counts the overlap twice,
    ///covered area counts it once. A caller who wants one and gets the other is wrong by exactly that
    ///overlap, which on a real layer is not a rounding difference.
    ///</summary>
    [Fact]
    public void Drawn_area_double_counts_an_overlap_and_covered_area_does_not()
    {
        var key = new LayerKey(65, 20);

        //100x100 at the origin, and another shifted 50 across and 50 up: a 50x50 overlap.
        var layout = LayoutOf((key, Square(0, 0, 100)), (key, Square(50, 50, 100)));

        Assert.Equal(20000, Measure.DrawnAreaOf(layout, key));
        Assert.Equal(17500, Measure.CoveredAreaOf(layout, key), 1);
    }

    [Fact]
    public void Covered_area_takes_a_hole_off()
    {
        var key = new LayerKey(65, 20);

        //A ring: four bars round an empty middle, so the merge has a hole in it.
        var layout = LayoutOf(
            (key, Square(0, 0, 300)),
            (key, Square(100, 100, 100)));

        //The big square alone, because the small one is inside it.
        Assert.Equal(90000, Measure.CoveredAreaOf(layout, key), 1);

        //Now punch it out for real, by merging four bars instead.
        var ring = LayoutOf(
            (key, new List<Element.Point> { At(0, 0), At(300, 0), At(300, 100), At(0, 100), At(0, 0) }),
            (key, new List<Element.Point> { At(0, 200), At(300, 200), At(300, 300), At(0, 300), At(0, 200) }),
            (key, new List<Element.Point> { At(0, 0), At(100, 0), At(100, 300), At(0, 300), At(0, 0) }),
            (key, new List<Element.Point> { At(200, 0), At(300, 0), At(300, 300), At(200, 300), At(200, 0) }));

        //300x300 less the 100x100 hole in the middle.
        Assert.Equal(80000, Measure.CoveredAreaOf(ring, key), 1);
    }

    [Fact]
    public void Layers_are_measured_apart_from_each_other()
    {
        var one = new LayerKey(65, 20);
        var other = new LayerKey(66, 20);

        var layout = LayoutOf((one, Square(0, 0, 100)), (other, Square(0, 0, 200)));

        Assert.Equal(10000, Measure.DrawnAreaOf(layout, one));
        Assert.Equal(40000, Measure.DrawnAreaOf(layout, other));

        Assert.Equal(new Bounds(0, 0, 100, 100), Measure.BoundsOf(layout, one));
        Assert.Equal(new Bounds(0, 0, 200, 200), Measure.BoundsOf(layout, other));

        //And the whole layout is both together.
        Assert.Equal(new Bounds(0, 0, 200, 200), Measure.BoundsOf(layout));

        var byLayer = Measure.BoundsByLayer(layout);

        Assert.Equal(2, byLayer.Count);
        Assert.Equal(new Bounds(0, 0, 100, 100), byLayer[one]);
    }

    ///<summary>
    ///Density is the covered figure over the extent, not the drawn one.
    ///
    ///Two of the three squares overlap on purpose. Without that the two areas are the same number and this
    ///passes just as well against a density that sums its shapes - which is the whole thing it is here to
    ///tell apart, and is what the first version of this test did not do.
    ///</summary>
    [Fact]
    public void Density_is_what_the_layer_covers_of_its_own_extent()
    {
        var key = new LayerKey(65, 20);

        //Two overlapping by a quarter, and a third out at the corner to stretch the extent to 300x300.
        var layout = LayoutOf(
            (key, Square(0, 0, 100)),
            (key, Square(50, 50, 100)),
            (key, Square(200, 200, 100)));

        //Drawn is 30000; covered is 27500, the overlap counted once.
        Assert.Equal(30000, Measure.DrawnAreaOf(layout, key));
        Assert.Equal(27500, Measure.CoveredAreaOf(layout, key), 1);

        Assert.Equal(27500.0 / 90000.0, Measure.DensityOf(layout, key), 6);

        //A layer with nothing on it is not a division by zero.
        Assert.Equal(0, Measure.DensityOf(layout, new LayerKey(99, 0)));
    }

    ///<summary>Labels are anchors, not shapes, so they are not area - but they are still in the extent.</summary>
    [Fact]
    public void A_label_has_no_area_and_still_counts_towards_the_box()
    {
        var key = new LayerKey(65, 20);

        var layout = LayoutOf((key, Square(0, 0, 100)));

        layout.Elements.Add(new Element
        {
            Layer = new Layer(key, "#000000"),
            Points = { At(500, 500) },
            Text = "PIN A"
        });

        Assert.Equal(10000, Measure.DrawnAreaOf(layout, key));
        Assert.Equal(new Bounds(0, 0, 500, 500), Measure.BoundsOf(layout));
    }

    #endregion **************************************************************************



    #region A real file *****************************************************************

    ///<summary>
    ///The bundled transistor, whose extent is a fact about the file rather than about this code - so it is
    ///worth pinning, and it is what catches a change that quietly moves every coordinate.
    ///</summary>
    [Fact]
    public void The_bundled_transistor_measures_the_same_every_time()
    {
        var layout = GdsFlattener.Flatten(new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample)));

        var bounds = Measure.BoundsOf(layout);

        Assert.False(bounds.IsEmpty);
        Assert.Equal(new Bounds(-1350, 0, 1450, 1500), bounds);

        //2800 by 1500 database units, which at this file's nanometer grid is 2.8 by 1.5 microns - the
        //right order for a transistor, and the check that says these are units rather than numbers.
        Assert.Equal(2800, bounds.Width);
        Assert.Equal(1500, bounds.Height);

        //And every layer's box is inside the whole layout's, which is true by construction and is the
        //cheapest way to catch a union that stopped unioning.
        foreach (var layer in Measure.BoundsByLayer(layout))
            Assert.True(bounds.Contains(layer.Value), $"{layer.Key} at {layer.Value} is outside {bounds}");
    }

    ///<summary>
    ///Covered can never exceed drawn, on any layer of any real file. A cheap invariant over the whole
    ///corpus, and one that a merge returning its input unmerged would pass while a merge that lost shapes
    ///or invented them would not.
    ///</summary>
    [Fact]
    public void Covered_never_exceeds_drawn_anywhere_in_the_corpus()
    {
        var wrong = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            var layout = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(path)));

            foreach (var layer in Measure.BoundsByLayer(layout).Keys)
            {
                double drawn = Measure.DrawnAreaOf(layout, layer);
                double covered = Measure.CoveredAreaOf(layout, layer);

                //A unit of slack per layer, for the rounding a clipping pass introduces.
                if (covered > drawn + 1)
                    wrong.Add($"{Path.GetFileName(path)} {layer}: covered {covered} > drawn {drawn}");
            }
        }

        Assert.Empty(wrong);
    }

    #endregion **************************************************************************
}
