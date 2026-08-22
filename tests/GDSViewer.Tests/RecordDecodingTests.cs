using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers GDS.Record's byte-level decoding. Every record is fed through the real parser rather than
///poked at directly, because Record picks its own data type from the record type and the conversion
///helpers are private.
///</summary>
public class RecordDecodingTests
{
    ///<summary>Parses a stream and returns its records, bypassing the structural model.</summary>
    private static List<GDS.Record> ParseRecords(byte[] stream)
    {
        //A well-formed library is needed because the GDS constructor also builds the model tree.
        var gds = new GDS(stream);

        return gds.Records;
    }

    private static GDS.Record SingleRecord(RecordType type, byte[] payload)
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("L")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.ENDLIB),
            GdsTestData.Record(type, payload));

        return ParseRecords(stream).Last();
    }

    #region Header framing **************************************************************

    [Fact]
    public void Record_length_and_type_are_read_big_endian()
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary());

        Assert.Equal(RecordType.HEADER, records[0].Type);
        Assert.Equal(RecordType.BGNLIB, records[1].Type);
        Assert.Equal(RecordType.LIBNAME, records[2].Type);
        Assert.Equal(RecordType.UNITS, records[3].Type);
        Assert.Equal(RecordType.ENDLIB, records[^1].Type);
    }

    [Fact]
    public void Every_record_in_the_stream_is_parsed()
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary());

        Assert.Equal(13, records.Count);
    }

    #endregion *************************************************************************



    #region Declared data types ********************************************************

    ///<summary>
    ///A GDSII record type word packs the record type in its high byte and its data type in the low one -
    ///LAYER is 0x0D02, type 0x0D carrying INT2. So the low byte is the format's own statement of what a
    ///record contains, and every decoded record has to agree with it.
    ///</summary>
    public static TheoryData<RecordType> AllRecordTypes()
    {
        var types = new TheoryData<RecordType>();

        foreach (RecordType type in Enum.GetValues<RecordType>())
            types.Add(type);

        return types;
    }

    ///<summary>A payload of the right shape for a record, big enough for the records that need more than one value.</summary>
    private static byte[] PayloadFor(RecordType type, RecordDataType dataType)
    {
        //These three read more than one value out of their payload, so a minimal one would not do.
        if (type == RecordType.BGNLIB || type == RecordType.BGNSTR)
            return GdsTestData.Timestamps();

        if (type == RecordType.UNITS)
            return GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9));

        switch (dataType)
        {
            case RecordDataType.NODATA:
                return Array.Empty<byte>();
            case RecordDataType.BITARRAY:
                return new byte[] { 0x00, 0x01 };
            case RecordDataType.INT2:
                return GdsTestData.Int2(1);
            case RecordDataType.INT4:
                return GdsTestData.Int4(1);
            case RecordDataType.REAL4:
                return new byte[] { 0x41, 0x10, 0x00, 0x00 };
            case RecordDataType.REAL8:
                return GdsTestData.Real8(1.0);
            case RecordDataType.ASCII:
                return GdsTestData.Ascii("AB");
            default:
                return Array.Empty<byte>();
        }
    }

    [Theory]
    [MemberData(nameof(AllRecordTypes))]
    public void The_decoded_data_type_matches_the_one_the_record_type_declares(RecordType type)
    {
        var declared = (RecordDataType)((short)type & 0xFF);

        var record = SingleRecord(type, PayloadFor(type, declared));

        Assert.Equal(declared, record.DataType);
    }

    [Theory]
    [MemberData(nameof(AllRecordTypes))]
    public void A_record_with_a_payload_decodes_it(RecordType type)
    {
        var declared = (RecordDataType)((short)type & 0xFF);

        if (declared == RecordDataType.NODATA)
            return;

        var record = SingleRecord(type, PayloadFor(type, declared));

        Assert.NotNull(record.Data);
    }

    #endregion ************************************************************************



    #region INT2 ***********************************************************************

    [Fact]
    public void Int2_with_a_single_value_decodes_to_a_scalar_short()
    {
        var record = SingleRecord(RecordType.LAYER, GdsTestData.Int2(235));

        Assert.Equal(RecordDataType.INT2, record.DataType);
        Assert.Equal((short)235, ((Int2Data)record.Data!).Value);
    }

    [Fact]
    public void Int2_with_several_values_decodes_to_a_short_array()
    {
        var record = SingleRecord(RecordType.COLROW, GdsTestData.Int2(3, 7));

        short[] values = ((Int2Data)record.Data!).Values;

        Assert.Equal(new short[] { 3, 7 }, values);
    }

    [Fact]
    public void Int2_decodes_negative_values()
    {
        var record = SingleRecord(RecordType.LAYER, GdsTestData.Int2(-42));

        Assert.Equal((short)-42, ((Int2Data)record.Data!).Value);
    }

    ///<summary>
    ///The stamps read 122 and 123 in the file, which is the years-since-1900 convention, so they come out
    ///as 2022 and 2023. See the year-convention tests below.
    ///</summary>
    [Fact]
    public void Bgnlib_timestamps_are_exposed_as_a_pair_of_DateTime()
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary());
        var bgnlib = records[1];

        var (modified, accessed) = bgnlib.Timestamps!.Value;

        Assert.Equal(new DateTime(2022, 12, 13, 16, 59, 44), modified);
        Assert.Equal(new DateTime(2023, 4, 22, 14, 56, 21), accessed);
    }

    ///<summary>
    ///Writers disagree about the year field and three conventions are in circulation, so a year under 50 is
    ///read as a two-digit 2000s year and anything else below 1000 as an offset from 1900. These pin both
    ///cuts, because they are guesses and a future reader should be able to see exactly which values they
    ///change. The 50 matches KLayout's own reader.
    ///</summary>
    [Theory]
    //The years-since-1900 convention, which Mosfet.gds uses.
    [InlineData((short)122, 2022)]
    [InlineData((short)123, 2023)]
    //A two-digit year, which years-since-1900 would date to a decade GDSII did not exist in.
    [InlineData((short)0, 2000)]
    [InlineData((short)24, 2024)]
    //Either side of the cut between the two readings.
    [InlineData((short)49, 2049)]
    [InlineData((short)50, 1950)]
    //The last value that is shifted at all, and the first that is not.
    [InlineData((short)999, 2899)]
    [InlineData((short)1000, 1000)]
    //The full-year convention, which the 896 sky130 cells use, is left alone.
    [InlineData((short)2019, 2019)]
    [InlineData((short)2022, 2022)]
    public void A_small_year_is_read_as_the_year_its_writer_meant(short written, int expected)
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary(stamps: new short[] { written, 6, 15, 12, 30, 0, written, 6, 15, 12, 30, 0 }));

        Assert.Equal(expected, records[1].Timestamps!.Value.Modified.Year);
    }

    ///
    ///And the record says whether the century came from the file or from us.
    ///
    ///**Because the interpretation is right and it is still an interpretation.** Three conventions are in
    ///circulation for the year field and nothing in the record says which is in use, so a small year is
    ///guessed at - correctly, for every file anyone will open. A date reported flat is a date somebody may
    ///go on to quote, and this is the difference between reporting what the file said and reporting what we
    ///decided it meant.
    ///
    ///The pairs below are the two real conventions and the two that are left alone.
    ///
    [Theory]
    //Guessed: the tm_year convention Mosfet.gds writes, and a bare two-digit year.
    [InlineData((short)122, true)]
    [InlineData((short)24, true)]
    //Taken as written: a full year, which the 896 sky130 cells use, and anything past the outer cut.
    [InlineData((short)2019, false)]
    [InlineData((short)1000, false)]
    public void The_record_says_whether_the_century_was_inferred(short written, bool inferred)
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary(stamps: new short[] { written, 6, 15, 12, 30, 0, written, 6, 15, 12, 30, 0 }));

        Assert.Equal(inferred, records[1].YearWasInferred);
    }

    ///<summary>A stamp too broken to read at all leaves nothing to have inferred.</summary>
    [Fact]
    public void A_year_that_yields_no_date_is_not_reported_as_inferred()
    {
        //Month 13, which makes the whole pair unreadable - see toTimestampPair.
        var records = ParseRecords(GdsTestData.MinimalLibrary(stamps: new short[] { 122, 13, 15, 12, 30, 0, 122, 13, 15, 12, 30, 0 }));

        Assert.Null(records[1].Timestamps);
        Assert.False(records[1].YearWasInferred);
    }

    ///<summary>
    ///Negative is left alone deliberately: it is corruption under either convention, and shifting it would
    ///turn nonsense into a plausible 19th-century date instead of leaving it unreadable.
    ///</summary>
    [Fact]
    public void A_negative_year_is_not_shifted_into_the_nineteenth_century()
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary(stamps: new short[] { -1, 6, 15, 12, 30, 0, -1, 6, 15, 12, 30, 0 }));

        Assert.Null(records[1].Timestamps);
    }

    ///<summary>The interpretation must not reach the payload, or a file would not write back out as it came in.</summary>
    [Fact]
    public void Reading_the_year_as_an_offset_does_not_change_the_stored_value()
    {
        byte[] stream = GdsTestData.MinimalLibrary();

        var gds = new GDS(stream);

        Assert.Equal((short)122, ((Int2Data)gds.Records[1].Data!).Values[0]);
        Assert.Equal(2022, gds.Records[1].Timestamps!.Value.Modified.Year);
        Assert.Contains("BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21  ", gds.AsText());
        Assert.Equal(stream, gds.Serialize());
    }

    ///<summary>Both conventions, each read from a real file rather than a hand-built one.</summary>
    [Fact]
    public void Both_conventions_in_the_sample_files_come_out_as_the_year_meant()
    {
        var handMade = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var sky130 = new GDS(GdsTestData.ReadSample(GdsTestData.Sky130Sample("sky130_fd_sc_hd__nand2_1.gds")));

        Assert.Equal(2022, handMade.Records[1].Timestamps!.Value.Modified.Year);
        Assert.Equal(2019, sky130.Records[1].Timestamps!.Value.Modified.Year);
    }

    #endregion *************************************************************************



    #region INT4 and XY ****************************************************************

    [Fact]
    public void Xy_decodes_to_a_flat_big_endian_int_array()
    {
        var record = SingleRecord(RecordType.XY, GdsTestData.Int4(0, 0, 1000, 0, 1000, 500));

        Assert.Equal(RecordDataType.INT4, record.DataType);
        Assert.Equal(new[] { 0, 0, 1000, 0, 1000, 500 }, ((Int4Data)record.Data!).Values);
    }

    [Fact]
    public void Xy_decodes_negative_coordinates()
    {
        var record = SingleRecord(RecordType.XY, GdsTestData.Int4(-1000, -2000));

        Assert.Equal(new[] { -1000, -2000 }, ((Int4Data)record.Data!).Values);
    }

    [Fact]
    public void Xy_decodes_values_beyond_the_16_bit_range()
    {
        var record = SingleRecord(RecordType.XY, GdsTestData.Int4(1_000_000, -1_000_000));

        Assert.Equal(new[] { 1_000_000, -1_000_000 }, ((Int4Data)record.Data!).Values);
    }

    #endregion *************************************************************************



    #region REAL8 **********************************************************************

    ///<summary>
    ///Hand-written canonical encodings, so this does not just check the test helper against itself.
    ///1.0 is 1/16 * 16^1, which is exponent 65 (excess-64) and a mantissa of 2^52.
    ///</summary>
    [Theory]
    [InlineData(new byte[] { 0x41, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 1.0)]
    [InlineData(new byte[] { 0xC1, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, -1.0)]
    [InlineData(new byte[] { 0x40, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0.5)]
    [InlineData(new byte[] { 0x41, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 2.0)]
    [InlineData(new byte[] { 0x42, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 16.0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0.0)]
    public void Real8_decodes_canonical_encodings(byte[] encoded, double expected)
    {
        var record = SingleRecord(RecordType.MAG, encoded);

        Assert.Equal(RecordDataType.REAL8, record.DataType);
        Assert.Equal(expected, ((Real8Data)record.Data!).Value, 1e-12);
    }

    [Fact]
    public void Real8_treats_a_zero_mantissa_as_zero_whatever_the_exponent()
    {
        var record = SingleRecord(RecordType.MAG, new byte[] { 0x7F, 0, 0, 0, 0, 0, 0, 0 });

        Assert.Equal(0.0, ((Real8Data)record.Data!).Value);
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(1e-9)]
    [InlineData(1.0)]
    [InlineData(-0.25)]
    [InlineData(1000.0)]
    public void Real8_round_trips_through_the_encoder(double value)
    {
        var record = SingleRecord(RecordType.ANGLE, GdsTestData.Real8(value));

        Assert.Equal(value, ((Real8Data)record.Data!).Value, Math.Abs(value) * 1e-12 + 1e-18);
    }

    [Fact]
    public void Units_decodes_both_reals_into_a_two_element_array()
    {
        var records = ParseRecords(GdsTestData.MinimalLibrary());
        var units = records[3];

        double[] values = ((Real8Data)units.Data!).Values;

        Assert.Equal(2, values.Length);
        Assert.Equal(0.001, values[0], 1e-15);
        Assert.Equal(1e-9, values[1], 1e-21);
    }

    #endregion *************************************************************************



    #region ASCII **********************************************************************

    [Fact]
    public void Ascii_decodes_an_even_length_string_unchanged()
    {
        var record = SingleRecord(RecordType.STRNAME, GdsTestData.Ascii("CELL"));

        Assert.Equal(RecordDataType.ASCII, record.DataType);
        Assert.Equal("CELL", ((AsciiData)record.Data!).Value);
    }

    [Fact]
    public void Ascii_strips_the_null_used_to_pad_an_odd_length_string()
    {
        var record = SingleRecord(RecordType.STRNAME, GdsTestData.Ascii("ODD"));

        Assert.Equal("ODD", ((AsciiData)record.Data!).Value);
    }

    [Fact]
    public void Ascii_keeps_interior_characters_that_are_not_the_trailing_null()
    {
        var record = SingleRecord(RecordType.LIBNAME, GdsTestData.Ascii("a b_c-1"));

        Assert.Equal("a b_c-1", ((AsciiData)record.Data!).Value);
    }

    #endregion *************************************************************************



    #region NODATA and BITARRAY ********************************************************

    [Fact]
    public void Nodata_records_carry_no_data()
    {
        var record = SingleRecord(RecordType.ENDEL, Array.Empty<byte>());

        Assert.Equal(RecordDataType.NODATA, record.DataType);
        Assert.Null(record.Data);
    }

    [Fact]
    public void Bitarray_records_keep_their_raw_bytes()
    {
        var record = SingleRecord(RecordType.STRANS, new byte[] { 0x80, 0x00 });

        Assert.Equal(RecordDataType.BITARRAY, record.DataType);
        Assert.Equal(new byte[] { 0x80, 0x00 }, ((BitArrayData)record.Data!).Value);
    }

    #endregion *************************************************************************
}
