using GdsII;

namespace GDSViewer.Tests;

///
///What the 3D view's spacing slider does to the stack.
///
///**Every layer moves, which is the thing that was broken.** A layer given a height - by a layermap with a
///process stack in it, or by a thickness typed into the settings popup - used to be skipped by
///`SetStackingOffsets` entirely, because the height and the drawn position were one field and writing a
///spread into it would have destroyed the height being read. So those layers never moved: dragging the
///slider pulled the layout apart around a clump that stayed exactly where it was, and the further it was
///dragged the more obviously wrong the picture got.
///
///It reported three times before it was found, and every correctness test stayed green through all of it -
///the layout drawn is right, every shape is on the layer it belongs to, and only where the layers sit is
///wrong. Which is why these are arithmetic tests on the offsets rather than anything about a picture.
///
public class LayerSpacingTests
{
    ///
    ///A stack of exactly the layers named, off a real library so the type is built the way the app builds it.
    ///
    private static AdditionalGDSInformation StackOf(params LayerKey[] keys)
    {
        var information = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample)).AdditionalInformation;

        information.Layers.Clear();

        foreach (var key in keys)
            information.Layers[key] = new Layer(key, "#ffffff");

        information.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpacing);

        return information;
    }

    private static readonly LayerKey Diff = new LayerKey(65, 20);
    private static readonly LayerKey Poly = new LayerKey(66, 20);
    private static readonly LayerKey Li1 = new LayerKey(67, 20);
    private static readonly LayerKey Met1 = new LayerKey(68, 20);

    ///<summary>The plain case, unchanged: one step per layer, from nothing.</summary>
    [Fact]
    public void Layers_with_no_height_of_their_own_step_evenly()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.SetStackingOffsets(200);

        Assert.Equal(new[] { 0, 200, 400, 600 }, Heights(stack));
    }

    ///
    ///**A layer given a height still moves when the slider does.**
    ///
    ///This is the failure, stated. Before the fix Poly stayed at 900 whatever the slider said, while the
    ///layers around it walked away from it.
    ///
    [Fact]
    public void A_layer_given_a_height_moves_with_the_slider_too()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.Layers[Poly].CustomHeight = 900;
        stack.Layers[Poly].StackIsCustom = true;

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpacing);

        int atRest = stack.Layers[Poly].Offset;

        stack.SetStackingOffsets(700);

        Assert.NotEqual(atRest, stack.Layers[Poly].Offset);
    }

    ///
    ///At the slider's own minimum nothing has moved from where it always sat, a real height included.
    ///
    ///The spread is measured from the default rather than from zero precisely so that this holds: a file
    ///with a process stack in it draws that stack when nobody has asked for anything else.
    ///
    [Fact]
    public void At_the_resting_spacing_a_given_height_is_exactly_where_it_was_asked_for()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.Layers[Poly].CustomHeight = 900;

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpacing);

        Assert.Equal(900, stack.Layers[Poly].Offset);

        //And the layers with no height of their own are where they always were.
        Assert.Equal(0, stack.Layers[Diff].Offset);
        Assert.Equal(AdditionalGDSInformation.DefaultLayerSpacing * 2, stack.Layers[Li1].Offset);
    }

    ///
    ///Dragging the slider twice is not dragging it twice as far.
    ///
    ///**The bug this rules out is the reason the height is kept apart from the position.** Adding a spread to
    ///the field the height is read from would compound on every step of a drag - a hundred input events, each
    ///one further than the last - and the layout would fly apart.
    ///
    [Fact]
    public void Setting_the_same_spacing_twice_lands_in_the_same_place()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.Layers[Poly].CustomHeight = 900;

        stack.SetStackingOffsets(700);

        var once = Heights(stack);

        stack.SetStackingOffsets(700);

        Assert.Equal(once, Heights(stack));
    }

    ///<summary>And a drag that goes out and comes back leaves the stack where it started.</summary>
    [Fact]
    public void Going_out_and_coming_back_is_where_it_started()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.Layers[Poly].CustomHeight = 900;

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpacing);

        var resting = Heights(stack);

        stack.SetStackingOffsets(700);
        stack.SetStackingOffsets(400);
        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpacing);

        Assert.Equal(resting, Heights(stack));
    }

    ///
    ///**No two layers share a height once the slider is off its minimum**, which is what "spread out" means.
    ///
    ///A given height can collide with the automatic stack - a layermap that puts a layer at 100 when the
    ///layer two places up is automatically at 100 - and at rest that is the file's own business. What must
    ///not happen is that pulling the slider leaves them together, because then the control does nothing for
    ///the case somebody is dragging it to see.
    ///
    [Fact]
    public void Spreading_separates_every_layer_even_when_heights_collide()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        //Exactly where the automatic stack puts Li1, two places above it.
        stack.Layers[Diff].CustomHeight = AdditionalGDSInformation.DefaultLayerSpacing * 2;

        stack.SetStackingOffsets(700);

        var heights = Heights(stack);

        Assert.Equal(heights.Length, heights.Distinct().Count());
    }

    ///<summary>Putting a layer back on the automatic stack forgets the height it was given.</summary>
    [Fact]
    public void Restoring_a_layer_puts_it_back_on_the_automatic_stack()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.Layers[Poly].CustomHeight = 900;
        stack.Layers[Poly].StackIsCustom = true;

        stack.RestoreStacking(Poly, 200);

        Assert.Null(stack.Layers[Poly].CustomHeight);
        Assert.False(stack.Layers[Poly].StackIsCustom);
        Assert.Equal(200, stack.Layers[Poly].Offset);
    }

    private static int[] Heights(AdditionalGDSInformation stack)
    {
        return stack.OrderedLayers().Select(entry => entry.Value.Offset).ToArray();
    }
}
