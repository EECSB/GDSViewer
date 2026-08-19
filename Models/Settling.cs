namespace GDSViewer.Models
{
    ///
    ///Waits for a drag to stop before paying for it.
    ///
    ///**A slider is one event per step, and a step is not a decision.** Both sliders in this app are bound
    ///`@oninput`, so dragging across one fires the handler again and again - and both handlers do work
    ///proportional to the whole file. Measured: the 3D Layer Distance slider took **7.6 seconds per step** on
    ///a twenty-thousand-element layout, so a twelve-step drag was a minute and a half of frozen tab. The
    ///opacity slider rebuilds the entire SVG *and* re-serializes, deflates and stores the whole library, per
    ///step.
    ///
    ///**Debounced rather than moved to `@onchange`**, which would also have fixed it and would have cost the
    ///live preview on every file small enough to afford one. A delay keeps the feedback immediate where the
    ///work is cheap - each step lands before the next arrives - and coalesces it only where it is not, which
    ///is precisely where it was unaffordable. One control, behaving well at both sizes.
    ///
    ///Not a timer and not a thread. WebAssembly is single-threaded, so this is `Task.Delay` yielding to the
    ///browser and the next event arriving in the gap - which is what cancels the one before it.
    ///
    public sealed class Settling
    {
        ///
        ///How long a drag has to pause before the work runs, in milliseconds.
        ///
        ///Short enough to feel immediate on a file that can keep up, long enough that a drag across a slider
        ///lands as one piece of work rather than fifty. A pointer moving across a range control emits steps
        ///far faster than this.
        ///
        public const int Quiet = 120;

        private CancellationTokenSource? waiting;

        ///
        ///Runs it once the events stop arriving, and not at all if another comes first.
        ///
        ///The last call wins, which is the behavior a slider wants: what somebody meant is where they let go,
        ///not the forty places they passed through.
        ///
        public async Task After(Func<Task> work)
        {
            //Whatever was waiting is now out of date - somebody has moved the slider again.
            waiting?.Cancel();

            var mine = new CancellationTokenSource();

            waiting = mine;

            try
            {
                await Task.Delay(Quiet, mine.Token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            //Checked as well as caught. A cancellation landing between the delay finishing and this line is
            //a real race in a single-threaded runtime, because the await is where the browser gets a turn.
            if (mine.IsCancellationRequested)
                return;

            await work();
        }
    }
}
