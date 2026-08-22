using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Covers DrcDeck, which reads a design rule deck out of text the user supplies.
///
///Pure string and list work, so it is tested directly rather than through the browser - the same reason
///LayerNames is. Most of what is checked here is the *refusal* behavior rather than the happy path: a deck
///that half-reads is the failure this format exists to prevent, because a rule that quietly did not run
///turns into a report that says a layout is clean when nobody looked.
///</summary>
public class DrcDeckTests
{
    #region Reading ********************************************************************

    [Fact]
    public void A_layer_line_names_a_pair()
    {
        var deck = DrcDeck.Parse("layer met1 68/20");

        Assert.Empty(deck.Problems);
        Assert.Equal(new LayerKey(68, 20), deck.Layers["met1"]);
    }

    [Fact]
    public void A_derivation_reads_left_to_right()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
derive gate = poly and diff");

        Assert.Empty(deck.Problems);

        var gate = Assert.Single(deck.Derivations);

        Assert.Equal("gate", gate.Name);
        Assert.Equal("poly", gate.First);

        var step = Assert.Single(gate.Rest);

        Assert.Equal(BooleanOperation.And, step.Operation);
        Assert.Equal("diff", step.Operand);
    }

    ///<summary>Three layers deep, to prove the walk continues rather than stopping after one operation.</summary>
    [Fact]
    public void A_derivation_carries_every_operation_it_names()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
layer psdm 94/20
derive odd = poly and diff not psdm");

        Assert.Empty(deck.Problems);

        var odd = Assert.Single(deck.Derivations);

        Assert.Equal(2, odd.Rest.Count);
        Assert.Equal(BooleanOperation.And, odd.Rest[0].Operation);
        Assert.Equal(BooleanOperation.Not, odd.Rest[1].Operation);
        Assert.Equal("psdm", odd.Rest[1].Operand);
    }

    [Fact]
    public void A_rule_carries_its_operands_value_and_description()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met1 140 ""Met1 spacing""");

        Assert.Empty(deck.Problems);
        Assert.Empty(deck.Refused);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal("met1.2", rule.Id);
        Assert.Equal(DrcCheck.Space, rule.Check);
        Assert.Equal("met1", Assert.Single(rule.Operands));
        Assert.Equal(140, rule.Value);
        Assert.Equal("Met1 spacing", rule.Description);
        Assert.True(deck.AllRulesUnderstood);
    }

    [Fact]
    public void Comments_and_blank_lines_are_skipped()
    {
        var deck = DrcDeck.Parse(@"#a deck

layer met1 68/20

#another word entirely
");

        Assert.Empty(deck.Problems);
        Assert.Single(deck.Layers);
    }

    #endregion **************************************************************************



    #region Refusing *******************************************************************

    ///<summary>
    ///The test this whole format exists for.
    ///
    ///A rule asking for a check this build cannot measure is written perfectly well - it is not a typo, and
    ///treating it as one would file it under "problems" beside a missing comma. It has to come back named,
    ///so a report can say which rules did not run.
    ///</summary>
    [Fact]
    public void An_unsupported_check_is_refused_by_name()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
rule poly.4 spaceparallel poly diff 75 ""Poly on field to diff, parallel edges only""");

        string refused = Assert.Single(deck.Refused);

        Assert.Contains("poly.4", refused);
        Assert.Contains("spaceparallel", refused);
    }

    [Fact]
    public void A_refused_rule_does_not_become_a_rule()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
rule poly.4 spaceparallel poly 75 ""Not measurable here""");

        Assert.Empty(deck.Rules);
    }

    ///<summary>A refusal is not a problem, and the two are counted apart on purpose.</summary>
    [Fact]
    public void A_refusal_is_not_filed_as_a_problem()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
rule poly.4 spaceparallel poly 75 ""Not measurable here""");

        Assert.Empty(deck.Problems);
        Assert.Single(deck.Refused);
    }

    ///<summary>
    ///Extension is refused, and it is worth a test of its own because it is the one that was implemented
    ///and then taken out.
    ///
    ///It read as Enclosure with its arguments swapped - the same six lines - measuring in every direction
    ///at once, where every extension rule a real deck carries is directional. Run against the bundled
    ///transistor it reported the sides of the channel for a rule about its ends, twice, and nothing else.
    ///A check whose whole output is the wrong axis is worse than one that says it did not look.
    ///</summary>
    [Fact]
    public void An_extension_rule_is_refused_because_direction_cannot_be_expressed()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
rule poly.8 extension poly diff 130 ""Poly endcap""");

        string refused = Assert.Single(deck.Refused);

        Assert.Contains("poly.8", refused);
        Assert.Empty(deck.Rules);
    }

    ///<summary>
    ///A rule may name the metric it is measured in, which puts it on the edge engine.
    ///
    ///`parallel` rather than `projection` as the word a deck writes, because that is what a rule manual
    ///says - sky130's poly.4 reads "parallel edges only", and a deck should be transcribable in the words
    ///it was written in.
    ///</summary>
    [Theory]
    [InlineData("parallel", DrcMetric.Projection)]
    [InlineData("projection", DrcMetric.Projection)]
    [InlineData("euclidean", DrcMetric.Euclidean)]
    [InlineData("square", DrcMetric.Square)]
    public void A_rule_may_name_the_metric_it_is_measured_in(string word, DrcMetric expected)
    {
        var deck = DrcDeck.Parse($@"layer poly 66/20
rule poly.4 space poly 75 {word} ""Poly spacing""");

        Assert.Empty(deck.Problems);

        Assert.Equal(expected, Assert.Single(deck.Rules).Metric);
    }

    ///<summary>Naming none is the ordinary case, and means the rule is measured by sizing.</summary>
    [Fact]
    public void A_rule_naming_no_metric_is_measured_by_sizing()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
rule poly.2 space poly 210 ""Poly spacing""");

        Assert.Null(Assert.Single(deck.Rules).Metric);
    }

    ///<summary>
    ///A metric on a check the edge engine cannot measure is refused rather than quietly ignored.
    ///
    ///The whole point of naming one is that the answer differs, so giving the sizing answer instead would
    ///be the single outcome nobody asked for - and it would look like the rule had run.
    ///</summary>
    [Fact]
    public void A_metric_on_a_check_with_no_edge_form_is_refused()
    {
        var deck = DrcDeck.Parse(@"layer hvtp 78/44
rule hvtp.5 area hvtp 265000 euclidean ""An area has no edges to pair""");

        Assert.Empty(deck.Rules);

        Assert.Contains("hvtp.5", Assert.Single(deck.Refused));
    }

    [Fact]
    public void A_deck_holding_a_refusal_is_not_all_understood()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
rule poly.1a width poly 150 ""Poly width""
rule poly.4 spaceparallel poly 75 ""Not measurable here""");

        Assert.Single(deck.Rules);
        Assert.False(deck.AllRulesUnderstood);
    }

    #endregion **************************************************************************



    #region Problems *******************************************************************

    [Fact]
    public void A_pair_that_is_not_two_numbers_is_reported()
    {
        var deck = DrcDeck.Parse("layer met1 68-20");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Layers);
    }

    [Fact]
    public void A_name_declared_twice_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
layer met1 69/20");

        Assert.Single(deck.Problems);
        Assert.Equal(new LayerKey(68, 20), deck.Layers["met1"]);
    }

    ///<summary>A derivation may not take a name a drawn layer already has, either.</summary>
    [Fact]
    public void A_derivation_may_not_reuse_a_layer_name()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
derive poly = poly and diff");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Derivations);
    }

    [Fact]
    public void A_value_of_zero_measures_nothing_and_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met1 0 ""Met1 spacing""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    [Fact]
    public void A_negative_value_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met1 -140 ""Met1 spacing""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    [Fact]
    public void A_derivation_ending_in_an_operation_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
derive gate = poly and diff not");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Derivations);
    }

    [Fact]
    public void A_line_that_starts_with_something_else_is_reported()
    {
        var deck = DrcDeck.Parse("check met1 140");

        Assert.Single(deck.Problems);
    }

    ///<summary>The same cap LayerNames uses, and for the same reason: a wrong delimiter throws one per line.</summary>
    [Fact]
    public void Problems_stop_being_listed_after_five()
    {
        var deck = DrcDeck.Parse(string.Join("\n", Enumerable.Repeat("layer met1 68-20", 20)));

        Assert.Equal(5, deck.Problems.Count);
    }

    #endregion **************************************************************************



    #region Resolving ******************************************************************

    ///<summary>
    ///A typo in a layer name parses perfectly and names nothing, which without this reaches the engine and
    ///finds no geometry - indistinguishable from a layer with no violations on it.
    ///</summary>
    [Fact]
    public void A_rule_naming_an_undeclared_layer_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met2 140 ""Met1 spacing""");

        Assert.Single(deck.Problems);
        Assert.Contains("met2", deck.Problems[0]);
    }

    [Fact]
    public void A_derivation_naming_an_undeclared_layer_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive gate = poly and diff");

        Assert.Single(deck.Problems);
        Assert.Contains("diff", deck.Problems[0]);
    }

    ///<summary>Resolved after the whole file is read, so writing a derivation above its input still works.</summary>
    [Fact]
    public void A_rule_may_name_a_derivation_written_below_it()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
rule poly.8 enclosure gate poly 130 ""Gate inside poly""
derive gate = poly and diff");

        Assert.Empty(deck.Problems);
        Assert.Single(deck.Rules);
    }

    ///<summary>Off-grid runs over everything drawn, and nothing declares a layer called that.</summary>
    [Fact]
    public void Off_grid_takes_every_layer_without_declaring_it()
    {
        var deck = DrcDeck.Parse(@"rule grid.1 offgrid * 5 ""Coordinate off the manufacturing grid""");

        Assert.Empty(deck.Problems);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal(DrcCheck.OffGrid, rule.Check);
        Assert.Equal(DrcDeck.EveryLayer, Assert.Single(rule.Operands));
    }

    ///<summary>
    ///And it carries the manufacturing grid as its value, because that number cannot be recovered.
    ///
    ///Grid.Of reads back the greatest common divisor of a library's coordinates, which the stray coordinate
    ///being looked for drags to one - so a grid read off the file is defined away by the very fault the
    ///rule exists to catch. It is PDK data, like every other value in a deck.
    ///</summary>
    [Fact]
    public void Off_grid_carries_the_grid_it_checks_against()
    {
        var deck = DrcDeck.Parse(@"rule grid.1 offgrid * 5 ""Off the 5nm grid""");

        Assert.Equal(5, Assert.Single(deck.Rules).Value);
    }

    [Fact]
    public void Off_grid_without_a_grid_is_reported()
    {
        var deck = DrcDeck.Parse(@"rule grid.1 offgrid * ""No grid given""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    #endregion **************************************************************************



    #region Descriptions and exemptions ************************************************

    ///<summary>
    ///A one-word description is quoted like any other, and the quoting is what says it is one.
    ///
    ///Guessing from a space instead reads this as a layer name, and the rule is then rejected for naming a
    ///layer that does not exist - a failure that points at the layer list, which is the wrong place to look.
    ///</summary>
    [Fact]
    public void A_one_word_description_is_not_mistaken_for_a_layer()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met1 140 ""Spacing""");

        Assert.Empty(deck.Problems);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal("Spacing", rule.Description);
        Assert.Equal("met1", Assert.Single(rule.Operands));
    }

    [Fact]
    public void A_rule_needs_no_description_at_all()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met1 140");

        Assert.Empty(deck.Problems);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal("", rule.Description);
        Assert.Equal(140, rule.Value);
    }

    [Fact]
    public void Except_names_the_layer_a_rule_does_not_apply_inside()
    {
        var deck = DrcDeck.Parse(@"layer diff 65/20
layer nwell 64/20
layer uhvi 97/20
rule difftap.8 enclosure diff nwell 180 except uhvi ""N-well enclosure of diff""");

        Assert.Empty(deck.Problems);

        var rule = Assert.Single(deck.Rules);

        Assert.Equal("uhvi", rule.Except);
        Assert.Equal(2, rule.Operands.Count);
        Assert.Equal(180, rule.Value);
    }

    ///<summary>
    ///An exemption dropped turns a passing layout into a failing one, so the rule goes rather than being
    ///kept with the modifier quietly discarded.
    ///</summary>
    [Fact]
    public void Except_with_no_layer_after_it_drops_the_rule()
    {
        var deck = DrcDeck.Parse(@"layer diff 65/20
layer nwell 64/20
rule difftap.8 enclosure diff nwell 180 except");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    [Fact]
    public void An_exemption_naming_an_undeclared_layer_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer diff 65/20
layer nwell 64/20
rule difftap.8 enclosure diff nwell 180 except uhvi ""N-well enclosure""");

        Assert.Single(deck.Problems);
        Assert.Contains("uhvi", deck.Problems[0]);
    }

    #endregion **************************************************************************



    #region How many layers a check takes **********************************************

    [Fact]
    public void Space_takes_one_layer_or_two()
    {
        var one = DrcDeck.Parse(@"layer met1 68/20
rule met1.2 space met1 140 ""One""");

        var two = DrcDeck.Parse(@"layer nsdm 93/44
layer diff 65/20
rule nsdm.2 space nsdm diff 130 ""Two""");

        Assert.Single(one.Rules);
        Assert.Single(two.Rules);
    }

    [Fact]
    public void Enclosure_needs_two_layers()
    {
        var deck = DrcDeck.Parse(@"layer diff 65/20
rule difftap.3 enclosure diff 180 ""Missing the enclosing layer""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    [Fact]
    public void Width_takes_only_one_layer()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
layer met2 69/20
rule met1.1 width met1 met2 140 ""Two layers for a width""");

        Assert.Single(deck.Problems);
        Assert.Empty(deck.Rules);
    }

    #endregion **************************************************************************



    #region The bundled deck ***********************************************************

    ///<summary>
    ///The sky130 starter deck that ships beside the sample layouts, read as the app would read it.
    ///
    ///**Worth pinning because the deck is documentation that has to stay executable.** It is transcribed by
    ///hand from a rule manual, it is the first thing anybody trying this feature will load, and nothing else
    ///would notice if an edit to it stopped parsing. Nothing here checks that the *values* are right - only
    ///a foundry can say that - but a deck that does not read is a deck nobody can even disagree with.
    ///</summary>
    [Fact]
    public void The_bundled_sky130_deck_reads_clean()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        Assert.Empty(deck.Problems);
        Assert.Empty(deck.Refused);
        Assert.True(deck.AllRulesUnderstood);

        Assert.Equal(21, deck.Layers.Count);
        Assert.Equal(6, deck.Derivations.Count);
        Assert.Equal(30, deck.Rules.Count);
    }

    ///<summary>The derived layers a real deck cannot do without, present and pointing at drawn layers.</summary>
    [Fact]
    public void The_bundled_deck_derives_the_gate_from_poly_and_diff()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        var gate = deck.Derivations.Single(derivation => derivation.Name == "gate");

        Assert.Equal("poly", gate.First);
        Assert.Equal(BooleanOperation.And, Assert.Single(gate.Rest).Operation);
        Assert.Equal("diff", Assert.Single(gate.Rest).Operand);
    }

    #endregion **************************************************************************

    ///
    ///Every rule remembers which line it was read from.
    ///
    ///**So that a rule can be taken back out.** A deck is its text - that is what gets exported, saved and
    ///read again - so removing a rule means removing its line and reading the deck afresh, rather than
    ///reaching into the parsed list and leaving the two disagreeing. The line is what says which one.
    ///
    [Fact]
    public void Every_rule_remembers_the_line_it_came_from()
    {
        var lines = new[]
        {
            "layer met1 68/20",
            "",
            "#a comment, which is not a line any rule came from",
            "rule met1.w width met1 140 \u0022Metal 1 width\u0022",
            "rule met1.s space met1 140 \u0022Metal 1 space\u0022"
        };

        var read = DrcDeck.Parse(string.Join("\n", lines));

        Assert.Empty(read.Problems);
        Assert.Equal(2, read.Rules.Count);

        //Counted from one, and past the blank and the comment - which is what makes it an index into the
        //text somebody is holding rather than into the rules that came out of it.
        Assert.Equal(4, read.Rules[0].Line);
        Assert.Equal(5, read.Rules[1].Line);

        //And the line it names is its own.
        Assert.Contains(read.Rules[0].Id, lines[read.Rules[0].Line - 1]);
        Assert.Contains(read.Rules[1].Id, lines[read.Rules[1].Line - 1]);
    }

    ///
    ///And taking that line out takes exactly that rule out, on a deck that repeats an id.
    ///
    ///The case matching by id would get wrong, and the deck somebody is most likely to be in the panel
    ///fixing.
    ///
    [Fact]
    public void Removing_a_rules_line_removes_that_rule_even_when_an_id_repeats()
    {
        var lines = new List<string>
        {
            "layer met1 68/20",
            "rule same width met1 140 \u0022The first\u0022",
            "rule same space met1 200 \u0022The second\u0022"
        };

        var read = DrcDeck.Parse(string.Join("\n", lines));

        Assert.Equal(2, read.Rules.Count);

        var second = read.Rules[1];

        lines.RemoveAt(second.Line - 1);

        var after = DrcDeck.Parse(string.Join("\n", lines));

        Assert.Single(after.Rules);
        Assert.Equal(DrcCheck.Width, after.Rules[0].Check);
        Assert.Equal("The first", after.Rules[0].Description);
    }
}
