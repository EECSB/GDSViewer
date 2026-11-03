using GdsII;
using GdsII.Cli;

namespace GDSViewer.Tests;

///<summary>
///A rule check over part of a layout says so: in the result, in `gds drc`'s report and exit code, in the KLayout
///report a run is written out as, in what the AI assistant's check_rules answers, and on standard error from every
///other command that flattens. See DrcResult.NotChecked.
///
///**With a ceiling of a few hundred, given to each flatten**, so nothing here lowers the one every other test
///flattens under - <see cref="GdsFlattener.Flatten(GDS, int)"/> takes its own, and so does `--most-elements`. What
///showed it, IHP's 1024x16 SRAM macro, is 2.6 million elements and somebody else's.
///</summary>
public class DrcNotCheckedTests : IDisposable
{
    #region Layouts and a deck ****************************************************************

    ///<summary>A hundred-shape cell arrayed ten by ten: ten thousand elements, so a ceiling of five hundred stops it early.</summary>
    private static GDS Layout()
    {
        return Synthetic.Layout(100, 10, 10);
    }

    ///<summary>One rule nothing in <see cref="Layout"/> breaks, so a run that finds nothing is clean only when it saw everything.</summary>
    private const string Deck = """
        layer M 64/20
        rule M.1 width M 1 "Metal width"

        """;

    ///<summary>A cell that places itself, under a top that places it once.</summary>
    private const string Loop = """
        HEADER: 600
        BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21
        LIBNAME: LIB
        UNITS: 0.001 1E-09
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: LOOP
        BOUNDARY:
        LAYER: 64
        DATATYPE: 20
        XY: 0 0 1000 0 1000 1000 0 1000 0 0
        ENDEL:
        SREF:
        SNAME: LOOP
        XY: 2000 0
        ENDEL:
        ENDSTR:
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: TOP
        SREF:
        SNAME: LOOP
        XY: 0 0
        ENDEL:
        ENDSTR:
        ENDLIB:

        """;

    #endregion ****************************************************************************



    #region The result ********************************************************************

    [Fact]
    public void A_flatten_given_a_ceiling_of_its_own_stops_there()
    {
        var drawn = GdsFlattener.Flatten(Layout(), 500);

        Assert.True(drawn.Stopped);
        Assert.InRange(drawn.Elements.Count, 500, 600);
        Assert.False(GdsFlattener.Flatten(Layout(), 20_000).Stopped);
    }

    ///
    ///**The fault itself.** A check over a layout that stopped flattening found nothing and called it clean, as it
    ///would have called a layout clean that it saw all of. Now it says what it did not see, and it is neither
    ///complete nor clean.
    ///
    [Fact]
    public void A_check_over_a_layout_that_stopped_says_how_much_of_it_was_checked()
    {
        var stopped = GdsFlattener.Flatten(Layout(), 500);
        var result = Drc.Check(DrcDeck.Parse(Deck), stopped);

        Assert.Empty(result.Violations);

        string missing = Assert.Single(result.NotChecked);

        Assert.StartsWith(FormattableString.Invariant($"Only the first {stopped.Elements.Count:N0} elements of the layout were checked"), missing);
        Assert.False(result.Complete);
        Assert.False(result.Clean);
    }

    [Fact]
    public void A_check_over_all_of_it_says_nothing_of_the_kind()
    {
        var result = Drc.Check(DrcDeck.Parse(Deck), GdsFlattener.Flatten(Layout()));

        Assert.Empty(result.NotChecked);
        Assert.True(result.Clean);
    }

    ///<summary>A cell that contains itself cuts the nesting short, which is the other way a layout is checked in part.</summary>
    [Fact]
    public void A_check_over_a_cell_that_contains_itself_says_the_nesting_was_cut_short()
    {
        var result = Drc.Check(DrcDeck.Parse(Deck), GdsFlattener.Flatten(GDS.FromText(Loop)));

        Assert.Equal("A cell in this file contains itself, so its nesting was cut short and what lies past the cut was not checked.", Assert.Single(result.NotChecked));
        Assert.False(result.Complete);
    }

    ///
    ///The assistant's check_rules - the app's and `gds mcp`'s - says it too, under what it found: over the whole
    ///layout, and over an area of it, which is a window cut from the same flatten and so missing what it was missing.
    ///
    [Theory]
    [InlineData("""{"everything": true}""")]
    [InlineData("""{"everything": true, "area": [-1, -1, 5, 2]}""")]
    public void Check_rules_says_what_it_did_not_check(string arguments)
    {
        var session = new AgentSession(GDS.FromText(Loop), new EditHistory())
        {
            Deck = DrcDeck.Parse(Deck)
        };

        var answer = AgentTools.Call(session, "check_rules", arguments);

        Assert.False(answer.IsError, answer.Text);
        Assert.Contains("\nNot checked: A cell in this file contains itself", answer.Text);
    }

    #endregion ****************************************************************************



    #region The command line ****************************************************************

    private readonly StringWriter output = new StringWriter();
    private readonly StringWriter error = new StringWriter();
    private readonly List<string> temporaryFiles = new List<string>();

    private int Run(params string[] args)
    {
        return Cli.Run(args, output, error);
    }

    private string Temporary(string extension, byte[] contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gdsnotchecked-{Guid.NewGuid():N}{extension}");

        File.WriteAllBytes(path, contents);
        temporaryFiles.Add(path);

        return path;
    }

    private string LayoutFile()
    {
        return Temporary(".gds", Layout().Serialize());
    }

    private string DeckFile()
    {
        return Temporary(".drc", System.Text.Encoding.UTF8.GetBytes(Deck));
    }

    public void Dispose()
    {
        foreach (string path in temporaryFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }

    ///
    ///**Exit 4, the code for a check that cannot be concluded from**, over a layout `gds drc` stopped flattening -
    ///and the report says what was missed, and how to go further, above a summary that does not call it clean.
    ///Said in the report rather than on standard error as well, once being enough.
    ///
    [Fact]
    public void Drc_over_a_layout_that_stopped_is_an_incomplete_check()
    {
        Assert.Equal(Cli.IncompleteCheck, Run("drc", LayoutFile(), "--deck", DeckFile(), "--most-elements", "500"));

        string said = output.ToString();

        Assert.Contains("Not all of the layout was checked:", said);
        Assert.Contains("  Only the first ", said);
        Assert.Contains("  --most-elements takes a flatten further", said);
        Assert.Contains("This layout has NOT been fully checked. Nothing here says it is clean.", said);
        Assert.Equal("", error.ToString());
    }

    ///<summary>The same layout under the command line's own ceiling, which is past what the app draws, checks whole.</summary>
    [Fact]
    public void Drc_over_all_of_it_is_clean()
    {
        Assert.Equal(Cli.Ok, Run("drc", LayoutFile(), "--deck", DeckFile()));

        Assert.Contains("No violations. All 1 rule(s) ran.", output.ToString());
        Assert.DoesNotContain("Not all of the layout", output.ToString());
        Assert.True(Cli.MostElements > GdsFlattener.MostElements);
    }

    ///<summary>A loop is no reason to go further, so its report does not suggest it.</summary>
    [Fact]
    public void Drc_over_a_cell_that_contains_itself_is_incomplete_without_a_ceiling_to_raise()
    {
        Assert.Equal(Cli.IncompleteCheck, Run("drc", Temporary(".gds", GDS.FromText(Loop).Serialize()), "--deck", DeckFile()));

        Assert.Contains("  A cell in this file contains itself", output.ToString());
        Assert.DoesNotContain("--most-elements", output.ToString());
    }

    [Fact]
    public void Info_notes_that_what_it_counts_is_part_of_the_layout()
    {
        Assert.Equal(Cli.Ok, Run("info", LayoutFile(), "--most-elements", "500"));

        Assert.Contains("note        flattening stopped at ", output.ToString());
        Assert.Equal("", error.ToString());
    }

    ///<summary>Every other command that flattens says it on standard error, where a picture on standard output stays a picture.</summary>
    [Fact]
    public void The_other_commands_say_it_on_standard_error()
    {
        Assert.Equal(Cli.Ok, Run("layers", LayoutFile(), "--most-elements", "500"));

        Assert.StartsWith("Only the first ", error.ToString());
        Assert.Contains("--most-elements takes it further", error.ToString());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("many")]
    public void A_ceiling_that_is_not_a_count_is_a_usage_error(string most)
    {
        Assert.Equal(Cli.UsageError, Run("info", LayoutFile(), "--most-elements", most));

        Assert.Contains("--most-elements needs a whole number of one or more.", error.ToString());
    }

    ///<summary>`-o` writes it into the KLayout report as well, which is read back here the way it went out.</summary>
    [Fact]
    public void Drc_writes_what_it_did_not_check_into_its_report()
    {
        string report = Path.Combine(Path.GetTempPath(), $"gdsnotchecked-{Guid.NewGuid():N}.lyrdb");

        temporaryFiles.Add(report);

        Assert.Equal(Cli.IncompleteCheck, Run("drc", LayoutFile(), "--deck", DeckFile(), "--most-elements", "500", "-o", report));

        var read = DrcReport.Read(File.ReadAllBytes(report), Layout(), GdsFlattener.Flatten(Layout()));

        Assert.StartsWith("Only the first ", Assert.Single(read.NotChecked));
        Assert.EndsWith(" - not fully checked", read.Description);
        Assert.Empty(read.Items);
    }

    #endregion ****************************************************************************



    #region In a KLayout report ***************************************************************

    ///<summary>A width nothing in <see cref="Layout"/> meets, so there are faults to list.</summary>
    private const string AbsurdWidth = """
        layer M 64/20
        rule M.1 width M 100000 "Metal width"

        """;

    ///<summary>The same, with a rule this build refuses.</summary>
    private const string DeckWithARefusal = AbsurdWidth + "rule M.2 extension M 1 \"Not measurable here\"\n";

    ///<summary>A sentence with what a text value has to escape in it: quotes of both kinds, a backslash and a tab.</summary>
    private const string Awkward = "Line 3: \"frob\" isn't a check, in C:\\decks\\main.drc\tafter a tab.";

    private static (DrcResult Result, DrcDeck Deck, GDS Gds) Stopped(string deckText)
    {
        var gds = Layout();
        var deck = DrcDeck.Parse(deckText);

        return (Drc.Check(deck, GdsFlattener.Flatten(gds, 500)), deck, gds);
    }

    private static DrcReportFile ReadBack(string xml, GDS gds)
    {
        return DrcReport.Read(xml, gds, GdsFlattener.Flatten(gds));
    }

    ///
    ///**The fault this is for.** A report of a check that stopped held its faults and nothing else, so in KLayout's
    ///marker browser it was a list of things to fix. Now its description says it fell short, and a category of its
    ///own comes first - declared first and filed first, which is the order the browser lists them in.
    ///
    [Fact]
    public void A_report_of_a_check_that_stopped_says_so_ahead_of_its_faults()
    {
        var run = Stopped(DeckWithARefusal);

        string xml = DrcReport.Write(run.Result, run.Deck, run.Gds, "TOP", "layout against deck");

        Assert.NotEmpty(run.Result.Violations);
        Assert.Contains("<description>layout against deck - not fully checked</description>", xml);
        Assert.True(xml.IndexOf("<name>Not checked</name>", StringComparison.Ordinal) < xml.IndexOf("<name>M.1</name>", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<category>'Not checked'</category>", StringComparison.Ordinal) < xml.IndexOf("<category>'M.1'</category>", StringComparison.Ordinal));
        Assert.Equal(2, xml.Split("<value>text: ").Length - 1);
    }

    [Fact]
    public void A_report_of_a_check_that_got_to_everything_has_no_such_category()
    {
        var gds = Layout();
        var deck = DrcDeck.Parse(AbsurdWidth);
        var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

        string xml = DrcReport.Write(result, deck, gds, "TOP", "layout against deck");

        Assert.True(result.Complete);
        Assert.DoesNotContain(DrcReport.NotCheckedCategory, xml);
        Assert.Contains("<description>layout against deck</description>", xml);
    }

    ///
    ///Read back here, the sentences come in as what the check did not get to and not as faults: none of them is an
    ///item, nor is their category one, so the app counts the same faults KLayout lists under the rules.
    ///
    [Fact]
    public void Read_back_what_was_not_checked_is_said_and_not_counted_as_faults()
    {
        var run = Stopped(DeckWithARefusal);

        var read = ReadBack(DrcReport.Write(run.Result, run.Deck, run.Gds, "TOP", "layout against deck"), run.Gds);

        Assert.Equal(run.Result.NotChecked.Concat(run.Result.NotRun), read.NotChecked);
        Assert.Equal(run.Result.Violations.Count, read.Items.Count);
        Assert.DoesNotContain(read.Categories, category => category.Name == DrcReport.NotCheckedCategory);
        Assert.Empty(read.Problems);
    }

    [Fact]
    public void A_report_over_a_cell_that_contains_itself_says_the_nesting_was_cut_short()
    {
        var gds = GDS.FromText(Loop);
        var deck = DrcDeck.Parse(Deck);
        var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

        var read = ReadBack(DrcReport.Write(result, deck, gds, "TOP", "loop"), gds);

        Assert.Equal("A cell in this file contains itself, so its nesting was cut short and what lies past the cut was not checked.", Assert.Single(read.NotChecked));
        Assert.Equal("loop - not fully checked", read.Description);
    }

    ///<summary>Written as KLayout quotes a text value - see the KLayout test below - and so read back as it was said.</summary>
    [Fact]
    public void A_sentence_keeps_its_quotes_and_backslashes_on_the_way_back()
    {
        var gds = Layout();
        var result = new DrcResult();

        result.Problems.Add(Awkward);

        var read = ReadBack(DrcReport.Write(result, DrcDeck.Parse(Deck), gds, "TOP", "awkward"), gds);

        Assert.Equal(Awkward, Assert.Single(read.NotChecked));
    }

    ///
    ///Told apart by what is in it as well as by its name: a rule somebody did call "Not checked" finds faults, each
    ///with its marker, and they come back as the faults they are.
    ///
    [Fact]
    public void A_rule_called_not_checked_is_still_a_rule_when_read_back()
    {
        var gds = Layout();
        var deck = DrcDeck.Parse("layer M 64/20\nrule \"Not checked\" width M 100000 \"A rule named so\"\n");
        var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

        var read = ReadBack(DrcReport.Write(result, deck, gds, "TOP", "named so"), gds);

        Assert.True(result.Complete);
        Assert.NotEmpty(result.Violations);
        Assert.Empty(read.NotChecked);
        Assert.Equal(result.Violations.Count, read.Items.Count);
        Assert.Contains(read.Categories, category => category.Name == "Not checked" && category.Items == result.Violations.Count);
    }

    ///
    ///**What KLayout makes of it**: its marker browser shows the description at the top, lists "Not checked" first
    ///of the categories, and has every sentence as it was written - quotes, backslash and tab included.
    ///
    [Fact]
    [Trait("Needs", "KLayout")]
    public void KLayout_lists_what_was_not_checked_first()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed to read this back.");

        var run = Stopped(DeckWithARefusal);

        run.Result.Problems.Add(Awkward);

        string path = Temporary(".lyrdb", System.Text.Encoding.UTF8.GetBytes(DrcReport.Write(run.Result, run.Deck, run.Gds, "TOP", "layout against deck")));
        var words = OasisTestData.ReadReport(path);
        var said = OasisTestData.ReportWords(path);

        Assert.Equal("layout against deck - not fully checked", said.Description);
        Assert.Equal(new[] { DrcReport.NotCheckedCategory, "M.1" }, said.Categories);
        Assert.Equal(run.Result.NotChecked.Concat(run.Result.NotRun).Concat(run.Result.Problems), said.Texts);
        Assert.Equal(run.Result.Violations.Count + 3, words.Items);
    }

    #endregion ****************************************************************************
}
