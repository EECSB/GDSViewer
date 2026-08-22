using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The examples in the package readmes, run.
///
///**Because a readme is the first thing a package's user tries.** The one shipped inside a .nupkg is what
///nuget.org renders on the listing page, and an example there that does not compile is a worse first
///impression than no example at all - it is also the piece of a repository most likely to be left behind,
///since nothing else breaks when it goes stale.
///
///These are the snippets from [`GdsII/README.md`](../GdsII/README.md), transcribed rather than extracted:
///pulling them out of the markdown at build time would need a fenced-block parser and a compiler, and this
///gets the same guarantee by being ordinary code that a change to the API stops compiling.
///</summary>
public class PackageReadmeTests
{
    private static string Sample
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample); }
    }

    ///<summary>The one at the top: open a file, list its layers, write it back out as OASIS.</summary>
    [Fact]
    public void The_opening_example_runs()
    {
        var gds = new GDS(File.ReadAllBytes(Sample));

        var listed = new List<string>();

        foreach (var layer in gds.AdditionalInformation.Layers)
            listed.Add($"{layer.Key}: {layer.Value.DisplayName}");

        Assert.NotEmpty(listed);

        byte[] oasis = OasisWriter.Write(gds);

        Assert.True(OasisReader.LooksLikeOasis(oasis));
    }

    [Fact]
    public void The_reading_example_runs()
    {
        GDS gds;

        using (var stream = File.OpenRead(Sample))
            gds = GDS.FromStream(stream);

        Assert.NotEmpty(gds.Records);

        byte[] bytes = OasisWriter.Write(gds);

        GDS oas;

        using (var stream = new MemoryStream(bytes))
            oas = OasisReader.Read(stream);

        Assert.NotEmpty(oas.Records);

        //And the line about letting the bytes decide.
        byte[] firstThirteenBytes = bytes[..13];

        Assert.True(OasisReader.LooksLikeOasis(firstThirteenBytes));
    }

    ///<summary>
    ///Flattening, and the boolean under it - where poly crosses diffusion is a transistor gate, which is
    ///the example because it is the operation somebody actually wants on their first day.
    ///</summary>
    [Fact]
    public void The_flattening_example_runs()
    {
        var gds = new GDS(File.ReadAllBytes(Sample));

        var layout = GdsFlattener.Flatten(gds);

        var described = new List<string>();

        foreach (var element in layout.Elements)
            described.Add($"{element.Layer.Key}: {element.Points.Count} points");

        Assert.NotEmpty(described);

        var poly = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(66, 20))).Select(e => e.Points).ToList();
        var diff = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(65, 20))).Select(e => e.Points).ToList();

        Assert.NotEmpty(poly);
        Assert.NotEmpty(diff);

        var gate = Booleans.Combine(poly, diff, BooleanOperation.And);

        //The gate of a transistor is where those two overlap, and in this file they do.
        Assert.NotEmpty(gate);
    }

    ///
    ///The creating example: a new library, a boundary, a path, a label, a second cell and a placement.
    ///
    ///**The one that most needed writing down.** Everything else in the readme starts from a file that
    ///already exists, so a reader asking whether this library can build a layout rather than only read one
    ///had nothing to look at - and the answer was yes the whole time.
    ///
    [Fact]
    public void The_creating_example_runs()
    {
        var gds = GDS.NewLibrary("AUTHORED");

        var top = Hierarchy.Named(gds, "TOP")!;

        var square = new[]
        {
            new Element.Point(0, 0),
            new Element.Point(1000, 0),
            new Element.Point(1000, 600),
            new Element.Point(0, 600)
        };

        new AddElement(gds, top, new LayerKey(68, 20), square).Apply();

        var run = new[] { new Element.Point(0, 900), new Element.Point(2000, 900) };

        new AddElement(gds, top, new LayerKey(67, 20), run, 140, Paths.Ends.Extended).Apply();
        new AddElement(gds, top, new LayerKey(68, 5), new Element.Point(500, 300), "VDD").Apply();

        new AddStructure(gds, "LEAF", Paths.Records(new LayerKey(66, 20), run, 200, Paths.Ends.Flush)!).Apply();
        new AddElement(gds, top, Hierarchy.PlacementRecords("LEAF", new Element.Point(3000, 0), false, 90), "Place").Apply();

        byte[] bytes = gds.Serialize();

        //Both cells, and the placement resolved: three shapes drawn in TOP plus the one LEAF holds.
        Assert.Equal(new[] { "TOP", "LEAF" }, Hierarchy.Names(gds));
        Assert.Equal(4, GdsFlattener.Flatten(gds).Elements.Count);

        //And what came out is a file, not just an object that agreed with itself.
        var reopened = new GDS(bytes);

        Assert.Equal(4, GdsFlattener.Flatten(reopened).Elements.Count);
    }

    ///
    ///The shapes, curves and routes example.
    ///
    ///**Named arguments on purpose**, exactly as the readme writes them. A snippet using `x:` against a
    ///parameter called `centerX` reads perfectly and does not compile, and that is the class of mistake a
    ///transcribed example exists to catch - it was in this section before this test was written.
    ///
    [Fact]
    public void The_shapes_and_routes_example_runs()
    {
        Assert.Equal(4, Shapes.Rectangle(centerX: 0, centerY: 0, width: 1000, height: 600).Count);
        Assert.Equal(4, Shapes.Between(0, 0, 1000, 600).Count);
        Assert.Equal(128, Shapes.Circle(centerX: 0, centerY: 0, radius: 500, vertices: 128).Count);
        Assert.NotEmpty(Shapes.Ellipse(0, 0, radiusX: 800, radiusY: 300));
        Assert.Equal(6, Shapes.RegularPolygon(0, 0, radius: 500, sides: 6, turnDegrees: 30).Count);

        var ribbon = new BezierBuilder()
            .AddPoint(0, 0).AddPoint(0, 1000).AddPoint(1000, 1000).AddPoint(1000, 0)
            .BuildPolygon(width: 200, vertices: 128);

        Assert.NotEmpty(ribbon);

        var gds = GDS.NewLibrary("ROUTED");
        var top = Hierarchy.Named(gds, "TOP")!;

        var route = new PathBuilder(new Element.Point(-3100, -3300), headingDegrees: 0)
            .Straight(2000)
            .BendDeg(-45, radius: 500)
            .Straight(1000)
            .BendDeg(180, 300)
            .Bezier(b => b.AddPoint(0, 0).AddPoint(0, 1000).AddPoint(2000, 1000).AddPoint(1000, 0));

        new AddElement(gds, top, new LayerKey(68, 20), route.BuildPolygon(width: 140)).Apply();

        foreach (var piece in route.Build(maxVertices: 200))
            new AddElement(gds, top, new LayerKey(68, 20), piece, 140, Paths.Ends.Flush).Apply();

        //The outline plus a path element per piece, and all of it a file that reads back.
        var drawn = GdsFlattener.Flatten(new GDS(gds.Serialize()));

        Assert.True(drawn.Elements.Count >= 2);
    }

    [Fact]
    public void The_text_editing_example_runs()
    {
        var gds = new GDS(File.ReadAllBytes(Sample));

        string text = gds.AsText();
        var edited = GDS.FromText(text);

        Assert.Equal(gds.Records.Count, edited.Records.Count);
    }

    ///<summary>
    ///Both readmes ship inside their package, so both have to be there to ship at all - a missing one is a
    ///pack-time error that would only turn up on the tag that was meant to be the release.
    ///</summary>
    [Theory]
    [InlineData("GdsII")]
    [InlineData("GdsII.Cli")]
    public void The_package_readme_is_where_the_project_says_it_is(string project)
    {
        Assert.True(File.Exists(Path.Combine(GdsTestData.RepositoryRoot, project, "README.md")));
    }

    ///<summary>
    ///And the license files the packages name. The Unlicense is what this project is under; the notices
    ///file is what carries everyone else's terms along, which is the whole of what any of them asks for.
    ///</summary>
    [Theory]
    [InlineData("UNLICENSE")]
    [InlineData("docs/THIRD-PARTY-NOTICES.md")]
    [InlineData("GdsII/Clipper2/LICENSE")]
    public void The_license_files_the_packages_carry_are_there(string path)
    {
        Assert.True(File.Exists(Path.Combine(GdsTestData.RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar))));
    }

    ///
    ///And the icon, which is the app's own so the packages and the viewer are one thing on a search page.
    ///
    ///**Both projects reach across to it rather than keeping a copy**, so the path is the thing that can
    ///break - and it breaks at `dotnet pack`, which on this repository is the release tag rather than the
    ///commit. The size limit is nuget.org's own; the format is checked because a PackageIcon that is not one
    ///of these is rejected on push, after the version has already been decided.
    ///
    [Fact]
    public void The_icon_the_packages_carry_is_there_and_within_what_nuget_takes()
    {
        string path = Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", "icon-192.png");

        Assert.True(File.Exists(path), $"The packages name {path} as their icon.");

        var icon = new FileInfo(path);

        //One megabyte, which is nuget.org's ceiling.
        Assert.InRange(icon.Length, 1, 1024 * 1024);

        //A PNG, by its own first bytes rather than by its name.
        byte[] first = File.ReadAllBytes(path)[..8];

        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, first);
    }

    ///<summary>
    ///Both projects have to name it, or one package ships without a picture - which is invisible until the
    ///listing page is already published.
    ///</summary>
    [Theory]
    [InlineData("GdsII/GdsII.csproj")]
    [InlineData("GdsII.Cli/GdsII.Cli.csproj")]
    public void Both_projects_declare_the_icon(string project)
    {
        string text = File.ReadAllText(Path.Combine(GdsTestData.RepositoryRoot, project.Replace('/', Path.DirectorySeparatorChar)));

        Assert.Contains("<PackageIcon>icon.png</PackageIcon>", text);

        //And pack it under the name PackageIcon just claimed, since the two are checked against each other
        //at pack time and not before.
        Assert.Contains("PackagePath=\"\\icon.png\"", text);
    }
}
