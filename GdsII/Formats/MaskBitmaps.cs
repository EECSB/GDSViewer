using System.IO.Compression;

namespace GdsII
{
    ///<summary>
    ///A layout as bitmaps, one a layer, for a mask writer that takes pictures rather than polygons.
    ///
    ///**Who wants this.** A laser writer, a maskless aligner, a DMD exposure system, a 3D printer making a
    ///shadow mask - much of the equipment a lab or a small fab actually has takes a black and white image at a
    ///stated pixel size, not a GDSII file. Getting one out of a layout normally means a script; this is the
    ///script.
    ///
    ///**One bit a pixel, and no blending.** A pixel is exposed or it is not, so each is decided by whether its
    ///middle is inside a shape - the rule every rasterizing mask writer uses - and there is no anti-aliasing
    ///to put a gray edge on something that has no gray. Written as a one-bit grayscale PNG, which every tool
    ///opens and which compresses a mostly empty mask to almost nothing.
    ///
    ///**Rasterized here rather than by the browser's canvas**, which draws anti-aliased and caps how big a
    ///canvas may be. These are exact, testable, and as large as memory allows.
    ///</summary>
    public static class MaskBitmaps
    {
        #region Constants *******************************************************************

        ///<summary>
        ///The most pixels one layer's bitmap may have: a gigapixel, an eighth of a gigabyte at a bit a pixel,
        ///which is a 3 cm die at a micron a pixel. Past it a coarser pixel is the answer.
        ///</summary>
        public const long MostPixels = 1_000_000_000;

        #endregion **************************************************************************



        #region Bitmaps *********************************************************************

        ///<summary>
        ///One layer as pixels: <see cref="Width"/> across and <see cref="Height"/> down, a bit each, rows top
        ///to bottom and packed eight pixels a byte with the leftmost in the high bit - which is also how a PNG
        ///row of one-bit pixels is laid out, so nothing is repacked to write it.
        ///</summary>
        public sealed class Bitmap
        {
            public Bitmap(int width, int height)
            {
                Width = width;
                Height = height;
                Stride = (width + 7) / 8;
                Bits = new byte[(long)Stride * height];
            }

            public int Width { get; }

            public int Height { get; }

            ///<summary>Bytes a row.</summary>
            public int Stride { get; }

            public byte[] Bits { get; }

            public bool this[int x, int y]
            {
                get { return (Bits[((long)y * Stride) + (x >> 3)] & (0x80 >> (x & 7))) != 0; }
            }

            ///<summary>How many pixels are set, for a test or a summary.</summary>
            public long Count()
            {
                long count = 0;

                foreach (byte value in Bits)
                    count += System.Numerics.BitOperations.PopCount(value);

                return count;
            }

            internal void Fill(int y, int fromX, int toX)
            {
                long row = (long)y * Stride;

                for (int x = fromX; x < toX; x++)
                    Bits[row + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            }
        }

        ///
        ///How many pixels a frame comes to at this pixel size, across and down.
        ///
        ///**Whole pixels, rounded up**, so the bitmap covers the whole frame and a shape on its far edge is not
        ///cut off by a fraction of a pixel.
        ///
        public static (long Width, long Height) SizeOf(Bounds frame, double unitsPerPixel)
        {
            if (frame.IsEmpty || !(unitsPerPixel > 0))
                return (0, 0);

            return ((long)Math.Ceiling(frame.Width / unitsPerPixel), (long)Math.Ceiling(frame.Height / unitsPerPixel));
        }

        ///
        ///The shapes as a bitmap over <paramref name="frame"/>, <paramref name="unitsPerPixel"/> to a pixel.
        ///
        ///**A pixel is set when its middle is inside a shape.** Each shape is filled on its own by the even-odd
        ///rule and the bitmaps of all of them are laid over one another - which is the union a mask is, and
        ///handles a keyhole exactly, since its channel has no width for a middle to fall in.
        ///
        ///Rows run down from the top of the frame, the way an image does; the layout's Y runs up, so the top
        ///row is the frame's top edge.
        ///
        public static Bitmap Rasterize(IEnumerable<IReadOnlyList<Element.Point>> shapes, Bounds frame, double unitsPerPixel)
        {
            var (width, height) = SizeOf(frame, unitsPerPixel);

            if (width * height > MostPixels || width > int.MaxValue || height > int.MaxValue)
                throw new ArgumentException(FormattableString.Invariant(
                    $"{width:N0} by {height:N0} pixels is more than one bitmap can hold. Choose a coarser pixel."));

            var bitmap = new Bitmap((int)width, (int)height);

            if (width == 0 || height == 0)
                return bitmap;

            var crossings = new List<double>();

            foreach (var shape in shapes)
            {
                if (shape.Count < 3)
                    continue;

                var box = Bounds.Of(shape);

                //The rows whose middles the shape's box spans: the middle of row r is at top - (r + 0.5) pixels.
                int fromRow = (int)Math.Clamp(Math.Floor((((double)frame.Top - box.Top) / unitsPerPixel) - 0.5), 0, bitmap.Height - 1);
                int toRow = (int)Math.Clamp(Math.Ceiling((((double)frame.Top - box.Bottom) / unitsPerPixel) - 0.5), -1, bitmap.Height - 1);

                for (int row = fromRow; row <= toRow; row++)
                {
                    double y = frame.Top - ((row + 0.5) * unitsPerPixel);

                    crossings.Clear();

                    for (int i = 0; i < shape.Count; i++)
                    {
                        var from = shape[i];
                        var to = shape[(i + 1) % shape.Count];

                        //Half-open, so a corner exactly on a row's middle is crossed once rather than twice.
                        if ((from.Y <= y && to.Y > y) || (to.Y <= y && from.Y > y))
                            crossings.Add(from.X + ((y - from.Y) * (to.X - (double)from.X) / (to.Y - (double)from.Y)));
                    }

                    if (crossings.Count < 2)
                        continue;

                    crossings.Sort();

                    for (int k = 0; k + 1 < crossings.Count; k += 2)
                    {
                        //The pixels whose middles fall between the two crossings: the middle of column c is at
                        //left + (c + 0.5) pixels.
                        int fromColumn = (int)Math.Ceiling(((crossings[k] - frame.Left) / unitsPerPixel) - 0.5);
                        int toColumn = (int)Math.Ceiling(((crossings[k + 1] - frame.Left) / unitsPerPixel) - 0.5);

                        fromColumn = Math.Clamp(fromColumn, 0, bitmap.Width);
                        toColumn = Math.Clamp(toColumn, 0, bitmap.Width);

                        if (toColumn > fromColumn)
                            bitmap.Fill(row, fromColumn, toColumn);
                    }
                }
            }

            return bitmap;
        }

        #endregion **************************************************************************



        #region Writing *********************************************************************

        ///
        ///The bitmap as a one-bit grayscale PNG.
        ///
        ///<paramref name="shapesDark"/> says which way round: shapes black on white, which is how a layout is
        ///usually printed and what most writers mean by "exposed", or white on black for one that wants the
        ///field dark. <paramref name="micronsPerPixel"/> goes into the file's pHYs chunk, so a tool that reads it
        ///knows the scale without being told.
        ///
        public static byte[] Png(Bitmap bitmap, bool shapesDark, double micronsPerPixel)
        {
            using var file = new MemoryStream();

            file.Write(PngFile.Signature);

            var header = new byte[13];

            PngFile.BigEndian(header, 0, (uint)bitmap.Width);
            PngFile.BigEndian(header, 4, (uint)bitmap.Height);

            //One bit a pixel, grayscale, the standard compression and filtering, not interlaced.
            header[8] = 1;
            header[9] = 0;

            PngFile.Chunk(file, "IHDR", header);

            //Pixels per meter, which is what PNG measures a pixel in.
            if (micronsPerPixel > 0)
            {
                var physical = new byte[9];
                uint perMeter = (uint)Math.Clamp(Math.Round(1_000_000 / micronsPerPixel), 1, uint.MaxValue);

                PngFile.BigEndian(physical, 0, perMeter);
                PngFile.BigEndian(physical, 4, perMeter);
                physical[8] = 1;

                PngFile.Chunk(file, "pHYs", physical);
            }

            using (var image = new MemoryStream())
            {
                using (var compressed = new ZLibStream(image, CompressionLevel.Optimal, leaveOpen: true))
                {
                    var row = new byte[bitmap.Stride + 1];

                    for (int y = 0; y < bitmap.Height; y++)
                    {
                        //No filter: a row of one-bit pixels is runs of the same byte, which deflate takes care of.
                        row[0] = 0;

                        Array.Copy(bitmap.Bits, (long)y * bitmap.Stride, row, 1, bitmap.Stride);

                        //In grayscale a one is white, so a dark shape is a set bit turned off.
                        if (shapesDark)
                        {
                            for (int i = 1; i < row.Length; i++)
                                row[i] = (byte)~row[i];
                        }

                        compressed.Write(row, 0, row.Length);
                    }
                }

                PngFile.Chunk(file, "IDAT", image.ToArray());
            }

            PngFile.Chunk(file, "IEND", Array.Empty<byte>());

            return file.ToArray();
        }

        ///<summary>Files gathered into one zip, stored as they are - a PNG is compressed already.</summary>
        public static byte[] Zip(IEnumerable<(string Name, byte[] Bytes)> files)
        {
            using var zip = new MemoryStream();

            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, bytes) in files)
                {
                    var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);

                    using var stream = entry.Open();

                    stream.Write(bytes, 0, bytes.Length);
                }
            }

            return zip.ToArray();
        }

        ///
        ///One layer's file name in a set of masks: its number and datatype, and its name when it has one -
        ///`68_20_met1.png` - so the files sort by layer and say what they are.
        ///
        public static string FileNameFor(Layer layer)
        {
            //Invariant: a file name is not prose, and a data type the file did not give is -1.
            string name = FormattableString.Invariant($"{layer.Number}_{layer.DataType}");

            if (!string.IsNullOrWhiteSpace(layer.Name))
            {
                var safe = new System.Text.StringBuilder();

                foreach (char each in layer.Name.Trim())
                {
                    if (char.IsLetterOrDigit(each) || each == '-' || each == '.')
                        safe.Append(each);
                    else
                        safe.Append('_');
                }

                name += "_" + safe;
            }

            return name + ".png";
        }

        #endregion **************************************************************************
    }
}
