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

    ///
    ///The pairs are every layer a row says anything about - a name, or only a color or a height - once each and in the
    ///order pairs sort, whatever order the rows came in. Which is what a session's mapping puts back for a named layer
    ///with nothing drawn on it.
    ///
    [Fact]
    public void The_pairs_are_every_layer_a_row_says_anything_about()
    {
        var names = LayerNames.Parse("68,20,met1\n66,20,,#ff0000\n67,20,,,940,100\n66,20,poly");

        Assert.Equal(new[] { new LayerKey(66, 20), new LayerKey(67, 20), new LayerKey(68, 20) }, names.Pairs);
        Assert.Equal(3, names.Count);
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
    ///A spreadsheet's "CSV UTF-8" starts with a byte order mark, and a file read as bytes - which is how the app
    ///reads one - keeps it. It is not part of the first field: a first row that is a mapping rather than a header
    ///lost its layer number to it, since neither Trim nor short.TryParse takes it for a space.
    ///</summary>
    [Fact]
    public void A_byte_order_mark_is_not_part_of_the_first_row()
    {
        var names = LayerNames.Parse("\uFEFF65,20,diff.drawing\n66,20,poly.drawing");

        Assert.Empty(names.Problems);
        Assert.Equal(2, names.Count);
        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
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



    #region The 3D view's texture ****************************************************

    ///<summary>The two columns past the pattern's: a picture's file name, and how big a repeat of it is.</summary>
    [Fact]
    public void A_row_carries_a_texture_and_its_size()
    {
        var gds = LibraryWithPair(65, 20);

        var read = LayerNames.Parse("65,20,met1,#ff0000,0,50,conductor,dots,#0000ff,14,copper.png,500");

        int applied = read.ApplyTo(gds.AdditionalInformation.Layers);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        Assert.Empty(read.Problems);
        Assert.Equal(1, applied);
        Assert.Equal("copper.png", layer.Texture);
        Assert.Equal(500, layer.TextureSize);
    }

    ///
    ///A texture is a setting of its own, so a row that sets nothing else is still a row - and is not reported as
    ///naming no layer, which is what a row with its columns shifted looks like.
    ///
    [Fact]
    public void A_texture_alone_is_a_row_worth_keeping()
    {
        var read = LayerNames.Parse("65,20,,,,,,,,,copper.png");

        Assert.Empty(read.Problems);
        Assert.Equal(1, read.Count);
        Assert.Equal("copper.png", read.Textures[new LayerKey(65, 20)]);
    }

    ///<summary>A path below the CSV's own folder is a name like any other: that is where a zip made by hand puts them.</summary>
    [Fact]
    public void A_texture_in_a_folder_below_the_CSV_is_found_there()
    {
        var read = LayerNames.Parse("65,20,met1,,,,,,,,textures/copper.png");

        Assert.Empty(read.Problems);
        Assert.Equal("textures/copper.png", read.Textures[new LayerKey(65, 20)]);
    }

    ///
    ///A name that climbs out of the CSV's folder, starts at a root or a drive, or is not a name at all is reported
    ///rather than taken: in a zip it would be a file outside it, and on a page a fetch from wherever it points.
    ///
    [Theory]
    [InlineData("../copper.png")]
    [InlineData("textures/../../copper.png")]
    [InlineData("/copper.png")]
    [InlineData("C:copper.png")]
    [InlineData("textures\\copper.png")]
    [InlineData("textures//copper.png")]
    [InlineData("https://example.com/copper.png")]
    public void A_texture_outside_the_CSV_folder_is_reported(string texture)
    {
        var read = LayerNames.Parse($"65,20,met1,,,,,,,,{texture}");

        Assert.Empty(read.Textures);
        Assert.Single(read.Problems);
        Assert.Contains("is not a texture", read.Problems[0]);

        //And the rest of the row still lands.
        Assert.Equal("met1", read.Names[new LayerKey(65, 20)]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-40")]
    [InlineData("big")]
    public void An_impossible_texture_size_is_reported(string size)
    {
        var read = LayerNames.Parse($"65,20,met1,,,,,,,,copper.png,{size}");

        Assert.Empty(read.TextureSizes);
        Assert.Single(read.Problems);
        Assert.Contains("texture size", read.Problems[0]);
        Assert.Equal("copper.png", read.Textures[new LayerKey(65, 20)]);
    }

    ///
    ///Both survive an export and a re-read - on a layer with no pattern at all, which is the cascade's hard case: the
    ///role, the fill and both pattern columns have to be written, empty where they have nothing to say, to put the
    ///texture in the eleventh place. The pattern size in front of it was written as a number that had to be there,
    ///and a layer with none would have stopped the export.
    ///
    [Fact]
    public void A_texture_round_trips_through_an_export_on_a_layer_with_no_pattern()
    {
        var gds = LibraryWithPair(65, 20);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        layer.Texture = "copper.png";
        layer.TextureSize = 250;

        var reread = LayerNames.Parse(LayerNames.Export(gds.AdditionalInformation));

        Assert.Empty(reread.Problems);
        Assert.Empty(reread.PatternSizes);
        Assert.Empty(reread.PatternColors);

        //The fill column is written to put the texture past it - as "none", the way the role column always was, which
        //reads back as no pattern.
        Assert.Equal(LayerFill.None, reread.Fills.GetValueOrDefault(new LayerKey(65, 20), LayerFill.None));

        Assert.Equal("copper.png", reread.Textures[new LayerKey(65, 20)]);
        Assert.Equal(250, reread.TextureSizes[new LayerKey(65, 20)]);
    }

    ///<summary>A layer with nothing but a texture is still kept in a session - which is what WasSaid decides.</summary>
    [Fact]
    public void A_texture_on_a_nameless_layer_is_still_stored()
    {
        var gds = LibraryWithPair(65, 20);

        gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Texture = "copper.png";

        var stored = LayerNames.Parse(LayerNames.Named(gds.AdditionalInformation));

        Assert.Empty(stored.Problems);
        Assert.Equal("copper.png", stored.Textures[new LayerKey(65, 20)]);
    }

    ///<summary>Clearing the names takes the texture with them, the way it takes the pattern.</summary>
    [Fact]
    public void Clearing_the_names_clears_the_texture()
    {
        var gds = LibraryWithPair(65, 20);

        LayerNames.Parse("65,20,met1,,,,,,,,copper.png,500").ApplyTo(gds.AdditionalInformation.Layers);

        LayerNames.Clear(gds.AdditionalInformation);

        var layer = gds.AdditionalInformation.Layers[new LayerKey(65, 20)];

        Assert.Null(layer.Texture);
        Assert.Null(layer.TextureSize);
    }

    ///<summary>The header names the two columns last, where a reader that stops at the tenth never looks.</summary>
    [Fact]
    public void The_header_names_the_texture_columns_last()
    {
        Assert.EndsWith(",patternsize,texture,texturesize\n", LayerNames.HeaderRow);
    }

    #endregion ***********************************************************************



    #region Quoted fields ****************************************************************

    ///
    ///**A name with a comma in it comes back as it was written, and so does the rest of its row** - from the session
    ///and from Export alike. It used to go out as it was, and the comma split the row: `poly, gate` read back as
    ///`poly`, with ` gate` for its color and every column after it one place along, the role among them - so poly
    ///stopped carrying a net.
    ///
    [Fact]
    public void A_name_with_a_comma_in_it_comes_back_with_the_rest_of_its_row()
    {
        var gds = LibraryWithPair(66, 20);
        var key = new LayerKey(66, 20);
        var layer = gds.AdditionalInformation.Layers[key];

        layer.Name = "poly, gate";
        layer.Role = LayerRole.Conductor;

        foreach (string written in new[] { LayerNames.Named(gds.AdditionalInformation), LayerNames.Export(gds.AdditionalInformation) })
        {
            var reread = LayerNames.Parse(written);

            Assert.Empty(reread.Problems);
            Assert.Equal("poly, gate", reread.Names[key]);
            Assert.Equal(LayerRole.Conductor, reread.Roles[key]);
        }
    }

    ///
    ///**A field in double quotes is read the way a spreadsheet writes one**: a comma inside it is part of it, a
    ///doubled quote is one quote, every field may be quoted, and blank space around the quotes is not part of
    ///anything. A quote in the middle of a name is still the character it looks like.
    ///
    [Theory]
    [InlineData("65,20,\"poly, gate\",#ff0000", "poly, gate")]
    [InlineData("65,20,\"say \"\"hi\"\"\",#ff0000", "say \"hi\"")]
    [InlineData("\"65\",\"20\",\"diff\",\"#ff0000\"", "diff")]
    [InlineData("65,20, \"poly, gate\" ,#ff0000", "poly, gate")]
    [InlineData("65,20,6\" pad,#ff0000", "6\" pad")]
    public void A_quoted_field_is_read_the_way_a_spreadsheet_writes_one(string row, string name)
    {
        var read = LayerNames.Parse(row);

        Assert.Empty(read.Problems);
        Assert.Equal(name, read.Names[new LayerKey(65, 20)]);
        Assert.Equal("#ff0000", read.Colors[new LayerKey(65, 20)]);
    }

    ///<summary>A quote left open takes the rest of the line, rather than the row being lost over it.</summary>
    [Fact]
    public void A_quote_left_open_takes_the_rest_of_the_line()
    {
        var read = LayerNames.Parse("65,20,\"poly, gate");

        Assert.Empty(read.Problems);
        Assert.Equal("poly, gate", read.Names[new LayerKey(65, 20)]);
    }

    ///
    ///**Only what needs quotes gets them**, so a file with no comma or quote in its names is written exactly as it
    ///always was and reads the same in any tool. A quote is doubled inside them, and a line break - which a row
    ///cannot hold - is a space.
    ///
    [Theory]
    [InlineData("diff", "66,20,diff\n")]
    [InlineData("poly, gate", "66,20,\"poly, gate\"\n")]
    [InlineData("6\" pad", "66,20,\"6\"\" pad\"\n")]
    [InlineData("poly\ngate", "66,20,poly gate\n")]
    public void Only_a_name_that_needs_quotes_is_quoted(string name, string row)
    {
        var gds = LibraryWithPair(66, 20);

        gds.AdditionalInformation.Layers[new LayerKey(66, 20)].Name = name;

        Assert.Equal(row, LayerNames.Named(gds.AdditionalInformation));
    }

    ///<summary>The other text columns are quoted the same way: a picture whose name has a comma comes back, and its size after it.</summary>
    [Fact]
    public void A_picture_with_a_comma_in_its_name_comes_back_with_its_size()
    {
        var gds = LibraryWithPair(66, 20);
        var layer = gds.AdditionalInformation.Layers[new LayerKey(66, 20)];

        layer.Texture = "copper, brushed.png";
        layer.TextureSize = 250;

        var reread = LayerNames.Parse(LayerNames.Named(gds.AdditionalInformation));

        Assert.Empty(reread.Problems);
        Assert.Equal("copper, brushed.png", reread.Textures[new LayerKey(66, 20)]);
        Assert.Equal(250, reread.TextureSizes[new LayerKey(66, 20)]);
    }

    #endregion ***********************************************************************



    #region A KLayout .lyp ***************************************************************

    ///<summary>A .lyp around these nodes, written the way KLayout writes one.</summary>
    private static string Lyp(string nodes)
    {
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<layer-properties>\n" + nodes + "</layer-properties>\n";
    }

    ///<summary>One layer of a .lyp: its source, and a name, a fill color and a brightness where given.</summary>
    ///<summary>
    ///A .lyp node, its numbers written invariantly - a file says -1, never a culture's own minus: written in Swedish,
    ///Finnish or Norwegian, a negative brightness came out as U+2212 and five tests failed there (the 2026-10-21 audit).
    ///</summary>
    private static string Node(string source, string name = "", string color = "", int brightness = 0)
    {
        return FormattableString.Invariant($@" <properties>
  <frame-color>{color}</frame-color>
  <fill-color>{color}</fill-color>
  <frame-brightness>0</frame-brightness>
  <fill-brightness>{brightness}</fill-brightness>
  <dither-pattern>I1</dither-pattern>
  <valid>true</valid>
  <visible>true</visible>
  <name>{name}</name>
  <source>{source}</source>
 </properties>
");
    }

    ///
    ///**A KLayout .lyp names and colors the layers it lists**, read through the same Parse as a CSV - so the command
    ///line, gds mcp and the app's Import all take one. sky130's own names end in their pair, which every label here
    ///shows already, so it is taken off.
    ///
    [Fact]
    public void A_lyp_names_and_colors_the_layers_it_lists()
    {
        var read = LayerNames.Parse(Lyp(Node("68/20@1", "met1drawing_m 68/20", "#9999FF") + Node("66/20@1", "poly", "#d80000")));

        Assert.Empty(read.Problems);
        Assert.Equal(2, read.Count);
        Assert.Equal("met1drawing_m", read.Names[new LayerKey(68, 20)]);
        Assert.Equal("#9999ff", read.Colors[new LayerKey(68, 20)]);
        Assert.Equal("poly", read.Names[new LayerKey(66, 20)]);
        Assert.Equal("#d80000", read.Colors[new LayerKey(66, 20)]);
    }

    ///
    ///**A source is read the way KLayout reads it**, each of these asked of KLayout first: the first layout when none is
    ///given, a name before the pair, a layer alone as data type 0, and a transformation after it saying nothing about
    ///which layer it is.
    ///
    [Theory]
    [InlineData("68/20", 68, 20)]
    [InlineData("68/20@1", 68, 20)]
    [InlineData("met1 68/20@1", 68, 20)]
    [InlineData("1st 68/20", 68, 20)]
    [InlineData("68@1", 68, 0)]
    [InlineData("68", 68, 0)]
    [InlineData("68/20@1 (r90 *2 0,0)", 68, 20)]
    [InlineData("68/20@*", 68, 20)]
    public void A_source_is_read_the_way_KLayout_reads_one(string source, short layer, short dataType)
    {
        var read = LayerNames.Parse(Lyp(Node(source, "met1", "#123456")));

        Assert.Empty(read.Problems);
        Assert.Equal(new LayerKey(layer, dataType), Assert.Single(read.Names.Keys));
        Assert.Equal("#123456", read.Colors[new LayerKey(layer, dataType)]);
    }

    ///
    ///**A source that is not one layer of this layout is not an entry**: a wildcard, a layer with any data type, a
    ///layer known only by name, and one on another layout than the first. Nothing to report, either - every .lyp with
    ///a group has one of these.
    ///
    [Theory]
    [InlineData("*/*@*")]
    [InlineData("68/*@1")]
    [InlineData("*/20@1")]
    [InlineData("metal9@1")]
    [InlineData("68/20@2")]
    [InlineData("")]
    public void A_source_that_is_not_one_layer_of_this_layout_is_no_entry(string source)
    {
        var read = LayerNames.Parse(Lyp(Node(source, "met1", "#123456")));

        Assert.Empty(read.Problems);
        Assert.Equal(0, read.Count);
    }

    ///
    ///**A group's color is its members'**, and its brightness adds to theirs - the colors here are KLayout's own for
    ///this file. The outermost color on the way down wins over a member's own and an inner group's, and comes from the
    ///group whether it is written before its members or after them. A group is not drawn, so it is no entry, even
    ///where its own source names a pair.
    ///
    [Fact]
    public void A_groups_color_is_its_members_and_its_brightness_adds_to_theirs()
    {
        string groups = @" <properties>
  <fill-color>#667788</fill-color>
  <fill-brightness>64</fill-brightness>
  <source>*/*@*</source>
  <group-members><name>plain</name><source>74/0@1</source></group-members>
  <group-members><fill-color>#778899</fill-color><name>colored</name><source>75/0</source></group-members>
  <group-members>
   <source>*/*@1</source>
   <group-members><name>deeper</name><source>76/5@1</source></group-members>
  </group-members>
 </properties>
 <properties>
  <fill-brightness>64</fill-brightness>
  <source>*/*@*</source>
  <group-members><fill-color>#408060</fill-color><source>1/0@1</source></group-members>
  <group-members><fill-color>#408060</fill-color><fill-brightness>32</fill-brightness><source>2/0@1</source></group-members>
 </properties>
 <properties>
  <fill-color>#204060</fill-color>
  <source>*/*@*</source>
  <group-members>
   <fill-color>#a0b0c0</fill-color>
   <source>*/*@1</source>
   <group-members><fill-color>#ffffff</fill-color><source>3/0@1</source></group-members>
  </group-members>
 </properties>
 <properties>
  <fill-color>#654321</fill-color>
  <fill-brightness>-32</fill-brightness>
  <source>5/0@1</source>
  <group-members><fill-brightness>64</fill-brightness><source>6/0@1</source></group-members>
 </properties>
 <properties>
  <group-members><fill-color>#aaaaaa</fill-color><source>7/0</source></group-members>
  <fill-color>#123456</fill-color>
  <source>*/*@2</source>
 </properties>
";

        var read = LayerNames.Parse(Lyp(groups));

        Assert.Empty(read.Problems);

        Assert.Equal("#939fab", read.Colors[new LayerKey(74, 0)]);
        Assert.Equal("#939fab", read.Colors[new LayerKey(75, 0)]);
        Assert.Equal("#939fab", read.Colors[new LayerKey(76, 5)]);
        Assert.Equal("colored", read.Names[new LayerKey(75, 0)]);
        Assert.Equal("deeper", read.Names[new LayerKey(76, 5)]);

        Assert.Equal("#78a68f", read.Colors[new LayerKey(1, 0)]);
        Assert.Equal("#8eb4a1", read.Colors[new LayerKey(2, 0)]);

        Assert.Equal("#204060", read.Colors[new LayerKey(3, 0)]);

        Assert.Equal("#7e6245", read.Colors[new LayerKey(6, 0)]);
        Assert.False(read.Colors.ContainsKey(new LayerKey(5, 0)));

        //A member does not take its group's layout: KLayout reads 7/0 under */*@2 as on the first.
        Assert.Equal("#123456", read.Colors[new LayerKey(7, 0)]);
    }

    ///
    ///**Brightness moves a color the way KLayout's does**, to the last bit - each of these is KLayout's answer. A share
    ///of the way to white, or to black below zero, halving every 128, worked in whole 256ths.
    ///
    [Theory]
    [InlineData("#ff0000", -255, "#3f0000")]
    [InlineData("#ff0000", -128, "#7f0000")]
    [InlineData("#ff0000", -1, "#fe0000")]
    [InlineData("#ff0000", 1, "#ff0101")]
    [InlineData("#ff0000", 128, "#ff8080")]
    [InlineData("#ff0000", 255, "#ffc0c0")]
    [InlineData("#808080", 128, "#c0c0c0")]
    [InlineData("#808080", -255, "#202020")]
    [InlineData("#f3ff80", 208, "#fcffd6")]
    [InlineData("#ff00ff", 144, "#ff8bff")]
    [InlineData("#00ffff", 96, "#68ffff")]
    public void Brightness_moves_a_color_the_way_KLayout_does(string color, int brightness, string drawn)
    {
        var read = LayerNames.Parse(Lyp(Node("1/0@1", "", color, brightness)));

        Assert.Equal(drawn, read.Colors[new LayerKey(1, 0)]);
    }

    ///
    ///**A name loses the pair it ends in, when it is its own** - sky130 writes `met1drawing_m 68/20` - and keeps
    ///anything else: a comma too, which the row the name is stored in quotes, and an entity is what it stands for. A
    ///name that was only the pair leaves the layer unnamed, and still colored.
    ///
    [Theory]
    [InlineData("met1drawing_m 68/20", "met1drawing_m")]
    [InlineData("met1.drawing - 68/20", "met1.drawing")]
    [InlineData("met1 (68/20)", "met1")]
    [InlineData("x168/20", "x168/20")]
    [InlineData("met1 66/20", "met1 66/20")]
    [InlineData("poly, gate", "poly, gate")]
    [InlineData("a &amp; b &lt;c&gt;", "a & b <c>")]
    public void A_name_loses_its_own_pair_and_keeps_the_rest(string written, string read)
    {
        Assert.Equal(read, LayerNames.Parse(Lyp(Node("68/20@1", written, "#123456"))).Names[new LayerKey(68, 20)]);
    }

    [Fact]
    public void A_name_that_was_only_the_pair_leaves_the_layer_unnamed_and_colored()
    {
        var read = LayerNames.Parse(Lyp(Node("68/20@1", "68/20", "#123456")));

        Assert.Empty(read.Names);
        Assert.Equal("#123456", read.Colors[new LayerKey(68, 20)]);
    }

    ///<summary>A file of several tabs is read by its first, the one listed first.</summary>
    [Fact]
    public void Only_the_first_tab_of_several_is_read()
    {
        string tabs = @"<?xml version=""1.0"" encoding=""utf-8""?>
<layer-properties-tabs>
 <layer-properties>
  <name>first tab</name>
  <properties><fill-color>#111111</fill-color><name>one</name><source>1/0@1</source></properties>
 </layer-properties>
 <layer-properties>
  <name>second tab</name>
  <properties><fill-color>#222222</fill-color><name>two</name><source>1/0@1</source></properties>
  <properties><fill-color>#333333</fill-color><name>three</name><source>2/0@1</source></properties>
 </layer-properties>
</layer-properties-tabs>
";

        var read = LayerNames.Parse(tabs);

        Assert.Equal(1, read.Count);
        Assert.Equal("one", read.Names[new LayerKey(1, 0)]);
        Assert.Equal("#111111", read.Colors[new LayerKey(1, 0)]);
    }

    ///<summary>Told from a CSV past a byte order mark and blank lines, which a file read as bytes keeps.</summary>
    [Fact]
    public void A_lyp_after_a_byte_order_mark_and_blank_lines_is_still_one()
    {
        var read = LayerNames.Parse("\uFEFF\n  \n" + Lyp(Node("68/20@1", "met1", "#123456")));

        Assert.Empty(read.Problems);
        Assert.Equal("met1", read.Names[new LayerKey(68, 20)]);
    }

    ///
    ///**What cannot be read is said**, the way a bad CSV row is: XML that is not a .lyp at all, and a color or a
    ///brightness that is not one - whose layer keeps its name and is left its own color.
    ///
    [Fact]
    public void What_is_not_a_lyp_or_cannot_be_read_in_one_is_reported()
    {
        var notOne = LayerNames.Parse("<?xml version=\"1.0\"?>\n<svg></svg>\n");

        Assert.Equal(0, notOne.Count);
        Assert.Contains("not a KLayout layer-properties file", Assert.Single(notOne.Problems));

        var badColor = LayerNames.Parse(Lyp(Node("68/20@1", "met1", "#12345")));

        Assert.Equal("met1", badColor.Names[new LayerKey(68, 20)]);
        Assert.Empty(badColor.Colors);
        Assert.Contains("\"#12345\" is not a color", Assert.Single(badColor.Problems));

        var badBrightness = LayerNames.Parse(Lyp(Node("68/20@1", "met1", "#123456").Replace("<fill-brightness>0</fill-brightness>", "<fill-brightness>bright</fill-brightness>")));

        Assert.Equal("#123456", badBrightness.Colors[new LayerKey(68, 20)]);
        Assert.Contains("\"bright\" is not a brightness", Assert.Single(badBrightness.Problems));
    }

    ///
    ///**A .lyp in, a layermap out**: applied to a file and exported, it is the CSV of its names and colors - which is
    ///how one becomes a starting point for the columns a .lyp does not have, the stack and the roles.
    ///
    [Fact]
    public void A_lyp_applied_and_exported_is_the_csv_of_its_names_and_colors()
    {
        var gds = LibraryWithPairs(new LayerKey(66, 20), new LayerKey(68, 20));

        LayerNames.Parse(Lyp(Node("68/20@1", "met1drawing_m 68/20", "#9999ff") + Node("66/20@1", "poly", "#d80000"))).ApplyTo(gds.AdditionalInformation.Layers);

        var reread = LayerNames.Parse(LayerNames.Export(gds.AdditionalInformation));

        Assert.Empty(reread.Problems);
        Assert.Equal("met1drawing_m", reread.Names[new LayerKey(68, 20)]);
        Assert.Equal("#9999ff", reread.Colors[new LayerKey(68, 20)]);
        Assert.Equal("poly", reread.Names[new LayerKey(66, 20)]);
        Assert.Equal("#d80000", reread.Colors[new LayerKey(66, 20)]);
    }

    ///<summary>One of sky130's own .lyp files, out of the PDK archive the repository keeps, written to a file KLayout can open.</summary>
    private static (string Path, string Text) Sky130Lyp(string name)
    {
        string archive = Path.Combine(GdsTestData.RepositoryRoot, "OtherResources", "Sky130", "skywater130-main.zip");

        using var zip = System.IO.Compression.ZipFile.OpenRead(archive);
        using var reader = new StreamReader(zip.GetEntry("skywater130-main/sky130/klayout/" + name)!.Open());

        string text = reader.ReadToEnd();
        string path = Path.Combine(Path.GetTempPath(), $"gdsviewer-{Guid.NewGuid():N}-{name}");

        File.WriteAllText(path, text);

        return (path, text);
    }

    ///
    ///**sky130's own two .lyp files, read here and by KLayout, agree layer by layer**: every layer KLayout lists has the
    ///name it is written with - less the pair sky130's end in - and the color KLayout fills it with, brightness and
    ///all, and nothing else is read. The main one is 413 layers; the cross-section one has the brightened layers.
    ///
    [Theory]
    [Trait("Needs", "KLayout")]
    [InlineData("layers.lyp")]
    [InlineData("sky130_xs.lyp")]
    public void Sky130s_own_lyp_files_read_as_KLayout_reads_them(string name)
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var (path, text) = Sky130Lyp(name);

        try
        {
            var theirs = OasisTestData.LayerPropertiesOf(path);
            var ours = LayerNames.Parse(text);

            Assert.Empty(ours.Problems);
            Assert.NotEmpty(theirs);

            var last = new Dictionary<LayerKey, (string Color, string Name)>();

            foreach (var node in theirs)
                last[node.Key] = (node.Color, node.Name);

            Assert.Equal(last.Count, ours.Count);

            foreach (var layer in last)
            {
                string pair = $" {layer.Key.Number}/{layer.Key.DataType}";
                string named = layer.Value.Name;

                if (named.EndsWith(pair, StringComparison.Ordinal))
                    named = named[..^pair.Length].TrimEnd();

                Assert.Equal(named, ours.Names[layer.Key]);
                Assert.Equal(layer.Value.Color, ours.Colors[layer.Key]);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    ///
    ///**Brightness agrees with KLayout's across colors**, not only the ones written out above: three hundred colors and
    ///brightnesses drawn at random, the same every run, through KLayout and through this.
    ///
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Brightness_agrees_with_KLayout_across_colors()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var random = new Random(130);
        var nodes = new System.Text.StringBuilder();

        for (int layer = 1; layer <= 300; layer++)
        {
            string color = "#" + random.Next(0, 1 << 24).ToString("x6", System.Globalization.CultureInfo.InvariantCulture);

            nodes.Append(Node($"{layer}/0@1", "", color, random.Next(-255, 256)));
        }

        string text = Lyp(nodes.ToString());
        string path = Path.Combine(Path.GetTempPath(), $"gdsviewer-{Guid.NewGuid():N}-brightness.lyp");

        File.WriteAllText(path, text);

        try
        {
            var theirs = OasisTestData.LayerPropertiesOf(path);
            var ours = LayerNames.Parse(text);

            Assert.Equal(300, theirs.Count);

            foreach (var node in theirs)
                Assert.Equal(node.Color, ours.Colors[node.Key]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    #endregion ***********************************************************************
}
