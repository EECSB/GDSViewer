using System.IO.Compression;
using GdsII;

namespace GDSViewer.Tests;

///
///A layout as one-bit bitmaps for a mask writer.
///
///**Counted pixel for pixel.** A mask is exposed where a pixel's middle is inside a shape and nowhere else, so
///each case is small enough to count: a square of ten units at a unit a pixel is a hundred pixels, at two
///units twenty-five, and a ring keyholed by the booleans is the outer square less the inner. The PNG is read
///back by hand - its chunks, its checksums by a second implementation, its rows inflated - since a file a mask
///writer refuses is worse than no file.
///
public class MaskBitmapsTests
{
    #region Helpers *******************************************************************

    private static List<Element.Point> Square(int left, int bottom, int side)
    {
        return Shapes.Between(left, bottom, left + side, bottom + side);
    }

    private static readonly Bounds Frame = new Bounds(0, 0, 10, 10);

    ///<summary>A PNG's chunks, each checked against a CRC worked out bit by bit rather than by table.</summary>
    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png[..8]);

        var chunks = new List<(string, byte[])>();
        int at = 8;

        while (at < png.Length)
        {
            int length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            byte[] data = png[(at + 8)..(at + 8 + length)];
            uint stored = (uint)((png[at + 8 + length] << 24) | (png[at + 9 + length] << 16) | (png[at + 10 + length] << 8) | png[at + 11 + length]);

            Assert.Equal(BitwiseCrc(png[(at + 4)..(at + 8 + length)]), stored);

            chunks.Add((type, data));
            at += 12 + length;
        }

        return chunks;
    }

    private static uint BitwiseCrc(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in bytes)
        {
            crc ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ 0xEDB88320;
                else
                    crc >>= 1;
            }
        }

        return ~crc;
    }

    #endregion ***********************************************************************



    #region Rasterizing ***************************************************************

    [Fact]
    public void A_square_is_its_area_in_pixels()
    {
        var shapes = new[] { Square(0, 0, 10) };

        Assert.Equal(100, MaskBitmaps.Rasterize(shapes, Frame, 1).Count());
        Assert.Equal(25, MaskBitmaps.Rasterize(shapes, Frame, 2).Count());
    }

    ///<summary>
    ///**A pixel is in when its middle is.** A shape from 0.5 to 2.5 covers the middles at 0.5, 1.5 and not 2.5:
    ///the left edge counts and the right does not, so two shapes that meet share no pixel and leave no gap.
    ///</summary>
    [Fact]
    public void A_pixel_is_in_when_its_middle_is()
    {
        var bitmap = MaskBitmaps.Rasterize(new[] { (IReadOnlyList<Element.Point>)Shapes.Between(1, 0, 3, 10) }, Frame, 1);

        Assert.False(bitmap[0, 5]);
        Assert.True(bitmap[1, 5]);
        Assert.True(bitmap[2, 5]);
        Assert.False(bitmap[3, 5]);
    }

    [Fact]
    public void Two_shapes_that_meet_share_no_pixel_and_leave_no_gap()
    {
        var left = MaskBitmaps.Rasterize(new[] { (IReadOnlyList<Element.Point>)Shapes.Between(0, 0, 4, 10) }, Frame, 1);
        var right = MaskBitmaps.Rasterize(new[] { (IReadOnlyList<Element.Point>)Shapes.Between(4, 0, 10, 10) }, Frame, 1);

        Assert.Equal(100, left.Count() + right.Count());

        for (int x = 0; x < 10; x++)
            Assert.NotEqual(left[x, 5], right[x, 5]);
    }

    ///<summary>The top row is the top of the frame, since an image counts down and a layout counts up.</summary>
    [Fact]
    public void The_top_row_is_the_top_of_the_layout()
    {
        var bitmap = MaskBitmaps.Rasterize(new[] { Square(0, 8, 2) }, Frame, 1);

        Assert.True(bitmap[0, 0]);
        Assert.True(bitmap[1, 1]);
        Assert.False(bitmap[0, 9]);
        Assert.Equal(4, bitmap.Count());
    }

    ///<summary>A ring as the booleans make one - a keyhole - is the outer square less the inner, the channel taking nothing.</summary>
    [Fact]
    public void A_keyholed_ring_is_its_ring()
    {
        var ring = Booleans.Combine(new[] { Square(0, 0, 10) }, new[] { Square(3, 3, 4) }, BooleanOperation.Not);

        Assert.Equal(100 - 16, MaskBitmaps.Rasterize(ring, Frame, 1).Count());
    }

    ///<summary>Shapes on one layer that overlap are one exposed area, counted once.</summary>
    [Fact]
    public void Overlapping_shapes_are_one_area()
    {
        var shapes = new[] { Square(0, 0, 6), Square(4, 4, 6) };

        Assert.Equal(36 + 36 - 4, MaskBitmaps.Rasterize(shapes, Frame, 1).Count());
    }

    [Fact]
    public void A_bitmap_too_large_to_hold_is_refused_with_a_reason()
    {
        var huge = new Bounds(0, 0, 1_000_000, 1_000_000);

        var refused = Assert.Throws<ArgumentException>(() => MaskBitmaps.Rasterize(new[] { Square(0, 0, 10) }, huge, 1));

        Assert.Contains("coarser", refused.Message);
    }

    #endregion ***********************************************************************



    #region Writing *******************************************************************

    ///<summary>The PNG a mask writer gets: one-bit grayscale, the size asked for, the scale written in, shapes black.</summary>
    [Fact]
    public void The_png_is_a_one_bit_picture_with_its_scale()
    {
        var bitmap = MaskBitmaps.Rasterize(new[] { Square(0, 8, 2) }, Frame, 1);
        var chunks = Chunks(MaskBitmaps.Png(bitmap, shapesDark: true, micronsPerPixel: 2));

        Assert.Equal(new[] { "IHDR", "pHYs", "IDAT", "IEND" }, chunks.Select(chunk => chunk.Type));

        var header = chunks[0].Data;

        Assert.Equal(10, (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3]);
        Assert.Equal(10, (header[4] << 24) | (header[5] << 16) | (header[6] << 8) | header[7]);
        Assert.Equal(1, header[8]);
        Assert.Equal(0, header[9]);

        //Two microns a pixel is half a million pixels a meter.
        var physical = chunks[1].Data;

        Assert.Equal(500_000, (physical[0] << 24) | (physical[1] << 16) | (physical[2] << 8) | physical[3]);
        Assert.Equal(1, physical[8]);

        using var inflated = new MemoryStream();

        using (var compressed = new ZLibStream(new MemoryStream(chunks[2].Data), CompressionMode.Decompress))
            compressed.CopyTo(inflated);

        var rows = inflated.ToArray();

        //Ten rows of a filter byte and two bytes of pixels.
        Assert.Equal(10 * 3, rows.Length);

        //The top row: the square's two pixels dark - zero - and the rest light.
        Assert.Equal(0, rows[0]);
        Assert.Equal(0b0011_1111, rows[1]);

        //A row with nothing in it: every pixel light.
        Assert.Equal(0xFF, rows[(5 * 3) + 1]);
    }

    [Fact]
    public void Shapes_light_on_dark_is_the_same_picture_the_other_way_round()
    {
        var bitmap = MaskBitmaps.Rasterize(new[] { Square(0, 8, 2) }, Frame, 1);
        var chunks = Chunks(MaskBitmaps.Png(bitmap, shapesDark: false, micronsPerPixel: 1));

        using var inflated = new MemoryStream();

        using (var compressed = new ZLibStream(new MemoryStream(chunks.Single(chunk => chunk.Type == "IDAT").Data), CompressionMode.Decompress))
            compressed.CopyTo(inflated);

        Assert.Equal(0b1100_0000, inflated.ToArray()[1]);
    }

    [Fact]
    public void Masks_go_into_one_zip_by_layer_name()
    {
        var zip = MaskBitmaps.Zip(new[] { ("68_20_met1.png", new byte[] { 1, 2, 3 }), ("66_20_poly.png", new byte[] { 4 }) });

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        Assert.Equal(new[] { "68_20_met1.png", "66_20_poly.png" }, archive.Entries.Select(entry => entry.FullName));

        using var read = new MemoryStream();

        archive.Entries[0].Open().CopyTo(read);

        Assert.Equal(new byte[] { 1, 2, 3 }, read.ToArray());
    }

    [Fact]
    public void A_layer_file_is_named_by_its_numbers_and_its_name()
    {
        Assert.Equal("68_20.png", MaskBitmaps.FileNameFor(new Layer(new LayerKey(68, 20), "#000000")));
        Assert.Equal("68_20_met1.png", MaskBitmaps.FileNameFor(new Layer(new LayerKey(68, 20), "#000000") { Name = "met1" }));
        Assert.Equal("68_20_met_1_drawing.png", MaskBitmaps.FileNameFor(new Layer(new LayerKey(68, 20), "#000000") { Name = "met 1/drawing" }));
    }

    #endregion ***********************************************************************
}
