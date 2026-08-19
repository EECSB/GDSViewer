using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Changing a library, and taking the change back.
///
///**The round trip is the test.** A GDS holds a flat list of records and a tree built over it, and the
///tree's nodes are the very records in the list. An edit that changed one and not the other would look
///right in whichever half you happened to check - the view would move a shape the download did not, or
///the download would carry one the view no longer draws. So most of what is here does the edit, writes
///the file out, reads it back, and asks whether the bytes agree with the picture.
///
///And undo has to be exact. A file edited and then undone has to be the file it was, byte for byte -
///anything less is a history that only mostly works, which is worse than none because it is trusted.
///</summary>
public class LayoutEditTests
{
    #region A library to edit *******************************************************

    ///<summary>A leaf with one square, placed three times, plus a square of the top's own.</summary>
    private static GDS Placed()
    {
        return new GDS(GdsTestData.ReadFixture("placed.gds"));
    }

    private static GDS.StructureModel StructureNamed(GDS gds, string name)
    {
        return gds.StreamFormat.Structures.Single(structure =>
            ((AsciiData)structure.STRNAME.Data!).Value == name);
    }

    ///<summary>Every corner the file draws, in a stable order, for comparing two states of it.</summary>
    private static List<string> Corners(GDS gds)
    {
        return GdsTestData.Geometry(gds);
    }

    #endregion **********************************************************************



    #region Moving ******************************************************************

    [Fact]
    public void Moving_an_element_moves_its_coordinates()
    {
        var gds = Placed();
        var leaf = StructureNamed(gds, "LEAF").Elements[0];

        var before = ((Int4Data)leaf.Element.XY.Data!).Values.ToArray();

        new MoveElement(StructureNamed(gds, "LEAF"), leaf, 25, -40).Apply();

        var after = ((Int4Data)leaf.Element.XY.Data!).Values;

        for (int i = 0; i + 1 < before.Length; i += 2)
        {
            Assert.Equal(before[i] + 25, after[i]);
            Assert.Equal(before[i + 1] - 40, after[i + 1]);
        }
    }

    ///<summary>
    ///**Every instance moves.** There is one LEAF and the three squares are references to it, so a move
    ///inside it moves all three. This is the thing the whole editing context exists to make visible, and
    ///the thing a reader of the file has to see too.
    ///</summary>
    [Fact]
    public void Moving_a_cell_moves_every_instance_of_it()
    {
        var gds = Placed();

        var drawn = GdsFlattener.Flatten(gds).Elements
            .Where(element => element.Source!.Structure == "LEAF")
            .Select(element => element.Points[0])
            .ToList();

        Assert.Equal(3, drawn.Count);

        new MoveElement(StructureNamed(gds, "LEAF"), 0, 100, 200).Apply();

        var moved = GdsFlattener.Flatten(gds).Elements
            .Where(element => element.Source!.Structure == "LEAF")
            .Select(element => element.Points[0])
            .ToList();

        Assert.Equal(3, moved.Count);

        for (int i = 0; i < drawn.Count; i++)
        {
            Assert.Equal(drawn[i].X + 100, moved[i].X);
            Assert.Equal(drawn[i].Y + 200, moved[i].Y);
        }
    }

    ///<summary>And the top's own square does not move, because it is in a different cell.</summary>
    [Fact]
    public void Moving_one_cell_leaves_the_others_alone()
    {
        var gds = Placed();

        var before = GdsFlattener.Flatten(gds).Elements.Single(element => element.Source!.Structure == "TOP").Points[0];

        new MoveElement(StructureNamed(gds, "LEAF"), 0, 100, 200).Apply();

        var after = GdsFlattener.Flatten(gds).Elements.Single(element => element.Source!.Structure == "TOP").Points[0];

        Assert.Equal(before, after);
    }

    ///<summary>
    ///The move reaches the file, not only the model. Written out and read back, because that is what a
    ///download is - and an edit the download did not carry would be the worst kind of silent.
    ///</summary>
    [Fact]
    public void A_move_is_in_the_bytes_that_are_written()
    {
        var gds = Placed();

        new MoveElement(StructureNamed(gds, "LEAF"), 0, 70, 90).Apply();

        var reread = new GDS(gds.Serialize());

        Assert.Equal(Corners(gds), Corners(reread));

        //And it is genuinely different from where it started.
        Assert.NotEqual(Corners(Placed()), Corners(reread));
    }

    ///<summary>The record dump is the same model, so it carries an edit too - which is what the text view shows.</summary>
    [Fact]
    public void A_move_is_in_the_record_dump()
    {
        var gds = Placed();

        string before = gds.AsText();

        new MoveElement(StructureNamed(gds, "LEAF"), 0, 5, 5).Apply();

        Assert.NotEqual(before, gds.AsText());
        Assert.Equal(Corners(gds), Corners(GDS.FromText(gds.AsText())));
    }

    ///
    ///**A drag on screen, written into a cell that is turned.**
    ///
    ///The case the whole provenance and context chain exists for. LEAF is placed at a quarter turn, so
    ///what is to the right on screen is downwards inside the cell - and the number that goes into the file
    ///has to be the second one while the shape still follows the pointer.
    ///
    ///Measured: a drag of +200 in x becomes (0, -200) in the cell, and the drawn corner moves from (0, 0)
    ///to (200, 0). An editor that wrote the screen distance straight into the file would move this shape
    ///the wrong way by ninety degrees, and would look perfectly correct on any unrotated cell.
    ///
    [Fact]
    public void A_drag_on_screen_is_written_in_the_cell_own_coordinates()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var gds = new GDS(GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("R")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(65)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(100))),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.STRANS, new byte[] { 0x00, 0x00 }),
            GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(90)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));

        var shape = GdsFlattener.Flatten(gds).Elements.Single();
        var context = CellContext.At(shape.Source!);

        Assert.Equal(new Element.Point(0, 0), shape.Points[0]);

        //Two points converted rather than the distance itself, because a translation put through a
        //transform would pick up that transform's own offset as well.
        (double zeroX, double zeroY) = context.ToLocal(0, 0)!.Value;
        (double movedX, double movedY) = context.ToLocal(200, 0)!.Value;

        int localX = (int)Math.Round(movedX - zeroX);
        int localY = (int)Math.Round(movedY - zeroY);

        //Right on screen is down in a cell turned a quarter.
        Assert.Equal(0, localX);
        Assert.Equal(-200, localY);

        new MoveElement(StructureNamed(gds, "LEAF"), shape.Source!.Model, localX, localY).Apply();

        //And on screen it went where it was dragged.
        Assert.Equal(new Element.Point(200, 0), GdsFlattener.Flatten(gds).Elements.Single().Points[0]);
    }

    #endregion **********************************************************************



    #region Deleting ****************************************************************

    [Fact]
    public void Deleting_takes_the_element_out_of_its_structure_and_the_file()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        int recordsBefore = gds.Records.Count;
        int elementsBefore = top.Elements.Count;

        new DeleteElement(gds, top, square).Apply();

        Assert.Equal(elementsBefore - 1, top.Elements.Count);
        Assert.DoesNotContain(square, top.Elements);

        //BOUNDARY, LAYER, DATATYPE, XY, ENDEL.
        Assert.Equal(recordsBefore - 5, gds.Records.Count);

        //And what comes out the other side no longer draws it.
        var reread = new GDS(gds.Serialize());

        Assert.DoesNotContain(GdsFlattener.Flatten(reread).Elements, element => element.Layer.Key.Number == 67);
    }

    ///<summary>
    ///Deleting the shape inside a placed cell takes every instance of it with it, which is the same rule
    ///moving follows and for the same reason: there is one cell.
    ///</summary>
    [Fact]
    public void Deleting_inside_a_cell_removes_every_instance()
    {
        var gds = Placed();

        Assert.Equal(3, GdsFlattener.Flatten(gds).Elements.Count(element => element.Source!.Structure == "LEAF"));

        var leaf = StructureNamed(gds, "LEAF");

        new DeleteElement(gds, leaf, leaf.Elements[0]).Apply();

        var reread = new GDS(gds.Serialize());

        //The placements are still there; there is simply nothing in the cell to draw.
        Assert.DoesNotContain(GdsFlattener.Flatten(reread).Elements, element => element.Layer.Key.Number == 65);
    }

    [Fact]
    public void Deleting_something_that_is_not_there_says_so_rather_than_corrupting_the_file()
    {
        var gds = Placed();
        var other = Placed();

        var top = StructureNamed(gds, "TOP");
        var stranger = StructureNamed(other, "TOP").Elements[0];

        Assert.Throws<InvalidOperationException>(() => new DeleteElement(gds, top, stranger).Apply());

        //And nothing was taken out on the way to finding out.
        Assert.Equal(Corners(Placed()), Corners(gds));
    }

    #endregion **********************************************************************



    #region Moving one corner *******************************************************

    [Fact]
    public void Moving_a_corner_moves_that_corner_and_leaves_the_others()
    {
        var gds = Placed();
        var leaf = StructureNamed(gds, "LEAF").Elements[0];

        var before = ((Int4Data)leaf.Element.XY.Data!).Values.ToArray();

        new MoveVertex(StructureNamed(gds, "LEAF"), leaf, 1, 30, 40).Apply();

        var after = ((Int4Data)leaf.Element.XY.Data!).Values;

        Assert.Equal(before[2] + 30, after[2]);
        Assert.Equal(before[3] + 40, after[3]);

        //And the second corner is the only one that went anywhere.
        Assert.Equal(before[4], after[4]);
        Assert.Equal(before[5], after[5]);
    }

    ///
    ///**The closing corner goes with the first.**
    ///
    ///A GDSII boundary repeats its opening corner at the end to close the ring. Dragging corner zero and
    ///leaving the copy behind opens the outline into a hook - which draws as a filled shape with a slit in
    ///it and reads back as a perfectly valid file, which is the worst combination there is.
    ///
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Moving_either_end_of_a_closed_ring_moves_both(int corner)
    {
        var gds = Placed();
        var leaf = StructureNamed(gds, "LEAF").Elements[0];

        var before = ((Int4Data)leaf.Element.XY.Data!).Values.ToArray();

        //The fixture's square is closed: five corners, the last repeating the first.
        Assert.Equal(10, before.Length);
        Assert.Equal(before[0], before[8]);
        Assert.Equal(before[1], before[9]);

        new MoveVertex(StructureNamed(gds, "LEAF"), leaf, corner, 15, -25).Apply();

        var after = ((Int4Data)leaf.Element.XY.Data!).Values;

        Assert.Equal(after[0], after[8]);
        Assert.Equal(after[1], after[9]);

        Assert.Equal(before[0] + 15, after[0]);
        Assert.Equal(before[1] - 25, after[1]);
    }

    [Fact]
    public void A_corner_that_is_not_there_is_left_alone()
    {
        var gds = Placed();
        var leaf = StructureNamed(gds, "LEAF").Elements[0];

        var before = ((Int4Data)leaf.Element.XY.Data!).Values.ToArray();

        new MoveVertex(StructureNamed(gds, "LEAF"), leaf, 99, 10, 10).Apply();
        new MoveVertex(StructureNamed(gds, "LEAF"), leaf, -1, 10, 10).Apply();

        Assert.Equal(before, ((Int4Data)leaf.Element.XY.Data!).Values);
    }

    #endregion **********************************************************************



    #region Drawing *****************************************************************

    [Fact]
    public void A_drawn_boundary_becomes_an_element_of_the_structure()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        int before = top.Elements.Count;

        new AddElement(gds, top, new LayerKey(70, 0), Square(2000, 2000, 300)).Apply();

        Assert.Equal(before + 1, top.Elements.Count);

        //And it is drawn, on the layer it was given.
        var drawn = GdsFlattener.Flatten(gds).Elements.Where(element => element.Layer.Key.Equals(new LayerKey(70, 0))).ToList();

        Assert.Single(drawn);
        Assert.Contains(new Element.Point(2000, 2000), drawn[0].Points);
    }

    ///<summary>Written, read back, and still there - which is the only thing a drawing tool finally has to do.</summary>
    [Fact]
    public void A_drawn_boundary_is_in_the_file()
    {
        var gds = Placed();

        new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 0), Square(0, 0, 500)).Apply();

        var reread = new GDS(gds.Serialize());

        Assert.Contains(GdsFlattener.Flatten(reread).Elements, element => element.Layer.Key.Equals(new LayerKey(70, 0)));
        Assert.Equal(Corners(gds), Corners(reread));
    }

    ///
    ///An outline that does not close is closed on the way in. A boundary whose last corner is not its
    ///first is one every reader complains about, and whoever is dragging a rectangle should not have to
    ///remember that.
    ///
    [Fact]
    public void An_open_outline_is_closed_before_it_is_written()
    {
        var gds = Placed();

        var open = new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(100, 0),
            new Element.Point(100, 100),
            new Element.Point(0, 100)
        };

        new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 0), open).Apply();

        var written = ((Int4Data)StructureNamed(gds, "TOP").Elements[^1].Element.XY.Data!).Values;

        Assert.Equal(10, written.Length);
        Assert.Equal(written[0], written[8]);
        Assert.Equal(written[1], written[9]);
    }

    ///<summary>Drawing into a placed cell puts the shape into every instance of it, like any other edit.</summary>
    [Fact]
    public void Drawing_in_a_placed_cell_draws_in_every_instance()
    {
        var gds = Placed();

        new AddElement(gds, StructureNamed(gds, "LEAF"), new LayerKey(70, 0), Square(0, 0, 50)).Apply();

        var reread = new GDS(gds.Serialize());

        Assert.Equal(3, GdsFlattener.Flatten(reread).Elements.Count(element => element.Layer.Key.Equals(new LayerKey(70, 0))));
    }

    [Fact]
    public void An_undone_drawing_leaves_the_file_as_it_was()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 0), Square(0, 0, 400)));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());

        //And redoing puts it back rather than adding a second one.
        history.Redo();

        Assert.Single(GdsFlattener.Flatten(gds).Elements, element => element.Layer.Key.Equals(new LayerKey(70, 0)));
    }

    private static List<Element.Point> Square(int left, int bottom, int size)
    {
        return new List<Element.Point>
        {
            new Element.Point(left, bottom),
            new Element.Point(left + size, bottom),
            new Element.Point(left + size, bottom + size),
            new Element.Point(left, bottom + size),
            new Element.Point(left, bottom)
        };
    }

    #endregion **********************************************************************



    ///
    ///**A corner that repeats the one before it is dropped.**
    ///
    ///A side of no length is a point on the outline with no direction, which is where an offset or a boolean
    ///over that shape stops behaving - some distance away from whatever drew it. They arrive without anybody
    ///asking: GDSII coordinates are whole numbers and a curve is not, so an ellipse asked for at many sides
    ///on a small radius has pairs of corners that round to the same pair of integers.
    ///
    [Fact]
    public void A_corner_that_repeats_the_one_before_it_is_dropped()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var doubled = new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(0, 0),
            new Element.Point(100, 0),
            new Element.Point(100, 100),
            new Element.Point(100, 100),
            new Element.Point(0, 100)
        };

        new AddElement(gds, top, new LayerKey(70, 0), doubled).Apply();

        var drawn = GdsFlattener.Flatten(gds).Elements.Single(element => element.Layer.Key.Number == 70);

        //Four corners and the one that closes the ring, rather than six and a repeat at the seam.
        Assert.Equal(5, drawn.Points.Count);
        Assert.Equal(drawn.Points[0], drawn.Points[^1]);

        for (int i = 1; i < drawn.Points.Count; i++)
            Assert.NotEqual(drawn.Points[i - 1], drawn.Points[i]);
    }

    ///<summary>And a ring handed over already closed does not come back with two copies of its first corner.</summary>
    [Fact]
    public void A_ring_that_is_already_closed_is_not_closed_twice()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var ring = new List<Element.Point>(Square(0, 0, 200)) { new Element.Point(0, 0) };

        new AddElement(gds, top, new LayerKey(70, 0), ring).Apply();

        var drawn = GdsFlattener.Flatten(gds).Elements.Single(element => element.Layer.Key.Number == 70);

        Assert.Equal(5, drawn.Points.Count);
    }

    #region Labels ******************************************************************

    ///
    ///**A label is a TEXT element, and the whole of one.**
    ///
    ///Written out and read back, because that is what a download is: an element assembled wrongly here would
    ///look fine in the view that made it - the model is in hand - and be a file no other tool would take.
    ///
    [Fact]
    public void A_label_is_written_as_a_text_element_and_reads_back()
    {
        var gds = Placed();

        new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 5), new Element.Point(400, 900), "VDD").Apply();

        var reread = new GDS(gds.Serialize());

        var label = GdsFlattener.Flatten(reread).Elements.Single(element => element.Text is not null);

        Assert.Equal("VDD", label.Text);
        Assert.Equal(new LayerKey(70, 5), label.Layer.Key);
        Assert.Equal(new Element.Point(400, 900), label.Points[0]);
    }

    ///
    ///TEXTTYPE, not DATATYPE. The format spells the second half of the layer pair differently for each
    ///element, and a label carrying a DATATYPE is one no reader will pair with its layer - which shows up as
    ///a label on 70/-2 rather than on 70/5, in this app as much as anywhere else.
    ///
    [Fact]
    public void A_label_carries_a_texttype_rather_than_a_datatype()
    {
        var gds = Placed();

        new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 5), new Element.Point(0, 0), "A").Apply();

        var written = new GDS(gds.Serialize()).Records.Select(record => record.Type).ToList();

        Assert.Contains(RecordType.TEXTTYPE, written);

        //And it reads back paired with its layer rather than as a datatype nobody set.
        var label = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements.Single(element => element.Text is not null);

        Assert.Equal(5, label.Layer.Key.DataType);
    }

    ///<summary>
    ///Centered on the point that was clicked, both ways. The format's own default for a missing
    ///PRESENTATION is left and top, so the record has to be written rather than left out.
    ///</summary>
    [Fact]
    public void A_label_sits_centered_on_the_point_it_was_given()
    {
        var gds = Placed();

        new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 5), new Element.Point(10, 20), "X").Apply();

        var label = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements.Single(element => element.Text is not null);

        Assert.Equal(HorizontalPresentation.Center, label.Presentation.Horizontal);
        Assert.Equal(VerticalPresentation.Middle, label.Presentation.Vertical);
    }

    ///<summary>The undo button says which of the two it would take back, not "Draw" for both.</summary>
    [Fact]
    public void A_label_and_a_shape_are_named_apart()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var shape = new AddElement(gds, top, new LayerKey(70, 0), Square(0, 0, 100));
        var label = new AddElement(gds, top, new LayerKey(70, 5), new Element.Point(0, 0), "A");

        Assert.Equal("Draw", shape.Description);
        Assert.Equal("Label", label.Description);
    }

    [Fact]
    public void Undoing_a_label_puts_the_file_back_exactly()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 5), new Element.Point(0, 0), "PIN"));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    ///
    ///**A GDSII string is ASCII, and the encoder turns anything else into a question mark.**
    ///
    ///Silently: a label typed with a micron sign in it becomes one that reads "?" and nobody finds out until
    ///they open the file somewhere else. Dropped where it is typed instead, so what lands on screen is what
    ///went into the file.
    ///
    [Theory]
    [InlineData("VDD", "VDD")]
    [InlineData("2 µm", "2 m")]
    [InlineData("a—b", "ab")]
    [InlineData("tab\there", "tabhere")]
    [InlineData("", "")]
    public void A_label_keeps_only_what_the_format_can_hold(string typed, string kept)
    {
        Assert.Equal(kept, AddElement.AsAscii(typed));
    }

    [Fact]
    public void A_label_is_capped_at_a_length_a_reader_will_take()
    {
        string kept = AddElement.AsAscii(new string('x', AddElement.LongestLabel + 50));

        Assert.Equal(AddElement.LongestLabel, kept.Length);
    }

    ///<summary>
    ///An odd-length string is padded to even in the record and unpadded on the way back, which is the one
    ///place a label of the wrong length would show up as a trailing NUL in every reader.
    ///</summary>
    [Theory]
    [InlineData("ODD")]
    [InlineData("EVEN")]
    public void A_label_of_either_length_reads_back_as_itself(string says)
    {
        var gds = Placed();

        new AddElement(gds, StructureNamed(gds, "TOP"), new LayerKey(70, 5), new Element.Point(0, 0), says).Apply();

        var label = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements.Single(element => element.Text is not null);

        Assert.Equal(says, label.Text);
    }

    ///<summary>
    ///The PRESENTATION field is numbered from the left, which is the thing that gets written backwards - so
    ///the two ends of it are checked against each other rather than against a hex constant somebody chose.
    ///</summary>
    [Theory]
    [InlineData(HorizontalPresentation.Left, VerticalPresentation.Top, 0)]
    [InlineData(HorizontalPresentation.Center, VerticalPresentation.Middle, 0)]
    [InlineData(HorizontalPresentation.Right, VerticalPresentation.Bottom, 3)]
    [InlineData(HorizontalPresentation.Center, VerticalPresentation.Bottom, 2)]
    public void A_presentation_reads_back_as_what_was_written(
        HorizontalPresentation horizontal,
        VerticalPresentation vertical,
        int font)
    {
        var written = new TextPresentation(horizontal, vertical, font);

        var read = TextPresentation.From(new BitArrayData(written.Encode()));

        Assert.Equal(horizontal, read.Horizontal);
        Assert.Equal(vertical, read.Vertical);
        Assert.Equal(font, read.Font);
    }

    #endregion **********************************************************************



    #region Changing an element's layer *********************************************

    [Fact]
    public void A_shape_moves_onto_the_layer_it_is_given()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        new RelayerElement(gds, top, square, new LayerKey(70, 5)).Apply();

        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements
            .Single(element => element.Source!.Structure == "TOP" && element.Text is null);

        Assert.Equal(new LayerKey(70, 5), drawn.Layer.Key);
    }

    ///<summary>Its geometry and its place are untouched: only what it is for changed.</summary>
    [Fact]
    public void Changing_a_layer_moves_nothing()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        int at = top.Elements.IndexOf(square);
        var before = ((Int4Data)square.Element.XY!.Data!).Values.ToArray();

        new RelayerElement(gds, top, square, new LayerKey(70, 5)).Apply();

        Assert.Equal(before, ((Int4Data)square.Element.XY!.Data!).Values);
        Assert.Equal(at, top.Elements.IndexOf(square));
        Assert.Equal(4, top.Elements.Count);
    }

    ///
    ///**A label moves by its TEXTTYPE, a boundary by its DATATYPE.**
    ///
    ///The format spells the second half of the layer pair differently for every element, so writing a
    ///DATATYPE onto a label would leave it on a pair no reader matches - and it would still *have* a layer,
    ///still draw, and be on the wrong one. The record the element already holds is what gets written.
    ///
    [Fact]
    public void A_label_moves_by_its_own_half_of_the_pair()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        new AddElement(gds, top, new LayerKey(70, 5), new Element.Point(0, 0), "PIN").Apply();

        var label = top.Elements.Last();

        new RelayerElement(gds, top, label, new LayerKey(93, 44)).Apply();

        var reread = new GDS(gds.Serialize());

        var drawn = GdsFlattener.Flatten(reread).Elements.Single(element => element.Text is not null);

        Assert.Equal(new LayerKey(93, 44), drawn.Layer.Key);

        //Still a TEXT with a TEXTTYPE, rather than one that has grown a DATATYPE beside it.
        var types = reread.Records.Select(record => record.Type).ToList();

        Assert.Contains(RecordType.TEXTTYPE, types);
        Assert.Equal(1, types.Count(type => type == RecordType.TEXTTYPE));
    }

    [Fact]
    public void Undoing_a_layer_change_puts_the_file_back_exactly()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        byte[] before = gds.Serialize();

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        var history = new EditHistory();

        history.Do(new RelayerElement(gds, top, square, new LayerKey(70, 5)));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    ///<summary>
    ///A layer the file has never used has to be introduced, or the flattener looks it up, misses, and skips
    ///the shape - which is a shape that is genuinely in the file and nowhere on the screen.
    ///</summary>
    [Fact]
    public void A_shape_moved_onto_a_new_layer_is_still_drawn()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        Assert.False(gds.AdditionalInformation.Layers.ContainsKey(new LayerKey(191, 7)));

        new RelayerElement(gds, top, square, new LayerKey(191, 7)).Apply();

        Assert.Contains(GdsFlattener.Flatten(gds).Elements, element => element.Layer.Key.Number == 191);
    }

    ///<summary>Moved twice and undone twice comes back through where it went, not straight to the end.</summary>
    [Fact]
    public void Two_layer_changes_unwind_one_at_a_time()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        var started = GdsFlattener.Flatten(gds).Elements
            .Single(element => element.Source!.Structure == "TOP" && element.Text is null).Layer.Key;

        var history = new EditHistory();

        history.Do(new RelayerElement(gds, top, square, new LayerKey(70, 5)));
        history.Do(new RelayerElement(gds, top, square, new LayerKey(93, 44)));

        history.Undo();

        Assert.Equal(new LayerKey(70, 5), Only(gds).Layer.Key);

        history.Undo();

        Assert.Equal(started, Only(gds).Layer.Key);
    }

    private static Element Only(GDS gds)
    {
        return GdsFlattener.Flatten(gds).Elements
            .Single(element => element.Source!.Structure == "TOP" && element.Text is null);
    }

    #endregion **********************************************************************



    #region Copying an element ******************************************************

    [Fact]
    public void A_copy_is_the_same_shape_somewhere_else()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);
        var before = ((Int4Data)square.Element.XY!.Data!).Values.ToArray();

        AddElement.CopyOf(gds, top, square, 400, -250)!.Apply();

        var reread = new GDS(gds.Serialize());

        var drawn = GdsFlattener.Flatten(reread).Elements
            .Where(element => element.Source!.Structure == "TOP" && element.Text is null)
            .ToList();

        Assert.Equal(2, drawn.Count);

        //The original is where it was, and the copy is the same corners moved.
        var moved = ((Int4Data)StructureNamed(reread, "TOP").Elements
            .Where(element => element.Element is GDS.BoundaryModel)
            .Last().Element.XY!.Data!).Values;

        for (int i = 0; i + 1 < before.Length; i += 2)
        {
            Assert.Equal(before[i] + 400, moved[i]);
            Assert.Equal(before[i + 1] - 250, moved[i + 1]);
        }
    }

    ///<summary>The one it was copied from does not move, which is what makes it a copy and not a drag.</summary>
    [Fact]
    public void Copying_leaves_the_original_where_it_was()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);
        var before = ((Int4Data)square.Element.XY!.Data!).Values.ToArray();

        AddElement.CopyOf(gds, top, square, 400, -250)!.Apply();

        Assert.Equal(before, ((Int4Data)square.Element.XY!.Data!).Values);
    }

    ///
    ///**A label copies as a label, not as the polygon it never was.**
    ///
    ///The reason a copy is made from an element's records rather than from the corners it happens to draw.
    ///Rebuilding one from its outline would turn every label, path and box in a selection into a boundary -
    ///a file that draws almost the same and says something else entirely.
    ///
    [Fact]
    public void A_copied_label_is_still_a_label_and_still_says_the_same_thing()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        new AddElement(gds, top, new LayerKey(70, 5), new Element.Point(100, 100), "VDD").Apply();

        var label = top.Elements.Last();

        AddElement.CopyOf(gds, top, label, 600, 0)!.Apply();

        var copies = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements
            .Where(element => element.Text is not null)
            .ToList();

        Assert.Equal(2, copies.Count);
        Assert.All(copies, one => Assert.Equal("VDD", one.Text));

        //And it kept how it is justified, which an outline would have thrown away with everything else.
        Assert.All(copies, one => Assert.Equal(HorizontalPresentation.Center, one.Presentation.Horizontal));

        Assert.Equal(copies[0].Points[0].X + 600, copies[1].Points[0].X);
    }

    ///<summary>Every record the element had comes across, in the order it had them.</summary>
    [Fact]
    public void A_copy_carries_the_records_the_element_had()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        new AddElement(gds, top, new LayerKey(70, 5), new Element.Point(0, 0), "A").Apply();

        var label = top.Elements.Last();

        int before = gds.Records.Count;

        AddElement.CopyOf(gds, top, label, 10, 10)!.Apply();

        //TEXT, LAYER, TEXTTYPE, PRESENTATION, XY, STRING, ENDEL.
        Assert.Equal(before + 7, gds.Records.Count);
    }

    [Fact]
    public void Undoing_a_copy_puts_the_file_back_exactly()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        byte[] before = gds.Serialize();

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        var history = new EditHistory();

        history.Do(AddElement.CopyOf(gds, top, square, 400, -250)!);

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    ///<summary>
    ///A copy of a copy is a copy: the records are new objects rather than the ones already in the file, so
    ///moving one afterwards does not drag the other with it.
    ///</summary>
    [Fact]
    public void A_copy_does_not_share_its_coordinates_with_the_original()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        AddElement.CopyOf(gds, top, square, 400, 0)!.Apply();

        var copy = top.Elements.Last();

        new MoveElement(top, copy, 1000, 1000).Apply();

        //The original is untouched by what happened to the copy.
        Assert.Equal(Corners(Placed()).First(), Corners(gds).First());
    }

    ///<summary>An element from another library has no records here, and says so rather than copying nothing.</summary>
    [Fact]
    public void Copying_something_that_is_not_in_this_library_comes_back_as_nothing()
    {
        var gds = Placed();
        var other = Placed();

        var stranger = StructureNamed(other, "TOP").Elements[0];

        Assert.Null(AddElement.CopyOf(gds, StructureNamed(gds, "TOP"), stranger, 10, 10));
    }

    #endregion **********************************************************************



    #region Several at once *********************************************************

    [Fact]
    public void A_compound_edit_applies_all_of_them()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var before = GdsFlattener.Flatten(gds).Elements.Count;

        new CompoundEdit("Draw", new LayoutEdit[]
        {
            new AddElement(gds, top, new LayerKey(70, 0), Square(0, 0, 100)),
            new AddElement(gds, top, new LayerKey(70, 0), Square(200, 0, 100))
        }).Apply();

        Assert.Equal(before + 2, GdsFlattener.Flatten(gds).Elements.Count);
    }

    ///
    ///**Reverted backwards.**
    ///
    ///Undoing forwards happens to work for moves, which do not touch each other, and stops working the
    ///moment the edits do. Deleting three shapes takes them out at three positions; putting the first back
    ///before the third would put it into a list the third has not returned to, at an index that no longer
    ///means what it meant. Asserted byte for byte, which is the only way to see it.
    ///
    [Fact]
    public void Undoing_several_deletions_puts_the_file_back_exactly()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        byte[] before = gds.Serialize();

        //Every element of the top, taken out together - so the order they go back in is the whole test.
        var deletions = top.Elements
            .ToList()
            .Select(element => (LayoutEdit)new DeleteElement(gds, top, element))
            .ToList();

        var history = new EditHistory();

        history.Do(new CompoundEdit("Delete", deletions));

        Assert.Empty(top.Elements);

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void A_compound_of_moves_undoes_and_redoes_as_one_step()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");
        var leaf = StructureNamed(gds, "LEAF");

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new CompoundEdit("Move", new LayoutEdit[]
        {
            new MoveElement(top, 0, 50, 50),
            new MoveElement(leaf, 0, -20, 30)
        }));

        byte[] moved = gds.Serialize();

        Assert.NotEqual(before, moved);

        //One step, not two.
        Assert.Equal(1, history.Count);

        history.Undo();

        Assert.Equal(before, gds.Serialize());
        Assert.False(history.CanUndo);

        history.Redo();

        Assert.Equal(moved, gds.Serialize());
    }

    [Fact]
    public void A_compound_names_how_many_it_holds()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var one = new CompoundEdit("Move", new LayoutEdit[] { new MoveElement(top, 0, 1, 1) });

        Assert.Equal("Move", one.Description);

        var several = new CompoundEdit("Move", new LayoutEdit[]
        {
            new MoveElement(top, 0, 1, 1),
            new MoveElement(top, 0, 1, 1),
            new MoveElement(top, 0, 1, 1)
        });

        Assert.Equal("Move 3 shapes", several.Description);
    }

    #endregion **********************************************************************



    #region Undo and redo ***********************************************************

    ///<summary>
    ///**Byte for byte.** A file edited and then undone has to be the file it was. Anything less is a
    ///history that only mostly works, which is worse than none because it gets trusted.
    ///</summary>
    [Theory]
    [InlineData("move")]
    [InlineData("delete")]
    public void Undoing_puts_the_file_back_exactly(string what)
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        if (what == "move")
            history.Do(new MoveElement(StructureNamed(gds, "LEAF"), 0, 33, -77));
        else
            history.Do(new DeleteElement(gds, StructureNamed(gds, "TOP"), StructureNamed(gds, "TOP").Elements[0]));

        Assert.NotEqual(before, gds.Serialize());

        Assert.True(history.Undo());

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void Redoing_puts_the_change_back()
    {
        var gds = Placed();
        var history = new EditHistory();

        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), 0, 10, 20));

        byte[] edited = gds.Serialize();

        history.Undo();
        history.Redo();

        Assert.Equal(edited, gds.Serialize());
    }

    ///<summary>Several edits come back off in the order they went on, not all at once and not backwards.</summary>
    [Fact]
    public void A_stack_of_edits_unwinds_one_at_a_time()
    {
        var gds = Placed();
        var leaf = StructureNamed(gds, "LEAF").Elements[0];

        byte[] start = gds.Serialize();

        var history = new EditHistory();

        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), leaf, 100, 0));

        byte[] afterFirst = gds.Serialize();

        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), leaf, 0, 100));
        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), leaf, -50, -50));

        Assert.Equal(3, history.Count);

        history.Undo();
        history.Undo();

        Assert.Equal(afterFirst, gds.Serialize());

        history.Undo();

        Assert.Equal(start, gds.Serialize());
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void There_is_nothing_to_undo_or_redo_to_begin_with()
    {
        var history = new EditHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Null(history.NextUndo);
        Assert.False(history.Undo());
        Assert.False(history.Redo());
    }

    ///<summary>
    ///Doing something new drops what was undone. A history that let you redo your way into a future that
    ///no longer follows from the present is worse than one that forgets.
    ///</summary>
    [Fact]
    public void Doing_something_new_forgets_what_was_undone()
    {
        var gds = Placed();
        var leaf = StructureNamed(gds, "LEAF").Elements[0];

        var history = new EditHistory();

        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), leaf, 100, 0));
        history.Undo();

        Assert.True(history.CanRedo);

        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), leaf, 0, 50));

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void The_history_names_what_it_would_take_back()
    {
        var gds = Placed();
        var history = new EditHistory();

        history.Do(new MoveElement(StructureNamed(gds, "LEAF"), 0, 1, 1));

        Assert.Equal("Move", history.NextUndo);

        history.Undo();

        Assert.Equal("Move", history.NextRedo);
        Assert.Null(history.NextUndo);
    }

    ///<summary>
    ///A delete undone puts the element back where it was, not on the end - or the records would be in a
    ///different order and the file would not be the file it was.
    ///</summary>
    [Fact]
    public void An_undone_delete_puts_the_element_back_in_its_place()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        //The first of four, so putting it back on the end would be visible.
        var first = top.Elements[0];

        var history = new EditHistory();

        history.Do(new DeleteElement(gds, top, first));
        history.Undo();

        Assert.Same(first, top.Elements[0]);
        Assert.Equal(Placed().Serialize(), gds.Serialize());
    }

    #endregion **********************************************************************



    #region Retyping a label ********************************************************

    ///<summary>A library with one label in TOP, and that label.</summary>
    private static (GDS Gds, GDS.StructureModel Top, GDS.ElementModel Label) WithALabel(string says)
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        new AddElement(gds, top, new LayerKey(70, 5), new Element.Point(400, 400), says).Apply();

        return (gds, top, top.Elements.Last());
    }

    [Fact]
    public void Retyping_changes_what_a_label_says()
    {
        (GDS gds, GDS.StructureModel top, GDS.ElementModel label) = WithALabel("VDD");

        new RetextElement(top, label, "GND").Apply();

        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize()))
            .Elements.Single(element => element.Text is not null);

        Assert.Equal("GND", drawn.Text);
    }

    ///
    ///**Nothing but the string moves.**
    ///
    ///Which is what makes this a written record rather than a rebuilt element: the label keeps its place in
    ///the file, its anchor, its justification and its identity. A rebuild would be correct and would put it
    ///on the end of the cell with a new model behind it.
    ///
    [Fact]
    public void Retyping_moves_nothing_else()
    {
        (GDS gds, GDS.StructureModel top, GDS.ElementModel label) = WithALabel("VDD");

        int at = top.Elements.IndexOf(label);
        int[] where = ((Int4Data)label.Element.XY!.Data!).Values.ToArray();

        new RetextElement(top, label, "A_MUCH_LONGER_NAME").Apply();

        Assert.Same(label, top.Elements[at]);
        Assert.Equal(where, ((Int4Data)label.Element.XY!.Data!).Values);

        //Still one TEXT with one PRESENTATION, rather than a second label beside the first.
        var types = gds.Records.Select(record => record.Type).ToList();

        Assert.Equal(1, types.Count(type => type == RecordType.TEXT));
        Assert.Equal(1, types.Count(type => type == RecordType.PRESENTATION));
    }

    [Fact]
    public void Undoing_a_retype_puts_the_file_back_exactly()
    {
        (GDS gds, GDS.StructureModel top, GDS.ElementModel label) = WithALabel("VDD");

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new RetextElement(top, label, "GND"));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void Redoing_a_retype_puts_the_new_words_back()
    {
        (GDS gds, GDS.StructureModel top, GDS.ElementModel label) = WithALabel("VDD");

        var history = new EditHistory();

        history.Do(new RetextElement(top, label, "GND"));

        byte[] retyped = gds.Serialize();

        history.Undo();
        history.Redo();

        Assert.Equal(retyped, gds.Serialize());
    }

    ///<summary>GDSII holds plain ASCII, so anything else is dropped on the way in rather than written out.</summary>
    [Fact]
    public void Retyping_keeps_only_what_the_format_holds()
    {
        (GDS gds, GDS.StructureModel top, GDS.ElementModel label) = WithALabel("VDD");

        new RetextElement(top, label, "VÉÉ").Apply();

        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize()))
            .Elements.Single(element => element.Text is not null);

        Assert.Equal("V", drawn.Text);
    }

    ///<summary>A boundary has nothing to say, and asking is how the panel knows not to offer the box.</summary>
    [Fact]
    public void Only_a_label_has_words_to_read()
    {
        var gds = Placed();
        var top = StructureNamed(gds, "TOP");

        var square = top.Elements.Single(element => element.Element is GDS.BoundaryModel);

        Assert.Null(RetextElement.TextOf(square));
    }

    ///
    ///**A retype survives the file being closed.**
    ///
    ///What a label said is not on the file once it has been changed, so both ends go into the session - the
    ///same shape a layer change takes, and for the same reason.
    ///
    [Fact]
    public void A_retype_can_be_written_down_and_read_back()
    {
        (GDS gds, GDS.StructureModel top, GDS.ElementModel label) = WithALabel("VDD");

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new RetextElement(top, label, "GND"));

        var written = history.Describe();

        //A new parse of the changed file, as reopening one gives.
        var reopened = new GDS(gds.Serialize());

        var restored = new EditHistory();

        restored.Restore(reopened, written);

        restored.Undo();

        Assert.Equal(before, reopened.Serialize());

        restored.Redo();

        Assert.Equal("GND", GdsFlattener.Flatten(reopened).Elements.Single(element => element.Text is not null).Text);
    }

    #endregion **********************************************************************

}
