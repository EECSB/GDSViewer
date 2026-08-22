using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Writing a path: the centerline, the width, and the ends.
///
///**The round trip is the test.** A path assembled wrongly looks fine in the model that made it - every
///property reads back exactly what was put in - and is a file another reader either refuses or draws
///differently. So everything here goes out through <c>Serialize</c> and comes back through the parser before
///a single assertion, and the ones that matter most go one step further and check what the *flattener* draws,
///because a width and an end style that reach the file and change nothing on screen are worse than absent.
///</summary>
public class PathTests
{
    #region A library to draw into **************************************************

    private static GDS Placed()
    {
        return new GDS(GdsTestData.ReadFixture("placed.gds"));
    }

    private static GDS.StructureModel Named(GDS gds, string name)
    {
        return gds.StreamFormat.Structures.Single(structure =>
            ((AsciiData)structure.STRNAME.Data!).Value == name);
    }

    private static readonly LayerKey OnLayer = new LayerKey(70, 0);

    private static List<Element.Point> Along(params int[] coordinates)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < coordinates.Length; i += 2)
            points.Add(new Element.Point(coordinates[i], coordinates[i + 1]));

        return points;
    }

    ///<summary>Draws one path into TOP and hands back the file as it reads after a round trip.</summary>
    private static GDS Drawn(IReadOnlyList<Element.Point> along, int width, Paths.Ends ends)
    {
        var gds = Placed();

        new AddElement(gds, Named(gds, "TOP"), OnLayer, along, width, ends).Apply();

        return new GDS(gds.Serialize());
    }

    private static GDS.PathModel OnlyPath(GDS gds)
    {
        return gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Select(element => element.Element)
            .OfType<GDS.PathModel>()
            .Single();
    }

    #endregion **********************************************************************



    #region What gets written *******************************************************

    [Fact]
    public void A_drawn_path_survives_being_written_and_read_back()
    {
        var path = OnlyPath(Drawn(Along(0, 0, 1000, 0, 1000, 800), 120, Paths.Ends.Round));

        Assert.Equal(new int[] { 0, 0, 1000, 0, 1000, 800 }, ((Int4Data)path.XY!.Data!).Values);
        Assert.Equal(120, ((Int4Data)path.WIDTH!.Data!).Value);
        Assert.Equal(1, ((Int2Data)path.PATHTYPE!.Data!).Value);
        Assert.Equal(70, ((Int2Data)path.LAYER!.Data!).Value);
        Assert.Equal(0, ((Int2Data)path.DATATYPE!.Data!).Value);
    }

    ///
    ///**A path is not closed.** A boundary's last corner has to be its first; joining a path's ends would
    ///draw a wire back to where it started, which is a different route from the one that was clicked.
    ///
    [Fact]
    public void A_path_keeps_its_ends_apart()
    {
        var path = OnlyPath(Drawn(Along(0, 0, 500, 0, 500, 500), 100, Paths.Ends.Flush));

        int[] xy = ((Int4Data)path.XY!.Data!).Values;

        Assert.Equal(6, xy.Length);
        Assert.NotEqual((xy[0], xy[1]), (xy[^2], xy[^1]));
    }

    ///<summary>Two points is a wire. It is the polygon that needs three, and only because it needs area.</summary>
    [Fact]
    public void Two_points_is_a_path()
    {
        Assert.NotNull(Paths.Records(OnLayer, Along(0, 0, 900, 0), 50, Paths.Ends.Flush));
    }

    [Fact]
    public void One_point_is_not()
    {
        Assert.Null(Paths.Records(OnLayer, Along(0, 0), 50, Paths.Ends.Flush));
    }

    ///<summary>A click on top of the one before it is a zero-length segment, which has no direction to turn by.</summary>
    [Fact]
    public void A_repeated_point_is_dropped()
    {
        var records = Paths.Records(OnLayer, Along(0, 0, 500, 0, 500, 0, 500, 400), 50, Paths.Ends.Flush);

        Assert.NotNull(records);

        int[] xy = ((Int4Data)records!.Single(record => record.Type == RecordType.XY).Data!).Values;

        Assert.Equal(new int[] { 0, 0, 500, 0, 500, 400 }, xy);
    }

    ///<summary>Repeats dropped can take it below two points, and then there is no path left.</summary>
    [Fact]
    public void A_path_that_is_all_one_point_is_refused()
    {
        Assert.Null(Paths.Records(OnLayer, Along(700, 700, 700, 700, 700, 700), 50, Paths.Ends.Flush));
    }

    [Fact]
    public void A_negative_width_is_refused()
    {
        Assert.Null(Paths.Records(OnLayer, Along(0, 0, 900, 0), -1, Paths.Ends.Flush));
    }

    ///
    ///**The records come out in the order the format states.**
    ///
    ///PATH, LAYER, DATATYPE, PATHTYPE, WIDTH, XY, ENDEL. A reader that walks the stream expecting that order
    ///stops at the first record out of place - and this app's own parser is forgiving enough that a wrong
    ///order reads back perfectly here and fails somewhere else entirely.
    ///
    [Fact]
    public void The_records_are_in_the_order_the_format_states()
    {
        var records = Paths.Records(OnLayer, Along(0, 0, 900, 0), 50, Paths.Ends.Extended);

        Assert.NotNull(records);

        Assert.Equal(
            new[]
            {
                RecordType.PATH,
                RecordType.LAYER,
                RecordType.DATATYPE,
                RecordType.PATHTYPE,
                RecordType.WIDTH,
                RecordType.XY,
                RecordType.ENDEL
            },
            records!.Select(record => record.Type));
    }

    #endregion **********************************************************************



    #region What it draws ***********************************************************

    ///
    ///**The width reaches the picture.**
    ///
    ///A path is stored as a centerline, so what is drawn is an outline the flattener builds from the width -
    ///and a WIDTH record that arrives correctly and is never used would leave a wire on screen as a hairline.
    ///Measured as the box the outline fills across a horizontal run, which is the width plus nothing for
    ///flush ends.
    ///
    [Fact]
    public void The_width_is_what_gets_drawn()
    {
        var drawn = GdsFlattener.Flatten(Drawn(Along(0, 0, 1000, 0), 200, Paths.Ends.Flush));

        var wire = drawn.Elements.Single(element => element.Layer.Key.Number == 70);

        var box = Bounds.Of(wire.Points);

        Assert.Equal(200, box.Top - box.Bottom);
        Assert.Equal(1000, box.Right - box.Left);
    }

    ///
    ///**Extended ends reach half a width past each endpoint**, where flush ends stop on it. Which is the one
    ///observable difference between the two, and the reason the control exists at all.
    ///
    [Theory]
    [InlineData(Paths.Ends.Flush, 1000)]
    [InlineData(Paths.Ends.Extended, 1200)]
    public void The_ends_decide_how_far_it_reaches(Paths.Ends ends, int expected)
    {
        var drawn = GdsFlattener.Flatten(Drawn(Along(0, 0, 1000, 0), 200, ends));

        var wire = drawn.Elements.Single(element => element.Layer.Key.Number == 70);

        var box = Bounds.Of(wire.Points);

        Assert.Equal(expected, box.Right - box.Left);
    }

    ///<summary>A path on a layer the file never used still draws, which needs that layer registered.</summary>
    [Fact]
    public void A_path_brings_its_layer_with_it()
    {
        var gds = Placed();

        Assert.False(gds.AdditionalInformation.Layers.ContainsKey(OnLayer));

        new AddElement(gds, Named(gds, "TOP"), OnLayer, Along(0, 0, 900, 0), 100, Paths.Ends.Flush).Apply();

        Assert.Contains(GdsFlattener.Flatten(gds).Elements, element => element.Layer.Key.Number == 70);
    }

    [Fact]
    public void Undoing_a_drawn_path_puts_the_file_back_exactly()
    {
        var gds = Placed();

        byte[] before = gds.Serialize();

        var history = new EditHistory();

        history.Do(new AddElement(gds, Named(gds, "TOP"), OnLayer, Along(0, 0, 900, 400), 100, Paths.Ends.Round));

        Assert.NotEqual(before, gds.Serialize());

        history.Undo();

        Assert.Equal(before, gds.Serialize());
    }

    ///
    ///**A path of no width encloses nothing, everywhere that asks.**
    ///
    ///It has no outline to build, so the flattener hands its centerline through - and anything that treats a
    ///set of points as a ring then joins the two ends and counts the shape between them. On a straight line
    ///that is nothing; on an arc, which is what a DXF is full of, it is a solid segment. Every measurement
    ///and every extrusion in the app comes through these two, so this is the seam where it has to be true.
    ///
    [Fact]
    public void A_path_with_no_width_covers_no_ground()
    {
        //Bent, so the two ends of the centerline are not on the same line - a straight one encloses nothing
        //whatever the code does, and would pass on an implementation that counts it.
        var gds = Drawn(Along(0, 0, 1000, 0, 1000, 1000), 0, Paths.Ends.Flush);

        var drawn = GdsFlattener.Flatten(gds);

        Assert.Equal(0, Measure.DrawnAreaOf(drawn, OnLayer));
        Assert.Equal(0, Measure.CoveredAreaOf(drawn, OnLayer));
    }

    ///<summary>And it is not extruded, since a line has no volume - the same filter, at the same seam.</summary>
    [Fact]
    public void A_path_with_no_width_is_not_merged_into_a_slab()
    {
        var drawn = GdsFlattener.Flatten(Drawn(Along(0, 0, 1000, 0, 1000, 1000), 0, Paths.Ends.Flush));

        Assert.DoesNotContain(Booleans.MergeByLayer(drawn.Elements), outline => outline.Layer.Key.Equals(OnLayer));
    }

    ///<summary>One that has a width is still all three of those things, which is what says the filter is not too wide.</summary>
    [Fact]
    public void A_path_with_a_width_still_counts()
    {
        var drawn = GdsFlattener.Flatten(Drawn(Along(0, 0, 1000, 0), 200, Paths.Ends.Flush));

        Assert.Equal(200000, Measure.DrawnAreaOf(drawn, OnLayer));
        Assert.Equal(200000, Measure.CoveredAreaOf(drawn, OnLayer));

        Assert.Contains(Booleans.MergeByLayer(drawn.Elements), outline => outline.Layer.Key.Equals(OnLayer));
    }

    #endregion **********************************************************************



    #region Changing one afterwards *************************************************

    ///<summary>The records of the only path in a file, as a span to be rewritten.</summary>
    private static List<GDS.Record> RecordsOf(GDS gds)
    {
        var model = gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.PathModel);

        int start = gds.Records.IndexOf(model.Element.Opening);
        int end = gds.Records.IndexOf(model.ENDEL);

        return gds.Records.GetRange(start, end - start + 1);
    }

    [Fact]
    public void A_path_reads_back_what_it_is_drawn_with()
    {
        var gds = Drawn(Along(0, 0, 900, 0), 140, Paths.Ends.Extended);

        var model = gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.PathModel);

        Assert.Equal((140, Paths.Ends.Extended), Paths.Of(model));
    }

    ///<summary>A path with neither record is a hairline with square ends, which is what the format states.</summary>
    [Fact]
    public void A_path_with_no_records_reads_as_the_defaults()
    {
        var gds = Placed();

        new AddElement(
            gds,
            Named(gds, "TOP"),
            new List<GDS.Record>
            {
                Hierarchy.Make(RecordType.PATH, null),
                Hierarchy.Make(RecordType.LAYER, new Int2Data(70)),
                Hierarchy.Make(RecordType.DATATYPE, new Int2Data(0)),
                Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { 0, 0, 900, 0 })),
                Hierarchy.Make(RecordType.ENDEL, null)
            },
            "Bare").Apply();

        var reopened = new GDS(gds.Serialize());

        var model = reopened.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.PathModel);

        Assert.Equal((0, Paths.Ends.Flush), Paths.Of(model));
    }

    ///
    ///**A path that had no width can be given one**, which is the case that makes this a rebuild rather than
    ///a change: there is no record to write into, so one has to be added.
    ///
    [Fact]
    public void A_width_can_be_added_to_a_path_that_had_none()
    {
        var gds = Placed();

        new AddElement(
            gds,
            Named(gds, "TOP"),
            new List<GDS.Record>
            {
                Hierarchy.Make(RecordType.PATH, null),
                Hierarchy.Make(RecordType.LAYER, new Int2Data(70)),
                Hierarchy.Make(RecordType.DATATYPE, new Int2Data(0)),
                Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { 0, 0, 1000, 0 })),
                Hierarchy.Make(RecordType.ENDEL, null)
            },
            "Bare").Apply();

        var rewritten = Paths.Rewritten(RecordsOf(gds), 300, Paths.Ends.Flush);

        Assert.NotNull(rewritten);

        new AddElement(gds, Named(gds, "TOP"), rewritten!, "Width").Apply();

        //Two paths now, and the second one is 300 across where the first is a hairline.
        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize()));

        var widths = drawn.Elements
            .Where(element => element.Layer.Key.Number == 70)
            .Select(element => Bounds.Of(element.Points))
            .Select(box => (long)(box.Top - box.Bottom))
            .Order()
            .ToList();

        Assert.Equal(new List<long> { 0, 300 }, widths);
    }

    [Fact]
    public void Rewriting_keeps_the_centerline_and_the_layer()
    {
        var rewritten = Paths.Rewritten(RecordsOf(Drawn(Along(0, 0, 900, 400), 100, Paths.Ends.Flush)), 250, Paths.Ends.Round);

        Assert.NotNull(rewritten);

        Assert.Equal(
            new int[] { 0, 0, 900, 400 },
            ((Int4Data)rewritten!.Single(record => record.Type == RecordType.XY).Data!).Values);

        Assert.Equal(70, ((Int2Data)rewritten.Single(record => record.Type == RecordType.LAYER).Data!).Value);
        Assert.Equal(250, ((Int4Data)rewritten.Single(record => record.Type == RecordType.WIDTH).Data!).Value);
        Assert.Equal(1, ((Int2Data)rewritten.Single(record => record.Type == RecordType.PATHTYPE).Data!).Value);
    }

    ///<summary>Rewritten twice is not two widths and two end styles.</summary>
    [Fact]
    public void Rewriting_replaces_rather_than_adds()
    {
        var once = Paths.Rewritten(RecordsOf(Drawn(Along(0, 0, 900, 0), 100, Paths.Ends.Flush)), 250, Paths.Ends.Round);

        var twice = Paths.Rewritten(once!, 400, Paths.Ends.Extended);

        Assert.NotNull(twice);

        Assert.Single(twice!, record => record.Type == RecordType.WIDTH);
        Assert.Single(twice!, record => record.Type == RecordType.PATHTYPE);
        Assert.Equal(400, ((Int4Data)twice!.Single(record => record.Type == RecordType.WIDTH).Data!).Value);
    }

    ///<summary>The order still holds after a rewrite, which is the half of it a round trip cannot see.</summary>
    [Fact]
    public void A_rewritten_path_is_still_in_the_format_s_order()
    {
        var rewritten = Paths.Rewritten(RecordsOf(Drawn(Along(0, 0, 900, 0), 100, Paths.Ends.Flush)), 250, Paths.Ends.Round);

        Assert.NotNull(rewritten);

        Assert.Equal(
            new[]
            {
                RecordType.PATH,
                RecordType.LAYER,
                RecordType.DATATYPE,
                RecordType.PATHTYPE,
                RecordType.WIDTH,
                RecordType.XY,
                RecordType.ENDEL
            },
            rewritten!.Select(record => record.Type));
    }

    [Fact]
    public void Rewriting_something_that_is_not_a_path_is_refused()
    {
        var boundary = new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.BOUNDARY, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(70)),
            Hierarchy.Make(RecordType.DATATYPE, new Int2Data(0)),
            Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { 0, 0, 10, 0, 10, 10, 0, 0 })),
            Hierarchy.Make(RecordType.ENDEL, null)
        };

        Assert.Null(Paths.Rewritten(boundary, 100, Paths.Ends.Flush));
    }

    [Fact]
    public void Rewriting_to_a_negative_width_is_refused()
    {
        Assert.Null(Paths.Rewritten(RecordsOf(Drawn(Along(0, 0, 900, 0), 100, Paths.Ends.Flush)), -5, Paths.Ends.Flush));
    }

    ///
    ///A width written straight back into the file draws the wire wider, which is the whole point and the one
    ///thing the record's own value cannot tell you.
    ///
    [Fact]
    public void A_rewritten_width_is_what_gets_drawn()
    {
        var gds = Drawn(Along(0, 0, 1000, 0), 100, Paths.Ends.Flush);

        var model = gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.PathModel);

        var structure = Named(gds, "TOP");
        int index = structure.Elements.IndexOf(model);

        var rewritten = Paths.Rewritten(RecordsOf(gds), 500, Paths.Ends.Flush);

        new CompoundEdit("Path width", new List<LayoutEdit>
        {
            new DeleteElement(gds, structure, model),
            new AddElement(gds, structure, index, rewritten!, "Path width")
        }).Apply();

        var wire = GdsFlattener.Flatten(new GDS(gds.Serialize()))
            .Elements.Single(element => element.Layer.Key.Number == 70);

        var box = Bounds.Of(wire.Points);

        Assert.Equal(500, box.Top - box.Bottom);
    }

    #endregion **********************************************************************
}
