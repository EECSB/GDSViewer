using Microsoft.AspNetCore.Components;

namespace GDSViewer.Pages
{
    public partial class Viewer
    {
        public enum ViewType
        {
            ViewText,
            View2DSvg,
            View3D
        }

        ///<summary>
        ///What a view is called in the URL. Spelled out rather than derived from the enum names, which
        ///would put "View2DSvg" in a link someone is meant to read and paste - and would also tie a URL
        ///people have bookmarked to an identifier that is free to be renamed.
        ///</summary>
        private static string slugOf(ViewType view)
        {
            if (view == ViewType.ViewText)
                return "text";

            if (view == ViewType.View3D)
                return "3d";

            return "2d";
        }

        ///<summary>The reverse, falling back to the 2D view for anything unrecognized.</summary>
        private static ViewType viewOf(string? slug)
        {
            if (string.Equals(slug, "text", StringComparison.OrdinalIgnoreCase))
                return ViewType.ViewText;

            if (string.Equals(slug, "3d", StringComparison.OrdinalIgnoreCase))
                return ViewType.View3D;

            return ViewType.View2DSvg;
        }
    }
}
