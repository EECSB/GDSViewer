using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The report database a run is written out as, and the only outside opinion this feature can get.
///
///**Two different things are checked here and they are worth telling apart.** The first is that what this
///writes is a file KLayout will open - the same interoperability standard the GDSII and OASIS writers are
///held to, applied to the one format in this feature that somebody else defined. The second, and the more
///valuable, is whether the *answers* agree: every other test of the checker measures it against itself, and
///none of them can say the design agrees with anybody.
///
///The KLayout ones are skipped where it is not installed, like the boolean comparisons.
///</summary>
public class DrcReportTests
{
    private static GDS Mosfet()
    {
        return new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
    }

    private static string MosfetPath
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample); }
    }

    ///<summary>A real sky130 standard cell, signed off by the foundry - which is what makes it interesting.</summary>
    private static string StandardCellPath
    {
        get
        {
            return Path.Combine(
                GdsTestData.RepositoryRoot,
                "OtherResources",
                "Sky130",
                "GDS",
                "Sky130 GDS",
                "sky130_fd_sc_hd__a2111o_1.gds");
        }
    }

    private static (DrcResult Result, DrcDeck Deck, GDS Gds) Run(string deckText, GDS gds)
    {
        var deck = DrcDeck.Parse(deckText);

        return (Drc.Check(deck, GdsFlattener.Flatten(gds)), deck, gds);
    }

    private const string AbsurdWidth = @"layer poly 66/20
rule poly.1a width poly 2000 ""Poly minimum width""";

    #region What it writes **************************************************************

    [Fact]
    public void A_report_names_the_top_cell_and_the_rules_that_fired()
    {
        var run = Run(AbsurdWidth, Mosfet());

        string xml = DrcReport.Write(run.Result, run.Deck, run.Gds, "mosfet");

        Assert.Contains("<top-cell>mosfet</top-cell>", xml);
        Assert.Contains("<name>poly.1a</name>", xml);
        Assert.Contains("<description>Poly minimum width</description>", xml);
    }

    ///<summary>
    ///A category is named in single quotes on the item and bare in the declaration.
    ///
    ///KLayout's own doing rather than a mistake here, and learned by reading a report it wrote rather than
    ///guessed: an item naming its category bare is read as belonging to a different one.
    ///</summary>
    [Fact]
    public void An_item_quotes_the_category_it_belongs_to()
    {
        var run = Run(AbsurdWidth, Mosfet());

        string xml = DrcReport.Write(run.Result, run.Deck, run.Gds, "mosfet");

        Assert.Contains("<category>'poly.1a'</category>", xml);
    }

    ///<summary>
    ///Coordinates go out in microns, which is the one place this feature leaves database units.
    ///
    ///The format is somebody else's and it is written in microns. A marker at 2000 database units on a
    ///nanometer grid is at 2 in the file.
    ///</summary>
    [Fact]
    public void Coordinates_are_written_in_microns()
    {
        var run = Run(AbsurdWidth, Mosfet());

        string xml = DrcReport.Write(run.Result, run.Deck, run.Gds, "mosfet");

        //The bundled transistor is a couple of microns across, so nothing in it reaches three figures.
        Assert.Contains("polygon: (", xml);
        Assert.DoesNotContain("polygon: (-300,", xml);
    }

    [Fact]
    public void A_run_that_found_nothing_still_writes_a_readable_report()
    {
        var run = Run("layer poly 66/20\nrule poly.1a width poly 100 \"Poly width\"", Mosfet());

        string xml = DrcReport.Write(run.Result, run.Deck, run.Gds, "mosfet");

        Assert.Empty(run.Result.Violations);
        Assert.Contains("<items>", xml);
        Assert.Contains("<top-cell>mosfet</top-cell>", xml);
    }

    #endregion **************************************************************************



    #region What KLayout makes of it ****************************************************

    ///<summary>
    ///The standard this repository holds its writers to: the other tool opens what this one writes.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void KLayout_reads_the_report_this_writes()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed to read this back.");

        var run = Run(AbsurdWidth, Mosfet());

        string path = Path.Combine(Path.GetTempPath(), $"gdsdrc-{Guid.NewGuid():N}.lyrdb");

        try
        {
            File.WriteAllText(path, DrcReport.Write(run.Result, run.Deck, run.Gds, "mosfet"));

            var read = OasisTestData.ReadReport(path);

            Assert.Equal(run.Result.Violations.Count, read.Items);
            Assert.Contains("poly.1a", read.Categories);
            Assert.StartsWith("polygon:", read.FirstValue);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }

    #endregion **************************************************************************



    #region Whether the answers agree ***************************************************

    ///<summary>
    ///A layout both engines call clean.
    ///
    ///The weaker half of the pair and still worth having: an engine that reported something here would be
    ///wrong in the direction that makes a checker useless, since drawing to minimum is what layout is.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Both_engines_find_nothing_wrong_with_poly_at_its_real_minimum()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        var run = Run("layer poly 66/20\nrule poly.1a width poly 150 \"Poly width\"", Mosfet());

        int theirs = OasisTestData.RuleViolations(MosfetPath, 66, 20, "width", 150);

        Assert.Empty(run.Result.Violations);
        Assert.Equal(0, theirs);
    }

    ///<summary>
    ///And a limit nothing could satisfy, which both find.
    ///
    ///Counts are deliberately not compared. KLayout answers in edge pairs and this answers in regions, so
    ///one region too narrow is any number of edges facing each other - the comparable thing is whether
    ///either engine found anything, which is the question a rule check exists to answer.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Both_engines_find_poly_too_narrow_for_an_absurd_limit()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        var run = Run(AbsurdWidth, Mosfet());

        int theirs = OasisTestData.RuleViolations(MosfetPath, 66, 20, "width", 2000);

        Assert.NotEmpty(run.Result.Violations);
        Assert.True(theirs > 0, "KLayout found nothing, so the two do not agree");
    }

    ///<summary>
    ///The one that was left open when the command was built, settled.
    ///
    ///**A signed-off sky130 standard cell reports one diffusion spacing violation here**, and the honest
    ///position at the time was that only a reference tool could say whether that was real. It is: KLayout's
    ///own engine, on the same cell against the same limit, finds it too.
    ///
    ///So the engine is not what is wrong. What remains open is the *rule* - the real difftap.2 carries
    ///qualifications this deck does not transcribe, and a gap between two diffusions of different types
    ///across a well boundary is not the thing that rule is about. That is a question about the deck, which
    ///is a text file somebody edits, rather than about the code.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void KLayout_finds_the_same_diffusion_spacing_this_does()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");
        Assert.True(File.Exists(StandardCellPath), $"The cell this compares against is missing: {StandardCellPath}");

        var gds = new GDS(File.ReadAllBytes(StandardCellPath));

        var run = Run("layer diff 65/20\nrule difftap.2 space diff 340 \"Diffusion spacing\"", gds);

        int theirs = OasisTestData.RuleViolations(StandardCellPath, 65, 20, "space", 340);

        Assert.NotEmpty(run.Result.Violations);
        Assert.True(theirs > 0, "KLayout found no spacing fault, so this one is ours alone");
    }

    ///<summary>
    ///And the same cell is clean under both at a limit the layout does satisfy.
    ///
    ///The companion to the one above, and what stops it from being a test that would pass on an engine that
    ///reported everything.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Both_engines_leave_that_cell_alone_at_a_limit_it_meets()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");
        Assert.True(File.Exists(StandardCellPath), $"The cell this compares against is missing: {StandardCellPath}");

        var gds = new GDS(File.ReadAllBytes(StandardCellPath));

        var run = Run("layer diff 65/20\nrule difftap.2 space diff 100 \"Diffusion spacing\"", gds);

        int theirs = OasisTestData.RuleViolations(StandardCellPath, 65, 20, "space", 100);

        Assert.Empty(run.Result.Violations);
        Assert.Equal(0, theirs);
    }

    #endregion **************************************************************************
}
