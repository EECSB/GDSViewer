using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The process stack: a layer's height and its thickness, given rather than worked out.
///
///The 3D view spaces layers evenly until it is told otherwise, which says only what order they are in. A
///process gives each layer a real height and thickness, and those arrive either typed into the settings
///popup or in the two columns a layermap can now carry.
///
///What is worth testing here is the part that would silently undo itself: a height that the spacing slider
///puts back, or a column that reads as a name.
///</summary>
public class LayerStackTests
{
    private static AdditionalGDSInformation MosfetLayers()
    {
        return new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample)).AdditionalInformation;
    }

    private static Layer Any(AdditionalGDSInformation information)
    {
        return information.OrderedLayers()[0].Value;
    }

    #region Keeping a height *********************************************************

    ///
    ///A given height is where the layer rests, and the slider still moves it.
    ///
    ///**This test said the opposite until somebody dragged the slider on a real file three times.** It
    ///asserted that a height typed in survived the spacing being changed, which is true of the height and was
    ///implemented as the layer being skipped altogether - so it never moved at all, while everything around
    ///it spread away from it. A stack that comes apart around a clump that stays put is not what either
    ///control is for.
    ///
    ///So: at the resting spacing the height is exactly what was asked for, the thickness always is, and off
    ///the minimum the layer moves like every other. See LayerSpacingTests for the arithmetic in full.
    ///
    [Fact]
    public void A_given_height_rests_where_it_was_asked_for_and_still_spreads()
    {
        var information = MosfetLayers();

        //
        //**Two measured layers, and this reads the upper one.**
        //
        //Something has to be the floor the spread is measured from, and it gains nothing by definition - which
        //is not the same as being skipped. Reading the lower of the two would be reading whatever happened
        //to rest lowest, and a test that cannot tell "did not move" from "had nothing to gain" is not
        //pinning what it names.
        //
        var floor = information.OrderedLayers()[1].Value;

        floor.CustomHeight = 1000;
        floor.Depth = 100;
        floor.StackIsCustom = true;

        var layer = information.OrderedLayers()[2].Value;

        layer.CustomHeight = 12345;
        layer.Depth = 400;
        layer.StackIsCustom = true;

        information.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        Assert.Equal(12345, layer.Offset);
        Assert.Equal(400, layer.Depth);

        information.SetStackingOffsets(700);

        Assert.NotEqual(12345, layer.Offset);

        //The thickness is not a position and is never touched by the slider.
        Assert.Equal(400, layer.Depth);
    }

    [Fact]
    public void A_layer_left_alone_still_moves_with_the_spacing()
    {
        var information = MosfetLayers();

        information.SetStackingOffsets(100);
        var before = information.OrderedLayers().Select(entry => entry.Value.Offset).ToList();

        information.SetStackingOffsets(200);
        var after = information.OrderedLayers().Select(entry => entry.Value.Offset).ToList();

        Assert.NotEqual(before, after);
    }

    ///<summary>
    ///The step counts past a pinned layer rather than closing up behind it, so the layers around one real
    ///height stay where they would have been instead of shuffling down a place.
    ///</summary>
    [Fact]
    public void A_pinned_layer_does_not_move_the_ones_above_it()
    {
        var information = MosfetLayers();

        information.SetStackingOffsets(100);
        var expected = information.OrderedLayers().ToDictionary(entry => entry.Key, entry => entry.Value.Offset);

        //Pin the first layer somewhere else entirely, then recompute.
        var pinned = information.OrderedLayers()[0];
        pinned.Value.Offset = 99999;
        pinned.Value.StackIsCustom = true;

        information.SetStackingOffsets(100);

        foreach (var entry in information.OrderedLayers())
        {
            if (entry.Key.Equals(pinned.Key))
                continue;

            Assert.Equal(expected[entry.Key], entry.Value.Offset);
        }
    }

    [Fact]
    public void Resetting_a_layer_puts_it_back_on_the_even_spacing()
    {
        var information = MosfetLayers();

        information.SetStackingOffsets(100);

        var first = information.OrderedLayers()[0];
        int automatic = first.Value.Offset;

        first.Value.Offset = 55555;
        first.Value.Depth = 7;
        first.Value.StackIsCustom = true;

        information.RestoreStacking(first.Key, 100);

        Assert.Equal(automatic, first.Value.Offset);
        Assert.Equal(AdditionalGDSInformation.DefaultLayerDepth, first.Value.Depth);
        Assert.False(first.Value.StackIsCustom);
    }

    #endregion ***********************************************************************



    #region The two new columns ******************************************************

    [Fact]
    public void A_row_can_carry_a_height_and_a_thickness()
    {
        var names = LayerNames.Parse("65,20,diff.drawing,#00ff00,2000,500\n");

        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
        Assert.Equal("#00ff00", names.Colors[new LayerKey(65, 20)]);
        Assert.Equal((2000, 500), names.Stack[new LayerKey(65, 20)]);
    }

    ///<summary>A mapping written for an older build of this app has four columns, and still reads.</summary>
    [Theory]
    [InlineData("65,20,diff.drawing\n")]
    [InlineData("65,20,diff.drawing,#00ff00\n")]
    public void A_row_without_the_stack_columns_still_reads(string text)
    {
        var names = LayerNames.Parse(text);

        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
        Assert.Empty(names.Stack);
        Assert.Empty(names.Problems);
    }

    ///<summary>
    ///Half a stack is not a stack. Guessing the other half would put a layer somewhere nobody asked for,
    ///so the row keeps its name and loses only the column that was incomplete.
    ///</summary>
    [Theory]
    [InlineData("65,20,diff.drawing,#00ff00,2000\n")]
    [InlineData("65,20,diff.drawing,#00ff00,,500\n")]
    public void A_height_without_a_thickness_is_refused_and_reported(string text)
    {
        var names = LayerNames.Parse(text);

        Assert.Equal("diff.drawing", names.Names[new LayerKey(65, 20)]);
        Assert.Empty(names.Stack);
        Assert.Single(names.Problems);
    }

    [Fact]
    public void A_thickness_of_nothing_is_refused()
    {
        var names = LayerNames.Parse("65,20,diff.drawing,#00ff00,2000,0\n");

        Assert.Empty(names.Stack);
        Assert.Contains("draw nothing", names.Problems.Single());
    }

    [Fact]
    public void A_stack_value_that_is_not_a_number_is_reported_by_line()
    {
        var names = LayerNames.Parse("65,20,diff.drawing,#00ff00,high,500\n");

        Assert.Empty(names.Stack);
        Assert.Contains("Line 1", names.Problems.Single());
    }

    ///<summary>
    ///An int rather than a short, because the layer number beside it is one. A stack in nanometers runs
    ///past 32767 well before it is out of the metal.
    ///</summary>
    [Fact]
    public void A_stack_taller_than_a_short_reads()
    {
        var names = LayerNames.Parse("65,20,diff.drawing,#00ff00,120000,4000\n");

        Assert.Equal((120000, 4000), names.Stack[new LayerKey(65, 20)]);
    }

    [Fact]
    public void The_header_row_names_all_six_columns()
    {
        var names = LayerNames.Parse(LayerNames.HeaderRow + "65,20,diff.drawing,#00ff00,2000,500\n");

        Assert.Single(names.Names);
        Assert.Empty(names.Problems);
    }

    #endregion ***********************************************************************



    #region Applying and writing back ************************************************

    [Fact]
    public void Applying_a_mapping_puts_the_stack_onto_the_layers()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        LayerNames.Parse($"{key.Number},{key.DataType},named,#00ff00,3000,250\n").ApplyTo(information.Layers);

        var layer = information.Layers[key];

        Assert.Equal(3000, layer.Offset);
        Assert.Equal(250, layer.Depth);
        Assert.True(layer.StackIsCustom);
    }

    ///
    ///And it then survives the slider, which is the whole point of the flag.
    ///
    ///**Survives means the spread is measured from it**, not that it never moves. Every layer moves once the
    ///slider is off its minimum - see SetStackingOffsets.
    ///
    ///It used to gain nothing, and that was not the flag working: it was this layer happening to be first in
    ///the file, back when the spread counted down the layer numbers rather than up the stack. On sky130 that
    ///difference put the implants - the highest numbers in the file and the lowest things on the wafer -
    ///climbing past met3 as the slider was dragged.
    ///
    ///**Two rows, because the layer this reads must not be the floor.** The spread is measured from
    ///whatever rests lowest and that layer gains nothing by definition, so a single mapped row could pass
    ///this by being the floor rather than by carrying its height through the change.
    ///gains nothing by definition, which would make this pass for the wrong reason.
    ///
    [Fact]
    public void A_stack_out_of_a_mapping_survives_the_spacing_being_changed()
    {
        var information = MosfetLayers();
        var floor = information.OrderedLayers()[0].Key;
        var key = information.OrderedLayers()[1].Key;

        string mapping =
            $"{floor.Number},{floor.DataType},lower,#00ff00,1000,100\n" +
            $"{key.Number},{key.DataType},named,#00ff00,3000,250\n";

        LayerNames.Parse(mapping).ApplyTo(information.Layers);

        //At rest it is exactly what the mapping asked for.
        information.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        Assert.Equal(3000, information.Layers[key].Offset);

        information.SetStackingOffsets(700);

        //Still measured from its own height rather than reset to the automatic stack.
        Assert.True(information.Layers[key].Offset > 3000);
        Assert.True(information.Layers[key].StackIsCustom);
    }

    ///<summary>The round trip: what is written comes back as what it was.</summary>
    [Fact]
    public void A_written_mapping_reads_back_with_its_stack()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        var layer = information.Layers[key];
        layer.Name = "named";
        layer.Offset = 3000;
        layer.CustomHeight = 3000;
        layer.Depth = 250;
        layer.StackIsCustom = true;

        var read = LayerNames.Parse(LayerNames.Named(information));

        Assert.Equal((3000, 250), read.Stack[key]);
    }

    ///<summary>
    ///A layer left on the even spacing writes no stack columns.
    ///
    ///Writing the automatic heights would turn a mapping into a snapshot of one file's spacing: loaded
    ///against another file, every layer would arrive pinned to where some other layout's layers sat, with
    ///nothing on the row to say it was a guess.
    ///</summary>
    [Fact]
    public void A_layer_on_the_even_spacing_writes_no_stack()
    {
        var information = MosfetLayers();
        var layer = information.OrderedLayers()[0];

        layer.Value.Name = "named";

        string written = LayerNames.Named(information);

        Assert.DoesNotContain(",,", written);

        //Three: the pair and the name. It was four, back when the palette color was written down as well -
        //and that was the same mistake as writing the automatic heights, one column to the left. See
        //A_name_on_its_own_does_not_write_the_palette_color.
        Assert.Equal(3, written.Split('\n')[0].Split(',').Length);
        Assert.Empty(LayerNames.Parse(written).Stack);
    }

    ///
    ///The export fills every column, including for a layer nobody has touched.
    ///
    ///It wrote the stack only for a placed layer, which made the export useless for the thing it is for:
    ///the header said height,thickness and not one row had them, so building a stack meant knowing to type
    ///two columns that were not there. What comes out is what the app is drawing.
    ///
    [Fact]
    public void The_export_fills_every_column_even_for_an_untouched_layer()
    {
        var information = MosfetLayers();

        Assert.DoesNotContain(information.OrderedLayers(), entry => entry.Value.StackIsCustom);

        string[] rows = LayerNames.Export(information)
            .Split('\n')
            .Where(row => row.Length > 0 && !row.StartsWith('#'))
            .ToArray();

        Assert.NotEmpty(rows);

        foreach (string row in rows)
            Assert.Equal(6, row.Split(',').Length);
    }

    ///
    ///And it reads back as the stack that was on screen, which is what makes it worth editing.
    ///
    ///**Where a layer rests, not where the slider had pushed it.** This asserted `Offset` - the drawn
    ///position, height plus the spread for its rank - and that is the assertion that let the export
    ///compound. Exported at a wide spacing, every height came out inflated; loading it back applied the
    ///inflated numbers as measured heights and the slider spread them again, so a file that had been
    ///written out and read in a few times had a stack that no longer resembled anything. The session's
    ///shorter row is written by the same method and carried the same fault onto the next file opened.
    ///
    ///So the property worth having is not "what was on screen" but **the same stack, reproduced**: loading
    ///the export back and stacking at the spacing it was taken at gives the picture it was taken from.
    ///That holds at every spacing, where the old assertion only held at the slider's own minimum.
    ///
    [Fact]
    public void An_untouched_export_reads_back_as_the_stack_it_was_taken_from()
    {
        var information = MosfetLayers();

        information.SetStackingOffsets(100);

        var drawn = information.OrderedLayers().ToDictionary(entry => entry.Key, entry => entry.Value.Offset);

        var read = LayerNames.Parse(LayerNames.Export(information));

        //The heights that go out are the resting ones, which is what the spread is measured from.
        foreach (var entry in information.OrderedLayers())
            Assert.Equal((entry.Value.Resting, entry.Value.Depth), read.Stack[entry.Key]);

        //And loading it back gives the same picture rather than a wider one.
        var reopened = MosfetLayers();

        read.ApplyTo(reopened.Layers);
        reopened.SetStackingOffsets(100);

        foreach (var entry in reopened.OrderedLayers())
            Assert.Equal(drawn[entry.Key], entry.Value.Offset);
    }

    ///<summary>
    ///What a session keeps is still only what was placed. Recording the automatic heights there would pin
    ///every layer of the next file opened to where this one's happened to sit.
    ///</summary>
    [Fact]
    public void What_a_session_keeps_is_still_only_what_was_placed()
    {
        var information = MosfetLayers();

        information.OrderedLayers()[0].Value.Name = "named";

        Assert.Empty(LayerNames.Parse(LayerNames.Named(information)).Stack);
    }

    [Fact]
    public void The_template_carries_a_stack_that_was_set()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Offset = 3000;
        information.Layers[key].CustomHeight = 3000;
        information.Layers[key].Depth = 250;
        information.Layers[key].StackIsCustom = true;

        var read = LayerNames.Parse(LayerNames.Export(information));

        Assert.Equal((3000, 250), read.Stack[key]);
    }

    ///<summary>Clearing the names drops the stack with them, the way it drops the colors.</summary>
    [Fact]
    public void Clearing_the_names_puts_the_stack_back_too()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        LayerNames.Parse($"{key.Number},{key.DataType},named,#00ff00,3000,250\n").ApplyTo(information.Layers);

        LayerNames.Clear(information);

        Assert.False(information.Layers[key].StackIsCustom);
        Assert.Equal(AdditionalGDSInformation.DefaultLayerDepth, information.Layers[key].Depth);
    }

    #endregion ***********************************************************************


    #region A layer's fill pattern ***************************************************

    ///
    ///The eighth column, read back onto the layer.
    ///
    ///Typed by hand rather than exported, like the role beside it: a PDK table carries the colors and the
    ///heights and has no opinion about stipples. Which is exactly why it has to survive a round trip -
    ///somebody who worked out that two of their layers were too alike should not do it twice.
    ///
    [Fact]
    public void Applying_a_mapping_puts_the_fill_onto_the_layer()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        LayerNames.Parse($"{key.Number},{key.DataType},named,#00ff00,3000,250,conductor,crosshatch\n").ApplyTo(information.Layers);

        Assert.Equal(LayerFill.CrossHatch, information.Layers[key].Fill);
    }

    ///<summary>Case does not matter, since this column is typed rather than exported.</summary>
    [Theory]
    [InlineData("dots", LayerFill.Dots)]
    [InlineData("DOTS", LayerFill.Dots)]
    [InlineData("BackDiagonal", LayerFill.BackDiagonal)]
    [InlineData("backdiagonal", LayerFill.BackDiagonal)]
    public void A_fill_is_read_whatever_its_case(string written, LayerFill expected)
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        LayerNames.Parse($"{key.Number},{key.DataType},named,#00ff00,3000,250,none,{written}\n").ApplyTo(information.Layers);

        Assert.Equal(expected, information.Layers[key].Fill);
    }

    ///<summary>And a word this does not know is reported rather than swallowed, the way a bad role is.</summary>
    [Fact]
    public void An_unknown_fill_is_reported()
    {
        var read = LayerNames.Parse("65,20,named,#00ff00,3000,250,none,tartan\n");

        Assert.Empty(read.Fills);
        Assert.Contains(read.Problems, problem => problem.Contains("tartan"));
    }

    ///
    ///The round trip, and the empty column that makes it work.
    ///
    ///The columns are positional, so a fill needs a role in front of it whether or not the layer has one -
    ///a layer patterned and roleless writes an explicit "none" rather than leaving a gap that would shift
    ///the fill into the role's place and be read as a bad role.
    ///
    [Fact]
    public void A_written_mapping_reads_back_with_its_fill()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Name = "named";
        information.Layers[key].Fill = LayerFill.Grid;

        string written = LayerNames.Named(information);
        var read = LayerNames.Parse(written);

        Assert.Equal(LayerFill.Grid, read.Fills[key]);

        //And no role came along with it, which is what an explicit "none" has to mean on the way back.
        Assert.False(read.Roles.TryGetValue(key, out var role) && role != LayerRole.None);
    }

    ///<summary>A solid layer writes no fill column, the same as a layer on the even spacing writes no stack.</summary>
    [Fact]
    public void A_solid_layer_writes_no_fill()
    {
        var information = MosfetLayers();

        information.OrderedLayers()[0].Value.Name = "named";

        Assert.Empty(LayerNames.Parse(LayerNames.Named(information)).Fills);
    }

    ///<summary>Clearing a mapping takes the patterns with it, the way it takes the names and the stack.</summary>
    [Fact]
    public void Clearing_a_mapping_puts_the_layers_back_to_solid()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Fill = LayerFill.Dashes;

        LayerNames.Clear(information);

        Assert.Equal(LayerFill.None, information.Layers[key].Fill);
    }

    #endregion ***********************************************************************



    #region What a session is willing to write down ***********************************

    ///
    ///**A role must not smuggle the automatic stack into the session.**
    ///
    ///The columns are positional, so a role at column seven needs a height at five and a thickness at six to
    ///sit behind it - and those two were filled in from wherever the even spacing had put the layer. Read back,
    ///a height in that column means the layer was placed by hand, and a placed layer is one
    ///SetStackingOffsets will not move.
    ///
    ///So the shipped sky130 mapping, which gives most of its layers a role and no height, pinned most of a
    ///file's layers the moment a session was stored - and the 3D view's spacing slider stopped moving them the
    ///second time the file was opened. `A_layer_on_the_even_spacing_writes_no_stack` above says this and passed
    ///throughout: it names a layer and stops, and a name alone never reaches the branch that writes them.
    ///
    [Fact]
    public void A_role_does_not_pin_a_layer_that_was_never_placed()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Role = LayerRole.Conductor;

        var read = LayerNames.Parse(LayerNames.Named(information));

        //The role arrives, which is what the row is for.
        Assert.Equal(LayerRole.Conductor, read.Roles[key]);

        //And the height does not, which is what the empty columns in front of it are for.
        Assert.Empty(read.Stack);
    }

    ///<summary>And the whole way round: applied to a fresh file, the slider still moves the layer.</summary>
    [Fact]
    public void A_stored_role_leaves_the_spacing_slider_working()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Role = LayerRole.Conductor;

        var fresh = MosfetLayers();

        LayerNames.Parse(LayerNames.Named(information)).ApplyTo(fresh.Layers);

        Assert.False(fresh.Layers[key].StackIsCustom);

        fresh.SetStackingOffsets(700);

        var heights = fresh.OrderedLayers().Select(entry => entry.Value.Offset).ToList();

        //The even stack's own rung, plus what the slider opens on top of it.
        const int step = AdditionalGDSInformation.DefaultLayerSpacing + 700;

        for (int at = 1; at < heights.Count; at++)
            Assert.Equal(step, heights[at] - heights[at - 1]);
    }

    ///<summary>A fill in front of the stack columns does the same thing, for the same reason.</summary>
    [Fact]
    public void A_fill_does_not_pin_a_layer_that_was_never_placed()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        information.Layers[key].Fill = LayerFill.Dots;

        var read = LayerNames.Parse(LayerNames.Named(information));

        Assert.Equal(LayerFill.Dots, read.Fills[key]);
        Assert.Empty(read.Stack);
    }

    ///<summary>A height that *was* chosen still goes down, which is the other half of the same rule.</summary>
    [Fact]
    public void A_placed_layer_with_a_role_keeps_both()
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;

        var layer = information.Layers[key];
        layer.Role = LayerRole.Via;
        layer.Offset = 3000;
        layer.CustomHeight = 3000;
        layer.Depth = 250;
        layer.StackIsCustom = true;

        var read = LayerNames.Parse(LayerNames.Named(information));

        Assert.Equal((3000, 250), read.Stack[key]);
        Assert.Equal(LayerRole.Via, read.Roles[key]);
    }

    ///
    ///**Anything said about a layer is worth a row, not just a name and a role.**
    ///
    ///The session writer skipped a layer with neither, which was the whole test while those were the only two
    ///columns. A color by hand, a stack, a fill and the two pattern columns each arrived afterwards without
    ///coming back to it - so a hatch chosen on a layer nobody had named was written nowhere and gone on the
    ///next refresh. The row builder was already willing to write it, which is what made it look like it had
    ///worked. See Layer.WasSaid.
    ///
    [Theory]
    [InlineData("fill")]
    [InlineData("patterncolor")]
    [InlineData("patternsize")]
    [InlineData("stack")]
    [InlineData("color")]
    public void A_setting_on_a_nameless_layer_is_still_stored(string setting)
    {
        var information = MosfetLayers();
        var key = information.OrderedLayers()[0].Key;
        var layer = information.Layers[key];

        if (setting == "fill")
            layer.Fill = LayerFill.Diagonal;

        if (setting == "patterncolor")
        {
            layer.Fill = LayerFill.Dots;
            layer.PatternColor = "#123456";
        }

        if (setting == "patternsize")
        {
            layer.Fill = LayerFill.Dots;
            layer.PatternPixels = 24;
        }

        if (setting == "stack")
        {
            layer.Offset = 3000;
            layer.CustomHeight = 3000;
            layer.Depth = 250;
            layer.StackIsCustom = true;
        }

        if (setting == "color")
        {
            layer.Color = "#abcdef";
            layer.ColorIsCustom = true;
        }

        var fresh = MosfetLayers();

        LayerNames.Parse(LayerNames.Named(information)).ApplyTo(fresh.Layers);

        var back = fresh.Layers[key];

        Assert.Null(back.Name);

        if (setting == "fill")
            Assert.Equal(LayerFill.Diagonal, back.Fill);

        if (setting == "patterncolor")
            Assert.Equal("#123456", back.PatternColor);

        if (setting == "patternsize")
            Assert.Equal(24, back.PatternPixels);

        if (setting == "stack")
            Assert.Equal((3000, 250), (back.Offset, back.Depth));

        if (setting == "color")
            Assert.Equal("#abcdef", back.Color);
    }

    ///<summary>And a layer nobody has touched still writes nothing, which is what the row list is trimmed for.</summary>
    [Fact]
    public void An_untouched_layer_writes_no_row()
    {
        var information = MosfetLayers();

        Assert.Equal("", LayerNames.Named(information));
    }

    #endregion ***********************************************************************

    #region Writing a stack back out *********************************************************

    ///
    ///**A stack written out is the one that was asked for, whatever the spacing slider is at.**
    ///
    ///This is the bug that made a layout come apart over repeated opens, and it compounded because the two
    ///halves of it fed each other. A layer is drawn at its height plus the spread for its rank, and the
    ///writer wrote the drawn position - so a map exported, or a session saved, while the slider was off its
    ///minimum recorded the spread as though it were a measured height. Reopening applied it as one, the
    ///slider spread it again, and the next save recorded that. On Mosfet.gds the bundled sky130 heights had
    ///walked from -120..1370 to -16..2180 before anybody could say which number was wrong.
    ///
    ///Read at a wide spacing on purpose: at the slider's own minimum the spread is zero and the drawn
    ///position and the height are the same number, which is exactly the reading that cannot fail.
    ///
    [Fact]
    public void An_exported_stack_is_the_height_that_was_asked_for_rather_than_where_the_slider_put_it()
    {
        var information = MosfetLayers();
        var floor = information.OrderedLayers()[0].Key;
        var upper = information.OrderedLayers()[3].Key;

        string mapping =
            $"{floor.Number},{floor.DataType},lower,#00ff00,-120,120\n" +
            $"{upper.Number},{upper.DataType},upper,#00ff00,1370,360\n";

        LayerNames.Parse(mapping).ApplyTo(information.Layers);

        //Pulled well open, so a spread written into the height column would be unmissable.
        information.SetStackingOffsets(700);

        var written = LayerNames.Parse(LayerNames.Export(information));

        Assert.Equal((-120, 120), written.Stack[floor]);
        Assert.Equal((1370, 360), written.Stack[upper]);
    }

    ///<summary>And the same for the shorter row a session keeps, which is the one that carries across files.</summary>
    [Fact]
    public void A_stack_kept_for_the_next_file_is_the_height_that_was_asked_for_too()
    {
        var information = MosfetLayers();
        var upper = information.OrderedLayers()[3].Key;

        LayerNames.Parse($"{upper.Number},{upper.DataType},upper,#00ff00,1370,360\n").ApplyTo(information.Layers);

        information.SetStackingOffsets(700);

        var written = LayerNames.Parse(LayerNames.Named(information));

        Assert.Equal((1370, 360), written.Stack[upper]);
    }

    ///
    ///**And writing it out and reading it back changes nothing, however many times.**
    ///
    ///The property the two above are really about. A round trip that moves a layer moves it again on the
    ///next one, so the failure is not a wrong number once - it is a stack that never settles.
    ///
    [Fact]
    public void A_stack_survives_being_written_out_and_read_back_any_number_of_times()
    {
        var information = MosfetLayers();
        var upper = information.OrderedLayers()[3].Key;

        LayerNames.Parse($"{upper.Number},{upper.DataType},upper,#00ff00,1370,360\n").ApplyTo(information.Layers);

        for (int round = 0; round < 5; round++)
        {
            information.SetStackingOffsets(700);

            LayerNames.Parse(LayerNames.Export(information)).ApplyTo(information.Layers);
        }

        information.SetStackingOffsets(AdditionalGDSInformation.DefaultLayerSpread);

        Assert.Equal(1370, information.Layers[upper].Offset);
        Assert.Equal(360, information.Layers[upper].Depth);
    }

    #endregion **************************************************************************
}