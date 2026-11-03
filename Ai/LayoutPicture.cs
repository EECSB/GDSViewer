using System.Globalization;
using System.Text;
using GdsII;

namespace GDSViewer.Ai
{
    ///<summary>A picture ready to be drawn: the SVG document, its size in pixels, and what the model is told about it.</summary>
    public sealed record LayoutPictureDraft(string Document, int Width, int Height, string Says);

    ///
    ///The picture <c>look</c> hands the model: a cell, drawn the way the 2D view draws it, and said in words.
    ///
    ///**The 2D view's own drawing.** The markup is <see cref="SvgWriter"/>'s, the same as the view and the camera's
    ///PNG, so the model sees the colors, fills and labels the person sees - and, since this app maps GDSII's upward Y
    ///onto the screen's downward one, the same way up. The words say so, because the tools count Y upward and a model
    ///that took the top of the picture for the largest Y would put a shape on the wrong side of everything.
    ///
    ///**Said as well as drawn.** A picture alone gives the model no scale and no key: the words name the area, how
    ///many microns a pixel is and which color is which layer, so what it sees can be turned into numbers it can call
    ///the tools with.
    ///
    ///Pure, so all of that is tested without a browser; the browser only turns the document into pixels.
    ///
    public static class LayoutPicture
    {
        ///
        ///The longer side, in pixels.
        ///
        ///A model reads a picture at about this size whatever it is sent - Anthropic scales anything much larger
        ///down to it, and charges by the pixel up to there - so a bigger picture costs more and shows nothing more.
        ///
        public const int LongestSide = 1024;

        ///<summary>How opaque the fills are: enough to read a layer by its color, and to see one through another.</summary>
        public const float Opacity = 0.6f;

        ///<summary>The most layers the key names, past which it says how many more there are.</summary>
        private const int MostNamed = 40;

        ///
        ///The narrowest a picture is allowed to be against its length, as a fraction.
        ///
        ///A cell that is one long bus would otherwise come out a few pixels high, which is no picture at all. Such an
        ///area is widened about its middle until it is a twelfth as tall as it is long, and the words say what area was
        ///actually drawn.
        ///
        private const double Slimmest = 1.0 / 12;

        ///
        ///The picture of a cell, framed on an area in microns or on everything the cell draws - or null, with why not.
        ///
        ///<paramref name="hidden"/> is what the person has hidden in the layer list, which is left out here too, so the
        ///model is looking at what they are - and the words name what was left out. Every other layer is drawn, one
        ///the list has not caught up with included.
        ///
        public static LayoutPictureDraft? Draft(
            GDS gds,
            string cell,
            (double Left, double Bottom, double Right, double Top)? area,
            double micronsPerUnit,
            IReadOnlySet<LayerKey>? hidden,
            out string why)
        {
            why = "";

            if (Hierarchy.Named(gds, cell) is null)
            {
                why = $"There is no cell called {cell}.";

                return null;
            }

            var layout = GdsFlattener.Flatten(gds, cell);

            if (layout.Elements.Count == 0)
            {
                why = $"{cell} has nothing in it to look at.";

                return null;
            }

            if (!(micronsPerUnit > 0) || !double.IsFinite(micronsPerUnit))
                micronsPerUnit = 1;

            //The frame, in database units: the area asked for, or everything drawn with a margin round it.
            double left, bottom, right, top;

            if (area is (double l, double b, double r, double t))
            {
                left = l / micronsPerUnit;
                bottom = b / micronsPerUnit;
                right = r / micronsPerUnit;
                top = t / micronsPerUnit;
            }
            else
            {
                if (Preview.BoxOf(layout) is not (int boxLeft, int boxTop, int boxWidth, int boxHeight))
                {
                    why = $"{cell} has nothing in it to look at.";

                    return null;
                }

                double margin = Math.Max(1, Math.Max(boxWidth, boxHeight) / 20.0);

                left = boxLeft - margin;
                bottom = boxTop - margin;
                right = boxLeft + boxWidth + margin;
                top = boxTop + boxHeight + margin;
            }

            widen(ref left, ref right, ref bottom, ref top);

            double across = right - left;
            double down = top - bottom;

            if (!(across > 0) || !(down > 0) || !double.IsFinite(across) || !double.IsFinite(down))
            {
                why = "That area has no size to draw.";

                return null;
            }

            int width = LongestSide;
            int height = Math.Max(1, (int)Math.Round(LongestSide * down / across));

            if (down > across)
            {
                height = LongestSide;
                width = Math.Max(1, (int)Math.Round(LongestSide * across / down));
            }

            double unitsPerPixel = across / width;

            var window = new Bounds(clamp(Math.Floor(left)), clamp(Math.Floor(bottom)), clamp(Math.Ceiling(right)), clamp(Math.Ceiling(top)));

            var drawn = new HashSet<LayerKey>(SvgWriter.AllLayers(layout));

            if (hidden is not null)
                drawn.ExceptWith(hidden);

            //Labels where the view would draw them at this scale, which is where they can be read: further out, a
            //label is a smudge over the shapes it names.
            IReadOnlySet<LayerKey> labeled = new HashSet<LayerKey>();

            if (SvgWriter.LabelsLegibleAt(unitsPerPixel))
                labeled = drawn;

            //Everything in the window, however small. The view leaves out what is under a pixel to keep panning
            //quick, which a picture drawn once has no need of - and on a cell zoomed far out that rule can leave
            //nothing at all.
            string markup = SvgWriter.Build(layout, drawn, Opacity, labeled, null, window, 0);

            string viewBox = string.Join(' ', number(left), number(bottom), number(across), number(down));
            string document = $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"{viewBox}\">{markup}</svg>";

            string says = describe(layout, cell, window, drawn, left, bottom, right, top, width, height, micronsPerUnit);

            return new LayoutPictureDraft(document, width, height, says);
        }

        ///<summary>Widens the shorter side about its middle until the frame is no slimmer than <see cref="Slimmest"/>.</summary>
        private static void widen(ref double left, ref double right, ref double bottom, ref double top)
        {
            double across = right - left;
            double down = top - bottom;

            if (down < across * Slimmest)
            {
                double middle = (bottom + top) / 2;
                double half = across * Slimmest / 2;

                bottom = middle - half;
                top = middle + half;
            }
            else if (across < down * Slimmest)
            {
                double middle = (left + right) / 2;
                double half = down * Slimmest / 2;

                left = middle - half;
                right = middle + half;
            }
        }

        private static string describe(
            FlattenedLayout layout,
            string cell,
            Bounds window,
            IReadOnlySet<LayerKey> drawn,
            double left,
            double bottom,
            double right,
            double top,
            int width,
            int height,
            double micronsPerUnit)
        {
            //Which layers have something inside the frame, in the order the layer list puts them, and which of those
            //were left out for being hidden.
            var inFrame = new SortedDictionary<LayerKey, Layer>();

            foreach (var element in layout.Elements)
            {
                if (inFrame.ContainsKey(element.Layer.Key) || !element.Box.Intersects(window))
                    continue;

                inFrame[element.Layer.Key] = element.Layer;
            }

            var shown = new StringBuilder();
            var hidden = new StringBuilder();
            int named = 0;
            int more = 0;

            foreach (var (key, layer) in inFrame)
            {
                if (!drawn.Contains(key))
                {
                    if (hidden.Length > 0)
                        hidden.Append(", ");

                    hidden.Append(nameOf(layer));

                    continue;
                }

                if (named == MostNamed)
                {
                    more++;

                    continue;
                }

                if (shown.Length > 0)
                    shown.Append(", ");

                shown.Append(nameOf(layer)).Append(' ').Append(layer.Color);
                named++;
            }

            double micronsPerPixel = (right - left) * micronsPerUnit / width;

            var says = new StringBuilder();

            says.Append($"A picture of {cell} from x {Microns(left * micronsPerUnit)} to {Microns(right * micronsPerUnit)} µm and y {Microns(bottom * micronsPerUnit)} to {Microns(top * micronsPerUnit)} µm, ");
            says.Append($"{width} by {height} pixels, {Microns(micronsPerPixel)} µm a pixel. ");
            says.Append("It is drawn the way the person's 2D view draws it: x to the right and y DOWN the picture, ");
            says.Append($"so its top edge is y {Microns(bottom * micronsPerUnit)} µm and its bottom edge y {Microns(top * micronsPerUnit)} µm. ");

            if (shown.Length == 0)
            {
                says.Append("Nothing is drawn in that area.");
            }
            else
            {
                says.Append($"Shapes are filled in their layer's color at {(int)Math.Round(Opacity * 100)}% opacity: {shown}");

                if (more > 0)
                    says.Append($", and {more} more");

                says.Append('.');
            }

            if (hidden.Length > 0)
                says.Append($" Left out, because the person has them hidden: {hidden}.");

            return says.ToString();
        }

        private static string nameOf(Layer layer)
        {
            if (string.IsNullOrWhiteSpace(layer.Name))
                return layer.Key.ToString();

            return $"{layer.Name} {layer.Key}";
        }

        ///<summary>A length in microns as the answers write it: plain, invariant, and no more digits than mean anything.</summary>
        public static string Microns(double value)
        {
            if (Math.Abs(value) >= 1000)
                return value.ToString("0.#", CultureInfo.InvariantCulture);

            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string number(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static int clamp(double value)
        {
            return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
        }
    }
}
