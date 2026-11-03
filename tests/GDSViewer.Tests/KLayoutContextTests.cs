using GdsII;
using GdsII.Cli;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///KLayout's context cell: the structure KLayout writes first in a GDSII library to say which of its cells were made from
///a PCell or a library, and which it makes no cell of when it reads the file back - so neither does this. See
///GDS.StreamFormatModel.Context.
///
///**Built by hand, and held to KLayout.** The libraries below are what KLayout writes, cut down to one cell made from a
///PCell and carrying the strings it writes; KLayout reading each of them is the second opinion, and a library KLayout
///writes itself, with one of its own PCells in it, is the check that the hand-built one is the real thing. What this was
///found on, IHP's 60 GHz amplifier core, is somebody else's layout and is not kept here.
///</summary>
public class KLayoutContextTests : IDisposable
{
    #region Libraries *********************************************************************

    private const string Head = """
        HEADER: 600
        BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21
        LIBNAME: LIB
        UNITS: 0.001 1E-09

        """;

    ///
    ///KLayout's context cell as it writes one: a boundary of no area on 0/0 carrying a note kept with the layout, then an
    ///SREF at the origin of the one cell made from a PCell, with the library, the PCell and a parameter on it - in the
    ///order and the spelling IHP's amplifier core has them.
    ///
    private const string Context = """
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: $$$CONTEXT_INFO$$$
        BOUNDARY:
        LAYER: 0
        DATATYPE: 0
        XY: 0 0 0 0 0 0 0 0 0 0
        PROPATTR: 0
        PROPVALUE: META(written_by)='GDS Viewer'
        ENDEL:
        SREF:
        SNAME: via_stack$1
        XY: 0 0
        PROPATTR: 2
        PROPVALUE: PCELL=via_stack
        PROPATTR: 1
        PROPVALUE: P(vn_columns)=#10
        PROPATTR: 0
        PROPVALUE: LIB=SG13_dev
        ENDEL:
        ENDSTR:

        """;

    ///<summary>The cell made from the PCell: one contact.</summary>
    private const string Via = """
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: via_stack$1
        BOUNDARY:
        LAYER: 6
        DATATYPE: 0
        XY: 0 0 160 0 160 160 0 160 0 0
        ENDEL:
        ENDSTR:

        """;

    ///<summary>The layout: the via stack placed away from the origin, so one drawn at the origin as well shows.</summary>
    private const string Top = """
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: TOP
        SREF:
        SNAME: via_stack$1
        XY: 5000 2000
        ENDEL:
        ENDSTR:

        """;

    ///<summary>A layout that does not place the via stack: a box of its own and nothing else.</summary>
    private const string TopAlone = """
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: TOP
        BOUNDARY:
        LAYER: 2
        DATATYPE: 0
        XY: 1000 1000 4000 1000 4000 2000 1000 2000 1000 1000
        ENDEL:
        ENDSTR:

        """;

    ///<summary>The library as KLayout writes it, its context first.</summary>
    private const string AsWritten = Head + Context + Via + Top + "ENDLIB:\n";

    ///<summary>The same structures with the context second, where KLayout reads it as a cell like any other.</summary>
    private const string ContextSecond = Head + Via + Context + Top + "ENDLIB:\n";

    ///<summary>A context naming a cell nothing in the layout places.</summary>
    private const string NothingElsePlaces = Head + Context + Via + TopAlone + "ENDLIB:\n";

    private static readonly Dictionary<string, string> Libraries = new Dictionary<string, string>
    {
        ["as-written"] = AsWritten,
        ["context-second"] = ContextSecond,
        ["nothing-else-places"] = NothingElsePlaces
    };

    ///<summary>The names of a library's cells, in the file's order.</summary>
    private static List<string> CellsOf(GDS gds)
    {
        return Hierarchy.Summarize(gds).Select(cell => cell.Name).ToList();
    }

    private static List<string> TopsOf(GDS gds)
    {
        return Hierarchy.Summarize(gds).Where(cell => cell.IsTop).Select(cell => cell.Name).ToList();
    }

    ///<summary>The layer of each shape the library draws, sorted.</summary>
    private static List<string> LayersDrawn(GDS gds)
    {
        return GdsFlattener.Flatten(gds).Elements.Select(element => element.Layer.Key.ToString()).Order(StringComparer.Ordinal).ToList();
    }

    ///
    ///A reading of a library put the way <see cref="OasisTestData.CellsAndShapes"/> puts KLayout's: each cell, each top,
    ///and how many shapes the flattener draws on each layer - sorted, so the two compare line for line.
    ///
    private static List<string> CellsAndShapes(GDS gds)
    {
        var lines = new List<string>();

        foreach (var cell in Hierarchy.Summarize(gds))
        {
            lines.Add($"cell {cell.Name}");

            if (cell.IsTop)
                lines.Add($"top {cell.Name}");
        }

        foreach (var layer in GdsFlattener.Flatten(gds).Elements.GroupBy(element => element.Layer.Key))
            lines.Add($"layer {layer.Key} {layer.Count()}");

        lines.Sort(StringComparer.Ordinal);

        return lines;
    }

    #endregion ****************************************************************************



    #region Read the way KLayout reads it *************************************************

    [Fact]
    public void Klayouts_context_is_held_aside_rather_than_read_as_a_cell()
    {
        var gds = GDS.FromText(AsWritten);

        Assert.Equal(GDS.StreamFormatModel.ContextName, Hierarchy.NameOf(gds.StreamFormat.Context!));
        Assert.Equal(new[] { "via_stack$1", "TOP" }, gds.StreamFormat.Structures.Select(Hierarchy.NameOf));

        //Nothing counts its SREF as a placement, so the cell it names is placed once, by the layout, and the layout is
        //the one top.
        var cells = Hierarchy.Summarize(gds);

        Assert.Equal(new[] { "via_stack$1", "TOP" }, CellsOf(gds));
        Assert.Equal(1, cells[0].PlacedBy);
        Assert.Equal(new[] { "TOP" }, TopsOf(gds));
    }

    ///
    ///**The fault itself.** The context was a second top cell, so the via stack was drawn where the layout puts it and
    ///again at the origin - and the boundary on 0/0 beside it, as a shape of no area.
    ///
    [Fact]
    public void What_it_names_is_drawn_once_where_the_layout_places_it()
    {
        var element = Assert.Single(GdsFlattener.Flatten(GDS.FromText(AsWritten)).Elements);

        Assert.Equal("6/0", element.Layer.Key.ToString());
        Assert.Equal(5000, element.Points.Min(point => point.X));
        Assert.Equal(2000, element.Points.Min(point => point.Y));
    }

    ///<summary>And the boundary no longer puts a layer 0/0 in the layer list with that shape of no area on it.</summary>
    [Fact]
    public void Its_boundary_puts_no_layer_in_the_list()
    {
        var gds = GDS.FromText(AsWritten);

        Assert.Equal(new[] { "6/0" }, gds.AdditionalInformation.Layers.Keys.Select(key => key.ToString()));
    }

    ///
    ///**Only first.** KLayout's reader asks for the context as the first structure of the library, and reads one by that
    ///name anywhere else as a cell like any other - a top, drawing what it names at the origin and its boundary as a
    ///shape on 0/0. So does this, since what KLayout draws of a file is what this is held to.
    ///
    [Fact]
    public void Anywhere_but_first_it_is_a_cell_like_any_other()
    {
        var gds = GDS.FromText(ContextSecond);

        Assert.Null(gds.StreamFormat.Context);
        Assert.Equal(new[] { "$$$CONTEXT_INFO$$$", "TOP" }, TopsOf(gds));
        Assert.Equal(new[] { "0/0", "6/0", "6/0" }, LayersDrawn(gds));
    }

    ///<summary>A cell only the context names is a top of its own, as it is to KLayout: nothing places it.</summary>
    [Fact]
    public void A_cell_only_the_context_names_is_a_top_of_its_own()
    {
        var gds = GDS.FromText(NothingElsePlaces);

        Assert.Equal(new[] { "via_stack$1", "TOP" }, TopsOf(gds));
        Assert.Equal(new[] { "2/0", "6/0" }, LayersDrawn(gds));
    }

    ///
    ///**Imported, the layout is what gets placed.** An import places the incoming library's first top, and the context -
    ///first in the file, placed by nothing - was it: importing a file KLayout wrote put down the cells made from PCells at
    ///one point and none of the layout. Now the incoming cells come across without it, and the top is the layout.
    ///
    [Fact]
    public void Imported_the_layout_is_what_gets_placed()
    {
        var host = GDS.FromText(Head + TopAlone.Replace("STRNAME: TOP", "STRNAME: HOST") + "ENDLIB:\n");
        var plan = Importing.PlanFor(host, GDS.FromText(AsWritten))!;

        foreach (var edit in plan.Edits)
            edit.Apply();

        Assert.Equal("TOP", plan.TopCell);
        Assert.Equal(new[] { "HOST", "via_stack$1", "TOP" }, Hierarchy.Names(host));
    }

    ///<summary>The paced read the app opens a file with, a run of elements at a time, holds it aside the same.</summary>
    [Fact]
    public async Task Read_a_run_at_a_time_it_is_held_aside_the_same()
    {
        byte[] bytes = GDS.FromText(AsWritten).Serialize();

        var gds = await GDS.FromBytesAsync(bytes, () => ValueTask.CompletedTask);

        Assert.Equal(GDS.StreamFormatModel.ContextName, Hierarchy.NameOf(gds.StreamFormat.Context!));
        Assert.Equal(new[] { "via_stack$1", "TOP" }, gds.StreamFormat.Structures.Select(Hierarchy.NameOf));
    }

    #endregion ****************************************************************************



    #region Kept for KLayout ****************************************************************

    ///
    ///**Held aside rather than dropped**: its records are still the file's, so a save writes it back where it was - first,
    ///byte for byte - and KLayout reading what was saved here still knows which cell came from which PCell.
    ///
    [Fact]
    public void Saved_it_goes_back_first_as_it_came()
    {
        byte[] bytes = GDS.FromText(AsWritten).Serialize();
        var gds = new GDS(bytes);

        Assert.Equal(bytes, gds.Serialize());
        Assert.Same(gds.StreamFormat.Context!.STRNAME, gds.Records.First(record => record.Type == RecordType.STRNAME));
    }

    ///
    ///A cell renamed here keeps what KLayout knows of it: the context's SREF names the cell it describes, and one left on
    ///the old name would have KLayout read the renamed cell back as plain shapes. Undone, both go back.
    ///
    [Fact]
    public void Renaming_a_cell_renames_it_in_the_context_too()
    {
        var gds = GDS.FromText(AsWritten);
        var rename = new RenameStructure(gds, "via_stack$1", "contact");
        var described = gds.StreamFormat.Context!.Elements[1];
        var placed = Hierarchy.Named(gds, "TOP")!.Elements[0];

        rename.Apply();

        Assert.Equal("contact", Hierarchy.PlacedBy(described));
        Assert.Equal("contact", Hierarchy.PlacedBy(placed));

        rename.Revert();

        Assert.Equal("via_stack$1", Hierarchy.PlacedBy(described));
        Assert.Equal("via_stack$1", Hierarchy.PlacedBy(placed));
    }

    ///
    ///**Written as OASIS or DXF, it is left out.** Neither has a place for it - KLayout keeps the same thing in OASIS as
    ///properties of its own - and written as a cell it would be one more top to every reader of the result, KLayout
    ///included. The cell it names goes as the cell it is.
    ///
    [Fact]
    public void Written_as_oasis_or_dxf_it_is_left_out()
    {
        var gds = GDS.FromText(AsWritten);
        var oasis = OasisReader.Read(OasisWriter.Write(gds));
        var dxf = DxfReader.Read(DxfWriter.Write(gds));

        Assert.Equal(new[] { "TOP", "via_stack$1" }, CellsOf(oasis).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "6/0" }, LayersDrawn(oasis));
        Assert.Equal(new[] { "6/0" }, LayersDrawn(dxf));
    }

    #endregion ****************************************************************************



    #region The command line ****************************************************************

    private readonly StringWriter output = new StringWriter();
    private readonly StringWriter error = new StringWriter();
    private readonly List<string> temporaryFiles = new List<string>();

    ///<summary>A path in the system's temporary directory, removed when the test finishes.</summary>
    private string TemporaryPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gdscontext-{Guid.NewGuid():N}.gds");

        temporaryFiles.Add(path);

        return path;
    }

    ///<summary>A library written out to a file of its own.</summary>
    private string Written(string library)
    {
        string path = TemporaryPath();

        File.WriteAllBytes(path, GDS.FromText(library).Serialize());

        return path;
    }

    public void Dispose()
    {
        foreach (string path in temporaryFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }

    ///<summary>`gds info` lists the cells, and says on a line of its own what the one more structure in the file is.</summary>
    [Fact]
    public void Info_says_what_the_context_is_beside_the_cells()
    {
        Assert.Equal(Cli.Ok, Cli.Run(new[] { "info", Written(AsWritten) }, output, error));

        string said = output.ToString();

        Assert.Contains("structures  2", said);
        Assert.Contains("context     $$$CONTEXT_INFO$$$, KLayout's record of what 1 cell(s) were made from - not a cell, and not drawn", said);
        Assert.Contains("drawn       1 shape(s)", said);
    }

    [Fact]
    public void Cells_say_which_they_leave_out()
    {
        Assert.Equal(Cli.Ok, Cli.Run(new[] { "cells", Written(AsWritten) }, output, error));

        string said = output.ToString();

        Assert.Contains("2 cell(s), 1 of them placed by nothing.", said);
        Assert.Contains("$$$CONTEXT_INFO$$$ is not among them: it is KLayout's record of what 1 cell(s) were made from, and draws nothing.", said);
    }

    ///<summary>Converted to GDSII it goes out with the cells, first, and is said with them - as validating the file says it.</summary>
    [Fact]
    public void Converted_to_gdsii_it_goes_out_with_the_cells()
    {
        string source = Written(AsWritten);
        string converted = TemporaryPath();

        Assert.Equal(Cli.Ok, Cli.Run(new[] { "convert", source, "-o", converted }, output, error));
        Assert.Equal(Cli.Ok, Cli.Run(new[] { "validate", source }, output, error));

        Assert.Contains("GDSII, 2 structure(s) and KLayout's context.", output.ToString());
        Assert.Contains("records, 2 structure(s) and KLayout's context)", output.ToString());
        Assert.Equal(GDS.StreamFormatModel.ContextName, Hierarchy.NameOf(new GDS(File.ReadAllBytes(converted)).StreamFormat.Context!));
    }

    #endregion ****************************************************************************



    #region Against KLayout *****************************************************************

    ///
    ///KLayout reading each library, against this reading it: the same cells, the same tops, the same shapes on the same
    ///layers. KLayout knows no PCell called via_stack, so it reads the cell the context names as the file draws it - which
    ///is how it reads IHP's designs too, without IHP's PDK.
    ///
    [Theory]
    [Trait("Needs", "KLayout")]
    [InlineData("as-written")]
    [InlineData("context-second")]
    [InlineData("nothing-else-places")]
    public void Klayout_reads_each_library_as_this_does(string name)
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        byte[] bytes = GDS.FromText(Libraries[name]).Serialize();

        Assert.Equal(OasisTestData.CellsAndShapes(bytes, $"context-{name}"), CellsAndShapes(new GDS(bytes)));
    }

    ///
    ///**A library KLayout wrote itself**, a circle from its Basic library in it: the context is the one KLayout writes,
    ///boundary and all, and KLayout makes the circle from the PCell again as it reads the file. Read here, the same cells,
    ///tops and shapes. Then with the context moved off the front, where KLayout makes a cell of it, draws the circle a
    ///second time at the origin and the boundary as a shape - and so does this.
    ///
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_a_library_it_wrote_with_a_pcell_as_this_does()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed to write the library here.");

        byte[] written = OasisTestData.WrittenWithAPcell();
        var gds = new GDS(written);

        //Not passing for want of a context to hold aside: KLayout wrote one, the boundary and the circle's SREF.
        Assert.Equal(new[] { "BoundaryModel", "SrefModel" }, gds.StreamFormat.Context!.Elements.Select(element => element.Element.GetType().Name));
        Assert.Equal(OasisTestData.CellsAndShapes(written, "context-klayout"), CellsAndShapes(gds));

        byte[] moved = ContextMovedBack(gds);

        Assert.Null(new GDS(moved).StreamFormat.Context);
        Assert.Equal(OasisTestData.CellsAndShapes(moved, "context-klayout-moved"), CellsAndShapes(new GDS(moved)));
    }

    ///<summary>The library with its context moved from the front to after the cell that followed it.</summary>
    private static byte[] ContextMovedBack(GDS gds)
    {
        var records = gds.Records;
        int from = records.IndexOf(gds.StreamFormat.Context!.BGNSTR);
        int to = records.IndexOf(gds.StreamFormat.Context!.ENDSTR);
        int after = records.IndexOf(gds.StreamFormat.Structures[0].ENDSTR);

        var moved = new List<GDS.Record>();

        moved.AddRange(records.Take(from));
        moved.AddRange(records.Skip(to + 1).Take(after - to));
        moved.AddRange(records.Skip(from).Take(to - from + 1));
        moved.AddRange(records.Skip(after + 1));

        return GDS.FromRecords(moved).Serialize();
    }

    #endregion ****************************************************************************
}
