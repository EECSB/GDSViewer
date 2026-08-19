using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Editing the hierarchy itself: making a cell, placing one, and asking what places what.
///
///**A cell is the one thing an edit adds to the library rather than to a structure in it.** Everything else
///here changes an element inside a cell; this changes what cells there are. The round trip is the test for
///the same reason it is everywhere else - written out and read back, because a structure assembled wrongly
///looks fine in the model that made it and is a file nobody else will take.
///</summary>
public class HierarchyTests
{
    #region A library to work in ****************************************************

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

    ///<summary>The records of one boundary, to put inside a new cell.</summary>
    private static List<GDS.Record> ASquare(int size)
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



    #region Making a cell ***********************************************************

    [Fact]
    public void A_new_cell_is_in_the_library_and_in_the_file()
    {
        var gds = Placed();

        new AddStructure(gds, "MADE", ASquare(400)).Apply();

        Assert.Contains("MADE", Hierarchy.Names(gds));

        //And it survives being written out and read back, which is what says it is well formed.
        var reread = new GDS(gds.Serialize());

        Assert.Contains("MADE", Hierarchy.Names(reread));
        Assert.Single(Named(reread, "MADE").Elements);
    }

    ///<summary>A cell nothing places draws on its own, which is how the flattener treats any unreferenced one.</summary>
    [Fact]
    public void A_new_cell_with_a_shape_in_it_is_drawn()
    {
        var gds = Placed();

        new AddStructure(gds, "MADE", ASquare(400)).Apply();

        Assert.Contains(GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements,
            element => element.Source!.Structure == "MADE");
    }

    ///<summary>
    ///On a layer the file has never used, the shape inside it still draws - the flattener skips an element
    ///whose layer it cannot look up, so a whole cell of them would be a cell that is there and invisible.
    ///</summary>
    [Fact]
    public void A_new_cell_introduces_the_layers_inside_it()
    {
        var gds = Placed();

        Assert.False(gds.AdditionalInformation.Layers.ContainsKey(new LayerKey(70, 0)));

        new AddStructure(gds, "MADE", ASquare(400)).Apply();

        Assert.True(gds.AdditionalInformation.Layers.ContainsKey(new LayerKey(70, 0)));
        Assert.Contains(GdsFlattener.Flatten(gds).Elements, element => element.Layer.Key.Number == 70);
    }

    [Fact]
    public void Undoing_a_new_cell_puts_the_file_back_exactly()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddStructure(gds, "MADE", ASquare(400)));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
        Assert.DoesNotContain("MADE", Hierarchy.Names(gds));
    }

    [Fact]
    public void Redoing_puts_the_cell_back()
    {
        var gds = Placed();

        var history = new EditHistory();

        history.Do(new AddStructure(gds, "MADE", ASquare(400)));

        byte[] made = gds.Serialize();

        history.Undo();
        history.Redo();

        Assert.Equal(made, gds.Serialize());
    }

    ///<summary>A cell has to be somewhere in the file, and after everything else is the only place that works.</summary>
    [Fact]
    public void A_new_cell_goes_in_before_the_end_of_the_library()
    {
        var gds = Placed();

        new AddStructure(gds, "MADE", ASquare(400)).Apply();

        var types = gds.Records.Select(record => record.Type).ToList();

        Assert.Equal(RecordType.ENDLIB, types[^1]);
        Assert.Equal(RecordType.ENDSTR, types[^2]);
    }

    #endregion **********************************************************************



    #region Renaming one ************************************************************

    [Fact]
    public void Renaming_changes_what_the_cell_is_called()
    {
        var gds = Placed();

        new RenameStructure(gds, "LEAF", "PIN").Apply();

        var reread = new GDS(gds.Serialize());

        Assert.Contains("PIN", Hierarchy.Names(reread));
        Assert.DoesNotContain("LEAF", Hierarchy.Names(reread));
    }

    ///
    ///**Every placement of it is renamed too.**
    ///
    ///A library refers to a cell by writing its name into an SNAME on each reference, so changing the
    ///STRNAME alone leaves three instances pointing at a cell that no longer exists - a file that parses,
    ///opens, and draws nothing where they were.
    ///
    [Fact]
    public void Renaming_takes_every_placement_with_it()
    {
        var gds = Placed();

        Assert.Equal(3, Hierarchy.PlacementsOf(gds, "LEAF"));

        new RenameStructure(gds, "LEAF", "PIN").Apply();

        var reread = new GDS(gds.Serialize());

        Assert.Equal(3, Hierarchy.PlacementsOf(reread, "PIN"));
        Assert.Equal(0, Hierarchy.PlacementsOf(reread, "LEAF"));

        //And the three are still drawn, which is what says nothing was left dangling.
        Assert.Equal(3, GdsFlattener.Flatten(reread).Elements.Count(element => element.Source!.Structure == "PIN"));
    }

    [Fact]
    public void Undoing_a_rename_puts_the_file_back_exactly()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new RenameStructure(gds, "LEAF", "PIN"));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void Redoing_a_rename_puts_the_new_name_back()
    {
        var gds = Placed();

        var history = new EditHistory();

        history.Do(new RenameStructure(gds, "LEAF", "PIN"));

        byte[] renamed = gds.Serialize();

        history.Undo();
        history.Redo();

        Assert.Equal(renamed, gds.Serialize());
    }

    ///<summary>A name a longer one starts with is not the same name, so nothing else moves with it.</summary>
    [Fact]
    public void Renaming_leaves_a_cell_whose_name_merely_starts_the_same()
    {
        var gds = Placed();

        new AddStructure(gds, "LEAFY", ASquare(100)).Apply();

        new RenameStructure(gds, "LEAF", "PIN").Apply();

        Assert.Contains("LEAFY", Hierarchy.Names(gds));
        Assert.Contains("PIN", Hierarchy.Names(gds));
    }

    [Fact]
    public void Renaming_a_cell_that_is_not_there_does_nothing()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        new RenameStructure(gds, "NOWHERE", "PIN").Apply();

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void A_rename_survives_a_reload()
    {
        var gds = Placed();

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        history.Do(new RenameStructure(gds, "LEAF", "PIN"));

        var reopened = new GDS(gds.Serialize());
        var restored = new EditHistory();

        restored.Restore(reopened, history.Describe());

        Assert.Equal("Rename cell", restored.NextUndo);

        restored.Undo();

        Assert.Equal(original, reopened.Serialize());

        restored.Redo();

        Assert.Contains("PIN", Hierarchy.Names(reopened));
    }

    #endregion **********************************************************************



    #region Taking one away *********************************************************

    [Fact]
    public void Removing_a_cell_takes_it_out_of_the_library_and_the_file()
    {
        var gds = Placed();

        new RemoveStructure(gds, "LEAF").Apply();

        Assert.DoesNotContain("LEAF", Hierarchy.Names(gds));
        Assert.DoesNotContain("LEAF", Hierarchy.Names(new GDS(gds.Serialize())));
    }

    [Fact]
    public void Undoing_a_removed_cell_puts_the_file_back_exactly()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new RemoveStructure(gds, "LEAF"));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    ///
    ///**Back where it was, not on the end.**
    ///
    ///LEAF is the first of two cells in the fixture, so putting it back after TOP would be a file with the
    ///same cells in it and different bytes - which is a history that only mostly works.
    ///
    [Fact]
    public void A_removed_cell_goes_back_where_it_was()
    {
        var gds = Placed();

        var order = Hierarchy.Names(gds);

        var history = new EditHistory();

        history.Do(new RemoveStructure(gds, order[0]));
        history.Undo();

        Assert.Equal(order, Hierarchy.Names(gds));
    }

    ///<summary>The cell that a flatten takes away is one this app would otherwise draw twice - see the view.</summary>
    [Fact]
    public void Removing_a_cell_stops_it_being_drawn()
    {
        var gds = Placed();

        //Nothing places TOP, so it draws on its own.
        Assert.Contains(GdsFlattener.Flatten(gds).Elements, element => element.Source!.Structure == "TOP");

        new RemoveStructure(gds, "TOP").Apply();

        Assert.DoesNotContain(GdsFlattener.Flatten(gds).Elements, element => element.Source!.Structure == "TOP");
    }

    ///<summary>A cell that is not there is not an error: something else in the same gesture may have taken it.</summary>
    [Fact]
    public void Removing_a_cell_that_is_not_there_does_nothing()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        new RemoveStructure(gds, "NOWHERE").Apply();

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void A_removed_cell_survives_a_reload()
    {
        var gds = Placed();

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        history.Do(new RemoveStructure(gds, "LEAF"));

        var reopened = new GDS(gds.Serialize());
        var restored = new EditHistory();

        restored.Restore(reopened, history.Describe());

        Assert.Equal("Remove cell", restored.NextUndo);

        restored.Undo();

        //The whole cell came back, out of the stack rather than out of the file - the saved file had none.
        Assert.Contains("LEAF", Hierarchy.Names(reopened));
        Assert.Equal(original, reopened.Serialize());
    }

    #endregion **********************************************************************



    #region Placing one *************************************************************

    ///
    ///Placing a cell does not add a copy of it to the picture: it changes how the one that was there is
    ///reached. A cell nothing places is drawn as a top of its own, and stops being one the moment something
    ///names it - so the count stays at one and what moves is the chain it comes through.
    ///
    [Fact]
    public void A_placement_draws_the_cell_it_names()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        new AddStructure(gds, "MADE", ASquare(400)).Apply();

        //Drawn on its own, because nothing places it yet.
        var standalone = GdsFlattener.Flatten(gds).Elements
            .Single(element => element.Source!.Structure == "MADE");

        Assert.Equal(0, standalone.Source!.Depth);

        new AddElement(gds, top, Hierarchy.PlacementRecords("MADE", new Element.Point(2000, 2000), false, 0), "Place")
            .Apply();

        var through = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements
            .Single(element => element.Source!.Structure == "MADE");

        Assert.Equal("TOP", through.Source!.Path[0]);
        Assert.True(through.Source!.Depth > 0);
    }

    ///<summary>A plain instance carries no STRANS: the format's default is no transform, and saying so twice
    ///is a record every reader has to read to learn nothing.</summary>
    [Fact]
    public void A_plain_placement_has_no_transform_record()
    {
        var records = Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0);

        Assert.DoesNotContain(RecordType.STRANS, records.Select(record => record.Type));
        Assert.DoesNotContain(RecordType.ANGLE, records.Select(record => record.Type));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 90)]
    [InlineData(true, 270)]
    public void A_turned_or_mirrored_placement_says_so(bool mirrored, double angle)
    {
        var records = Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), mirrored, angle);

        Assert.Contains(RecordType.STRANS, records.Select(record => record.Type));

        Assert.Equal(angle != 0, records.Any(record => record.Type == RecordType.ANGLE));
    }

    ///
    ///**A placement that is turned draws its cell turned.**
    ///
    ///Read off the flattener rather than off the records, because writing a STRANS whose reflection bit is
    ///in the wrong place produces a file that parses, draws something, and draws it the wrong way up.
    ///
    [Fact]
    public void A_placement_at_a_quarter_turn_draws_the_cell_turned()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        new AddStructure(gds, "MADE", ASquare(400)).Apply();

        new AddElement(gds, top, Hierarchy.PlacementRecords("MADE", new Element.Point(0, 0), false, 90), "Place")
            .Apply();

        var through = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements
            .Single(element => element.Source!.Structure == "MADE" && element.Source!.Depth > 0);

        //The square runs 0..400 both ways in the cell. A quarter turn the way the angles run sends (x, y) to
        //(-y, x), so it comes out spanning -400..0 across and 0..400 up.
        var box = Bounds.Of(through.Points);

        Assert.Equal(-400, box.Left);
        Assert.Equal(0, box.Right);
        Assert.Equal(0, box.Bottom);
        Assert.Equal(400, box.Top);
    }

    #endregion **********************************************************************



    #region Repeating one as an array ***********************************************

    ///<summary>The records of the one placement in a library that has one, for turning into an array.</summary>
    private static List<GDS.Record> ThePlacement(GDS gds, string inside)
    {
        var structure = Named(gds, inside);

        var instance = structure.Elements.First(element => Hierarchy.PlacedBy(element) is not null);

        int start = gds.Records.IndexOf(instance.Element.Opening);
        int end = gds.Records.IndexOf(instance.ENDEL);

        return gds.Records.GetRange(start, end - start + 1);
    }

    ///
    ///**One record where copying would be one element per place.**
    ///
    ///The whole reason an array reference exists: a hundred by a hundred is a single AREF rather than ten
    ///thousand elements, and it is what the format is for.
    ///
    [Fact]
    public void An_array_is_one_element_however_many_it_draws()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        var span = ThePlacement(gds, "TOP");

        var made = Hierarchy.AsArray(span, 10, 10, 1000, 0, 0, 1000);

        Assert.NotNull(made);

        int before = top.Elements.Count;

        new AddElement(gds, top, made, "Array").Apply();

        Assert.Equal(before + 1, top.Elements.Count);

        //And it draws a hundred of them.
        var reread = new GDS(gds.Serialize());

        Assert.Equal(100, GdsFlattener.Flatten(reread).Elements
            .Count(element => element.Source!.Path.Count > 1 && element.Source!.Structure == "LEAF")
            - 3);
    }

    ///
    ///**Three points, not a pitch.**
    ///
    ///The format stores where the first one sits and where the columns and rows would reach one step past
    ///the last, and a reader divides by the counts to get the step back. Writing the pitch there gives a grid
    ///a tenth of the size it should be - and it draws, which is the worst way to be wrong.
    ///
    [Fact]
    public void An_array_is_spaced_by_the_step_it_was_given()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        //Take the three existing placements out, so what is left is only the array.
        foreach (var element in top.Elements.ToList())
        {
            if (Hierarchy.PlacedBy(element) is not null)
                new DeleteElement(gds, top, element).Apply();
        }

        var span = new List<GDS.Record>(Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0));

        var made = Hierarchy.AsArray(span, 3, 1, 5000, 0, 0, 5000);

        Assert.NotNull(made);

        new AddElement(gds, top, made, "Array").Apply();

        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements
            .Where(element => element.Source!.Structure == "LEAF")
            .Select(element => Bounds.Of(element.Points).Left)
            .OrderBy(left => left)
            .ToList();

        Assert.Equal(3, drawn.Count);

        //Five thousand apart, which is the step - not five thousand across the whole row.
        Assert.Equal(5000, drawn[1] - drawn[0]);
        Assert.Equal(5000, drawn[2] - drawn[1]);
    }

    ///<summary>Whatever the placement was written with comes across: this one is turned and mirrored.</summary>
    [Fact]
    public void An_array_keeps_what_the_placement_was_written_with()
    {
        var span = new List<GDS.Record>(Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), true, 90));

        var made = Hierarchy.AsArray(span, 2, 2, 100, 0, 0, 100);

        Assert.NotNull(made);

        var types = made.Select(record => record.Type).ToList();

        Assert.Contains(RecordType.STRANS, types);
        Assert.Contains(RecordType.ANGLE, types);

        //An AREF rather than an SREF, with the counts before the coordinates.
        Assert.Equal(RecordType.AREF, types[0]);
        Assert.True(types.IndexOf(RecordType.COLROW) < types.IndexOf(RecordType.XY));
    }

    [Fact]
    public void An_array_names_the_same_cell()
    {
        var span = new List<GDS.Record>(Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0));

        var made = Hierarchy.AsArray(span, 2, 3, 100, 0, 0, 100)!;

        int at = 0;
        var model = new GDS.ElementModel(ref at, made);

        Assert.Equal("LEAF", Hierarchy.PlacedBy(model));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-2, 3)]
    public void An_array_of_no_columns_or_rows_is_refused(int columns, int rows)
    {
        var span = new List<GDS.Record>(Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0));

        Assert.Null(Hierarchy.AsArray(span, columns, rows, 100, 0, 0, 100));
    }

    ///<summary>Something with no coordinates in it is not a placement, and says so rather than half-building one.</summary>
    [Fact]
    public void Something_that_is_not_a_placement_is_refused()
    {
        var notOne = new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.SREF, null),
            Hierarchy.Make(RecordType.SNAME, new AsciiData("LEAF")),
            Hierarchy.Make(RecordType.ENDEL, null)
        };

        Assert.Null(Hierarchy.AsArray(notOne, 2, 2, 100, 0, 0, 100));
    }

    #endregion **********************************************************************



    #region What places what ********************************************************

    [Fact]
    public void A_cell_knows_what_it_places()
    {
        var gds = Placed();

        Assert.Equal(new[] { "LEAF", "LEAF", "LEAF" }, Hierarchy.Places(Named(gds, "TOP")));
        Assert.Empty(Hierarchy.Places(Named(gds, "LEAF")));
    }

    [Fact]
    public void The_library_knows_how_many_placements_name_a_cell()
    {
        var gds = Placed();

        Assert.Equal(3, Hierarchy.PlacementsOf(gds, "LEAF"));
        Assert.Equal(0, Hierarchy.PlacementsOf(gds, "TOP"));
    }

    ///
    ///**The question a placement has to be refused on.**
    ///
    ///Putting a cell inside something it already contains makes a hierarchy with no bottom: the format
    ///cannot say it is wrong, every writer stores it happily, and every reader gives up at a different
    ///depth. Before it is written is the only place to catch it.
    ///
    [Fact]
    public void A_cell_reaches_what_it_places_and_itself()
    {
        var gds = Placed();

        Assert.True(Hierarchy.Reaches(gds, "TOP", "LEAF"));
        Assert.True(Hierarchy.Reaches(gds, "TOP", "TOP"));
        Assert.True(Hierarchy.Reaches(gds, "LEAF", "LEAF"));

        //And LEAF places nothing, so it reaches nothing else.
        Assert.False(Hierarchy.Reaches(gds, "LEAF", "TOP"));
    }

    [Fact]
    public void Reaching_follows_a_chain_rather_than_one_step()
    {
        var gds = Placed();

        //MIDDLE places LEAF, and TOP already places LEAF - so putting MIDDLE in TOP makes TOP reach LEAF two
        //ways, and LEAF must still not reach TOP.
        new AddStructure(gds, "MIDDLE", Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0)).Apply();

        new AddElement(gds, Named(gds, "TOP"), Hierarchy.PlacementRecords("MIDDLE", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        Assert.True(Hierarchy.Reaches(gds, "TOP", "LEAF"));
        Assert.True(Hierarchy.Reaches(gds, "MIDDLE", "LEAF"));
        Assert.False(Hierarchy.Reaches(gds, "LEAF", "MIDDLE"));
    }

    ///<summary>
    ///A library handed to this app may already hold a cycle - the format cannot refuse one - so the walk
    ///answers rather than following it forever.
    ///</summary>
    [Fact]
    public void Reaching_through_a_library_that_already_loops_still_answers()
    {
        var gds = Placed();

        //LEAF placing TOP, where TOP already places LEAF.
        new AddElement(gds, Named(gds, "LEAF"), Hierarchy.PlacementRecords("TOP", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        Assert.True(Hierarchy.Reaches(gds, "TOP", "LEAF"));
        Assert.True(Hierarchy.Reaches(gds, "LEAF", "TOP"));
        Assert.False(Hierarchy.Reaches(gds, "TOP", "NOWHERE"));
    }

    [Fact]
    public void An_unused_name_avoids_the_ones_already_taken()
    {
        var gds = Placed();

        Assert.Equal("CELL", Hierarchy.UnusedName(gds, "CELL"));

        new AddStructure(gds, "CELL", ASquare(100)).Apply();

        Assert.Equal("CELL1", Hierarchy.UnusedName(gds, "CELL"));

        new AddStructure(gds, "CELL1", ASquare(100)).Apply();

        Assert.Equal("CELL2", Hierarchy.UnusedName(gds, "CELL"));
    }

    #endregion **********************************************************************



    #region Across a reload *********************************************************

    ///<summary>
    ///The whole cell travels, because a cell that has been undone is not in the saved file at all - the same
    ///reason a deleted element carries its records.
    ///</summary>
    [Fact]
    public void A_new_cell_survives_a_reload()
    {
        var gds = Placed();

        byte[] original = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddStructure(gds, "MADE", ASquare(400)));

        var reopened = new GDS(gds.Serialize());
        var restored = new EditHistory();

        restored.Restore(reopened, history.Describe());

        Assert.Equal("Make cell", restored.NextUndo);

        restored.Undo();

        Assert.Equal(original, reopened.Serialize());
        Assert.DoesNotContain("MADE", Hierarchy.Names(reopened));

        restored.Redo();

        Assert.Contains("MADE", Hierarchy.Names(reopened));
    }

    #endregion **********************************************************************



    #region Turning a placement *****************************************************

    ///<summary>The records of the first instance in a cell.</summary>
    private static List<GDS.Record> PlacementIn(GDS gds, string cell)
    {
        var instance = FirstPlacement(gds, cell);

        int start = gds.Records.IndexOf(instance.Element.Opening);
        int end = gds.Records.IndexOf(instance.ENDEL);

        return gds.Records.GetRange(start, end - start + 1);
    }

    private static GDS.ElementModel FirstPlacement(GDS gds, string cell)
    {
        return Named(gds, cell).Elements.First(element => element.Element is GDS.SrefModel);
    }

    [Fact]
    public void APlainPlacementReadsAsSquareAndUnmirrored()
    {
        var gds = Placed();

        (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(FirstPlacement(gds, "TOP"));

        Assert.False(mirrored);
        Assert.Equal(0, angle);
        Assert.Equal(1, magnification);
    }

    ///<summary>
    ///**The composition is checked against what actually draws, not against the numbers it produces.**
    ///
    ///A test that asserted `Turned(false, 90, FlipX)` is `(true, 90)` would only be repeating the
    ///implementation back at itself, and would keep passing if both were wrong together. What has to hold is
    ///that a placement turned draws its cell turned - so both routes are put through
    ///<see cref="Transform.ForPlacement"/>, the same one the flattener draws with, and compared at a point.
    ///
    ///Off-axis and asymmetric on purpose: a point on either axis, or a starting angle of zero, survives
    ///several wrong compositions that this one does not.
    ///</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 90)]
    [InlineData(false, 270)]
    [InlineData(true, 0)]
    [InlineData(true, 90)]
    [InlineData(true, 180)]
    public void TurningAPlacementTurnsWhatItDraws(bool mirrored, double angle)
    {
        const double probeX = 37;
        const double probeY = 11;

        foreach (Turn turn in Enum.GetValues<Turn>())
        {
            var placed = Transform.ForPlacement(mirrored, 1, angle, 0, 0);

            (bool turnedMirror, double turnedAngle) = Hierarchy.Turned(mirrored, angle, turn);

            var afterwards = Transform.ForPlacement(turnedMirror, 1, turnedAngle, 0, 0);

            //Drawn, then turned where it landed.
            (double drawnX, double drawnY) = placed.ApplyTo(probeX, probeY);
            (double wantedX, double wantedY) = Turning.Point(drawnX, drawnY, turn, 0, 0);

            //Against: turned in the placement, then drawn.
            (double gotX, double gotY) = afterwards.ApplyTo(probeX, probeY);

            Assert.Equal(wantedX, gotX, 9);
            Assert.Equal(wantedY, gotY, 9);
        }
    }

    ///<summary>Four quarters is where it started, whatever it started as.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 45)]
    public void FourQuartersComeBackAround(bool mirrored, double angle)
    {
        bool turning = mirrored;
        double turned = angle;

        for (int i = 0; i < 4; i++)
            (turning, turned) = Hierarchy.Turned(turning, turned, Turn.Quarter);

        Assert.Equal(mirrored, turning);
        Assert.Equal(angle, turned, 9);
    }

    ///<summary>An angle is kept in a half-open turn, so one orientation is never written two ways.</summary>
    [Fact]
    public void AnAngleIsKeptBelowAFullTurn()
    {
        Assert.Equal(0, Hierarchy.Turned(false, 270, Turn.Quarter).Angle);
        Assert.Equal(270, Hierarchy.Turned(false, 0, Turn.ThreeQuarters).Angle);
        Assert.Equal(180, Hierarchy.Turned(false, 0, Turn.FlipX).Angle);
        Assert.Equal(0, Hierarchy.Turned(false, 0, Turn.FlipY).Angle);
    }

    [Fact]
    public void ATurnedPlacementWritesItsStransAndReadsBack()
    {
        var gds = Placed();

        var rebuilt = Hierarchy.WithTransform(PlacementIn(gds, "TOP"), new Element.Point(400, 700), true, 90, 1);

        Assert.NotNull(rebuilt);

        var structure = Named(gds, "TOP");
        var edit = new AddElement(gds, structure, rebuilt!, "Turn");

        edit.Apply();

        var reopened = new GDS(gds.Serialize());

        var placement = Named(reopened, "TOP").Elements.Last(element => element.Element is GDS.SrefModel);

        (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(placement);

        Assert.True(mirrored);
        Assert.Equal(90, angle);
        Assert.Equal(1, magnification);

        var sref = (GDS.SrefModel)placement.Element;

        Assert.Equal(new int[] { 400, 700 }, ((Int4Data)sref.XY!.Data!).Values);
    }

    ///<summary>What it places is what it placed - a turn moves an instance, it does not repoint it.</summary>
    [Fact]
    public void ATurnedPlacementStillPlacesTheSameCell()
    {
        var gds = Placed();

        var was = PlacementIn(gds, "TOP");

        string named = was
            .Where(record => record.Type == RecordType.SNAME)
            .Select(record => ((AsciiData)record.Data!).Value)
            .Single();

        var rebuilt = Hierarchy.WithTransform(was, new Element.Point(0, 0), false, 180, 1);

        Assert.Equal(
            named,
            rebuilt!.Where(record => record.Type == RecordType.SNAME)
                .Select(record => ((AsciiData)record.Data!).Value)
                .Single());
    }

    ///<summary>
    ///A square placement writes no STRANS at all, which is what a plain instance looks like in every file
    ///that was not written by something that turned one.
    ///</summary>
    [Fact]
    public void ASquarePlacementWritesNoTransformRecords()
    {
        var rebuilt = Hierarchy.WithTransform(
            new List<GDS.Record>
            {
                Hierarchy.Make(RecordType.SREF, null),
                Hierarchy.Make(RecordType.SNAME, new AsciiData("LEAF")),
                Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { 0, 0 })),
                Hierarchy.Make(RecordType.ENDEL, null)
            },
            new Element.Point(5, 6),
            false,
            0,
            1);

        Assert.NotNull(rebuilt);
        Assert.DoesNotContain(rebuilt!, record => record.Type == RecordType.STRANS);
        Assert.DoesNotContain(rebuilt!, record => record.Type == RecordType.ANGLE);
        Assert.DoesNotContain(rebuilt!, record => record.Type == RecordType.MAG);
    }

    ///<summary>A scale it was placed with is not thrown away by turning it.</summary>
    [Fact]
    public void AMagnificationSurvivesBeingRewritten()
    {
        var rebuilt = Hierarchy.WithTransform(PlacementIn(Placed(), "TOP"), new Element.Point(0, 0), false, 0, 2.5);

        Assert.NotNull(rebuilt);

        var gds = Placed();
        var edit = new AddElement(gds, Named(gds, "TOP"), rebuilt!, "Scale");

        edit.Apply();

        var reopened = new GDS(gds.Serialize());

        var placement = Named(reopened, "TOP").Elements.Last(element => element.Element is GDS.SrefModel);

        Assert.Equal(2.5, Hierarchy.TransformOf(placement).Magnification, 9);
    }

    [Fact]
    public void RewritingSomethingThatPlacesNothingIsRefused()
    {
        Assert.Null(Hierarchy.WithTransform(ASquare(100), new Element.Point(0, 0), false, 90, 1));
    }

    #endregion **********************************************************************



    #region Copying a whole cell ****************************************************

    ///<summary>Every element of a cell, as the records to build a second one out of.</summary>
    private static List<GDS.Record> ContentsOf(GDS gds, string cell)
    {
        var records = new List<GDS.Record>();

        foreach (var element in Named(gds, cell).Elements)
        {
            int start = gds.Records.IndexOf(element.Element.Opening);
            int end = gds.Records.IndexOf(element.ENDEL);

            foreach (var record in gds.Records.GetRange(start, end - start + 1))
                records.Add(new GDS.Record((short)record.Type, record.Data?.Encode() ?? Array.Empty<byte>()));
        }

        return records;
    }

    [Fact]
    public void ACopiedCellHoldsTheSameShapes()
    {
        var gds = Placed();

        new AddStructure(gds, "LEAF2", ContentsOf(gds, "LEAF")).Apply();

        var reopened = new GDS(gds.Serialize());

        Assert.Equal(
            Named(reopened, "LEAF").Elements.Count,
            Named(reopened, "LEAF2").Elements.Count);

        Assert.Equal(
            ((Int4Data)Named(reopened, "LEAF").Elements[0].Element.XY!.Data!).Values,
            ((Int4Data)Named(reopened, "LEAF2").Elements[0].Element.XY!.Data!).Values);
    }

    ///
    ///**The copy is its own cell.** Changing one has to leave the other alone, which is the whole difference
    ///between copying a cell and placing a second instance of it - and would not hold if the two shared
    ///records.
    ///
    [Fact]
    public void ChangingTheCopyLeavesTheOriginalAlone()
    {
        var gds = Placed();

        new AddStructure(gds, "LEAF2", ContentsOf(gds, "LEAF")).Apply();

        var copy = Named(gds, "LEAF2");

        new MoveElement(copy, copy.Elements[0], 5000, 5000).Apply();

        var reopened = new GDS(gds.Serialize());

        Assert.NotEqual(
            ((Int4Data)Named(reopened, "LEAF").Elements[0].Element.XY!.Data!).Values,
            ((Int4Data)Named(reopened, "LEAF2").Elements[0].Element.XY!.Data!).Values);
    }

    ///<summary>And nothing places it yet, so this view draws it as a top of its own until something does.</summary>
    [Fact]
    public void ACopiedCellIsPlacedByNothing()
    {
        var gds = Placed();

        new AddStructure(gds, "LEAF2", ContentsOf(gds, "LEAF")).Apply();

        Assert.Equal(0, Hierarchy.PlacementsOf(gds, "LEAF2"));
        Assert.Equal(3, Hierarchy.PlacementsOf(gds, "LEAF"));

        Assert.Contains(Hierarchy.Summarize(new GDS(gds.Serialize())), cell => cell.Name == "LEAF2" && cell.IsTop);
    }

    ///<summary>Copying a cell that places another copies the placement, not what it places.</summary>
    [Fact]
    public void CopyingACellThatPlacesOneCopiesThePlacement()
    {
        var gds = Placed();

        new AddStructure(gds, "TOP2", ContentsOf(gds, "TOP")).Apply();

        var reopened = new GDS(gds.Serialize());

        //Six placements of LEAF now: the three TOP had, and the three TOP2 copied.
        Assert.Equal(6, Hierarchy.PlacementsOf(reopened, "LEAF"));

        //And still one LEAF.
        Assert.Equal(1, Hierarchy.Names(reopened).Count(name => name == "LEAF"));
    }

    [Fact]
    public void UndoingACopyPutsTheFileBackExactly()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddStructure(gds, "LEAF2", ContentsOf(gds, "LEAF")));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    #endregion **********************************************************************



    #region Reading a transform back into a placement *******************************

    ///
    ///**Every placement survives being made into a matrix and read back out of one.**
    ///
    ///This is the property the whole of instance editing rests on: turning an instance where it sits is done
    ///by composing matrices out to the layout and back, and what comes home has to be writable as the three
    ///things a placement is written with. If the round trip loses anything, an instance turned inside a cell
    ///that is itself placed comes out somewhere else entirely - and only in that case, which is exactly the
    ///one nobody tries by hand.
    ///
    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(false, 90, 1)]
    [InlineData(false, 270, 2.5)]
    [InlineData(true, 0, 1)]
    [InlineData(true, 45, 1)]
    [InlineData(true, 180, 0.25)]
    [InlineData(false, 37.5, 1)]
    public void APlacementSurvivesBecomingAMatrix(bool mirrored, double angle, double magnification)
    {
        var built = Transform.ForPlacement(mirrored, magnification, angle, 300, -400);

        (bool readMirror, double readAngle, double readScale, Element.Point at) = Hierarchy.Placement(built);

        Assert.Equal(mirrored, readMirror);
        Assert.Equal(angle, readAngle, 6);
        Assert.Equal(magnification, readScale, 9);
        Assert.Equal(300, at.X);
        Assert.Equal(-400, at.Y);
    }

    ///
    ///**Turning by composition agrees with turning by the angle rules, wherever both apply.**
    ///
    ///Two independent routes to the same thing: <see cref="Hierarchy.Turned"/> reasons about the angle, and
    ///composing through <see cref="Turning.About"/> reasons about nothing at all. They have to agree on a
    ///cell placed square, which is what says the composition route can be trusted in the cases where the
    ///angle rules cannot be written down.
    ///
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 90)]
    [InlineData(true, 0)]
    [InlineData(true, 270)]
    public void ComposingATurnAgreesWithTheAngleRules(bool mirrored, double angle)
    {
        foreach (Turn turn in Enum.GetValues<Turn>())
        {
            var placed = Transform.ForPlacement(mirrored, 1, angle, 0, 0);

            var composed = Hierarchy.Placement(placed.Then(Turning.About(turn, 0, 0)));

            (bool wantedMirror, double wantedAngle) = Hierarchy.Turned(mirrored, angle, turn);

            Assert.Equal(wantedMirror, composed.Mirrored);
            Assert.Equal(wantedAngle, composed.Angle, 6);
        }
    }

    ///<summary>A quarter turn about a point away from the origin moves the placement to where it lands.</summary>
    [Fact]
    public void TurningAboutAPivotMovesWhereThePlacementSits()
    {
        var placed = Transform.ForPlacement(false, 1, 0, 100, 0);

        var turned = Hierarchy.Placement(placed.Then(Turning.About(Turn.Quarter, 0, 0)));

        //(100, 0) a quarter the way the angles run is (0, 100).
        Assert.Equal(0, turned.At.X);
        Assert.Equal(100, turned.At.Y);
        Assert.Equal(90, turned.Angle, 6);
    }

    ///
    ///**Inside a cell that is itself placed mirrored, the turn is still the turn that was asked for.**
    ///
    ///The case the case-by-case version gets wrong: a mirrored frame reverses which way a quarter goes, so
    ///composing the turn where the instance is *drawn* and taking the frame back off is the only route that
    ///lands in the same place as the shapes around it. Checked at a point rather than on the numbers, because
    ///what has to be true is where it draws.
    ///
    [Fact]
    public void ATurnInsideAMirroredCellGoesTheWayItLooks()
    {
        var frame = Transform.ForPlacement(true, 1, 0, 0, 0);
        var inside = Transform.ForPlacement(false, 1, 0, 50, 20);

        var drawn = inside.Then(frame);

        var wanted = drawn.Then(Turning.About(Turn.Quarter, 0, 0));

        Assert.NotNull(frame.Inverse());

        var local = wanted.Then(frame.Inverse()!.Value);

        (bool mirrored, double angle, double magnification, Element.Point at) = Hierarchy.Placement(local);

        var rebuilt = Transform.ForPlacement(mirrored, magnification, angle, at.X, at.Y).Then(frame);

        //A probe point drawn through the rebuilt placement lands where the turn put it.
        (double wantedX, double wantedY) = wanted.ApplyTo(7, 3);
        (double gotX, double gotY) = rebuilt.ApplyTo(7, 3);

        Assert.Equal(wantedX, gotX, 6);
        Assert.Equal(wantedY, gotY, 6);
    }

    ///<summary>A right angle that has been through a cosine comes home as a right angle.</summary>
    [Fact]
    public void AComposedAngleSettlesOntoAWholeNumber()
    {
        var placed = Transform.ForPlacement(false, 1, 0, 0, 0);

        var turned = Hierarchy.Placement(placed.Then(Turning.About(Turn.Quarter, 0, 0)));

        Assert.Equal(90, turned.Angle);
        Assert.Equal(1, turned.Magnification);
    }

    [Fact]
    public void AnAngleSettlesInsideOneTurn()
    {
        Assert.Equal(10, Hierarchy.Settled(370));
        Assert.Equal(350, Hierarchy.Settled(-10));
        Assert.Equal(0, Hierarchy.Settled(360));
        Assert.Equal(0, Hierarchy.Settled(-0.0000000001));
        Assert.Equal(45.5, Hierarchy.Settled(45.5));
    }

    #endregion **********************************************************************

    #region The library as a tree ****************************************************

    ///Every row as "depth:name", which is the whole shape of a tree in one readable line.
    private static string[] Shape(GDS gds)
    {
        return Hierarchy.Tree(gds)
            .Select(row => $"{row.Depth}:{row.Cell.Name}")
            .ToArray();
    }

    ///
    ///The plain case: what nothing places is a root, and what it places sits under it.
    ///
    [Fact]
    public void A_tree_puts_what_a_cell_places_underneath_it()
    {
        Assert.Equal(new[] { "0:TOP", "1:LEAF" }, Shape(Placed()));
    }

    ///
    ///A cell placed four times by one parent is one child of it.
    ///
    ///The count of placements is already on the row; four identical lines would say it worse, and a
    ///standard cell used a thousand times would be a thousand rows under one parent.
    ///
    [Fact]
    public void A_cell_placed_many_times_by_one_parent_is_one_row()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        //LEAF is already placed three times by TOP in the fixture; one more changes nothing about the shape.
        new AddElement(gds, top, Hierarchy.PlacementRecords("LEAF", new Element.Point(9000, 9000), false, 0), "Place")
            .Apply();

        Assert.Equal(new[] { "0:TOP", "1:LEAF" }, Shape(gds));
    }

    ///Depth is the nesting, so a chain comes out as a staircase.
    [Fact]
    public void A_chain_comes_out_as_a_staircase()
    {
        var gds = Placed();

        new AddStructure(gds, "MIDDLE", Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0)).Apply();

        new AddElement(gds, Named(gds, "TOP"), Hierarchy.PlacementRecords("MIDDLE", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        //
        //TOP places LEAF *and* MIDDLE, so those two are siblings rather than a chain - and MIDDLE places
        //LEAF, which puts a second LEAF one deeper. Written out in the order the file is walked.
        //
        Assert.Equal(new[] { "0:TOP", "1:LEAF", "1:MIDDLE", "2:LEAF" }, Shape(gds));
    }

    ///
    ///**A cell placed in two parents appears under both**, which is where this parts company with a folder
    ///tree - a directory is in one place, and a GDS cell is genuinely shared.
    ///
    ///The second time is marked rather than hidden, so a reader can tell "the same cell again" from "two of
    ///these", and its children are left out because the shape below is identical to the first.
    ///
    [Fact]
    public void A_shared_cell_appears_under_each_parent_and_says_so()
    {
        var gds = Placed();

        new AddStructure(gds, "MIDDLE", Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0)).Apply();

        new AddElement(gds, Named(gds, "TOP"), Hierarchy.PlacementRecords("MIDDLE", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        var rows = Hierarchy.Tree(gds);
        var leaves = rows.Where(row => row.Cell.Name == "LEAF").ToList();

        Assert.Equal(2, leaves.Count);

        //The first is the one worth walking; the second says it has been seen.
        Assert.False(leaves[0].Repeats);
        Assert.True(leaves[1].Repeats);
    }

    ///
    ///A file that says A places B and B places A is illegal and exists anyway.
    ///
    ///The flattener already guards against it; a list that hung on one would be the same bug somewhere else.
    ///The looping name is drawn once so it can be seen, and never descended into.
    ///
    [Fact]
    public void A_loop_is_drawn_once_rather_than_forever()
    {
        var gds = Placed();

        //LEAF places TOP, which places LEAF.
        new AddElement(gds, Named(gds, "LEAF"), Hierarchy.PlacementRecords("TOP", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        var rows = Hierarchy.Tree(gds);

        //It came back at all, and it is small.
        Assert.NotEmpty(rows);
        Assert.True(rows.Count < 10);

        //Every cell in the file is on it somewhere, which is the point of a list.
        Assert.Contains(rows, row => row.Cell.Name == "TOP");
        Assert.Contains(rows, row => row.Cell.Name == "LEAF");
    }

    ///
    ///A library that is all loop has no top at all, and a tree built only from tops would be empty.
    ///
    ///Better a flat list than no list: anything a walk from the tops could not reach is added at the root.
    ///
    [Fact]
    public void A_library_with_no_top_still_lists_its_cells()
    {
        var gds = Placed();

        new AddElement(gds, Named(gds, "LEAF"), Hierarchy.PlacementRecords("TOP", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        var names = Hierarchy.Tree(gds).Select(row => row.Cell.Name).Distinct().ToList();

        Assert.Contains("TOP", names);
        Assert.Contains("LEAF", names);
    }

    ///A cell with nothing inside it has no twisty, which is what the list draws that from.
    [Fact]
    public void A_row_says_whether_anything_is_under_it()
    {
        var rows = Hierarchy.Tree(Placed());

        Assert.True(rows.Single(row => row.Cell.Name == "TOP").HasChildren);
        Assert.False(rows.Single(row => row.Cell.Name == "LEAF").HasChildren);
    }

    ///A name that points at nothing is a broken reference, and there is no row to draw for it.
    [Fact]
    public void A_placement_of_a_cell_that_is_not_there_is_skipped()
    {
        var gds = Placed();

        new AddElement(gds, Named(gds, "TOP"), Hierarchy.PlacementRecords("MISSING", new Element.Point(0, 0), false, 0), "Place")
            .Apply();

        Assert.DoesNotContain(Hierarchy.Tree(gds), row => row.Cell.Name == "MISSING");

        //And the rest of the tree is unharmed.
        Assert.Equal(new[] { "0:TOP", "1:LEAF" }, Shape(gds));
    }

    #endregion **********************************************************************

    #region The layers and the shapes on them ****************************************

    ///Every row as "depth:what", which is the whole shape of a three-level tree in readable lines.
    private static string[] FullShape(GDS gds, string[]? folded = null, string[]? opened = null, int mostShapes = 200)
    {
        var rows = Hierarchy.Tree(
            gds,
            new HashSet<string>(folded ?? Array.Empty<string>()),
            new HashSet<string>(opened ?? Array.Empty<string>()),
            mostShapes);

        return rows.Select(row =>
        {
            if (row.Kind == Hierarchy.TreeRowKind.Cell)
                return $"{row.Depth}:cell {row.Cell!.Name}";

            if (row.Kind == Hierarchy.TreeRowKind.Layer)
                return $"{row.Depth}:layer {row.Layer} x{row.Count}";

            if (row.Kind == Hierarchy.TreeRowKind.Shape)
                return $"{row.Depth}:shape";

            return $"{row.Depth}:rest {row.Count}";
        }).ToArray();
    }

    ///
    ///The cells are still the cells, and the layers appear under them.
    ///
    ///The two-level tree's own answers have to survive: TOP first, and LEAF a level in under it, with
    ///whatever each draws on listed in between. See A_tree_puts_what_a_cell_places_underneath_it.
    ///
    [Fact]
    public void The_three_level_tree_keeps_the_cells_where_the_two_level_one_put_them()
    {
        var rows = Hierarchy.Tree(Placed(), new HashSet<string>(), new HashSet<string>());

        var cells = rows
            .Where(row => row.Kind == Hierarchy.TreeRowKind.Cell)
            .Select(row => $"{row.Depth}:{row.Cell!.Name}")
            .ToArray();

        Assert.Equal(new[] { "0:TOP", "1:LEAF" }, cells);
    }

    ///
    ///Opening a cell puts the layers it draws on underneath it.
    ///
    [Fact]
    public void Opening_a_cell_shows_the_layers_it_draws_on()
    {
        var gds = Placed();

        //Cells are open by default, so the layers are simply there.
        var rows = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());

        Assert.Contains(rows, row => row.Kind == Hierarchy.TreeRowKind.Layer);

        //Under the cell whose shapes they are, one level in.
        var layer = rows.First(row => row.Kind == Hierarchy.TreeRowKind.Layer);
        var cell = rows.First(row => row.Kind == Hierarchy.TreeRowKind.Cell);

        Assert.Equal(cell.Depth + 1, layer.Depth);
        Assert.Equal(cell.Cell!.Name, layer.Structure);
    }

    ///
    ///A cell's own layers, not the ones the cells it places draw on.
    ///
    ///A placement's layers belong to the cell it places, which has a row of its own further down. Counting
    ///them here would say a cell draws on a layer it never touches, once per level of nesting.
    ///
    [Fact]
    public void A_cell_lists_the_layers_of_its_own_shapes_only()
    {
        var gds = Placed();

        var rows = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());

        var underTop = rows.Count(row => row.Kind == Hierarchy.TreeRowKind.Layer && row.Structure == "TOP");
        var underLeaf = rows.Count(row => row.Kind == Hierarchy.TreeRowKind.Layer && row.Structure == "LEAF");

        Assert.Equal(Hierarchy.LayersIn(Named(gds, "TOP")).Count, underTop);
        Assert.Equal(Hierarchy.LayersIn(Named(gds, "LEAF")).Count, underLeaf);
    }

    ///
    ///Opening a layer puts its shapes underneath it, one row each.
    ///
    [Fact]
    public void Opening_a_layer_shows_the_shapes_on_it()
    {
        var gds = Placed();

        var shut = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());
        var layer = shut.First(row => row.Kind == Hierarchy.TreeRowKind.Layer);

        Assert.DoesNotContain(shut, row => row.Kind == Hierarchy.TreeRowKind.Shape);

        var open = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string> { layer.Key });

        var shapes = open.Where(row => row.Kind == Hierarchy.TreeRowKind.Shape).ToList();

        Assert.Equal(layer.Count, shapes.Count);

        //A level in from the layer, and each carrying the file's own element - which is what selecting one
        //has to find.
        Assert.All(shapes, shape => Assert.Equal(layer.Depth + 1, shape.Depth));
        Assert.All(shapes, shape => Assert.NotNull(shape.Shape));
    }

    ///
    ///Folding a cell takes away everything under it - its layers and what it places alike.
    ///
    [Fact]
    public void Folding_a_cell_takes_away_what_is_under_it()
    {
        var gds = Placed();

        var open = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());
        var top = open.First(row => row.Kind == Hierarchy.TreeRowKind.Cell);

        Assert.True(open.Count > 1);

        var folded = Hierarchy.Tree(gds, new HashSet<string> { top.Key }, new HashSet<string>());

        Assert.Single(folded);
        Assert.Equal("TOP", folded[0].Cell!.Name);
        Assert.False(folded[0].Open);
        Assert.True(folded[0].Folds);
    }

    ///
    ///**A layer lists so many and then says how many are left.**
    ///
    ///A cell of forty thousand boundaries is a real file. A tree that tried to draw a row for each would
    ///take the page down, so the cap is what makes this level safe to open at all - and the row that says
    ///what was left out is there because a list that silently stopped would be a lie about the file.
    ///
    [Fact]
    public void A_layer_lists_only_so_many_shapes_and_says_how_many_are_left()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        //Enough of them that a cap of two leaves a remainder to report.
        for (int more = 0; more < 4; more++)
            new AddElement(gds, leaf, ASquare(100 + more), "Add").Apply();

        var shut = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());
        var layer = shut.First(row => row.Kind == Hierarchy.TreeRowKind.Layer && row.Structure == "LEAF" && row.Count > 2);

        var rows = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string> { layer.Key }, mostShapes: 2);

        Assert.Equal(2, rows.Count(row => row.Kind == Hierarchy.TreeRowKind.Shape));

        var rest = rows.Single(row => row.Kind == Hierarchy.TreeRowKind.Rest);

        Assert.Equal(layer.Count - 2, rest.Count);
    }

    ///Under the cap there is nothing left over, so nothing says there is.
    [Fact]
    public void A_layer_that_fits_says_nothing_about_a_remainder()
    {
        var gds = Placed();

        var shut = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());
        var layer = shut.First(row => row.Kind == Hierarchy.TreeRowKind.Layer);

        var rows = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string> { layer.Key }, mostShapes: 1000);

        Assert.DoesNotContain(rows, row => row.Kind == Hierarchy.TreeRowKind.Rest);
    }

    ///
    ///The same cell under two parents folds separately.
    ///
    ///Which is why a row is keyed by its path rather than by its name: the tree draws a shared cell once per
    ///parent on purpose, and folding one copy away should not fold the other.
    ///
    [Fact]
    public void A_shared_cell_folds_in_one_place_at_a_time()
    {
        var gds = Placed();

        //A second parent for LEAF, so it appears twice.
        new AddStructure(gds, "OTHER", Hierarchy.PlacementRecords("LEAF", new Element.Point(0, 0), false, 0)).Apply();

        var rows = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());
        var leaves = rows.Where(row => row.Kind == Hierarchy.TreeRowKind.Cell && row.Cell!.Name == "LEAF").ToList();

        Assert.Equal(2, leaves.Count);
        Assert.NotEqual(leaves[0].Key, leaves[1].Key);
    }

    ///A cell with nothing in it has nothing to fold, and says so rather than offering an empty twisty.
    [Fact]
    public void A_cell_with_nothing_in_it_does_not_fold()
    {
        var gds = Placed();

        new AddStructure(gds, "EMPTY", new List<GDS.Record>()).Apply();

        var rows = Hierarchy.Tree(gds, new HashSet<string>(), new HashSet<string>());
        var empty = rows.Single(row => row.Kind == Hierarchy.TreeRowKind.Cell && row.Cell!.Name == "EMPTY");

        Assert.False(empty.Folds);
        Assert.DoesNotContain(rows, row => row.Structure == "EMPTY" && row.Kind != Hierarchy.TreeRowKind.Cell);
    }

    ///
    ///Layers come out sorted, unlike the cells above them.
    ///
    ///A layer is a number, numbers have an order everybody already knows, and a file's own order for them is
    ///whichever shape happened to be written first.
    ///
    [Fact]
    public void The_layers_of_a_cell_come_out_in_order()
    {
        var gds = Placed();
        var leaf = Named(gds, "LEAF");

        //Written high then low, so file order and sorted order differ.
        new AddElement(gds, leaf, OnLayer(99, 5), "Add").Apply();
        new AddElement(gds, leaf, OnLayer(3, 1), "Add").Apply();

        var layers = Hierarchy.LayersIn(Named(gds, "LEAF")).Select(found => found.Layer).ToList();
        var sorted = layers.OrderBy(key => key).ToList();

        Assert.Equal(sorted, layers);
    }

    ///<summary>One boundary on a chosen layer and datatype.</summary>
    private static List<GDS.Record> OnLayer(short layer, short dataType)
    {
        return new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.BOUNDARY, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(layer)),
            Hierarchy.Make(RecordType.DATATYPE, new Int2Data(dataType)),
            Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { 0, 0, 50, 0, 50, 50, 0, 50, 0, 0 })),
            Hierarchy.Make(RecordType.ENDEL, null)
        };
    }

    ///A placement carries no layer, so it is not one of the cell's own - the tree has a row for it already.
    [Fact]
    public void A_placement_is_not_counted_as_a_layer()
    {
        var gds = Placed();

        int listed = Hierarchy.LayersIn(Named(gds, "TOP")).Sum(found => found.Shapes.Count);
        int onALayer = Named(gds, "TOP").Elements.Count(element => element.Element is GDS.IHasLayer);

        Assert.Equal(onALayer, listed);
    }

    #endregion **********************************************************************


    #region Deleting several placements at once *************************************

    ///
    ///Several placements taken out in one step, all of them.
    ///
    ///The panel offers this on a band dragged across a layout, so the edit behind it has to hold more than
    ///one - and each DeleteElement finds its own element by reference at the moment it runs, which is what
    ///makes a list of them safe when every removal shifts the ones after it.
    ///
    [Fact]
    public void Deleting_every_placement_at_once_removes_all_of_them()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        var placements = top.Elements.Where(one => one.Element is GDS.SrefModel).ToList();

        Assert.Equal(3, placements.Count);

        new CompoundEdit("Delete", placements.Select(one => (LayoutEdit)new DeleteElement(gds, top, one))).Apply();

        Assert.DoesNotContain(top.Elements, one => one.Element is GDS.SrefModel);

        //The top's own square is untouched, which is what says this took out placements and not elements.
        Assert.Single(top.Elements);
    }

    ///
    ///**And the cell they placed is then drawn on its own**, which reads as one of them surviving.
    ///
    ///It is not. Nothing references LEAF once its placements are gone, and the flattener walks every
    ///structure nothing references - so it is a top-level cell now and is drawn at its own coordinates,
    ///at depth 0 rather than through a placement.
    ///
    ///Worth a test rather than a note, because it is exactly the observation that sends somebody looking
    ///for a bug in the delete: four shapes drawn, three placements taken out, two shapes left. It caught
    ///me while checking the button that does it.
    ///
    [Fact]
    public void The_cell_that_was_placed_is_drawn_on_its_own_afterwards()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        var placements = top.Elements.Where(one => one.Element is GDS.SrefModel).ToList();

        new CompoundEdit("Delete", placements.Select(one => (LayoutEdit)new DeleteElement(gds, top, one))).Apply();

        var drawn = GdsFlattener.Flatten(gds).Elements;

        //One from each cell, and neither of them reached through anything.
        Assert.Equal(2, drawn.Count);
        Assert.Contains(drawn, one => one.Source!.Structure == "LEAF" && one.Source!.Depth == 0);
        Assert.Contains(drawn, one => one.Source!.Structure == "TOP" && one.Source!.Depth == 0);
    }

    ///<summary>And undoing puts every one of them back, since the step is one step.</summary>
    [Fact]
    public void Undoing_puts_every_placement_back()
    {
        var gds = Placed();
        var top = Named(gds, "TOP");

        int before = GdsFlattener.Flatten(gds).Elements.Count;

        var placements = top.Elements.Where(one => one.Element is GDS.SrefModel).ToList();
        var edit = new CompoundEdit("Delete", placements.Select(one => (LayoutEdit)new DeleteElement(gds, top, one)));

        edit.Apply();
        edit.Revert();

        Assert.Equal(3, top.Elements.Count(one => one.Element is GDS.SrefModel));
        Assert.Equal(before, GdsFlattener.Flatten(gds).Elements.Count);
    }

    #endregion **********************************************************************
}
