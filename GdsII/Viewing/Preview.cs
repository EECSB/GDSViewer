using System.Globalization;

namespace GdsII
{
    ///
    ///A drawing of a layout small enough to sit beside a list of them.
    ///
    ///**The frame is the whole of the problem.** Drawing the shapes is <see cref="SvgWriter"/>'s job and is
    ///the same here as anywhere; what a thumbnail needs on top of that is a viewBox, and a viewBox is not
    ///something a layout carries. A cell is drawn wherever its coordinates put it - which for a cell placed
    ///far from the origin is nowhere near the middle of anything - so a frame worked out from the shapes
    ///themselves is the difference between a picture and an empty square.
    ///
    ///Here rather than in whichever page happens to need it, because three lists want the same picture: the
    ///bundled examples, the files opened before, and the cells of the library that is open. The first two
    ///had a copy of this each, which is two chances for a thumbnail to be framed differently from the one
    ///beside it.
    ///
    public static class Preview
    {
        ///
        ///What to draw, and the box to draw it in.
        ///
        ///Empty markup and a unit box for a layout with nothing in it, which is a real thing to be handed: a
        ///cell may hold nothing at all, and a library is allowed to contain one.
        ///
        ///<paramref name="named"/> is what this is a picture *of* - a cell or a file - and is what keeps it
        ///from painting over the drawing beside it. A thumbnail is never alone on the page: the styles and
        ///the pattern definitions a picture carries are resolved document-wide however deeply the SVG
        ///holding them is nested, so without a name of its own a preview of one cell redrew the whole
        ///layout at the preview's opacity. See SvgWriter.PictureToken.
        ///
        public static (string Markup, string ViewBox) Of(FlattenedLayout layout, float opacity, string named = "")
        {
            //Empty for an unnamed preview rather than a token built from nothing, since an empty token is what
            //the writer reads as "this picture is the only one on the page".
            string token = "";

            if (named.Length > 0)
                token = SvgWriter.PictureToken(named);

            string markup = SvgWriter.Build(
                layout,
                SvgWriter.AllLayers(layout),
                opacity,
                new HashSet<LayerKey>(),
                null,
                null,
                0,
                token);

            if (BoxOf(layout) is null)
                return ("", EmptyFrame);

            return (markup, FrameOf(layout));
        }

        ///
        ///A whole layout as an SVG file of its own: the drawing, framed to fit, in a document that opens in a
        ///browser or an illustration program as it stands.
        ///
        ///**Framed the way a thumbnail is**, with the same margin off the widest side, rather than the way the
        ///view happens to be framed: this is what the download in the bar writes, and that writes the file -
        ///all of it - where the camera on the canvas keeps what is on screen. In the 3D and text views there is
        ///no picture on screen to take in any case.
        ///
        ///<paramref name="visibleLayers"/> and <paramref name="labeledLayers"/> are the rows' own switches, so
        ///a layer put away in the list is out of the picture as it is out of the view.
        ///
        ///<paramref name="yUp"/> turns the picture the format's way up - see below; the download leaves it as the view draws it.
        ///
        public static string Document(FlattenedLayout layout, IReadOnlySet<LayerKey> visibleLayers, IReadOnlySet<LayerKey> labeledLayers, float opacity, bool yUp = false)
        {
            string markup = SvgWriter.Build(layout, visibleLayers, opacity, labeledLayers);
            string frame = FrameOf(layout);

            if (!yUp)
                return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{frame}\">{markup}</svg>";

            //
            //**The format's way up**, for a file to read as KLayout draws the layout - which is what `gds svg`
            //writes, and used to frame on its own. The view and the download draw y downward, as SVG does; this
            //turns the picture over about the middle of its frame, y' = top + bottom - y, so the frame stays where
            //it is with the picture inside it.
            //
            string middle = topAndBottomOf(layout).ToString(CultureInfo.InvariantCulture);

            return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{frame}\"><g transform=\"translate(0,{middle}) scale(1,-1)\">{markup}</g></svg>";
        }

        ///<summary>The frame's least and greatest y added, which is what a flip about its middle translates by.</summary>
        private static long topAndBottomOf(FlattenedLayout layout)
        {
            //The unit square, see EmptyFrame.
            if (BoxOf(layout) is not (int left, int top, int width, int height))
                return 1;

            var frame = framed(left, top, width, height);

            return (long)frame.Bottom + frame.Top;
        }

        ///<summary>The box a layout with nothing in it is drawn in. One unit square, since a viewBox of zero draws nothing.</summary>
        private const string EmptyFrame = "0 0 1 1";

        ///
        ///The viewBox that frames everything drawn, with a margin - or a unit square when nothing is.
        ///
        private static string FrameOf(FlattenedLayout layout)
        {
            if (BoxOf(layout) is not (int left, int top, int width, int height))
                return EmptyFrame;

            var frame = framed(left, top, width, height);

            return string.Join(' ',
                frame.Left.ToString(CultureInfo.InvariantCulture),
                frame.Bottom.ToString(CultureInfo.InvariantCulture),
                frame.Width.ToString(CultureInfo.InvariantCulture),
                frame.Height.ToString(CultureInfo.InvariantCulture));
        }

        ///
        ///A box with the thumbnail's margin round it, as the frame a picture is drawn in.
        ///
        ///**A margin off the widest side**, so a tall cell and a wide one are inset by the same amount. Taken
        ///from the larger of the two rather than from each: a fifth of the height either side of something one
        ///unit wide is a hairline in the middle of a lot of nothing, and the same rule applied per axis frames a
        ///via loosely and a bus tightly.
        ///
        ///The frame's Bottom is its least y, which a viewBox calls its top - see BoxOf.
        ///
        private static Bounds framed(int left, int top, int width, int height)
        {
            int margin = Math.Max(1, Math.Max(width, height) / 20);

            return new Bounds(left - margin, top - margin, left + width + margin, top + height + margin);
        }

        ///
        ///The corners of everything drawn, or null when nothing is.
        ///
        ///Off the points - each element's box, which is its points' - rather than off
        ///<see cref="Measure.BoundsOf(FlattenedLayout)"/>, which answers for a reading and would have to be converted
        ///back. Width and height are floored at one so that a box is never zero on a side - a cell that is one straight
        ///line has no height at all, and a viewBox with a zero in it draws nothing.
        ///
        ///**Top is the least y**, as a viewBox has it: the thumbnail is drawn the way SVG draws, y running down.
        ///
        public static (int Left, int Top, int Width, int Height)? BoxOf(FlattenedLayout layout)
        {
            return boxFrom(extentOf(layout.Elements, 0, layout.Elements.Count, Bounds.Empty));
        }

        ///<summary>What a run of elements adds to an extent found so far.</summary>
        private static Bounds extentOf(List<Element> elements, int from, int count, Bounds extent)
        {
            int stop = Math.Min(elements.Count, from + count);

            for (int i = from; i < stop; i++)
                extent = extent.Union(elements[i].Box);

            return extent;
        }

        private static (int Left, int Top, int Width, int Height)? boxFrom(Bounds extent)
        {
            if (extent.IsEmpty)
                return null;

            return (extent.Left, extent.Bottom, (int)Math.Max(1, extent.Width), (int)Math.Max(1, extent.Height));
        }

        #region The picture as pixels *******************************************************

        ///
        ///The most pixels a picture is drawn across or down.
        ///
        ///A picture is four floats a pixel while it is being drawn, so one the size of a screen at twice its density
        ///would be a hundred megabytes for a thumbnail. This is a large window's preview at a little over a pixel per
        ///pixel, and the browser scales it the rest of the way.
        ///
        public const int LargestPicture = 1600;

        ///
        ///The thumbnail as pixels rather than markup - see <see cref="PngWriter"/> for why a list wants those - framed
        ///as <see cref="Of"/> frames it and fitted to <paramref name="most"/> by <paramref name="mostDown"/> pixels.
        ///
        ///Null for a layout with nothing in it, the same thing an empty markup is from <see cref="Of"/>.
        ///
        public static PngWriter.Picture? Picture(FlattenedLayout layout, int most, int mostDown, float opacity)
        {
            if (BoxOf(layout) is not (int left, int top, int width, int height))
                return null;

            return PngWriter.Build(layout, framed(left, top, width, height), fitted(most), fitted(mostDown), opacity);
        }

        ///
        ///The same, offering to stop as it goes: <paramref name="pause"/> is awaited between runs of the walk that finds
        ///the frame and of the drawing - see <see cref="PngWriter.BuildAsync"/>.
        ///
        public static async ValueTask<PngWriter.Picture?> PictureAsync(FlattenedLayout layout, int most, int mostDown, float opacity, Func<ValueTask> pause)
        {
            var elements = layout.Elements;
            var extent = Bounds.Empty;

            for (int from = 0; from < elements.Count; from += Pauses.Every)
            {
                extent = extentOf(elements, from, Pauses.Every, extent);

                await pause();
            }

            if (boxFrom(extent) is not (int left, int top, int width, int height))
                return null;

            return await PngWriter.BuildAsync(layout, framed(left, top, width, height), fitted(most), fitted(mostDown), opacity, pause);
        }

        private static int fitted(int pixels)
        {
            return Math.Clamp(pixels, 1, LargestPicture);
        }

        #endregion **************************************************************************



        #region The picture as markup *******************************************************

        ///
        ///The thumbnail as an SVG document of its own, for an img to show: the markup <see cref="Of"/> writes, in the frame
        ///it gives, and saying it is the size <see cref="Picture"/> would have drawn - see <see cref="PngWriter.SizeFor"/> -
        ///so either kind lands the same in a frame.
        ///
        ///**For a small layout**, whose markup costs less than its pixels: a PNG is every pixel of the frame for every layer,
        ///however little is drawn there, where markup is what the layout holds. The app chooses which - see PreviewPictures.
        ///Unnamed, since a document of its own is the only picture in it. Null for a layout with nothing in it, as
        ///<see cref="Picture"/> is.
        ///
        public static string? PictureDocument(FlattenedLayout layout, int most, int mostDown, float opacity)
        {
            if (BoxOf(layout) is not (int left, int top, int width, int height))
                return null;

            var (across, down) = PngWriter.SizeFor(framed(left, top, width, height), fitted(most), fitted(mostDown));
            var (markup, viewBox) = Of(layout, opacity);

            return FormattableString.Invariant($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"{viewBox}\" width=\"{across}\" height=\"{down}\">{markup}</svg>");
        }

        #endregion **************************************************************************
    }
}
