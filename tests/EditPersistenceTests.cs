using System.Text.Json;

using GdsII;
using GDSViewer.Models;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///An undo stack outliving the page it was made on.
///
///**The refresh is the test.** Every one of these does the edits, writes the stack down, throws the library
///away and parses the *edited bytes* back the way a reload does - and only then asks whether undo still
///works. Asserting against the same library the edits were made on would pass with the stack holding
///references to objects that a real reload does not have, which is the whole difficulty.
///
///And the answer has to be exact. A file edited, closed, reopened and undone has to be the file it was, byte
///for byte; a restored history that only mostly works is worse than none, because it gets trusted.
///</summary>
public class EditPersistenceTests
{
    #region Libraries to edit *******************************************************

    ///<summary>A leaf with one square, placed three times, plus a square of the top's own.</summary>
    private static GDS Placed()
    {
        return new GDS(GdsTestData.ReadFixture("placed.gds"));
    }

    private static GDS.StructureModel Named(GDS gds, string name)
    {
        return gds.StreamFormat.Structures.Single(structure =>
            ((AsciiData)structure.STRNAME.Data!).Value == name);
    }

    ///<summary>
    ///A top whose one placement is turned a quarter and mirrored, so a deleted element carries records an
    ///outline would not: STRANS, ANGLE and the name of the cell it places.
    ///</summary>
    private static GDS Turned()
    {
        byte[] stamps = GdsTestData.Timestamps();

        return new GDS(GdsTestData.Concat(
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
            GdsTestData.Record(RecordType.STRANS, new byte[] { 0x80, 0x00 }),
            GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(90)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));
    }

    ///<summary>
    ///What a refresh does: the edited file is written out, and everything else is thrown away. The bytes and
    ///the written-down stack are all that cross.
    ///</summary>
    private static (GDS Reopened, EditHistory History) Reload(GDS edited, SavedEdits saved)
    {
        var reopened = new GDS(edited.Serialize());
        var restored = new EditHistory();

        restored.Restore(reopened, saved);

        return (reopened, restored);
    }

    ///<summary>The same, but through the JSON a session is actually stored as.</summary>
    private static SavedEdits ThroughJson(SavedEdits saved)
    {
        var session = new SavedSession { Edits = saved };

        return SavedSession.Deserialize(SavedSession.Serialize(session))!.Edits!;
    }

    private static int UndoEverything(EditHistory history)
    {
        int steps = 0;

        while (history.Undo())
            steps++;

        return steps;
    }

    #endregion **********************************************************************



    #region One edit at a time ******************************************************

    [Theory]
    [InlineData("move")]
    [InlineData("vertex")]
    [InlineData("draw")]
    [InlineData("delete")]
    public void An_edit_can_be_undone_after_the_file_has_been_reopened(string what)
    {
        var gds = Placed();

        byte[] original = gds.Serialize();

        var history = new EditHistory();
        var leaf = Named(gds, "LEAF");
        var top = Named(gds, "TOP");

        if (what == "move")
            history.Do(new MoveElement(leaf, leaf.Elements[0], 33, -77));
        else if (what == "vertex")
            history.Do(new MoveVertex(leaf, leaf.Elements[0], 1, 15, 25));
        else if (what == "draw")
            history.Do(new AddElement(gds, top, new LayerKey(70, 0), Square(0, 0, 400)));
        else
            history.Do(new DeleteElement(gds, top, top.Elements[0]));

        Assert.NotEqual(original, gds.Serialize());

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.True(restored.CanUndo);
        Assert.True(restored.Undo());

        Assert.Equal(original, reopened.Serialize());
    }

    [Theory]
    [InlineData("move")]
    [InlineData("draw")]
    [InlineData("delete")]
    public void And_redone_again_afterwards(string what)
    {
        var gds = Placed();

        var history = new EditHistory();
        var leaf = Named(gds, "LEAF");
        var top = Named(gds, "TOP");

        if (what == "move")
            history.Do(new MoveElement(leaf, leaf.Elements[0], 33, -77));
        else if (what == "draw")
            history.Do(new AddElement(gds, top, new LayerKey(70, 0), Square(0, 0, 400)));
        else
            history.Do(new DeleteElement(gds, top, top.Elements[0]));

        byte[] edited = gds.Serialize();

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        restored.Undo();

        Assert.True(restored.CanRedo);
        Assert.True(restored.Redo());

        Assert.Equal(edited, reopened.Serialize());
    }

    ///<summary>
    ///**What was undone before the refresh is still undone after it, and still redoable.**
    ///
    ///The other stack, and the one that is easy to get backwards: these edits are *not* applied to the file
    ///that was saved, so a restored one has to start by going forwards rather than back.
    ///</summary>
    [Fact]
    public void What_had_been_undone_can_still_be_redone_after_a_reload()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        var history = new EditHistory();

        history.Do(new MoveElement(leaf, leaf.Elements[0], 40, 40));

        byte[] wanted = gds.Serialize();

        history.Undo();

        byte[] saved = gds.Serialize();

        Assert.NotEqual(wanted, saved);

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.False(restored.CanUndo);
        Assert.True(restored.CanRedo);

        restored.Redo();

        Assert.Equal(wanted, reopened.Serialize());
    }

    #endregion **********************************************************************



    #region A stack of them *********************************************************

    ///
    ///**The one that says whether the indexes were taken at the right moment.**
    ///
    ///Three edits in one structure, with a deletion in the middle of them that shifts everything after it up
    ///by one. An edit has to record where its element sat *when that edit ran* - because undo walks the stack
    ///backwards, and by the time it reaches one the library is in exactly the state that edit left. Writing
    ///down where a shape sits at save time instead looks perfectly reasonable, and undoes the wrong element
    ///the moment anything below it changed the numbering.
    ///
    [Fact]
    public void A_stack_whose_indexes_shifted_still_unwinds_onto_the_right_shapes()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        //The last element, then the first taken out from under it, then the last again - which is now at a
        //different index than it was two edits ago.
        history.Do(new MoveElement(top, top.Elements[^1], 10, 20));
        history.Do(new DeleteElement(gds, top, top.Elements[0]));
        history.Do(new MoveElement(top, top.Elements[^1], 5, 5));

        var written = ThroughJson(history.Describe());

        //And the numbering really did move, or this proves nothing.
        Assert.Equal(3, written.Done.Count);
        Assert.NotEqual(written.Done[0].At, written.Done[2].At);

        (var reopened, var restored) = Reload(gds, written);

        Assert.Equal(3, UndoEverything(restored));

        Assert.Equal(original, reopened.Serialize());
    }

    ///<summary>The same the other way: everything undone, then everything put back.</summary>
    [Fact]
    public void And_winds_back_up_again()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        var history = new EditHistory();

        history.Do(new MoveElement(top, top.Elements[^1], 10, 20));
        history.Do(new DeleteElement(gds, top, top.Elements[0]));
        history.Do(new AddElement(gds, top, new LayerKey(70, 0), Square(0, 0, 250)));

        byte[] edited = gds.Serialize();

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        UndoEverything(restored);

        while (restored.Redo())
        {
        }

        Assert.Equal(edited, reopened.Serialize());
    }

    ///
    ///**Three deletions made one at a time, which is not the same as three made together.**
    ///
    ///Each is its own step on the stack, so each records where its element sat at its own moment - and those
    ///three moments have three different numberings, because every deletion before one shifts everything
    ///after it. The group case above happens to hide this: all four of those run back to back against a
    ///state nothing else has touched.
    ///
    [Fact]
    public void Deletions_made_one_at_a_time_all_come_back()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        //The first each time, which is what clicking the topmost shape three times over comes to.
        for (int i = 0; i < 3; i++)
            history.Do(new CompoundEdit("Delete", new LayoutEdit[] { new DeleteElement(gds, top, top.Elements[0]) }));

        Assert.Single(top.Elements);

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.Equal(3, UndoEverything(restored));

        Assert.Equal(original, reopened.Serialize());
    }

    ///<summary>And the same taking them from the end rather than from the front.</summary>
    [Fact]
    public void Deletions_from_the_end_come_back_too()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        for (int i = 0; i < 3; i++)
            history.Do(new CompoundEdit("Delete", new LayoutEdit[] { new DeleteElement(gds, top, top.Elements[^1]) }));

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.Equal(3, UndoEverything(restored));

        Assert.Equal(original, reopened.Serialize());
    }

    ///<summary>A group made in one gesture is still one step after a reload, not several.</summary>
    [Fact]
    public void A_group_comes_back_as_one_step()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        var deletions = top.Elements
            .ToList()
            .Select(element => (LayoutEdit)new DeleteElement(gds, top, element))
            .ToList();

        history.Do(new CompoundEdit("Delete", deletions));

        Assert.Empty(top.Elements);

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.Equal("Delete 4 shapes", restored.NextUndo);
        Assert.Equal(1, UndoEverything(restored));

        Assert.Equal(original, reopened.Serialize());
    }

    #endregion **********************************************************************



    #region What has to be carried **************************************************

    ///
    ///**A deleted element comes back with everything it had, not with an outline.**
    ///
    ///The reason the stack stores an element's records rather than its shape. This placement is turned a
    ///quarter and mirrored; writing down where its corners ended up on screen would put back an SREF with no
    ///STRANS and no ANGLE - a file that parses, draws something plausible, and is wrong.
    ///
    [Fact]
    public void A_deleted_placement_comes_back_turned_and_mirrored_as_it_was()
    {
        var gds = Turned();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        history.Do(new DeleteElement(gds, top, top.Elements[0]));

        //Nothing reaches the screen through a placement any more. The cell itself still draws - with nothing
        //referring to it, the flattener treats it as a top of its own - which is why this asks about the
        //chain rather than about the count.
        Assert.DoesNotContain(GdsFlattener.Flatten(gds).Elements, element => element.Source!.Depth > 0);

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        restored.Undo();

        Assert.Equal(original, reopened.Serialize());

        //And it draws through the placement again, turned and mirrored as it was.
        var drawn = GdsFlattener.Flatten(reopened).Elements.Single(element => element.Source!.Depth > 0);

        Assert.Equal("TOP", drawn.Source!.Path[0]);
    }

    ///
    ///**Undoing the deletion of the last shape on a layer puts it back on the screen, not only in the file.**
    ///
    ///Found in a browser and only visible after a reload. The flattener draws an element by looking its layer
    ///up in the table the parser built from the file - and a file saved with that shape deleted has no such
    ///layer in it, so the table has no row. Putting the shape back then produced a file that genuinely
    ///carried it, that survived a save and reload, and that drew nothing at all where it should be.
    ///
    ///The file being right is what makes this worth a test of its own: everything that checks bytes passed.
    ///
    [Fact]
    public void Undoing_the_deletion_of_a_layer_last_shape_draws_it_again()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        //The top's own square is the only thing on its layer, so deleting it takes the layer out of the file.
        var only = top.Elements.Single(element => element.Element is GDS.BoundaryModel);
        var layer = GdsFlattener.Flatten(gds).Elements
            .Single(element => element.Source!.Structure == "TOP").Layer.Key;

        var history = new EditHistory();

        history.Do(new DeleteElement(gds, top, only));

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        //The reopened file has never heard of that layer, which is the whole trap.
        Assert.False(reopened.AdditionalInformation.Layers.ContainsKey(layer));

        restored.Undo();

        Assert.Contains(GdsFlattener.Flatten(reopened).Elements, element => element.Layer.Key.Equals(layer));
    }

    ///
    ///A layer change comes back, both ways.
    ///
    ///The other edit that stores what it was rather than a way to work it out: where a shape came from is
    ///not derivable from where it went, so both ends travel.
    ///
    [Fact]
    public void A_layer_change_survives_a_reload()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var square = top.Elements.First(element => element.Element is GDS.BoundaryModel);

        var history = new EditHistory();

        history.Do(new RelayerElement(gds, top, square, new LayerKey(93, 44)));

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.Equal("Change layer", restored.NextUndo);

        restored.Undo();

        Assert.Equal(original, reopened.Serialize());

        restored.Redo();

        Assert.Contains(GdsFlattener.Flatten(reopened).Elements,
            element => element.Layer.Key.Equals(new LayerKey(93, 44)));
    }

    ///
    ///A turn comes back, and comes back exact.
    ///
    ///The one edit that stores both ends of what it did rather than one end and a way to compute the other.
    ///A quarter turn is exactly reversible about a whole-numbered point in a cell placed square, and only
    ///then - so the stack carries what was there, and undoing writes it back rather than turning again.
    ///
    [Fact]
    public void A_turn_survives_a_reload_and_undoes_exactly()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        byte[] original = gds.Serialize();

        var square = leaf.Elements[0];
        var context = CellContext.At(GdsFlattener.Flatten(gds).Elements
            .First(element => element.Source!.Structure == "LEAF").Source!);

        var after = Turning.Coordinates(context, square, Turn.Quarter, 0, 0);

        Assert.NotNull(after);

        var history = new EditHistory();

        history.Do(new ReshapeElement(leaf, square, after, "Turn right"));

        byte[] turned = gds.Serialize();

        Assert.NotEqual(original, turned);

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.Equal("Turn right", restored.NextUndo);

        restored.Undo();

        Assert.Equal(original, reopened.Serialize());

        restored.Redo();

        Assert.Equal(turned, reopened.Serialize());
    }

    ///
    ///A label comes back as a label, and the button still calls it one.
    ///
    ///Both halves matter. The records carry a TEXT rather than a BOUNDARY, which is the same reason a deleted
    ///placement is stored as its records; and what the edit was *called* is carried too, or an undo restored
    ///from a session would offer to take back a "Draw" that was never a shape.
    ///
    [Fact]
    public void A_label_survives_a_reload_as_a_label()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddElement(gds, top, new LayerKey(70, 5), new Element.Point(250, 250), "PIN"));

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        Assert.Equal("Label", restored.NextUndo);

        restored.Undo();

        Assert.Equal(original, reopened.Serialize());

        restored.Redo();

        var label = GdsFlattener.Flatten(reopened).Elements.Single(element => element.Text is not null);

        Assert.Equal("PIN", label.Text);
    }

    ///<summary>
    ///A shape drawn on a layer the file had none of, undone and then redone after a reload, is drawn again -
    ///the flattener skips an element whose layer it cannot look up, so the layer has to be reintroduced.
    ///</summary>
    [Fact]
    public void A_redrawn_shape_on_a_new_layer_is_still_visible()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        var history = new EditHistory();

        history.Do(new AddElement(gds, top, new LayerKey(191, 7), Square(0, 0, 300)));

        (var reopened, var restored) = Reload(gds, ThroughJson(history.Describe()));

        restored.Undo();

        Assert.DoesNotContain(GdsFlattener.Flatten(reopened).Elements, element => element.Layer.Key.Number == 191);

        restored.Redo();

        Assert.Contains(GdsFlattener.Flatten(reopened).Elements, element => element.Layer.Key.Number == 191);
    }

    #endregion **********************************************************************



    #region What is left out ********************************************************

    ///
    ///**An edit that does not fit the file takes everything below it, and only what is below it.**
    ///
    ///A hole in the middle of a stack cannot be closed up: the edits above one record where things sat *with
    ///it applied*, and the ones below record where things sat without it. Only one of those two halves can
    ///still be trusted, and it is the top - which is also the half somebody is about to reach for.
    ///
    ///The three moves are deliberately different distances. Keeping the wrong half also leaves exactly one
    ///edit on the stack, so a test that only counted them would pass either way; what says which half
    ///survived is how far the shape goes back.
    ///
    [Fact]
    public void An_edit_that_does_not_fit_the_file_takes_the_ones_below_it_with_it()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        var history = new EditHistory();

        history.Do(new MoveElement(leaf, leaf.Elements[0], 1, 0));
        history.Do(new MoveElement(leaf, leaf.Elements[0], 20, 0));
        history.Do(new MoveElement(leaf, leaf.Elements[0], 300, 0));

        var written = history.Describe();

        //The middle one now names a cell that is not there.
        written.Done[1].Structure = "NOWHERE";

        (var reopened, var restored) = Reload(gds, written);

        Assert.Equal(1, restored.Count);

        int before = FirstCorner(reopened);

        Assert.Equal(1, UndoEverything(restored));

        //The newest is what came back, so 300 comes off - not the 1 from the far side of the hole.
        Assert.Equal(before - 300, FirstCorner(reopened));
    }

    ///<summary>Where the leaf's square starts, which is what a move of it shows up in.</summary>
    private static int FirstCorner(GDS gds)
    {
        var leaf = Named(gds, "LEAF");

        return ((Int4Data)leaf.Elements[0].Element.XY!.Data!).Values[0];
    }

    [Fact]
    public void An_edit_pointing_past_the_end_of_a_structure_is_left_out()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        var history = new EditHistory();

        history.Do(new MoveElement(leaf, leaf.Elements[0], 1, 1));

        var written = history.Describe();

        written.Done[0].At = 99;

        (var reopened, var restored) = Reload(gds, written);

        //Rebuilt, because a place past the end is only knowable against the live structure - and then
        //refusing to do anything with it, which is what matters.
        byte[] before = reopened.Serialize();

        restored.Undo();

        Assert.Equal(before, reopened.Serialize());
    }

    ///<summary>
    ///A stack is bounded, because it is written to a browser's storage on every edit. The newest steps are
    ///the ones kept - they are what somebody is about to reach for, and the only end it is safe to cut.
    ///</summary>
    [Fact]
    public void Only_so_many_steps_are_carried_and_they_are_the_newest()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        var history = new EditHistory();

        for (int i = 0; i < EditHistory.MostSteps + 20; i++)
            history.Do(new MoveElement(leaf, leaf.Elements[0], 1, 0));

        var written = history.Describe();

        Assert.Equal(EditHistory.MostSteps, written.Done.Count);

        (var reopened, var restored) = Reload(gds, written);

        Assert.Equal(EditHistory.MostSteps, restored.Count);

        UndoEverything(restored);

        //Back by exactly what was carried, and no further: the twenty oldest steps stay done.
        var shape = GdsFlattener.Flatten(reopened).Elements.First(element => element.Source!.Structure == "LEAF");
        var original = GdsFlattener.Flatten(Placed()).Elements.First(element => element.Source!.Structure == "LEAF");

        Assert.Equal(original.Points[0].X + 20, shape.Points[0].X);
    }

    ///<summary>An edit that was never applied does not know where it acted, so it is not written down.</summary>
    [Fact]
    public void An_edit_that_never_ran_cannot_be_written_down()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        Assert.Null(new MoveElement(leaf, leaf.Elements[0], 1, 1).Describe());
        Assert.Null(new DeleteElement(gds, leaf, leaf.Elements[0]).Describe());
        Assert.Null(new AddElement(gds, leaf, new LayerKey(1, 0), Square(0, 0, 10)).Describe());
    }

    #endregion **********************************************************************



    #region Through a session *******************************************************

    ///<summary>
    ///A session with no stack in it is a session from before there was one, and opens with an empty history
    ///rather than being thrown away for the shape it is.
    ///</summary>
    [Fact]
    public void A_session_written_before_this_existed_still_opens()
    {
        string json = JsonSerializer.Serialize(new SavedSession { FileName = "a.gds" });

        Assert.DoesNotContain("\"u\"", json);

        var read = SavedSession.Deserialize(json);

        Assert.NotNull(read);
        Assert.Null(read.Edits);
    }

    ///
    ///**A move costs the same however big the shape it moves.**
    ///
    ///The whole reason this is a list of changes rather than a list of copies of the file. A thousand-corner
    ///polygon dragged fifty times is fifty pairs of numbers; the shape itself is never written down, because
    ///the file already has it. Two libraries whose only difference is how many corners that polygon has, and
    ///the stacks come out the same size to the character.
    ///
    [Fact]
    public void A_move_costs_the_same_whatever_it_moves()
    {
        static string StackOver(int corners)
        {
            var gds = Placed();
            var top = Named(gds, "TOP");

            var outline = new List<Element.Point>();

            for (int i = 0; i < corners; i++)
                outline.Add(new Element.Point(i * 10, (i % 7) * 10));

            new AddElement(gds, top, new LayerKey(70, 0), outline).Apply();

            var history = new EditHistory();

            for (int i = 0; i < 50; i++)
                history.Do(new MoveElement(top, top.Elements[^1], 1, 1));

            return JsonSerializer.Serialize(history.Describe());
        }

        Assert.Equal(StackOver(4).Length, StackOver(1000).Length);
    }

    #endregion **********************************************************************



    #region Helpers *****************************************************************

    private static List<Element.Point> Square(int x, int y, int size)
    {
        return new List<Element.Point>
        {
            new Element.Point(x, y),
            new Element.Point(x + size, y),
            new Element.Point(x + size, y + size),
            new Element.Point(x, y + size)
        };
    }

    #endregion **********************************************************************
}
