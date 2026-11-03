using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Taking a placement apart: what it draws, written into the cell that holds it.
///
///**The picture is the test.** Every case here flattens something and then asks the flattener what the library
///draws, before and after, as a set of outlines - because a flatten that writes plausible records and draws
///something else is exactly the failure this replaced: a placement inside a turned instance came out unturned,
///and a path inside a magnified one came out at its old width, and both looked like perfectly good files.
///
///The records are checked as well where they are the point - a turned placement is a turned placement, not
///forty corners that happen to land in the right places.
///</summary>
public class FlatteningTests
{
    #region Libraries to flatten ****************************************************

    ///
    ///TOP holds a square of its own, MID placed mirrored, magnified twice and turned a quarter, and LEAF as an
    ///array of three columns by two rows. MID holds a square, a placement of LEAF, a path and a label - so
    ///flattening it has a placement to turn, a width to magnify, a label to turn and a shape to do none of
    ///that to. The same library as the browser spec's nested.gds, with the label added.
    ///
    private const string Nested = """
        HEADER: 600
        BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21
        LIBNAME: NESTED
        UNITS: 0.001 1E-09
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: LEAF
        BOUNDARY:
        LAYER: 65
        DATATYPE: 20
        XY: 0 0 800 0 800 200 0 200 0 0
        ENDEL:
        ENDSTR:
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: MID
        BOUNDARY:
        LAYER: 67
        DATATYPE: 20
        XY: 0 0 1000 0 1000 600 0 600 0 0
        ENDEL:
        SREF:
        SNAME: LEAF
        XY: 100 800
        ENDEL:
        PATH:
        LAYER: 68
        DATATYPE: 20
        WIDTH: 100
        XY: 0 1300 1000 1300
        ENDEL:
        TEXT:
        LAYER: 66
        TEXTTYPE: 5
        XY: 500 300
        STRING: M
        ENDEL:
        ENDSTR:
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: TOP
        BOUNDARY:
        LAYER: 67
        DATATYPE: 20
        XY: -3000 0 -2400 0 -2400 600 -3000 600 -3000 0
        ENDEL:
        SREF:
        SNAME: MID
        STRANS: 0x8000
        MAG: 2
        ANGLE: 90
        XY: 2000 0
        ENDEL:
        AREF:
        SNAME: LEAF
        COLROW: 3 2
        XY: 0 -3000 3000 -3000 0 -2000
        ENDEL:
        ENDSTR:
        ENDLIB:
        """;

    ///<summary>The start of a library: a header, and LEAF, a bar wider than it is tall so that a turn shows.</summary>
    private const string Header = """
        HEADER: 600
        BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21
        LIBNAME: MADE
        UNITS: 0.001 1E-09
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: LEAF
        BOUNDARY:
        LAYER: 65
        DATATYPE: 20
        XY: 0 0 800 0 800 200 0 200 0 0
        ENDEL:
        ENDSTR:

        """;

    ///<summary>LEAF and a TOP holding whatever is given, written as records.</summary>
    private static GDS Top(string holds)
    {
        return GDS.FromText(Header + "BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21\nSTRNAME: TOP\n" + holds + "\nENDSTR:\nENDLIB:\n");
    }

    private static GDS.StructureModel Cell(GDS gds, string name)
    {
        return Hierarchy.Named(gds, name)!;
    }

    ///<summary>The first element of a cell that places another, of the kind asked for.</summary>
    private static GDS.ElementModel Placing<T>(GDS gds, string inside, string placed)
    {
        return Cell(gds, inside).Elements.First(element => element.Element is T && Hierarchy.PlacedBy(element) == placed);
    }

    ///<summary>Flattens placements as one step, the way the app and the assistant both do, and gives the step back.</summary>
    private static EditHistory Flatten(GDS gds, string inside, params GDS.ElementModel[] placements)
    {
        var edits = Flattening.Edits(gds, Cell(gds, inside), placements);

        Assert.NotNull(edits);

        var history = new EditHistory();

        history.Do(new CompoundEdit("Flatten", edits!));

        return history;
    }

    ///
    ///Everything a library draws: each shape's layer, its words if it is a label, and its corners sorted -
    ///all sorted.
    ///
    ///The corners are sorted because a path is outlined from its centerline wherever it is drawn, so the same
    ///rectangle starts at another corner once the path has been through a mirror. That is the same picture.
    ///
    private static List<string> Picture(GDS gds)
    {
        return GdsFlattener.Flatten(gds).Elements
            .Select(element => $"{element.Layer.Key} {element.Text} " + string.Join(" ", element.Points
                .Select(point => $"{point.X},{point.Y}")
                .OrderBy(text => text, StringComparer.Ordinal)))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();
    }

    ///<summary>A copy of an element's records out of the library's flat list, which is how the app takes them.</summary>
    private static List<GDS.Record> RecordsOf(GDS gds, GDS.ElementModel model)
    {
        int start = gds.Records.IndexOf(model.Element.Opening);
        int end = gds.Records.IndexOf(model.ENDEL);

        return gds.Records.GetRange(start, end - start + 1)
            .Select(record => Hierarchy.Make(record.Type, record.Data))
            .ToList();
    }

    private static int[] Xy(GDS.ElementModel model)
    {
        return ((Int4Data)model.Element.XY!.Data!).Values;
    }

    #endregion **********************************************************************



    #region What comes out draws what went in ***************************************

    ///<summary>The case that was wrong: a turned, mirrored and magnified instance with a placement inside it.</summary>
    [Fact]
    public void A_turned_mirrored_and_magnified_instance_draws_the_same_flattened()
    {
        var gds = GDS.FromText(Nested);
        var before = Picture(gds);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));

        Assert.Equal(before, Picture(gds));
    }

    ///
    ///**The placement inside turns with it.** Its point is where the instance put it, and it is written turned,
    ///mirrored and magnified the way the instance drew it - not left facing the way it faced inside MID.
    ///
    [Fact]
    public void The_placement_inside_comes_out_turned_with_the_instance()
    {
        var gds = GDS.FromText(Nested);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));

        var leaf = Placing<GDS.SrefModel>(gds, "TOP", "LEAF");

        Assert.Equal((true, 90.0, 2.0), Hierarchy.TransformOf(leaf));

        //(100, 800) through mirrored, twice, a quarter turn and on to (2000, 0).
        Assert.Equal(new[] { 3600, 200 }, Xy(leaf));
    }

    [Fact]
    public void A_path_inside_a_magnified_instance_comes_out_twice_as_wide()
    {
        var gds = GDS.FromText(Nested);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));

        var path = Cell(gds, "TOP").Elements.Single(element => element.Element is GDS.PathModel);

        Assert.Equal(200, ((Int4Data)((GDS.PathModel)path.Element).WIDTH!.Data!).Value);
        Assert.Equal(new[] { 4600, 0, 4600, 2000 }, Xy(path));
    }

    ///<summary>A label faces the way its own STRANS says, and that is composed with the instance like a placement's.</summary>
    [Fact]
    public void A_label_inside_a_turned_instance_turns_with_it()
    {
        var gds = GDS.FromText(Nested);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));

        var label = Cell(gds, "TOP").Elements.Single(element => element.Element is GDS.TextModel);
        var records = RecordsOf(gds, label);

        Assert.True(Strans.From(records.Single(record => record.Type == RecordType.STRANS).Data).ReflectAboutX);
        Assert.Equal(2.0, ((Real8Data)records.Single(record => record.Type == RecordType.MAG).Data!).Value);
        Assert.Equal(90.0, ((Real8Data)records.Single(record => record.Type == RecordType.ANGLE).Data!).Value);
        Assert.Equal(new[] { 2600, 1000 }, Xy(label));

        //And it still says what it said, which is the part a rebuilt label would lose first.
        Assert.Equal("M", RetextElement.TextOf(label));
    }

    ///<summary>Written where the instance was, so the cell reads in the order it did.</summary>
    [Fact]
    public void What_an_instance_held_goes_in_where_the_instance_was()
    {
        var gds = GDS.FromText(Nested);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));

        var kinds = Cell(gds, "TOP").Elements.Select(element => element.Element.GetType().Name).ToList();

        Assert.Equal(new[] { "BoundaryModel", "BoundaryModel", "SrefModel", "PathModel", "TextModel", "ArefModel" }, kinds);
    }

    ///
    ///Two at once, each one's shapes where it was. Every index is found before anything moves, so they are
    ///written last first - working from the front, the second placement's shapes went in at an index the
    ///first one's had already pushed along, and landed in the middle of them.
    ///
    [Fact]
    public void Two_placements_flattened_together_each_go_in_where_they_were()
    {
        var gds = GDS.FromText(Nested);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"), Placing<GDS.ArefModel>(gds, "TOP", "LEAF"));

        var kinds = Cell(gds, "TOP").Elements.Select(element => element.Element.GetType().Name).ToList();

        Assert.Equal(new[] { "BoundaryModel", "BoundaryModel", "SrefModel", "PathModel", "TextModel" }, kinds.Take(5));
        Assert.Equal(Enumerable.Repeat("BoundaryModel", 6), kinds.Skip(5));
    }

    ///
    ///**An array comes apart into every place.** The whole of it rather than the one place a click landed on,
    ///which is what the app did - it took the array out and put one place's shapes back.
    ///
    [Fact]
    public void An_array_flattens_into_every_place_it_has()
    {
        var gds = GDS.FromText(Nested);
        var before = Picture(gds);
        var array = Placing<GDS.ArefModel>(gds, "TOP", "LEAF");

        Assert.Equal(6, Flattening.CountOf(gds, array));

        Flatten(gds, "TOP", array);

        Assert.Equal(before, Picture(gds));
        Assert.DoesNotContain(Cell(gds, "TOP").Elements, element => element.Element is GDS.ArefModel);
        Assert.Equal(6, Cell(gds, "TOP").Elements.Count(element => element.Element is GDS.BoundaryModel boundary && ((Int2Data)boundary.LAYER.Data!).Value == 65));
    }

    ///
    ///**An absolute angle and magnification draw the way they drew.** Each is measured against the world, so
    ///the flattener divides the instance's own out when it draws one; moved into the cell above, it is
    ///written with what it was drawn with, and the flag goes, since that is now the cell's own value.
    ///
    [Fact]
    public void An_absolute_angle_and_magnification_inside_an_instance_draw_the_same_flattened()
    {
        var gds = GDS.FromText(Header + """
            BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
            STRNAME: INNER
            SREF:
            SNAME: LEAF
            STRANS: 0x0006
            MAG: 1.5
            ANGLE: 30
            XY: 300 100
            ENDEL:
            ENDSTR:
            BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
            STRNAME: TOP
            SREF:
            SNAME: INNER
            STRANS: 0x8000
            MAG: 2
            ANGLE: 90
            XY: 5000 0
            ENDEL:
            ENDSTR:
            ENDLIB:
            """);

        var before = Picture(gds);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "INNER"));

        Assert.Equal(before, Picture(gds));
    }

    #endregion **********************************************************************



    #region The cell, the history, and what is refused ******************************

    ///<summary>Nothing else places MID, so it would otherwise be left drawing itself as a top - every shape twice.</summary>
    [Fact]
    public void A_cell_goes_with_its_last_placement()
    {
        var gds = GDS.FromText(Nested);

        Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));

        Assert.Null(Hierarchy.Named(gds, "MID"));
    }

    [Fact]
    public void A_cell_something_else_still_places_stays()
    {
        var gds = GDS.FromText(Nested);

        //LEAF is placed by MID as well as by the array.
        Flatten(gds, "TOP", Placing<GDS.ArefModel>(gds, "TOP", "LEAF"));

        Assert.NotNull(Hierarchy.Named(gds, "LEAF"));
    }

    ///<summary>Two placements of one cell flattened together are all of them, so the cell goes - which one at a time would not see.</summary>
    [Fact]
    public void Flattening_every_placement_of_a_cell_at_once_takes_the_cell_too()
    {
        var gds = Top("SREF:\nSNAME: LEAF\nXY: 0 0\nENDEL:\nSREF:\nSNAME: LEAF\nXY: 2000 0\nENDEL:");
        var before = Picture(gds);

        Assert.Equal(new[] { "LEAF" }, Flattening.CellsGoing(gds, Cell(gds, "TOP").Elements));

        Flatten(gds, "TOP", Cell(gds, "TOP").Elements.ToArray());

        Assert.Equal(before, Picture(gds));
        Assert.Null(Hierarchy.Named(gds, "LEAF"));
    }

    [Fact]
    public void Undoing_a_flatten_puts_the_file_back_byte_for_byte()
    {
        var gds = GDS.FromText(Nested);
        byte[] before = gds.Serialize();

        var history = Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"), Placing<GDS.ArefModel>(gds, "TOP", "LEAF"));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    [Fact]
    public void And_redoing_it_flattens_it_again()
    {
        var gds = GDS.FromText(Nested);

        var history = Flatten(gds, "TOP", Placing<GDS.SrefModel>(gds, "TOP", "MID"));
        byte[] flattened = gds.Serialize();

        history.Undo();
        history.Redo();

        Assert.Equal(flattened, gds.Serialize());
    }

    ///<summary>A hundred and one by a hundred of one shape is past what one flatten writes.</summary>
    [Fact]
    public void An_array_too_big_to_take_apart_is_refused()
    {
        var gds = Top("AREF:\nSNAME: LEAF\nCOLROW: 101 100\nXY: 0 0 101000 0 0 50000\nENDEL:");
        var array = Placing<GDS.ArefModel>(gds, "TOP", "LEAF");

        Assert.Equal(10_100, Flattening.CountOf(gds, array));
        Assert.Null(Flattening.Edits(gds, Cell(gds, "TOP"), new[] { array }));
    }

    [Fact]
    public void A_flatten_that_would_land_past_what_a_coordinate_holds_is_refused()
    {
        var gds = Top("SREF:\nSNAME: LEAF\nSTRANS: 0x0000\nMAG: 3000000\nXY: 0 0\nENDEL:");

        Assert.Null(Flattening.Edits(gds, Cell(gds, "TOP"), new[] { Placing<GDS.SrefModel>(gds, "TOP", "LEAF") }));
    }

    [Fact]
    public void A_shape_is_not_a_placement_and_is_refused()
    {
        var gds = GDS.FromText(Nested);

        Assert.Null(Flattening.Edits(gds, Cell(gds, "TOP"), new[] { Cell(gds, "TOP").Elements[0] }));
    }

    #endregion **********************************************************************



    #region Finding the placement a click came through ******************************

    ///<summary>Any place of an array finds it - not only the first, whose point is the one the array is written at.</summary>
    [Fact]
    public void Any_place_of_an_array_finds_the_array()
    {
        var gds = GDS.FromText(Nested);
        var top = Cell(gds, "TOP");
        var array = Placing<GDS.ArefModel>(gds, "TOP", "LEAF");

        Assert.Same(array, Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(0, -3000)));
        Assert.Same(array, Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(2000, -2500)));
    }

    [Fact]
    public void A_point_between_places_or_past_the_last_finds_nothing()
    {
        var gds = GDS.FromText(Nested);
        var top = Cell(gds, "TOP");

        Assert.Null(Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(500, -3000)));
        Assert.Null(Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(3000, -3000)));
        Assert.Null(Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(0, -2000)));
    }

    ///<summary>A single row has a step of nothing down it, so there is no grid to solve, and the row is walked instead.</summary>
    [Fact]
    public void A_single_row_is_found_along_its_length()
    {
        var gds = Top("AREF:\nSNAME: LEAF\nCOLROW: 4 1\nXY: 0 0 4000 0 0 0\nENDEL:");
        var top = Cell(gds, "TOP");
        var array = Placing<GDS.ArefModel>(gds, "TOP", "LEAF");

        Assert.Same(array, Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(3000, 0)));
        Assert.Null(Hierarchy.PlacementAt(top, "LEAF", Transform.ForTranslation(4000, 0)));
    }

    [Fact]
    public void A_single_instance_is_found_by_its_point_and_by_nothing_else()
    {
        var gds = GDS.FromText(Nested);
        var top = Cell(gds, "TOP");

        Assert.Same(Placing<GDS.SrefModel>(gds, "TOP", "MID"), Hierarchy.PlacementAt(top, "MID", Transform.ForTranslation(2000, 0)));
        Assert.Null(Hierarchy.PlacementAt(top, "MID", Transform.ForTranslation(2000, 5)));
    }

    #endregion **********************************************************************



    #region Putting a placement through a turn **************************************

    ///
    ///**An array stays an array.** Its three points are all places, so each goes through the turn and the
    ///grid turns with them. Rebuilding the placement from its transform wrote a single instance, which is how
    ///turning an array in the app came to keep one place of it.
    ///
    [Fact]
    public void Turning_an_array_turns_every_place_and_keeps_it_an_array()
    {
        var gds = GDS.FromText(Nested);
        var array = Placing<GDS.ArefModel>(gds, "TOP", "LEAF");
        var quarter = Turning.About(Turn.Quarter, 0, 0);

        var drawn = GdsFlattener.Flatten(gds).Elements
            .Where(element => element.Source!.Ancestry.Count == 2 && element.Source.Structure == "LEAF")
            .Select(element => string.Join(" ", element.Points.Select(point => quarter.Apply(point.X, point.Y)).Select(point => $"{point.X},{point.Y}").OrderBy(text => text, StringComparer.Ordinal)))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        var turned = Hierarchy.Through(RecordsOf(gds, array), quarter)!;
        int at = Cell(gds, "TOP").Elements.IndexOf(array);

        new CompoundEdit("Turn", new List<LayoutEdit>
        {
            new DeleteElement(gds, Cell(gds, "TOP"), array),
            new AddElement(gds, Cell(gds, "TOP"), at, turned, "Turn")
        }).Apply();

        var after = GdsFlattener.Flatten(gds).Elements
            .Where(element => element.Source!.Ancestry.Count == 2 && element.Source.Structure == "LEAF")
            .Select(element => string.Join(" ", element.Points.Select(point => $"{point.X},{point.Y}").OrderBy(text => text, StringComparer.Ordinal)))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(6, after.Count);
        Assert.Equal(drawn, after);
        Assert.Contains(turned, record => record.Type == RecordType.AREF);
        Assert.Contains(turned, record => record.Type == RecordType.COLROW);
    }

    ///<summary>Properties ride on a placement's records, and a placement rebuilt from its transform dropped them.</summary>
    [Fact]
    public void Turning_a_placement_keeps_what_else_it_carries()
    {
        var gds = Top("SREF:\nSNAME: LEAF\nXY: 100 800\nPROPATTR: 1\nPROPVALUE: pin\nENDEL:");

        var turned = Hierarchy.Through(RecordsOf(gds, Placing<GDS.SrefModel>(gds, "TOP", "LEAF")), Turning.About(Turn.Quarter, 0, 0))!;

        Assert.Equal(
            new[] { RecordType.SREF, RecordType.SNAME, RecordType.STRANS, RecordType.ANGLE, RecordType.XY, RecordType.PROPATTR, RecordType.PROPVALUE, RecordType.ENDEL },
            turned.Select(record => record.Type).ToArray());

        Assert.Equal("pin", ((AsciiData)turned.Single(record => record.Type == RecordType.PROPVALUE).Data!).Value);
    }

    ///<summary>Turned all the way round, a placement is written as plainly as it started: no STRANS saying "no transform".</summary>
    [Fact]
    public void A_placement_turned_back_to_square_carries_no_transform()
    {
        var gds = Top("SREF:\nSNAME: LEAF\nSTRANS: 0x0000\nANGLE: 90\nXY: 100 800\nENDEL:");

        var turned = Hierarchy.Through(RecordsOf(gds, Placing<GDS.SrefModel>(gds, "TOP", "LEAF")), Turning.About(Turn.ThreeQuarters, 0, 0))!;

        Assert.DoesNotContain(turned, record => record.Type == RecordType.STRANS || record.Type == RecordType.ANGLE);
    }

    ///<summary>A plain shape has no orientation of its own to compose, and gets none written into it.</summary>
    [Fact]
    public void A_shape_is_only_its_corners()
    {
        var gds = GDS.FromText(Nested);
        var square = Cell(gds, "TOP").Elements[0];

        var turned = Hierarchy.Through(RecordsOf(gds, square), Turning.About(Turn.Quarter, 0, 0))!;

        Assert.Equal(RecordsOf(gds, square).Select(record => record.Type), turned.Select(record => record.Type));
        Assert.Equal(new[] { 0, -3000, 0, -2400, -600, -2400, -600, -3000, 0, -3000 }, ((Int4Data)turned.Single(record => record.Type == RecordType.XY).Data!).Values);
    }

    ///<summary>An array of an array is not something the format has, and making one wrote two sets of counts.</summary>
    [Fact]
    public void An_array_is_not_made_into_an_array()
    {
        var gds = GDS.FromText(Nested);

        Assert.Null(Hierarchy.AsArray(RecordsOf(gds, Placing<GDS.ArefModel>(gds, "TOP", "LEAF")), 2, 2, 100, 0, 0, 100));
    }

    #endregion **********************************************************************
}
