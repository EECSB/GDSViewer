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
        Assert.Equal(new LayerKey((short)layer, (short)dataType),
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
}
