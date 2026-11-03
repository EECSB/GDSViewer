using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The walks that offer to stop: reading a file, flattening it, indexing it, writing its picture and merging its
///layers, each with an async twin that awaits a pause between runs of its steps. See GdsII's Pauses.
///
///**Each twin is held to the walk it pauses.** A load in the browser goes through the twins and everything else
///through the plain walks, so a twin that answered differently would be a file that looked one way when opened and
///another after its first edit. So every test here runs both over real files and compares - and the pause they are
///given really does suspend, as the browser's does, so the twins are tested on the path the app takes.
///
///**And a pause that throws gives the walk up**, which is how a load is canceled: the exception comes out of the
///twin, and nothing after it runs.
///</summary>
public class PacingTests
{
    #region Files ************************************************************************

    ///<summary>A hand-made cell, a foundry standard cell, and a whole design with a hierarchy of placed cells.</summary>
    public static IEnumerable<object[]> Files()
    {
        yield return new object[] { GdsTestData.MosfetSample };
        yield return new object[] { GdsTestData.Sky130Sample("sky130_fd_sc_hd__nand2_1.gds") };
        yield return new object[] { Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds") };
    }

    private static byte[] bytesOf(string sample)
    {
        return File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, sample));
    }

    ///<summary>A pause that suspends every time, the way the browser's does when a slice is used up, and counts.</summary>
    private sealed class Counting
    {
        public int Pauses;

        public async ValueTask Pause()
        {
            Pauses++;

            await Task.Yield();
        }
    }

    ///<summary>A pause that throws on its <paramref name="at"/>th call, the way the browser's does once a load is canceled.</summary>
    private static Func<ValueTask> givenUpAt(int at)
    {
        int calls = 0;

        return () =>
        {
            calls++;

            if (calls >= at)
                throw new OperationCanceledException();

            return default;
        };
    }

    #endregion **************************************************************************



    #region The same answers ************************************************************

    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_file_read_a_run_of_records_at_a_time_is_the_file_read_whole(string sample)
    {
        byte[] bytes = bytesOf(sample);
        var counting = new Counting();

        var whole = new GDS(bytes);
        var paced = await GDS.FromBytesAsync(bytes, counting.Pause);

        Assert.Equal(whole.Records.Count, paced.Records.Count);
        Assert.Equal(whole.Serialize(), paced.Serialize());
        Assert.True(counting.Pauses > 0, "the read never paused");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_file_read_at_a_pace_is_built_into_the_same_model_and_layer_table(string sample)
    {
        byte[] bytes = bytesOf(sample);

        var whole = new GDS(bytes);
        var paced = await GDS.FromBytesAsync(bytes, new Counting().Pause);

        var cells = whole.StreamFormat.Structures;
        var pacedCells = paced.StreamFormat.Structures;

        Assert.Equal(cells.Count, pacedCells.Count);

        for (int i = 0; i < cells.Count; i++)
        {
            Assert.Equal(Hierarchy.NameOf(cells[i]), Hierarchy.NameOf(pacedCells[i]));
            Assert.Equal(cells[i].Elements.Select(element => element.Element.GetType()), pacedCells[i].Elements.Select(element => element.Element.GetType()));
            Assert.NotNull(pacedCells[i].ENDSTR);
        }

        Assert.NotNull(paced.StreamFormat.ENDLIB);

        var layers = whole.AdditionalInformation.OrderedLayers().Select(layer => (layer.Key, layer.Value.Color, layer.Value.Offset));
        var pacedLayers = paced.AdditionalInformation.OrderedLayers().Select(layer => (layer.Key, layer.Value.Color, layer.Value.Offset));

        Assert.Equal(layers, pacedLayers);
    }

    ///
    ///Gpiov2's 106 ms pause on its first hover over Examples was the model built out of the records and then its layer
    ///table, two walks that did not stop. Each pauses a run of elements at a time now, as the records already did.
    ///
    [Fact]
    public async Task A_read_pauses_while_it_builds_the_model_and_its_layer_table()
    {
        byte[] bytes = bytesOf(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds"));
        var whole = new GDS(bytes);
        int every = runLength();
        int records = whole.Records.Count;
        int elements = whole.StreamFormat.Structures.Sum(structure => structure.Elements.Count);
        var counting = new Counting();

        await GDS.FromBytesAsync(bytes, counting.Pause);

        //One after each run of records, and one each time a run of elements is used up: building the model, and again
        //building its layer table.
        int expected = (records + every - 1) / every + 2 * (elements / every);

        Assert.True(elements >= every, "the design is too small to pause in its elements");
        Assert.Equal(expected, counting.Pauses);
    }

    [Fact]
    public async Task A_pause_that_throws_while_the_model_is_built_gives_the_read_up()
    {
        byte[] bytes = bytesOf(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds"));
        int every = runLength();
        int recordRuns = (new GDS(bytes).Records.Count + every - 1) / every;

        //The first pause after the last run of records is the model's.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await GDS.FromBytesAsync(bytes, givenUpAt(recordRuns + 1)));
    }

    ///<summary>How many steps a walk takes between pauses - GdsII's Pauses.Every, which is internal to the library.</summary>
    private static int runLength()
    {
        return (int)typeof(GDS).Assembly.GetType("GdsII.Pauses")!.GetField("Every")!.GetValue(null)!;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_flatten_that_pauses_draws_the_same_elements_in_the_same_order(string sample)
    {
        var gds = new GDS(bytesOf(sample));
        var counting = new Counting();

        var whole = GdsFlattener.Flatten(gds);
        var paced = await GdsFlattener.FlattenAsync(gds, counting.Pause);

        Assert.Equal(whole.Elements.Count, paced.Elements.Count);

        for (int i = 0; i < whole.Elements.Count; i++)
        {
            var one = whole.Elements[i];
            var other = paced.Elements[i];

            Assert.Equal(one.Layer.Key, other.Layer.Key);
            Assert.Equal(one.Text, other.Text);
            Assert.Equal(one.Points, other.Points);
            Assert.Equal(one.Source?.Structure, other.Source?.Structure);
        }

        Assert.Equal(whole.UnresolvedReferences, paced.UnresolvedReferences);
        Assert.Equal(whole.DepthLimitReached, paced.DepthLimitReached);
        Assert.Equal(whole.Stopped, paced.Stopped);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task An_index_built_a_run_at_a_time_answers_as_the_one_built_whole(string sample)
    {
        var layout = GdsFlattener.Flatten(new GDS(bytesOf(sample)));
        var counting = new Counting();

        var whole = new LayoutIndex(layout.Elements);
        var paced = await LayoutIndex.BuildAsync(layout.Elements, counting.Pause);

        Assert.Equal(whole.Extent, paced.Extent);
        Assert.Equal(whole.ShapeExtent, paced.ShapeExtent);
        Assert.Equal(whole.ShapeLayers.Select(layer => layer.Key), paced.ShapeLayers.Select(layer => layer.Key));
        Assert.Equal(whole.LargestArea, paced.LargestArea);

        //A box over each corner and the middle, and over everything, with and without the smallest shapes.
        var extent = whole.Extent;
        int halfWidth = (int)Math.Max(1, extent.Width / 2);
        int halfHeight = (int)Math.Max(1, extent.Height / 2);

        var boxes = new[]
        {
            extent,
            new Bounds(extent.Left, extent.Bottom, extent.Left + halfWidth, extent.Bottom + halfHeight),
            new Bounds(extent.Left + halfWidth, extent.Bottom + halfHeight, extent.Right, extent.Top),
            new Bounds(extent.Left + (halfWidth / 2), extent.Bottom + (halfHeight / 2), extent.Right - (halfWidth / 2), extent.Top - (halfHeight / 2))
        };

        foreach (var box in boxes)
        {
            Assert.Equal(whole.Within(box), paced.Within(box));
            Assert.Equal(whole.Within(box, whole.LargestArea / 64, withLabels: false), paced.Within(box, paced.LargestArea / 64, withLabels: false));
        }
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_picture_written_a_run_at_a_time_is_the_picture_written_whole(string sample)
    {
        var gds = new GDS(bytesOf(sample));
        var layout = GdsFlattener.Flatten(gds);
        var everyLayer = new HashSet<LayerKey>(gds.AdditionalInformation.Layers.Keys);
        var counting = new Counting();

        //Through a window, as the view draws it - which reads the index - and with the smallest shapes left out.
        var window = layout.Index.Extent;
        long smallest = layout.Index.LargestArea / 1000;

        string whole = SvgWriter.Build(layout, everyLayer, 0.5f, everyLayer, null, window, smallest);
        string paced = await SvgWriter.BuildAsync(layout, everyLayer, 0.5f, everyLayer, null, window, smallest, counting.Pause);

        Assert.Equal(whole, paced);
        Assert.True(counting.Pauses > 0, "the picture never paused");
    }

    ///<summary>The thumbnail as pixels: the same file, byte for byte, however often it stopped on the way.</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_preview_picture_drawn_a_run_at_a_time_is_the_picture_drawn_whole(string sample)
    {
        var layout = GdsFlattener.Flatten(new GDS(bytesOf(sample)));
        var counting = new Counting();

        var whole = Preview.Picture(layout, 320, 240, 0.85f)!;
        var paced = (await Preview.PictureAsync(layout, 320, 240, 0.85f, counting.Pause))!;

        Assert.Equal(whole.Png, paced.Png);
        Assert.Equal((whole.Width, whole.Height), (paced.Width, paced.Height));
        Assert.True(counting.Pauses > 0, "the picture never paused");
    }

    ///<summary>One cell by name, as a list of cells draws it: what the pausing walk finds is what the plain one does, for every cell.</summary>
    [Fact]
    public async Task A_cell_flattened_at_a_pace_is_the_cell_flattened_whole()
    {
        var gds = new GDS(bytesOf(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds")));
        var counting = new Counting();

        foreach (string name in Hierarchy.Names(gds))
        {
            var whole = GdsFlattener.Flatten(gds, name);
            var paced = await GdsFlattener.FlattenAsync(gds, name, counting.Pause);

            Assert.Equal(whole.Elements.Count, paced.Elements.Count);

            for (int i = 0; i < whole.Elements.Count; i++)
            {
                Assert.Equal(whole.Elements[i].Layer.Key, paced.Elements[i].Layer.Key);
                Assert.Equal(whole.Elements[i].Points, paced.Elements[i].Points);
            }
        }

        Assert.True(counting.Pauses > 0, "no cell's walk ever paused");
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Layers_merged_a_group_at_a_time_are_the_layers_merged_whole(string sample)
    {
        var layout = GdsFlattener.Flatten(new GDS(bytesOf(sample)));
        var counting = new Counting();

        var whole = Booleans.MergeByLayer(layout.Elements);
        var paced = await Booleans.MergeByLayerAsync(layout.Elements, counting.Pause);

        Assert.Equal(whole.Count, paced.Count);

        for (int i = 0; i < whole.Count; i++)
        {
            Assert.Equal(whole[i].Layer.Key, paced[i].Layer.Key);
            Assert.Equal(whole[i].Boundary, paced[i].Boundary);
            Assert.Equal(whole[i].Holes.Count, paced[i].Holes.Count);
        }

        Assert.True(counting.Pauses > 0, "the merge never paused");
    }

    ///
    ///A design flattens at the pace of its placements: the walk that pauses steps from one placed cell to the next and
    ///draws each small one in one go, so it must still pause - and a placement big enough to walk into on its own is
    ///still drawn whole.
    ///
    [Fact]
    public async Task A_design_flattened_at_a_pace_pauses_between_its_placements()
    {
        var gds = new GDS(bytesOf(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds")));
        var counting = new Counting();

        var paced = await GdsFlattener.FlattenAsync(gds, counting.Pause);

        Assert.True(counting.Pauses > 0, "the flatten never paused");
        Assert.Equal(GdsFlattener.Flatten(gds).Elements.Count, paced.Elements.Count);
    }

    #endregion **************************************************************************



    #region Giving up *******************************************************************

    [Fact]
    public async Task A_pause_that_throws_gives_each_walk_up()
    {
        byte[] bytes = bytesOf(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds"));
        var gds = new GDS(bytes);
        var layout = GdsFlattener.Flatten(gds);
        var everyLayer = new HashSet<LayerKey>(gds.AdditionalInformation.Layers.Keys);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await GDS.FromBytesAsync(bytes, givenUpAt(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await GdsFlattener.FlattenAsync(gds, givenUpAt(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await LayoutIndex.BuildAsync(layout.Elements, givenUpAt(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await SvgWriter.BuildAsync(layout, everyLayer, 0.5f, everyLayer, null, layout.Index.Extent, 0, givenUpAt(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Booleans.MergeByLayerAsync(layout.Elements, givenUpAt(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await Preview.PictureAsync(layout, 320, 240, 0.85f, givenUpAt(2)));
        //The top cell, which is the one with placements enough to walk a placement at a time - a leaf has nothing to pause between.
        string top = Hierarchy.Names(gds).OrderByDescending(name => GdsFlattener.Flatten(gds, name).Elements.Count).First();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await GdsFlattener.FlattenAsync(gds, top, givenUpAt(2)));
    }

    ///<summary>And the layout an index was being built for keeps no part of the one given up: its own is built afresh when asked.</summary>
    [Fact]
    public async Task An_index_given_up_leaves_its_layout_to_build_its_own()
    {
        var layout = GdsFlattener.Flatten(new GDS(bytesOf(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds"))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await layout.IndexAsync(givenUpAt(2)));

        var built = new LayoutIndex(layout.Elements);

        Assert.Equal(built.Extent, layout.Index.Extent);
        Assert.Equal(built.Within(built.Extent), layout.Index.Within(built.Extent));
    }

    #endregion **************************************************************************



    #region The pace ********************************************************************

    ///<summary>A pace lets the page in with the turn it is handed, once its slice is used up - a picture's is a posted message.</summary>
    [Fact]
    public async Task A_pace_lets_the_page_in_with_the_turn_it_is_handed()
    {
        int turns = 0;

        ValueTask turn()
        {
            turns++;

            return ValueTask.CompletedTask;
        }

        var pacer = new GDSViewer.Models.Pacer { Slice = 0, Turn = turn };

        await pacer.Pause();
        await pacer.Pause();

        Assert.Equal(2, turns);
    }

    ///<summary>And not while its slice lasts: a walk that never uses it up is never stopped.</summary>
    [Fact]
    public async Task A_pace_lets_nothing_in_while_its_slice_lasts()
    {
        int turns = 0;

        ValueTask turn()
        {
            turns++;

            return ValueTask.CompletedTask;
        }

        var pacer = new GDSViewer.Models.Pacer { Slice = 60000, Turn = turn };

        for (int i = 0; i < 1000; i++)
            await pacer.Pause();

        Assert.Equal(0, turns);
    }

    #endregion **************************************************************************
}
