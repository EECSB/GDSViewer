using GdsII;

namespace GDSViewer.Tests;

///<summary>
///A layer that merges into one connected outline with many holes in it - a mesh of metal, a supply grid, a
///guard ring with a circuit inside - checked in a fraction of a second rather than in minutes.
///
///**The case that took 190 seconds over a single area rule.** An analog PLL's Metal1 merges into one
///outline carrying 2,516 holes, and the engine kept every layer the way GDSII has to hold it: each hole
///folded into its outline as a keyhole. Every check then handed that outline back to Clipper, which takes
///each channel apart again - and building its tree, it decides which side of each channel is inside the
///other by walking corners of one round the whole of the other, where the two sides share the channel and
///keep answering "on the boundary". Every Metal1 and Metal2 rule took minutes; every other rule in the deck
///took half a second. A layer is kept as rings now, its holes holes - see DrcLayers.RingsOf.
///
///**Built here rather than read from that layout**, which is somebody else's and is not in the repository. A
///mesh of bars is the same shape of problem - one outline, and a hole in every cell of it - and its cost
///climbed the same way: before the fix, the area rule took a fifteenth of a second over a mesh of 100 holes,
///1.4 seconds over 400, 33 over 900 and 512 over the 1,600 here, and the other rules about as long.
///It is that big so the gap between a fixed engine and a regressed one stays wide on any machine.
///
///**And some of the answers were wrong.** Merged back from its keyholed outline, a mesh came out with
///channels left in it: some holes glued to their neighbor along the channel between them, and some channels
///left as slits in the outline. At 900 holes the hole area rule found 803, and the edge engine found 54 width
///faults where every bar is wider than the limit - each the two sides of a leftover channel, lying on top of
///each other. The counts below are asserted as well as the time.
///
///**Each rule gets a deadline rather than a timing**, and a generous one. What is pinned is that nothing goes
///back to minutes, and a deadline thirty times what the slowest rule here takes fails for that and for nothing
///else.
///</summary>
public class DrcHoleTests
{
    #region A mesh **********************************************************************

    ///<summary>Holes along each side, so the mesh has this many squared.</summary>
    private const int Across = 40;

    private const int Hole = 1000;
    private const int Bar = 300;
    private const int Pitch = Hole + Bar;

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(20);

    private static List<Element.Point> Box(int left, int bottom, int right, int top)
    {
        return new List<Element.Point>
        {
            new Element.Point { X = left, Y = bottom },
            new Element.Point { X = right, Y = bottom },
            new Element.Point { X = right, Y = top },
            new Element.Point { X = left, Y = top },
            new Element.Point { X = left, Y = bottom }
        };
    }

    ///<summary>
    ///Forty-one bars across and forty-one up, so the metal merges into a single outline around sixteen hundred
    ///square holes - and a via at every crossing, so an enclosure has something to measure.
    ///
    ///Drawn as long overlapping bars on purpose. The holes are not drawn anywhere: they are what the merge
    ///finds, which is how a real layer comes to have them.
    ///</summary>
    private static FlattenedLayout Mesh()
    {
        var metal = new Layer(new LayerKey(34, 0), "#000000");
        var via = new Layer(new LayerKey(35, 0), "#000000");

        var elements = new List<Element>();
        int length = (Across * Pitch) + Bar;

        for (int i = 0; i <= Across; i++)
        {
            elements.Add(new Element { Layer = metal, Points = Box(0, i * Pitch, length, (i * Pitch) + Bar) });
            elements.Add(new Element { Layer = metal, Points = Box(i * Pitch, 0, (i * Pitch) + Bar, length) });
        }

        for (int i = 0; i <= Across; i++)
        {
            for (int j = 0; j <= Across; j++)
            {
                int x = (j * Pitch) + (Bar / 2);
                int y = (i * Pitch) + (Bar / 2);

                elements.Add(new Element { Layer = via, Points = Box(x - 50, y - 50, x + 50, y + 50) });
            }
        }

        return new FlattenedLayout { Elements = elements };
    }

    private const string Layers = @"layer met 34/0
layer via 35/0
";

    #endregion **************************************************************************



    #region Every rule, in seconds ******************************************************

    ///<summary>
    ///Every check that reads a layer's geometry, over the mesh, each well inside its deadline.
    ///
    ///One rule a run so a regression names the check it is in. The answers matter as much as the time: the
    ///bars are wider than every width and the holes wider than every spacing, so only two rules find anything.
    ///The hole area finds all sixteen hundred holes, which is what says they were kept as holes rather than lost.
    ///And the density finds every one of its sixteen windows: each lines up with ten bars and ten holes each way,
    ///so each is 69/169 metal, 40.8%, against a rule asking for 42% - a window that counted a single hole as metal
    ///would pass and drop out of the count.
    ///</summary>
    [Theory]
    [InlineData("rule m.1 width met 230 \"Width\"", 0)]
    [InlineData("rule m.2 space met 230 \"Spacing\"", 0)]
    [InlineData("rule m.3 area met 144400 \"Area\"", 0)]
    [InlineData("rule m.4 holearea met 2000000 \"Hole area\"", Across * Across)]
    [InlineData("rule m.5 notch met 230 \"Notch\"", 0)]
    [InlineData("rule m.6 density met 420 window 13000 step 13000 \"Density\"", 16)]
    [InlineData("rule m.7 width met 230 euclidean \"Width, edge pairs\"", 0)]
    [InlineData("rule m.8 space met 230 parallel \"Spacing, edge pairs\"", 0)]
    [InlineData("rule v.1 enclosure via met 10 \"Enclosure of via\"", 0)]
    public async Task A_rule_over_one_outline_with_many_holes_finishes_in_seconds(string rule, int expected)
    {
        var deck = DrcDeck.Parse(Layers + rule);
        var layout = Mesh();

        Assert.Empty(deck.Problems);

        var result = await Task.Run(() => Drc.Check(deck, layout)).WaitAsync(Deadline);

        Assert.True(result.Complete, string.Join("; ", result.NotRun.Concat(result.Problems)));
        Assert.Equal(expected, result.Violations.Count);
    }

    ///<summary>
    ///The library's own density sweep over the same mesh, which a deck's rule does not go through: every window
    ///of 13,000 units holds ten bars and ten holes each way, so the emptiest and the fullest are both 69/169.
    ///
    ///**Pinned because it was wrong.** With the layer merged keyholed, a window clipped out of it could count a
    ///hole as metal: here the fullest came back as 49.1%, and on the PLL's COMP a window that is 7.1% metal came
    ///back as 82.8%.
    ///</summary>
    [Fact]
    public async Task A_layers_density_range_is_the_same_in_every_window_of_the_mesh()
    {
        var layout = Mesh();

        var range = await Task.Run(() => Measure.DensityRange(layout, new LayerKey(34, 0), 13000, 13000)).WaitAsync(Deadline);

        Assert.NotNull(range);
        Assert.Equal(69.0 / 169.0, range.Value.Least, 12);
        Assert.Equal(69.0 / 169.0, range.Value.Most, 12);
    }

    #endregion **************************************************************************



    #region The layer as it is kept *****************************************************

    ///<summary>
    ///One ring with every hole still a hole, which is what the checks measure.
    ///</summary>
    [Fact]
    public void A_layer_keeps_its_holes_as_holes()
    {
        var layers = DrcLayers.Resolve(DrcDeck.Parse(Layers + "rule m.3 area met 144400 \"Area\""), Mesh());

        var ring = Assert.Single(layers.RingsOf("met"));

        Assert.Equal(Across * Across, ring.Holes.Count);
    }

    ///<summary>
    ///And read back as shapes, the same ground as one keyholed outline - the form a file holds, which is
    ///what <see cref="DrcLayers.Of"/> has always answered.
    ///</summary>
    [Fact]
    public void A_layer_read_back_as_shapes_is_one_keyholed_outline_covering_the_same_ground()
    {
        var layers = DrcLayers.Resolve(DrcDeck.Parse(Layers + "rule m.3 area met 144400 \"Area\""), Mesh());

        var outline = Assert.Single(layers.Of("met"));

        double side = (Across * Pitch) + Bar;
        double holes = Across * Across * (double)Hole * Hole;

        Assert.Equal((side * side) - holes, Measure.AreaOf(outline));
    }

    #endregion **************************************************************************
}
