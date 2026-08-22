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

        information.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        return information;
    }

    private static readonly LayerKey Diff = new LayerKey(65, 20);
    private static readonly LayerKey Poly = new LayerKey(66, 20);
    private static readonly LayerKey Li1 = new LayerKey(67, 20);
    private static readonly LayerKey Met1 = new LayerKey(68, 20);

    ///
    ///**A layer the file says nothing about keeps its meaningless place here, and the 3D view is what
    ///leaves it out.**
    ///
    ///This tried for one round to answer that question in the stack itself, by parking such a layer above
    ///the top of everything measured - and parking is exactly what a person then saw. Four of the eight
    ///unmapped layers on a sky130 standard cell are drawn to the whole cell, so they hung over the layout
    ///as a ladder of cell-sized plates with sky above and below each one. There is no height that is right
    ///for a layer that is not on the wafer, so the stack stops guessing at one.
    ///
    [Fact]
    public void A_layer_with_no_height_keeps_its_place_in_the_even_stack()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        //Two films, measured, with the top of the upper one at 1370 + 360.
        stack.Layers[Li1].CustomHeight = 940;
        stack.Layers[Li1].Depth = 100;
        stack.Layers[Met1].CustomHeight = 1370;
        stack.Layers[Met1].Depth = 360;

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        //The two that were told sit where they were told.
        Assert.Equal(940, stack.Layers[Li1].Offset);
        Assert.Equal(1370, stack.Layers[Met1].Offset);

        //And the two that were not keep the places their index gives them, which say only what order the
        //layers are in. Nothing here claims otherwise - HasProcessStack below is what the 3D view asks
        //before it believes one of these.
        Assert.Equal(0, stack.Layers[Diff].Offset);
        Assert.Equal(AdditionalGDSInformation.DefaultLayerSpacing, stack.Layers[Poly].Offset);
    }

    ///
    ///**Whether the file was told anything at all**, which is the whole of what the 3D view asks before it
    ///leaves a layer out.
    ///
    ///Both directions matter. On a file with no layermap every layer is untold, the even stack is the only
    ///statement about order there is, and a view that skipped untold layers would draw nothing at all.
    ///
    [Fact]
    public void A_file_has_a_process_stack_once_any_layer_has_been_given_a_height()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        Assert.False(stack.HasProcessStack);
        Assert.Equal(new[] { 0, 50, 100, 150 }, Heights(stack));

        stack.Layers[Met1].CustomHeight = 1370;

        Assert.True(stack.HasProcessStack);
    }

    ///
    ///The plain case: one step per layer, from nothing.
    ///
    ///**The step is the rung plus the spread**, not the spread alone. A layer nobody measured rests at its
    ///place in the even stack, which is DefaultLayerSpacing apart, and the slider opens its own gap on top
    ///of that - so at a spread of 200 these step by 250 and at a spread of nought they step by the rung
    ///they always had. The slider used to be read as the step itself, which is why it could not go below
    ///50: there was no way to say "add nothing" without also collapsing this stack.
    ///
    [Fact]
    public void Layers_with_no_height_of_their_own_step_evenly()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.SetStackingOffsets(200);

        const int step = AdditionalGDSInformation.DefaultLayerSpacing + 200;

        Assert.Equal(new[] { 0, step, step * 2, step * 3 }, Heights(stack));
    }

    ///<summary>And with nothing asked for, the even stack is exactly its own rung height.</summary>
    [Fact]
    public void Layers_with_no_height_rest_on_the_even_stack_when_nothing_is_spread()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        const int rung = AdditionalGDSInformation.DefaultLayerSpacing;

        Assert.Equal(new[] { 0, rung, rung * 2, rung * 3 }, Heights(stack));
    }

    ///
    ///**A layer given a height still moves when the slider does.**
    ///
    ///This is the failure, stated. Before the fix Poly stayed at 900 whatever the slider said, while the
    ///layers around it walked away from it.
    ///
    ///**Two told heights, and the upper one is what this reads.** Something has to be the floor the spread
    ///is measured from, and that is whatever rests lowest - it gains nothing by definition, which is not the
    ///same as being skipped. A single told layer would be that floor here, so the assertion would hold for
    ///the wrong reason.
    ///
    [Fact]
    public void A_layer_given_a_height_moves_with_the_slider_too()
    {
        var stack = StackOf(Diff, Poly, Li1, Met1);

        stack.Layers[Poly].CustomHeight = 900;
        stack.Layers[Poly].StackIsCustom = true;

        stack.Layers[Met1].CustomHeight = 2000;
        stack.Layers[Met1].StackIsCustom = true;

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        int atRest = stack.Layers[Met1].Offset;

        Assert.Equal(2000, atRest);

        stack.SetStackingOffsets(700);

        Assert.NotEqual(atRest, stack.Layers[Met1].Offset);

        //Measured from its own height rather than reset to the automatic stack.
        Assert.True(stack.Layers[Met1].Offset > 2000);
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

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        Assert.Equal(900, stack.Layers[Poly].Offset);

        //And the layers with no height of their own keep the places their index gives them - 0, 100 and
        //150 - rather than being shuffled around the one film that was measured. Those numbers mean
        //nothing, which is the point: see A_layer_with_no_height_keeps_its_place_in_the_even_stack.
        Assert.Equal(0, stack.Layers[Diff].Offset);
        Assert.Equal(100, stack.Layers[Li1].Offset);
        Assert.Equal(150, stack.Layers[Met1].Offset);
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

        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        var resting = Heights(stack);

        stack.SetStackingOffsets(700);
        stack.SetStackingOffsets(400);
        stack.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

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

        //Second on the even stack, so one rung up and one spread out - see Layers_with_no_height_of_their
        //_own_step_evenly for why the two are added rather than the slider's number being the step.
        Assert.Equal(AdditionalGDSInformation.DefaultLayerSpacing + 200, stack.Layers[Poly].Offset);
    }

    private static int[] Heights(AdditionalGDSInformation stack)
    {
        return stack.OrderedLayers().Select(entry => entry.Value.Offset).ToArray();
    }
}
