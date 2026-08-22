using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers Preview, which frames a layout so it can be drawn beside a list of them.
///
///The framing is the whole of it. Drawing the shapes is SvgWriter's and is covered there; what a thumbnail
///needs on top is a viewBox, and that is arithmetic over the coordinates rather than anything about the
///format - which makes it exactly the kind of thing that can be got wrong quietly, since a badly framed
///drawing is still a drawing.
///</summary>
public class PreviewTests
{
    #region Building libraries **********************************************************

    private static byte[] Cell(string name, int[] xy)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));
    }

    ///<summary>A structure with nothing in it, which a library is allowed to contain.</summary>
    private static byte[] EmptyCell(string name)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.ENDSTR));
    }

    private static byte[] Library(params byte[][] structures)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Concat(structures),
            GdsTestData.Record(RecordType.ENDLIB));
    }

    private static FlattenedLayout LayoutOf(params byte[][] structures)
    {
        return GdsFlattener.Flatten(new GDS(Library(structures)));
    }

    private static int[] Square(int left, int top, int size)
    {
        return new[]
        {
            left, top,
            left + size, top,
            left + size, top + size,
            left, top + size,
            left, top
        };
    }

    #endregion *************************************************************************



    #region The box ********************************************************************

    [Fact]
    public void The_box_is_the_corners_of_everything_drawn()
    {
        var layout = LayoutOf(Cell("A", Square(10, 20, 100)));

        Assert.Equal((10, 20, 100, 100), Preview.BoxOf(layout));
    }

    ///<summary>Nothing drawn is no box, rather than a box at the origin of no size.</summary>
    [Fact]
    public void An_empty_layout_has_no_box()
    {
        Assert.Null(Preview.BoxOf(LayoutOf(EmptyCell("NOTHING"))));
    }

    ///
    ///A side of zero is floored at one.
    ///
    ///A cell that is one straight line has no height at all, and a viewBox with a zero in it draws nothing -
    ///so the one case where a drawing is most likely to be a mistake would silently produce a blank square.
    ///
    [Fact]
    public void A_flat_shape_still_has_a_side()
    {
        var layout = LayoutOf(Cell("LINE", new[] { 0, 50, 200, 50, 200, 50, 0, 50, 0, 50 }));

        var box = Preview.BoxOf(layout);

        Assert.NotNull(box);
        Assert.Equal(200, box!.Value.Width);
        Assert.Equal(1, box.Value.Height);
    }

    #endregion *************************************************************************



    #region The frame ******************************************************************

    ///
    ///The frame is around the shapes, not around the origin.
    ///
    ///Which is the whole point of computing one: a cell placed far from the origin draws at its own
    ///coordinates, and a viewBox starting at 0 0 would put it off the edge of a thumbnail that looked
    ///perfectly fine - empty, but fine.
    ///
    [Fact]
    public void A_shape_far_from_the_origin_is_framed_around_itself()
    {
        var layout = LayoutOf(Cell("FAR", Square(100000, 200000, 1000)));

        (_, string viewBox) = Preview.Of(layout, 1f);

        int[] box = viewBox.Split(' ').Select(int.Parse).ToArray();

        //A twentieth of the wider side, either side of it.
        Assert.Equal(new[] { 100000 - 50, 200000 - 50, 1000 + 100, 1000 + 100 }, box);
    }

    ///<summary>The same inset on both axes, so a tall cell and a wide one are framed alike.</summary>
    [Fact]
    public void The_margin_comes_off_the_wider_side()
    {
        var layout = LayoutOf(Cell("TALL", new[] { 0, 0, 20, 0, 20, 2000, 0, 2000, 0, 0 }));

        (_, string viewBox) = Preview.Of(layout, 1f);

        int[] box = viewBox.Split(' ').Select(int.Parse).ToArray();

        //2000/20 is 100, taken off both sides of both axes rather than a twentieth of each.
        Assert.Equal(new[] { -100, -100, 20 + 200, 2000 + 200 }, box);
    }

    [Fact]
    public void A_layout_with_nothing_in_it_frames_a_unit_box()
    {
        (string markup, string viewBox) = Preview.Of(LayoutOf(EmptyCell("NOTHING")), 1f);

        Assert.Equal("", markup);
        Assert.Equal("0 0 1 1", viewBox);
    }

    ///<summary>And there is something to draw when there is something in it.</summary>
    [Fact]
    public void A_layout_with_a_shape_in_it_draws_one()
    {
        (string markup, _) = Preview.Of(LayoutOf(Cell("A", Square(0, 0, 100))), 1f);

        Assert.NotEqual("", markup);
    }

    #endregion *************************************************************************
}
