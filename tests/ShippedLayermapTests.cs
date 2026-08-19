using System.IO.Compression;
using System.Text.RegularExpressions;
using GdsII;

namespace GDSViewer.Tests;

///
///The layermap that ships in wwwroot, checked against sky130's own table rather than against anybody's
///memory of it.
///
///**Because four of its rows were wrong and nothing noticed.** It said 69/20 was a via called "via" when
///69/20 is met2 - which would have made every met2 shape join whatever it happened to overlap, the exact
///opposite of what a via role is for - and it said 70/20 was met2 when 70/20 is met3. Two more rows named
///the wrong implant. None of it showed, because every bundled example is a standard cell that stops at met1,
///so the wrong half of the file is the half nothing exercises.
///
///That is precisely the shape of thing a test is for: a claim about a PDK, in a file nobody compiles, which
///only fails on a layout none of the examples is. The authority is
///`OtherResources/Sky130/skywater130-main.zip`, the gdsfactory sky130 package vendored in this repo - so
///this reads the numbers out of it rather than repeating them here, and a row that drifts from it fails.
///
public class ShippedLayermapTests
{
    private const string Layermap = "sky130-roles.csv";

    private const string Vendored = "OtherResources/Sky130/skywater130-main.zip";

    private const string LayersPy = "skywater130-main/sky130/layers.py";

    ///<summary>The names the shipped map gives, by pair, as <see cref="LayerNames.Parse"/> reads them.</summary>
    private static LayerNames Shipped()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, Layermap);

        Assert.True(File.Exists(path), $"{Layermap} is not in {GdsTestData.SampleDirectory}");

        return LayerNames.Parse(File.ReadAllText(path));
    }

    ///
    ///sky130's own name for each drawing layer, out of layers.py.
    ///
    ///**Both halves of that file, because neither is complete.** The trailing block is the short list with a
    ///comment on each - `via = (68, 44)  # Contact from metal 1 to metal 2` is the line that settles the
    ///argument this test exists to settle - but it holds no `pwell`, `tap` or `text`. The `LayerMap` class
    ///above it does, spelling the purpose into the name, so `pwelldrawing` is read as `pwell`.
    ///
    ///The class first and the block over the top of it: they agree everywhere both have an entry, and the
    ///block is the one with the comments saying what a layer is *for*.
    ///
    private static Dictionary<LayerKey, string> Sky130Drawing()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(GdsTestData.RepositoryRoot, Vendored));

        var entry = zip.GetEntry(LayersPy);

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());

        string text = reader.ReadToEnd();

        var found = new Dictionary<LayerKey, string>();

        //The class, drawing purposes only - a "net", "pin" or "label" entry is a different layer.
        foreach (Match match in Regex.Matches(text, @"^\s+(\w+?)drawing: Layer = \((\d+), (\d+)\)", RegexOptions.Multiline))
            found[Pair(match)] = match.Groups[1].Value;

        //Then the bare assignments at the end, which are the same layers said again with a comment.
        foreach (Match match in Regex.Matches(text, @"^(\w+) = \((\d+), (\d+)\)", RegexOptions.Multiline))
            found[Pair(match)] = match.Groups[1].Value;

        Assert.NotEmpty(found);

        return found;
    }

    private static LayerKey Pair(Match match)
    {
        return new LayerKey(short.Parse(match.Groups[2].Value), short.Parse(match.Groups[3].Value));
    }

    ///
    ///Every drawing layer the map names is the layer sky130 says it is.
    ///
    ///Only the drawing layers: the map also names pin and label purposes, which the block this reads does
    ///not carry - those are checked below by the shape of their names instead.
    ///
    [Fact]
    public void Every_named_drawing_layer_matches_sky130()
    {
        var sky130 = Sky130Drawing();

        foreach (var named in Shipped().Names)
        {
            //A purpose-qualified name is not a drawing layer, so the block has nothing to say about it.
            if (named.Value.Contains('.'))
                continue;

            if (!sky130.TryGetValue(named.Key, out string? expected))
                continue;

            Assert.Equal(expected, named.Value);
        }
    }

    ///
    ///And the layer each name belongs to is the one the map puts it on.
    ///
    ///The other direction, which is the one that catches a *swap*: two rows naming each other's pairs
    ///satisfy the test above for neither and both, since each pair does have a name and each name does
    ///exist. Measured against the first version of this file, where met2 and met3 were exactly that.
    ///
    [Theory]
    [InlineData("licon1")]
    [InlineData("li1")]
    [InlineData("mcon")]
    [InlineData("met1")]
    [InlineData("via")]
    [InlineData("met2")]
    [InlineData("via2")]
    [InlineData("met3")]
    [InlineData("via3")]
    [InlineData("met4")]
    [InlineData("via4")]
    [InlineData("met5")]
    [InlineData("poly")]
    [InlineData("diff")]
    [InlineData("nwell")]
    [InlineData("pwell")]
    [InlineData("nsdm")]
    [InlineData("psdm")]
    [InlineData("npc")]
    [InlineData("tap")]
    [InlineData("text")]
    public void Each_name_sits_on_the_pair_sky130_gives_it(string name)
    {
        var sky130 = Sky130Drawing();

        var wanted = sky130.Where(entry => entry.Value == name).Select(entry => entry.Key).ToList();

        Assert.Single(wanted);

        var shipped = Shipped().Names.Where(entry => entry.Value == name).Select(entry => entry.Key).ToList();

        Assert.Single(shipped);
        Assert.Equal(wanted[0], shipped[0]);
    }

    ///
    ///A via joins what it overlaps and a metal carries a net along itself, and the two are not interchangeable.
    ///
    ///**This is the one that would have caught the original bug.** Giving met2's pair a via's role does not
    ///produce a wrong name or a parse error - it produces a layout where every met2 shape is shorted to
    ///whatever sits under it, silently, on the first file anybody opens that has two metals.
    ///
    [Fact]
    public void The_metals_conduct_and_the_vias_join()
    {
        var read = Shipped();

        foreach (var named in read.Names)
        {
            read.Roles.TryGetValue(named.Key, out var role);

            if (Regex.IsMatch(named.Value, @"^met\d$") || named.Value == "li1" || named.Value == "poly" || named.Value == "diff")
                Assert.Equal(LayerRole.Conductor, role);

            if (Regex.IsMatch(named.Value, @"^via\d?$") || named.Value == "licon1" || named.Value == "mcon")
                Assert.Equal(LayerRole.Via, role);
        }
    }

    ///
    ///Every level of the stack is reachable from the one below it.
    ///
    ///A metal with no via under it is a net that stops climbing, which reads as a trace that mysteriously
    ///ends - and is what the first version did above met1, where the 68/44 row was simply absent.
    ///
    [Theory]
    [InlineData("li1", "mcon", "met1")]
    [InlineData("met1", "via", "met2")]
    [InlineData("met2", "via2", "met3")]
    [InlineData("met3", "via3", "met4")]
    [InlineData("met4", "via4", "met5")]
    public void Each_metal_is_joined_to_the_next_by_a_via(string below, string through, string above)
    {
        var read = Shipped();

        foreach (string name in new[] { below, through, above })
            Assert.Contains(name, read.Names.Values);
    }

    ///
    ///A pin or a label carries no role.
    ///
    ///A pin shape sits exactly on the metal it names, so calling it a conductor puts a second copy of every
    ///pin into the net it is already part of. A label needs no role either: the name of a traced net is read
    ///from whatever text lands on it, whatever that layer is said to be.
    ///
    [Fact]
    public void The_pin_and_label_purposes_take_no_part()
    {
        var read = Shipped();

        foreach (var named in read.Names)
        {
            if (!named.Value.EndsWith(".pin") && !named.Value.EndsWith(".label"))
                continue;

            read.Roles.TryGetValue(named.Key, out var role);

            Assert.Equal(LayerRole.None, role);
        }
    }

    ///<summary>And it reads without complaint, which is the thing a shifted column breaks.</summary>
    [Fact]
    public void It_reads_with_no_problems_reported()
    {
        var read = Shipped();

        Assert.Empty(read.Problems);

        //And says something about every row: a file of comments would pass everything above.
        Assert.True(read.Count >= 20, $"the map only says anything about {read.Count} layers");
    }
}
