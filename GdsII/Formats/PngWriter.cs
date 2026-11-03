using System.Globalization;
using System.IO.Compression;

namespace GdsII
{
    ///
    ///A layout as a PNG: the picture <see cref="SvgWriter"/> writes, drawn into pixels instead of written out as markup.
    ///
    ///**For a picture that is only looked at** - a thumbnail beside a list of files or cells. As SVG a real chip is
    ///megabytes of text: 8.2 million characters for the RGB mixer, which the browser then parses, lays out and paints
    ///on the page's only thread. Pointing at one of the big designs in the Examples list froze the page for half a
    ///second to most of a second on a fast machine, and for a quarter of a second even when the markup was already
    ///made (measured 2026-10-17, local Release build). The same picture as pixels is a few hundred kilobytes at most,
    ///which the browser decodes away from the page, and drawing it costs less than writing the markup did.
    ///
    ///**The same picture.** Each layer is one group, as the SVG's path for it is: the fill - the layer's color, or
    ///its pattern over the washed ground - with the black outline over that, and the whole at the opacity, laid over
    ///the layers before it in the order the SVG writes its paths. Shapes on one layer overlap without doubling up, as
    ///the path's subpaths do. An open run is a line of the layer's color two pixels wide. Labels are not drawn: a
    ///thumbnail has none.
    ///
    ///**Coverage rather than samples.** A real layout at thumbnail size is mostly shapes smaller than a pixel.
    ///Sampled at the middles of pixels most of them would vanish and the rest would come and go with the frame; here
    ///each shape adds the share of a pixel it covers, so a block of standard cells is the tone it is in the SVG. This
    ///is the signed-area accumulation rasterizer of font-rs: an edge adds its area to the cells it crosses, and a sum
    ///along each row gives every pixel its coverage. A shape inside one pixel adds its area there and nothing else.
    ///
    ///**Rows run down as the SVG's y does**: the frame's least y is the top row, so the picture is the thumbnail's,
    ///pixel for pixel. <see cref="MaskBitmaps"/> runs the other way - a mask is drawn as the layout stands.
    ///
    public static class PngWriter
    {
        #region Constants *******************************************************************

        ///
        ///How thin an outline can be and still be drawn, in pixels.
        ///
        ///The SVG strokes every shape <see cref="SvgWriter.OutlineWidth"/> units wide, which on a cell is a line a
        ///third of a pixel wide and on a chip a thousandth of one. Below this it changes no pixel anybody could see,
        ///and stroking every edge of a chip is most of the work of drawing it.
        ///
        private const float LeastOutline = 0.05f;

        ///<summary>How many rows are put together, or written out, between offers to stop.</summary>
        private const int RowsAtATime = 16;

        #endregion **************************************************************************



        #region Drawing *********************************************************************

        ///<summary>The file, and how many pixels across and down it is.</summary>
        public sealed record Picture(byte[] Png, int Width, int Height);

        ///
        ///The layout inside <paramref name="frame"/>, fitted to <paramref name="most"/> pixels across and
        ///<paramref name="mostDown"/> down with its proportions kept, at <paramref name="opacity"/>.
        ///
        ///The frame is in the layout's own units, and the picture is exactly the frame: what is outside it is not
        ///drawn, and a part of it with nothing in it is clear. A frame with no area is a single clear pixel.
        ///
        public static Picture Build(FlattenedLayout layout, Bounds frame, int most, int mostDown, float opacity)
        {
            var painting = new Painting(layout, frame, most, mostDown, opacity);

            painting.Gather(int.MaxValue);

            for (int group = 0; group < painting.Groups; group++)
            {
                painting.Begin(group);
                painting.Draw(int.MaxValue);
                painting.Composite(int.MaxValue);
            }

            painting.Encode(int.MaxValue);

            return painting.Finished();
        }

        ///
        ///How many pixels across and down a picture of <paramref name="frame"/> is, fitted to <paramref name="most"/> by
        ///<paramref name="mostDown"/> with its proportions kept: what <see cref="Build"/> draws, and what a picture drawn as
        ///markup instead says it is, so the two land the same in a frame - see Preview.PictureDocument.
        ///
        public static (int Across, int Down) SizeFor(Bounds frame, int most, int mostDown)
        {
            var (_, across, down) = fit(frame, most, mostDown);

            return (across, down);
        }

        ///<summary>How many pixels one of the frame's units is, and the size that makes the picture. A frame with no area is one unit a pixel.</summary>
        private static (double Scale, int Across, int Down) fit(Bounds frame, int most, int mostDown)
        {
            double wide = Math.Max(1, frame.Width);
            double high = Math.Max(1, frame.Height);
            double scale = Math.Min(Math.Max(1, most) / wide, Math.Max(1, mostDown) / high);

            if (frame.IsEmpty)
                scale = 1;

            return (scale, Math.Max(1, (int)Math.Round(wide * scale)), Math.Max(1, (int)Math.Round(high * scale)));
        }

        ///
        ///The same picture, offering to stop as it goes: <paramref name="pause"/> is awaited between runs of shapes and
        ///of rows, so a caller on a single thread can let its page answer while a chip is drawn - and throw from it to
        ///give the picture up. See <see cref="Pauses"/>.
        ///
        ///**The same steps as <see cref="Build"/>**, a run at a time, so the two cannot draw different pictures - and
        ///each run is a plain loop rather than one inside this method, whose locals would live on the heap.
        ///
        public static async ValueTask<Picture> BuildAsync(FlattenedLayout layout, Bounds frame, int most, int mostDown, float opacity, Func<ValueTask> pause)
        {
            var painting = new Painting(layout, frame, most, mostDown, opacity);

            while (painting.Gather(Pauses.Every))
                await pause();

            for (int group = 0; group < painting.Groups; group++)
            {
                painting.Begin(group);

                while (painting.Draw(Pauses.Every))
                    await pause();

                while (painting.Composite(RowsAtATime))
                    await pause();
            }

            while (painting.Encode(RowsAtATime))
                await pause();

            return painting.Finished();
        }

        #endregion **************************************************************************



        #region One picture *****************************************************************

        ///
        ///One picture being drawn, kept between the runs of a picture drawn a run at a time: the shapes sorted into
        ///their groups, the coverage of the group being drawn, and the picture so far.
        ///
        private sealed class Painting
        {
            private readonly FlattenedLayout layout;
            private readonly float opacity;

            //Where the frame's corner is, in layout units, and how many pixels one unit is.
            private readonly double left;
            private readonly double top;
            private readonly double scale;

            private readonly int across;
            private readonly int down;

            //Two cells past the end of every row: an edge on the frame's right side writes one past the last pixel.
            private readonly int stride;

            //How wide an outline is in pixels, and whether that is wide enough to draw.
            private readonly float outline;

            //One group per layer, open runs apart, in the order the first shape of each appears - see SvgWriter.Writing.
            private readonly List<(Layer Layer, bool Open)> order = new List<(Layer Layer, bool Open)>();
            private readonly Dictionary<(LayerKey, bool), List<Element>> groups = new Dictionary<(LayerKey, bool), List<Element>>();
            private int gathered;
            private Bounds shapes = Bounds.Empty;
            private long tile;

            //The picture so far, four floats a pixel, the color already multiplied by its strength.
            private readonly float[] image;

            //The group being drawn: its fill's and its outline's signed area per cell, summed along a row into coverage.
            private readonly float[] fill;
            private readonly float[] edges;

            private Layer? layer;
            private bool open;
            private List<Element> members = new List<Element>();
            private int drawn;
            private int firstRow;
            private int lastRow;
            private int composited;

            //One shape's corners in pixels, reused shape to shape.
            private float[] xs = new float[64];
            private float[] ys = new float[64];
            private readonly float[] bandXs = new float[4];
            private readonly float[] bandYs = new float[4];

            //The file, written a run of rows at a time.
            private readonly MemoryStream packed = new MemoryStream();
            private ZLibStream? compressed;
            private byte[] line = Array.Empty<byte>();
            private int encoded;

            public Painting(FlattenedLayout layout, Bounds frame, int most, int mostDown, float opacity)
            {
                this.layout = layout;
                this.opacity = opacity;

                (scale, across, down) = fit(frame, most, mostDown);
                left = frame.Left;
                top = frame.Bottom;
                stride = across + 2;

                image = new float[across * down * 4];
                fill = new float[stride * down];
                edges = new float[stride * down];
                outline = (float)(SvgWriter.OutlineWidth * scale);

                //A frame with no area draws nothing, rather than everything piled into one pixel.
                if (frame.IsEmpty)
                    gathered = layout.Elements.Count;
            }

            public int Groups => order.Count;

            ///<summary>The next <paramref name="most"/> elements into their groups; true while there are more.</summary>
            public bool Gather(int most)
            {
                var elements = layout.Elements;
                int stop = (int)Math.Min(elements.Count, (long)gathered + most);

                for (; gathered < stop; gathered++)
                {
                    var element = elements[gathered];

                    if (element.Text is not null || element.Points.Count == 0)
                        continue;

                    shapes = shapes.Union(element.Box);

                    var key = (element.Layer.Key, element.IsOpen);

                    if (!groups.TryGetValue(key, out var group))
                    {
                        group = new List<Element>();

                        groups.Add(key, group);
                        order.Add((element.Layer, element.IsOpen));
                    }

                    group.Add(element);
                }

                //The pattern's tile, off the same extent the SVG's is - once every shape has been seen.
                tile = SvgWriter.TileFor(shapes);

                return gathered < elements.Count;
            }

            ///<summary>Starts on one group: nothing of it drawn yet.</summary>
            public void Begin(int group)
            {
                var (each, isOpen) = order[group];

                layer = each;
                open = isOpen;
                members = groups[(each.Key, isOpen)];
                drawn = 0;
                firstRow = down;
                lastRow = -1;
                composited = 0;
            }

            ///<summary>The next <paramref name="most"/> shapes of the group into its coverage; true while there are more.</summary>
            public bool Draw(int most)
            {
                int stop = (int)Math.Min(members.Count, (long)drawn + most);
                bool outlined = !open && outline >= LeastOutline;
                float run = SvgWriter.OpenRunPixels / 2f;

                for (; drawn < stop; drawn++)
                {
                    var points = members[drawn].Points;
                    int count = points.Count;

                    if (xs.Length < count)
                    {
                        xs = new float[count * 2];
                        ys = new float[count * 2];
                    }

                    for (int i = 0; i < count; i++)
                    {
                        xs[i] = (float)((points[i].X - left) * scale);
                        ys[i] = (float)((points[i].Y - top) * scale);
                    }

                    if (open)
                    {
                        for (int i = 1; i < count; i++)
                            band(fill, xs[i - 1], ys[i - 1], xs[i], ys[i], run);

                        continue;
                    }

                    ring(fill, xs, ys, count);

                    if (outlined)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            int next = (i + 1) % count;

                            band(edges, xs[i], ys[i], xs[next], ys[next], outline / 2);
                        }
                    }
                }

                return drawn < members.Count;
            }

            ///
            ///A closed ring's area into the cells it covers - always counted positive, whichever way round the ring
            ///was drawn, so two shapes on one layer add up rather than cancel where they overlap.
            ///
            private void ring(float[] into, float[] ringXs, float[] ringYs, int count)
            {
                if (count < 3)
                    return;

                float leastX = ringXs[0];
                float leastY = ringYs[0];
                float mostX = ringXs[0];
                float mostY = ringYs[0];
                double twice = 0;

                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;

                    twice += ((double)ringXs[i] * ringYs[next]) - ((double)ringXs[next] * ringYs[i]);

                    leastX = Math.Min(leastX, ringXs[i]);
                    leastY = Math.Min(leastY, ringYs[i]);
                    mostX = Math.Max(mostX, ringXs[i]);
                    mostY = Math.Max(mostY, ringYs[i]);
                }

                if (twice == 0)
                    return;

                int column = (int)MathF.Floor(leastX);
                int row = (int)MathF.Floor(leastY);

                //Inside one pixel: its area is its coverage there, and nothing more needs working out.
                if ((int)MathF.Floor(mostX) == column
                    && (int)MathF.Floor(mostY) == row
                    && column >= 0
                    && row >= 0
                    && column < across
                    && row < down)
                {
                    float area = (float)Math.Abs(twice / 2);
                    int at = (row * stride) + column;

                    into[at] += area;
                    into[at + 1] -= area;

                    touch(row, row);

                    return;
                }

                float sign = 1;

                if (twice < 0)
                    sign = -1;

                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;

                    accumulate(into, ringXs[i], ringYs[i], ringXs[next], ringYs[next], sign);
                }
            }

            ///<summary>A line along one edge, <paramref name="half"/> pixels either side of it, as the rectangle it covers.</summary>
            private void band(float[] into, float x0, float y0, float x1, float y1, float half)
            {
                float dx = x1 - x0;
                float dy = y1 - y0;
                float length = MathF.Sqrt((dx * dx) + (dy * dy));

                if (length < 1e-6f)
                    return;

                float nx = -dy / length * half;
                float ny = dx / length * half;

                bandXs[0] = x0 + nx;
                bandYs[0] = y0 + ny;
                bandXs[1] = x1 + nx;
                bandYs[1] = y1 + ny;
                bandXs[2] = x1 - nx;
                bandYs[2] = y1 - ny;
                bandXs[3] = x0 - nx;
                bandYs[3] = y0 - ny;

                ring(into, bandXs, bandYs, 4);
            }

            private void touch(int from, int to)
            {
                firstRow = Math.Min(firstRow, from);
                lastRow = Math.Max(lastRow, to);
            }

            ///
            ///One edge's signed area into the cells it crosses, a row at a time - font-rs's accumulation. Summed along a
            ///row afterwards, the cells give each pixel the share of it that the ring covers.
            ///
            ///Clipped to the frame: above and below it the rows are not there, and beside it the edge is held to the
            ///frame's side, which leaves what is inside the frame covered exactly as it would be.
            ///
            private void accumulate(float[] into, float x0, float y0, float x1, float y1, float sign)
            {
                if (y0 == y1)
                    return;

                float direction = sign;

                if (y0 > y1)
                {
                    (x0, x1) = (x1, x0);
                    (y0, y1) = (y1, y0);
                    direction = -sign;
                }

                x0 = Math.Clamp(x0, 0, across);
                x1 = Math.Clamp(x1, 0, across);

                float slope = (x1 - x0) / (y1 - y0);
                float x = x0;

                if (y0 < 0)
                {
                    x -= y0 * slope;
                    y0 = 0;
                }

                int start = (int)y0;
                int end = Math.Min(down, (int)MathF.Ceiling(y1));

                if (start >= end)
                    return;

                touch(start, end - 1);

                for (int y = start; y < end; y++)
                {
                    int row = y * stride;
                    float dy = Math.Min(y + 1, y1) - Math.Max(y, y0);
                    float next = Math.Clamp(x + (slope * dy), 0, across);
                    float d = dy * direction;

                    float low = Math.Min(x, next);
                    float high = Math.Max(x, next);
                    float lowFloor = MathF.Floor(low);
                    int lowCell = (int)lowFloor;
                    float highCeiling = MathF.Ceiling(high);
                    int highCell = (int)highCeiling;

                    if (highCell <= lowCell + 1)
                    {
                        //Within one cell: the part of it right of the edge's middle is covered.
                        float middle = (0.5f * (x + next)) - lowFloor;

                        into[row + lowCell] += d - (d * middle);
                        into[row + lowCell + 1] += d * middle;
                    }
                    else
                    {
                        //Across several: a triangle in the first and last, and an even share in each between.
                        float share = 1 / (high - low);
                        float lowFraction = low - lowFloor;
                        float first = 0.5f * share * (1 - lowFraction) * (1 - lowFraction);
                        float highFraction = high - highCeiling + 1;
                        float last = 0.5f * share * highFraction * highFraction;

                        into[row + lowCell] += d * first;

                        if (highCell == lowCell + 2)
                            into[row + lowCell + 1] += d * (1 - first - last);
                        else
                        {
                            float second = share * (1.5f - lowFraction);

                            into[row + lowCell + 1] += d * (second - first);

                            for (int cell = lowCell + 2; cell < highCell - 1; cell++)
                                into[row + cell] += d * share;

                            float beforeLast = second + ((highCell - lowCell - 3) * share);

                            into[row + highCell - 1] += d * (1 - beforeLast - last);
                        }

                        into[row + highCell] += d * last;
                    }

                    x = next;
                }
            }

            ///
            ///The next <paramref name="rows"/> rows of the group onto the picture; true while there are more. Its fill,
            ///the black outline over that, and the two at the opacity - one group, as the SVG's path is. Each cell is
            ///emptied as it is read, so the next group starts from nothing.
            ///
            public bool Composite(int rows)
            {
                if (layer is null || lastRow < firstRow)
                    return false;

                var (red, green, blue) = rgbOf(layer.Color);
                var (markRed, markGreen, markBlue) = rgbOf(SvgWriter.MarksColorOf(layer));
                bool patterned = !open && layer.Fill != LayerFill.None;
                bool outlined = !open && outline >= LeastOutline;
                float ground = SvgWriter.PatternGround;

                int from = firstRow + composited;
                int stop = (int)Math.Min(lastRow + 1L, (long)from + rows);

                for (int row = from; row < stop; row++)
                {
                    int cells = row * stride;
                    int pixels = row * across * 4;
                    float covered = 0;
                    float stroked = 0;

                    for (int column = 0; column < across; column++)
                    {
                        covered += fill[cells + column];
                        fill[cells + column] = 0;

                        if (outlined)
                        {
                            stroked += edges[cells + column];
                            edges[cells + column] = 0;
                        }

                        float c = Math.Min(1, Math.Abs(covered));
                        float s = Math.Min(1, Math.Abs(stroked));

                        if (c <= 0 && s <= 0)
                            continue;

                        //The fill's color and strength at this pixel, the color multiplied by the strength.
                        float fillRed = red;
                        float fillGreen = green;
                        float fillBlue = blue;
                        float fillStrength = 1;

                        if (patterned)
                        {
                            float marked = motifAt(layer.Fill, column, row);
                            float washed = (1 - marked) * ground;

                            fillRed = (marked * markRed) + (washed * red);
                            fillGreen = (marked * markGreen) + (washed * green);
                            fillBlue = (marked * markBlue) + (washed * blue);
                            fillStrength = marked + washed;
                        }

                        //The outline over the fill, then the group at its opacity, then the group over the picture.
                        float kept = (1 - s) * c;
                        float strength = (s + (kept * fillStrength)) * opacity;
                        float under = 1 - strength;
                        int at = pixels + (column * 4);

                        image[at] = (kept * fillRed * opacity) + (image[at] * under);
                        image[at + 1] = (kept * fillGreen * opacity) + (image[at + 1] * under);
                        image[at + 2] = (kept * fillBlue * opacity) + (image[at + 2] * under);
                        image[at + 3] = strength + (image[at + 3] * under);
                    }

                    //And what an edge on the frame's right side wrote past the last pixel.
                    fill[cells + across] = 0;
                    fill[cells + across + 1] = 0;
                    edges[cells + across] = 0;
                    edges[cells + across + 1] = 0;
                }

                composited = stop - firstRow;

                return stop <= lastRow;
            }

            ///
            ///How much of a pixel the pattern's marks cover, from four samples in it - the motifs of
            ///SvgWriter.appendMotif, on the same tile, which starts at the layout's origin as the SVG's
            ///userSpaceOnUse pattern does.
            ///
            private float motifAt(LayerFill pattern, int column, int row)
            {
                int hits = 0;

                for (int sample = 0; sample < 4; sample++)
                {
                    double x = left + ((column + 0.25 + (0.5 * (sample & 1))) / scale);
                    double y = top + ((row + 0.25 + (0.5 * (sample >> 1))) / scale);

                    if (inMotif(pattern, wrapped(x), wrapped(y)))
                        hits++;
                }

                return hits / 4f;
            }

            private double wrapped(double value)
            {
                double inTile = value % tile;

                if (inTile < 0)
                    inTile += tile;

                return inTile;
            }

            private bool inMotif(LayerFill pattern, double u, double v)
            {
                double whole = tile;
                double half = whole / 2;
                double weight = whole / 6;

                if (pattern == LayerFill.Dots)
                    return ((u - half) * (u - half)) + ((v - half) * (v - half)) <= (whole / 4) * (whole / 4);

                if (pattern == LayerFill.Squares)
                    return u >= half && v >= half;

                if (pattern == LayerFill.Grid)
                    return u <= weight / 2 || u >= whole - (weight / 2) || v <= weight / 2 || v >= whole - (weight / 2);

                if (pattern == LayerFill.Dashes)
                    return u <= half && Math.Abs(v - half) <= weight / 2;

                bool forward = nearDiagonal(u + v, whole, weight);
                bool backward = nearDiagonal(u - v, whole, weight);

                if (pattern == LayerFill.Diagonal)
                    return forward;

                if (pattern == LayerFill.BackDiagonal)
                    return backward;

                if (pattern == LayerFill.CrossHatch)
                    return forward || backward;

                return false;
            }

            ///<summary>Whether one of the 45-degree lines through every multiple of the tile passes within half a weight.</summary>
            private static bool nearDiagonal(double along, double whole, double weight)
            {
                double off = along % whole;

                if (off < 0)
                    off += whole;

                double distance = Math.Min(off, whole - off) / Math.Sqrt(2);

                return distance <= weight / 2;
            }

            ///
            ///A color the way a layer holds one - `#rrggbb`, or the short `#rgb` - as three fractions. Anything else is
            ///drawn gray rather than not at all, the way an SVG with a color it cannot read leaves a shape black.
            ///
            private static (float Red, float Green, float Blue) rgbOf(string color)
            {
                string hex = color.Trim();

                if (hex.StartsWith('#'))
                    hex = hex.Substring(1);

                if (hex.Length == 3)
                    hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);

                if (hex.Length != 6 && hex.Length != 8)
                    return (0.5f, 0.5f, 0.5f);

                if (!int.TryParse(hex.AsSpan(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                    return (0.5f, 0.5f, 0.5f);

                return (((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f);
            }

            ///
            ///The next <paramref name="rows"/> rows into the file; true while there are more.
            ///
            ///Eight bits a channel with an alpha, unfiltered: measured on the designs, the row filters made the files
            ///bigger rather than smaller - a chip at this size is detail in every pixel, which none of them predicts -
            ///and on a cell they saved a few kilobytes of a few tens.
            ///
            public bool Encode(int rows)
            {
                if (compressed is null)
                {
                    compressed = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true);
                    line = new byte[(across * 4) + 1];
                }

                int stop = (int)Math.Min(down, (long)encoded + rows);

                for (int row = encoded; row < stop; row++)
                {
                    int pixels = row * across * 4;

                    line[0] = 0;

                    for (int column = 0; column < across; column++)
                    {
                        int at = pixels + (column * 4);
                        int to = 1 + (column * 4);
                        float strength = image[at + 3];

                        if (strength <= 0)
                        {
                            line[to] = 0;
                            line[to + 1] = 0;
                            line[to + 2] = 0;
                            line[to + 3] = 0;

                            continue;
                        }

                        //A PNG's color is not multiplied by its alpha, so divided back out.
                        line[to] = toByte(image[at] / strength);
                        line[to + 1] = toByte(image[at + 1] / strength);
                        line[to + 2] = toByte(image[at + 2] / strength);
                        line[to + 3] = toByte(strength);
                    }

                    compressed.Write(line, 0, line.Length);
                }

                encoded = stop;

                return encoded < down;
            }

            private static byte toByte(float value)
            {
                return (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
            }

            public Picture Finished()
            {
                compressed?.Dispose();

                using var file = new MemoryStream();

                file.Write(PngFile.Signature);

                var header = new byte[13];

                PngFile.BigEndian(header, 0, (uint)across);
                PngFile.BigEndian(header, 4, (uint)down);

                //Eight bits a channel, color with an alpha, the standard compression and filtering, not interlaced.
                header[8] = 8;
                header[9] = 6;

                PngFile.Chunk(file, "IHDR", header);
                PngFile.Chunk(file, "IDAT", packed.ToArray());
                PngFile.Chunk(file, "IEND", Array.Empty<byte>());

                return new Picture(file.ToArray(), across, down);
            }
        }

        #endregion **************************************************************************
    }
}
