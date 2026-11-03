namespace GDSViewer.Models
{
    ///
    ///**Auto-generate preview** - one switch, with each of the four previews it decides: under the 2D view's cell tree and
    ///the text's cell list, and in the line under History and under Examples. Ticked, what is pointed at is drawn by
    ///itself; off, the picture waits for a press. The shell holds it and remembers it in this browser - see autoPreview in
    ///Viewer.razor.
    ///
    ///What it says on hover is here, in one place, since it is the same switch in all four.
    ///
    public static class AutoPreviewSwitch
    {
        public const string Title = "Draw what is pointed at by itself. Off, the picture waits for a press on it - for a file or a computer where drawing a whole cell or chip on every look is slow. One switch for every preview, remembered in this browser";
    }
}
