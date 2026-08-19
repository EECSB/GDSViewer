using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

//xUnit has a Record of its own, and the test project imports Xunit globally.
using Record = GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers TextFormat, which reads back the dump AsText writes - the text view's save path.
///
///The load-bearing test here is the corpus one: every bundled file dumped to text, read back, and
///re-serialized has to come out byte for byte identical to the file on disk. That is a stronger statement
///than any hand-written case, because it says the text format loses nothing about a real file.
///</summary>
public class TextFormatTests
{
    private static Record Single(string line)
    {
        return Assert.Single(TextFormat.ParseRecords(line));
    }

    #region Payload types **************************************************************

    [Fact]
    public void A_scalar_int2_reads_back()
    {
        var record = Single("HEADER: 600 \n");

        Assert.Equal(RecordType.HEADER, record.Type);
        Assert.Equal((short)600, ((Int2Data)record.Data!).Value);
    }

    [Fact]
    public void An_int2_array_reads_back()
    {
        var record = Single("BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21  \n");

        Assert.Equal(new short[] { 122, 12, 13, 16, 59, 44, 123, 4, 22, 14, 56, 21 }, ((Int2Data)record.Data!).Values);
    }

    ///<summary>
    ///The twelve INT2 values of a BGNLIB are two timestamps, and that has to survive the text - including
    ///the years-since-1900 reading that turns 122 into 2022.
    ///</summary>
    [Fact]
    public void A_timestamp_pair_is_rebuilt_from_text()
    {
        var record = Single("BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21  \n");

        Assert.NotNull(record.Timestamps);
        Assert.Equal(new DateTime(2022, 12, 13, 16, 59, 44), record.Timestamps!.Value.Modified);
        Assert.Equal(new DateTime(2023, 4, 22, 14, 56, 21), record.Timestamps.Value.Accessed);
    }

    [Fact]
    public void An_int4_array_reads_back_including_negatives()
    {
        var record = Single("XY: -600 600 550 -1100  \n");

        Assert.Equal(new[] { -600, 600, 550, -1100 }, ((Int4Data)record.Data!).Values);
    }

    ///<summary>WIDTH is 0x0F03, so its low byte declares INT4 - wider than a short holds.</summary>
    [Fact]
    public void A_width_reads_back_as_int4()
    {
        var record = Single("WIDTH: 140 \n");

        Assert.Equal(140, ((Int4Data)record.Data!).Value);
    }

    [Fact]
    public void A_real8_pair_reads_back()
    {
        var record = Single("UNITS: 0.001 1E-09  \n");

        var values = ((Real8Data)record.Data!).Values;

        Assert.Equal(0.001, values[0], 1e-15);
        Assert.Equal(1e-9, values[1], 1e-20);
    }

    [Fact]
    public void An_ascii_value_reads_back_verbatim()
    {
        var record = Single("LIBNAME: TESTLIB \n");

        Assert.Equal("TESTLIB", ((AsciiData)record.Data!).Value);
    }

    ///<summary>
    ///The writer puts one space after the colon and one at the end of the line. Only those two come off,
    ///so a string that starts or ends with a space keeps it.
    ///</summary>
    [Fact]
    public void An_ascii_value_keeps_its_own_surrounding_spaces()
    {
        var record = Single("LIBNAME:  padded  \n");

        Assert.Equal(" padded ", ((AsciiData)record.Data!).Value);
    }

    [Fact]
    public void A_bit_array_reads_back_from_hex()
    {
        var record = Single("STRANS: 0x8000 \n");

        Assert.Equal(new byte[] { 0x80, 0x00 }, ((BitArrayData)record.Data!).Value);
        Assert.True(Strans.From(record.Data).ReflectAboutX);
    }

    [Fact]
    public void A_record_with_nothing_after_the_colon_carries_no_payload()
    {
        var record = Single("ENDLIB:  \n");

        Assert.Equal(RecordType.ENDLIB, record.Type);
        Assert.Null(record.Data);
    }

    #endregion ************************************************************************



    #region Tolerance ******************************************************************

    ///<summary>Monaco writes CRLF on Windows, so the buffer coming back is not what AsText wrote.</summary>
    [Fact]
    public void Windows_line_endings_are_accepted()
    {
        var records = TextFormat.ParseRecords("HEADER: 600 \r\nENDLIB:  \r\n");

        Assert.Equal(2, records.Count);
        Assert.Equal((short)600, ((Int2Data)records[0].Data!).Value);
        Assert.Equal(RecordType.ENDLIB, records[1].Type);
    }

    [Fact]
    public void Blank_lines_are_skipped()
    {
        var records = TextFormat.ParseRecords("HEADER: 600 \n\n   \nENDLIB:  \n");

        Assert.Equal(2, records.Count);
    }

    ///<summary>Typed by hand, without the spaces the writer adds.</summary>
    [Fact]
    public void The_separator_spaces_are_optional()
    {
        var records = TextFormat.ParseRecords("HEADER:600\nENDLIB:\n");

        Assert.Equal(2, records.Count);
        Assert.Equal((short)600, ((Int2Data)records[0].Data!).Value);
        Assert.Null(records[1].Data);
    }

    [Fact]
    public void A_record_type_is_matched_regardless_of_case()
    {
        Assert.Equal(RecordType.HEADER, Single("header: 600 \n").Type);
    }

    #endregion ************************************************************************



    #region Malformed text *************************************************************

    [Fact]
    public void A_line_with_no_colon_is_rejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("HEADER 600\n"));

        Assert.Contains("Line 1", error.Message);
        Assert.Contains("no colon", error.Message);
    }

    [Fact]
    public void An_unknown_record_type_is_rejected_and_names_the_line()
    {
        var error = Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("HEADER: 600 \nWIBBLE: 1 \n"));

        Assert.Contains("Line 2", error.Message);
        Assert.Contains("WIBBLE", error.Message);
    }

    ///<summary>TryParse alone would accept a bare number as an enum value that does not exist.</summary>
    [Fact]
    public void A_number_is_not_accepted_as_a_record_type()
    {
        Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("5: 600 \n"));
    }

    [Fact]
    public void A_value_that_is_not_a_number_is_rejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("XY: 0 fish 10 \n"));

        Assert.Contains("fish", error.Message);
    }

    ///<summary>Out of range is rejected rather than wrapping round to a different layer.</summary>
    [Fact]
    public void An_int2_value_too_large_for_a_short_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("LAYER: 40000 \n"));
    }

    [Fact]
    public void An_odd_number_of_hex_digits_is_rejected()
    {
        var error = Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("STRANS: 0x800 \n"));

        Assert.Contains("even number of hex digits", error.Message);
    }

    [Fact]
    public void A_bad_hex_digit_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("STRANS: 0x80ZZ \n"));
    }

    [Fact]
    public void Text_with_no_records_at_all_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => TextFormat.ParseRecords("\n   \n\n"));
    }

    #endregion ************************************************************************



    #region Culture independence *******************************************************

    ///<summary>
    ///The other half of the invariant formatting. Read with the current culture, "0.001" in a
    ///comma-decimal one treats the point as a group separator and yields 1 - so saving an untouched file
    ///would silently change its units by a factor of a thousand.
    ///</summary>
    [Fact]
    public void A_real_is_read_with_a_decimal_point_whatever_the_culture()
    {
        var record = GdsTestData.UnderHostileCulture(() => Single("UNITS: 0.001 1E-09  \n"));

        var values = ((Real8Data)record.Data!).Values;

        Assert.Equal(0.001, values[0], 1e-15);
        Assert.Equal(1e-9, values[1], 1e-20);
    }

    [Fact]
    public void A_negative_coordinate_is_read_whatever_the_culture()
    {
        var record = GdsTestData.UnderHostileCulture(() => Single("XY: -600 600 \n"));

        Assert.Equal(new[] { -600, 600 }, ((Int4Data)record.Data!).Values);
    }

    #endregion ************************************************************************



    #region Round trip *****************************************************************

    [Fact]
    public void Deserializing_from_text_replaces_the_records_and_the_layer_information()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 1));

        string edited = gds.AsText().Replace("LAYER: 1 ", "LAYER: 99 ");

        gds.Deserialize(edited);

        Assert.True(GdsTestData.HasLayerNumber(gds, 99));
        Assert.False(GdsTestData.HasLayerNumber(gds, 1));
    }

    ///<summary>A save that will not parse has to leave the loaded file alone, not half replaced.</summary>
    [Fact]
    public void A_text_that_does_not_parse_leaves_the_file_untouched()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 7));

        var recordsBefore = gds.Records;
        string textBefore = gds.AsText();

        Assert.Throws<InvalidDataException>(() => gds.Deserialize("HEADER: 600 \nWIBBLE: 1 \n"));

        Assert.Same(recordsBefore, gds.Records);
        Assert.Equal(textBefore, gds.AsText());
        Assert.True(GdsTestData.HasLayerNumber(gds, 7));
    }

    ///<summary>
    ///Parseable line by line but not a library - no ENDLIB - so the structural pass fails after the
    ///records were swapped in. That has to roll back too.
    ///</summary>
    [Fact]
    public void A_text_that_parses_but_is_not_a_library_leaves_the_file_untouched()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 7));

        var recordsBefore = gds.Records;
        var streamFormatBefore = gds.StreamFormat;
        string textBefore = gds.AsText();

        Assert.ThrowsAny<Exception>(() => gds.Deserialize("HEADER: 600 \n"));

        Assert.Same(recordsBefore, gds.Records);
        Assert.Same(streamFormatBefore, gds.StreamFormat);
        Assert.Equal(textBefore, gds.AsText());
    }

    [Fact]
    public void A_minimal_library_survives_the_text_round_trip_byte_for_byte()
    {
        byte[] original = GdsTestData.MinimalLibrary(layer: 5, xy: new[] { -600, 600, 550, 600, 550, 1100, -600, 600 });

        var gds = new GDS(original);

        gds.Deserialize(gds.AsText());

        Assert.Equal(original, gds.Serialize());
    }

    ///<summary>
    ///Every bundled file, dumped to text, read back, and written out again. Byte for byte against the file
    ///on disk, so the text format is proven to lose nothing about a real library - REAL8 values included,
    ///which is the part that could plausibly drift, since they go out as decimal and come back through the
    ///encoder.
    ///</summary>
    [Fact]
    public void Every_sample_file_survives_the_text_round_trip_byte_for_byte()
    {
        var failures = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            byte[] original = File.ReadAllBytes(path);
            var gds = new GDS(original);

            gds.Deserialize(gds.AsText());

            if (!gds.Serialize().SequenceEqual(original))
                failures.Add(Path.GetFileName(path));
        }

        Assert.Equal(Array.Empty<string>(), failures.ToArray());
    }

    ///<summary>
    ///A structurally broken edit is named, not merely refused. These used to assert the opposite - a
    ///missing record reported as the library ending early, and reordering accepted outright - which is what
    ///the element models validating their record types replaced.
    ///</summary>
    ///<summary>
    ///The record number is where the mismatch is, which is the line of the dump the editor's cursor needs
    ///to go to: delete the LAYER on line 8 and record 8 is the DATATYPE that slid into its place.
    ///</summary>
    [Theory]
    [InlineData("LAYER: 5 \n", "LAYER", "Record 8 is DATATYPE")]
    [InlineData("DATATYPE: 0 \n", "DATATYPE", "Record 9 is XY")]
    public void Deleting_a_record_an_element_needs_names_the_record_and_where(string line, string expected, string reported)
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 5));

        var error = Assert.Throws<InvalidDataException>(() => gds.Deserialize(gds.AsText().Replace(line, "")));

        Assert.Contains($"{reported} where {expected} was expected", error.Message);
    }

    [Fact]
    public void Records_in_a_nonsensical_order_are_refused()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(xy: new[] { 0, 0, 10, 0, 10, 10, 0, 0 }));

        string swapped = gds.AsText().Replace("XY: 0 0 10 0 10 10 0 0  \nENDEL:  ", "ENDEL:  \nXY: 0 0 10 0 10 10  ");

        var error = Assert.Throws<InvalidDataException>(() => gds.Deserialize(swapped));

        Assert.Contains("is ENDEL where XY was expected", error.Message);
        Assert.Contains("out of order", error.Message);
    }

    ///<summary>
    ///An odd number of coordinates is a valid INT4 array, so nothing about the payload rejects it - it is
    ///caught because an XY is a list of pairs, and one coordinate has no partner.
    ///</summary>
    [Fact]
    public void An_xy_with_an_odd_number_of_coordinates_is_refused()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(xy: new[] { 0, 0, 10, 0, 10, 10, 0, 0 }));

        var error = Assert.Throws<InvalidDataException>(() => gds.Deserialize(gds.AsText().Replace("XY: 0 0 10 0 10 10 0 0  ", "XY: 0 0 10  ")));

        Assert.Contains("3 coordinates", error.Message);
        Assert.Contains("unpaired", error.Message);
    }

    ///<summary>
    ///An even count is necessary but no longer sufficient: the pairs also have to make the shape the
    ///element is. This used to accept a single point for a boundary, which was only ever accepted because
    ///nothing checked what a boundary is.
    ///</summary>
    [Fact]
    public void An_xy_that_matches_the_elements_shape_is_accepted()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(xy: new[] { 0, 0, 10, 0, 10, 10, 0, 0 }));

        gds.Deserialize(gds.AsText().Replace("XY: 0 0 10 0 10 10 0 0  ", "XY: 0 0 20 0 20 20 0 0  "));

        var element = Assert.Single(GdsFlattener.Flatten(gds).Elements);

        Assert.Equal(4, element.Points.Count);
    }

    [Fact]
    public void An_xy_with_too_few_pairs_for_its_element_is_refused()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(xy: new[] { 0, 0, 10, 0, 10, 10, 0, 0 }));

        //An even count, and a valid INT4 payload - just not a boundary.
        var error = Assert.Throws<InvalidDataException>(() =>
            gds.Deserialize(gds.AsText().Replace("XY: 0 0 10 0 10 10 0 0  ", "XY: 0 0  ")));

        Assert.Contains("BOUNDARY", error.Message);
        Assert.Contains("at least 4 coordinate pairs", error.Message);
    }

    [Fact]
    public void A_boundary_that_does_not_close_is_refused()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(xy: new[] { 0, 0, 10, 0, 10, 10, 0, 0 }));

        //Four pairs, but the last does not return to the first.
        var error = Assert.Throws<InvalidDataException>(() =>
            gds.Deserialize(gds.AsText().Replace("XY: 0 0 10 0 10 10 0 0  ", "XY: 0 0 10 0 10 10 0 10  ")));

        Assert.Contains("close on the point it starts from", error.Message);
    }

    ///<summary>And it still holds when the machine's culture is hostile, since the dump is the go-between.</summary>
    [Fact]
    public void A_real_file_survives_the_text_round_trip_under_a_hostile_culture()
    {
        byte[] original = GdsTestData.ReadSample(GdsTestData.MosfetSample);

        byte[] written = GdsTestData.UnderHostileCulture(() =>
        {
            var gds = new GDS(original);

            gds.Deserialize(gds.AsText());

            return gds.Serialize();
        });

        Assert.Equal(original, written);
    }

    #endregion ************************************************************************
}
