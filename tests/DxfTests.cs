using GdsII;

using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Reading DXF as a GDSII library.
///
///**The round trip is the test, as everywhere else here.** A converted library assembled wrongly reads back
///perfectly from the model that made it and is a file another tool refuses - so almost everything below goes
///out through <c>Serialize</c> and comes back through the parser before a single assertion, and the ones that
///matter go one step further and ask the *flattener* what got drawn.
///
///The fixtures are written inline because DXF is text: a file that isolates one entity is four lines, and a
///binary fixture per case would be four hundred unreadable bytes each.
///
public class DxfTests
{
    #region Files to read ***********************************************************

    ///<summary>Wraps a run of pairs in the sections a DXF needs to be one.</summary>
    private static string Drawing(string entities, string header = "", string tables = "", string blocks = "")
    {
        return string.Join("\n", new[]
        {
            "999", "written by hand",
            "0", "SECTION", "2", "HEADER", header, "0", "ENDSEC",
            "0", "SECTION", "2", "TABLES", tables, "0", "ENDSEC",
            "0", "SECTION", "2", "BLOCKS", blocks, "0", "ENDSEC",
            "0", "SECTION", "2", "ENTITIES", entities, "0", "ENDSEC",
            "0", "EOF", ""
        }.Where(line => line.Length > 0));
    }

    private static string Pairs(params object[] codesAndValues)
    {
        var lines = new List<string>();

        for (int i = 0; i + 1 < codesAndValues.Length; i += 2)
        {
            lines.Add(Convert.ToString(codesAndValues[i], System.Globalization.CultureInfo.InvariantCulture)!);
            lines.Add(Convert.ToString(codesAndValues[i + 1], System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return string.Join("\n", lines);
    }

    ///<summary>A square, as the entity every other case is built beside.</summary>
    private static string ASquare(string layer = "METAL", double size = 10)
    {
        return Pairs(
            0, "LWPOLYLINE", 8, layer, 90, 4, 70, 1,
            10, 0, 20, 0,
            10, size, 20, 0,
            10, size, 20, size,
            10, 0, 20, size);
    }

    ///<summary>The one placement in a drawing, which is what the block cases all reach for.</summary>
    private static GDS.ElementModel Placement(string text)
    {
        return Reopened(text).StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.SrefModel or GDS.ArefModel);
    }

    ///<summary>Reads a drawing and hands back the library as it comes off a round trip through the bytes.</summary>
    private static GDS Reopened(string text)
    {
        return new GDS(DxfReader.Read(text).Serialize());
    }

    private static FlattenedLayout DrawnFrom(string text)
    {
        return GdsFlattener.Flatten(Reopened(text));
    }

    #endregion **********************************************************************



    #region Telling one apart *******************************************************

    [Fact]
    public void A_drawing_is_recognized_by_what_it_starts_with()
    {
        Assert.True(DxfReader.LooksLikeDxf(System.Text.Encoding.ASCII.GetBytes("0\nSECTION\n2\nHEADER\n")));
    }

    ///<summary>Most exporters write a comment first, and AutoCAD's own does not - both have to be read.</summary>
    [Fact]
    public void A_leading_comment_does_not_hide_it()
    {
        Assert.True(DxfReader.LooksLikeDxf(System.Text.Encoding.ASCII.GetBytes("999\nQCAD\n0\nSECTION\n2\nHEADER\n")));
    }

    [Fact]
    public void Something_that_is_not_one_is_not_taken_for_one()
    {
        Assert.False(DxfReader.LooksLikeDxf(System.Text.Encoding.ASCII.GetBytes("HEADER\0\x06\x00\x02")));
        Assert.False(DxfReader.LooksLikeDxf(System.Text.Encoding.ASCII.GetBytes("%SEMI-OASIS\r\n")));
        Assert.False(DxfReader.LooksLikeDxf(Array.Empty<byte>()));
    }

    ///<summary>The binary flavor is a different reading, and this one does not claim it - see DxfBinaryTests.</summary>
    [Fact]
    public void A_binary_drawing_is_not_taken_for_the_text_one()
    {
        Assert.False(DxfReader.LooksLikeDxf(System.Text.Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\x1a\0")));
    }

    #endregion **********************************************************************



    #region The scale ***************************************************************

    ///
    ///**A drawing that says nothing is taken as microns.**
    ///
    ///Something has to be assumed, because a number with no unit is not a length - and this is the
    ///assumption that makes a layout-sized drawing come out layout-sized. A ten-unit square is ten microns,
    ///which at a nanometer database unit is ten thousand of them.
    ///
    [Fact]
    public void With_no_units_a_drawing_unit_is_a_micron()
    {
        var square = DrawnFrom(Drawing(ASquare())).Elements.Single();

        var box = Bounds.Of(square.Points);

        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
    }

    ///<summary>And a drawing that says millimeters is read as millimeters.</summary>
    [Fact]
    public void The_header_decides_the_scale()
    {
        var square = DrawnFrom(Drawing(ASquare(), header: Pairs(9, "$INSUNITS", 70, 4))).Elements.Single();

        //Ten millimeters is ten thousand microns, which is ten million database units.
        Assert.Equal(10000000, Bounds.Of(square.Points).Width);
    }

    [Theory]
    [InlineData(13, 1)]
    [InlineData(4, 1000)]
    [InlineData(1, 25400)]
    [InlineData(12, 0.001)]
    [InlineData(0, 1)]
    [InlineData(999, 1)]
    public void Every_unit_the_format_names_has_a_scale(int insUnits, double microns)
    {
        Assert.Equal(microns, DxfReader.MicronsPerUnit(insUnits));
    }

    #endregion **********************************************************************



    #region The entities ************************************************************

    ///<summary>A closed run is a boundary, because an outline is what has area.</summary>
    [Fact]
    public void A_closed_polyline_is_a_boundary()
    {
        var reopened = Reopened(Drawing(ASquare()));

        Assert.Contains(reopened.Records, record => record.Type == RecordType.BOUNDARY);
        Assert.DoesNotContain(reopened.Records, record => record.Type == RecordType.PATH);
    }

    ///
    ///**And an open one is a path.** Calling it a polygon would fill in a shape nobody drew - which is not a
    ///rounding or a detail, it is a different drawing.
    ///
    [Fact]
    public void An_open_polyline_is_a_path()
    {
        string open = Pairs(0, "LWPOLYLINE", 8, "M1", 90, 3, 70, 0, 10, 0, 20, 0, 10, 10, 20, 0, 10, 10, 20, 10);

        var reopened = Reopened(Drawing(open));

        Assert.Contains(reopened.Records, record => record.Type == RecordType.PATH);
        Assert.DoesNotContain(reopened.Records, record => record.Type == RecordType.BOUNDARY);
    }

    ///<summary>A path takes the drawing's own constant width, which is what makes it a wire rather than a line.</summary>
    [Fact]
    public void A_polylines_width_comes_across()
    {
        string wide = Pairs(0, "LWPOLYLINE", 8, "M1", 90, 2, 70, 0, 43, 2, 10, 0, 20, 0, 10, 10, 20, 0);

        var drawn = DrawnFrom(Drawing(wide)).Elements.Single();

        //Two microns across, which is two thousand database units.
        Assert.Equal(2000, Bounds.Of(drawn.Points).Height);
    }

    [Fact]
    public void A_line_is_a_path_between_its_two_ends()
    {
        string line = Pairs(0, "LINE", 8, "M1", 10, 0, 20, 0, 11, 20, 21, 5);

        var reopened = Reopened(Drawing(line));

        var path = reopened.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Select(element => element.Element)
            .OfType<GDS.PathModel>()
            .Single();

        Assert.Equal(new int[] { 0, 0, 20000, 5000 }, ((Int4Data)path.XY!.Data!).Values);
    }

    ///<summary>The old-style polyline, whose points arrive as separate entities after it.</summary>
    [Fact]
    public void A_polyline_collects_the_vertices_that_follow_it()
    {
        string polyline = Pairs(
            0, "POLYLINE", 8, "M1", 70, 1,
            0, "VERTEX", 10, 0, 20, 0,
            0, "VERTEX", 10, 10, 20, 0,
            0, "VERTEX", 10, 10, 20, 10,
            0, "SEQEND");

        var drawn = DrawnFrom(Drawing(polyline)).Elements.Single();

        Assert.Equal(10000, Bounds.Of(drawn.Points).Width);
    }

    ///
    ///**A layout format has no curves**, so a circle is a polygon and always has been. What is checked is
    ///that it comes out round to about the right size rather than that it has an exact point count, which is
    ///a number somebody may reasonably change.
    ///
    [Fact]
    public void A_circle_becomes_a_polygon_of_about_the_right_size()
    {
        string circle = Pairs(0, "CIRCLE", 8, "M1", 10, 0, 20, 0, 40, 5);

        var drawn = DrawnFrom(Drawing(circle)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        //Ten microns across either way, within the flattening error of a sixty-four-sided ring.
        Assert.InRange(box.Width, 9950, 10000);
        Assert.InRange(box.Height, 9950, 10000);
        Assert.True(drawn.Points.Count > 32);
    }

    ///
    ///An arc is open, so it is a path - and every point on it is on the arc.
    ///
    ///**Accuracy rather than a count.** This asserted a point count between fifteen and nineteen, which was
    ///a statement about the sixty-four-sided circle of the day rather than about the arc: it failed the
    ///moment the flattening got better, and would have passed just as happily on an arc of the right number
    ///of points in the wrong places. What matters is that the run is on the circle it came from.
    ///
    [Fact]
    public void An_arc_is_an_open_run_that_lands_on_its_own_circle()
    {
        string arc = Pairs(0, "ARC", 8, "M1", 10, 0, 20, 0, 40, 5, 50, 0, 51, 90);

        var reopened = Reopened(Drawing(arc));

        var path = reopened.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Select(element => element.Element)
            .OfType<GDS.PathModel>()
            .Single();

        var xy = ((Int4Data)path.XY!.Data!).Values;

        Assert.True(xy.Length >= 4, "an arc of one point is not an arc");

        //Five microns of radius about the origin, in nanometers - and a nanometer of slack for the rounding
        //every coordinate goes through on the way in.
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            double away = Math.Sqrt(((double)xy[i] * xy[i]) + ((double)xy[i + 1] * xy[i + 1]));

            Assert.InRange(away, 4999, 5001);
        }

        //A quarter turn, so it starts on the x axis and ends on the y one.
        Assert.Equal(5000, xy[0]);
        Assert.Equal(0, xy[1]);
        Assert.Equal(0, xy[^2]);
        Assert.Equal(5000, xy[^1]);
    }

    ///<summary>An arc whose end angle is below its start has come the long way round, not backwards.</summary>
    [Fact]
    public void An_arc_that_wraps_past_zero_is_read_the_long_way()
    {
        string arc = Pairs(0, "ARC", 8, "M1", 10, 0, 20, 0, 40, 5, 50, 315, 51, 45);

        var drawn = DrawnFrom(Drawing(arc)).Elements.Single();

        //Ninety degrees about the positive x axis, so it reaches the full radius across and half of it up
        //and down - and never gets near the left of the circle.
        var box = Bounds.Of(drawn.Points);

        Assert.InRange(box.Right, 4990, 5000);
        Assert.True(box.Left > 3000);
    }

    #region Bulges ******************************************************************

    ///
    ///A polyline vertex that bows, which is how a drawing writes a rounded corner or a slot end.
    ///
    ///**It was read as a straight chord**, silently, which is the worst kind of wrong a reader has: the
    ///shape opens, looks like a shape, and is not the one in the file. A slot came out as a rectangle with
    ///its ends cut flat, and nothing anywhere said so.
    ///
    [Fact]
    public void A_vertex_that_bulges_comes_out_as_the_arc_it_describes()
    {
        //A ten-micron chord with a bulge of one on it: a semicircle, which is five microns of bow.
        string run = Pairs(
            0, "LWPOLYLINE", 8, "M1", 90, 2, 70, 0,
            10, 0, 20, 0, 42, 1,
            10, 10, 20, 0);

        var drawn = DrawnFrom(Drawing(run)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        //Ten microns across still, and now five deep where it used to be a line with no height at all.
        Assert.InRange(box.Width, 9990, 10010);
        Assert.InRange(box.Height, 4990, 5010);

        //Every point on the semicircle, within the nanometer the coordinates are rounded to.
        foreach (var point in drawn.Points)
        {
            double away = Math.Sqrt(((point.X - 5000.0) * (point.X - 5000.0)) + ((double)point.Y * point.Y));

            Assert.InRange(away, 4998, 5002);
        }
    }

    ///<summary>The old-style POLYLINE carries its bulges on the vertices, one each rather than inline.</summary>
    [Fact]
    public void An_old_style_polylines_vertices_bulge_too()
    {
        string run = Pairs(0, "POLYLINE", 8, "M1", 70, 0)
            + "\n" + Pairs(0, "VERTEX", 8, "M1", 10, 0, 20, 0, 42, 1)
            + "\n" + Pairs(0, "VERTEX", 8, "M1", 10, 10, 20, 0)
            + "\n" + Pairs(0, "SEQEND", 8, "M1");

        var drawn = DrawnFrom(Drawing(run)).Elements.Single();

        Assert.InRange(Bounds.Of(drawn.Points).Height, 4990, 5010);
    }

    ///
    ///A bulge on the last vertex of a closed run bows the segment that closes it, which is the one segment
    ///with no vertex after it to take the bulge from.
    ///
    [Fact]
    public void A_closed_runs_last_bulge_bows_the_segment_that_closes_it()
    {
        string run = Pairs(
            0, "LWPOLYLINE", 8, "M1", 90, 2, 70, 1,
            10, 0, 20, 0, 42, 1,
            10, 10, 20, 0, 42, 1);

        var drawn = DrawnFrom(Drawing(run)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        //Two semicircles back to back is a circle: ten across and ten deep, not ten across and five.
        Assert.InRange(box.Width, 9990, 10010);
        Assert.InRange(box.Height, 9990, 10010);
    }

    ///
    ///And a bulge belongs to the segment leaving its vertex, not the one arriving at it - a run where only
    ///the second vertex bows must leave the first segment straight.
    ///
    [Fact]
    public void A_bulge_bows_the_segment_that_leaves_its_vertex()
    {
        string run = Pairs(
            0, "LWPOLYLINE", 8, "M1", 90, 3, 70, 0,
            10, 0, 20, 0,
            10, 10, 20, 0, 42, 1,
            10, 10, 20, 10);

        var drawn = DrawnFrom(Drawing(run)).Elements.Single();

        //The bow is on the second segment, which runs up the right - so it reaches past x = 10 microns and
        //never below y = 0, and the first segment is still a straight run along the axis.
        var box = Bounds.Of(drawn.Points);

        Assert.InRange(box.Right, 14990, 15010);
        Assert.InRange(box.Bottom, -10, 10);

        foreach (var point in drawn.Points)
        {
            if (point.X < 9990)
                Assert.InRange(point.Y, -10, 10);
        }
    }

    ///<summary>A run with no bulge at all is the run it always was, to the point.</summary>
    [Fact]
    public void A_run_with_no_bulges_is_unchanged()
    {
        var drawn = DrawnFrom(Drawing(ASquare())).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
        Assert.Equal(5, drawn.Points.Count);
    }

    #endregion **********************************************************************



    ///
    ///**A SOLID's corners are numbered in a Z**, so taking them as written draws a bowtie. The third and
    ///fourth are the far edge backwards, which is the one thing about this entity worth knowing.
    ///
    [Fact]
    public void A_solid_comes_out_as_a_ring_rather_than_a_bowtie()
    {
        string solid = Pairs(0, "SOLID", 8, "M1", 10, 0, 20, 0, 11, 10, 21, 0, 12, 0, 22, 10, 13, 10, 23, 10);

        var drawn = DrawnFrom(Drawing(solid)).Elements.Single();

        //A square of ten microns has an area of a hundred; a bowtie of the same corners has half of it.
        Assert.Equal(10000L * 10000L, Math.Abs(Measure.AreaOf(drawn.Points)));
    }

    [Fact]
    public void Text_comes_across_as_a_label()
    {
        string text = Pairs(0, "TEXT", 8, "M1", 10, 3, 20, 4, 40, 1, 1, "PAD1");

        var drawn = DrawnFrom(Drawing(text)).Elements.Single();

        Assert.Equal("PAD1", drawn.Text);
        Assert.Equal(3000, drawn.Points[0].X);
        Assert.Equal(4000, drawn.Points[0].Y);
    }

    ///
    ///An entity nobody can convert does not cost the file the ones they can.
    ///
    ///This used a SPLINE, which is read now - so the case is made with an MTEXT: a paragraph of formatted
    ///prose with its own markup language inside it, which is a drawing construct in a way a curve never was.
    ///
    [Fact]
    public void An_entity_this_does_not_read_is_skipped_rather_than_refused()
    {
        string mixed = ASquare() + "\n" + Pairs(0, "MTEXT", 8, "M1", 10, 0, 20, 0, 1, @"{\fArial|b1;paragraphs}");

        Assert.Single(DrawnFrom(Drawing(mixed)).Elements);
    }

    #endregion **********************************************************************



    #region The curved entities *****************************************************

    ///
    ///An ellipse, which the format gives as a center, a vector to the end of the major axis, and how long
    ///the minor one is as a fraction of that.
    ///
    ///It used to be dropped, on the reading that a drawing construct is not mask geometry. An ellipse is
    ///mask geometry - it is a rounded pad, a lens, a fillet - and the argument that actually applied was
    ///about how finely to flatten one, which the tolerance answers.
    ///
    [Fact]
    public void An_ellipse_becomes_the_outline_it_describes()
    {
        //Ten across the major axis, four across the minor: ratio 0.4.
        string ellipse = Pairs(0, "ELLIPSE", 8, "M1", 10, 0, 20, 0, 11, 5, 21, 0, 40, 0.4, 41, 0, 42, 6.283185307179586);

        var drawn = DrawnFrom(Drawing(ellipse)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        Assert.InRange(box.Width, 9990, 10010);
        Assert.InRange(box.Height, 3990, 4010);

        //Every point on it, which a bounding box alone would not say - a rectangle has the same box.
        foreach (var point in drawn.Points)
        {
            double x = point.X / 5000.0;
            double y = point.Y / 2000.0;

            Assert.InRange((x * x) + (y * y), 0.999, 1.001);
        }
    }

    ///<summary>An ellipse turned on its side is turned by its major axis vector, not by an angle.</summary>
    [Fact]
    public void An_ellipse_is_turned_by_where_its_major_axis_points()
    {
        string ellipse = Pairs(0, "ELLIPSE", 8, "M1", 10, 0, 20, 0, 11, 0, 21, 5, 40, 0.4, 41, 0, 42, 6.283185307179586);

        var box = Bounds.Of(DrawnFrom(Drawing(ellipse)).Elements.Single().Points);

        //The same ellipse standing up: four across and ten tall.
        Assert.InRange(box.Width, 3990, 4010);
        Assert.InRange(box.Height, 9990, 10010);
    }

    ///<summary>Part of one is an open run, the way part of a circle is.</summary>
    [Fact]
    public void Part_of_an_ellipse_is_a_path_rather_than_an_outline()
    {
        string half = Pairs(0, "ELLIPSE", 8, "M1", 10, 0, 20, 0, 11, 5, 21, 0, 40, 1, 41, 0, 42, 3.141592653589793);

        var reopened = Reopened(Drawing(half));

        Assert.Contains(reopened.Records, record => record.Type == RecordType.PATH);
        Assert.DoesNotContain(reopened.Records, record => record.Type == RecordType.BOUNDARY);
    }

    ///
    ///A spline, evaluated rather than guessed at.
    ///
    ///The old comment argued a spline "has no honest fixed-segment reading", and that is true of a fixed
    ///segment *count* - it is not true of a chord tolerance, which is a statement about the result rather
    ///than about the arithmetic. A degree-1 spline is a polyline, which is what makes it worth testing
    ///first: the curve through those control points is a known answer.
    ///
    [Fact]
    public void A_straight_spline_runs_through_its_control_points()
    {
        //Degree 1 over three points, with the knot vector a clamped degree-1 curve needs.
        string spline = Pairs(
            0, "SPLINE", 8, "M1", 70, 8, 71, 1, 72, 5, 73, 3,
            40, 0, 40, 0, 40, 1, 40, 2, 40, 2,
            10, 0, 20, 0,
            10, 10, 20, 0,
            10, 10, 20, 10);

        var drawn = DrawnFrom(Drawing(spline)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
    }

    ///
    ///And a curved one stays inside the hull its control points make, which is the property that says it
    ///was evaluated as a spline rather than joined up as a polyline.
    ///
    [Fact]
    public void A_curved_spline_bows_inside_its_control_points()
    {
        //A quadratic arch: up at the middle control point, and the curve reaches half that height.
        string spline = Pairs(
            0, "SPLINE", 8, "M1", 70, 8, 71, 2, 72, 6, 73, 3,
            40, 0, 40, 0, 40, 0, 40, 1, 40, 1, 40, 1,
            10, 0, 20, 0,
            10, 5, 20, 10,
            10, 10, 20, 0);

        var drawn = DrawnFrom(Drawing(spline)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        //Ten wide, and five tall rather than ten: a Bezier reaches half way to its middle control point.
        Assert.InRange(box.Width, 9990, 10010);
        Assert.InRange(box.Height, 4990, 5010);
    }

    ///<summary>A spline with only fit points is joined up through them, which is the shape that was drawn.</summary>
    [Fact]
    public void A_spline_with_only_fit_points_is_the_run_through_them()
    {
        string spline = Pairs(
            0, "SPLINE", 8, "M1", 70, 8, 71, 3, 74, 3,
            11, 0, 21, 0,
            11, 10, 21, 0,
            11, 10, 21, 10);

        var box = Bounds.Of(DrawnFrom(Drawing(spline)).Elements.Single().Points);

        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
    }

    ///
    ///A hatch, as the area it fills.
    ///
    ///**Read in order rather than by group code**, which is what makes this entity different from every
    ///other one here: 10 and 20 are the elevation point before the paths, a vertex inside one, and a seed
    ///point after them all. A reader that asks for "the 10" gets the elevation and draws the hatch at the
    ///origin - so the fixture carries all three, which is the case that catches it.
    ///
    [Fact]
    public void A_hatch_becomes_the_area_it_fills()
    {
        string hatch = Pairs(
            0, "HATCH", 8, "M1",
            10, 0, 20, 0, 30, 0,           //the elevation point, which is not the boundary
            2, "SOLID", 70, 1, 71, 0,
            91, 1,
            92, 3, 72, 0, 73, 1, 93, 4,    //one polyline path, closed, four vertices
            10, 20, 20, 20,
            10, 30, 20, 20,
            10, 30, 20, 30,
            10, 20, 20, 30,
            97, 0,
            75, 0, 76, 1,
            98, 1, 10, 25, 20, 25);        //a seed point, which is not the boundary either

        var drawn = DrawnFrom(Drawing(hatch)).Elements.Single();

        var box = Bounds.Of(drawn.Points);

        //Ten microns square, and at twenty microns out rather than at the origin.
        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
        Assert.Equal(20000, box.Left);
        Assert.Equal(20000, box.Bottom);
    }

    ///
    ///An island inside a hatch is a hole, not a shape.
    ///
    ///Emitting each boundary path as its own filled outline turns a washer into a disc with a disc on top
    ///of it - which is the same class of quiet wrongness a bulge read as a chord was. They are subtracted.
    ///
    [Fact]
    public void An_island_in_a_hatch_is_cut_out_of_it()
    {
        string hatch = Pairs(
            0, "HATCH", 8, "M1",
            10, 0, 20, 0,
            2, "SOLID", 70, 1,
            91, 2,
            92, 3, 72, 0, 73, 1, 93, 4,    //outermost: flags 1|2, a closed polyline
            10, 0, 20, 0,
            10, 30, 20, 0,
            10, 30, 20, 30,
            10, 0, 20, 30,
            97, 0,
            92, 2, 72, 0, 73, 1, 93, 4,    //an island: polyline, and neither external nor outermost
            10, 10, 20, 10,
            10, 20, 20, 10,
            10, 20, 20, 20,
            10, 10, 20, 20,
            97, 0,
            75, 0, 76, 1);

        var drawn = DrawnFrom(Drawing(hatch));

        //Thirty microns square with a ten-micron hole: 900 - 100 square microns of area.
        double area = 0;

        foreach (var element in drawn.Elements)
            area += Math.Abs(Measure.AreaOf(element.Points));

        Assert.InRange(area / 1e6, 799, 801);
    }

    ///<summary>A boundary written as edges rather than as a polyline reads the same way.</summary>
    [Fact]
    public void A_hatch_bounded_by_edges_reads_as_the_same_area()
    {
        string hatch = Pairs(
            0, "HATCH", 8, "M1",
            10, 0, 20, 0,
            2, "SOLID", 70, 1,
            91, 1,
            92, 1, 93, 4,
            72, 1, 10, 0, 20, 0, 11, 10, 21, 0,
            72, 1, 10, 10, 20, 0, 11, 10, 21, 10,
            72, 1, 10, 10, 20, 10, 11, 0, 21, 10,
            72, 1, 10, 0, 20, 10, 11, 0, 21, 0,
            97, 0,
            75, 0, 76, 1);

        var box = Bounds.Of(DrawnFrom(Drawing(hatch)).Elements.Single().Points);

        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
    }

    #endregion **********************************************************************



    #region Layers ******************************************************************

    ///
    ///**A layer named after a number is that number.** The whole point of the mapping: somebody drawing for
    ///a shuttle run is told to put the structure on 68/20 and what they do is call the AutoCAD layer 68/20.
    ///Numbering that by declaration order threw away the one instruction in the file.
    ///
    [Theory]
    [InlineData("68", 68, 0)]
    [InlineData("68/20", 68, 20)]
    [InlineData("68.20", 68, 20)]
    [InlineData("68-20", 68, 20)]
    [InlineData("68:20", 68, 20)]
    [InlineData("L68D20", 68, 20)]
    [InlineData("l68d20", 68, 20)]
    [InlineData("METAL1 (68/20)", 68, 20)]
    [InlineData("structural (68)", 68, 0)]
    [InlineData("0", 0, 0)]
    public void A_layer_named_as_a_number_is_that_number(string name, int layer, int dataType)
    {
        var read = DxfReader.NumberFromName(name);

        Assert.NotNull(read);
        Assert.Equal(new LayerKey((short)layer, (short)dataType), read!.Value);
    }

    ///
    ///And a name that is a name is left alone, rather than having a number picked out of it.
    ///
    ///`METAL1` becoming layer 1 would be a guess, and the kind that is right often enough to be trusted and
    ///wrong on the file that mattered. `POLY_2024_01` is the same guess with a date in it. Anything past
    ///what a GDSII layer holds is not one either - which is the case that would otherwise wrap into a
    ///negative number silently.
    ///
    [Theory]
    [InlineData("METAL")]
    [InlineData("METAL1")]
    [InlineData("POLY")]
    [InlineData("POLY_2024_01")]
    [InlineData("M1_68_20")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-7")]
    [InlineData("99999")]
    [InlineData("70000/20")]
    [InlineData("layer one")]
    public void A_layer_named_as_a_name_is_not_given_a_number_from_it(string name)
    {
        Assert.Null(DxfReader.NumberFromName(name));
    }

    ///<summary>Through the reader rather than through the parse: the number reaches the element.</summary>
    [Fact]
    public void A_numbered_layer_name_reaches_the_geometry()
    {
        var gds = DxfReader.Read(Drawing(ASquare("68/20")));

        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements.Single();

        Assert.Equal(68, drawn.Layer.Key.Number);
        Assert.Equal(20, drawn.Layer.Key.DataType);
    }

    ///
    ///**Numbered in the order the LAYER table declares them** when the name is not a number, so two runs of
    ///one file agree. A number taken from a hash of the name, or from whatever order a dictionary happened
    ///to be walked in, would give the same drawing different numbers on different days.
    ///
    [Fact]
    public void Layers_are_numbered_in_the_order_the_table_declares_them()
    {
        string tables = Pairs(
            0, "TABLE", 2, "LAYER",
            0, "LAYER", 2, "BOTTOM",
            0, "LAYER", 2, "TOP",
            0, "ENDTAB");

        //A shape on each, because the library's layer table is built from what is drawn - a layer the
        //drawing declares and never uses is not one this app has anywhere to put.
        var gds = DxfReader.Read(Drawing(ASquare("TOP") + "\n" + ASquare("BOTTOM"), tables: tables));

        Assert.Equal("BOTTOM", gds.AdditionalInformation.Layers.Values.Single(layer => layer.Number == 0).Name);
        Assert.Equal("TOP", gds.AdditionalInformation.Layers.Values.Single(layer => layer.Number == 1).Name);
    }

    ///<summary>A layer an entity uses that the table left out still gets one, at the end.</summary>
    [Fact]
    public void A_layer_the_table_missed_is_still_numbered()
    {
        string tables = Pairs(0, "TABLE", 2, "LAYER", 0, "LAYER", 2, "KNOWN", 0, "ENDTAB");

        var gds = DxfReader.Read(Drawing(ASquare("SURPRISE"), tables: tables));

        var used = gds.AdditionalInformation.Layers.Values.Single(layer => layer.Name == "SURPRISE");

        Assert.Equal(1, used.Number);
    }

    ///<summary>Reading the same drawing twice gives the same numbers, which is what makes a conversion a fact.</summary>
    [Fact]
    public void The_same_drawing_reads_to_the_same_bytes_twice()
    {
        string text = Drawing(ASquare("A") + "\n" + ASquare("B"));

        Assert.Equal(DxfReader.Read(text).Serialize(), DxfReader.Read(text).Serialize());
    }

    ///<summary>The names are carried onto the layers, since GDSII has only numbers to remember them by.</summary>
    [Fact]
    public void The_drawings_layer_names_survive()
    {
        var gds = DxfReader.Read(Drawing(ASquare("POLY")));

        Assert.Contains(gds.AdditionalInformation.Layers.Values, layer => layer.Name == "POLY");
    }

    #endregion **********************************************************************



    #region Blocks ******************************************************************

    private const string ABlock = "0\nBLOCK\n2\nPAD\n10\n0\n20\n0\n"
        + "0\nLWPOLYLINE\n8\nM1\n90\n4\n70\n1\n10\n0\n20\n0\n10\n4\n20\n0\n10\n4\n20\n4\n10\n0\n20\n4\n"
        + "0\nENDBLK";

    [Fact]
    public void A_block_becomes_a_cell()
    {
        var gds = Reopened(Drawing("", blocks: ABlock));

        Assert.Contains("PAD", Hierarchy.Names(gds));
    }

    ///
    ///**An insert is a placement, so the hierarchy is kept.** Flattening one would be easier and would turn
    ///a block used four hundred times into four hundred copies of it.
    ///
    [Fact]
    public void An_insert_places_the_block_it_names()
    {
        string insert = Pairs(0, "INSERT", 8, "M1", 2, "PAD", 10, 20, 20, 30);

        var gds = Reopened(Drawing(insert, blocks: ABlock));

        Assert.Equal(1, Hierarchy.PlacementsOf(gds, "PAD"));

        var drawn = GdsFlattener.Flatten(gds).Elements.Single(element => element.Source!.Structure == "PAD");

        //The block's own square is at its origin, so the instance puts it where the insert says.
        Assert.Equal(20000, Bounds.Of(drawn.Points).Left);
        Assert.Equal(30000, Bounds.Of(drawn.Points).Bottom);
    }

    [Fact]
    public void An_insert_carries_its_rotation()
    {
        string insert = Pairs(0, "INSERT", 8, "M1", 2, "PAD", 10, 0, 20, 0, 50, 90);

        var gds = Reopened(Drawing(insert, blocks: ABlock));

        var placement = gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.SrefModel);

        Assert.Equal(90, Hierarchy.TransformOf(placement).Angle);
    }

    [Fact]
    public void An_insert_carries_its_scale()
    {
        string insert = Pairs(0, "INSERT", 8, "M1", 2, "PAD", 10, 0, 20, 0, 41, 3, 42, 3);

        var gds = Reopened(Drawing(insert, blocks: ABlock));

        var placement = gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Single(element => element.Element is GDS.SrefModel);

        Assert.Equal(3, Hierarchy.TransformOf(placement).Magnification, 9);

        //And it draws three times as big, which is the half a record cannot tell you.
        var drawn = GdsFlattener.Flatten(gds).Elements.Single(element => element.Source!.Structure == "PAD");

        Assert.Equal(12000, Bounds.Of(drawn.Points).Width);
    }

    ///<summary>A repeated insert is one array record rather than one placement per position.</summary>
    [Fact]
    public void A_repeated_insert_becomes_an_array()
    {
        string insert = Pairs(0, "INSERT", 8, "M1", 2, "PAD", 10, 0, 20, 0, 70, 3, 71, 2, 44, 10, 45, 10);

        var gds = Reopened(Drawing(insert, blocks: ABlock));

        Assert.Contains(gds.Records, record => record.Type == RecordType.AREF);

        //Six of them drawn, from one record.
        Assert.Equal(6, GdsFlattener.Flatten(gds).Elements.Count(element => element.Source!.Structure == "PAD"));
    }

    ///<summary>A block is written before anything that places it, which is what a reader expects.</summary>
    [Fact]
    public void A_block_is_written_before_the_cell_that_places_it()
    {
        string insert = Pairs(0, "INSERT", 8, "M1", 2, "PAD", 10, 0, 20, 0);

        var names = Hierarchy.Names(Reopened(Drawing(insert, blocks: ABlock)));

        Assert.Equal(new List<string> { "PAD", DxfReader.TopCell }, names);
    }

    #endregion **********************************************************************



    #region Placing it in the right place *******************************************

    ///
    ///**A negative scale is how DXF spells a mirror**, and GDSII has a flag for it instead - so the two are
    ///translated rather than copied. Four cases, and the last of them is the one a reader gets wrong: two
    ///minus signs look like more mirroring than one and are in fact none.
    ///
    [Theory]
    [InlineData(1, 1, false, 0)]
    [InlineData(1, -1, true, 0)]
    [InlineData(-1, 1, true, 180)]
    [InlineData(-1, -1, false, 180)]
    public void A_negative_scale_becomes_a_reflection(double across, double down, bool mirrored, double turn)
    {
        Assert.Equal((mirrored, turn), DxfReader.MirrorOf(across, down));
    }

    ///
    ///And through the reader: a block mirrored across in the drawing is mirrored in the library.
    ///
    ///Silent before this - PlacementRecords was handed a hardcoded false, and a negative magnification was
    ///dropped on the floor by the writer downstream, so a mirrored block came out unmirrored at 1x. The
    ///block is deliberately not symmetric, since a square mirrors onto itself and would pass either way.
    ///
    [Fact]
    public void A_mirrored_insert_places_a_mirrored_cell()
    {
        //An L: four microns across the foot and one micron up the stem, so left and right are different.
        string block = Pairs(
            0, "BLOCK", 2, "ELL", 10, 0, 20, 0,
            0, "LWPOLYLINE", 8, "M1", 90, 4, 70, 1,
            10, 0, 20, 0,
            10, 4, 20, 0,
            10, 4, 20, 1,
            10, 0, 20, 1,
            0, "ENDBLK");

        string upright = Pairs(0, "INSERT", 8, "M1", 2, "ELL", 10, 0, 20, 0);
        string flipped = Pairs(0, "INSERT", 8, "M1", 2, "ELL", 10, 0, 20, 0, 41, -1, 42, 1);

        var one = Hierarchy.TransformOf(Placement(Drawing(upright, blocks: block)));
        var other = Hierarchy.TransformOf(Placement(Drawing(flipped, blocks: block)));

        Assert.False(one.Mirrored);
        Assert.True(other.Mirrored);

        //Mirroring across is a reflection and a half turn, which is what GDSII has to say it with.
        Assert.Equal(180, other.Angle);

        //And the geometry ends up on the other side of the insertion point.
        var flat = GdsFlattener.Flatten(Reopened(Drawing(flipped, blocks: block)));

        Assert.InRange(Bounds.Of(flat.Elements.Single().Points).Right, -1, 1);
    }

    ///<summary>Both scales negative is a half turn and no reflection, which is the case that reads wrong.</summary>
    [Fact]
    public void An_insert_scaled_negative_both_ways_is_turned_rather_than_mirrored()
    {
        string insert = Pairs(0, "INSERT", 8, "M1", 2, "PAD", 10, 0, 20, 0, 41, -1, 42, -1);

        var transform = Hierarchy.TransformOf(Placement(Drawing(insert, blocks: ABlock)));

        Assert.False(transform.Mirrored);
        Assert.Equal(180, transform.Angle);
    }

    ///
    ///A block's base point is where an insert puts it, so it comes off the geometry.
    ///
    ///A GDSII placement puts the cell's *origin* at the point it names, where an insert puts the block's
    ///base point there - so a block drawn around (100, 100) with its base point there landed a hundred
    ///microns out on every instance. Nearly every writer uses the origin, which is what makes the one that
    ///does not a bad afternoon rather than an obvious bug.
    ///
    [Fact]
    public void A_blocks_base_point_is_where_an_insert_puts_it()
    {
        //Drawn from (100, 100) to (104, 101), with the base point at the corner it starts from.
        string block = Pairs(
            0, "BLOCK", 2, "FAR", 10, 100, 20, 100,
            0, "LWPOLYLINE", 8, "M1", 90, 4, 70, 1,
            10, 100, 20, 100,
            10, 104, 20, 100,
            10, 104, 20, 101,
            10, 100, 20, 101,
            0, "ENDBLK");

        string insert = Pairs(0, "INSERT", 8, "M1", 2, "FAR", 10, 20, 20, 30);

        var box = Bounds.Of(GdsFlattener.Flatten(Reopened(Drawing(insert, blocks: block))).Elements.Single().Points);

        //Twenty microns across and thirty up, which is where the insert put it - not a hundred and twenty.
        Assert.Equal(20000, box.Left);
        Assert.Equal(30000, box.Bottom);
        Assert.Equal(4000, box.Width);
    }

    ///
    ///An entity drawn on a plane facing the other way is not mirrored on the way in.
    ///
    ///DXF measures an entity's coordinates in a plane perpendicular to its extrusion vector, which is
    ///(0, 0, 1) almost every time and so can be ignored for years before it is silently wrong. An extrusion
    ///of (0, 0, -1) - something drawn on the back of a face - flips X, and a reader that takes the numbers
    ///as written draws it backwards with no sign of having done so.
    ///
    [Fact]
    public void An_entity_drawn_on_a_flipped_plane_is_turned_the_right_way_round()
    {
        //A shape well off the axis, so a mirror about it moves the whole thing rather than turning it over
        //in place.
        string run = Pairs(
            0, "LWPOLYLINE", 8, "M1", 90, 4, 70, 1,
            210, 0, 220, 0, 230, -1,
            10, 10, 20, 0,
            10, 20, 20, 0,
            10, 20, 20, 5,
            10, 10, 20, 5);

        var box = Bounds.Of(DrawnFrom(Drawing(run)).Elements.Single().Points);

        //Ten to twenty in the entity's own plane is minus twenty to minus ten in the drawing's.
        Assert.Equal(-20000, box.Left);
        Assert.Equal(-10000, box.Right);
        Assert.Equal(0, box.Bottom);
    }

    ///<summary>And the extrusion that says nothing changes nothing, which is nearly every entity.</summary>
    [Fact]
    public void The_ordinary_extrusion_leaves_a_shape_where_it_was()
    {
        string run = Pairs(
            0, "LWPOLYLINE", 8, "M1", 90, 4, 70, 1,
            210, 0, 220, 0, 230, 1,
            10, 10, 20, 0,
            10, 20, 20, 0,
            10, 20, 20, 5,
            10, 10, 20, 5);

        var box = Bounds.Of(DrawnFrom(Drawing(run)).Elements.Single().Points);

        Assert.Equal(10000, box.Left);
        Assert.Equal(20000, box.Right);
    }

    #endregion **********************************************************************



    #region Being a file at all *****************************************************

    ///<summary>An empty drawing is an empty library rather than an exception.</summary>
    [Fact]
    public void A_drawing_with_nothing_in_it_reads_as_an_empty_library()
    {
        var gds = Reopened(Drawing(""));

        Assert.Single(gds.StreamFormat.Structures);
        Assert.Empty(gds.StreamFormat.Structures[0].Elements);
    }

    ///<summary>Truncated halfway through, which is what a partial download is, rather than a crash.</summary>
    [Fact]
    public void A_drawing_that_stops_partway_is_read_as_far_as_it_goes()
    {
        string cut = "0\nSECTION\n2\nENTITIES\n" + ASquare() + "\n10\n";

        Assert.Single(DrawnFrom(cut).Elements);
    }

    [Fact]
    public void Windows_line_endings_read_the_same_as_the_other_kind()
    {
        string text = Drawing(ASquare());

        Assert.Equal(
            DxfReader.Read(text).Serialize(),
            DxfReader.Read(text.Replace("\n", "\r\n")).Serialize());
    }

    ///<summary>What comes out is a GDSII library, which is what everything downstream assumes.</summary>
    [Fact]
    public void What_comes_out_is_a_well_formed_library()
    {
        var gds = Reopened(Drawing(ASquare(), blocks: ABlock));

        var types = gds.Records.Select(record => record.Type).ToList();

        Assert.Equal(RecordType.HEADER, types[0]);
        Assert.Equal(RecordType.BGNLIB, types[1]);
        Assert.Equal(RecordType.LIBNAME, types[2]);
        Assert.Equal(RecordType.UNITS, types[3]);
        Assert.Equal(RecordType.ENDLIB, types[^1]);
    }

    ///<summary>A database unit is a nanometer, which is what makes a coordinate typed in microns whole.</summary>
    [Fact]
    public void A_database_unit_is_a_nanometer()
    {
        var units = (Real8Data)Reopened(Drawing(ASquare())).StreamFormat.UNITS.Data!;

        Assert.Equal(0.001, units.Values[0], 12);
        Assert.Equal(1e-9, units.Values[1], 15);
    }

    #endregion **********************************************************************
}
