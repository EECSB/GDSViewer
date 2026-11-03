using GdsII;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Two layouts laid over each other, and where they differ.
///
///**What is drawn, not what is written.** The cases that matter are the ones where the two disagree: the
///same area drawn as one rectangle in one file and two halves in the other is no difference, and one
///rectangle moved a unit is two - the strip it left and the strip it took. Each is held to the area it should
///come to, and to which file it is in.
///
public class CompareTests
{
    #region Helpers *******************************************************************

    private static readonly Layer Metal = new Layer(new LayerKey(68, 20), "#0000ff");

    private static readonly Layer Poly = new Layer(new LayerKey(66, 20), "#ff0000");

    private static Element Box(int left, int bottom, int right, int top, Layer layer)
    {
        return new Element { Layer = layer, Points = Shapes.Between(left, bottom, right, top) };
    }

    private static FlattenedLayout Layout(params Element[] elements)
    {
        return new FlattenedLayout { Elements = elements.ToList() };
    }

    private static double AreaOf(Comparison comparison, DifferenceSide side)
    {
        return comparison.Differences.Where(difference => difference.Side == side).Sum(difference => difference.Area);
    }

    #endregion ***********************************************************************



    [Fact]
    public void The_same_layout_is_the_same()
    {
        var one = Layout(Box(0, 0, 100, 100, Metal), Box(0, 0, 50, 50, Poly));
        var other = Layout(Box(0, 0, 100, 100, Metal), Box(0, 0, 50, 50, Poly));

        var comparison = Compare.Layouts(one, other);

        Assert.True(comparison.Same);
        Assert.Equal(new[] { Poly.Key, Metal.Key }, comparison.Compared);
    }

    ///<summary>The same area cut differently is the same drawing: one rectangle, or two halves of it, or two that overlap.</summary>
    [Fact]
    public void The_same_area_drawn_differently_is_no_difference()
    {
        var whole = Layout(Box(0, 0, 100, 100, Metal));
        var halves = Layout(Box(0, 0, 50, 100, Metal), Box(50, 0, 100, 100, Metal));
        var overlapping = Layout(Box(0, 0, 70, 100, Metal), Box(30, 0, 100, 100, Metal));

        Assert.True(Compare.Layouts(whole, halves).Same);
        Assert.True(Compare.Layouts(whole, overlapping).Same);
    }

    ///<summary>A rectangle moved ten units across is two differences: the strip it left, and the strip it took.</summary>
    [Fact]
    public void A_shape_moved_is_where_it_was_and_where_it_went()
    {
        var before = Layout(Box(0, 0, 100, 100, Metal));
        var after = Layout(Box(10, 0, 110, 100, Metal));

        var comparison = Compare.Layouts(after, before);

        Assert.Equal(2, comparison.Differences.Count);
        Assert.Equal(1000, AreaOf(comparison, DifferenceSide.OnlyHere));
        Assert.Equal(1000, AreaOf(comparison, DifferenceSide.OnlyThere));

        var here = comparison.Differences.Single(difference => difference.Side == DifferenceSide.OnlyHere);

        Assert.Equal(new Bounds(100, 0, 110, 100), here.Bounds);
    }

    ///<summary>A layer the other file does not have at all is all difference, on the side that has it.</summary>
    [Fact]
    public void A_layer_only_one_file_has_is_all_difference()
    {
        var comparison = Compare.Layouts(Layout(Box(0, 0, 100, 100, Metal), Box(0, 0, 10, 10, Poly)), Layout(Box(0, 0, 100, 100, Metal)));

        var difference = Assert.Single(comparison.Differences);

        Assert.Equal(Poly.Key, difference.Layer);
        Assert.Equal(DifferenceSide.OnlyHere, difference.Side);
        Assert.Equal(100, difference.Area);
    }

    ///<summary>A shape with a hole cut out of one of them comes back as the hole, on the side that has the solid.</summary>
    [Fact]
    public void A_hole_cut_in_one_is_a_difference_in_the_other()
    {
        var ring = Booleans.Combine(new[] { Shapes.Between(0, 0, 100, 100) }, new[] { Shapes.Between(40, 40, 60, 60) }, BooleanOperation.Not);
        var solid = Layout(Box(0, 0, 100, 100, Metal));
        var cut = new FlattenedLayout { Elements = ring.Select(outline => new Element { Layer = Metal, Points = outline }).ToList() };

        var comparison = Compare.Layouts(solid, cut);

        var difference = Assert.Single(comparison.Differences);

        Assert.Equal(DifferenceSide.OnlyHere, difference.Side);
        Assert.Equal(400, difference.Area);
    }

    ///<summary>A file in another unit is brought onto this one's before anything is compared.</summary>
    [Fact]
    public void Another_unit_is_scaled_onto_this_one()
    {
        var nanometers = Layout(Box(0, 0, 1000, 1000, Metal));
        var tenths = Layout(Box(0, 0, 100, 100, Metal));

        Assert.True(Compare.Layouts(nanometers, tenths, scaleThere: 10).Same);
        Assert.False(Compare.Layouts(nanometers, tenths).Same);
    }

    ///<summary>
    ///**Slivers too thin to matter can be left out, and only when asked.** One file snapped to a grid the other
    ///was not differs by a unit along every edge; a tolerance of two takes those away and keeps a real change.
    ///</summary>
    [Fact]
    public void A_sliver_under_the_tolerance_is_left_out_and_only_when_asked()
    {
        var one = Layout(Box(0, 0, 100, 100, Metal), Box(500, 0, 600, 100, Metal));
        var other = Layout(Box(0, 0, 101, 100, Metal), Box(500, 0, 650, 100, Metal));

        Assert.Equal(2, Compare.Layouts(one, other).Differences.Count);

        var tolerant = Compare.Layouts(one, other, ignoreUnder: 2);
        var kept = Assert.Single(tolerant.Differences);

        Assert.Equal(new Bounds(600, 0, 650, 100), kept.Bounds);
    }

    ///<summary>In layer order, and down a layer before across it - the order Next walks.</summary>
    [Fact]
    public void Differences_come_layer_by_layer_and_down_each()
    {
        var one = Layout(Box(0, 500, 10, 510, Metal), Box(0, 0, 10, 10, Metal), Box(0, 0, 10, 10, Poly));
        var comparison = Compare.Layouts(one, Layout());

        Assert.Equal(new[] { Poly.Key, Metal.Key, Metal.Key }, comparison.Differences.Select(difference => difference.Layer));
        Assert.True(comparison.Differences[1].Bounds.Bottom < comparison.Differences[2].Bounds.Bottom);
    }

    ///<summary>A real cell compared with itself after one edit: the difference is where the edit was.</summary>
    [Fact]
    public void A_cell_against_itself_after_a_move_differs_where_the_move_was()
    {
        var before = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var after = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        var structure = after.StreamFormat.Structures[0];
        var moved = structure.Elements.First(each => each.Element is BoundaryModel);

        new EditHistory().Do(new MoveElement(structure, moved, 50, 0));

        var comparison = Compare.Layouts(GdsFlattener.Flatten(after), GdsFlattener.Flatten(before));

        Assert.False(comparison.Same);
        Assert.All(comparison.Differences, difference => Assert.Equal(GdsFlattener.KeyOf((IHasLayer)moved.Element), difference.Layer));
    }

    [Fact]
    public void Differences_are_drawn_by_side_with_the_current_one_picked_out()
    {
        var comparison = Compare.Layouts(Layout(Box(10, 0, 110, 100, Metal)), Layout(Box(0, 0, 100, 100, Metal)));

        string drawn = SvgWriter.Differences(comparison.Differences, 1);

        Assert.Contains($"id=\"{SvgWriter.DifferencesId}\"", drawn);
        Assert.Contains(SvgWriter.DifferenceHereClass, drawn);
        Assert.Contains(SvgWriter.DifferenceThereClass, drawn);
        Assert.Contains($"{SvgWriter.DifferenceCurrentClass}\" {SvgWriter.DifferenceAttribute}=\"1\"", drawn);
        Assert.Equal("", SvgWriter.Differences(new List<Difference>()));
    }
}
