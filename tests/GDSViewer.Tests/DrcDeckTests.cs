using System.Globalization;
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

    ///<summary>
    ///The grid a layer is drawn on, from the rules that check it: `*` for every layer the deck declares, a name
    ///for the layers it is drawn on - a derivation's all the way down - and every such rule at once.
    ///
    ///What a tool asks before it works out a corner, so the corner lands where the check will look for it.
    ///</summary>
    [Fact]
    public void A_layers_grid_is_what_every_off_grid_rule_on_it_asks_for_at_once()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
layer met2 69/20
layer poly 66/20
layer diff 65/20
derive gate = poly and diff
rule grid.1 offgrid * 5 ""Manufacturing grid""
rule met2.grid offgrid met2 2 ""Met2 on even units""
rule gate.grid offgrid gate 10");

        Assert.Empty(deck.Problems);

        Assert.Equal(5, deck.GridFor(new LayerKey(68, 20)));
        Assert.Equal(10, deck.GridFor(new LayerKey(69, 20)));
        Assert.Equal(10, deck.GridFor(new LayerKey(66, 20)));
        Assert.Equal(10, deck.GridFor(new LayerKey(65, 20)));
        Assert.Equal(new[] { "grid.1", "met2.grid" }, deck.GridRulesFor(new LayerKey(69, 20)).Select(rule => rule.Id));
    }

    ///<summary>A layer the deck does not declare is one the check does not look at, so there is no grid to draw it on.</summary>
    [Fact]
    public void A_layer_the_deck_does_not_declare_has_no_grid()
    {
        var deck = DrcDeck.Parse(@"layer met1 68/20
rule grid.1 offgrid * 5");

        Assert.Equal(1, deck.GridFor(new LayerKey(70, 20)));
        Assert.Empty(deck.GridRulesFor(new LayerKey(70, 20)));
        Assert.Equal(1, DrcDeck.Parse("layer met1 68/20").GridFor(new LayerKey(68, 20)));
    }

    ///<summary>The drawn layers behind a derivation come from the names alone, so they can be asked with nothing drawn.</summary>
    [Fact]
    public void The_layers_behind_a_derivation_are_the_decks_to_say()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
derive gate = poly and diff
derive either = gate or poly");

        Assert.Equal(new[] { new LayerKey(66, 20), new LayerKey(65, 20) }, deck.DrawnBehind("either"));
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

    ///<summary>Its one grid rule puts every layer it declares on five units - which is what the assistant puts curves on.</summary>
    [Fact]
    public void The_bundled_deck_puts_every_layer_it_declares_on_five_units()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        foreach (var key in deck.Layers.Values)
            Assert.Equal(5, deck.GridFor(key));
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

    ///<summary>
    ///The gf180mcu deck that ships beside sky130's (2026-11-02), read as the app reads it, for the reason above - and its
    ///layers are the layermap's own, under the same names on the same pairs, so what the rules check is what the layer
    ///panel names. Every layer it declares is on the 5 nm grid.
    ///</summary>
    [Fact]
    public void The_bundled_gf180mcu_deck_reads_clean()
    {
        var deck = DrcDeck.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "gf180mcuD.drc")));

        Assert.Empty(deck.Problems);
        Assert.Empty(deck.Refused);
        Assert.True(deck.AllRulesUnderstood);

        Assert.Equal(19, deck.Layers.Count);
        Assert.Equal(2, deck.Derivations.Count);
        Assert.Equal(56, deck.Rules.Count);

        var names = LayerNames.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "gf180mcu-roles.csv")));

        foreach (var layer in deck.Layers)
        {
            Assert.Equal(layer.Key, names.Names[layer.Value]);
            Assert.Equal(5, deck.GridFor(layer.Value));
        }
    }

    ///<summary>Its top metal is Metal5 under the MetalTop rules - 0.44 and 0.46 - where the metals below it are 0.28.</summary>
    [Fact]
    public void The_gf180mcu_decks_top_metal_follows_the_MetalTop_rules()
    {
        var deck = DrcDeck.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "gf180mcuD.drc")));

        Assert.Equal(new LayerKey(81, 0), deck.Layers["Metal5"]);
        Assert.Equal(440, deck.Rules.Single(rule => rule.Id == "MT.1").Value);
        Assert.Equal(460, deck.Rules.Single(rule => rule.Id == "MT.2a").Value);
        Assert.Equal(280, deck.Rules.Single(rule => rule.Id == "M4.1").Value);
    }

    ///<summary>
    ///The ihp-sg13g2 deck the same way (2026-11-02): read clean, its layers the layermap's own, all on the 5 nm grid.
    ///</summary>
    [Fact]
    public void The_bundled_ihp_sg13g2_deck_reads_clean()
    {
        var deck = DrcDeck.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "sg13g2.drc")));

        Assert.Empty(deck.Problems);
        Assert.Empty(deck.Refused);
        Assert.True(deck.AllRulesUnderstood);

        Assert.Equal(22, deck.Layers.Count);
        Assert.Single(deck.Derivations);
        Assert.Equal(68, deck.Rules.Count);

        var names = LayerNames.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "ihp-sg13g2-roles.csv")));

        foreach (var layer in deck.Layers)
        {
            Assert.Equal(layer.Key, names.Names[layer.Value]);
            Assert.Equal(5, deck.GridFor(layer.Value));
        }
    }

    ///
    ///**Every rule in it is IHP's, at IHP's value**: its id is in the SG13G2 rule tables the PDK publishes as text,
    ///main_rules.md and extra_rules.md, vendored at OtherResources/IHP - and its value is that table's, in microns or
    ///square microns there and nanometers or square nanometers here. A value mistyped, or a rule invented under an id
    ///that sounds right, fails here. The grid rule is the one id of the deck's own, stated in rules 3.1 rather than in
    ///a table.
    ///
    [Fact]
    public void Every_ihp_sg13g2_rule_is_ihps_own_at_ihps_value()
    {
        var deck = DrcDeck.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "sg13g2.drc")));
        var tables = new Dictionary<string, double>();

        foreach (string table in new[] { "main_rules.md", "extra_rules.md" })
        {
            foreach (string line in File.ReadAllLines(Path.Combine(GdsTestData.RepositoryRoot, "OtherResources", "IHP", table)))
            {
                string[] cells = line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray();

                if (cells.Length < 3 || !double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    continue;

                tables.TryAdd(cells[0], value);
            }
        }

        int held = 0;

        foreach (var rule in deck.Rules.Where(rule => rule.Id != "grid.1"))
        {
            Assert.True(tables.TryGetValue(rule.Id, out double microns), $"{rule.Id} is not in IHP's rule tables");

            double scale = 1000;

            if (rule.Check == DrcCheck.Area || rule.Check == DrcCheck.HoleArea)
                scale = 1_000_000;

            Assert.True(Math.Round(microns * scale) == rule.Value, $"{rule.Id} is {rule.Value} here and {microns} in IHP's table");

            held++;
        }

        Assert.Equal(67, held);
    }

    ///
    ///**Its width and space rules measure as distance on exactly the layers IHP allows at 45 degrees**, as IHP's own
    ///rules do - Activ, GatPoly, the five metals and the two top metals (rules 3.1) - and as squares everywhere else.
    ///Measured as squares, a 45-degree line reads 1.41 times nearer its neighbor than it is, and IHP's own sg13g2_tielo
    ///turns its poly that way; the deck's header has the measurements. Pinned so the choice stays one.
    ///
    [Fact]
    public void The_ihp_sg13g2_deck_measures_its_45_degree_layers_as_distance()
    {
        var deck = DrcDeck.Parse(File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "sg13g2.drc")));
        var angled = new[] { "Activ", "GatPoly", "Metal1", "Metal2", "Metal3", "Metal4", "Metal5", "TopMetal1", "TopMetal2" };
        int asDistance = 0;

        foreach (var rule in deck.Rules)
        {
            bool sizedOnce = (rule.Check == DrcCheck.Width || rule.Check == DrcCheck.Space) && rule.Operands.Count == 1;

            if (sizedOnce && angled.Contains(rule.Operands[0]))
            {
                Assert.Equal(DrcMetric.Euclidean, rule.Metric);

                asDistance++;
            }
            else
            {
                Assert.Null(rule.Metric);
            }
        }

        Assert.Equal(18, asDistance);
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

    ///<summary>
    ///Every rule line out - one this build refuses too, and one written in capitals, since the reader takes
    ///the keyword either way - and the rest of the deck as it was written: the layers and the derivation the
    ///next rule will name, and the comment.
    ///</summary>
    [Fact]
    public void Taking_every_rule_out_leaves_the_rest_of_the_deck()
    {
        string text = string.Join(
            "\n",
            "# a deck",
            "layer poly 66/20",
            "layer diff 65/20",
            "derive gate = poly and diff",
            "rule poly.1 width poly 150 \"Poly width\"",
            "  RULE poly.4 spaceparallel poly 75 \"Not measurable here\"",
            "");

        string left = DrcDeck.WithoutRules(text);

        Assert.Equal(string.Join("\n", "# a deck", "layer poly 66/20", "layer diff 65/20", "derive gate = poly and diff", ""), left);

        var deck = DrcDeck.Parse(left);

        Assert.Empty(deck.Rules);
        Assert.Empty(deck.Refused);
        Assert.Equal(2, deck.Layers.Count);
    }

    #region Writing ********************************************************************

    ///
    ///Every kind of rule the PDK manager's form makes, read and written back out as the line it was read from - the
    ///check, its layers, the value, the description and each qualifier, in the order the bundled deck writes them. That
    ///is what lets the form write into the deck's text, which is what a deck is, rather than keep a list beside it.
    ///
    [Theory]
    [InlineData("rule met1.1 width met1 140 \"Met1 width\"")]
    [InlineData("rule met1.2 space met1 140 \"Met1 spacing\"")]
    [InlineData("rule m12 space met1 met2 100 \"\"")]
    [InlineData("rule nwell.7 notch nwell 1270 \"N-well notch\"")]
    [InlineData("rule difftap.8 enclosure diff nwell 180 \"N-well enclosure of diffusion\"")]
    [InlineData("rule met1.6 area met1 83000 \"Met1 area\"")]
    [InlineData("rule met1.7 holearea met1 140000 \"Met1 hole area\"")]
    [InlineData("rule met1.d density met1 300 \"Met1 fill\" window 100000 step 50000")]
    [InlineData("rule ant.1 antenna met1 poly 400 \"Met1 antenna\"")]
    [InlineData("rule grid.1 offgrid * 5 \"Off the grid\"")]
    [InlineData("rule poly.4 space poly 75 \"Poly spacing\" parallel")]
    [InlineData("rule met1.w width met1 140 \"Met1 width\" euclidean")]
    [InlineData("rule licon.9 space met1 200 \"Licon spacing\" except nwell")]
    public void A_rule_written_out_is_the_line_it_was_read_from(string line)
    {
        string layers = "layer met1 68/20\nlayer met2 69/20\nlayer nwell 64/20\nlayer diff 65/20\nlayer poly 66/20\n";

        var read = DrcDeck.Parse(layers + line);

        Assert.Empty(read.Problems);

        Assert.Equal(line, DrcDeck.WriteRule(Assert.Single(read.Rules)));
    }

    ///<summary>The reader takes every double quote as the start or the end of a description, so one inside it cannot be written.</summary>
    [Fact]
    public void A_quote_in_a_description_is_written_as_an_apostrophe()
    {
        var rule = new DrcRule { Id = "m1", Check = DrcCheck.Width, Operands = new[] { "met1" }, Value = 140, Description = "The \"thin\" one" };

        string line = DrcDeck.WriteRule(rule);

        Assert.Equal("rule m1 width met1 140 \"The 'thin' one\"", line);
        Assert.Equal("The 'thin' one", DrcDeck.Parse("layer met1 68/20\n" + line).Rules[0].Description);
    }

    ///<summary>A rule's line replaced, or taken out, where it is - the rest of the deck as it was written, comments and all.</summary>
    [Fact]
    public void A_line_is_replaced_or_taken_out_where_it_is()
    {
        string text = "# a deck\r\nlayer met1 68/20\r\nrule a width met1 140 \"A\"\r\n# the end\r\n";

        Assert.Equal("# a deck\nlayer met1 68/20\nrule a width met1 150 \"A\"\n# the end\n", DrcDeck.WithLine(text, 3, "rule a width met1 150 \"A\""));
        Assert.Equal("# a deck\nlayer met1 68/20\n# the end\n", DrcDeck.WithLine(text, 3, null));
    }

    [Fact]
    public void A_line_the_deck_does_not_have_changes_nothing()
    {
        string text = "layer met1 68/20\nrule a width met1 140 \"A\"";

        Assert.Equal(text, DrcDeck.WithLine(text, 0, "x"));
        Assert.Equal(text, DrcDeck.WithLine(text, 3, "x"));
    }

    ///<summary>Under the last rule, so it lands among the rules - not under the list of rules left out that closes the bundled deck.</summary>
    [Fact]
    public void A_rule_added_lands_under_the_last_rule()
    {
        string text = string.Join("\n", "layer met1 68/20", "rule a width met1 140 \"A\"", "", "# Not here: poly.2", "");

        string added = DrcDeck.WithRuleAdded(text, "rule b space met1 140 \"B\"");

        Assert.Equal(string.Join("\n", "layer met1 68/20", "rule a width met1 140 \"A\"", "rule b space met1 140 \"B\"", "", "# Not here: poly.2", ""), added);
        Assert.Equal(2, DrcDeck.Parse(added).Rules.Count);
    }

    [Fact]
    public void A_rule_added_to_a_deck_with_none_goes_at_its_end()
    {
        Assert.Equal("layer met1 68/20\nrule b width met1 140 \"B\"\n", DrcDeck.WithRuleAdded("layer met1 68/20\n\n", "rule b width met1 140 \"B\""));
        Assert.Equal("rule b width met1 140 \"B\"\n", DrcDeck.WithRuleAdded("", "rule b width met1 140 \"B\""));
    }

    ///
    ///Lines an AI model wrote, each put where its kind goes: a layer under the layers with the comment over it kept over
    ///it, a derive under the derives, a rule under the rules - and each kind in the order it came.
    ///
    [Fact]
    public void Lines_added_each_land_where_their_kind_goes()
    {
        string text = string.Join("\n", "layer met1 68/20", "derive wide = met1 and met1", "rule a width met1 140 \"A\"", "", "# Not here: poly.2", "");

        string added = DrcDeck.WithLinesAdded(text, new[]
        {
            "",
            "# Met9, from the manual",
            "layer met9 99/20",
            "rule m9.1 width met9 300 \"Met9 width\"",
            "derive thick = met9 and met1",
            "rule m9.2 space met9 300 \"Met9 spacing\"",
            "  # left out: m9.3, needs an extension check"
        });

        Assert.Equal(string.Join("\n",
            "layer met1 68/20",
            "# Met9, from the manual",
            "layer met9 99/20",
            "derive wide = met1 and met1",
            "derive thick = met9 and met1",
            "rule a width met1 140 \"A\"",
            "rule m9.1 width met9 300 \"Met9 width\"",
            "rule m9.2 space met9 300 \"Met9 spacing\"",
            "# left out: m9.3, needs an extension check",
            "",
            "# Not here: poly.2",
            ""), added);

        var parsed = DrcDeck.Parse(added);

        Assert.Equal(3, parsed.Rules.Count);
        Assert.Empty(parsed.Problems);
    }

    ///<summary>Into a deck with none of a kind: layers ahead of everything, a derive under them, rules at the end.</summary>
    [Fact]
    public void Lines_added_to_an_empty_deck_come_out_in_the_order_the_reader_needs()
    {
        string added = DrcDeck.WithLinesAdded("", new[] { "rule a width met1 140 \"A\"", "derive m = met1 and met1", "layer met1 68/20" });

        Assert.Equal(string.Join("\n", "layer met1 68/20", "derive m = met1 and met1", "rule a width met1 140 \"A\"", ""), added);
        Assert.Single(DrcDeck.Parse(added).Rules);
    }

    ///<summary>A layer the form offered from the PDK's own, declared under the deck's last layer - or ahead of everything in a deck with none.</summary>
    [Fact]
    public void A_layer_declared_lands_under_the_last_layer()
    {
        string text = string.Join("\n", "# a deck", "layer met1 68/20", "derive both = met1 or met1", "rule a width met1 140 \"A\"");

        Assert.Equal(
            string.Join("\n", "# a deck", "layer met1 68/20", "layer met2 69/20", "derive both = met1 or met1", "rule a width met1 140 \"A\""),
            DrcDeck.WithLayerDeclared(text, "met2", new LayerKey(69, 20)));

        Assert.Equal("layer met2 69/20\nrule a width met2 140 \"A\"", DrcDeck.WithLayerDeclared("rule a width met2 140 \"A\"", "met2", new LayerKey(69, 20)));
    }

    #endregion **************************************************************************
}
