namespace GdsII
{
    ///
    ///How a long walk offers to stop: every so many steps it awaits the caller's pause.
    ///
    ///**For a caller on a single thread**, which is what a browser running this is. A walk of a real chip runs for
    ///seconds, and nothing else on the page - a press, a frame - can happen until it lets go. A walk that awaits a
    ///pause now and then lets the caller hand the page back for a moment, and throw from the pause to give the
    ///walk up altogether - which is how a load is canceled. See the async twins: <see cref="GDS.FromBytesAsync"/>,
    ///<see cref="GdsFlattener.FlattenAsync(GDS, Func{ValueTask})"/>, <see cref="LayoutIndex.BuildAsync"/>,
    ///<see cref="SvgWriter.BuildAsync"/>, <see cref="PngWriter.BuildAsync"/> and <see cref="Booleans.MergeByLayerAsync"/>.
    ///
    ///**Each twin runs the synchronous walk's own steps**, a run at a time - a method over a range of elements that
    ///the synchronous entry point calls once for everything. The loop stays a plain one either way, which matters:
    ///a loop inside an async method keeps its locals on the heap, and in the browser's interpreter that made every
    ///walk a third slower, the synchronous callers' as much as the paced ones'.
    ///
    ///**Counted rather than timed**, so offering costs a comparison: the pause itself decides whether enough time has
    ///gone by to be worth letting go, and it is asked only every <see cref="Every"/> steps.
    ///
    internal sealed class Pauses
    {
        ///<summary>How many steps go by between offers to stop: a few milliseconds of a walk on the slowest host this runs on.</summary>
        public const int Every = 128;

        private readonly Func<ValueTask>? pause;

        private int since;

        public Pauses(Func<ValueTask>? pause)
        {
            this.pause = pause;
        }

        ///<summary>One step: the caller's pause on every <see cref="Every"/>th, and nothing to wait for otherwise.</summary>
        public ValueTask Tick()
        {
            if (pause is null)
                return default;

            since++;

            if (since < Every)
                return default;

            since = 0;

            return pause();
        }
    }
}
