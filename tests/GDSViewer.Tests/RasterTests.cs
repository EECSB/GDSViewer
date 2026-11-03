using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Text and pictures as geometry: the pixel font, and squares merged into the outlines a layout can hold.
///
///The font is a table of bytes copied from a convention, which is exactly the kind of thing that is wrong in
///one character and right in the rest - so a few glyphs are pinned as pictures here, the way they read. The
///merge is Clipper's and is covered as such elsewhere; what these hold is the arithmetic around it: which way
///up a picture lands, how big a pixel is, and that the area drawn is the area lit.
///</summary>
public class RasterTests
{
    ///<summary>A bitmap as the rows of a picture, # for lit, so a glyph can be read in the test that pins it.</summary>
    private static string[] Picture(bool[,] lit)
    {
        var rows = new string[lit.GetLength(0)];

        for (int row = 0; row < rows.Length; row++)
        {
            var line = new char[lit.GetLength(1)];

            for (int column = 0; column < line.Length; column++)
            {
                if (lit[row, column])
                    line[column] = '#';
                else
                    line[column] = '.';
            }

            rows[row] = new string(line);
        }

        return rows;
    }

    private static bool[,] Lit(params string[] rows)
    {
        return Raster.FromRows(rows);
    }

    ///
    ///The area a set of outlines covers, by the shoelace - holes are keyholes, so each ring is simple.
    ///
    ///Every edge including the one back to the start, whether or not the ring repeats its first point: a
    ///repeated point adds an edge of no length, and Merge hands its rings back without one.
    ///
    private static long AreaOf(List<List<Element.Point>> polygons)
    {
        long twice = 0;

        foreach (var polygon in polygons)
        {
            long ring = 0;

            for (int i = 0; i < polygon.Count; i++)
            {
                var from = polygon[i];
                var to = polygon[(i + 1) % polygon.Count];

                ring += ((long)from.X * to.Y) - ((long)to.X * from.Y);
            }

            twice += Math.Abs(ring);
        }

        return twice / 2;
    }

    #region The font ***************************************************************

    [Fact]
    public void An_A_reads_as_an_A()
    {
        Assert.Equal(new[]
        {
            ".###.",
            "#...#",
            "#...#",
            "#...#",
            "#####",
            "#...#",
            "#...#"
        }, Picture(PixelFont.Bitmap("A")));
    }

    ///<summary>Lower case sits inside the same seven rows, its tail on the last of them rather than below.</summary>
    [Fact]
    public void A_g_keeps_inside_the_seven_rows()
    {
        Assert.Equal(new[]
        {
            ".....",
            ".####",
            "#...#",
            "#...#",
            ".####",
            "....#",
            ".###."
        }, Picture(PixelFont.Bitmap("g")));
    }

    ///<summary>Five columns a character and one of space between them, and none after the last.</summary>
    [Fact]
    public void Characters_are_six_columns_apart_and_the_writing_ends_where_it_ends()
    {
        var lit = PixelFont.Bitmap("IIII");

        Assert.Equal(PixelFont.Height, lit.GetLength(0));
        Assert.Equal((4 * PixelFont.Advance) - 1, lit.GetLength(1));
    }

    ///
    ///Lines one under the other with two rows between, all from the left and as wide as the longest - writing
    ///pasted with its line breaks keeps them - and an empty line a line's height of nothing.
    ///
    [Fact]
    public void Lines_go_one_under_the_other_from_the_same_left_edge()
    {
        var lit = PixelFont.Lines(new[] { "I", "", "AB" });

        Assert.Equal((3 * PixelFont.Height) + (2 * PixelFont.LineGap), lit.GetLength(0));
        Assert.Equal((2 * PixelFont.Advance) - 1, lit.GetLength(1));

        var rows = Picture(lit);

        Assert.Equal(Picture(PixelFont.Bitmap("I"))[0] + "......", rows[0]);
        Assert.Equal(new string('.', 11), rows[PixelFont.Height]);
        Assert.Equal(new string('.', 11), rows[PixelFont.Height + PixelFont.LineGap]);
        Assert.Equal(Picture(PixelFont.Bitmap("AB"))[0], rows[2 * (PixelFont.Height + PixelFont.LineGap)]);
    }

    ///<summary>A character the font has not got is drawn, as a question mark, rather than silently left out.</summary>
    [Fact]
    public void A_character_outside_the_font_is_a_question_mark()
    {
        Assert.Equal(Picture(PixelFont.Bitmap("?")), Picture(PixelFont.Bitmap("é")));
    }

    [Fact]
    public void A_space_is_nothing_lit()
    {
        Assert.Empty(Raster.Polygons(PixelFont.Bitmap(" "), 10));
    }

    #endregion **********************************************************************



    #region Squares into outlines ***************************************************

    ///<summary>The top row is the highest: a picture is read downwards and a layout's y runs up.</summary>
    [Fact]
    public void A_picture_lands_the_right_way_up()
    {
        var polygons = Raster.Polygons(Lit("#.", ".."), 10);

        var only = Assert.Single(polygons);

        Assert.Equal(10, only.Min(point => point.Y));
        Assert.Equal(20, only.Max(point => point.Y));
        Assert.Equal(0, only.Min(point => point.X));
        Assert.Equal(10, only.Max(point => point.X));
    }

    ///<summary>Touching pixels are one outline, not a square each.</summary>
    [Fact]
    public void A_block_of_pixels_is_one_outline()
    {
        var polygons = Raster.Polygons(Lit("##", "##"), 5);

        Assert.Single(polygons);
        Assert.Equal(100, AreaOf(polygons));
    }

    ///<summary>
    ///A ring keeps its hole: eight pixels lit round one that is not, drawn as one keyholed outline covering the
    ///eight and not the ninth.
    ///</summary>
    [Fact]
    public void A_ring_keeps_its_hole()
    {
        var polygons = Raster.Polygons(Lit("###", "#.#", "###"), 10);

        Assert.Equal(8 * 100, AreaOf(polygons));
    }

    ///<summary>Whatever the picture, the area drawn is the area lit - here every pixel of a line of text.</summary>
    [Fact]
    public void The_area_drawn_is_the_area_lit()
    {
        var lit = PixelFont.Bitmap("GDS 130");
        long count = 0;

        foreach (bool one in lit)
        {
            if (one)
                count++;
        }

        Assert.Equal(count * 4 * 4, AreaOf(Raster.Polygons(lit, 4)));
    }

    [Fact]
    public void A_pixel_of_no_size_draws_nothing()
    {
        Assert.Empty(Raster.Polygons(Lit("##"), 0));
    }

    #endregion **********************************************************************



    #region Turned and mirrored where it lies ***************************************

    ///<summary>An L, three rows by two, which reads differently every way round it can be turned or mirrored.</summary>
    private static readonly string[] AnL = { "#.", "#.", "##" };

    ///<summary>A quarter to the right as it is read: the bottom row is the first column, from the top.</summary>
    [Fact]
    public void A_quarter_to_the_right_lays_the_bottom_row_down_the_left()
    {
        Assert.Equal(new[] { "###", "#.." }, Picture(Raster.Turned(Lit(AnL), 1)));
    }

    [Fact]
    public void A_quarter_to_the_left_is_three_to_the_right()
    {
        Assert.Equal(Picture(Raster.Turned(Lit(AnL), 3)), Picture(Raster.Turned(Lit(AnL), -1)));
        Assert.Equal(new[] { "..#", "###" }, Picture(Raster.Turned(Lit(AnL), -1)));
    }

    ///<summary>Four quarters and none are the bitmap as it was - a copy, so turning what comes back leaves the one given alone.</summary>
    [Fact]
    public void Four_quarters_come_back_round_to_where_they_started()
    {
        var lit = Lit(AnL);

        Assert.Equal(AnL, Picture(Raster.Turned(lit, 4)));
        Assert.Equal(AnL, Picture(Raster.Turned(lit, 0)));
        Assert.NotSame(lit, Raster.Turned(lit, 0));
    }

    [Fact]
    public void Mirrored_down_its_last_row_is_first()
    {
        Assert.Equal(new[] { "##", "#.", "#." }, Picture(Raster.MirroredDown(Lit(AnL))));
    }

    ///
    ///Writing the right way round in the 2D view, which draws Y downward: mirrored down before it is drawn, the top
    ///row of the words lands at the least Y - the top of the screen - and the bottom row highest.
    ///
    [Fact]
    public void Mirrored_down_the_top_row_lands_lowest()
    {
        var polygons = Raster.Polygons(Raster.MirroredDown(Lit("#.", "..")), 10);

        var only = Assert.Single(polygons);

        Assert.Equal(0, only.Min(point => point.Y));
        Assert.Equal(10, only.Max(point => point.Y));
    }

    #endregion **********************************************************************



    #region A picture read back from the browser ************************************

    ///<summary>The rows a decoded picture comes back as read as the picture they draw, anything but # unlit.</summary>
    [Fact]
    public void Rows_read_back_as_the_picture_they_draw()
    {
        var lit = Raster.FromRows(new[] { "#.#", "ooo", ".#." });

        Assert.Equal(new[] { "#.#", "...", ".#." }, Picture(lit));
    }

    ///<summary>A short row is a row with less lit in it, not a picture that cannot be read.</summary>
    [Fact]
    public void A_short_row_is_padded_unlit()
    {
        var lit = Raster.FromRows(new[] { "##", "#" });

        Assert.Equal(new[] { "##", "#." }, Picture(lit));
    }

    [Fact]
    public void No_rows_is_nothing_drawn()
    {
        Assert.Empty(Raster.Polygons(Raster.FromRows(Array.Empty<string>()), 10));
    }

    #endregion **********************************************************************
}
