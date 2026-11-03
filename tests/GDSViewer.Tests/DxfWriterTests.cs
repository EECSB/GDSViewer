using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Writing a library out as DXF.
///
///**The round trip is the test.** A writer can be checked against the group codes it emits, which says it
///wrote what it meant to and nothing about whether that was right - or it can be written out, read back,
///and compared against what went in. The second is what a person converting a file actually cares about,
///and it is available here because the reader on the other side of it is this project's too.
///
///That is also its weakness, and the reason `Klayout_reads_a_dxf_this_wrote` exists in
///<see cref="InteropTests"/>: a writer and a reader that share a wrong idea agree with each other
///perfectly. The round trips below say the two halves are consistent; only another program says the file
///is a DXF.
///
public class DxfWriterTests
{
    #region Libraries to write ******************************************************

    ///<summary>A library built here, so what goes into the writer is known rather than read from somewhere.</summary>
    private static GDS Built(params List<GDS.Record>[] structures)
    {
        var records = new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.HEADER, new Int2Data(600)),
            Hierarchy.Make(RecordType.BGNLIB, new Int2Data(new short[12])),
            Hierarchy.Make(RecordType.LIBNAME, new AsciiData("LIB")),
            Hierarchy.Make(RecordType.UNITS, new Real8Data(new double[] { 0.001, 1e-9 }))
        };

        foreach (var structure in structures)
            records.AddRange(structure);

        records.Add(Hierarchy.Make(RecordType.ENDLIB, null));

        return GDS.FromRecords(records);
    }

    ///<summary>One named cell, holding whatever is handed to it.</summary>
    private static List<GDS.Record> Cell(string name, params List<GDS.Record>[] elements)
    {
        var records = new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.BGNSTR, new Int2Data(new short[12])),
            Hierarchy.Make(RecordType.STRNAME, new AsciiData(name))
        };

        foreach (var element in elements)
            records.AddRange(element);

        records.Add(Hierarchy.Make(RecordType.ENDSTR, null));

        return records;
    }

    private static List<GDS.Record> Boundary(short layer, short dataType, params int[] xy)
    {
        return new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.BOUNDARY, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(layer)),
            Hierarchy.Make(RecordType.DATATYPE, new Int2Data(dataType)),
            Hierarchy.Make(RecordType.XY, new Int4Data(xy)),
            Hierarchy.Make(RecordType.ENDEL, null)
        };
    }

    ///<summary>A rectangle as a closed boundary, which is the shape most of these are made of.</summary>
    private static List<GDS.Record> Rectangle(short layer, short dataType, int width, int height)
    {
        return Boundary(layer, dataType, 0, 0, width, 0, width, height, 0, height, 0, 0);
    }

    private static List<GDS.Record> Path(short layer, int width, params int[] xy)
    {
        return new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.PATH, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(layer)),
            Hierarchy.Make(RecordType.DATATYPE, new Int2Data(0)),
            Hierarchy.Make(RecordType.WIDTH, new Int4Data(new int[] { width })),
            Hierarchy.Make(RecordType.XY, new Int4Data(xy)),
            Hierarchy.Make(RecordType.ENDEL, null)
        };
    }

    private static List<GDS.Record> Placement(string name, int x, int y, bool mirrored = false, double angle = 0)
    {
        return Hierarchy.PlacementRecords(name, new Element.Point(x, y), mirrored, angle);
    }

    private static List<GDS.Record> Array(string name, int columns, int rows, int across, int down)
    {
        var placement = Placement(name, 0, 0);

        return Hierarchy.AsArray(placement, columns, rows, across, 0, 0, down)!;
    }

    ///<summary>The library after a trip out to DXF and back, which is what every case below asks about.</summary>
    private static GDS RoundTripped(GDS gds)
    {
        return new GDS(DxfReader.Read(DxfWriter.Write(gds)).Serialize());
    }

    private static FlattenedLayout Drawn(GDS gds)
    {
        return GdsFlattener.Flatten(gds);
    }

    ///<summary>Each shape as its layer and its points, so two readings can be compared directly.</summary>
    private static List<string> Shapes(FlattenedLayout layout)
    {
        var shapes = new List<string>();

        foreach (var element in layout.Elements)
        {
            if (!string.IsNullOrEmpty(element.Text))
                continue;

            var points = element.Points.Select(point => $"{point.X},{point.Y}");

            shapes.Add($"{element.Layer.Key}:{string.Join(" ", points)}");
        }

        shapes.Sort(StringComparer.Ordinal);

        return shapes;
    }

    #endregion **********************************************************************



    #region Being a DXF at all ******************************************************

    ///<summary>What comes out is a DXF, by the same test the reader tells one by.</summary>
    [Fact]
    public void What_it_writes_is_read_as_a_drawing()
    {
        byte[] written = DxfWriter.Write(new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds")));

        Assert.True(DxfReader.LooksLikeDxf(written));
    }

    ///
    ///The sections a DXF is made of, in the order the format puts them.
    ///
    ///HEADER before TABLES before BLOCKS before ENTITIES is not a convention - a reader walks the file once
    ///and a block referenced before it is declared is a block that reader has never heard of.
    ///
    [Fact]
    public void The_sections_come_in_the_order_a_reader_walks_them()
    {
        string text = DxfWriter.Text(new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds")));

        int header = text.IndexOf("HEADER", StringComparison.Ordinal);
        int tables = text.IndexOf("TABLES", StringComparison.Ordinal);
        int blocks = text.IndexOf("BLOCKS", StringComparison.Ordinal);
        int entities = text.IndexOf("ENTITIES", StringComparison.Ordinal);

        Assert.True(header >= 0 && header < tables);
        Assert.True(tables < blocks);
        Assert.True(blocks < entities);

        Assert.EndsWith("EOF\n", text);
    }

    ///<summary>And it says what units it is in, rather than leaving a layout to be guessed at.</summary>
    [Fact]
    public void It_says_what_its_units_are()
    {
        string text = DxfWriter.Text(new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds")));

        Assert.Contains("$INSUNITS", text);

        //13 is microns, which is what the coordinates are written in.
        int at = text.IndexOf("$INSUNITS", StringComparison.Ordinal);

        Assert.Contains("13", text.Substring(at, 30));
    }

    ///
    ///**A UNITS that is not a size is written at a nanometer**, the way a missing one is - rather than multiplied
    ///through every coordinate, which for an infinity is a drawing of infinities. No file can say that and the
    ///encoder refuses to write it, so it reaches a library only behind their backs - and the writer believed it.
    ///
    [Fact]
    public void A_unit_that_is_not_a_size_is_written_as_a_nanometer()
    {
        var sound = Built(Cell("TOP", Boundary(1, 0, 0, 0, 1000, 0, 1000, 1000, 0, 1000, 0, 0)));
        var broken = Built(Cell("TOP", Boundary(1, 0, 0, 0, 1000, 0, 1000, 1000, 0, 1000, 0, 0)));

        broken.StreamFormat.UNITS.Data = new Real8Data(0.001, double.PositiveInfinity);

        Assert.Equal(DxfWriter.Text(sound), DxfWriter.Text(broken));
    }

    #endregion **********************************************************************



    #region What comes back *********************************************************

    ///
    ///**A real file goes out and comes back the same drawing.** Every shape, on the layer it was on, at the
    ///coordinates it was at.
    ///
    ///The layer numbers are the part that has nowhere else to live: DXF layers are names, so they are
    ///written as `L65D20` and read back out of that. Anything else about the name would lose them.
    ///
    [Fact]
    public void A_sample_survives_the_trip_out_and_back()
    {
        var original = new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds"));

        Assert.Equal(Shapes(Drawn(original)), Shapes(Drawn(RoundTripped(original))));
    }

    ///<summary>Including its labels, which are the one thing carrying a string rather than a shape.</summary>
    [Fact]
    public void The_labels_survive_it_too()
    {
        var original = new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds"));

        var went = Drawn(original).Elements.Where(element => !string.IsNullOrEmpty(element.Text))
            .Select(element => $"{element.Text}@{element.Points[0].X},{element.Points[0].Y}")
            .OrderBy(one => one, StringComparer.Ordinal).ToList();

        var came = Drawn(RoundTripped(original)).Elements.Where(element => !string.IsNullOrEmpty(element.Text))
            .Select(element => $"{element.Text}@{element.Points[0].X},{element.Points[0].Y}")
            .OrderBy(one => one, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(went);
        Assert.Equal(went, came);
    }

    ///<summary>A layer keeps both halves of its pair, not only the number.</summary>
    [Theory]
    [InlineData(65, 20)]
    [InlineData(0, 0)]
    [InlineData(255, 255)]
    [InlineData(32767, 32767)]
    public void A_layer_and_its_datatype_both_come_back(int layer, int dataType)
    {
        Assert.Equal(
            new LayerKey((short)layer, (short)dataType),
            DxfReader.NumberFromName(DxfWriter.LayerName((short)layer, (short)dataType)));
    }

    ///
    ///**A path stays a path**, with its width - which is the one element whose meaning is not its outline.
    ///
    ///Written as an open run, so nothing fills it in; the width comes back through the run's own, which is
    ///what turns a centerline into the shape a wire covers.
    ///
    [Fact]
    public void A_path_keeps_its_width()
    {
        var gds = Built(Cell("WIRE", Path(5, 2000, 0, 0, 10000, 0)));

        var came = Drawn(RoundTripped(gds)).Elements.Single();

        //Two microns across, which is two thousand database units.
        Assert.Equal(2000, Bounds.Of(came.Points).Height);
        Assert.Equal(10000, Bounds.Of(came.Points).Width);
    }

    ///<summary>A boundary stays closed, which is what says it has an area at all.</summary>
    [Fact]
    public void A_boundary_stays_an_outline()
    {
        var gds = Built(Cell("PAD", Rectangle(7, 3, 4000, 4000)));

        var reopened = DxfReader.Read(DxfWriter.Write(gds));

        Assert.Contains(reopened.Records, record => record.Type == RecordType.BOUNDARY);
        Assert.DoesNotContain(reopened.Records, record => record.Type == RecordType.PATH);

        var came = Drawn(new GDS(reopened.Serialize())).Elements.Single();

        Assert.Equal(new LayerKey(7, 3), came.Layer.Key);
        Assert.Equal(16e6, Measure.AreaOf(came.Points));
    }

    #endregion **********************************************************************



    #region The hierarchy ***********************************************************

    ///
    ///A cell something places becomes a block, and the placement an insert - so the hierarchy survives
    ///rather than being flattened on the way out.
    ///
    ///Flattening would be the easy answer and would be a different file: a library of a thousand instances
    ///of one cell becomes a thousand copies of its geometry, which is the size problem hierarchy exists to
    ///solve.
    ///
    [Fact]
    public void A_placed_cell_becomes_a_block_and_the_placement_an_insert()
    {
        var gds = Built(
            Cell("PAD", Rectangle(5, 0, 2000, 2000)),
            Cell("TOP", Placement("PAD", 10000, 20000)));

        string text = DxfWriter.Text(gds);

        Assert.Contains("BLOCK", text);
        Assert.Contains("INSERT", text);

        //The cell is still a cell on the way back, rather than its geometry copied into the drawing.
        var came = RoundTripped(gds);

        Assert.Contains("PAD", Hierarchy.Names(came));
        Assert.Equal(1, Hierarchy.PlacementsOf(came, "PAD"));

        //And it is placed where it was.
        var box = Bounds.Of(Drawn(came).Elements.Single().Points);

        Assert.Equal(10000, box.Left);
        Assert.Equal(20000, box.Bottom);
    }

    ///<summary>An array becomes one repeated insert rather than one insert per position.</summary>
    [Fact]
    public void An_array_becomes_a_repeated_insert()
    {
        var gds = Built(
            Cell("PAD", Rectangle(5, 0, 1000, 1000)),
            Cell("TOP", Array("PAD", 3, 2, 3000, 4000)));

        string text = DxfWriter.Text(gds);

        //One entity for the whole array, which is what an AREF is.
        Assert.Contains("MINSERT", text);

        var came = Drawn(RoundTripped(gds));

        //Six of them, three microns apart across and two up.
        Assert.Equal(6, came.Elements.Count);

        var box = Bounds.Of(came.Elements.SelectMany(element => element.Points).ToList());

        Assert.Equal(0, box.Left);
        Assert.Equal(7000, box.Right);
        Assert.Equal(5000, box.Top);
    }

    ///
    ///And a mirrored placement comes back mirrored, which is the transform with no direct spelling.
    ///
    ///GDSII reflects about the X axis and then rotates; DXF has no flag and says it with a negative scale.
    ///The two are translated in both directions - see DxfReader.MirrorOf - and a round trip is the one
    ///test that catches a translation that is self-consistent and backwards.
    ///
    [Fact]
    public void A_mirrored_placement_comes_back_mirrored()
    {
        var gds = Built(
            Cell("ELL", Rectangle(5, 0, 4000, 1000)),
            Cell("TOP", Placement("ELL", 0, 0, mirrored: true)));

        var went = Bounds.Of(Drawn(gds).Elements.Single().Points);
        var came = Bounds.Of(Drawn(RoundTripped(gds)).Elements.Single().Points);

        //Reflected about the X axis, so it hangs below the line rather than sitting on it - and comes back
        //hanging below it rather than the right way up.
        Assert.Equal(-1000, went.Bottom);
        Assert.Equal(went.Bottom, came.Bottom);
        Assert.Equal(went.Top, came.Top);
        Assert.Equal(went.Left, came.Left);
        Assert.Equal(went.Right, came.Right);
    }

    ///<summary>A rotated one too, which is the transform that has a spelling and could still be lost.</summary>
    [Fact]
    public void A_turned_placement_comes_back_turned()
    {
        var gds = Built(
            Cell("ELL", Rectangle(5, 0, 4000, 1000)),
            Cell("TOP", Placement("ELL", 0, 0, angle: 90)));

        var came = Bounds.Of(Drawn(RoundTripped(gds)).Elements.Single().Points);

        //A quarter turn swaps how wide it is for how tall.
        Assert.Equal(1000, came.Width);
        Assert.Equal(4000, came.Height);
    }

    #endregion **********************************************************************

    #region What was lost on the way ****************************************************

    private static List<GDS.Record> Label(short layer, string text, int x, int y, TextPresentation presentation)
    {
        return new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.TEXT, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(layer)),
            Hierarchy.Make(RecordType.TEXTTYPE, new Int2Data(0)),
            Hierarchy.Make(RecordType.PRESENTATION, new BitArrayData(presentation.Encode())),
            Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { x, y })),
            Hierarchy.Make(RecordType.STRING, new AsciiData(text)),
            Hierarchy.Make(RecordType.ENDEL, null)
        };
    }

    private static List<GDS.Record> Node(short layer, params int[] xy)
    {
        return new List<GDS.Record>
        {
            Hierarchy.Make(RecordType.NODE, null),
            Hierarchy.Make(RecordType.LAYER, new Int2Data(layer)),
            Hierarchy.Make(RecordType.NODETYPE, new Int2Data(0)),
            Hierarchy.Make(RecordType.XY, new Int4Data(xy)),
            Hierarchy.Make(RecordType.ENDEL, null)
        };
    }

    ///
    ///Which part of a label sits at its point survives the trip: 72 and 73 go out from PRESENTATION and come back into
    ///it, and the point goes out again as the second alignment point a DXF reader anchors justified text at. Neither
    ///was written or read until 2026-11-03, so every label came back left and bottom.
    ///
    [Fact]
    public void A_labels_justification_survives_the_trip()
    {
        var presentation = new TextPresentation(HorizontalPresentation.Right, VerticalPresentation.Top, 0);
        var original = Built(Cell("A", Label(1, "PAD", 300, 400, presentation)));

        string text = DxfWriter.Text(original);

        Assert.Contains("\n 72\n2\n", text);
        Assert.Contains("\n 73\n3\n", text);
        Assert.Contains("\n 11\n0.3\n", text);

        var back = RoundTripped(original);
        var label = (GDS.TextModel)back.StreamFormat.Structures.Single().Elements.Single().Element;
        var came = TextPresentation.From(label.TextBody.PRESENTATION?.Data);

        Assert.Equal(HorizontalPresentation.Right, came.Horizontal);
        Assert.Equal(VerticalPresentation.Top, came.Vertical);

        var drawn = Drawn(back).Elements.Single();

        Assert.Equal(300, drawn.Points[0].X);
        Assert.Equal(400, drawn.Points[0].Y);
    }

    ///<summary>The format's default, left and bottom, is DXF's own and goes out as nothing - so a plain label's entity is what it always was.</summary>
    [Fact]
    public void A_label_at_the_left_and_bottom_writes_no_justification()
    {
        var original = Built(Cell("A", Label(1, "PAD", 0, 0, new TextPresentation(HorizontalPresentation.Left, VerticalPresentation.Bottom, 0))));

        string text = DxfWriter.Text(original);

        Assert.DoesNotContain("\n 72\n", text);
        Assert.DoesNotContain("\n 73\n", text);
    }

    ///<summary>A NODE has no entity, and is counted rather than left out quietly - the OASIS writer says the same thing of the same element.</summary>
    [Fact]
    public void A_node_has_no_entity_and_is_counted()
    {
        var original = Built(Cell("A", Rectangle(1, 0, 10, 10), Node(2, 0, 0, 5, 5)));

        DxfWriter.Write(original, out int skipped);

        Assert.Equal(1, skipped);

        DxfWriter.Write(Built(Cell("A", Rectangle(1, 0, 10, 10))), out int none);

        Assert.Equal(0, none);
    }

    ///<summary>A layer's name is a data file's, not prose: under a culture whose minus sign is not one, a negative number went into it as something the reader could not take back out.</summary>
    [Fact]
    public void A_layer_name_is_written_invariantly()
    {
        Assert.Equal("L-3D7", GdsTestData.UnderHostileCulture(() => DxfWriter.LayerName(-3, 7)));
    }

    ///<summary>A negative WIDTH is the format's absolute one, drawn at its size - it was dropped, and the run came back as a line of no width.</summary>
    [Fact]
    public void A_negative_width_goes_out_as_its_size()
    {
        var original = Built(Cell("A", Path(1, -200, 0, 0, 1000, 0)));

        string text = DxfWriter.Text(original);

        Assert.Contains("\n 40\n0.2\n", text);
    }

    #endregion **********************************************************************
}
