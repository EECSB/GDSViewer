using Microsoft.JSInterop;

namespace GDSViewer.Models
{
    ///
    ///Waiting for the screen to catch up with the page, before work that holds the page.
    ///
    ///C# runs on the page's only thread here, so what a render puts in the page reaches the screen only when that
    ///thread next lets go - and work started straight after a render holds it until the work is done. A message
    ///rendered to say the work has begun is then first seen once it has finished, which is too late to be of any
    ///use. Awaiting this between the two is what puts it up first. See afterPaint in JavaScriptInterOp.js.
    ///
    public static class Painting
    {
        ///<summary>
        ///Returns once the browser has drawn a frame of the page as it is now, or after a moment on a page that is
        ///not being shown, which gets no frames. A failure is let go: this only ever decides when something starts.
        ///</summary>
        public static async Task AfterPaintAsync(this IJSRuntime js)
        {
            try
            {
                await js.InvokeVoidAsync("afterPaint");
            }
            catch { }
        }
    }
}
