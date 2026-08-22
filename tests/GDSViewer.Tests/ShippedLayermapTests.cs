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

    ///
    ///sky130's own pair for each pin and label purpose, out of the same file.
    ///
    ///Spelled the way that file spells them - `nwelllabel`, `pwellpin` - so a `nwell.label` row here is
    ///checked against the entry it claims to be rather than against anybody's reading of the numbers.
    ///
    private static Dictionary<string, LayerKey> Sky130Purposes()
    {
        using var zip = ZipFile.OpenRead(Path.Combine(GdsTestData.RepositoryRoot, Vendored));

        var entry = zip.GetEntry(LayersPy);

        Assert.NotNull(entry);

        using var reader = new StreamReader(entry!.Open());

        string text = reader.ReadToEnd();

        var found = new Dictionary<string, LayerKey>();

        foreach (Match match in Regex.Matches(text, @"^\s+(\w+?(?:pin|label)): Layer = \((\d+), (\d+)\)", RegexOptions.Multiline))
            found[match.Groups[1].Value] = new LayerKey(short.Parse(match.Groups[2].Value), short.Parse(match.Groups[3].Value));

        Assert.NotEmpty(found);

        return found;
    }

    ///
    ///Every pin and label purpose the map names sits on the pair sky130 gives it.
    ///
    ///**This is the check that was missing when it mattered.** The map named li1's and met1's pin and label
    ///purposes and not the wells', so four layers every bundled cell carries had no row at all - and two of
    ///those are not even on the layer they belong to: pwell is 64/44, its label is 64/59 and its pin is
    ///122/16. Every_named_drawing_layer_matches_sky130 cannot see any of this, because a dotted name is the
    ///first thing it skips.
    ///
    [Fact]
    public void Every_pin_and_label_purpose_sits_on_the_pair_sky130_gives_it()
    {
        var sky130 = Sky130Purposes();

        int counted = 0;

        foreach (var named in Shipped().Names)
        {
            int dot = named.Value.IndexOf('.');

            if (dot < 0)
                continue;

            string spelled = named.Value.Remove(dot, 1);

            Assert.True(sky130.ContainsKey(spelled), $"sky130 has no layer called {named.Value}");
            Assert.Equal(sky130[spelled], named.Key);

            counted++;
        }

        //And it found some, or the loop above passes by never running.
        Assert.Equal(12, counted);
    }

    ///
    ///And each of them takes the height and thickness of the layer it annotates.
    ///
    ///A pin is drawn on the metal it names, not a step off it - which is the judgement the map's own header
    ///records, stated here so a row that drifts from it fails. It is also what keeps a pin out of the set
    ///the 3D view leaves out: see HasProcessStack.
    ///
    [Fact]
    public void Every_pin_and_label_purpose_takes_the_height_of_what_it_annotates()
    {
        var read = Shipped();

        //The pair each name is on, so a purpose can find the drawing layer it belongs to by name.
        var byName = new Dictionary<string, LayerKey>();

        foreach (var named in read.Names)
            byName[named.Value] = named.Key;

        int counted = 0;

        foreach (var named in read.Names)
        {
            int dot = named.Value.IndexOf('.');

            if (dot < 0)
                continue;

            string annotates = named.Value[..dot];

            Assert.True(byName.ContainsKey(annotates), $"{named.Value} annotates {annotates}, which the map does not name");

            Assert.True(read.Stack.TryGetValue(named.Key, out var purpose), $"{named.Value} has no height");
            Assert.True(read.Stack.TryGetValue(byName[annotates], out var drawing), $"{annotates} has no height");

            Assert.Equal(drawing, purpose);

            counted++;
        }

        Assert.Equal(12, counted);
    }

    ///
    ///Layers the bundled corpus carries that the map deliberately says nothing about.
    ///
    ///**None of them is on a wafer.** Six are area or extraction markers - `areaid.standardc` and
    ///`areaid.diode` mark what a region is for, and `poly.short`, `met5.short`, `diff.res` and `diff.cut`
    ///mark shapes for LVS and extraction rather than describing a film. `text` is where a name goes when it
    ///belongs to the drawing rather than to a layer. `236/0` is the cell outline the standard cell library
    ///draws around each cell, and `63/20` and `251/0` appear nowhere in sky130's own tables at all - not in
    ///layers.py, layers.lyp or layers.yaml - so they are somebody's tooling rather than the process.
    ///
    ///There is no height to give any of them, which is why the 3D view leaves them out rather than placing
    ///them somewhere. Being listed here is the decision having been made, not the question being skipped.
    ///
    private static readonly LayerKey[] NotOnTheWafer =
    {
        new LayerKey(63, 20),
        new LayerKey(65, 13),
        new LayerKey(65, 14),
        new LayerKey(66, 15),
        new LayerKey(72, 15),
        new LayerKey(81, 4),
        new LayerKey(81, 23),
        new LayerKey(83, 44),
        new LayerKey(236, 0),
        new LayerKey(251, 0)
    };

    ///
    ///Every layer the bundled examples actually draw on has a height, or is one of the three above.
    ///
    ///**This is the regression guard, and it is stated over the corpus rather than over one cell.** A row
    ///missing from this map is not a cosmetic gap: a layer with no height is one the 3D view leaves out
    ///entirely, so a forgotten row is geometry that stops being drawn. Eight layers were in that position
    ///and five of them should not have been.
    ///
    ///A new pair appearing here fails this test, which is the point - somebody then decides whether it is a
    ///film with a height or a marker with none, rather than it defaulting quietly into either.
    ///
    [Fact]
    public void Every_layer_the_bundled_cells_draw_on_is_mapped_or_listed_as_not_on_the_wafer()
    {
        var read = Shipped();
        var carried = new HashSet<LayerKey>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            var gds = new GDS(File.ReadAllBytes(path));

            foreach (var layer in gds.AdditionalInformation.Layers)
                carried.Add(layer.Key);
        }

        Assert.NotEmpty(carried);

        var missing = new List<LayerKey>();

        foreach (var key in carried)
        {
            if (read.Stack.ContainsKey(key) || NotOnTheWafer.Contains(key))
                continue;

            missing.Add(key);
        }

        Assert.True(missing.Count == 0, $"no height for {string.Join(", ", missing)}");
    }
}
