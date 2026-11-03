namespace GDSViewer.Models
{
    ///<summary>
    ///One thing the command palette can do: what it is called, where it belongs, the key it already has, and the
    ///doing of it.
    ///
    ///**Something the app can already do, by another way in.** Each is a button on the page pressed for you, or a
    ///method the page already runs from one - see Viewer.paletteCommands - so the palette cannot come to do a
    ///thing differently from the control it stands for.
    ///</summary>
    public sealed class PaletteCommand
    {
        public required string Name { get; init; }

        ///<summary>What part of the app it belongs to - File, View, Tools - shown beside it and searched with it.</summary>
        public string Group { get; init; } = "";

        ///<summary>The key that does the same without the palette, if there is one, so the palette teaches it.</summary>
        public string Keys { get; init; } = "";

        ///<summary>Other words it answers to besides its name: "save" finds Download, "dark" finds the theme.</summary>
        public string Words { get; init; } = "";

        public required Func<Task> Run { get; init; }
    }
}
