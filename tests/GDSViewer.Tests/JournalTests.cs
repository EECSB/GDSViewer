using GdsII;
using GDSViewer.Models;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///A file kept as it was last written whole and the steps taken since, rather than written whole after every edit.
///
///**The file is the test.** Every one of these writes the file out, takes some steps, and then opens what was
///written and takes the journal's steps again on it - and what comes out has to be the file as it now stands,
///byte for byte. A journal that mostly works is worse than none, because it gets trusted with somebody's edits.
///
///Through the JSON a session is stored as, since that is the form the steps cross a reload in.
///</summary>
public class JournalTests
{
    #region Helpers *****************************************************************

    ///<summary>A leaf with one square, placed three times, plus a square of the top's own.</summary>
    private static GDS Placed()
    {
        return new GDS(GdsTestData.ReadFixture("placed.gds"));
    }

    ///<summary>Twenty-one SkullFET inverters in a ring: a real design, with labels, whose cells are placed over and over.</summary>
    private static GDS Bones()
    {
        return new GDS(GdsTestData.ReadSample(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds")));
    }

    private static GDS.StructureModel Named(GDS gds, string name)
    {
        return gds.StreamFormat.Structures.Single(structure =>
            ((AsciiData)structure.STRNAME.Data!).Value == name);
    }

    ///<summary>The journal as a session stores it, and read back.</summary>
    private static List<SavedStep> ThroughJson(IReadOnlyList<SavedStep> steps)
    {
        var session = new SavedSession { Journal = steps.ToList() };

        return SavedSession.Deserialize(SavedSession.Serialize(session))!.Journal!;
    }

    ///<summary>What a reload does: the bytes written whole opened again, and the journal's steps taken on them.</summary>
    private static byte[] TakenAgain(byte[] written, EditHistory history)
    {
        Assert.NotNull(history.Journal);

        var reopened = new GDS(written);

        Assert.True(EditHistory.Replay(reopened, ThroughJson(history.Journal!)));

        return reopened.Serialize();
    }

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

    ///<summary>One boundary's records, for a cell made from nothing.</summary>
    private static List<GDS.Record> SquareRecords(int size)
    {
        return new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.BOUNDARY, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(70)),
            Hierarchy.Make(RecordType.DATATYPE, new Int2Data(0)),
            Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { 0, 0, size, 0, size, size, 0, size, 0, 0 })),
            Hierarchy.Make(RecordType.ENDEL, null)
        };
    }

    #endregion **********************************************************************



    #region Kept once a save says where it starts ***********************************

    ///<summary>A history nobody saves - the command line's - pays nothing for a journal.</summary>
    [Fact]
    public void There_is_none_until_the_file_has_been_written_whole()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");
        var history = new EditHistory();

        history.Do(new MoveElement(leaf, leaf.Elements[0], 10, 10));

        Assert.Null(history.Journal);
    }

    [Fact]
    public void Written_starts_an_empty_one_and_every_step_after_goes_in()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");
        var history = new EditHistory();

        history.Do(new MoveElement(leaf, leaf.Elements[0], 10, 10));
        history.Written();

        Assert.Empty(history.Journal!);

        history.Do(new MoveElement(leaf, leaf.Elements[0], 5, 5));
        history.Undo();
        history.Redo();

        Assert.Equal(new[] { false, true, false }, history.Journal!.Select(step => step.Undone));
        Assert.Equal(0, history.JournalWeight);
    }

    #endregion **********************************************************************



    #region Each edit, taken again **************************************************

    ///<summary>One of each edit there is, on the small fixture, so what each one does is easy to see.</summary>
    private static void TakeOne(GDS gds, EditHistory history, string kind)
    {
        var leaf = Named(gds, "LEAF");
        var top = Named(gds, "TOP");

        if (kind == "move")
            history.Do(new MoveElement(leaf, leaf.Elements[0], 33, -77));
        else if (kind == "vertex")
            history.Do(new MoveVertex(leaf, leaf.Elements[0], 0, 15, 25));
        else if (kind == "turn")
        {
            var xy = ((Int4Data)leaf.Elements[0].Element.XY!.Data!).Values;
            var turned = new int[xy.Length];

            for (int i = 0; i + 1 < xy.Length; i += 2)
            {
                turned[i] = -xy[i + 1];
                turned[i + 1] = xy[i];
            }

            history.Do(new ReshapeElement(leaf, leaf.Elements[0], turned, "Turn"));
        }
        else if (kind == "layer")
            history.Do(new RelayerElement(gds, leaf, leaf.Elements[0], new LayerKey(250, 7)));
        else if (kind == "label")
        {
            var label = new AddElement(gds, top, new LayerKey(70, 0), new Element.Point(40, 40), "IN");

            history.Do(label);
            history.Do(new RetextElement(top, label.Made!, "OUT"));
        }
        else if (kind == "draw")
            history.Do(new AddElement(gds, top, new LayerKey(70, 0), Square(0, 0, 400)));
        else if (kind == "path")
            history.Do(new AddElement(gds, top, new LayerKey(70, 0), new List<Element.Point> { new Element.Point(0, 0), new Element.Point(500, 0), new Element.Point(500, 300) }, 40, Paths.Ends.Flush));
        else if (kind == "delete")
            history.Do(new DeleteElement(gds, top, top.Elements[0]));
        else if (kind == "place")
            history.Do(new AddElement(gds, top, Hierarchy.PlacementRecords("LEAF", new Element.Point(900, 900), false, 0), "Place"));
        else if (kind == "cell")
            history.Do(new AddStructure(gds, "MORE", SquareRecords(60)));
        else if (kind == "rename")
            history.Do(new RenameStructure(gds, "LEAF", "LEAF2"));
        else if (kind == "remove cell")
            history.Do(new RemoveStructure(gds, "LEAF"));
        else
        {
            var placements = top.Elements.Where(each => each.Element is GDS.SrefModel).ToList();

            history.Do(new CompoundEdit("Delete", placements.Select(each => (LayoutEdit)new DeleteElement(gds, top, each))));
        }
    }

    [Theory]
    [InlineData("move")]
    [InlineData("vertex")]
    [InlineData("turn")]
    [InlineData("layer")]
    [InlineData("label")]
    [InlineData("draw")]
    [InlineData("path")]
    [InlineData("delete")]
    [InlineData("place")]
    [InlineData("cell")]
    [InlineData("rename")]
    [InlineData("remove cell")]
    [InlineData("group")]
    public void Each_edit_taken_again_on_the_file_as_written_is_the_file_as_it_is(string kind)
    {
        var gds = Placed();
        var history = new EditHistory();

        byte[] written = gds.Serialize();

        history.Written();

        TakeOne(gds, history, kind);

        byte[] now = gds.Serialize();

        Assert.NotEqual(written, now);
        Assert.Equal(now, TakenAgain(written, history));
    }

    ///<summary>And each taken back again, which a journal holds as a step of its own.</summary>
    [Theory]
    [InlineData("move")]
    [InlineData("layer")]
    [InlineData("label")]
    [InlineData("draw")]
    [InlineData("delete")]
    [InlineData("cell")]
    [InlineData("rename")]
    [InlineData("remove cell")]
    [InlineData("group")]
    public void Each_edit_taken_back_comes_out_as_the_file_it_was(string kind)
    {
        var gds = Placed();
        var history = new EditHistory();

        byte[] written = gds.Serialize();

        history.Written();

        TakeOne(gds, history, kind);

        int steps = 0;

        while (history.Undo())
            steps++;

        Assert.True(steps > 0);
        Assert.Equal(written, gds.Serialize());
        Assert.Equal(written, TakenAgain(written, history));
    }

    #endregion **********************************************************************



    #region Undo and redo between the writings **************************************

    ///
    ///**A run of edits on a real design, undone and redone and branched**, and the journal still says the file.
    ///
    ///The undo stack and the journal part company here: a new edit after an undo clears the redo stack, but the
    ///steps that led to it - the edit and its undoing - are still how the file got where it is.
    ///
    [Fact]
    public void A_run_of_steps_with_undos_and_a_branch_comes_out_as_the_file()
    {
        var gds = Bones();
        var history = new EditHistory();

        byte[] written = gds.Serialize();

        history.Written();

        var cell = gds.StreamFormat.Structures.First(each => each.Elements.Count(element => element.Element is GDS.BoundaryModel) >= 4);
        var shapes = cell.Elements.Where(element => element.Element is GDS.BoundaryModel).Take(4).ToList();

        history.Do(new MoveElement(cell, shapes[0], 120, -40));
        history.Do(new DeleteElement(gds, cell, shapes[1]));
        history.Do(new MoveVertex(cell, shapes[2], 1, 30, 30));
        history.Undo();
        history.Undo();
        history.Redo();
        history.Do(new AddElement(gds, cell, new LayerKey(68, 20), Square(-500, -500, 250)));
        history.Do(new RelayerElement(gds, cell, shapes[3], new LayerKey(69, 20)));
        history.Undo();

        Assert.Equal(gds.Serialize(), TakenAgain(written, history));
    }

    ///
    ///**An edit made before the file was written, taken back after it.** The bytes have the edit in them, so the
    ///step that takes it back is what the journal has to carry - and the stack below the writing is what makes
    ///such a step possible at all.
    ///
    [Fact]
    public void An_edit_from_before_the_writing_taken_back_after_it()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");
        var history = new EditHistory();

        history.Do(new MoveElement(leaf, leaf.Elements[0], 40, 40));
        history.Do(new AddElement(gds, leaf, new LayerKey(70, 0), Square(200, 200, 50)));

        byte[] written = gds.Serialize();

        history.Written();

        history.Undo();
        history.Undo();

        Assert.Equal(new[] { true, true }, history.Journal!.Select(step => step.Undone));
        Assert.Equal(gds.Serialize(), TakenAgain(written, history));
    }

    ///
    ///**A cell made and then drawn in.** A step is written down as it is taken, not when the journal is - which
    ///matters here, where the cell's records and the shape in it are the same objects: written down after the
    ///move, the cell would carry the moved shape, and taking the move again would move it twice.
    ///
    [Fact]
    public void A_cell_made_and_then_drawn_in_is_written_down_as_it_was_made()
    {
        var gds = Placed();
        var history = new EditHistory();

        byte[] written = gds.Serialize();

        history.Written();

        history.Do(new AddStructure(gds, "MORE", SquareRecords(60)));

        var more = Named(gds, "MORE");

        history.Do(new MoveElement(more, more.Elements[0], 15, 15));

        Assert.Equal(gds.Serialize(), TakenAgain(written, history));
    }

    #endregion **********************************************************************



    #region What ends one ***********************************************************

    ///<summary>A Clear is another file, and a Restore another past: neither is a step this file took.</summary>
    [Fact]
    public void A_clear_or_a_restore_ends_it()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");
        var history = new EditHistory();

        history.Written();
        history.Do(new MoveElement(leaf, leaf.Elements[0], 10, 10));
        history.Clear();

        Assert.Null(history.Journal);

        history.Written();
        history.Restore(gds, new SavedEdits());

        Assert.Null(history.Journal);
    }

    ///<summary>A step that cannot be written down leaves nothing that would say the file - so nothing at all.</summary>
    [Fact]
    public void A_step_that_cannot_be_written_down_ends_it()
    {
        var gds = Placed();
        var history = new EditHistory();

        history.Written();

        //A cell that is not there: it takes nothing out, and has nothing to say it did.
        history.Do(new RemoveStructure(gds, "NOWHERE"));

        Assert.Null(history.Journal);
        Assert.Equal(0, history.JournalWeight);
    }

    ///<summary>Steps from one file do not go onto another, and say so rather than going on regardless.</summary>
    [Fact]
    public void Steps_that_do_not_fit_the_file_are_refused()
    {
        var gds = Placed();
        var history = new EditHistory();

        history.Written();

        TakeOne(gds, history, "move");

        Assert.False(EditHistory.Replay(Bones(), history.Journal!));
    }

    ///<summary>A deletion carries the shape it took out, and the weight says so - which is what bounds a journal of them.</summary>
    [Fact]
    public void Its_weight_is_what_its_deletions_carry()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");
        var history = new EditHistory();

        history.Written();
        history.Do(new MoveElement(top, top.Elements[0], 1, 1));

        Assert.Equal(0, history.JournalWeight);

        history.Do(new DeleteElement(gds, top, top.Elements[0]));

        Assert.True(history.JournalWeight > 0);

        history.Written();

        Assert.Equal(0, history.JournalWeight);
    }

    #endregion **********************************************************************



    #region In a session ************************************************************

    ///<summary>The steps and the name of the writing they belong to, through the JSON a session is stored as.</summary>
    [Fact]
    public void A_journal_and_its_writing_survive_the_session()
    {
        var gds = Placed();
        var history = new EditHistory();

        history.Written();

        TakeOne(gds, history, "label");

        var session = new SavedSession { BytesApart = true, Base = "a1b2c3", Journal = history.Journal!.ToList() };
        var read = SavedSession.Deserialize(SavedSession.Serialize(session))!;

        Assert.Equal("a1b2c3", read.Base);
        Assert.Equal(history.Journal!.Count, read.Journal!.Count);
        Assert.Equal("OUT", read.Journal![^1].Edit.Says);
    }

    ///<summary>A session with nothing to take again writes neither key, which is what every session said before.</summary>
    [Fact]
    public void A_session_without_one_says_nothing_about_it()
    {
        string json = SavedSession.Serialize(new SavedSession { FileName = "Mosfet" });

        Assert.DoesNotContain("\"j\"", json);
        Assert.DoesNotContain("\"bs\"", json);

        var read = SavedSession.Deserialize(json)!;

        Assert.Null(read.Journal);
        Assert.Null(read.Base);
    }

    ///
    ///**Bytes stored under a name come back without it, and with it said.** Bytes stored before there were names
    ///are base64 and nothing else, and come back as they are, under none.
    ///
    [Fact]
    public void Bytes_are_stored_under_the_name_of_their_writing()
    {
        string bytes = Convert.ToBase64String(Placed().Serialize());
        var session = new SavedSession { Base = "a1b2c3", Journal = new List<SavedStep> { new SavedStep() } };

        session.BytesFrom(SavedSession.Stored("a1b2c3", bytes));

        Assert.Equal(bytes, session.FileBytes);
        Assert.Equal("a1b2c3", session.BytesBase);
        Assert.True(session.JournalFits);

        session.BytesFrom(bytes);

        Assert.Equal(bytes, session.FileBytes);
        Assert.Equal("", session.BytesBase);
        Assert.False(session.JournalFits);
    }

    ///<summary>Steps saved against one writing are not taken again on another.</summary>
    [Fact]
    public void A_journal_fits_only_the_writing_it_names()
    {
        var session = new SavedSession { Base = "first" };

        session.BytesFrom(SavedSession.Stored("second", "AAEC"));

        Assert.False(session.JournalFits);

        session.Base = "";
        session.BytesFrom(SavedSession.Stored("", "AAEC"));

        Assert.False(session.JournalFits);
    }

    #endregion **********************************************************************
}
