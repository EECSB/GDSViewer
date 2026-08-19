using GdsII;
using Microsoft.JSInterop;

namespace GDSViewer.Models
{
    ///
    ///Numbers about what the app has done, for a test that can only ask from outside.
    ///
    ///**Because the faults this catches are invisible from the inside.** Work done twice draws exactly the
    ///same picture as work done once - the layout is right, every assertion about what is on screen passes,
    ///and the only difference is time. An end-to-end test can see a shape and cannot see a second flatten,
    ///so the app has to be asked.
    ///
    ///Static and read on demand, so nothing is published on a render and no interop call is made unless a
    ///spec makes it. That matters: a counter that cost a round trip per draw would be measuring a cost it
    ///had itself created.
    ///
    ///Reachable from JavaScript as `DotNet.invokeMethod('GDSViewer', 'FlattenCount')`.
    ///
    public static class Counters
    {
        ///<summary>
        ///Whole-library flattens since the app started - see <see cref="GdsFlattener.Flattens"/> for what is
        ///counted and what deliberately is not.
        ///</summary>
        [JSInvokable]
        public static int FlattenCount()
        {
            return GdsFlattener.Flattens;
        }
    }
}
