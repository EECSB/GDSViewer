using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///The modal variables at a cell boundary. OASIS (10.1) has a reader forget every one of them but the six positions at
///each CELL record and each name record, so a record that leaves a field out leans on one set earlier *in its own
///cell* - and a file whose cell leans on the cell before is wrong. This project's writer wrote such files until
///2026-11-03, and its reader carried the variables across too, so 1,220 round trips agreed with each other and said
///nothing; KLayout read them with "Modal variable accessed before being defined".
///
///These pin both halves: a cell that says everything reads with nothing to report, one that leans on the cell before
///is read as its writer meant and reported, and what the writer writes now leans on nothing.
///
public class OasisModalTests
{
    private static int[] CornersOf(GDS.StructureModel cell)
    {
        var boundary = (GDS.BoundaryModel)cell.Elements.Single().Element;

        return ((Int4Data)boundary.XY!.Data!).Values.Take(8).ToArray();
    }

    private static short LayerOf(GDS.StructureModel cell)
    {
        return ((Int2Data)((GDS.BoundaryModel)cell.Elements.Single().Element).LAYER!.Data!).Value;
    }

    [Fact]
    public void A_cell_that_says_everything_reads_with_nothing_to_report()
    {
        var file = new OasisBuilder();

        file.Cell("ONE");
        file.Rectangle(1, 0, 100, 60, 0, 0);
        file.Cell("TWO");
        file.Rectangle(1, 0, 100, 60, 500, 0);

        var gds = OasisReader.Read(file.Done(), out var problems);

        Assert.Empty(problems);
        Assert.Equal(new[] { 500, 0, 600, 0, 600, 60, 500, 60 }, CornersOf(OasisBuilder.Named(gds, "TWO")));
    }

    ///
    ///Read as the writer meant - the second cell's rectangle is the first's size on the first's layer, which is what the
    ///file drew everywhere - and reported, once per variable, naming the cell.
    ///
    [Fact]
    public void A_cell_that_leans_on_the_cell_before_is_read_as_meant_and_reported()
    {
        var file = new OasisBuilder();

        file.Cell("ONE");
        file.Rectangle(1, 0, 100, 60, 0, 0);
        file.Cell("TWO");
        file.RectangleAt(500, 0);
        file.RectangleAt(700, 0);

        var gds = OasisReader.Read(file.Done(), out var problems);

        var two = OasisBuilder.Named(gds, "TWO");

        Assert.Equal(2, two.Elements.Count);
        Assert.Equal((short)1, ((Int2Data)((GDS.BoundaryModel)two.Elements[0].Element).LAYER!.Data!).Value);
        Assert.Equal(new[] { 500, 0, 600, 0, 600, 60, 500, 60 }, ((Int4Data)two.Elements[0].Element.XY!.Data!).Values.Take(8));

        //Four variables leaned on - layer, datatype, width, height - said once each, not once per record.
        Assert.Equal(4, problems.Count);
        Assert.All(problems, problem => Assert.StartsWith("Cell TWO uses the modal variable ", problem));
        Assert.Contains(problems, problem => problem.Contains(" layer "));
        Assert.Contains(problems, problem => problem.Contains(" datatype "));
        Assert.Contains(problems, problem => problem.Contains(" geometry-w "));
        Assert.Contains(problems, problem => problem.Contains(" geometry-h "));
    }

    [Fact]
    public void A_placement_that_names_no_cell_after_a_cell_record_is_reported()
    {
        var file = new OasisBuilder();

        file.Cell("LEAF");
        file.Rectangle(1, 0, 100, 60, 0, 0);
        file.Cell("TOP");
        file.Placement("LEAF", 0, 0);
        file.Cell("OTHER");
        file.PlacementAt(10, 10);

        var gds = OasisReader.Read(file.Done(), out var problems);

        var placed = (GDS.SrefModel)OasisBuilder.Named(gds, "OTHER").Elements.Single().Element;

        Assert.Equal("LEAF", ((AsciiData)placed.SNAME!.Data!).Value);

        var problem = Assert.Single(problems);

        Assert.StartsWith("Cell OTHER uses the modal variable placement-cell ", problem);
    }

    ///<summary>A file that leans on the cell before at every turn is said in the first twenty and counted after.</summary>
    [Fact]
    public void Many_faults_are_said_in_full_up_to_a_point_and_counted_past_it()
    {
        var file = new OasisBuilder();

        file.Cell("ONE");
        file.Rectangle(1, 0, 100, 60, 0, 0);

        for (int i = 0; i < 10; i++)
        {
            file.Cell($"LEANING{i}");
            file.RectangleAt(0, 0);
        }

        OasisReader.Read(file.Done(), out var problems);

        Assert.Equal(21, problems.Count);
        Assert.Equal("And 20 more of the same kind.", problems[20]);
    }

    ///
    ///The writer keeps to this now. Three cells, the second repeating the first's rectangle and the third on another
    ///layer: read back with nothing to report, and each cell saying its own layer and size - the third's rectangle was
    ///the one KLayout warned about, its size left to a cell it must not read.
    ///
    [Fact]
    public void What_the_writer_writes_leans_on_no_cell_before()
    {
        var back = OasisReader.Read(OasisWriter.Write(OasisBuilder.ThreeCellLibrary()), out var problems);

        Assert.Empty(problems);

        var three = OasisBuilder.Named(back, "THREE");

        Assert.Equal((short)9, LayerOf(three));
        Assert.Equal(new[] { 0, 0, 100, 0, 100, 60, 0, 60 }, CornersOf(three));
        Assert.Equal(new[] { 0, 0, 100, 0, 100, 60, 0, 60 }, CornersOf(OasisBuilder.Named(back, "TWO")));
    }

    ///<summary>The ordinary Read is the same reading without the report, for everything that has no use for one.</summary>
    [Fact]
    public void The_plain_read_reads_the_same_file()
    {
        var file = new OasisBuilder();

        file.Cell("ONE");
        file.Rectangle(1, 0, 100, 60, 0, 0);
        file.Cell("TWO");
        file.RectangleAt(500, 0);

        byte[] bytes = file.Done();

        Assert.Equal(GdsTestData.Geometry(OasisReader.Read(bytes, out _)), GdsTestData.Geometry(OasisReader.Read(bytes)));
    }
}
