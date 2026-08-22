using GdsII;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///Covers GDS.AsText, the record dump shown in the text editor view, plus the two round-trip entry
///points that are still stubs.
///</summary>
public class TextRepresentationTests
{
    private static string[] TextLines(GDS gds)
    {
        return gds.AsText().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    #region AsText ********************************************************************

    [Fact]
    public void Every_record_becomes_exactly_one_line()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Equal(gds.Records.Count, TextLines(gds).Length);
    }

    [Fact]
    public void Each_line_is_prefixed_with_its_record_type_name()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        string[] lines = TextLines(gds);

        for (int i = 0; i < lines.Length; i++)
            Assert.StartsWith(gds.Records[i].Type.ToString() + ": ", lines[i]);
    }

    [Fact]
    public void A_scalar_value_is_rendered_after_the_record_name()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Equal("HEADER: 600 ", TextLines(gds)[0]);
    }

    [Fact]
    public void An_ascii_value_is_rendered_verbatim()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Contains("LIBNAME: TESTLIB ", TextLines(gds));
        Assert.Contains("STRNAME: TESTCELL ", TextLines(gds));
    }

    ///<summary>
    ///Array payloads emit a trailing space per element, which lands next to the separator space the
    ///line format adds - hence the doubled space at the end. Pinned because the text editor's
    ///not-yet-written parser has to read this back.
    ///</summary>
    [Fact]
    public void An_array_value_is_space_separated_and_ends_with_a_doubled_space()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 5, xy: new[] { 0, 0, 100, 200, 50, 300, 0, 0 }));

        Assert.Contains("XY: 0 0 100 200 50 300 0 0  ", TextLines(gds));
        Assert.Contains("BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21  ", TextLines(gds));
    }

    [Fact]
    public void A_record_with_no_data_renders_an_empty_value()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Contains("ENDEL:  ", TextLines(gds));
        Assert.Contains("ENDLIB:  ", TextLines(gds));
    }

    [Fact]
    public void The_dump_is_newline_terminated()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.EndsWith("\n", gds.AsText());
    }

    [Fact]
    public void A_real_sample_file_dumps_one_line_per_record()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        Assert.Equal(gds.Records.Count, TextLines(gds).Length);
        Assert.StartsWith("HEADER: ", TextLines(gds)[0]);
    }

    #endregion ***********************************************************************



    #region Round trip *****************************************************************

    //Serialize() is covered in SerializeTests, and Deserialize(string) - the text editor's save path - in
    //TextFormatTests. What is left here is that AsText and that parser are actually inverses.

    ///<summary>
    ///The dump is the only description of what the text parser has to accept, so the two are pinned
    ///together: whatever AsText writes, ParseRecords reads back into the same records.
    ///</summary>
    [Fact]
    public void What_AsText_writes_is_what_the_text_parser_reads()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 5, xy: new[] { -600, 600, 550, 600, 550, 1100, -600, 600 }));

        var reparsed = TextFormat.ParseRecords(gds.AsText());

        Assert.Equal(gds.Records.Count, reparsed.Count);

        for (int i = 0; i < reparsed.Count; i++)
        {
            Assert.Equal(gds.Records[i].Type, reparsed[i].Type);
            Assert.Equal(gds.Records[i].DataType, reparsed[i].DataType);
            Assert.Equal(gds.Records[i].Serialize(), reparsed[i].Serialize());
        }
    }

    ///<summary>The byte overload does work, and re-reading a stream rebuilds everything.</summary>
    [Fact]
    public void Deserializing_from_bytes_replaces_the_records_and_the_layer_information()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(layer: 1));

        Assert.True(GdsTestData.HasLayerNumber(gds, 1));

        gds.Deserialize(GdsTestData.MinimalLibrary(layer: 99));

        Assert.True(GdsTestData.HasLayerNumber(gds, 99));
        Assert.False(GdsTestData.HasLayerNumber(gds, 1));
        Assert.Equal("TESTLIB", ((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value);
    }

    #endregion ***********************************************************************



    #region Culture independence ******************************************************

    ///<summary>
    ///The reason this matters: Blazor WebAssembly takes its culture from the browser, so this is the
    ///default state for a large share of users rather than an edge case.
    ///</summary>
    [Fact]
    public void A_real_is_written_with_a_decimal_point_whatever_the_culture()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        string units = GdsTestData.UnderHostileCulture(() => TextLines(gds).Single(line => line.StartsWith("UNITS")));

        Assert.Contains("0.001", units);
        Assert.DoesNotContain(",", units);
    }

    [Fact]
    public void A_negative_coordinate_keeps_its_minus_sign_whatever_the_culture()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary(xy: new[] { -1000, -2000, 0, 0, 10, 10, -1000, -2000 }));

        string xy = GdsTestData.UnderHostileCulture(() => TextLines(gds).Single(line => line.StartsWith("XY")));

        Assert.Contains("-1000", xy);
        Assert.DoesNotContain("!", xy);
    }

    ///<summary>
    ///The whole dump, not just the lines picked out above - a real file exercises every payload type at
    ///once, so nothing can regress by being formatted somewhere this test did not think to look.
    ///</summary>
    [Fact]
    public void The_whole_dump_of_a_real_file_is_identical_under_a_hostile_culture()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        string invariant = gds.AsText();
        string hostile = GdsTestData.UnderHostileCulture(() => gds.AsText());

        Assert.Equal(invariant, hostile);
    }

    #endregion ***********************************************************************
}

