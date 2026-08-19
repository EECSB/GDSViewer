using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers what happens to input that is not a well-formed GDSII stream. The parser walks a byte array
///with a cursor driven by lengths read out of that same array, so unchecked it will index past the end,
///allocate a negative-sized buffer, or stall - all of which surface as exceptions that say nothing
///useful about the file. Every case here should raise InvalidDataException instead, which the upload
///path can catch and report.
///</summary>
public class InputValidationTests
{
    #region Framing ********************************************************************

    [Fact]
    public void An_empty_file_is_rejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => new GDS(Array.Empty<byte>()));

        Assert.Contains("no GDSII records", ex.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Trailing_bytes_too_short_for_a_record_header_are_rejected(int strayBytes)
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.MinimalLibrary(),
            new byte[strayBytes]);

        var ex = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("header", ex.Message);
    }

    [Fact]
    public void A_file_shorter_than_one_record_header_is_rejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => new GDS(new byte[] { 0x00, 0x06 }));

        Assert.Contains("header", ex.Message);
    }

    ///<summary>
    ///The length field covers the four header bytes, so anything below four is nonsense. Zero is the
    ///dangerous one: the cursor would never advance.
    ///</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_record_length_below_the_header_size_is_rejected(int declaredLength)
    {
        byte[] stream = { (byte)(declaredLength >> 8), (byte)declaredLength, 0x00, 0x02 };

        var ex = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("less than", ex.Message);
    }

    [Fact]
    public void A_record_declaring_more_bytes_than_remain_is_rejected()
    {
        //Claims a 200-byte record but supplies only the header and two bytes.
        byte[] stream = { 0x00, 0xC8, 0x00, 0x02, 0x02, 0x58 };

        var ex = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("only", ex.Message);
    }

    [Fact]
    public void A_library_truncated_part_way_through_a_record_is_rejected()
    {
        byte[] complete = GdsTestData.MinimalLibrary();

        var ex = Assert.Throws<InvalidDataException>(() => new GDS(complete[..^3]));

        Assert.IsType<InvalidDataException>(ex);
    }

    #endregion ************************************************************************



    #region Payloads that do not fit their type ****************************************

    ///<summary>
    ///The format pads an odd-length string with a null precisely so that every record is even. Rejecting
    ///it here catches a family of half-read payloads at the length, rather than downstream where the
    ///stray byte looks like a value that is one short.
    ///</summary>
    [Fact]
    public void A_record_with_an_odd_length_is_rejected()
    {
        //Length 5: a four-byte header and a single byte of payload.
        byte[] stream = new byte[] { 0x00, 0x05, 0x0D, 0x02, 0x00 };

        var error = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("odd", error.Message);
    }

    ///<summary>
    ///Even length, but not a whole number of values. This is the one that used to get through: the
    ///decoder divides and truncates, so the payload becomes an empty array, and then LAYER, WIDTH or MAG
    ///reading its single value threw IndexOutOfRangeException out of a renderer instead of the file being
    ///refused where it was read.
    ///</summary>
    [Fact]
    public void An_int4_payload_that_is_not_a_multiple_of_four_is_rejected()
    {
        //WIDTH is 0x0F03, so INT4 - given two bytes rather than four.
        byte[] stream = new byte[] { 0x00, 0x06, 0x0F, 0x03, 0x00, 0x01 };

        var error = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("INT4", error.Message);
        Assert.Contains("multiple of 4", error.Message);
    }

    [Fact]
    public void A_real8_payload_that_is_not_a_multiple_of_eight_is_rejected()
    {
        //UNITS is 0x0305, so REAL8 - given four bytes rather than eight.
        byte[] stream = new byte[] { 0x00, 0x08, 0x03, 0x05, 0x00, 0x01, 0x00, 0x02 };

        var error = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("REAL8", error.Message);
    }

    ///<summary>Decoded directly, since an odd record length is refused before a decoder sees it.</summary>
    [Fact]
    public void An_int2_payload_of_an_odd_length_is_rejected_by_the_decoder()
    {
        var error = Assert.Throws<InvalidDataException>(() => Int2Data.Decode(new byte[] { 0x00, 0x01, 0x02 }));

        Assert.Contains("INT2", error.Message);
    }

    ///<summary>The other direction: a payload that does fit is still read.</summary>
    [Fact]
    public void A_payload_that_fits_its_type_is_still_accepted()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 7, xy: GdsTestData.ClosedSquare()));

        Assert.True(GdsTestData.HasLayerNumber(gds, 7));
    }

    #endregion ************************************************************************



    #region Not a GDSII file **********************************************************

    [Fact]
    public void Plain_text_is_rejected()
    {
        byte[] stream = "This is definitely not a layout file, it is a sentence."u8.ToArray();

        Assert.Throws<InvalidDataException>(() => new GDS(stream));
    }

    [Fact]
    public void A_png_is_rejected()
    {
        byte[] stream = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

        Assert.Throws<InvalidDataException>(() => new GDS(stream));
    }

    [Fact]
    public void All_zero_bytes_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => new GDS(new byte[64]));
    }

    ///<summary>
    ///Well-framed records that simply are not a library. This gets past the byte-level checks, so it is
    ///the structural guard that has to catch it.
    ///</summary>
    [Fact]
    public void A_stream_that_does_not_start_with_HEADER_is_rejected()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.ENDLIB));

        var ex = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        Assert.Contains("HEADER", ex.Message);
    }

    #endregion ************************************************************************



    #region Incomplete structure ******************************************************

    [Fact]
    public void A_library_that_ends_before_ENDLIB_is_rejected()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))));

        var ex = Assert.Throws<InvalidDataException>(() => new GDS(stream));

        //Names the record it wanted, rather than only that the stream ran out.
        Assert.Contains("ENDLIB was expected", ex.Message);
        Assert.Contains("the stream ends there", ex.Message);
    }

    [Fact]
    public void A_structure_that_is_never_closed_is_rejected()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)));

        Assert.Throws<InvalidDataException>(() => new GDS(stream));
    }

    #endregion ************************************************************************



    #region Impossible timestamps ******************************************************

    ///<summary>A library whose BGNLIB and BGNSTR carry the given twelve values, everything else well formed.</summary>
    private static byte[] LibraryStampedWith(params short[] values)
    {
        return GdsTestData.MinimalLibrary(stamps: values);
    }

    ///<summary>
    ///Zeroed stamps are the case that matters, because tools do write them. The file has to open: this is
    ///metadata nothing here draws from, so it is not worth refusing an otherwise readable layout over.
    ///</summary>
    [Fact]
    public void A_zeroed_timestamp_does_not_stop_the_file_opening()
    {
        var gds = new GDS(LibraryStampedWith(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        Assert.Equal("TESTLIB", ((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value);
        Assert.Single(gds.StreamFormat.Structures);
    }

    [Theory]
    //Month 0, and month 13.
    [InlineData(new short[] { 122, 0, 13, 16, 59, 44, 123, 4, 22, 14, 56, 21 })]
    [InlineData(new short[] { 122, 13, 13, 16, 59, 44, 123, 4, 22, 14, 56, 21 })]
    //Day 0, and a day the month does not have.
    [InlineData(new short[] { 122, 12, 0, 16, 59, 44, 123, 4, 22, 14, 56, 21 })]
    [InlineData(new short[] { 122, 2, 30, 16, 59, 44, 123, 4, 22, 14, 56, 21 })]
    //An hour, minute and second past their range.
    [InlineData(new short[] { 122, 12, 13, 24, 59, 44, 123, 4, 22, 14, 56, 21 })]
    [InlineData(new short[] { 122, 12, 13, 16, 60, 44, 123, 4, 22, 14, 56, 21 })]
    [InlineData(new short[] { 122, 12, 13, 16, 59, 60, 123, 4, 22, 14, 56, 21 })]
    //Negative values, which a signed short allows.
    [InlineData(new short[] { -1, 12, 13, 16, 59, 44, 123, 4, 22, 14, 56, 21 })]
    //Only the second stamp is impossible, so the pair as a whole is not usable.
    [InlineData(new short[] { 122, 12, 13, 16, 59, 44, 0, 0, 0, 0, 0, 0 })]
    public void An_impossible_timestamp_leaves_the_pair_unset_rather_than_throwing(short[] values)
    {
        var gds = new GDS(LibraryStampedWith(values));

        Assert.Null(gds.Records[1].Timestamps);
        Assert.Equal(RecordType.BGNLIB, gds.Records[1].Type);
    }

    ///<summary>
    ///The raw values are untouched, so the record still writes back out as it came in and the text dump
    ///still reports what the file actually said. Only the convenience reading is withheld.
    ///</summary>
    [Fact]
    public void An_impossible_timestamp_keeps_its_raw_values_and_round_trips()
    {
        short[] values = new short[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        byte[] original = LibraryStampedWith(values);

        var gds = new GDS(original);

        Assert.Equal(values, ((Int2Data)gds.Records[1].Data!).Values);
        Assert.Contains("BGNLIB: 0 0 0 0 0 0 0 0 0 0 0 0  ", gds.AsText());
        Assert.Equal(original, gds.Serialize());
    }

    ///<summary>And the same through the text view's save path, where a person could type it by hand.</summary>
    [Fact]
    public void A_zeroed_timestamp_typed_as_text_is_accepted()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        gds.Deserialize(gds.AsText().Replace("BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21  ", "BGNLIB: 0 0 0 0 0 0 0 0 0 0 0 0  "));

        Assert.Null(gds.Records[1].Timestamps);
        Assert.Equal("TESTLIB", ((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value);
    }

    [Fact]
    public void A_usable_timestamp_is_still_read()
    {
        var gds = new GDS(LibraryStampedWith(122, 12, 13, 16, 59, 44, 123, 4, 22, 14, 56, 21));

        Assert.NotNull(gds.Records[1].Timestamps);
        Assert.Equal(new DateTime(2022, 12, 13, 16, 59, 44), gds.Records[1].Timestamps!.Value.Modified);
    }

    ///<summary>
    ///A year of 0 is not impossible, so it only leaves the pair unset when the rest of the stamp is broken
    ///too, which is the case for the all-zeros stamp above: month 0 and day 0 are what make that one
    ///unreadable, not the year.
    ///
    ///It reads as 2000 rather than 1900 because a two-digit year is the only reading that can be true.
    ///Years-since-1900 would make it a 1900 file, and GDSII did not exist then. KLayout resolves it the
    ///same way.
    ///</summary>
    [Fact]
    public void A_year_of_zero_with_a_real_month_and_day_reads_as_2000()
    {
        var gds = new GDS(LibraryStampedWith(0, 1, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0));

        Assert.Equal(new DateTime(2000, 1, 1), gds.Records[1].Timestamps!.Value.Modified);
    }

    #endregion ************************************************************************



    #region Valid input still parses **************************************************

    [Fact]
    public void The_validation_does_not_reject_a_well_formed_library()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Equal("TESTLIB", ((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value);
    }

    [Fact]
    public void The_validation_does_not_reject_a_real_file()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        Assert.Equal("mosfet", ((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value);
    }

    ///<summary>
    ///A record length is two bytes, and reading it as signed would make anything past 32767 negative and
    ///so be rejected as "less than the header size". Real XY records get large, so read it unsigned.
    ///</summary>
    [Fact]
    public void A_record_larger_than_a_signed_short_is_accepted()
    {
        //40000 bytes of coordinates: a length of 40004, which is negative when read as Int16.
        int[] points = new int[10000];
        for (int i = 0; i < points.Length; i++)
            points[i] = i;

        //Closed, since it is a boundary. Also the case for no upper bound on a coordinate count: 5000
        //pairs is far past the figure the format's tables give, and a real writer will exceed it.
        points[points.Length - 2] = points[0];
        points[points.Length - 1] = points[1];

        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("BIG")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(points)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));

        var gds = new GDS(stream);
        var xy = gds.StreamFormat.Structures[0].Elements[0].Element.XY;

        Assert.Equal(points, ((Int4Data)xy.Data!).Values);
        Assert.Equal(stream, gds.Serialize());
    }

    #endregion ************************************************************************
}
