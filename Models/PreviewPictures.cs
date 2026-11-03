using System.Text;
using GdsII;
using Microsoft.JSInterop;

namespace GDSViewer.Models
{
    ///
    ///The pictures a list draws beside itself as the pointer goes down it - the bundled examples, the files opened before,
    ///the cells of the open file: drawn without stopping the page, given up when the pointer moves on, and kept.
    ///
    ///**Why each of those.** Pointing at one of the big designs in the Examples list froze the whole page - half a second
    ///to most of a second on a fast machine (measured 2026-10-17, local Release build), and longer on a slow one. Reading
    ///the file, flattening it and writing its picture as SVG was one piece of C# on the page's only thread, and the browser
    ///then had to take in six to eight million characters of markup: a quarter of a second more even for a picture that
    ///was already made. So a picture here is:
    ///
    ///- **Drawn at a pace.** Every walk awaits <see cref="Pacer.Pause"/>, which lets the page in every
    ///<see cref="SliceMilliseconds"/>, so the list goes on answering while a chip is drawn beside it.
    ///- **Given up** the moment the pointer settles on another row, or leaves: the next pause throws, and the new row's
    ///picture starts at once instead of waiting behind one nobody is looking at any more.
    ///- **Pixels, not markup**: a PNG the size of the frame, which the browser decodes away from the page. See GdsII's
    ///PngWriter, which draws the same picture the SVG did. Not for a small layout, whose markup is cheaper than its
    ///pixels: that is its SVG document, shown by an img the same way - see <see cref="MostAsMarkup"/>.
    ///- **Kept**: the last few drawn, as the addresses an img shows them by - and the Examples list keeps the costly ones
    ///between visits as well, in <see cref="PreviewStore"/>.
    ///
    ///One of these per list, since a list draws one picture at a time: a row pointed at gives up the picture of the row
    ///before it, and nothing in another list.
    ///
    public sealed class PreviewPictures
    {
        #region Constants *******************************************************************

        ///
        ///How long a walk runs before it lets the page in, for a picture - see <see cref="Pacer.Slice"/>.
        ///
        ///**Short, because of the garbage collector.** A big design's model is built while its picture is drawn, and the
        ///runtime collects the whole heap in one piece whenever it has grown enough - on gpiov2 once a picture, 36 to
        ///44 ms of it, at a moment the app does not choose. Landing late in a slice it ran on in the same task, so at
        ///30 ms a slice most first looks at gpiov2 stalled for 50 to 70 ms. At five a slice and a collection stay under
        ///the 50 ms that reads as a stall, unless the collection alone runs past it, as it can on a busy machine.
        ///Measured 2026-10-18 on a local Release build.
        ///
        ///**And let in with <see cref="Pacer.PageTurn"/> rather than a timer**, whose four milliseconds a turn made
        ///gpiov2's picture about a sixth slower at ten milliseconds a slice, and would be most of a slice again at five.
        ///
        public const int SliceMilliseconds = 5;

        ///<summary>What a kept picture's address starts with: the PNG, as a data address an img can show by itself.</summary>
        public const string PngAddress = "data:image/png;base64,";

        ///<summary>And a small layout's: its SVG document, the same way - see <see cref="MostAsMarkup"/>.</summary>
        public const string SvgAddress = "data:image/svg+xml;base64,";

        ///
        ///How big a layout can be - its elements and their corners, counted together - and still be drawn as markup rather
        ///than as a PNG.
        ///
        ///**Markup for the small ones, again since 2026-10-21** (asked that day: "the MOSFET is very simple"). A PNG costs
        ///every pixel of the frame for every layer drawn in it, however little is there, where markup costs what the layout
        ///holds - and the browser draws a few hundred kilobytes of it at once, away from C#. On the dev server, where C#
        ///runs unoptimized, Mosfet - a 1.5 KB file - showed 0.8 s after the pointer as a PNG in a 590 by 341 frame and
        ///2.35 s at twice the screen density, and 0.27 s at either as markup. The freezes the PNG is for were millions of
        ///characters of markup; this is a few hundred thousand at most.
        ///
        public const int MostAsMarkup = 20_000;

        ///<summary>How big a picture is drawn when its frame cannot be measured - a frame's usual size on a laptop.</summary>
        private const int UsualAcross = 480;
        private const int UsualDown = 360;

        #endregion **************************************************************************



        #region Fields **********************************************************************

        private readonly int most;
        private readonly Dictionary<string, string> kept = new Dictionary<string, string>();
        private readonly Queue<string> order = new Queue<string>();

        ///<summary>The pace of the picture being drawn, or null when none is.</summary>
        private Pacer? drawing;

        #endregion **************************************************************************



        ///<param name="most">How many pictures are kept - the oldest goes when one more is drawn.</param>
        public PreviewPictures(int most)
        {
            this.most = most;
        }

        #region What is kept ****************************************************************

        ///
        ///The picture kept under this key: a PNG's data address, or empty for something with nothing in it to draw - which
        ///is an answer worth keeping too, so a cell that holds nothing is not walked again to find that out.
        ///
        public bool TryGet(string key, out string picture)
        {
            if (kept.TryGetValue(key, out string? found))
            {
                picture = found;

                return true;
            }

            picture = "";

            return false;
        }

        public void Keep(string key, string picture)
        {
            if (!kept.ContainsKey(key))
                order.Enqueue(key);

            kept[key] = picture;

            while (order.Count > most)
                kept.Remove(order.Dequeue());
        }

        ///<summary>One picture forgotten, because what it was of has gone - a history row deleted.</summary>
        public void Forget(string key)
        {
            kept.Remove(key);
        }

        ///<summary>Every picture forgotten, and the one being drawn given up, because what they were drawn from has changed.</summary>
        public void ForgetAll()
        {
            Stop();

            kept.Clear();
            order.Clear();
        }

        #endregion **************************************************************************



        #region Drawing *********************************************************************

        ///<summary>Whether a picture is being drawn.</summary>
        public bool Drawing => drawing is not null;

        ///<summary>Gives up the picture being drawn, at its next pause.</summary>
        public void Stop()
        {
            drawing?.Cancel();
            drawing = null;
        }

        ///
        ///A new picture's pace, giving up the one before it - for a caller with more to do than <see cref="DrawAsync"/>
        ///does, such as fetching the file and looking for it in the store first. Hand it back to <see cref="End"/>.
        ///
        public Pacer Begin()
        {
            Stop();

            drawing = new Pacer { Slice = SliceMilliseconds, Turn = Pacer.PageTurn };

            return drawing;
        }

        ///<summary>Done with a pace, drawn or failed or given up. A newer one already begun is left alone.</summary>
        public void End(Pacer pacer)
        {
            if (ReferenceEquals(drawing, pacer))
                drawing = null;
        }

        ///
        ///One picture, start to finish: the layout <paramref name="layoutOf"/> reads and flattens at the pace it is handed,
        ///drawn into <paramref name="pixels"/> and kept under <paramref name="key"/>.
        ///
        ///**Null when it was given up** - another row's picture begun, or <see cref="Stop"/> - in which case nothing is
        ///kept and the caller has nothing to show: whatever gave it up has its own picture to draw. A layout that will not
        ///read or draw is kept as nothing, the way an empty one is; it costs its picture, not the list beside it.
        ///
        public async Task<string?> DrawAsync(string key, Func<Pacer, Task<FlattenedLayout>> layoutOf, (int Across, int Down) pixels, float opacity)
        {
            var pacer = Begin();
            string picture;

            try
            {
                var layout = await layoutOf(pacer);

                picture = await PictureAsync(layout, pixels, opacity, pacer);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                picture = "";
            }
            finally
            {
                End(pacer);
            }

            Keep(key, picture);

            return picture;
        }

        ///
        ///A layout as a picture's address, at a pace - or empty when there is nothing in it to draw. Its SVG document when it
        ///is small, which takes no pace, and a PNG otherwise - see <see cref="MostAsMarkup"/>.
        ///
        public static async Task<string> PictureAsync(FlattenedLayout layout, (int Across, int Down) pixels, float opacity, Pacer pacer)
        {
            if (Small(layout))
            {
                string? document = Preview.PictureDocument(layout, pixels.Across, pixels.Down, opacity);

                if (document is null)
                    return "";

                return SvgAddress + Convert.ToBase64String(Encoding.UTF8.GetBytes(document));
            }

            var picture = await Preview.PictureAsync(layout, pixels.Across, pixels.Down, opacity, pacer.Pause);

            if (picture is null)
                return "";

            return PngAddress + Convert.ToBase64String(picture.Png);
        }

        ///<summary>Whether a layout is drawn as markup - no more than <see cref="MostAsMarkup"/> elements and corners. Stops counting there.</summary>
        public static bool Small(FlattenedLayout layout)
        {
            long size = 0;

            foreach (var element in layout.Elements)
            {
                size += 1 + element.Points.Count;

                if (size > MostAsMarkup)
                    return false;
            }

            return true;
        }

        ///
        ///How many pixels a picture needs to fill <paramref name="frame"/> - a selector - across and down: its size on the
        ///page, or <paramref name="least"/> CSS pixels a side if that is more, at the screen's density. So a picture is as
        ///sharp as the markup it replaced was, whatever the window and the screen.
        ///
        ///A frame that is not on the page, or has no size, is given a frame's usual size rather than none.
        ///
        public static async Task<(int Across, int Down)> PixelsFor(IJSRuntime js, string frame, int least = 0)
        {
            try
            {
                int[]? measured = await js.InvokeAsync<int[]?>("picturePixels", frame, least);

                if (measured is [int across, int down] && across > 0 && down > 0)
                    return (across, down);
            }
            catch { }

            return (UsualAcross, UsualDown);
        }

        #endregion **************************************************************************
    }
}
