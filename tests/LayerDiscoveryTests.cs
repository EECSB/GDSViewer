using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers AdditionalGDSInformation, which walks the parsed structures to collect the layers present in
///a file and assigns each one a stacking offset (used by the 3D view) and a palette color (used by
///both the 2D and 3D views).
///
///A layer here is a **layer/datatype pair**, not a layer number - see <see cref="LayerKey"/>. The two
///halves are then treated differently on purpose, which the Stacking offsets and Palette colors regions
///below pin: height follows the layer number so one physical layer is one plane, while color follows the
///pair so drawn geometry and a pin on that layer can be told apart.
///</summary>
public class LayerDiscoveryTests
{
    ///<summary>The first seed color of the palette; the lowest-numbered layer always gets it.</summary>
    private const string FirstPaletteColor = "#b30000";

    ///<summary>Builds a library with one boundary per requested layer number, all on data type 0.</summary>
    private static GDS LibraryWithLayers(params short[] layers)
    {
        return LibraryWithPairs(layers.Select(layer => new LayerKey(layer, 0)).ToArray());
    }

    ///<summary>Builds a library with one boundary per requested layer/datatype pair.</summary>
    private static GDS LibraryWithPairs(params LayerKey[] pairs)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),
        };

        foreach (var pair in pairs)
        {
            records.Add(GdsTestData.Record(RecordType.BOUNDARY));
            records.Add(GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(pair.Number)));
            records.Add(GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(pair.DataType)));
            records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 10, 0, 10, 10, 0, 0)));
            records.Add(GdsTestData.Record(RecordType.ENDEL));
        }

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return new GDS(GdsTestData.Concat(records.ToArray()));
    }

    #region Discovery *****************************************************************

    [Fact]
    public void A_single_layer_is_discovered_and_keyed_by_its_pair()
    {
        var layers = LibraryWithLayers(5).AdditionalInformation.Layers;

        var layer = Assert.Single(layers);

        Assert.Equal(new LayerKey(5, 0), layer.Key);
        Assert.Equal((short)5, layer.Value.Number);
        Assert.Equal((short)0, layer.Value.DataType);
    }

    [Fact]
    public void A_layer_used_by_several_elements_is_recorded_once()
    {
        var layers = LibraryWithLayers(7, 7, 7).AdditionalInformation.Layers;

        Assert.Single(layers);
        Assert.True(layers.ContainsKey(new LayerKey(7, 0)));
    }

    [Fact]
    public void Every_distinct_layer_is_discovered()
    {
        var layers = LibraryWithLayers(64, 65, 66, 64).AdditionalInformation.Layers;

        Assert.Equal(3, layers.Count);
        Assert.Equal(
            new[] { new LayerKey(64, 0), new LayerKey(65, 0), new LayerKey(66, 0) },
            layers.Keys.OrderBy(key => key.Number).ToArray());
    }

    [Fact]
    public void A_library_with_no_elements_discovers_no_layers()
    {
        var layers = LibraryWithLayers().AdditionalInformation.Layers;

        Assert.Empty(layers);
    }

    ///<summary>
    ///SREF has no layer of its own, so a structure containing only references contributes nothing.
    ///</summary>
    [Fact]
    public void Structure_references_contribute_no_layers()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("SUB")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));

        var gds = new GDS(stream);

        Assert.Single(gds.StreamFormat.Structures[0].Elements);
        Assert.Empty(gds.AdditionalInformation.Layers);
    }

    [Fact]
    public void Layers_are_collected_across_all_structures()
    {
        byte[] cell = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("A")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));

        byte[] otherCell = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("B")),
            GdsTestData.Record(RecordType.BOX),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.BOXTYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));

        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            cell,
            otherCell,
            GdsTestData.Record(RecordType.ENDLIB));

        var layers = new GDS(stream).AdditionalInformation.Layers;

        Assert.Equal(2, layers.Count);
    }

    #endregion ***********************************************************************



    #region The data type half ********************************************************

    ///<summary>
    ///The whole point of keying on the pair. Before this, both of these were "layer 65" - one entry, one
    ///checkbox, one color - so hiding the drawn geometry hid the pin shapes with it.
    ///</summary>
    [Fact]
    public void Two_data_types_on_one_layer_are_two_layers()
    {
        var layers = LibraryWithPairs(new LayerKey(65, 20), new LayerKey(65, 16)).AdditionalInformation.Layers;

        Assert.Equal(2, layers.Count);
        Assert.True(layers.ContainsKey(new LayerKey(65, 20)));
        Assert.True(layers.ContainsKey(new LayerKey(65, 16)));
    }

    ///<summary>
    ///Each element type spells its half of the pair differently - DATATYPE, TEXTTYPE, BOXTYPE, NODETYPE -
    ///and IHasLayer.DataTypeRecord is what hides that. If one of them were wired to the wrong record, or
    ///left unwired, its elements would land on the "unknown" key instead and this catches it.
    ///</summary>
    [Fact]
    public void Every_element_type_contributes_its_own_kind_of_data_type()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),

            //BOUNDARY: DATATYPE.
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(10)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL),

            //PATH: DATATYPE.
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            //TEXT: TEXTTYPE, which lives in the text body rather than on the element.
            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(30)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(3)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii("A")),
            GdsTestData.Record(RecordType.ENDEL),

            //BOX: BOXTYPE.
            GdsTestData.Record(RecordType.BOX),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(40)),
            GdsTestData.Record(RecordType.BOXTYPE, GdsTestData.Int2(4)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 10, 0, 10, 10, 0, 10, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            //NODE: NODETYPE.
            GdsTestData.Record(RecordType.NODE),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(50)),
            GdsTestData.Record(RecordType.NODETYPE, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));

        var layers = new GDS(stream).AdditionalInformation.Layers;

        Assert.Equal(
            new[]
            {
                new LayerKey(10, 1),
                new LayerKey(20, 2),
                new LayerKey(30, 3),
                new LayerKey(40, 4),
                new LayerKey(50, 5)
            },
            layers.Keys.OrderBy(key => key.Number).ToArray());
    }

    ///<summary>
    ///The flattener builds the pair for each element separately from the discovery pass, so the two have to
    ///agree or an element looks up a key that is not there and is silently dropped.
    ///</summary>
    [Fact]
    public void The_flattener_finds_the_layer_of_an_element_on_a_nonzero_data_type()
    {
        var gds = LibraryWithPairs(new LayerKey(65, 20), new LayerKey(65, 16));

        var layout = GdsFlattener.Flatten(gds);

        Assert.Equal(2, layout.Elements.Count);
        Assert.Equal(
            new[] { new LayerKey(65, 16), new LayerKey(65, 20) },
            layout.Elements.Select(element => element.Layer.Key).OrderBy(key => key.DataType).ToArray());
    }

    #endregion ***********************************************************************



    #region Stacking offsets *********************************************************

    [Fact]
    public void The_only_layer_sits_at_offset_zero()
    {
        var layers = LibraryWithLayers(5).AdditionalInformation.Layers;

        Assert.Equal(0, layers[new LayerKey(5, 0)].Offset);
    }

    [Fact]
    public void Offsets_stack_by_layer_depth_in_ascending_layer_order()
    {
        //Deliberately out of order in the stream: the offsets must follow layer number, not file order.
        var layers = LibraryWithLayers(30, 10, 20).AdditionalInformation.Layers;

        Assert.Equal(0, layers[new LayerKey(10, 0)].Offset);
        Assert.Equal(50, layers[new LayerKey(20, 0)].Offset);
        Assert.Equal(100, layers[new LayerKey(30, 0)].Offset);
    }

    ///
    ///Height is per layer, so every row in the list separates from the one below it by the same step.
    ///
    ///**This asserted the opposite.** Every data type of one layer used to share a height, on the reading
    ///that 65/20 and 65/16 are drawn geometry and a pin on one diffusion layer rather than two depths in the
    ///wafer - which is true of a pin and false of the case that matters: a contact is a `/44` purpose of the
    ///layer below it, so licon1 sat at poly's height and mcon at li1's, and a via drawn inside the metal it
    ///climbs from is not a physical reading of anything.
    ///
    ///The cost is the old comment's and it is real: a pin or a label purpose now floats a step above the
    ///geometry it annotates, and the stack is as tall as the file has purposes. A layermap with real heights
    ///in it is what gets the physical stack; this is what a file without one gets.
    ///
    [Fact]
    public void Every_layer_gets_a_step_of_its_own()
    {
        var layers = LibraryWithPairs(
            new LayerKey(65, 16),
            new LayerKey(65, 20),
            new LayerKey(65, 44),
            new LayerKey(66, 20)).AdditionalInformation.Layers;

        //In the order the list reads them: number first, then data type.
        Assert.Equal(0, layers[new LayerKey(65, 16)].Offset);
        Assert.Equal(50, layers[new LayerKey(65, 20)].Offset);
        Assert.Equal(100, layers[new LayerKey(65, 44)].Offset);
        Assert.Equal(150, layers[new LayerKey(66, 20)].Offset);
    }

    ///<summary>And the gaps are equal, which is the whole of what the spacing slider promises.</summary>
    [Fact]
    public void The_gaps_between_the_layers_are_all_the_same()
    {
        var information = LibraryWithPairs(
            new LayerKey(65, 16),
            new LayerKey(65, 20),
            new LayerKey(66, 20),
            new LayerKey(66, 44),
            new LayerKey(67, 20)).AdditionalInformation;

        information.SetStackingOffsets(120);

        var heights = information.OrderedLayers().Select(entry => entry.Value.Offset).ToList();

        //The rung the even stack always had, plus the gap the slider asks to open on top of it.
        const int step = AdditionalGDSInformation.DefaultLayerSpacing + 120;

        for (int at = 1; at < heights.Count; at++)
            Assert.Equal(step, heights[at] - heights[at - 1]);
    }

    ///<summary>
    ///The 3D view's spacing slider re-runs the same walk rather than doing its own, which is what stops the
    ///two disagreeing about what a step is. It used to have its own copy.
    ///</summary>
    [Fact]
    public void Respacing_keeps_one_step_per_layer()
    {
        var information = LibraryWithPairs(
            new LayerKey(65, 16),
            new LayerKey(65, 20),
            new LayerKey(66, 20)).AdditionalInformation;

        information.SetStackingOffsets(700);

        const int step = AdditionalGDSInformation.DefaultLayerSpacing + 700;

        Assert.Equal(0, information.Layers[new LayerKey(65, 16)].Offset);
        Assert.Equal(step, information.Layers[new LayerKey(65, 20)].Offset);
        Assert.Equal(step * 2, information.Layers[new LayerKey(66, 20)].Offset);
    }

    [Fact]
    public void Layers_get_the_default_depth()
    {
        var layers = LibraryWithLayers(1, 2).AdditionalInformation.Layers;

        Assert.Equal(50, layers[new LayerKey(1, 0)].Depth);
        Assert.Equal(50, layers[new LayerKey(2, 0)].Depth);
    }

    #endregion ***********************************************************************



    #region Palette colors ***********************************************************

    [Fact]
    public void The_lowest_numbered_layer_gets_the_first_palette_color()
    {
        var layers = LibraryWithLayers(42, 7).AdditionalInformation.Layers;

        Assert.Equal(FirstPaletteColor, layers[new LayerKey(7, 0)].Color);
    }

    ///<summary>
    ///Color is per pair, unlike height: this is the half where telling drawn geometry from a pin is the
    ///point, and it is what a layer-properties file does - KLayout's sky130 .lyp gives all 413 entries
    ///their own color.
    ///</summary>
    [Fact]
    public void Data_types_of_one_layer_get_different_colors()
    {
        var layers = LibraryWithPairs(new LayerKey(65, 16), new LayerKey(65, 20)).AdditionalInformation.Layers;

        Assert.NotEqual(layers[new LayerKey(65, 16)].Color, layers[new LayerKey(65, 20)].Color);
    }

    ///<summary>
    ///The palette is walked with a step of paletteLength / layerCount, so distinct layers must land on
    ///distinct colors. A step that collapsed to zero would paint every layer the same and this would fail.
    ///</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(64)]
    public void Every_layer_gets_a_distinct_color(int layerCount)
    {
        short[] layerNumbers = Enumerable.Range(1, layerCount).Select(n => (short)n).ToArray();

        var layers = LibraryWithLayers(layerNumbers).AdditionalInformation.Layers;

        var colors = layers.Values.Select(layer => layer.Color).ToList();

        Assert.Equal(layerCount, colors.Count);
        Assert.Equal(layerCount, colors.Distinct().Count());
        Assert.All(colors, color => Assert.Matches("^#[0-9a-f]{6}$", color));
    }

    #endregion ***********************************************************************



    #region Identity and labeling ****************************************************

    ///<summary>The form the pair is written in everywhere else - KLayout, Magic, a PDK layermap.</summary>
    [Fact]
    public void A_pair_is_written_as_layer_slash_datatype()
    {
        Assert.Equal("65/20", new LayerKey(65, 20).ToString());
    }

    ///<summary>
    ///An element whose type record is missing or holds the wrong payload lands on a data type that cannot
    ///collide with a real one, rather than being read as data type 0 and merged with real geometry.
    ///</summary>
    [Fact]
    public void A_missing_data_type_reads_as_unknown_rather_than_zero()
    {
        var key = new LayerKey(65, LayerKey.UnknownDataType);

        Assert.NotEqual(new LayerKey(65, 0), key);
        Assert.Equal("65/?", key.ToString());
    }

    ///<summary>
    ///The numbers stay visible even when a name is known, the way KLayout's own layer panel shows both: the
    ///name is somebody's mapping and the numbers are what the file says, so a mismatch has to be visible.
    ///</summary>
    [Fact]
    public void A_named_layer_is_labeled_with_its_name_and_its_numbers()
    {
        var layer = new Layer(new LayerKey(65, 20), "#ffffff");

        Assert.Equal("65/20", layer.DisplayName);

        layer.Name = "diff.drawing";

        Assert.Equal("diff.drawing (65/20)", layer.DisplayName);
    }

    [Fact]
    public void A_blank_name_labels_the_layer_as_if_it_had_none()
    {
        var layer = new Layer(new LayerKey(65, 20), "#ffffff");

        layer.Name = "   ";

        Assert.Equal("65/20", layer.DisplayName);
    }

    #endregion ***********************************************************************

    #region Adding a layer nothing is drawn on ***************************************

    ///
    ///A layer put in the table that no shape carries.
    ///
    ///**Because the table is built from what the layout draws.** A new library has no layers at all and an
    ///existing one offers only the numbers it happens to use, so the answer to "draw on 66/44" was that
    ///there was nowhere to say it. This is what the layer sidebar's + Layer reaches, and what a shape
    ///drawn onto a pair nothing uses reaches through LayoutEdit.Register.
    ///
    [Fact]
    public void A_layer_can_be_added_with_nothing_drawn_on_it()
    {
        var information = new GDS(GdsTestData.MinimalLibrary()).AdditionalInformation;
        var key = new LayerKey(66, 44);

        Assert.False(information.Layers.ContainsKey(key));
        Assert.True(information.AddLayer(key));
        Assert.True(information.Layers.ContainsKey(key));
    }

    ///<summary>And it arrives in the gray a layer gets when the gradient was not divided for it.</summary>
    [Fact]
    public void An_added_layer_arrives_in_the_color_a_late_layer_gets()
    {
        var information = new GDS(GdsTestData.MinimalLibrary()).AdditionalInformation;
        var key = new LayerKey(66, 44);

        information.AddLayer(key);

        Assert.Equal(AdditionalGDSInformation.NewLayerColor, information.Layers[key].Color);
    }

    ///
    ///**A pair already there is refused rather than replaced**, and says so by answering false.
    ///
    ///The table is keyed by the pair, so a second add could only overwrite the row - which would throw away
    ///the name, color and height it was carrying. Silently, and on the press of a button whose whole job is
    ///to add something that was not there.
    ///
    [Fact]
    public void Adding_a_layer_that_is_already_there_changes_nothing()
    {
        var information = new GDS(GdsTestData.MinimalLibrary()).AdditionalInformation;
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Name = "named";

        Assert.False(information.AddLayer(key));
        Assert.Equal("named", information.Layers[key].Name);
    }

    ///<summary>And it takes a place in the stack like any other, rather than staying at nothing.</summary>
    [Fact]
    public void An_added_layer_is_stacked_with_the_rest()
    {
        var information = new GDS(GdsTestData.MinimalLibrary()).AdditionalInformation;

        information.AddLayer(new LayerKey(200, 0));
        information.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        var ordered = information.OrderedLayers();

        //Highest pair in the file, so it is the top of the even stack rather than sharing the floor.
        Assert.Equal(new LayerKey(200, 0), ordered[^1].Key);
        Assert.Equal(AdditionalGDSInformation.DefaultLayerSpacing * (ordered.Count - 1), ordered[^1].Value.Offset);
    }

    #endregion ***********************************************************************
}
