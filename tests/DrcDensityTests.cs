using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The density check, which is the one rule here that is not about a distance.
///
///**A sliding window, not an average.** A layer that averages 40% can still have a hundred-micron square
///with nothing in it, and the average is exactly what hides that - polishing dishes where metal is sparse
///and erodes where it is dense, and both faults are local. So the tests here are mostly about a layout
///whose overall figure is fine and whose worst window is not.
///</summary>
public class DrcDensityTests
{
    private static Element.Point At(int x, int y)
    {
        return new Element.Point { X = x, Y = y };
    }

    private static List<Element.Point> Box(int left, int bottom, int right, int top)
    {
        return new List<Element.Point>
        {
            At(left, bottom),
            At(right, bottom),
            At(right, top),
            At(left, top),
            At(left, bottom)
        };
    }

    #region The check *******************************************************************

    ///<summary>A solid square is fully dense, so no window of it comes up short.</summary>
    [Fact]
    public void A_solid_layer_fails_no_window()
    {
        var solid = new List<List<Element.Point>> { Box(0, 0, 1000, 1000) };

        //Every window inside it is 100% covered, so a 50% floor passes.
        Assert.Empty(DrcChecks.Density(solid, 200, 200, 500));
    }

    ///<summary>And a layer with an empty quarter fails a floor the full quarters pass.</summary>
    [Fact]
    public void A_sparse_layer_fails()
    {
        var sparse = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1000, 1000, 2000, 2000)
        };

        //A 2000 by 2000 extent in four windows: two solid on the diagonal, two with nothing in them.
        Assert.NotEmpty(DrcChecks.Density(sparse, 1000, 1000, 500));
    }

    ///<summary>
    ///A layout smaller than the window is not measured at all.
    ///
    ///A hundred-micron rule has no opinion about a two-micron test cell. Measuring one window that is
    ///mostly outside the layout would produce a failure about the window rather than about the layout, and
    ///it would fire on every small cell anybody opened.
    ///</summary>
    [Fact]
    public void A_layout_smaller_than_the_window_is_left_alone()
    {
        var small = new List<List<Element.Point>> { Box(0, 0, 500, 500) };

        Assert.Empty(DrcChecks.Density(small, 1000, 1000, 900));
    }

    ///<summary>
    ///The test the sliding window exists for.
    ///
    ///Two solid blocks far apart: the layer covers half of everything between them, so any figure taken
    ///over the whole extent passes a 40% floor comfortably. The empty ground in the middle is a window of
    ///nothing at all, and that is the one a process cares about.
    ///</summary>
    [Fact]
    public void A_layout_that_passes_on_average_can_fail_on_a_window()
    {
        var apart = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1000, 0, 2000, 1000)
        };

        var gapped = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(3000, 0, 4000, 1000)
        };

        //Side by side, every window is solid.
        Assert.Empty(DrcChecks.Density(apart, 1000, 500, 400));

        //Pulled apart, the ground between them is empty and a window lands on it.
        Assert.NotEmpty(DrcChecks.Density(gapped, 1000, 500, 400));
    }

    ///<summary>The marker is the window that came up short, since there is no shape to outline.</summary>
    [Fact]
    public void The_marker_is_the_window_rather_than_the_geometry()
    {
        var gapped = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(3000, 0, 4000, 1000)
        };

        var sparse = DrcChecks.Density(gapped, 1000, 500, 400);

        Assert.NotEmpty(sparse);

        //A square of exactly the window, so its area is the window's.
        Assert.Equal(1000.0 * 1000.0, Measure.AreaOf(sparse[0]), 3);
    }

    [Fact]
    public void A_layer_with_nothing_on_it_reports_nothing()
    {
        Assert.Empty(DrcChecks.Density(new List<List<Element.Point>>(), 1000, 500, 500));
    }

    #endregion **************************************************************************



    #region Through a deck **************************************************************

    [Fact]
    public void A_density_rule_carries_its_window_and_step()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.9 density met1 300 window 100000 step 50000 ""Met1 minimum density""");

        Assert.Empty(deck.Problems);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal(DrcCheck.Density, rule.Check);
        Assert.Equal(300, rule.Value);
        Assert.Equal(100000, rule.Window);
        Assert.Equal(50000, rule.Step);
        Assert.Equal("met1", Assert.Single(rule.Operands));
    }

    ///<summary>
    ///A density without a window is refused rather than given one.
    ///
    ///A window nobody chose is a number this invented, and every answer measured over it would be about
    ///that number rather than about the layout.
    ///</summary>
    [Fact]
    public void A_density_rule_without_a_window_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.9 density met1 300 ""No window""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    [Fact]
    public void A_window_that_is_not_a_number_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.9 density met1 300 window wide step 50000 ""Bad window""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    ///<summary>The modifiers can sit anywhere, like except - a fixed order is one somebody gets wrong.</summary>
    [Fact]
    public void The_window_and_step_may_come_in_either_order()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.9 density met1 step 50000 window 100000 300 ""Met1 minimum density""");

        Assert.Empty(deck.Problems);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal(100000, rule.Window);
        Assert.Equal(50000, rule.Step);
        Assert.Equal(300, rule.Value);
    }

    ///<summary>And a whole run, so the check reaches the engine rather than only the parser.</summary>
    [Fact]
    public void A_density_rule_runs_against_a_layout()
    {
        var layout = new FlattenedLayout
        {
            Elements =
            {
                new Element { Layer = new Layer(new LayerKey(68, 20), "#000000"), Points = Box(0, 0, 1000, 1000) },
                new Element { Layer = new Layer(new LayerKey(68, 20), "#000000"), Points = Box(4000, 0, 5000, 1000) }
            }
        };

        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.9 density met1 400 window 1000 step 500 ""Met1 minimum density""");

        var result = Drc.Check(deck, layout);

        Assert.Empty(result.Problems);
        Assert.Empty(result.NotRun);
        Assert.NotEmpty(result.Violations);
        Assert.Equal("met1.9", result.Violations[0].RuleId);
    }

    #endregion **************************************************************************



    #region The measurement it shares ***************************************************

    ///<summary>
    ///The range over a layout that is half solid and half empty.
    ///
    ///DensityOf answers one number about the whole layer; this answers the two that a process rule is
    ///written against, and on this layout they are as far apart as they can be.
    ///</summary>
    [Fact]
    public void The_range_finds_both_the_emptiest_and_the_fullest_window()
    {
        var layout = new FlattenedLayout
        {
            Elements =
            {
                new Element { Layer = new Layer(new LayerKey(68, 20), "#000000"), Points = Box(0, 0, 1000, 1000) },
                new Element { Layer = new Layer(new LayerKey(68, 20), "#000000"), Points = Box(4000, 0, 5000, 1000) }
            }
        };

        var range = Measure.DensityRange(layout, new LayerKey(68, 20), 1000, 500);

        Assert.NotNull(range);

        //Somewhere in the middle there is a window with nothing in it, and on each block one that is full.
        Assert.Equal(0, range!.Value.Least, 3);
        Assert.Equal(1, range.Value.Most, 3);
    }

    [Fact]
    public void The_range_of_a_layer_with_nothing_on_it_is_nothing()
    {
        var layout = new FlattenedLayout();

        Assert.Null(Measure.DensityRange(layout, new LayerKey(68, 20), 1000, 500));
    }

    #endregion **************************************************************************
}
