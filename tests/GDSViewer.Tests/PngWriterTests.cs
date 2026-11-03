using System.IO.Compression;
using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Covers PngWriter, which draws a layout as pixels - the picture SvgWriter writes, for a thumbnail that is only looked
///at.
///
///**Read back by hand**, chunk by chunk and row by row, so these say what is in the file rather than what the writer
///meant to put there. What they pin is what the picture has to agree with the SVG on: each layer's color at the
///opacity, layers over the layers before them, shapes on one layer not doubling up, a shape smaller than a pixel still
///shading it by its share, the outline, the pattern, and which way up it is.
///</summary>
public class PngWriterTests
{
    #region Building layouts ************************************************************

    private static Layer LayerOf(short number, string color)
    {
        return new Layer(new LayerKey(number, 0), color);
    }

    private static Element Square(Layer layer, int left, int bottom, int side)
    {
        return new Element
        {
            Layer = layer,
            Points = new List<Element.Point>
            {
                new Element.Point(left, bottom),
                new Element.Point(left + side, bottom),
                new Element.Point(left + side, bottom + side),
                new Element.Point(left, bottom + side)
            }
        };
    }

    private static FlattenedLayout LayoutOf(params Element[] elements)
    {
        var layout = new FlattenedLayout();

        layout.Elements.AddRange(elements);

        return layout;
    }

    #endregion **************************************************************************



    #region Reading the file back *******************************************************

    private sealed record Image(int Width, int Height, byte[] Pixels)
    {
        public (int Red, int Green, int Blue, int Alpha) At(int x, int y)
        {
            int at = ((y * Width) + x) * 4;

            return (Pixels[at], Pixels[at + 1], Pixels[at + 2], Pixels[at + 3]);
        }
    }

    ///<summary>The file's size and its pixels as RGBA, every row unfiltered whichever filter it carries.</summary>
    private static Image Read(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());

        int width = 0;
        int height = 0;
        var packed = new MemoryStream();
        int at = 8;

        while (at < png.Length)
        {
            int length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png.AsSpan(at + 8, length);

            if (type == "IHDR")
            {
                width = (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
                height = (data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7];

                Assert.Equal(8, data[8]);
                Assert.Equal(6, data[9]);
            }
            else if (type == "IDAT")
                packed.Write(data);

            at += 12 + length;
        }

        var raw = new MemoryStream();

        packed.Position = 0;

        using (var inflating = new ZLibStream(packed, CompressionMode.Decompress))
            inflating.CopyTo(raw);

        byte[] rows = raw.ToArray();
        int stride = width * 4;
        var pixels = new byte[stride * height];

        Assert.Equal((stride + 1) * height, rows.Length);

        for (int y = 0; y < height; y++)
        {
            int filter = rows[y * (stride + 1)];

            for (int i = 0; i < stride; i++)
            {
                int value = rows[(y * (stride + 1)) + 1 + i];
                int left = 0;
                int up = 0;
                int corner = 0;

                if (i >= 4)
                    left = pixels[(y * stride) + i - 4];

                if (y > 0)
                    up = pixels[((y - 1) * stride) + i];

                if (i >= 4 && y > 0)
                    corner = pixels[((y - 1) * stride) + i - 4];

                int predicted = 0;

                if (filter == 1)
                    predicted = left;
                else if (filter == 2)
                    predicted = up;
                else if (filter == 3)
                    predicted = (left + up) / 2;
                else if (filter == 4)
                    predicted = paeth(left, up, corner);

                pixels[(y * stride) + i] = (byte)(value + predicted);
            }
        }

        return new Image(width, height, pixels);
    }

    private static int paeth(int left, int up, int corner)
    {
        int guess = left + up - corner;
        int fromLeft = Math.Abs(guess - left);
        int fromUp = Math.Abs(guess - up);
        int fromCorner = Math.Abs(guess - corner);

        if (fromLeft <= fromUp && fromLeft <= fromCorner)
            return left;

        if (fromUp <= fromCorner)
            return up;

        return corner;
    }

    private static Image Draw(FlattenedLayout layout, Bounds frame, int across, int down, float opacity)
    {
        var picture = PngWriter.Build(layout, frame, across, down, opacity);
        var image = Read(picture.Png);

        Assert.Equal(picture.Width, image.Width);
        Assert.Equal(picture.Height, image.Height);

        return image;
    }

    #endregion **************************************************************************



    #region The frame and the pixels ****************************************************

    [Fact]
    public void A_picture_is_its_frame_fitted_to_the_pixels_asked_for()
    {
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 0, 0, 100));

        var wide = PngWriter.Build(layout, new Bounds(0, 0, 2000, 1000), 200, 200, 1);
        var tall = PngWriter.Build(layout, new Bounds(0, 0, 1000, 4000), 200, 200, 1);

        Assert.Equal((200, 100), (wide.Width, wide.Height));
        Assert.Equal((50, 200), (tall.Width, tall.Height));
    }

    [Fact]
    public void A_shape_is_its_layers_color_at_the_opacity_and_round_it_is_clear()
    {
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 10_000, 10_000, 80_000));

        var image = Draw(layout, new Bounds(0, 0, 100_000, 100_000), 100, 100, 0.85f);

        Assert.Equal((255, 0, 0, 217), image.At(50, 50));
        Assert.Equal((0, 0, 0, 0), image.At(2, 2));
        Assert.Equal((0, 0, 0, 0), image.At(97, 50));
    }

    ///<summary>The SVG draws a layer's shapes as one path, so where two of them overlap is no darker than either.</summary>
    [Fact]
    public void Shapes_on_one_layer_do_not_double_up_where_they_overlap()
    {
        var red = LayerOf(1, "#ff0000");
        var layout = LayoutOf(Square(red, 10_000, 10_000, 50_000), Square(red, 40_000, 40_000, 50_000));

        var image = Draw(layout, new Bounds(0, 0, 100_000, 100_000), 100, 100, 0.85f);

        Assert.Equal(image.At(20, 20), image.At(50, 50));
    }

    [Fact]
    public void A_layer_drawn_later_lies_over_one_drawn_before_it()
    {
        var layout = LayoutOf(
            Square(LayerOf(1, "#ff0000"), 10_000, 10_000, 60_000),
            Square(LayerOf(2, "#0000ff"), 30_000, 30_000, 60_000));

        var image = Draw(layout, new Bounds(0, 0, 100_000, 100_000), 100, 100, 0.85f);

        //Blue at 0.85 over red at 0.85: 0.1275 red and 0.85 blue in a pixel 0.9775 strong.
        var (red, green, blue, alpha) = image.At(50, 50);

        Assert.InRange(red, 32, 34);
        Assert.Equal(0, green);
        Assert.InRange(blue, 221, 223);
        Assert.InRange(alpha, 248, 250);
    }

    ///<summary>Which is what a chip at thumbnail size mostly is, and what makes a block of cells a tone rather than gaps.</summary>
    [Fact]
    public void A_shape_smaller_than_a_pixel_shades_it_by_the_share_it_covers()
    {
        //A thousand units to a pixel, so the outline is far too thin to draw and only the fill is counted.
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 3_000, 3_000, 500));

        var image = Draw(layout, new Bounds(0, 0, 10_000, 10_000), 10, 10, 1);

        Assert.Equal((255, 0, 0, 64), image.At(3, 3));
        Assert.Equal(0, image.At(4, 3).Alpha);
    }

    ///<summary>The thumbnail's orientation, which is the SVG's: y runs down, so the least y is the top row.</summary>
    [Fact]
    public void The_top_row_is_the_frames_least_y()
    {
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 0, 0, 20_000));

        var image = Draw(layout, new Bounds(0, 0, 100_000, 100_000), 10, 10, 1);

        Assert.True(image.At(0, 0).Alpha > 0, "the shape at the least y is not at the top");
        Assert.Equal(0, image.At(0, 9).Alpha);
    }

    ///<summary>Six units wide, as the SVG strokes it: at a unit a pixel that is a black band down the shape's edge.</summary>
    [Fact]
    public void An_outline_wide_enough_to_see_is_black_over_the_fill()
    {
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 20, 20, 60));

        var image = Draw(layout, new Bounds(0, 0, 100, 100), 100, 100, 1);
        var (red, green, blue, alpha) = image.At(20, 50);

        Assert.Equal((0, 0, 0, 255), (red, green, blue, alpha));
        Assert.Equal((255, 0, 0, 255), image.At(50, 50));
    }

    ///<summary>The marks in the layer's color, and between them its color washed out - see SvgWriter.appendPattern.</summary>
    [Fact]
    public void A_patterned_layer_shows_its_marks_over_a_washed_ground()
    {
        var squares = LayerOf(1, "#ff0000");

        squares.Fill = LayerFill.Squares;

        var layout = LayoutOf(Square(squares, 0, 0, 100_000));

        var image = Draw(layout, new Bounds(0, 0, 100_000, 100_000), 320, 320, 1);
        var strengths = new HashSet<int>();

        for (int y = 20; y < 300; y++)
        {
            for (int x = 20; x < 300; x++)
                strengths.Add(image.At(x, y).Alpha);
        }

        Assert.Contains(255, strengths);
        Assert.Contains(89, strengths);
    }

    [Fact]
    public void An_open_run_is_a_line_in_its_layers_color()
    {
        var run = new Element
        {
            Layer = LayerOf(1, "#00ff00"),
            IsOpen = true,
            Points = new List<Element.Point> { new Element.Point(0, 50), new Element.Point(100, 50) }
        };

        var image = Draw(LayoutOf(run), new Bounds(0, 0, 100, 100), 100, 100, 1);

        Assert.Equal((0, 255, 0, 255), image.At(50, 49));
        Assert.Equal((0, 255, 0, 255), image.At(50, 50));
        Assert.Equal(0, image.At(50, 40).Alpha);
    }

    [Fact]
    public void A_label_is_not_drawn()
    {
        var label = new Element
        {
            Layer = LayerOf(1, "#ff0000"),
            Text = "VDD",
            Points = new List<Element.Point> { new Element.Point(50, 50) }
        };

        var image = Draw(LayoutOf(label, Square(LayerOf(2, "#0000ff"), 0, 0, 10)), new Bounds(0, 0, 100, 100), 100, 100, 1);

        Assert.Equal(0, image.At(50, 50).Alpha);
    }

    #endregion **************************************************************************



    #region Through Preview *************************************************************

    ///<summary>The same frame the markup gets: the box of everything, and a twentieth of its wider side round it.</summary>
    [Fact]
    public void A_preview_picture_is_framed_as_the_markup_is()
    {
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 0, 0, 1000));

        var picture = Preview.Picture(layout, 110, 110, 1)!;
        var image = Read(picture.Png);

        //The frame is 1,100 units over 110 pixels: the shape runs from pixel 5 to pixel 104, and its outline - six
        //units, so most of a pixel - reaches a little into the pixel either side.
        Assert.Equal((110, 110), (image.Width, image.Height));
        Assert.Equal(0, image.At(3, 55).Alpha);
        Assert.True(image.At(4, 55).Alpha > 0, "the outline does not reach past the shape's edge");
        Assert.Equal(255, image.At(55, 55).Alpha);
        Assert.Equal(0, image.At(106, 55).Alpha);
    }

    [Fact]
    public void An_empty_layout_has_no_preview_picture()
    {
        Assert.Null(Preview.Picture(new FlattenedLayout(), 100, 100, 1));
    }

    [Fact]
    public void A_preview_picture_is_never_more_than_the_largest_across()
    {
        var layout = LayoutOf(Square(LayerOf(1, "#ff0000"), 0, 0, 1000));

        var picture = Preview.Picture(layout, 100_000, 100_000, 1)!;

        Assert.Equal(Preview.LargestPicture, picture.Width);
    }

    #endregion **************************************************************************
}
