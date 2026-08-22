using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers PathOutline, which converts a PATH's centerline and width into the shape it occupies.
///
///The corpus only exercises PATHTYPE 0 - all 898 of its paths - so the round and extended end caps and
///the BGNEXTN/ENDEXTN extensions are covered here by hand and nowhere else.
///</summary>
public class PathOutlineTests
{
    private static List<Element.Point> Line(params int[] coordinates)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < coordinates.Length; i += 2)
            points.Add(new Element.Point(coordinates[i], coordinates[i + 1]));

        return points;
    }

    private static (int X, int Y)[] Outline(List<Element.Point> centerline, int width, int pathType = 0, int begin = 0, int end = 0)
    {
        return PathOutline.Build(centerline, width, pathType, begin, end)
            .Select(point => (point.X, point.Y))
            .ToArray();
    }

    ///<summary>The signed area of a closed polygon, used to check that it encloses anything at all.</summary>
    private static double SignedArea((int X, int Y)[] points)
    {
        double sum = 0;

        for (int i = 0; i < points.Length; i++)
        {
            var current = points[i];
            var next = points[(i + 1) % points.Length];

            sum += (current.X * (double)next.Y) - (next.X * (double)current.Y);
        }

        return sum / 2;
    }

    #region A single segment ************************************************************

    [Fact]
    public void A_horizontal_segment_becomes_a_rectangle_of_its_width()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100);

        //Up one side and back the other: left edge at y = +50, right edge at y = -50.
        Assert.Equal(new[] { (0, 50), (1000, 50), (1000, -50), (0, -50) }, outline);
    }

    [Fact]
    public void A_vertical_segment_becomes_a_rectangle_of_its_width()
    {
        var outline = Outline(Line(0, 0, 0, 1000), width: 200);

        Assert.Equal(new[] { (-100, 0), (-100, 1000), (100, 1000), (100, 0) }, outline);
    }

    ///<summary>
    ///An odd width cannot be split symmetrically on an integer grid, so it comes out a unit narrow rather
    ///than lopsided. Academic in practice - every width in the sample files is even.
    ///</summary>
    [Fact]
    public void An_odd_width_stays_symmetric_about_the_centerline()
    {
        var outline = Outline(Line(0, 0, 10, 0), width: 101);

        Assert.Equal(new[] { (0, 50), (10, 50), (10, -50), (0, -50) }, outline);
    }

    [Fact]
    public void The_outline_encloses_the_expected_area()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100);

        Assert.Equal(1000 * 100, Math.Abs(SignedArea(outline)));
    }

    #endregion ************************************************************************



    #region Corners ********************************************************************

    ///<summary>
    ///A right angle mitres to a clean corner: the outer side reaches the corner of the square, the inner
    ///side cuts across it. Getting this wrong shows up as a notch at every bend.
    ///</summary>
    [Fact]
    public void A_right_angle_is_mitered()
    {
        var outline = Outline(Line(0, 0, 1000, 0, 1000, 1000), width: 100);

        Assert.Equal(
            new[] { (0, 50), (950, 50), (950, 1000), (1050, 1000), (1050, -50), (0, -50) },
            outline);
    }

    [Fact]
    public void A_straight_run_of_three_points_collapses_to_a_rectangle()
    {
        //The middle point is collinear, so its two offset edges already coincide and it adds no corner.
        var outline = Outline(Line(0, 0, 500, 0, 1000, 0), width: 100);

        Assert.Equal(new[] { (0, 50), (1000, 50), (1000, -50), (0, -50) }, outline);
    }

    [Fact]
    public void A_repeated_point_is_ignored_rather_than_breaking_the_outline()
    {
        var outline = Outline(Line(0, 0, 1000, 0, 1000, 0), width: 100);

        Assert.Equal(new[] { (0, 50), (1000, 50), (1000, -50), (0, -50) }, outline);
    }

    ///<summary>
    ///A path that nearly doubles back would mitre to an arbitrarily long spike, so past the limit the
    ///corner is cut off square instead. The give-away is the outline staying near the path.
    ///</summary>
    [Fact]
    public void A_very_sharp_corner_is_beveled_instead_of_spiking()
    {
        var outline = Outline(Line(0, 0, 1000, 0, 0, 20), width: 100);

        double reach = outline.Max(point => Math.Abs(point.X));

        Assert.True(reach < 1000 + (4 * 50) + 1, $"a spike escaped the miter limit, reaching x = {reach}");
    }

    #endregion ************************************************************************



    #region End caps *******************************************************************

    [Fact]
    public void Path_type_zero_ends_flush_with_its_endpoint()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100, pathType: 0);

        Assert.Equal(0, outline.Min(point => point.X));
        Assert.Equal(1000, outline.Max(point => point.X));
    }

    [Fact]
    public void Path_type_two_extends_half_a_width_past_each_end()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100, pathType: 2);

        Assert.Equal(new[] { (-50, 50), (1050, 50), (1050, -50), (-50, -50) }, outline);
    }

    [Fact]
    public void Path_type_four_extends_by_the_amounts_it_is_given()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100, pathType: 4, begin: 30, end: 70);

        Assert.Equal(new[] { (-30, 50), (1070, 50), (1070, -50), (-30, -50) }, outline);
    }

    [Fact]
    public void Path_type_one_adds_a_semicircle_at_each_end()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100, pathType: 1);

        //Four corners plus the points inside each of the two arcs.
        Assert.Equal(4 + (2 * 7), outline.Length);

        //The caps bulge exactly half a width beyond the endpoints.
        Assert.Equal(-50, outline.Min(point => point.X));
        Assert.Equal(1050, outline.Max(point => point.X));

        //And stay within the width, so the cap is a semicircle and not a box.
        Assert.All(outline, point => Assert.True(Math.Abs(point.Y) <= 50, $"y = {point.Y} escaped the width"));
    }

    [Fact]
    public void A_round_cap_bulges_along_the_path_not_across_it()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 100, pathType: 1);

        //The extreme point of the end cap sits on the centerline, half a width past the endpoint.
        Assert.Contains((1050, 0), outline);
        Assert.Contains((-50, 0), outline);
    }

    #endregion ************************************************************************



    #region Degenerate input ***********************************************************

    ///<summary>
    ///A zero width has no outline. Returning the centerline keeps such a path visible as the line the 2D
    ///view strokes, rather than making it disappear.
    ///</summary>
    [Fact]
    public void A_zero_width_path_keeps_its_centerline()
    {
        var outline = Outline(Line(0, 0, 1000, 0), width: 0);

        Assert.Equal(new[] { (0, 0), (1000, 0) }, outline);
    }

    [Fact]
    public void A_single_point_path_keeps_its_point()
    {
        var outline = Outline(Line(500, 500), width: 100);

        Assert.Equal(new[] { (500, 500) }, outline);
    }

    [Fact]
    public void A_path_of_repeated_points_keeps_its_input()
    {
        var outline = Outline(Line(500, 500, 500, 500), width: 100);

        Assert.Equal(new[] { (500, 500), (500, 500) }, outline);
    }

    #endregion ************************************************************************



    #region Through the flattener ******************************************************

    private static byte[] PathLibrary(short layer, int width, int[] xy, short? pathType = null)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("WIRE")),
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
        };

        if (pathType.HasValue)
            records.Add(GdsTestData.Record(RecordType.PATHTYPE, GdsTestData.Int2(pathType.Value)));

        records.Add(GdsTestData.Record(RecordType.WIDTH, GdsTestData.Int4(width)));
        records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)));
        records.Add(GdsTestData.Record(RecordType.ENDEL));
        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return GdsTestData.Concat(records.ToArray());
    }

    [Fact]
    public void A_path_reaches_the_renderers_as_an_outline()
    {
        var layout = GdsFlattener.Flatten(new GDS(PathLibrary(1, 100, new[] { 0, 0, 1000, 0 })));

        var element = Assert.Single(layout.Elements);

        Assert.Equal(
            new[] { (0, 50), (1000, 50), (1000, -50), (0, -50) },
            element.Points.Select(point => (point.X, point.Y)).ToArray());
    }

    [Fact]
    public void The_width_record_is_read_as_a_single_int4()
    {
        var gds = new GDS(PathLibrary(1, 480, new[] { 0, 0, 1000, 0 }));
        var path = (GDS.PathModel)gds.StreamFormat.Structures[0].Elements[0].Element;

        Assert.Equal(480, ((Int4Data)path.WIDTH!.Data!).Value);
    }

    ///<summary>
    ///Widening happens before placement, so a magnified reference scales the wire's width as well as its
    ///length. Doing it the other way round would leave a magnified path the wrong thickness.
    ///</summary>
    [Fact]
    public void A_magnified_placement_scales_the_width_too()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("WIRE")),
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.WIDTH, GdsTestData.Int4(100)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 1000, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("WIRE")),
            GdsTestData.Record(RecordType.STRANS, new byte[] { 0x00, 0x00 }),
            GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(2.0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));

        var layout = GdsFlattener.Flatten(new GDS(stream));
        var element = Assert.Single(layout.Elements);

        Assert.Equal(
            new[] { (0, 100), (2000, 100), (2000, -100), (0, -100) },
            element.Points.Select(point => (point.X, point.Y)).ToArray());
    }

    #endregion ************************************************************************



    #region The bundled corpus *********************************************************

    ///<summary>
    ///898 paths across 387 of the sample files, every one of them PATHTYPE 0. Each must now enclose real
    ///area - that is the whole point of the change - and stay a sane size relative to its centerline.
    ///</summary>
    [Fact]
    public void Every_path_in_the_corpus_becomes_a_closed_outline()
    {
        var failures = new List<string>();
        int paths = 0;

        foreach (string file in GdsTestData.AllSampleFiles())
        {
            var gds = new GDS(File.ReadAllBytes(file));

            foreach (var path in gds.StreamFormat.Structures.SelectMany(s => s.Elements).Select(e => e.Element).OfType<GDS.PathModel>())
            {
                paths++;

                int width = ((Int4Data)path.WIDTH!.Data!).Value;
                var centerline = ((Int4Data)path.XY.Data!).Values;

                var outline = PathOutline.Build(
                    Line(centerline),
                    width,
                    0,
                    0,
                    0);

                //Two sides of a run of segments, so at least four corners.
                if (outline.Count < 4)
                    failures.Add($"{Path.GetFileName(file)}: outline has only {outline.Count} points");

                double area = Math.Abs(SignedArea(outline.Select(p => (p.X, p.Y)).ToArray()));

                if (area <= 0)
                    failures.Add($"{Path.GetFileName(file)}: outline encloses no area");
            }
        }

        Assert.True(paths > 0, "the corpus should contain paths");
        Assert.True(failures.Count == 0, $"{failures.Count} of {paths} paths failed:\n{string.Join("\n", failures.Take(10))}");
    }

    [Fact]
    public void A_real_cell_draws_its_paths_with_area()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.Sky130Sample("sky130_fd_sc_hd__a2111oi_0.gds")));

        var layout = GdsFlattener.Flatten(gds);

        //The two 480-wide power rails run the full cell width, so they are the largest shapes present.
        var areas = layout.Elements
            .Where(element => element.Text is null && element.Points.Count >= 4)
            .Select(element => Math.Abs(SignedArea(element.Points.Select(p => (p.X, p.Y)).ToArray())))
            .ToList();

        Assert.NotEmpty(areas);
        Assert.All(areas, area => Assert.True(area > 0, "a shape reached the renderer with no area"));
    }

    #endregion ************************************************************************
}
