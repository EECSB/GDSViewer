using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Covers DrcLayers, which turns the layers a deck names into the geometry a check will be measured over.
///
///**Area is what most of this asserts**, for the reason BooleanTests gives: a boolean's corner list depends
///on how the clipper happened to walk the input, where the area it encloses is the thing the operation is
///for and comes out the same either way.
///
///The rest is about ordering and refusal - that a derivation reading another is computed after it whichever
///order they were written in, that a circle of them is reported rather than followed round, and that a
///layer which could not be worked out is never confused with one the file simply draws nothing on.
///</summary>
public class DrcLayerTests
{
    #region Shapes to work with ******************************************************

    private static Element.Point At(int x, int y)
    {
        return new Element.Point { X = x, Y = y };
    }

    ///<summary>A closed box, the way a GDSII boundary writes one - first point repeated at the end.</summary>
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

    private static Element Shape(LayerKey key, List<Element.Point> points)
    {
        return new Element { Layer = new Layer(key, "#000000"), Points = points };
    }

    private static FlattenedLayout LayoutOf(params Element[] elements)
    {
        return new FlattenedLayout { Elements = elements.ToList() };
    }

    private static double Area(IEnumerable<IReadOnlyList<Element.Point>> shapes)
    {
        double total = 0;

        foreach (var shape in shapes)
            total += Measure.AreaOf(shape);

        return total;
    }

    private static readonly LayerKey Poly = new LayerKey(66, 20);
    private static readonly LayerKey Diff = new LayerKey(65, 20);
    private static readonly LayerKey Psdm = new LayerKey(94, 20);

    ///<summary>
    ///Poly over the left square, diffusion over one shifted half its width right - so they cross in a
    ///50 by 100 band, which is the gate.
    ///</summary>
    private static FlattenedLayout CrossingLayout()
    {
        return LayoutOf(
            Shape(Poly, Box(0, 0, 100, 100)),
            Shape(Diff, Box(50, 0, 150, 100)));
    }

    private const string CrossingDeck = @"layer poly 66/20
layer diff 65/20";

    #endregion **************************************************************************



    #region Drawn layers ****************************************************************

    [Fact]
    public void A_drawn_layer_comes_back_as_its_geometry()
    {
        var deck = DrcDeck.Parse(CrossingDeck + "\nrule poly.1a width poly 150 \"Poly width\"");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal(10000, Area(layers.Of("poly")));
        Assert.True(layers.IsResolved("poly"));
    }

    ///<summary>Two shapes that overlap are one region afterwards, or every area measured off it is wrong.</summary>
    [Fact]
    public void A_drawn_layer_is_merged_rather_than_left_overlapping()
    {
        var layout = LayoutOf(
            Shape(Poly, Box(0, 0, 100, 100)),
            Shape(Poly, Box(50, 0, 150, 100)));

        var deck = DrcDeck.Parse("layer poly 66/20\nrule poly.1a width poly 150 \"Poly width\"");

        var layers = DrcLayers.Resolve(deck, layout);

        //15000 merged, against 20000 if the two were simply added up.
        Assert.Equal(15000, Area(layers.Of("poly")));
    }

    ///<summary>
    ///A layer the deck declares and the file does not carry is empty and *resolved*. A width check over
    ///nothing correctly finds nothing, and calling that a failure would report every deck against every
    ///cell that does not use all of it.
    ///</summary>
    [Fact]
    public void A_layer_the_file_does_not_carry_is_empty_but_resolved()
    {
        var deck = DrcDeck.Parse("layer met5 72/20\nrule met5.1 width met5 1600 \"Met5 width\"");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Empty(layers.Of("met5"));
        Assert.True(layers.IsResolved("met5"));
        Assert.True(layers.AllLayersResolved);
        Assert.Empty(layers.RulesLeftUnmeasurable());
    }

    ///<summary>A label is an anchor and a string, and unioning one in would put area where there is none.</summary>
    [Fact]
    public void A_label_takes_no_part()
    {
        var label = Shape(Poly, new List<Element.Point> { At(10, 10) });

        label.Text = "VDD";

        var layout = LayoutOf(Shape(Poly, Box(0, 0, 100, 100)), label);

        var deck = DrcDeck.Parse("layer poly 66/20\nrule poly.1a width poly 150 \"Poly width\"");

        Assert.Equal(10000, Area(DrcLayers.Resolve(deck, layout).Of("poly")));
    }

    ///<summary>A zero-width path is a centerline, which encloses nothing however it is drawn.</summary>
    [Fact]
    public void An_open_run_takes_no_part()
    {
        var open = Shape(Poly, Box(200, 200, 300, 300));

        open.IsOpen = true;

        var layout = LayoutOf(Shape(Poly, Box(0, 0, 100, 100)), open);

        var deck = DrcDeck.Parse("layer poly 66/20\nrule poly.1a width poly 150 \"Poly width\"");

        Assert.Equal(10000, Area(DrcLayers.Resolve(deck, layout).Of("poly")));
    }

    ///<summary>A deck may give one pair two names, and both have to answer with the geometry.</summary>
    [Fact]
    public void Two_names_for_one_pair_both_carry_its_geometry()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
layer gatepoly 66/20
rule poly.1a width poly 150 ""Poly width""
rule poly.2 space gatepoly 210 ""Poly spacing""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal(10000, Area(layers.Of("poly")));
        Assert.Equal(10000, Area(layers.Of("gatepoly")));
    }

    #endregion **************************************************************************



    #region Deriving ********************************************************************

    ///<summary>The one every PDK needs: where polysilicon crosses diffusion is a transistor gate.</summary>
    [Fact]
    public void A_gate_is_where_poly_crosses_diff()
    {
        var deck = DrcDeck.Parse(CrossingDeck + @"
derive gate = poly and diff
rule poly.8 enclosure gate poly 130 ""Gate inside poly""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        //The 50 by 100 band the two squares share.
        Assert.Equal(5000, Area(layers.Of("gate")));
    }

    [Fact]
    public void Field_poly_is_poly_with_the_gate_taken_out()
    {
        var deck = DrcDeck.Parse(CrossingDeck + @"
derive fieldpoly = poly not diff
rule poly.4 space fieldpoly 75 ""Field poly spacing""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal(5000, Area(layers.Of("fieldpoly")));
    }

    ///<summary>
    ///Left to right with no precedence, which is what the format promises: `poly and diff not psdm` is the
    ///gate with psdm taken off it, not poly intersected with whatever the right-hand side works out to.
    ///</summary>
    [Fact]
    public void A_chain_folds_left_to_right()
    {
        var layout = LayoutOf(
            Shape(Poly, Box(0, 0, 100, 100)),
            Shape(Diff, Box(50, 0, 150, 100)),
            Shape(Psdm, Box(75, 0, 200, 100)));

        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
layer psdm 94/20
derive band = poly and diff not psdm
rule made.1 width band 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, layout);

        //poly and diff is x 50..100; taking psdm from x 75 leaves x 50..75, a 25 by 100 band.
        Assert.Equal(2500, Area(layers.Of("band")));
    }

    ///<summary>
    ///A derivation may read another, and the order they are computed in is the order they depend on rather
    ///than the order they were written in - so this writes the reader first on purpose.
    ///</summary>
    [Fact]
    public void A_derivation_reading_another_is_computed_after_it()
    {
        var layout = LayoutOf(
            Shape(Poly, Box(0, 0, 100, 100)),
            Shape(Diff, Box(50, 0, 150, 100)),
            Shape(Psdm, Box(75, 0, 200, 100)));

        var deck = DrcDeck.Parse(@"layer poly 66/20
layer diff 65/20
layer psdm 94/20
derive trimmed = gate not psdm
derive gate = poly and diff
rule made.1 width trimmed 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, layout);

        Assert.Empty(layers.Problems);
        Assert.Equal(2500, Area(layers.Of("trimmed")));
    }

    #endregion **************************************************************************



    #region Circles and names that go nowhere ****************************************

    ///<summary>
    ///A circle of derivations cannot be computed and has to be said so. Followed round it would either
    ///never finish or quietly come back empty, and empty is what a clean report looks like.
    ///</summary>
    [Fact]
    public void A_circle_of_derivations_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive one = two and poly
derive two = one and poly
rule made.1 width one 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.NotEmpty(layers.Problems);
        Assert.False(layers.AllLayersResolved);
    }

    [Fact]
    public void A_derivation_in_a_circle_is_not_resolved()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive one = two and poly
derive two = one and poly
rule made.1 width one 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.False(layers.IsResolved("one"));
        Assert.False(layers.IsResolved("two"));
    }

    ///<summary>The point of all of it: the rule is nameable rather than coming back with nothing to report.</summary>
    [Fact]
    public void A_rule_reading_a_circle_is_named_as_unmeasurable()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive one = two and poly
derive two = one and poly
rule made.1 width one 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal("made.1", Assert.Single(layers.RulesLeftUnmeasurable()));
    }

    ///<summary>A derivation reading itself directly is the same fault with one name in it.</summary>
    [Fact]
    public void A_derivation_reading_itself_is_reported()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive loop = loop and poly
rule made.1 width loop 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.NotEmpty(layers.Problems);
        Assert.False(layers.IsResolved("loop"));
    }

    ///<summary>
    ///A typo names nothing, and everything reading it is carried along - otherwise the rule measures an
    ///empty layer and reports it clean.
    ///</summary>
    [Fact]
    public void A_derivation_reading_a_name_that_goes_nowhere_leaves_its_rule_unmeasurable()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive gate = poly and dff
rule made.1 width gate 100 ""Made up""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.False(layers.IsResolved("gate"));
        Assert.Equal("made.1", Assert.Single(layers.RulesLeftUnmeasurable()));
    }

    ///<summary>A rule whose layers are all fine is not dragged down by another rule's being broken.</summary>
    [Fact]
    public void A_sound_rule_beside_a_broken_one_stays_measurable()
    {
        var deck = DrcDeck.Parse(@"layer poly 66/20
derive gate = poly and dff
rule made.1 width gate 100 ""Broken""
rule poly.1a width poly 150 ""Sound""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal("made.1", Assert.Single(layers.RulesLeftUnmeasurable()));
        Assert.Equal(10000, Area(layers.Of("poly")));
    }

    #endregion **************************************************************************



    #region Only what is asked for ***************************************************

    ///<summary>
    ///A derivation nothing leads to is not computed, because working it out is a clipping pass over the
    ///whole layout for an answer nobody asked for. A deck carrying derivations ahead of the rules that
    ///will use them is a normal thing to write.
    ///</summary>
    [Fact]
    public void A_derivation_no_rule_reaches_is_not_computed()
    {
        var deck = DrcDeck.Parse(CrossingDeck + @"
derive gate = poly and diff");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Empty(layers.Of("gate"));
        Assert.Empty(layers.Problems);
    }

    ///<summary>And the same derivation is computed the moment a rule leads to it.</summary>
    [Fact]
    public void The_same_derivation_is_computed_once_a_rule_reaches_it()
    {
        var deck = DrcDeck.Parse(CrossingDeck + @"
derive gate = poly and diff
rule poly.8 enclosure gate poly 130 ""Gate inside poly""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal(5000, Area(layers.Of("gate")));
    }

    ///<summary>A layer reached only through the exemption on a rule is still reached.</summary>
    [Fact]
    public void A_layer_named_only_by_an_exemption_is_computed()
    {
        var deck = DrcDeck.Parse(CrossingDeck + @"
derive gate = poly and diff
rule poly.1a width poly 150 except gate ""Poly width, not under the gate""");

        var layers = DrcLayers.Resolve(deck, CrossingLayout());

        Assert.Equal(5000, Area(layers.Of("gate")));
    }

    #endregion **************************************************************************



    #region The bundled deck ***********************************************************

    ///<summary>
    ///The sky130 starter deck resolved against a real layout.
    ///
    ///Most of its twenty-one layers are not in this one cell, and that is the point: they come back empty
    ///and resolved, no rule is left unmeasurable, and nothing is reported. A deck is written for a process
    ///rather than for a file, so a cell using a handful of its layers is the normal case rather than a
    ///fault.
    ///</summary>
    [Fact]
    public void The_bundled_sky130_deck_resolves_against_a_bundled_layout()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        var gds = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var layers = DrcLayers.Resolve(deck, GdsFlattener.Flatten(gds));

        Assert.Empty(layers.Problems);
        Assert.True(layers.AllLayersResolved);
        Assert.Empty(layers.RulesLeftUnmeasurable());
    }

    ///<summary>
    ///The derived layer the deck's rules actually read, computed.
    ///
    ///`difftap.8` is an enclosure of P+ diffusion - `diff and psdm`, a layer nobody draws - by nwell, which
    ///is the whole argument for derivations being load-bearing rather than an advanced feature.
    ///</summary>
    [Fact]
    public void The_bundled_deck_computes_the_derived_layer_its_rules_read()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        var gds = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var layers = DrcLayers.Resolve(deck, GdsFlattener.Flatten(gds));

        Assert.True(layers.IsResolved("pdiff"));
        Assert.Empty(layers.Problems);
    }

    ///<summary>
    ///The gate is derived and deliberately not computed, because no rule in the deck reaches it.
    ///
    ///**Laziness working rather than a gap.** The two rules that read it were the extension pair, and both
    ///were taken out when running them against this very file showed that every violation they produced was
    ///the axis the rule does not mean. The derivation stays in the deck ready for a rule that can express
    ///itself, and until one exists it costs no clipping pass at all.
    ///</summary>
    [Fact]
    public void The_bundled_deck_leaves_a_derivation_no_rule_reads_uncomputed()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        var gds = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var layers = DrcLayers.Resolve(deck, GdsFlattener.Flatten(gds));

        Assert.Contains(deck.Derivations, derivation => derivation.Name == "gate");

        //Declared, and never worked out, because nothing asked.
        Assert.Empty(layers.Of("gate"));
    }

    #endregion **************************************************************************
}
