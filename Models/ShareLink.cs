using System.Globalization;
using System.Text.RegularExpressions;

namespace GDSViewer.Models
{
    ///
    ///What the share popup is set to: what a link opens, and how much of the app comes with it.
    ///
    ///**Everything the address can say, held as what somebody chose rather than as parameters.** The popup
    ///binds to this and ShareLink turns it into an address, so which parameter a choice becomes, and when it
    ///is left out because it would say nothing, is decided in one place that a test can read.
    ///
    public sealed class ShareOptions
    {
        ///<summary>The bundled example the link opens, by the name the address knows it by, or null for none.</summary>
        public string? File { get; set; }

        ///<summary>Which view it opens in: 2d, 3d or text.</summary>
        public string View { get; set; } = "2d";

        ///<summary>Where the 2D view looks - four numbers, x y width height - or null to frame the drawing.</summary>
        public string? Box { get; set; }

        ///<summary>Where the 3D camera stands and what it orbits - six numbers - or null for its opening place.</summary>
        public string? Camera { get; set; }

        public AppMode Mode { get; set; } = AppMode.Edit;

        ///<summary>Whether the title bar shows. Shown in the whole app and with no editing, hidden in viewer only.</summary>
        public bool Banner { get; set; } = true;

        ///<summary>The page's margins given to the view, as the full-screen button does.</summary>
        public bool Full { get; set; }

        ///<summary>The 2D view's cell tree, docked beside the drawing.</summary>
        public bool CellTree { get; set; } = true;

        ///<summary>The layer list down the side, beside every view - the text's since its rules were wanted there. There is none in viewer only.</summary>
        public bool Layers { get; set; } = true;

        ///<summary>The 2D view's grid.</summary>
        public bool Grid { get; set; } = true;

        ///<summary>The 3D scene's backdrop, by file name, or "none".</summary>
        public string Background { get; set; } = "none";
    }

    ///
    ///A share popup's choices, as an address and as the iframe that embeds it.
    ///
    ///**Only what changes something is written.** A link that spelled out every default would be twice as long
    ///and say nothing more - and worse, a default written down is a default frozen: the visitor's own session
    ///is overruled by every parameter an address names, so a link carrying grid=true would turn the grid back
    ///on for somebody who had turned it off. So each choice is written where it differs from what the app does
    ///unasked, and only for the view it applies to: a 2D setting in a 3D link is a setting nothing reads.
    ///
    ///**Names and values are the canonical ones** - lower case, view as 2d, 3d or text, mode as noedit or
    ///viewer - though the reader takes a wider spelling of most. See Embedding.ReadFrom.
    ///
    public static class ShareLink
    {
        ///<summary>The address that opens what the options say, on the app at <paramref name="baseUri"/>.</summary>
        public static string Address(string baseUri, ShareOptions options)
        {
            var named = new List<string>();

            if (!string.IsNullOrWhiteSpace(options.File))
                named.Add($"file={Uri.EscapeDataString(options.File.Trim())}");

            named.Add($"view={viewOf(options.View)}");

            if (options.Mode == AppMode.NoEdit)
                named.Add("mode=noedit");
            else if (options.Mode == AppMode.Viewer)
                named.Add("mode=viewer");

            //Against what the mode gives anyway: hidden in viewer only, shown otherwise.
            bool bannerByDefault = options.Mode != AppMode.Viewer;

            if (options.Banner != bannerByDefault)
                named.Add($"banner={flag(options.Banner)}");

            if (options.Full)
                named.Add("full=true");

            string view = viewOf(options.View);

            //
            //The cell tree is open unless asked otherwise - in viewer only as well - and every view's: the 3D view has one
            //of its own since 2026-10-17 and the text view a list of its cells, opened by the same switch. It was written
            //for a 2D link alone, so a 3D one could not close it (the 2026-10-21 audit).
            //
            if (!options.CellTree)
                named.Add("tree=false");

            //There is no layer list to close in viewer only.
            if (options.Mode != AppMode.Viewer && !options.Layers)
                named.Add("layers=false");

            if (view == "2d" && !options.Grid)
                named.Add("grid=false");

            if (view == "3d" && !string.IsNullOrWhiteSpace(options.Background) && !string.Equals(options.Background, "none", StringComparison.OrdinalIgnoreCase))
                named.Add($"background={Uri.EscapeDataString(options.Background.Trim())}");

            if (view == "2d" && Embedding.FramingNamed(options.Box) is string box)
                named.Add($"box={shortened(box)}");

            if (view == "3d" && Embedding.CameraNamed(options.Camera) is string camera)
                named.Add($"camera={shortened(camera)}");

            return $"{baseUri}?{string.Join("&", named)}";
        }

        ///
        ///The iframe that embeds an address, ready to paste into a page.
        ///
        ///**A size that is not a size is left at the default** rather than written as given: what is typed
        ///here goes into somebody else's page, and an attribute holding a quote or a bracket would break out
        ///of the tag it is in. A bare number is pixels, which is what the attribute means by one; a percentage
        ///is kept as one.
        ///
        ///xr-spatial-tracking only where asked for: it is what lets the 3D view's VR and AR buttons work from
        ///inside a frame, and a page has no reason to grant a permission its viewer will not use.
        ///
        public static string Iframe(string address, string width, string height, bool allowXr)
        {
            string allow = "";

            if (allowXr)
                allow = " allow=\"xr-spatial-tracking\"";

            return $"<iframe src=\"{attribute(address)}\" width=\"{sizeOr(width, "100%")}\" height=\"{sizeOr(height, "600")}\" style=\"border: 0;\"{allow} title=\"GDS Viewer\"></iframe>";
        }

        ///<summary>2d, 3d or text, whatever the view was given as. Anything else is 2D, which is what the reader makes of it too.</summary>
        private static string viewOf(string view)
        {
            string asked = (view ?? "").Trim().ToLowerInvariant();

            if (asked == "3d" || asked == "text")
                return asked;

            return "2d";
        }

        private static string flag(bool value)
        {
            if (value)
                return "true";

            return "false";
        }

        ///
        ///Framing numbers at two decimals, joined with commas.
        ///
        ///**Two places of a database unit** is far below anything a screen can show, and the app writes these
        ///at full double precision - sixteen digits each, so a 2D framing alone ran to seventy characters of a
        ///link somebody may be reading aloud. Commas rather than spaces, which an address would show as %20.
        ///
        private static string shortened(string numbers)
        {
            var parts = numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => twoPlaces(double.Parse(part, CultureInfo.InvariantCulture)));

            return string.Join(",", parts);
        }

        ///<summary>A number at two decimals at most - and a zero that rounding left negative written as 0, not -0.</summary>
        private static string twoPlaces(double number)
        {
            double rounded = Math.Round(number, 2);

            if (rounded == 0)
                rounded = 0;

            return rounded.ToString("0.##", CultureInfo.InvariantCulture);
        }

        ///<summary>A size as the attribute takes it - a whole or decimal number, optionally a percentage - or the default.</summary>
        private static string sizeOr(string given, string fallback)
        {
            string size = (given ?? "").Trim();

            if (size.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                size = size[..^2].TrimEnd();

            if (Regex.IsMatch(size, @"^\d{1,5}(\.\d{1,2})?%?$"))
                return size;

            return fallback;
        }

        ///<summary>An address made safe for a double-quoted attribute.</summary>
        private static string attribute(string value)
        {
            return value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}
