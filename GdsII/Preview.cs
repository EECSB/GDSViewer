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

            if (BoxOf(layout) is not (int left, int top, int width, int height))
                return ("", "0 0 1 1");

            //
            //A margin off the widest side, so a tall cell and a wide one are inset by the same amount.
            //
            //Taken from the larger of the two rather than from each: a fifth of the height either side of
            //something one unit wide is a hairline in the middle of a lot of nothing, and the same rule
            //applied per axis frames a via loosely and a bus tightly.
            //
            int margin = Math.Max(1, Math.Max(width, height) / 20);

            string viewBox = string.Join(' ',
                (left - margin).ToString(CultureInfo.InvariantCulture),
                (top - margin).ToString(CultureInfo.InvariantCulture),
                (width + (margin * 2)).ToString(CultureInfo.InvariantCulture),
                (height + (margin * 2)).ToString(CultureInfo.InvariantCulture));

            return (markup, viewBox);
        }

        ///
        ///The corners of everything drawn, or null when nothing is.
        ///
        ///Off the points rather than off <see cref="Measure.BoundsOf(FlattenedLayout)"/>, which answers for
        ///a reading and would have to be converted back. Width and height are floored at one so that a box
        ///is never zero on a side - a cell that is one straight line has no height at all, and a viewBox
        ///with a zero in it draws nothing.
        ///
        public static (int Left, int Top, int Width, int Height)? BoxOf(FlattenedLayout layout)
        {
            int left = 0, top = 0, right = 0, bottom = 0;
            bool any = false;

            foreach (var element in layout.Elements)
            {
                foreach (var point in element.Points)
                {
                    if (!any)
                    {
                        left = right = point.X;
                        top = bottom = point.Y;
                        any = true;

                        continue;
                    }

                    left = Math.Min(left, point.X);
                    right = Math.Max(right, point.X);
                    top = Math.Min(top, point.Y);
                    bottom = Math.Max(bottom, point.Y);
                }
            }

            if (!any)
                return null;

            return (left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
        }
    }
}
