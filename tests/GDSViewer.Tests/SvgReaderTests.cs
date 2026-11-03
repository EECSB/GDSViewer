using GdsII;

namespace GDSViewer.Tests;

///
///Reading a drawing as a layout.
///
///**Measured, not looked at.** Each drawing here is small enough to work out by hand - a rectangle, a circle,
///a ring, a line with a pen's width - and what comes out is held to that: its box in nanometers, its area, the
///layer it landed on. A document a few millimeters across, with a viewBox in millimeters, so a user unit is a
///million database units and the numbers stay readable.
///
public class SvgReaderTests
{
    #region Helpers *******************************************************************

    ///<summary>A drawing ten millimeters square whose user unit is a millimeter.</summary>
    private static GDS Drawn(string inside, string extra = "")
    {
        return SvgReader.Read($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10mm\" height=\"10mm\" viewBox=\"0 0 10 10\" {extra}>{inside}</svg>");
    }

    private static List<Element> Shapes(GDS gds)
    {
        return GdsFlattener.Flatten(gds).Elements;
    }

    private static double Area(IEnumerable<Element> shapes)
    {
        return shapes.Sum(shape => Math.Abs(Measure.AreaOf(shape.Points)));
    }

    private const double Millimeter = 1_000_000;

    #endregion ***********************************************************************



    #region Shapes ********************************************************************

    ///<summary>A rectangle, two by three millimeters, a millimeter in from the corner - and Y turned over.</summary>
    [Fact]
    public void A_rectangle_lands_at_its_size_with_Y_going_up()
    {
        var shapes = Shapes(Drawn("<rect x=\"1\" y=\"1\" width=\"2\" height=\"3\"/>"));

        var shape = Assert.Single(shapes);

        Assert.Equal(new Bounds(1_000_000, -4_000_000, 3_000_000, -1_000_000), shape.Box);
        Assert.Equal(6 * Millimeter * Millimeter, Area(shapes));
    }

    [Fact]
    public void A_circle_is_its_area_to_within_its_flattening()
    {
        var shapes = Shapes(Drawn("<circle cx=\"5\" cy=\"5\" r=\"1\"/>"));

        double circle = Math.PI * Millimeter * Millimeter;

        Assert.InRange(Area(shapes), circle * 0.9999, circle * 1.0001);
    }

    ///<summary>A rectangle with rounded corners: the square less what the four quarter circles leave out.</summary>
    [Fact]
    public void A_rounded_rectangle_loses_its_corners()
    {
        var shapes = Shapes(Drawn("<rect width=\"4\" height=\"4\" rx=\"1\"/>"));

        double expected = (16 - (4 - Math.PI)) * Millimeter * Millimeter;

        Assert.InRange(Area(shapes), expected * 0.9999, expected * 1.0001);
    }

    [Fact]
    public void A_polygon_is_closed_and_a_polyline_filled_as_though_it_were()
    {
        var polygon = Shapes(Drawn("<polygon points=\"0,0 2,0 2,2\"/>"));
        var polyline = Shapes(Drawn("<polyline points=\"0,0 2,0 2,2\"/>"));

        Assert.Equal(2 * Millimeter * Millimeter, Area(polygon));
        Assert.Equal(Area(polygon), Area(polyline));
    }

    #endregion ***********************************************************************



    #region Paths *********************************************************************

    ///<summary>Two arcs make a circle, which takes the arc conversion through both of its flags.</summary>
    [Fact]
    public void Arcs_make_a_circle()
    {
        var shapes = Shapes(Drawn("<path d=\"M 4 5 A 1 1 0 1 0 6 5 A 1 1 0 1 0 4 5 Z\"/>"));

        double circle = Math.PI * Millimeter * Millimeter;

        Assert.InRange(Area(shapes), circle * 0.9999, circle * 1.0001);
    }

    ///<summary>A parabola over a base of two and a rise of one: two thirds of the box round it.</summary>
    [Fact]
    public void A_quadratic_curve_is_a_parabola()
    {
        var shapes = Shapes(Drawn("<path d=\"M0,0 Q1,2 2,0 Z\"/>"));

        double parabola = 4.0 / 3 * Millimeter * Millimeter;

        Assert.InRange(Area(shapes), parabola * 0.9999, parabola * 1.0001);
    }

    ///<summary>The same square four ways: absolute, relative, by H and V, and packed with no spaces.</summary>
    [Theory]
    [InlineData("M1 1 L3 1 L3 3 L1 3 Z")]
    [InlineData("m1 1 l2 0 l0 2 l-2 0 z")]
    [InlineData("M1,1H3V3H1Z")]
    [InlineData("M1 1 3 1 3 3 1 3z")]
    public void Every_way_of_writing_a_square_is_the_same_square(string data)
    {
        var shape = Assert.Single(Shapes(Drawn($"<path d=\"{data}\"/>")));

        Assert.Equal(new Bounds(1_000_000, -3_000_000, 3_000_000, -1_000_000), shape.Box);
    }

    ///<summary>A smooth cubic reflects the last control point, so S after C draws the same S-curve as two Cs.</summary>
    [Fact]
    public void A_smooth_curve_reflects_the_one_before()
    {
        var smooth = Shapes(Drawn("<path d=\"M0,0 C0,2 2,2 2,0 S4,-2 4,0 L4,4 L0,4 Z\"/>"));
        var spelled = Shapes(Drawn("<path d=\"M0,0 C0,2 2,2 2,0 C2,-2 4,-2 4,0 L4,4 L0,4 Z\"/>"));

        Assert.Equal(Area(spelled), Area(smooth));
    }

    #endregion ***********************************************************************



    #region Fill and stroke ***********************************************************

    ///<summary>A square inside a square, one path, filled even-odd: a frame, cut as a keyhole since GDSII has no hole.</summary>
    [Fact]
    public void Even_odd_makes_a_hole()
    {
        var shapes = Shapes(Drawn("<path fill-rule=\"evenodd\" d=\"M0 0 H4 V4 H0 Z M1 1 H3 V3 H1 Z\"/>"));

        Assert.Equal((16 - 4) * Millimeter * Millimeter, Area(shapes));
    }

    ///<summary>Under nonzero, the same two squares run the same way are one square - and run opposite ways, a frame.</summary>
    [Fact]
    public void Nonzero_makes_a_hole_only_when_the_inner_ring_runs_the_other_way()
    {
        var same = Shapes(Drawn("<path d=\"M0 0 H4 V4 H0 Z M1 1 H3 V3 H1 Z\"/>"));
        var opposite = Shapes(Drawn("<path d=\"M0 0 H4 V4 H0 Z M1 1 V3 H3 V1 Z\"/>"));

        Assert.Equal(16 * Millimeter * Millimeter, Area(same));
        Assert.Equal(12 * Millimeter * Millimeter, Area(opposite));
    }

    ///<summary>A line with a pen a millimeter wide, flat at the ends: ten by one.</summary>
    [Fact]
    public void A_stroked_line_is_the_area_its_pen_covers()
    {
        var shapes = Shapes(Drawn("<line x1=\"0\" y1=\"5\" x2=\"10\" y2=\"5\" stroke=\"black\" stroke-width=\"1\"/>"));

        Assert.Equal(10 * Millimeter * Millimeter, Area(shapes));
    }

    ///<summary>A square drawn only with a pen is a frame: its outline, the pen's width wide, with a hole in the middle.</summary>
    [Fact]
    public void A_stroked_square_is_a_frame()
    {
        var shapes = Shapes(Drawn("<rect x=\"1\" y=\"1\" width=\"4\" height=\"4\" fill=\"none\" stroke=\"black\" stroke-width=\"1\"/>"));

        //Five square outside, three inside, the pen centered on the line.
        Assert.Equal((25 - 9) * Millimeter * Millimeter, Area(shapes));
    }

    [Fact]
    public void Nothing_painted_is_nothing_drawn()
    {
        Assert.Empty(Shapes(Drawn("<rect width=\"2\" height=\"2\" fill=\"none\"/>")));
        Assert.Empty(Shapes(Drawn("<rect width=\"2\" height=\"2\" style=\"display:none\"/>")));
        Assert.Empty(Shapes(Drawn("<g visibility=\"hidden\"><rect width=\"2\" height=\"2\"/></g>")));
        Assert.Empty(Shapes(Drawn("<rect width=\"2\" height=\"2\" fill-opacity=\"0\"/>")));
        Assert.Empty(Shapes(Drawn("<defs><rect width=\"2\" height=\"2\"/></defs>")));
    }

    #endregion ***********************************************************************



    #region Transforms ****************************************************************

    [Fact]
    public void A_group_moves_and_scales_what_is_in_it()
    {
        var shape = Assert.Single(Shapes(Drawn("<g transform=\"translate(5,0) scale(2)\"><rect width=\"1\" height=\"1\"/></g>")));

        Assert.Equal(new Bounds(5_000_000, -2_000_000, 7_000_000, 0), shape.Box);
    }

    ///<summary>A quarter turn: a two-by-one bar standing up, its corner at the origin.</summary>
    [Fact]
    public void A_rotation_turns_about_the_origin()
    {
        var shape = Assert.Single(Shapes(Drawn("<rect width=\"2\" height=\"1\" transform=\"rotate(90)\"/>")));

        Assert.Equal(new Bounds(-1_000_000, -2_000_000, 0, 0), shape.Box);
    }

    [Fact]
    public void A_matrix_is_the_same_as_the_functions_it_spells()
    {
        var spelled = Assert.Single(Shapes(Drawn("<rect width=\"1\" height=\"1\" transform=\"translate(3,4) scale(2,3)\"/>")));
        var matrix = Assert.Single(Shapes(Drawn("<rect width=\"1\" height=\"1\" transform=\"matrix(2,0,0,3,3,4)\"/>")));

        Assert.Equal(spelled.Box, matrix.Box);
    }

    ///<summary>A use draws what it names, again, where it says.</summary>
    [Fact]
    public void A_use_draws_what_it_names_where_it_says()
    {
        var shapes = Shapes(Drawn("<defs><rect id=\"r\" width=\"1\" height=\"1\"/></defs><use href=\"#r\" x=\"3\"/><use xlink:href=\"#r\" y=\"2\"/>"));

        Assert.Equal(2, shapes.Count);
        Assert.Contains(shapes, shape => shape.Box == new Bounds(3_000_000, -1_000_000, 4_000_000, 0));
        Assert.Contains(shapes, shape => shape.Box == new Bounds(0, -3_000_000, 1_000_000, -2_000_000));
    }

    #endregion ***********************************************************************



    #region Units *********************************************************************

    ///<summary>A drawing in pixels is measured at the CSS pixel: ninety-six of them are an inch.</summary>
    [Fact]
    public void A_drawing_in_pixels_is_measured_at_ninety_six_to_the_inch()
    {
        var shape = Assert.Single(Shapes(SvgReader.Read("<svg width=\"96\" height=\"96\"><rect width=\"96\" height=\"96\"/></svg>")));

        Assert.Equal(25_400_000, shape.Box.Width);
    }

    ///<summary>A viewBox squeezed into a size of its own is scaled, whatever its user units are called.</summary>
    [Fact]
    public void A_viewbox_is_scaled_to_the_size_the_document_says()
    {
        var shape = Assert.Single(Shapes(SvgReader.Read("<svg width=\"20mm\" height=\"20mm\" viewBox=\"0 0 1000 1000\"><rect width=\"500\" height=\"500\"/></svg>")));

        Assert.Equal(10_000_000, shape.Box.Width);
    }

    #endregion ***********************************************************************



    #region Layers ********************************************************************

    ///<summary>Two colors, two layers - named for their colors and drawn in them.</summary>
    [Fact]
    public void Each_color_is_a_layer_drawn_in_it()
    {
        var gds = Drawn("<rect width=\"1\" height=\"1\" fill=\"red\"/><rect x=\"2\" width=\"1\" height=\"1\" fill=\"#00f\"/><rect x=\"4\" width=\"1\" height=\"1\" fill=\"rgb(255,0,0)\"/>");

        var layers = gds.AdditionalInformation.Layers.Values.OrderBy(layer => layer.Number).ToList();

        Assert.Equal(2, layers.Count);
        Assert.Equal("#ff0000", layers[0].Color);
        Assert.Equal("#ff0000", layers[0].Name);
        Assert.Equal("#0000ff", layers[1].Color);
        Assert.True(layers[0].ColorIsCustom);
    }

    ///<summary>An Inkscape layer is a layer, and a name that is a number is that number.</summary>
    [Fact]
    public void An_inkscape_layer_named_by_number_is_that_layer()
    {
        var gds = Drawn(
            "<g inkscape:groupmode=\"layer\" inkscape:label=\"68/20\"><rect width=\"1\" height=\"1\" fill=\"red\"/></g>" +
            "<g inkscape:groupmode=\"layer\" inkscape:label=\"Channels\"><rect x=\"2\" width=\"1\" height=\"1\" fill=\"blue\"/></g>");

        var keys = gds.AdditionalInformation.Layers.Keys.ToHashSet();

        Assert.Contains(new LayerKey(68, 20), keys);
        Assert.Contains(new LayerKey(1, 0), keys);
        Assert.Equal("Channels", gds.AdditionalInformation.Layers[new LayerKey(1, 0)].Name);
    }

    ///<summary>The style sheet Illustrator writes: a class per paint.</summary>
    [Fact]
    public void A_style_sheet_class_paints_what_carries_it()
    {
        var gds = Drawn("<style>.cls-1{fill:#00ff00;}</style><rect class=\"cls-1\" width=\"1\" height=\"1\"/>");

        Assert.Equal("#00ff00", Assert.Single(gds.AdditionalInformation.Layers.Values).Color);
    }

    #endregion ***********************************************************************



    #region Which files ***************************************************************

    [Fact]
    public void An_svg_is_told_from_the_formats_beside_it()
    {
        Assert.True(SvgReader.LooksLikeSvg(System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\">")));
        Assert.True(SvgReader.LooksLikeSvg(System.Text.Encoding.UTF8.GetBytes("﻿<?xml version=\"1.0\"?>\n<!-- Created with Inkscape -->\n<svg>")));

        Assert.False(SvgReader.LooksLikeSvg(System.Text.Encoding.UTF8.GetBytes("%SEMI-OASIS\r\n")));
        Assert.False(SvgReader.LooksLikeSvg(System.Text.Encoding.UTF8.GetBytes("  0\nSECTION\n  2\nENTITIES\n")));
        Assert.False(SvgReader.LooksLikeSvg(GdsTestData.MinimalLibrary()));
    }

    [Fact]
    public void Markup_with_no_drawing_in_it_is_refused_out_loud()
    {
        Assert.Throws<InvalidDataException>(() => SvgReader.Read("<html><body>not a drawing</body></html>"));
    }

    ///<summary>What comes out is a library like any other: written and read back, the same shapes.</summary>
    [Fact]
    public void What_it_reads_is_a_library_that_saves()
    {
        var gds = Drawn("<path fill-rule=\"evenodd\" d=\"M0 0 H4 V4 H0 Z M1 1 H3 V3 H1 Z\"/><circle cx=\"7\" cy=\"7\" r=\"1\" fill=\"blue\"/>");

        var again = new GDS(gds.Serialize());

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(again));
        Assert.Equal(SvgReader.TopCell, Hierarchy.Summarize(again).Single().Name);
    }

    #endregion ***********************************************************************



    #region What a file cannot make it do *********************************************

    ///
    ///**Whatever is wrong with a drawing comes out as the one exception the app catches.** The shell reports an
    ///InvalidDataException in a line and keeps what it had open; anything else reaches its error screen. A
    ///character reference to half of a UTF-16 pair was one - kept as written now, like any reference this
    ///reader does not know.
    ///
    [Fact]
    public void Half_a_character_is_kept_as_written_rather_than_thrown()
    {
        var gds = Drawn("<rect id=\"&#xD800;\" x=\"1\" y=\"1\" width=\"2\" height=\"2\"/><rect id=\"&#56320;\" x=\"5\" y=\"5\" width=\"2\" height=\"2\"/>");

        Assert.Equal(2, Shapes(gds).Count);
    }

    ///<summary>
    ///Nested deeper than any drawing is, refused where it is read - two thousand groups took the test process
    ///down with a stack overflow, which nothing can catch.
    ///</summary>
    [Fact]
    public void Nesting_past_any_real_drawing_is_refused_rather_than_overflowing()
    {
        string nested(int depth)
        {
            return string.Concat(Enumerable.Repeat("<g>", depth)) + "<rect width=\"1\" height=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", depth));
        }

        Assert.Single(Shapes(Drawn(nested(100))));
        Assert.Throws<InvalidDataException>(() => Drawn(nested(5000)));
    }

    ///<summary>A use draws its target inside itself, so a chain of them nests deeper than anything written down - and is held to the same limit.</summary>
    [Fact]
    public void A_chain_of_uses_is_held_to_the_same_depth()
    {
        var builder = new System.Text.StringBuilder("<defs><rect id=\"l0\" width=\"1\" height=\"1\"/>");

        //Thirty levels, each a use of the one before inside five groups: six deeper a level once drawn.
        for (int i = 1; i <= 30; i++)
            builder.Append($"<g id=\"l{i}\"><g><g><g><g><use href=\"#l{i - 1}\"/></g></g></g></g></g>");

        builder.Append("</defs><use href=\"#l30\"/>");

        Assert.Throws<InvalidDataException>(() => Drawn(builder.ToString()));
    }

    ///
    ///**A few lines that double at every one.** Each group is two uses of the group before, so twenty-four lines
    ///come to sixteen million elements - and the thirty-two levels a use may nest would be four billion, from a
    ///file of a few kilobytes. Refused once it is past two million, which no drawing anybody made comes near.
    ///
    [Fact]
    public void A_use_repeating_a_use_is_refused_before_it_runs_away()
    {
        var builder = new System.Text.StringBuilder("<defs><g id=\"l0\"/>");

        for (int i = 1; i <= 24; i++)
            builder.Append($"<g id=\"l{i}\"><use href=\"#l{i - 1}\"/><use href=\"#l{i - 1}\"/></g>");

        builder.Append("</defs><use href=\"#l24\"/>");

        Assert.Throws<InvalidDataException>(() => Drawn(builder.ToString()));
    }

    #endregion ***********************************************************************
}
