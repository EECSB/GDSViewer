using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///The share popup's choices, turned into an address and an iframe.
///
///What these hold is the rule that keeps a link short and honest: a parameter is written only where it
///changes something, and only for the view it applies to - because every one an address names overrules the
///visitor's own session, so a default written down is a default forced on them. And the iframe goes into
///somebody else's page, so what is typed into its size boxes must not be able to break out of the tag.
///</summary>
public class ShareLinkTests
{
    private const string App = "https://example.org/GDSViewer/";

    [Fact]
    public void A_plain_link_names_the_file_and_the_view_and_nothing_else()
    {
        var options = new ShareOptions { File = "Mosfet", View = "2d" };

        Assert.Equal($"{App}?file=Mosfet&view=2d", ShareLink.Address(App, options));
    }

    ///<summary>No file is a link to the app itself, which opens on whatever the visitor had last.</summary>
    [Fact]
    public void Without_a_file_the_link_still_names_the_view()
    {
        Assert.Equal($"{App}?view=3d", ShareLink.Address(App, new ShareOptions { View = "3d" }));
    }

    [Fact]
    public void Each_mode_is_written_by_its_canonical_name_and_the_whole_app_is_not_written_at_all()
    {
        Assert.DoesNotContain("mode=", ShareLink.Address(App, new ShareOptions { Mode = AppMode.Edit }));
        Assert.Contains("mode=noedit", ShareLink.Address(App, new ShareOptions { Mode = AppMode.NoEdit }));
        Assert.Contains("mode=viewer", ShareLink.Address(App, new ShareOptions { Mode = AppMode.Viewer, Banner = false }));
    }

    ///<summary>
    ///The title bar against what the mode gives anyway: viewer only hides it, so it is written there only to
    ///ask for it back - and the other two show it, so there it is written only to take it away.
    ///</summary>
    [Fact]
    public void The_banner_is_written_only_where_it_differs_from_the_mode()
    {
        Assert.DoesNotContain("banner=", ShareLink.Address(App, new ShareOptions { Mode = AppMode.Viewer, Banner = false }));
        Assert.Contains("banner=true", ShareLink.Address(App, new ShareOptions { Mode = AppMode.Viewer, Banner = true }));

        Assert.DoesNotContain("banner=", ShareLink.Address(App, new ShareOptions { Mode = AppMode.Edit, Banner = true }));
        Assert.Contains("banner=false", ShareLink.Address(App, new ShareOptions { Mode = AppMode.NoEdit, Banner = false }));
    }

    ///<summary>The 2D view's settings are not written into a link that opens the 3D view, where nothing reads them.</summary>
    [Fact]
    public void A_setting_for_another_view_is_left_out()
    {
        var options = new ShareOptions
        {
            View = "3d",
            CellTree = false,
            Grid = false,
            Box = "0 0 100 100",
            Camera = "1 2 3 4 5 6",
            Background = "background2.jpg"
        };

        string address = ShareLink.Address(App, options);

        //The tree is every view's, so a 3D link closes it too - see the next test.
        Assert.Contains("tree=false", address);
        Assert.DoesNotContain("grid=", address);
        Assert.DoesNotContain("box=", address);
        Assert.Contains("camera=1,2,3,4,5,6", address);
        Assert.Contains("background=background2.jpg", address);
    }

    ///<summary>
    ///The cell tree closed from a link to any view: the 3D view has a tree of its own and the text view a list of its
    ///cells, all opened by the one switch. Only a 2D link wrote it until the 2026-10-21 audit.
    ///</summary>
    [Fact]
    public void The_cell_tree_is_closed_from_a_link_to_any_view()
    {
        foreach (string view in new[] { "2d", "3d", "text" })
            Assert.Contains("tree=false", ShareLink.Address(App, new ShareOptions { View = view, CellTree = false }));

        Assert.DoesNotContain("tree=", ShareLink.Address(App, new ShareOptions { View = "3d", CellTree = true }));
    }

    ///<summary>
    ///And the layer list is not offered where there is none: viewer only. Beside the text it is, since the rules are
    ///checked there too.
    ///</summary>
    [Fact]
    public void The_layer_list_is_written_only_where_there_is_one()
    {
        Assert.Contains("layers=false", ShareLink.Address(App, new ShareOptions { View = "2d", Layers = false }));
        Assert.Contains("layers=false", ShareLink.Address(App, new ShareOptions { View = "text", Layers = false }));
        Assert.DoesNotContain("layers=", ShareLink.Address(App, new ShareOptions { Mode = AppMode.Viewer, Banner = false, Layers = false }));
    }

    ///<summary>
    ///The framing the app writes at full precision, shortened to two places of a database unit and joined
    ///with commas - and a zero that rounding left negative written plainly.
    ///</summary>
    [Fact]
    public void A_framing_is_shortened_and_joined_with_commas()
    {
        var options = new ShareOptions { View = "2d", Box = "12.34567891234 -0.0001 100.5 200" };

        Assert.EndsWith("&box=12.35,0,100.5,200", ShareLink.Address(App, options));
    }

    [Fact]
    public void A_framing_that_does_not_read_is_left_out()
    {
        Assert.DoesNotContain("box=", ShareLink.Address(App, new ShareOptions { View = "2d", Box = "1 2 3" }));
        Assert.DoesNotContain("box=", ShareLink.Address(App, new ShareOptions { View = "2d", Box = "0 0 0 10" }));
    }

    ///<summary>The ampersands of the address become entities inside the attribute, as HTML asks.</summary>
    [Fact]
    public void The_iframe_carries_the_address_escaped_and_the_sizes_given()
    {
        string frame = ShareLink.Iframe($"{App}?file=Mosfet&view=2d", "800", "100%", false);

        Assert.Equal(
            $"<iframe src=\"{App}?file=Mosfet&amp;view=2d\" width=\"800\" height=\"100%\" style=\"border: 0;\" title=\"GDS Viewer\"></iframe>",
            frame);
    }

    ///<summary>
    ///A size is typed into a box and pasted into somebody else's page, so anything that is not a size - a quote
    ///that would close the attribute, say - is not written; the default is.
    ///</summary>
    [Fact]
    public void A_size_that_is_not_a_size_is_not_written_into_the_tag()
    {
        string frame = ShareLink.Iframe(App, "100%\" onload=\"alert(1)", "600px", false);

        Assert.Contains("width=\"100%\"", frame);
        Assert.Contains("height=\"600\"", frame);
        Assert.DoesNotContain("onload", frame);
    }

    [Fact]
    public void VR_and_AR_are_allowed_only_when_asked_for()
    {
        Assert.DoesNotContain("allow=", ShareLink.Iframe(App, "100%", "600", false));
        Assert.Contains("allow=\"xr-spatial-tracking\"", ShareLink.Iframe(App, "100%", "600", true));
    }
}
