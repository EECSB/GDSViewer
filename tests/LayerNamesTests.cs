using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers LayerNames, which reads a layer/datatype to name mapping out of text the user supplies.
///
///Pure string and dictionary work, so it is tested directly rather than through the browser - the same
///reason SvgWriter was pulled out of its component. The format is a Cadence-style layermap with commas,
///so most of what is checked here is tolerance: a file exported from a PDK, a spreadsheet or a text editor
///all have to work without editing.
///</summary>
public class LayerNamesTests
{
    private static GDS LibraryWithPair(short layer, short dataType)
    {
        return LibraryWithPairs(new LayerKey(layer, dataType));
    }

    ///<summary>
    ///A library holding one square on each pair given, so the palette divides by that many layers - which is
    ///what makes a file's own shades depend on how many layers it has.
    ///</summary>
    private static GDS LibraryWithPairs(params LayerKey[] pairs)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL"))
        };

        foreach (var pair in pairs)
        {
            records.Add(GdsTestData.Record(RecordType.BOUNDARY));
            records.Add(GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(pair.Number)));
            records.Add(GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(pair.DataType)));
            records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())));
            records.Add(GdsTestData.Record(RecordType.ENDEL));
        }

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return new GDS(GdsTestData.Concat(records.ToArray()));
    }

    #region Reading ********************************************************************

    [Fact]
    public void A_row_maps_a_pair_to_a_name()
    {
        var names = LayerNames.Parse("65,20,diff.drawing");

        Assert.Empty(names.Problems);
        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
    }

    [Fact]
    public void Several_rows_are_all_read()
    {
        var names = LayerNames.Parse("64,20,nwell.drawing\n65,20,diff.drawing\n67,20,li1.drawing");

        Assert.Equal(3, names.Count);
        Assert.Equal("li1.drawing", names.Names[new LayerKey(67, 20)]);
    }

    ///<summary>The two purposes of one layer are separate rows, which is the whole reason for the pair.</summary>
    [Fact]
    public void Two_data_types_of_one_layer_get_their_own_names()
    {
        var names = LayerNames.Parse("65,20,diff.drawing\n65,16,diff.pin");

        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
        Assert.Equal("diff.pin", names.Names[new LayerKey(65, 16)]);
    }

    [Theory]
    [InlineData("65,20,diff.drawing\r\n67,20,li1.drawing")]
    [InlineData("65,20,diff.drawing\r67,20,li1.drawing")]
    [InlineData("65,20,diff.drawing\n67,20,li1.drawing\n")]
    public void Any_line_ending_reads(string text)
    {
        Assert.Equal(2, LayerNames.Parse(text).Count);
    }

    [Fact]
    public void Whitespace_around_a_field_is_trimmed()
    {
        var names = LayerNames.Parse("  65 , 20 ,  diff.drawing  ");

        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
    }

    [Fact]
    public void Blank_lines_and_comments_are_skipped()
    {
        var names = LayerNames.Parse("#sky130\n\n65,20,diff.drawing\n\n   \n#end");

        Assert.Equal(1, names.Count);
        Assert.Empty(names.Problems);
    }

    ///<summary>
    ///A spreadsheet exports its column titles, so the one row whose numbers are words is skipped rather
    ///than reported. Recognized and not required: a file without one has to work too, which the rows above
    ///already show.
    ///</summary>
    [Fact]
    public void A_header_row_is_skipped_rather_than_reported()
    {
        var names = LayerNames.Parse("layer,datatype,name\n65,20,diff.drawing");

        Assert.Equal(1, names.Count);
        Assert.Empty(names.Problems);
    }

    ///<summary>
    ///The check is on the *numbers* being words, not on the name saying "name" - so a layer legitimately
    ///called that is still read.
    ///</summary>
    [Fact]
    public void A_data_row_whose_name_is_the_word_name_is_still_read()
    {
        var names = LayerNames.Parse("65,20,name");

        Assert.Equal("name", names.Names[new LayerKey(65, 20)]);
    }

    [Fact]
    public void A_fourth_field_sets_the_color()
    {
        var names = LayerNames.Parse("65,20,diff.drawing,#00ff00");

        Assert.Equal("#00ff00", names.Colors[new LayerKey(65, 20)]);
    }

    [Fact]
    public void A_row_without_a_color_sets_none()
    {
        var names = LayerNames.Parse("65,20,diff.drawing\n67,20,li1.drawing,#ff0000");

        Assert.False(names.Colors.ContainsKey(new LayerKey(65, 20)));
        Assert.True(names.Colors.ContainsKey(new LayerKey(67, 20)));
    }

    ///<summary>Appending a correction works the way editing the line would.</summary>
    [Fact]
    public void A_repeated_pair_takes_the_last_name()
    {
        var names = LayerNames.Parse("65,20,first\n65,20,second");

        Assert.Equal(1, names.Count);
        Assert.Equal("second", names.Names[new LayerKey(65, 20)]);
    }

    [Fact]
    public void Empty_text_reads_as_an_empty_mapping()
    {
        Assert.Equal(0, LayerNames.Parse("").Count);
        Assert.Equal(0, LayerNames.Parse("   \n  ").Count);
    }

    #endregion ***********************************************************************



    #region Rows that cannot be read **************************************************

    ///<summary>
    ///Not all-or-nothing, unlike saving an edited GDS file: this only labels what is already drawn, so a
    ///file with one bad row is still worth its good rows. Refusing the lot would be the worse failure.
    ///</summary>
    [Fact]
    public void A_bad_row_is_reported_and_the_rest_are_kept()
    {
        var names = LayerNames.Parse("65,20,diff.drawing\nnonsense\n67,20,li1.drawing");

        Assert.Equal(2, names.Count);
        Assert.Single(names.Problems);
        Assert.Contains("Line 2", names.Problems[0]);
    }

    [Fact]
    public void A_row_with_too_few_fields_names_the_line()
    {
        var names = LayerNames.Parse("65,20");

        Assert.Single(names.Problems);
        Assert.Contains("Line 1", names.Problems[0]);
        Assert.Contains("at least 3", names.Problems[0]);
    }

    [Theory]
    [InlineData("sixty-five,20,diff", "not a layer number")]
    [InlineData("65,twenty,diff", "not a data type")]
    public void A_field_that_is_not_a_number_says_which_one(string row, string expected)
    {
        var names = LayerNames.Parse(row);

        Assert.Empty(names.Names);
        Assert.Contains(expected, names.Problems[0]);
    }

    [Fact]
    public void A_row_with_no_name_is_reported()
    {
        var names = LayerNames.Parse("65,20,");

        Assert.Empty(names.Names);
        Assert.Contains("names no layer", names.Problems[0]);
    }

    ///<summary>
    ///A file with the wrong delimiter fails on every line, and a thousand identical complaints tell the
    ///reader nothing the first few do not.
    ///</summary>
    [Fact]
    public void Problems_stop_being_listed_after_the_first_few()
    {
        string tabSeparated = string.Join("\n", Enumerable.Range(1, 50).Select(n => $"{n}\t20\tlayer{n}"));

        var names = LayerNames.Parse(tabSeparated);

        Assert.Empty(names.Names);
        Assert.Equal(5, names.Problems.Count);
    }

    ///<summary>
    ///A negative number parses, since the format's own "unknown" data type is negative - so a mapping can
    ///name the layers of elements whose type record was missing.
    ///</summary>
    [Fact]
    public void A_negative_data_type_reads()
    {
        var names = LayerNames.Parse("65,-1,diff.unknown");

        Assert.Equal("diff.unknown", names.Names[new LayerKey(65, LayerKey.UnknownDataType)]);
    }

    #endregion ***********************************************************************



    #region Applying to a file ********************************************************

    [Fact]
    public void Applying_names_the_layers_the_file_has()
    {
        var gds = LibraryWithPair(65, 20);
        var names = LayerNames.Parse("65,20,diff.drawing");

        int applied = names.ApplyTo(gds.AdditionalInformation.Layers);

        Assert.Equal(1, applied);
        Assert.Equal("diff.drawing", gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Name);
        Assert.Equal("diff.drawing (65/20)", gds.AdditionalInformation.Layers[new LayerKey(65, 20)].DisplayName);
    }

    ///<summary>
    ///A mapping covers a whole PDK where a file uses a handful of its layers, so rows matching nothing is
    ///the normal case. What the count is for is the *zero* case, which means the mapping is for another
    ///technology or its columns are the wrong way round.
    ///</summary>
    [Fact]
    public void Rows_that_match_no_layer_in_the_file_are_ignored()
    {
        var gds = LibraryWithPair(65, 20);
        var names = LayerNames.Parse("65,20,diff.drawing\n99,44,nothing.here\n12,0,also.absent");

        Assert.Equal(1, names.ApplyTo(gds.AdditionalInformation.Layers));
    }

    [Fact]
    public void A_mapping_for_another_technology_applies_nothing()
    {
        var gds = LibraryWithPair(65, 20);
        var names = LayerNames.Parse("1,0,metal1\n2,0,metal2");

        Assert.Equal(0, names.ApplyTo(gds.AdditionalInformation.Layers));
        Assert.Null(gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Name);
    }

    ///<summary>
    ///The data type has to match, not just the number: a row for 65/16 must not name 65/20. This is what
    ///would have been impossible to express before layers were keyed by the pair.
    ///</summary>
    [Fact]
    public void A_row_for_another_data_type_does_not_name_this_one()
    {
        var gds = LibraryWithPair(65, 20);
        var names = LayerNames.Parse("65,16,diff.pin");

        Assert.Equal(0, names.ApplyTo(gds.AdditionalInformation.Layers));
    }

    [Fact]
    public void A_row_with_a_color_recolors_the_layer()
    {
        var gds = LibraryWithPair(65, 20);
        string before = gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Color;

        LayerNames.Parse("65,20,diff.drawing,#00ff00").ApplyTo(gds.AdditionalInformation.Layers);

        Assert.NotEqual(before, gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Color);
        Assert.Equal("#00ff00", gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Color);
    }

    [Fact]
    public void Clearing_drops_the_names_and_puts_the_palette_back()
    {
        var gds = LibraryWithPair(65, 20);
        var key = new LayerKey(65, 20);
        string palette = gds.AdditionalInformation.Layers[key].Color;

        LayerNames.Parse("65,20,diff.drawing,#00ff00").ApplyTo(gds.AdditionalInformation.Layers);
        LayerNames.Clear(gds.AdditionalInformation);

        Assert.Null(gds.AdditionalInformation.Layers[key].Name);
        Assert.Equal(palette, gds.AdditionalInformation.Layers[key].Color);
        Assert.Equal("65/20", gds.AdditionalInformation.Layers[key].DisplayName);
    }

    #endregion ***********************************************************************



    #region The template *************************************************************

    ///<summary>
    ///Writing the open file's own layers out is what makes starting a mapping bearable: the pairs are
    ///already listed, so only the names have to be filled in.
    ///</summary>
    [Fact]
    public void The_template_lists_the_pairs_of_the_open_file()
    {
        var gds = LibraryWithPair(65, 20);

        string template = LayerNames.Export(gds.AdditionalInformation);

        Assert.StartsWith("#layer,datatype,name,color", template);
        Assert.Contains("65,20,,#", template);
    }

    ///<summary>
    ///What a session stores: the layers something was said about, and nothing else. Storing the template's
    ///shape instead would write a row per untouched layer, and reading it back would report each as a row
    ///naming nothing.
    ///</summary>
    [Fact]
    public void Named_writes_only_the_layers_something_was_said_about()
    {
        var gds = LibraryWithPair(65, 20);

        Assert.Equal("", LayerNames.Named(gds.AdditionalInformation));

        LayerNames.Parse("65,20,diff.drawing").ApplyTo(gds.AdditionalInformation.Layers);

        Assert.StartsWith("65,20,diff.drawing", LayerNames.Named(gds.AdditionalInformation));
    }

    ///
    ///**A name on its own writes no color**, because the color it has is the palette's.
    ///
    ///This asserted the opposite - `65,20,diff.drawing,#` - and the `#` was the palette's shade for the second
    ///of two layers in a two-layer file. Read back, a color in that column means somebody chose it, which is
    ///the one thing `Layer.ColorIsCustom` exists to record: the palette is derived from how many layers a file
    ///has, so storing one of its colors stores something already known and then fights the palette if the next
    ///file has a different count. A mapping is kept per *technology*, so the next file is exactly the case.
    ///
    ///Nothing is lost by leaving it out. The same file reopened divides the same palette the same way and
    ///arrives at the same shades; what is stored is only what would otherwise be guessed at.
    ///
    [Fact]
    public void A_name_on_its_own_does_not_write_the_palette_color()
    {
        var gds = LibraryWithPair(65, 20);

        LayerNames.Parse("65,20,diff.drawing").ApplyTo(gds.AdditionalInformation.Layers);

        string stored = LayerNames.Named(gds.AdditionalInformation).TrimEnd('\n');

        Assert.Equal("65,20,diff.drawing", stored);

        //And read back, the layer is not marked as one somebody recolored.
        var fresh = LibraryWithPair(65, 20);
        LayerNames.Parse(stored).ApplyTo(fresh.AdditionalInformation.Layers);

        Assert.False(fresh.AdditionalInformation.Layers[new LayerKey(65, 20)].ColorIsCustom);
    }

    ///<summary>
    ///A color that *was* chosen goes with the name. Without the fourth field a loaded mapping's colors came
    ///back as the palette on the next visit, so the layout was named right and colored wrong.
    ///</summary>
    [Fact]
    public void A_color_survives_being_written_and_read_again()
    {
        var gds = LibraryWithPair(65, 20);

        LayerNames.Parse("65,20,diff.drawing,#00ff00").ApplyTo(gds.AdditionalInformation.Layers);

        string stored = LayerNames.Named(gds.AdditionalInformation);

        var fresh = LibraryWithPair(65, 20);
        LayerNames.Parse(stored).ApplyTo(fresh.AdditionalInformation.Layers);

        Assert.Equal("diff.drawing", fresh.AdditionalInformation.Layers[new LayerKey(65, 20)].Name);
        Assert.Equal("#00ff00", fresh.AdditionalInformation.Layers[new LayerKey(65, 20)].Color);
        Assert.True(fresh.AdditionalInformation.Layers[new LayerKey(65, 20)].ColorIsCustom);
    }

    ///
    ///And the case the whole distinction is for: a palette color must not follow a mapping onto another file.
    ///
    ///Two layers divide the gradient differently than three do, so the shade 66/20 gets as the second of two is
    ///not the shade it gets as the second of three. Storing the first as if it had been chosen carried it
    ///across, and a layer arriving at another file's shade is the visible half of the wrong this fixes.
    ///
    ///**The second layer rather than the first, and the guard below is why.** Every palette starts at the same
    ///place, so 65/20 as the first of one file and the first of another is the same color whatever the count -
    ///the first version of this test picked that pair and its premise was simply false. The assertion that the
    ///two files disagree is what said so, rather than a green test over a case that was not the case.
    ///
    [Fact]
    public void A_palette_color_does_not_follow_a_mapping_onto_another_file()
    {
        var named = LibraryWithPairs(new LayerKey(65, 20), new LayerKey(66, 20)).AdditionalInformation;

        LayerNames.Parse("66,20,poly").ApplyTo(named.Layers);

        //A file with one more layer, so the gradient divides differently.
        var other = LibraryWithPairs(new LayerKey(65, 20), new LayerKey(66, 20), new LayerKey(67, 20))
            .AdditionalInformation;

        string ownShade = other.Layers[new LayerKey(66, 20)].Color;

        //Confirms the fixture is the case it claims to be.
        Assert.NotEqual(named.Layers[new LayerKey(66, 20)].Color, ownShade);

        LayerNames.Parse(LayerNames.Named(named)).ApplyTo(other.Layers);

        //The name crosses over, which is what a mapping is for.
        Assert.Equal("poly", other.Layers[new LayerKey(66, 20)].Name);

        //The color does not, which is what this file's own palette already answered.
        Assert.Equal(ownShade, other.Layers[new LayerKey(66, 20)].Color);
        Assert.False(other.Layers[new LayerKey(66, 20)].ColorIsCustom);
    }

    ///<summary>And it round-trips: what Template writes, Parse reads back.</summary>
    [Fact]
    public void A_filled_in_template_reads_back()
    {
        var gds = LibraryWithPair(65, 20);

        LayerNames.Parse("65,20,diff.drawing").ApplyTo(gds.AdditionalInformation.Layers);

        var reread = LayerNames.Parse(LayerNames.Export(gds.AdditionalInformation));

        Assert.Equal("diff.drawing", reread.Names[new LayerKey(65, 20)]);
        Assert.Empty(reread.Problems);
    }

    ///<summary>
    ///Written invariantly, like the record dump: on a comma-decimal locale a culture-sensitive number would
    ///be the wrong thing to have asked for in a data file, and here it would also collide with the
    ///delimiter.
    ///</summary>
    [Fact]
    public void The_template_is_written_the_same_in_any_culture()
    {
        var gds = LibraryWithPair(65, 20);

        string invariant = LayerNames.Export(gds.AdditionalInformation);
        string hostile = GdsTestData.UnderHostileCulture(() => LayerNames.Export(gds.AdditionalInformation));

        Assert.Equal(invariant, hostile);
    }

    [Fact]
    public void A_mapping_reads_the_same_in_any_culture()
    {
        var invariant = LayerNames.Parse("65,20,diff.drawing");
        var hostile = GdsTestData.UnderHostileCulture(() => LayerNames.Parse("65,20,diff.drawing"));

        Assert.Equal(invariant.Names, hostile.Names);
        Assert.Empty(hostile.Problems);
    }

    #endregion ***********************************************************************



    #region How the pattern is drawn *************************************************

    ///<summary>The two columns past the fill: what its marks are colored, and how big a repeat is.</summary>
    [Fact]
    public void A_row_carries_the_pattern_color_and_size()
    {
        var gds = LibraryWithPair(65, 20);

        int applied = LayerNames
            .Parse("65,20,met1,#ff0000,0,50,conductor,dots,#0000ff,14")
            .ApplyTo(gds.AdditionalInformation.Layers);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        Assert.Equal(1, applied);
        Assert.Equal(LayerFill.Dots, layer.Fill);
        Assert.Equal("#0000ff", layer.PatternColor);
        Assert.Equal(14, layer.PatternPixels);
    }

    ///<summary>Either one alone is enough, and the one that is missing stays unset rather than guessed.</summary>
    [Fact]
    public void Each_of_the_two_can_be_given_without_the_other()
    {
        var gds = LibraryWithPair(65, 20);

        LayerNames.Parse("65,20,met1,#ff0000,0,50,none,grid,,22").ApplyTo(gds.AdditionalInformation.Layers);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        Assert.Null(layer.PatternColor);
        Assert.Equal(22, layer.PatternPixels);

        var second = LibraryWithPair(65, 20);

        LayerNames.Parse("65,20,met1,#ff0000,0,50,none,grid,#0000ff").ApplyTo(second.AdditionalInformation.Layers);

        Assert.Equal("#0000ff", second.AdditionalInformation.Layers[new LayerKey(65, 20)].PatternColor);
        Assert.Null(second.AdditionalInformation.Layers[new LayerKey(65, 20)].PatternPixels);
    }

    ///
    ///A size outside the range the popup offers is reported rather than taken.
    ///
    ///Below it every fill is a flat tone and above it a shape holds less than one repeat - both of which
    ///look exactly like the column doing nothing, which is the state a mapping should never leave somebody
    ///guessing about.
    ///
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("500")]
    [InlineData("wide")]
    public void An_impossible_pattern_size_is_reported(string size)
    {
        var read = LayerNames.Parse($"65,20,met1,#ff0000,0,50,none,grid,,{size}");

        Assert.Empty(read.PatternSizes);
        Assert.Single(read.Problems);
        Assert.Contains("pattern size", read.Problems[0]);

        //And the rest of the row still lands: one bad column is not a reason to lose the fill.
        Assert.Equal(LayerFill.Grid, read.Fills[new LayerKey(65, 20)]);
    }

    ///
    ///Both survive an export and a re-read, columns and all.
    ///
    ///The columns are positional, so this is really a test of the cascade in front of them: a layer whose
    ///only setting is a pattern size still has to write the role and the fill columns to put the size in
    ///the tenth place, or reading it back lands the number somewhere else entirely.
    ///
    [Fact]
    public void The_pattern_color_and_size_round_trip_through_an_export()
    {
        var gds = LibraryWithPair(65, 20);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        layer.Fill = LayerFill.CrossHatch;
        layer.PatternColor = "#0000ff";
        layer.PatternPixels = 14;

        var reread = LayerNames.Parse(LayerNames.Export(gds.AdditionalInformation));

        Assert.Empty(reread.Problems);
        Assert.Equal(LayerFill.CrossHatch, reread.Fills[new LayerKey(65, 20)]);
        Assert.Equal("#0000ff", reread.PatternColors[new LayerKey(65, 20)]);
        Assert.Equal(14, reread.PatternSizes[new LayerKey(65, 20)]);
    }

    ///<summary>And a size with no color in front of it keeps its place, which is the gap the cascade fills.</summary>
    [Fact]
    public void A_size_with_no_pattern_color_still_lands_in_its_own_column()
    {
        var gds = LibraryWithPair(65, 20);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        layer.Fill = LayerFill.Dashes;
        layer.PatternPixels = 30;

        var reread = LayerNames.Parse(LayerNames.Export(gds.AdditionalInformation));

        Assert.Empty(reread.Problems);
        Assert.Empty(reread.PatternColors);
        Assert.Equal(30, reread.PatternSizes[new LayerKey(65, 20)]);
    }

    ///<summary>Clearing the names takes them with it, the way it takes the fill they belong to.</summary>
    [Fact]
    public void Clearing_the_names_clears_how_the_pattern_was_drawn()
    {
        var gds = LibraryWithPair(65, 20);

        LayerNames
            .Parse("65,20,met1,#ff0000,0,50,conductor,dots,#0000ff,14")
            .ApplyTo(gds.AdditionalInformation.Layers);

        LayerNames.Clear(gds.AdditionalInformation);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        Assert.Equal(LayerFill.None, layer.Fill);
        Assert.Null(layer.PatternColor);
        Assert.Null(layer.PatternPixels);
    }

    #endregion ***********************************************************************
}
