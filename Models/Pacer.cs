using System.Diagnostics;

namespace GDSViewer.Models
{
    ///
    ///One load's pace, and its way out: what the long walks of a load wait on, so the page goes on answering while
    ///a big file is read and drawn - and what Cancel sets, so the load stops at the next of them.
    ///
    ///**Why a load has to stop to be stopped.** Reading, flattening, indexing and drawing a file is C# on the page's
    ///only thread - the RGB mixer was twelve seconds of it in one piece on the dev server - and while it runs nothing
    ///else happens: not a frame, not a press. A Cancel button pressed then is only heard once the load is over. So the
    ///library's walks await a pause every so many steps (GdsII's Pauses), and this decides when one is worth taking:
    ///once a walk has run a slice without letting go, it hands the page back for a turn of its event loop, which is
    ///where a press is heard and a frame is drawn.
    ///
    ///**And how it stops.** Canceled, the next pause throws, which unwinds the walk to whatever began the load - see
    ///the load routes in Viewer.razor, which put back what was on screen.
    ///
    public sealed class Pacer
    {
        ///
        ///How long a walk runs before it lets the page in.
        ///
        ///Short enough that a press on Cancel is answered before anybody wonders whether it was heard - under the tenth
        ///of a second that reads as at once - and long enough that letting go is a few percent of the load. Letting go
        ///costs more than the millisecond it asks for: a browser holds a timer set from inside another to four, so
        ///at 40 ms a slice the RGB mixer's load was a tenth slower for its pauses alone.
        ///
        public const int SliceMilliseconds = 80;

        ///
        ///How long a walk runs before it lets the page in, at this pace: <see cref="SliceMilliseconds"/> for a load.
        ///
        ///**Shorter for a picture drawn as the pointer moves down a list** - see PreviewPictures. Nobody is waiting on
        ///that picture the way they wait on a load, and the list under the pointer has to go on answering as though
        ///nothing were being drawn at all, so a few percent more time in pauses is the cheaper side of the trade.
        ///
        public int Slice { get; init; } = SliceMilliseconds;

        private readonly CancellationTokenSource stop = new CancellationTokenSource();

        private readonly Stopwatch running = Stopwatch.StartNew();

        ///
        ///Whether the note offers Cancel for this load. Not for the one that puts things back after a cancel: going
        ///back is the answer to Cancel, and a way back that could itself be given up would leave the page with
        ///neither file.
        ///
        public bool CanCancel { get; init; } = true;

        ///
        ///How this pace lets the page in: a timer when nothing is handed in, which is a load's way, or
        ///<see cref="PageTurn"/>, which is a picture's - see PreviewPictures.
        ///
        public Func<ValueTask>? Turn { get; init; }

        ///
        ///A turn of the page's event loop that is not a timer, set once by the app: a message the page posts to itself -
        ///pageTurn in JavaScriptInterOp.js - which is answered as soon as the browser has seen to what was waiting, a
        ///frame to draw or a press. A timer set from inside another is held to four milliseconds, a twentieth of a
        ///load's slice but most of a picture's. Null where there is no page, which is a test's case.
        ///
        public static Func<ValueTask>? PageTurn { get; set; }

        ///<summary>Canceled when the load is, for a fetch or anything else that takes a token rather than a pause.</summary>
        public CancellationToken Token => stop.Token;

        public bool Canceled => stop.IsCancellationRequested;

        ///<summary>Gives the load up: the next pause throws, and so does anything waiting on <see cref="Token"/>.</summary>
        public void Cancel()
        {
            stop.Cancel();
        }

        ///
        ///What a walk awaits between its steps: nothing to wait for while the slice lasts, a turn of the page's event
        ///loop once it is used up, and an <see cref="OperationCanceledException"/> once the load has been given up.
        ///
        public ValueTask Pause()
        {
            stop.Token.ThrowIfCancellationRequested();

            if (running.ElapsedMilliseconds < Slice)
                return default;

            return letGo();
        }

        ///
        ///A timer, or the <see cref="Turn"/> handed in, rather than Task.Yield, because either is certain to go through
        ///the browser's event loop - which is the whole point: a continuation queued any other way may run before
        ///anything the page was waiting to do.
        ///
        private async ValueTask letGo()
        {
            if (Turn is null)
                await Task.Delay(1);
            else
                await Turn();

            running.Restart();

            stop.Token.ThrowIfCancellationRequested();
        }
    }
}
