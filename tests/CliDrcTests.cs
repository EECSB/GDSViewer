using GdsII;
using GdsII.Cli;

namespace GDSViewer.Tests;

///<summary>
///Covers `gds drc`, the command that checks a layout against a deck of design rules.
///
///**The exit codes carry most of the weight.** A rule check exists to be scripted - a gate over a cell
///library is the point of having it on a command line at all - and a script branches on the code rather
///than reading the words. Three of them matter and they mean different things: the layout broke a rule,
///the run could not finish, and the command line was wrong.
///
///Cli.Run takes its writers and returns a code rather than touching Console, so all of this runs in
///process the way the rest of CliTests does.
///</summary>
public class CliDrcTests : IDisposable
{
    private readonly StringWriter output = new StringWriter();
    private readonly StringWriter error = new StringWriter();
    private readonly List<string> temporaryFiles = new List<string>();

    private string Output
    {
        get { return output.ToString(); }
    }

    private int Run(params string[] args)
    {
        return Cli.Run(args, output, error);
    }

    private string DeckFile(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gdsdrc-{Guid.NewGuid():N}.drc");

        File.WriteAllText(path, text);
        temporaryFiles.Add(path);

        return path;
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

    private static string Mosfet
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample); }
    }

    private static string BundledDeck
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc"); }
    }

    #region The command line ***********************************************************

    [Fact]
    public void Drc_without_a_deck_is_a_usage_error()
    {
        Assert.Equal(Cli.UsageError, Run("drc", Mosfet));
    }

    [Fact]
    public void Drc_with_a_deck_that_is_not_there_is_a_file_error()
    {
        Assert.Equal(Cli.FileError, Run("drc", Mosfet, "--deck", "no-such-deck.drc"));
    }

    [Fact]
    public void A_deck_holding_no_rules_at_all_is_a_file_error()
    {
        string deck = DeckFile("#nothing but a comment\n");

        Assert.Equal(Cli.FileError, Run("drc", Mosfet, "--deck", deck));
    }

    [Fact]
    public void Drc_is_offered_in_the_usage_text()
    {
        Run("--help");

        Assert.Contains("gds drc", Output);
    }

    #endregion **************************************************************************



    #region What the exit codes mean ****************************************************

    [Fact]
    public void A_layout_that_breaks_nothing_exits_zero()
    {
        string deck = DeckFile("layer met1 68/20\nrule met1.1 width met1 100 \"Met1 width\"\n");

        Assert.Equal(Cli.Ok, Run("drc", Mosfet, "--deck", deck));
        Assert.Contains("No violations", Output);
    }

    [Fact]
    public void A_layout_that_breaks_a_rule_exits_three()
    {
        string deck = DeckFile("layer poly 66/20\nrule poly.1a width poly 100000 \"Absurd width\"\n");

        Assert.Equal(Cli.ViolationsFound, Run("drc", Mosfet, "--deck", deck));
        Assert.Contains("poly.1a", Output);
    }

    ///<summary>
    ///The code that matters most. Nothing was found and nothing may be concluded from that, because a rule
    ///never ran - so it is neither zero nor the same code as a layout with faults in it.
    ///</summary>
    [Fact]
    public void A_run_that_could_not_finish_exits_four()
    {
        string deck = DeckFile("layer met1 68/20\nrule x.1 spaceparallel met1 100 \"Cannot measure\"\n");

        Assert.Equal(Cli.IncompleteCheck, Run("drc", Mosfet, "--deck", deck));
    }

    [Fact]
    public void A_run_that_could_not_finish_says_so_in_words_as_well()
    {
        string deck = DeckFile("layer met1 68/20\nrule x.1 spaceparallel met1 100 \"Cannot measure\"\n");

        Run("drc", Mosfet, "--deck", deck);

        Assert.Contains("did not run", Output);
        Assert.Contains("x.1", Output);
        Assert.Contains("NOT been fully checked", Output);
    }

    ///<summary>The word "clean" is never printed over a run that skipped something.</summary>
    [Fact]
    public void An_incomplete_run_never_claims_the_layout_is_clean()
    {
        string deck = DeckFile("layer met1 68/20\nrule x.1 spaceparallel met1 100 \"Cannot measure\"\n");

        Run("drc", Mosfet, "--deck", deck);

        Assert.DoesNotContain("No violations", Output);
    }

    ///<summary>Incomplete outranks a violation count, because it is the answer nobody can act on.</summary>
    [Fact]
    public void A_run_with_both_faults_and_a_skipped_rule_reports_the_skip()
    {
        string deck = DeckFile(@"layer poly 66/20
rule poly.1a width poly 100000 ""Absurd width""
rule x.1 spaceparallel poly 100 ""Cannot measure""
");

        Assert.Equal(Cli.IncompleteCheck, Run("drc", Mosfet, "--deck", deck));
    }

    #endregion **************************************************************************



    #region What it prints **************************************************************

    [Fact]
    public void Markers_list_where_each_violation_is()
    {
        string deck = DeckFile("layer poly 66/20\nrule poly.1a width poly 100000 \"Absurd width\"\n");

        Run("drc", Mosfet, "--deck", deck, "--markers");

        Assert.Contains("where", Output);
        Assert.Contains(" to ", Output);
    }

    [Fact]
    public void One_rule_can_be_singled_out_for_reporting()
    {
        string deck = DeckFile(@"layer poly 66/20
layer diff 65/20
rule poly.1a width poly 100000 ""Absurd poly width""
rule difftap.1 width diff 100000 ""Absurd diff width""
");

        Run("drc", Mosfet, "--deck", deck, "--rule", "poly.1a");

        Assert.Contains("poly.1a", Output);
        Assert.DoesNotContain("difftap.1", Output);
    }

    #endregion **************************************************************************



    #region The bundled deck ***********************************************************

    ///<summary>
    ///The hand-made transistor against the deck that ships beside it, which is the first thing anybody
    ///trying this will run.
    ///
    ///It comes back clean, and that is worth pinning rather than assuming: the deck carried two extension
    ///rules until this command was pointed at this file, and every violation they produced was the axis the
    ///rule does not mean. A deck whose own demonstration reports faults on a correct layout teaches nobody
    ///to trust it.
    ///</summary>
    [Fact]
    public void The_bundled_deck_finds_nothing_wrong_with_the_bundled_transistor()
    {
        Assert.Equal(Cli.Ok, Run("drc", Mosfet, "--deck", BundledDeck));
        Assert.Contains("No violations", Output);
    }

    ///<summary>
    ///A pin is not manufactured, so where it sits is not a grid violation.
    ///
    ///**Pinned because it was a real false positive**, and one that looked convincing: run over every
    ///element in the file rather than over the layers the deck declares, the off-grid rule reported a
    ///signed-off sky130 standard cell five times for one contact-sized square three nanometers off the
    ///grid - on 122/16, which is `pwell.pin`. Nothing on a mask comes from a pin. `*` means the layers the
    ///deck names, and the deck names the ones that are made.
    ///</summary>
    [Fact]
    public void A_pin_layer_is_not_checked_against_the_manufacturing_grid()
    {
        //122/16 is pwell.pin in sky130, and this cell carries one sitting off the 5nm grid.
        string cell = Path.Combine(
            GdsTestData.RepositoryRoot,
            "OtherResources",
            "Sky130",
            "GDS",
            "Sky130 GDS",
            "sky130_fd_sc_hd__a2111o_1.gds");

        Assert.True(File.Exists(cell), $"The cell this pins is missing: {cell}");

        //A deck naming only the pin's layer number on its drawing purpose, so the pin itself is not
        //declared and must not be looked at.
        string deck = DeckFile("layer pwell 64/44\nrule grid.1 offgrid * 5 \"Off the 5nm grid\"\n");

        Run("drc", cell, "--deck", deck);

        Assert.Contains("No violations", Output);
    }

    #endregion **************************************************************************
}
