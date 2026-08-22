using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers GdsFlattener, which resolves a library's hierarchy into placed geometry.
///
///These carry more weight than most of the suite: only one bundled file uses SREF and none uses AREF,
///and that one file references cells it does not contain, so the corpus cannot exercise placement at
///all. Everything below is built by hand for that reason.
///</summary>
public class FlattenerTests
{
    #region Building libraries **********************************************************

    ///<summary>
    ///A structure holding one square boundary, offset so rotation and reflection are visible.
    ///
    ///Closed - the last point repeats the first - because that is what the format requires of a boundary
    ///and what the parser now checks. It is why every expected point list below ends where it starts.
    ///</summary>
    private static byte[] Cell(string name, short layer = 1, int[]? xy = null)
    {
        xy ??= new[] { 0, 0, 100, 0, 100, 100, 0, 100, 0, 0 };

        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));
    }

    private static byte[] Strans(bool reflect, double? magnification = null, double? angle = null)
    {
        byte flags = 0x00;

        if (reflect)
            flags = 0x80;

        var records = new List<byte[]> { GdsTestData.Record(RecordType.STRANS, new byte[] { flags, 0x00 }) };

        if (magnification.HasValue)
            records.Add(GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(magnification.Value)));

        if (angle.HasValue)
            records.Add(GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(angle.Value)));

        return GdsTestData.Concat(records.ToArray());
    }

    ///<summary>A structure whose only content is one SREF placing another cell.</summary>
    private static byte[] CellWithSref(string name, string target, int x, int y, byte[]? strans = null)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii(target)),
            strans ?? Array.Empty<byte>(),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));
    }

    private static byte[] Library(params byte[][] structures)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Concat(structures),
            GdsTestData.Record(RecordType.ENDLIB));
    }

    private static FlattenedLayout Flatten(byte[] stream)
    {
        return GdsFlattener.Flatten(new GDS(stream));
    }

    private static List<(int X, int Y)> PointsOf(Element element)
    {
        return element.Points.Select(point => (point.X, point.Y)).ToList();
    }

    #endregion *************************************************************************



    #region A flat library *************************************************************

    [Fact]
    public void A_library_with_no_references_is_returned_as_is()
    {
        var layout = Flatten(Library(Cell("ONLY")));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (0, 0), (100, 0), (100, 100), (0, 100), (0, 0) }, PointsOf(element));
        Assert.Empty(layout.UnresolvedReferences);
    }

    [Fact]
    public void Unreferenced_structures_are_all_drawn()
    {
        var layout = Flatten(Library(Cell("A"), Cell("B", layer: 2)));

        Assert.Equal(2, layout.Elements.Count);
    }

    #endregion ************************************************************************



    #region Placement ******************************************************************

    [Fact]
    public void An_sref_places_the_cell_at_its_reference_point()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 1000, 2000)));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (1000, 2000), (1100, 2000), (1100, 2100), (1000, 2100), (1000, 2000) }, PointsOf(element));
    }

    ///<summary>The referenced cell must not also be drawn at the origin in its own right.</summary>
    [Fact]
    public void A_referenced_cell_is_drawn_only_where_it_is_placed()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 1000, 0)));

        Assert.Single(layout.Elements);
        Assert.DoesNotContain(layout.Elements, e => PointsOf(e).Contains((0, 0)));
    }

    [Fact]
    public void A_cell_referenced_twice_is_drawn_twice()
    {
        byte[] top = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(500, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));

        var layout = Flatten(Library(Cell("LEAF"), top));

        Assert.Equal(2, layout.Elements.Count);
        Assert.Contains(layout.Elements, e => PointsOf(e)[0] == (0, 0));
        Assert.Contains(layout.Elements, e => PointsOf(e)[0] == (500, 0));
    }

    #endregion ************************************************************************



    #region Transforms *****************************************************************

    [Fact]
    public void A_reflection_mirrors_the_cell_about_the_x_axis()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 0, 0, Strans(reflect: true))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (0, 0), (100, 0), (100, -100), (0, -100), (0, 0) }, PointsOf(element));
    }

    [Fact]
    public void A_magnification_scales_the_cell()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 0, 0, Strans(false, magnification: 2.0))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (0, 0), (200, 0), (200, 200), (0, 200), (0, 0) }, PointsOf(element));
    }

    [Fact]
    public void A_ninety_degree_rotation_turns_the_cell_counterclockwise()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 0, 0, Strans(false, angle: 90.0))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (0, 0), (0, 100), (-100, 100), (-100, 0), (0, 0) }, PointsOf(element));
    }

    [Fact]
    public void A_one_hundred_and_eighty_degree_rotation_inverts_the_cell()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 0, 0, Strans(false, angle: 180.0))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (0, 0), (-100, 0), (-100, -100), (0, -100), (0, 0) }, PointsOf(element));
    }

    ///<summary>
    ///The format's order is reflect, then magnify, then rotate, then translate. Applying rotation before
    ///reflection would put this square in a different quadrant, so the order is what is being pinned.
    ///</summary>
    [Fact]
    public void Reflection_happens_before_rotation()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 0, 0, Strans(true, angle: 90.0))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (0, 0), (0, 100), (100, 100), (100, 0), (0, 0) }, PointsOf(element));
    }

    [Fact]
    public void A_transform_and_a_reference_point_combine()
    {
        var layout = Flatten(Library(Cell("LEAF"), CellWithSref("TOP", "LEAF", 50, 50, Strans(false, magnification: 3.0))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(new[] { (50, 50), (350, 50), (350, 350), (50, 350), (50, 50) }, PointsOf(element));
    }

    #endregion ************************************************************************



    #region Nesting ********************************************************************

    [Fact]
    public void Nested_references_compose_their_translations()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithSref("MIDDLE", "LEAF", 100, 0),
            CellWithSref("TOP", "MIDDLE", 1000, 0)));

        var element = Assert.Single(layout.Elements);

        Assert.Equal((1100, 0), PointsOf(element)[0]);
    }

    ///<summary>
    ///The child's own placement happens inside the parent's frame, so the parent's rotation has to turn
    ///the child's offset too. Composing in the wrong order would leave the offset unrotated.
    ///</summary>
    [Fact]
    public void A_parents_rotation_applies_to_a_childs_offset()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithSref("MIDDLE", "LEAF", 100, 0),
            CellWithSref("TOP", "MIDDLE", 0, 0, Strans(false, angle: 90.0))));

        var element = Assert.Single(layout.Elements);

        //The child sat 100 along x; a quarter turn puts it 100 along y.
        Assert.Equal((0, 100), PointsOf(element)[0]);
    }

    ///<summary>Builds a STRANS block from a raw flag word, for the bits the bool overload cannot reach.</summary>
    private static byte[] StransWord(int flags, double? magnification = null, double? angle = null)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.STRANS, new byte[] { (byte)(flags >> 8), (byte)flags })
        };

        if (magnification.HasValue)
            records.Add(GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(magnification.Value)));

        if (angle.HasValue)
            records.Add(GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(angle.Value)));

        return GdsTestData.Concat(records.ToArray());
    }

    ///<summary>
    ///A magnification marked absolute is measured against the world, so the containing structure's own
    ///magnification does not multiply into it. Nested relative 2x inside 3x gives 6x - the test below
    ///this one - whereas absolute 2x inside 3x stays 2x.
    ///</summary>
    [Fact]
    public void An_absolute_magnification_ignores_the_one_it_is_nested_in()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithSref("MIDDLE", "LEAF", 0, 0, StransWord(0x0004, magnification: 2.0)),
            CellWithSref("TOP", "MIDDLE", 0, 0, Strans(false, magnification: 3.0))));

        var element = Assert.Single(layout.Elements);

        //The 100-unit square comes out 200 across, not 600.
        Assert.Equal((200, 0), PointsOf(element)[1]);
    }

    ///<summary>
    ///Likewise an absolute angle: a quarter turn inside a quarter turn stays a quarter turn rather than
    ///becoming a half turn.
    ///</summary>
    [Fact]
    public void An_absolute_angle_ignores_the_one_it_is_nested_in()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithSref("MIDDLE", "LEAF", 0, 0, StransWord(0x0002, angle: 90.0)),
            CellWithSref("TOP", "MIDDLE", 0, 0, Strans(false, angle: 90.0))));

        var element = Assert.Single(layout.Elements);

        //A single quarter turn takes (100, 0) to (0, 100); a half turn would give (-100, 0).
        Assert.Equal((0, 100), PointsOf(element)[1]);
    }

    [Fact]
    public void Nested_magnifications_multiply()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithSref("MIDDLE", "LEAF", 0, 0, Strans(false, magnification: 2.0)),
            CellWithSref("TOP", "MIDDLE", 0, 0, Strans(false, magnification: 3.0))));

        var element = Assert.Single(layout.Elements);

        Assert.Equal((600, 0), PointsOf(element)[1]);
    }

    #endregion ************************************************************************



    #region Arrays *********************************************************************

    private static byte[] CellWithAref(string name, string target, short columns, short rows, int[] xy)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.AREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii(target)),
            GdsTestData.Record(RecordType.COLROW, GdsTestData.Int2(columns, rows)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));
    }

    [Fact]
    public void An_aref_places_one_instance_per_grid_position()
    {
        //3 columns spanning 3000 and 2 rows spanning 2000, so a step of 1000 each way.
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithAref("TOP", "LEAF", 3, 2, new[] { 0, 0, 3000, 0, 0, 2000 })));

        Assert.Equal(6, layout.Elements.Count);

        var origins = layout.Elements.Select(e => PointsOf(e)[0]).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();

        Assert.Equal(
            new[] { (0, 0), (1000, 0), (2000, 0), (0, 1000), (1000, 1000), (2000, 1000) },
            origins);
    }

    [Fact]
    public void A_single_element_aref_places_one_instance_at_the_origin()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithAref("TOP", "LEAF", 1, 1, new[] { 700, 800, 1700, 800, 700, 1800 })));

        var element = Assert.Single(layout.Elements);

        Assert.Equal((700, 800), PointsOf(element)[0]);
    }

    [Fact]
    public void An_aref_with_no_columns_or_rows_places_nothing()
    {
        var layout = Flatten(Library(
            Cell("LEAF"),
            CellWithAref("TOP", "LEAF", 0, 0, new[] { 0, 0, 100, 0, 0, 100 })));

        Assert.Empty(layout.Elements);
    }

    #endregion ************************************************************************



    #region Text ***********************************************************************

    private static byte[] CellWithText(string name, short layer, string label, int x, int y)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii(label)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));
    }

    [Fact]
    public void A_text_element_keeps_its_label_and_anchor()
    {
        var layout = Flatten(Library(CellWithText("CELL", 67, "VPWR", 250, 400)));

        var element = Assert.Single(layout.Elements);

        Assert.Equal("VPWR", element.Text);
        Assert.Equal((250, 400), PointsOf(element)[0]);
        Assert.Equal((short)67, element.Layer.Number);
    }

    [Fact]
    public void Geometry_carries_no_label()
    {
        var layout = Flatten(Library(Cell("CELL")));

        Assert.Null(Assert.Single(layout.Elements).Text);
    }

    [Fact]
    public void A_text_element_moves_with_the_cell_that_contains_it()
    {
        var layout = Flatten(Library(
            CellWithText("LEAF", 67, "A", 10, 20),
            CellWithSref("TOP", "LEAF", 1000, 1000)));

        var element = Assert.Single(layout.Elements);

        Assert.Equal("A", element.Text);
        Assert.Equal((1010, 1020), PointsOf(element)[0]);
    }

    #endregion ************************************************************************



    #region Broken hierarchies *********************************************************

    ///<summary>
    ///The normal case for this repo's sample files: a standalone cell references the rest of its library
    ///without containing it. That has to degrade to "draw what is here" rather than fail.
    ///</summary>
    [Fact]
    public void A_reference_to_a_missing_cell_is_recorded_and_skipped()
    {
        var layout = Flatten(Library(CellWithSref("TOP", "SOMEWHERE_ELSE", 0, 0)));

        Assert.Empty(layout.Elements);
        Assert.Equal(new[] { "SOMEWHERE_ELSE" }, layout.UnresolvedReferences.ToArray());
    }

    [Fact]
    public void Geometry_beside_a_missing_reference_is_still_drawn()
    {
        byte[] top = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("MISSING")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 10, 0, 10, 10, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR));

        var layout = Flatten(Library(top));

        Assert.Single(layout.Elements);
        Assert.Single(layout.UnresolvedReferences);
    }

    [Fact]
    public void A_structure_that_references_itself_does_not_recurse_forever()
    {
        var layout = Flatten(Library(GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LOOP")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 10, 0, 10, 10, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LOOP")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(10, 10)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR))));

        Assert.True(layout.DepthLimitReached);
        Assert.NotEmpty(layout.Elements);
    }

    [Fact]
    public void Two_structures_that_reference_each_other_do_not_recurse_forever()
    {
        var layout = Flatten(Library(
            CellWithSref("A", "B", 10, 0),
            CellWithSref("B", "A", 10, 0)));

        Assert.True(layout.DepthLimitReached);
    }

    #endregion ************************************************************************



    #region The bundled corpus *********************************************************

    [Fact]
    public void Every_bundled_sample_file_flattens()
    {
        var failures = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            try
            {
                GdsFlattener.Flatten(new GDS(File.ReadAllBytes(path)));
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    ///<summary>
    ///Every sample file carries pin labels, which nothing drew before. This is the change users actually
    ///see, so it is worth asserting on real data rather than only on hand-built input.
    ///</summary>
    [Fact]
    public void A_real_cell_yields_its_pin_labels()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.Sky130Sample("sky130_fd_sc_hd__nand2_1.gds")));

        var labels = GdsFlattener.Flatten(gds).Elements
            .Where(element => element.Text is not null)
            .Select(element => element.Text!)
            .Distinct()
            .OrderBy(text => text)
            .ToArray();

        //The two inputs, the output, the four supply and well pins, and the cell-name label.
        Assert.Equal(new[] { "A", "B", "nand2_1", "VGND", "VNB", "VPB", "VPWR", "Y" }, labels);
    }

    ///<summary>The one bundled file with references points at cells it does not contain.</summary>
    [Fact]
    public void The_sample_that_uses_references_reports_them_as_unresolved()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.Sky130Sample("sky130_fd_sc_hd__macro_sparecell.gds")));

        var layout = GdsFlattener.Flatten(gds);

        Assert.NotEmpty(layout.UnresolvedReferences);
        Assert.All(layout.UnresolvedReferences, name => Assert.StartsWith("sky130_fd_sc_hd__", name));
    }

    #endregion ************************************************************************



    #region One cell by name **********************************************************

    ///
    ///Flatten(gds, name) answers a different question from Flatten(gds).
    ///
    ///The one without a name draws what the *file* draws - the structures nothing references. The one with
    ///a name draws the cell asked for, which is what a list of cells is pointing at: a leaf placed four
    ///hundred times is one drawing, at its own coordinates, not four hundred.
    ///
    [Fact]
    public void A_named_cell_is_drawn_on_its_own()
    {
        var gds = new GDS(Library(CellWithSref("TOP", "LEAF", 500, 700), Cell("LEAF")));

        var layout = GdsFlattener.Flatten(gds, "LEAF");

        var element = Assert.Single(layout.Elements);

        //Where the leaf's own coordinates put it, not where TOP places it.
        Assert.Equal(new[] { (0, 0), (100, 0), (100, 100), (0, 100), (0, 0) }, PointsOf(element));
    }

    ///<summary>And asking for the top gives what the file itself would draw.</summary>
    [Fact]
    public void The_top_by_name_is_what_the_file_draws()
    {
        var gds = new GDS(Library(CellWithSref("TOP", "LEAF", 500, 700), Cell("LEAF")));

        var named = GdsFlattener.Flatten(gds, "TOP");
        var whole = GdsFlattener.Flatten(new GDS(Library(CellWithSref("TOP", "LEAF", 500, 700), Cell("LEAF"))));

        Assert.Equal(PointsOf(Assert.Single(whole.Elements)), PointsOf(Assert.Single(named.Elements)));

        //Which is to say the placement was followed: the square is where TOP put it.
        Assert.Equal(new[] { (500, 700), (600, 700), (600, 800), (500, 800), (500, 700) }, PointsOf(Assert.Single(named.Elements)));
    }

    ///<summary>Whatever the named cell places is expanded under it, exactly as anywhere else.</summary>
    [Fact]
    public void A_named_cell_expands_what_it_places()
    {
        var gds = new GDS(Library(
            CellWithSref("TOP", "MIDDLE", 1000, 0),
            CellWithSref("MIDDLE", "LEAF", 40, 60),
            Cell("LEAF")));

        var layout = GdsFlattener.Flatten(gds, "MIDDLE");

        //MIDDLE's own placement of the leaf, without TOP's offset on top of it.
        Assert.Equal(new[] { (40, 60), (140, 60), (140, 160), (40, 160), (40, 60) }, PointsOf(Assert.Single(layout.Elements)));
    }

    ///
    ///A name the library does not hold draws nothing.
    ///
    ///Rather than falling back on the file's own top, which would put a picture of the wrong cell beside a
    ///row and say nothing about it being wrong.
    ///
    [Fact]
    public void A_name_that_is_not_there_draws_nothing()
    {
        var gds = new GDS(Library(Cell("ONLY")));

        Assert.Empty(GdsFlattener.Flatten(gds, "MISSING").Elements);
    }

    #endregion ************************************************************************
}
