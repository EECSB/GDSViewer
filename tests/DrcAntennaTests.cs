using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The antenna rule, which is the only check here that is about connectivity rather than geometry.
///
///**Two identical wires are fine or fatal depending on what else is on their net.** During manufacture a
///long run of metal collects charge from the plasma etching it, and that charge leaves through whatever
///gate oxide the run reaches - so the question is the ratio of a whole net's metal to the gate at the end
///of it, and no shape can be asked it on its own.
///
///Which makes the test that matters the one about roles: a GDSII file does not say which of its numbers are
///metal, so without them nothing is connected, every net is one shape, and the rule would pass a layout it
///never looked at.
///</summary>
public class DrcAntennaTests
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

    private static readonly LayerKey Met1 = new LayerKey(68, 20);
    private static readonly LayerKey Poly = new LayerKey(66, 20);
    private static readonly LayerKey Diff = new LayerKey(65, 20);
    private static readonly LayerKey Licon = new LayerKey(66, 44);

    private static Element Shape(LayerKey key, List<Element.Point> points, LayerRole role = LayerRole.None)
    {
        var layer = new Layer(key, "#000000");

        layer.Role = role;

        return new Element { Layer = layer, Points = points };
    }

    ///
    ///A transistor with a wire on its poly: poly crosses diff to make a gate, a contact sits on the poly,
    ///and a run of met1 comes off the contact. How long that run is decides whether the net is an antenna.
    ///
    ///**The contact is not decoration.** Two conductors are one net when they share a layer number or when
    ///something between them is a via - metal1 laid over poly is two different layers and is not connected
    ///by touching, which is exactly right and is how a real stack works. Building this fixture without the
    ///licon produced no net, no gate on it and no violation, which is what the first run of these tests
    ///found.
    ///
    private static FlattenedLayout Transistor(int metalLength)
    {
        return new FlattenedLayout
        {
            Elements =
            {
                //The gate: poly over diff.
                Shape(Diff, Box(0, 0, 400, 200)),
                Shape(Poly, Box(150, 0, 250, 200), LayerRole.Conductor),

                //The contact joining the poly to the metal above it.
                Shape(Licon, Box(200, 85, 240, 115), LayerRole.Via),

                //And the wire coming off it, running away to the right.
                Shape(Met1, Box(210, 80, 210 + metalLength, 120), LayerRole.Conductor)
            }
        };
    }

    private const string Deck = @"layer met1 68/20
layer poly 66/20
layer diff 65/20
layer licon 66/44
derive gate = poly and diff
rule met1.antenna antenna met1 gate 400 ""Met1 antenna ratio""";

    #region The ratio *******************************************************************

    ///<summary>
    ///A short wire on a gate is not an antenna.
    ///
    ///The gate is 100 by 200, so 20,000 square units. A wire of 40 by 400 is 16,000 - a ratio well under
    ///one, let alone four hundred.
    ///</summary>
    [Fact]
    public void A_short_wire_is_not_an_antenna()
    {
        var result = Drc.Check(DrcDeck.Parse(Deck), Transistor(400));

        Assert.Empty(result.NotRun);
        Assert.Empty(result.Violations);
    }

    ///<summary>And a wire long enough to pass four hundred to one is.</summary>
    [Fact]
    public void A_long_wire_on_the_same_gate_is()
    {
        //40 units tall against a 20,000 square gate: past 400:1 needs more than 8,000,000 square units of
        //metal, so 250,000 long.
        var result = Drc.Check(DrcDeck.Parse(Deck), Transistor(250000));

        Assert.Empty(result.NotRun);

        var violation = Assert.Single(result.Violations);

        Assert.Equal("met1.antenna", violation.RuleId);
        Assert.Equal(DrcCheck.Antenna, violation.Check);
    }

    ///<summary>
    ///A wire reaching no gate at all is left alone, however long it is.
    ///
    ///It has no oxide to damage. Dividing by its absent gate would make every dangling piece of metal the
    ///worst antenna in the file, which is the opposite of useful.
    ///</summary>
    [Fact]
    public void A_wire_attached_to_no_gate_is_left_alone()
    {
        var layout = new FlattenedLayout
        {
            Elements =
            {
                Shape(Diff, Box(0, 0, 400, 200)),
                Shape(Poly, Box(150, 0, 250, 200), LayerRole.Conductor),

                //Far away, touching nothing.
                Shape(Met1, Box(100000, 100000, 400000, 100040), LayerRole.Conductor)
            }
        };

        var result = Drc.Check(DrcDeck.Parse(Deck), layout);

        Assert.Empty(result.Violations);
    }

    #endregion **************************************************************************



    #region Without roles ***************************************************************

    ///<summary>
    ///The test the whole check hangs on.
    ///
    ///**No roles means no connectivity**, and no connectivity means every net is one shape and every ratio
    ///is tiny. A run that reported nothing here would be passing a layout it had not looked at - so the
    ///rule is refused instead, and the result is not clean.
    ///</summary>
    [Fact]
    public void Without_roles_the_rule_does_not_run_rather_than_passing()
    {
        //The same layout that fails above, with nothing said about what its layers are for.
        var layout = new FlattenedLayout
        {
            Elements =
            {
                Shape(Diff, Box(0, 0, 400, 200)),
                Shape(Poly, Box(150, 0, 250, 200)),
                Shape(Met1, Box(250, 80, 250250, 120))
            }
        };

        var result = Drc.Check(DrcDeck.Parse(Deck), layout);

        Assert.Empty(result.Violations);
        Assert.Single(result.NotRun);
        Assert.Contains("met1.antenna", result.NotRun[0]);
        Assert.False(result.Clean);
    }

    #endregion **************************************************************************



    #region Reading the deck ************************************************************

    [Fact]
    public void An_antenna_rule_takes_two_layers_and_a_ratio()
    {
        var deck = DrcDeck.Parse(Deck);

        Assert.Empty(deck.Problems);

        var rule = deck.Rules.Single(one => one.Check == DrcCheck.Antenna);

        Assert.Equal(2, rule.Operands.Count);
        Assert.Equal("met1", rule.Operands[0]);
        Assert.Equal("gate", rule.Operands[1]);
        Assert.Equal(400, rule.Value);
    }

    [Fact]
    public void An_antenna_rule_with_one_layer_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.antenna antenna met1 400 ""Missing the gate""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    #endregion **************************************************************************



    #region Every net at once ***********************************************************

    ///<summary>
    ///Nets.All finds each net once, which is what an antenna rule needs and what Reaching cannot give.
    ///
    ///Reaching answers about a shape somebody clicked and builds its own adjacency to do it - right for one
    ///question and quadratic for all of them.
    ///</summary>
    [Fact]
    public void Every_net_is_found_once()
    {
        var layout = new FlattenedLayout
        {
            Elements =
            {
                //Two wires that touch each other, and a third far away.
                Shape(Met1, Box(0, 0, 100, 40), LayerRole.Conductor),
                Shape(Met1, Box(100, 0, 200, 40), LayerRole.Conductor),
                Shape(Met1, Box(5000, 0, 5100, 40), LayerRole.Conductor)
            }
        };

        var nets = Nets.All(layout);

        Assert.Equal(2, nets.Count);
        Assert.Contains(nets, net => net.Count == 2);
        Assert.Contains(nets, net => net.Count == 1);
    }

    ///<summary>A layer with no role takes no part, so it forms no net of its own.</summary>
    [Fact]
    public void A_layer_with_no_role_is_not_a_net()
    {
        var layout = new FlattenedLayout
        {
            Elements =
            {
                Shape(Met1, Box(0, 0, 100, 40), LayerRole.Conductor),
                Shape(Diff, Box(5000, 0, 5100, 40))
            }
        };

        Assert.Single(Nets.All(layout));
    }

    #endregion **************************************************************************
}
