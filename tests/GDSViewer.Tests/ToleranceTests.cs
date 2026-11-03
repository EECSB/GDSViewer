using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Where this reader refuses a file that KLayout opens with a warning - and the one place it no longer does.
///
///Opening and saving apply the same rules on purpose, so each of these refuses on both paths - a file that
///opens is a file that saves, and there is one answer to "is this valid here" rather than one per entry
///point. See [Known gaps](../docs/DOCUMENTATION.md#known-gaps) for the reasoning and the cost.
///
///They are pinned so the table there is measured rather than assumed, and so that relaxing any single rule
///- which is the intended fix if a real file is ever refused - shows up as a deliberate change here instead
///of a silent one. Two of them have been relaxed that way, and the tests below pin the reading rather than
///the refusal. An element split across several XY records is read now: it became possible when Fracture
///made such a shape writable, which is the condition the whole list is held to. And an unclosed boundary is
///closed where it is read, because a real file - Efabless's SPM example - turned out to have one.
///</summary>
public class ToleranceTests
{
    private static byte[] Library(params byte[][] recordsAfterUnits)
    {
        var preamble = new[]
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("TESTLIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TESTCELL"))
        };

        var closing = new[]
        {
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)
        };

        return GdsTestData.Concat(preamble.Concat(recordsAfterUnits).Concat(closing).ToArray());
    }

    private static byte[] Boundary(params int[] xy)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL));
    }

    ///
    ///A boundary that does not come back to its first point is closed where it is read.
    ///
    ///**This one moved out of the refusals too.** KLayout drops a repeated last point if there is one and
    ///otherwise takes the list as the polygon, so an open quadrilateral is a quadrilateral to it - and this
    ///reader refused the whole file over it, until a real one arrived: the Efabless SPM example, the OpenLane
    ///demo design, which has one such boundary in 63,069. It is in an early copy of
    ///`sky130_ef_sc_hd__decap_12`, so anything built with that release of the cell carries it too.
    ///
    ///That ring, as the file has it: an li1 strip along the bottom of the cell that runs round from
    ///(0, -85) and stops at (0, 0), a point on its own left edge, without coming back.
    ///
    ///Closed rather than drawn open, so it saves: the ring is repeated in the record itself, which is the
    ///condition every relaxation here is held to.
    ///
    [Fact]
    public void An_unclosed_boundary_is_closed_where_it_is_read()
    {
        var gds = new GDS(Library(Boundary(0, -85, 5520, -85, 5520, 85, 0, 85, 0, 0)));

        //The strip, and its first corner again at the end.
        var drawn = GdsFlattener.Flatten(gds).Elements.Single();

        Assert.Equal(
            new[] { "0,-85", "5520,-85", "5520,85", "0,85", "0,0", "0,-85" },
            drawn.Points.Select(point => FormattableString.Invariant($"{point.X},{point.Y}")).ToList());

        //In the record, not only in the drawing - the text view and the writer read the same list.
        var xy = Assert.IsType<Int4Data>(gds.Records.Single(record => record.Type == RecordType.XY).Data);

        Assert.Equal(new[] { 0, -85, 5520, -85, 5520, 85, 0, 85, 0, 0, 0, -85 }, xy.Values);

        //So what is written out is a boundary the format allows, and reads back as the same square.
        var again = new GDS(gds.Serialize());

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(again));
    }

    ///<summary>
    ///A BOX is still held to it. The relaxation is for the one rule a real file tripped; no file has been seen
    ///with a BOX at all, so there is nothing to say it should be read any more loosely than the format says.
    ///</summary>
    [Fact]
    public void An_unclosed_box_is_still_refused()
    {
        byte[] box = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BOX),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.BOXTYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0, 100, 100, 0, 100, 0, 50)),
            GdsTestData.Record(RecordType.ENDEL));

        var thrown = Assert.Throws<InvalidDataException>(() => new GDS(Library(box)));

        Assert.Contains("has to close on the point it starts from", thrown.Message);
    }

    ///<summary>Three points and no repeat: a triangle, which KLayout draws.</summary>
    [Fact]
    public void A_three_point_boundary_is_refused()
    {
        var thrown = Assert.Throws<InvalidDataException>(() => new GDS(Library(Boundary(0, 0, 100, 0, 50, 100))));

        Assert.Contains("needs at least 4 coordinate pairs", thrown.Message);
    }

    ///
    ///A boundary whose points are split across consecutive XY records reads as one shape.
    ///
    ///**This one moved out of the list above.** A record holds at most 8,191 points, so a writer with a
    ///larger shape either cuts it into several elements or splits its points across several XY records -
    ///and the second is what KLayout's `allow_multi_xy_records` exists for. This reader refused it, and the
    ///refusal read as `XY where ENDEL was expected`, which describes where the parser was rather than what
    ///the file did.
    ///
    ///It was refused for a reason that has since gone. Accepting it meant drawing a shape this app could
    ///not then write back, and one set of rules for both directions is the rule here. Fracture answered
    ///that - a shape too large for one record is cut into several boundaries on the way out - so the file
    ///both opens and saves now, which is the condition that was always attached to relaxing this.
    ///
    ///Three records rather than two, so the join is a run and not a special case for a pair.
    ///
    [Fact]
    public void An_element_whose_points_are_split_across_xy_records_is_read_as_one()
    {
        byte[] stream = Library(GdsTestData.Concat(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(100, 100)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 100, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL)));

        var gds = new GDS(stream);

        //One square, with every corner from every record in the order the file had them.
        var drawn = GdsFlattener.Flatten(gds).Elements.Single();

        Assert.Equal(
            new[] { "0,0", "100,0", "100,100", "0,100", "0,0" },
            drawn.Points.Select(point => FormattableString.Invariant($"{point.X},{point.Y}")).ToList());

        //
        //And the run is one record in the library, not three.
        //
        //The models are built over this same list, so a joined shape that left the pieces behind would have
        //the text view and the edit path describing something other than what is drawn.
        //
        Assert.Single(gds.Records, record => record.Type == RecordType.XY);

        //Which is what makes it saveable: written back out and read again, it is still the one square.
        var again = new GDS(gds.Serialize());

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(again));
    }

    ///<summary>
    ///Some older writers pad a file out to a multiple of 2048 bytes with nulls. KLayout stops at ENDLIB and
    ///never looks; here the padding is read as a record of length zero and the file is refused. Neither
    ///KLayout file pads, so nothing has been seen to do this - the format's block structure is where the
    ///habit comes from.
    ///</summary>
    [Fact]
    public void Null_padding_after_endlib_is_refused()
    {
        byte[] library = GdsTestData.MinimalLibrary();
        byte[] padded = new byte[2048];

        library.CopyTo(padded, 0);

        var thrown = Assert.Throws<InvalidDataException>(() => new GDS(padded));

        Assert.Contains("is less than the four-byte header", thrown.Message);
    }

    ///<summary>A single-point path has no direction, but KLayout warns and draws it rather than refusing.</summary>
    [Fact]
    public void A_single_point_path_is_refused()
    {
        byte[] stream = Library(GdsTestData.Concat(
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL)));

        var thrown = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("needs at least 2 coordinate pairs", thrown.Message);
    }

    ///<summary>A path of twice the points one record holds, split across two XY records as a writer at the ceiling splits it.</summary>
    private static byte[] LongPath()
    {
        var coordinates = new int[8192 * 2];

        for (int i = 0; i < 8192; i++)
        {
            coordinates[i * 2] = i * 10;
            coordinates[(i * 2) + 1] = (i % 2) * 10;
        }

        return Library(GdsTestData.Concat(
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.WIDTH, GdsTestData.Int4(4)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(coordinates[..8000])),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(coordinates[8000..])),
            GdsTestData.Record(RecordType.ENDEL)));
    }

    ///
    ///The other way round for a path: one over the ceiling goes out as the run of XY records this reads back as one -
    ///see Fracture, and why a path is not cut into several the way a boundary is. Such a path opened and then could
    ///not be saved, and `gds convert` fell over on it with a trace.
    ///
    [Fact]
    public void A_path_of_more_points_than_one_record_holds_saves_and_reads_back_as_one()
    {
        var gds = new GDS(LongPath());

        var path = Assert.Single(gds.StreamFormat.Structures.Single().Elements);

        Assert.Equal(8192 * 2, ((Int4Data)path.Element.XY!.Data!).Values.Length);

        byte[] written = gds.Serialize();

        var again = new GDS(written);

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(again));
        Assert.Single(again.Records, record => record.Type == RecordType.XY);
        Assert.Equal(8192 * 2, ((Int4Data)again.StreamFormat.Structures.Single().Elements.Single().Element.XY!.Data!).Values.Length);
    }
}
