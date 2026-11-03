using System.Text.Json;
using GdsII;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///The bundled designs: that each one is listed with its credit, and that each one opens whole.
///
///The cells are globbed into the picker and need nothing else. A design is somebody else's work, listed by
///hand in designs.json with who made it and where it came from, and shown with that credit in the app - so
///what is checked here is the promise that list makes: that it and the folder agree, and that nothing can be
///bundled without saying whose it is.
///
///**GF180's and IHP's designs are listed the same way and shipped not at all** (2026-11-02, asked for):
///gf180-designs.json and ihp-designs.json credit each one and name where it is downloaded from when it is opened - the
///raw file at the commit its link names - so what is checked of those is that every one is a download, from exactly
///that commit.
///
///Everything the corpus tests check of every bundled file - parsing, flattening, the byte and OASIS round
///trips, the layer map - already covers the designs that ship, since they sit in the same tree.
///</summary>
public class DesignTests
{
    ///
    ///The most a bundled design may draw.
    ///
    ///**An example is the app's own choice of what to open somebody's eyes with**, so it has to open quickly.
    ///The largest bundled now is the RGB mixer at 222,274 shapes, which a published build draws in about two
    ///and a half seconds; this leaves room above that and not much more. Two candidates were left out on
    ///exactly this, both past the flattener's own ceiling of 1.5 million: the 1 KB OpenRAM SRAM at 1.6 million
    ///shapes, and Efabless's SPM example - the OpenLane demo, 600 microns square - at 1.56 million.
    ///
    private const int MostShapesForAnExample = 300_000;

    private const string Sky130Manifest = "designs.json";

    private const string Gf180Manifest = "gf180-designs.json";

    private const string IhpManifest = "ihp-designs.json";

    ///<summary>Each PDK's list of designs, which a credit and a link are checked on.</summary>
    public static TheoryData<string> Manifests
    {
        get
        {
            return new TheoryData<string> { Sky130Manifest, Gf180Manifest, IhpManifest };
        }
    }

    private static string DesignDirectory
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, "Sky130 Designs"); }
    }

    private static List<BundledDesign> Listed(string manifest)
    {
        string json = File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, manifest));

        var designs = JsonSerializer.Deserialize<List<BundledDesign>>(json);

        Assert.NotNull(designs);
        Assert.NotEmpty(designs);

        return designs;
    }

    ///<summary>
    ///The list and the folder name the same files. A design dropped in unlisted would be in no picker and
    ///carry no credit; one listed and missing would be a row that cannot open.
    ///</summary>
    [Fact]
    public void The_list_and_the_folder_hold_the_same_designs()
    {
        var listed = Listed(Sky130Manifest)
            .Select(design => design.File)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var onDisk = Directory.EnumerateFiles(DesignDirectory, "*.gds")
            .Select(path => Path.GetFileName(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(onDisk, listed);

        //And every one of them ships: a download is GF180's way and IHP's, not sky130's.
        Assert.All(Listed(Sky130Manifest), design => Assert.False(design.Downloaded, design.File));
    }

    ///
    ///**No GF180 or IHP design ships**: each is downloaded from its repository when it is opened, from exactly the
    ///commit its link names - the raw file behind the page the credit links to - with its size said before anybody
    ///presses for it.
    ///
    [Theory]
    [InlineData(Gf180Manifest, "GF180 Designs", 11)]
    [InlineData(IhpManifest, "IHP Designs", 11)]
    public void Every_design_downloaded_when_opened_comes_from_the_commit_its_link_names(string manifest, string folder, int count)
    {
        Assert.False(Directory.Exists(Path.Combine(GdsTestData.SampleDirectory, folder)));

        var designs = Listed(manifest);

        Assert.Equal(count, designs.Count);

        foreach (var design in designs)
        {
            string raw = design.Gds.Replace("https://github.com/", "https://raw.githubusercontent.com/").Replace("/blob/", "/");

            Assert.True(design.Downloaded, design.File);
            Assert.Equal(raw, design.Download);
            Assert.True(design.Size > 0, $"{design.File} does not say how big it is");
        }
    }

    ///
    ///**No two examples share a name** - a design, a cell or Mosfet.gds, shipped or downloaded, in any PDK - since
    ///the picker, a history entry and the assistant's read_example all find a file by it. Two may share one without its
    ///extension, GF180's tt_um_vga_clock.oas beside sky130's tt_um_vga_clock.gds, which a link then names whole.
    ///
    [Fact]
    public void Every_bundled_example_has_a_name_of_its_own()
    {
        var names = Directory.EnumerateFiles(GdsTestData.SampleDirectory, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".gds", StringComparison.Ordinal) || path.EndsWith(".oas", StringComparison.Ordinal))
            .Select(path => Path.GetFileName(path))
            .Concat(Listed(Gf180Manifest).Select(design => design.File))
            .Concat(Listed(IhpManifest).Select(design => design.File))
            .ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("tt_um_vga_clock.oas", names);
        Assert.Contains("tt_um_vga_clock.gds", names);
    }

    ///<summary>
    ///Every design says what it is, whose it is, under what terms, and where it came from - what its license
    ///asks to travel with it, and what the app shows under the list when one is open.
    ///</summary>
    [Theory]
    [MemberData(nameof(Manifests))]
    public void Every_design_carries_its_credit(string manifest)
    {
        foreach (var design in Listed(manifest))
        {
            Assert.False(string.IsNullOrWhiteSpace(design.Title), $"{design.File} has no title");
            Assert.False(string.IsNullOrWhiteSpace(design.Author), $"{design.File} has no author");
            Assert.False(string.IsNullOrWhiteSpace(design.About), $"{design.File} says nothing about what it is");

            Assert.StartsWith("https://", design.Project);
            Assert.StartsWith("https://", design.Gds);

            //The license's own text ships beside the files it covers, which is what the credit links to.
            string text = Path.Combine(GdsTestData.SampleDirectory, $"LICENSE-{design.License}.txt");

            Assert.True(File.Exists(text), $"{design.File} is {design.License}, and there is no text of it beside the files");
        }
    }

    ///<summary>
    ///And the link to the file names a commit rather than a branch, so it goes on pointing at what is bundled
    ///here however the project moves on - or, for a design downloaded when opened, at what is downloaded.
    ///</summary>
    [Theory]
    [MemberData(nameof(Manifests))]
    public void Every_link_to_a_file_is_pinned_to_a_commit(string manifest)
    {
        foreach (var design in Listed(manifest))
        {
            Assert.Matches(@"/blob/[0-9a-f]{40}/", design.Gds);
            Assert.EndsWith("/" + design.File, design.Gds);
        }
    }

    ///<summary>
    ///A design opens whole: every cell it places is in it, its nesting ends, and it is well inside what an
    ///example may draw.
    ///
    ///A design with a hole in it - a macro left out, a library never streamed in - would be the app showing
    ///off a broken file, and one that ran into the flattener's ceiling would open under the banner that says
    ///it is not all there.
    ///
    ///**GF180's were held to this when they were chosen, each downloaded once**: every one opened whole, and two draw
    ///more than a design that ships may - the Z80 at 644,547 shapes and the wafer.space screensaver at 321,452. A
    ///download is opened by somebody who pressed for it, told its size, so the limit is the shipped designs'. The
    ///KianV RISC-V SoC was left out, stopped at the flattener's ceiling past 1.5 million.
    ///
    ///**IHP's the same way**: every one opened whole, in nanometers, and four draw more than a design that ships may -
    ///TinyQV at 950,427 shapes, ROTFPGA at 458,531, Nyan Cat at 446,486 and the 256-by-8 SRAM at 380,822. Its 1024-by-16
    ///sibling was left out at the flattener's ceiling, 1,500,532, and an LC oscillator for carrying no word of who made
    ///it.
    ///</summary>
    [Fact]
    public void Every_design_opens_whole()
    {
        foreach (var design in Listed(Sky130Manifest))
        {
            var layout = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(Path.Combine(DesignDirectory, design.File))));

            Assert.True(layout.UnresolvedReferences.Count == 0, $"{design.File} places cells it does not contain: {string.Join(", ", layout.UnresolvedReferences)}");
            Assert.False(layout.DepthLimitReached, $"{design.File} nests without end");
            Assert.False(layout.Stopped, $"{design.File} stopped at the flattener's ceiling");
            Assert.True(layout.Elements.Count < MostShapesForAnExample, $"{design.File} draws {layout.Elements.Count:N0} shapes");
        }
    }
}
