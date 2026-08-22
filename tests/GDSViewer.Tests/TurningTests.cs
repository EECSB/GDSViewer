using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Turning and mirroring geometry.
///
///**Exact, or it is not worth having.** Only quarters and only mirrors about the axes, because every one of
///those maps whole numbers onto whole numbers about a whole-numbered point - so a shape comes out of one
///exactly on the grid it went in on. A turn of some other angle rounds every corner by a different amount
///and leaves geometry no mask shop would take, which is why it is not offered.
///
///The part worth testing hardest is the one that looks like it needs no testing: a shape inside a cell that
///is itself placed turned, or placed mirrored. Turning that shape where it sits comes out as some other
///quarter on screen, and on a mirrored cell it comes out as the opposite direction from the one asked for -
///both of which look plausible and are wrong.
///</summary>
public class TurningTests
{
    #region A cell to turn things in ************************************************

    ///
    ///One square in LEAF, placed once in TOP - turned, mirrored, or neither, as asked.
    ///
    ///The square is deliberately off-center and not square about the origin, so a turn that lands it back on
    ///itself would have to be a coincidence rather than a symmetry.
    ///
    private static GDS Placed(double angle, bool mirrored)
    {
        byte[] stamps = GdsTestData.Timestamps();

        var strans = new List<byte[]>();

        if (mirrored)
            strans.Add(GdsTestData.Record(RecordType.STRANS, new byte[] { 0x80, 0x00 }));
        else
            strans.Add(GdsTestData.Record(RecordType.STRANS, new byte[] { 0x00, 0x00 }));

        strans.Add(GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(angle)));

        return new GDS(GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("T")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(65)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(100, 200, 500, 200, 500, 400, 100, 400, 100, 200)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Concat(strans.ToArray()),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));
    }

    ///<summary>The one shape the file draws, and the cell it is being edited through.</summary>
    private static (Element Drawn, CellContext Context, GDS.ElementModel Model) Only(GDS gds)
    {
        var drawn = GdsFlattener.Flatten(gds).Elements.Single();

        return (drawn, CellContext.At(drawn.Source!), drawn.Source!.Model);
    }

    ///<summary>Where that shape is drawn now, in the layout's coordinates.</summary>
    private static Bounds DrawnBox(GDS gds)
    {
        return Bounds.Of(GdsFlattener.Flatten(gds).Elements.Single().Points);
    }

    private static void Turn(GDS gds, GdsII.Turn turn)
    {
        (_, var context, var model) = Only(gds);

        var box = DrawnBox(gds);

        double pivotX = Math.Round((box.Left + (double)box.Right) / 2);
        double pivotY = Math.Round((box.Bottom + (double)box.Top) / 2);

        var after = Turning.Coordinates(context, model, turn, pivotX, pivotY);

        Assert.NotNull(after);

        model.Element.XY!.Data = new Int4Data(after);
    }

    #endregion **********************************************************************



    #region The arithmetic **********************************************************

    ///<summary>
    ///A point to the right of the pivot goes below it, in a coordinate system counted the usual way up.
    ///Which direction that *looks* like is the view's business; see the buttons in Viewer2DSvg.
    ///</summary>
    [Fact]
    public void A_quarter_turn_takes_the_axes_round_one_step()
    {
        Assert.Equal((0.0, 1.0), Turning.Point(1, 0, GdsII.Turn.Quarter, 0, 0));
        Assert.Equal((-1.0, 0.0), Turning.Point(0, 1, GdsII.Turn.Quarter, 0, 0));
    }

    [Fact]
    public void Three_quarters_is_the_other_way()
    {
        Assert.Equal((0.0, -1.0), Turning.Point(1, 0, GdsII.Turn.ThreeQuarters, 0, 0));
        Assert.Equal((1.0, 0.0), Turning.Point(0, 1, GdsII.Turn.ThreeQuarters, 0, 0));
    }

    [Fact]
    public void A_mirror_moves_one_coordinate_and_leaves_the_other()
    {
        Assert.Equal((7.0, 3.0), Turning.Point(1, 3, GdsII.Turn.FlipX, 4, 99));
        Assert.Equal((1.0, 195.0), Turning.Point(1, 3, GdsII.Turn.FlipY, 4, 99));
    }

    [Fact]
    public void Turning_about_a_point_leaves_that_point_alone()
    {
        foreach (var turn in Enum.GetValues<GdsII.Turn>())
            Assert.Equal((25.0, -14.0), Turning.Point(25, -14, turn, 25, -14));
    }

    #endregion **********************************************************************



    #region On a shape **************************************************************

    ///
    ///**Four quarter turns are the file it was, byte for byte.**
    ///
    ///The whole claim of the feature in one assertion. Any rounding anywhere, any pivot that is not where it
    ///was the first time, any axis taken the wrong way, and the fourth turn does not land back on the first
    ///corner.
    ///
    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(180, false)]
    [InlineData(270, false)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    public void Four_quarter_turns_come_back_to_the_file_it_was(double angle, bool mirrored)
    {
        var gds = Placed(angle, mirrored);

        byte[] before = gds.Serialize();

        for (int i = 0; i < 4; i++)
            Turn(gds, GdsII.Turn.Quarter);

        Assert.Equal(before, gds.Serialize());
    }

    [Theory]
    [InlineData(GdsII.Turn.FlipX)]
    [InlineData(GdsII.Turn.FlipY)]
    public void Mirroring_twice_comes_back_to_the_file_it_was(GdsII.Turn turn)
    {
        var gds = Placed(90, false);

        byte[] before = gds.Serialize();

        Turn(gds, turn);

        Assert.NotEqual(before, gds.Serialize());

        Turn(gds, turn);

        Assert.Equal(before, gds.Serialize());
    }

    ///<summary>A quarter turn swaps how wide a thing is for how tall it is, wherever its cell is placed.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(270, true)]
    public void A_quarter_turn_swaps_width_for_height_on_screen(double angle, bool mirrored)
    {
        var gds = Placed(angle, mirrored);

        var before = DrawnBox(gds);

        Turn(gds, GdsII.Turn.Quarter);

        var after = DrawnBox(gds);

        Assert.Equal(before.Width, after.Height);
        Assert.Equal(before.Height, after.Width);
    }

    ///<summary>And a mirror does not: it is the same box, in the same place.</summary>
    [Theory]
    [InlineData(GdsII.Turn.FlipX)]
    [InlineData(GdsII.Turn.FlipY)]
    public void A_mirror_leaves_the_box_where_it_was(GdsII.Turn turn)
    {
        var gds = Placed(90, false);

        var before = DrawnBox(gds);

        Turn(gds, turn);

        Assert.Equal(before, DrawnBox(gds));
    }

    #endregion **********************************************************************



    #region Through a placement *****************************************************

    ///
    ///**The direction is the same on screen however the cell is placed.**
    ///
    ///The case the whole conjugation exists for, and the one that looks right when it is wrong. Turning a
    ///shape where it sits gives a different quarter on screen for a cell placed sideways, and the *opposite*
    ///direction for one placed mirrored - so a button marked "turn right" would turn some cells left.
    ///
    ///Read off the drawn corners rather than the file's: a corner that is to the right of the middle before
    ///the turn has to be below it afterwards, in the coordinates the view draws in, whatever the placement
    ///did on the way there.
    ///
    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(180, false)]
    [InlineData(270, false)]
    [InlineData(0, true)]
    [InlineData(90, true)]
    [InlineData(180, true)]
    [InlineData(270, true)]
    public void A_quarter_turn_goes_the_same_way_whatever_the_cell_is_placed_like(double angle, bool mirrored)
    {
        var gds = Placed(angle, mirrored);

        var box = DrawnBox(gds);

        double middleX = Math.Round((box.Left + (double)box.Right) / 2);
        double middleY = Math.Round((box.Bottom + (double)box.Top) / 2);

        //The corner furthest to the right of the middle, before.
        var rightmost = GdsFlattener.Flatten(gds).Elements.Single().Points
            .OrderByDescending(point => point.X)
            .First();

        Turn(gds, GdsII.Turn.Quarter);

        var corners = GdsFlattener.Flatten(gds).Elements.Single().Points;

        //Where that corner should have gone, worked out in the layout's own space.
        (double wantedX, double wantedY) = Turning.Point(rightmost.X, rightmost.Y, GdsII.Turn.Quarter, middleX, middleY);

        Assert.Contains(corners, point =>
            Math.Abs(point.X - wantedX) <= 1 && Math.Abs(point.Y - wantedY) <= 1);
    }

    ///<summary>
    ///And nothing is rounded on the way: a cell placed square keeps whole numbers whole, so the shape stays
    ///exactly on the grid it was drawn on rather than drifting a unit per turn.
    ///</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(90, false)]
    [InlineData(180, true)]
    public void A_turn_through_a_square_placement_rounds_nothing(double angle, bool mirrored)
    {
        var gds = Placed(angle, mirrored);

        var before = DrawnBox(gds);

        //Sixteen turns is four times round. Any drift at all shows as a box that is no longer where it was.
        for (int i = 0; i < 16; i++)
            Turn(gds, GdsII.Turn.Quarter);

        Assert.Equal(before, DrawnBox(gds));
    }

    #endregion **********************************************************************
}
