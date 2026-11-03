using System.Text.RegularExpressions;
using GdsII;

namespace GDSViewer.Tests;

///
///The gf180mcu layermap that ships in wwwroot, held to GlobalFoundries' own table the way ShippedLayermapTests holds
///sky130's to its own - asked for 2026-11-02, when gf180mcu was built in beside sky130.
///
///**The authority is the design manual's drawn-layer table**: docs/physical_verification/design_manual/tables_clear/
///06_Drawn_layer10.csv in github.com/google/gf180mcu-pdk at de3240d, vendored at OtherResources/GF180 under the PDK's
///Apache License 2.0. The pairs and names are read out of it rather than repeated here, so a row that drifts from it
///fails. GF180's numbers do not climb with the stack - Via2 is 38, Metal3 42, Via3 40 and Metal5 81 - which is exactly
///the shape of mistake a map written from memory makes, and the one sky130's first map made.
///
public class Gf180LayermapTests
{
    private const string Layermap = "gf180mcu-roles.csv";

    private const string Vendored = "OtherResources/GF180/06_Drawn_layer10.csv";

    ///<summary>The names, stack and roles the shipped map gives, as <see cref="LayerNames.Parse"/> reads them.</summary>
    private static LayerNames Shipped()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, Layermap);

        Assert.True(File.Exists(path), $"{Layermap} is not in {GdsTestData.SampleDirectory}");

        var read = LayerNames.Parse(File.ReadAllText(path));

        Assert.Empty(read.Problems);

        return read;
    }

    ///<summary>One row of the manual's table: what kind of layer it is - Drawn, Marking, EDAMark or DevMark - and its name.</summary>
    private sealed record ManualRow(string Type, string Name);

    ///
    ///**The four pairs the map takes from Magic's tech file instead** - open_pdks' gf180mcu/magic/gf180mcu.tech, at
    ///f55b793 - which the manual does not list and the bundled layouts carry: NWELLTXT and PWELLTXT, the wells' labels,
    ///and SUBCUT and VTEXT, which Magic reads as a marker and as text it ignores. Written here because nothing vendored
    ///says them, and short enough to read at a glance.
    ///
    private static readonly Dictionary<LayerKey, ManualRow> FromMagic = new Dictionary<LayerKey, ManualRow>
    {
        [new LayerKey(21, 10)] = new ManualRow("Drawn", "Nwell.label"),
        [new LayerKey(204, 10)] = new ManualRow("Drawn", "LVPWELL.label"),
        [new LayerKey(23, 5)] = new ManualRow("Marking", "SUBCUT"),
        [new LayerKey(63, 63)] = new ManualRow("Marking", "VTEXT")
    };

    ///
    ///The manual's rows by pair, each name as the map writes it: the footnote numbers off - "COMP (1)" is COMP - and a
    ///purpose the way this app writes one, so Metal1_Label is Metal1.label, Metal1_Dummy Metal1.dummy, Metal1_Slot
    ///Metal1.slot and Metal1_BLK Metal1.blockage. Its two names for 151/5, IND_MK and IND_MK1, are one layer, and the
    ///first is kept. And Magic's four beside them - see <see cref="FromMagic"/>.
    ///
    private static Dictionary<LayerKey, ManualRow> Gf180Layers()
    {
        var found = new Dictionary<LayerKey, ManualRow>(FromMagic);

        foreach (string line in File.ReadAllLines(Path.Combine(GdsTestData.RepositoryRoot, Vendored)).Skip(1))
        {
            //A purpose can hold commas inside its quotes, so the pair is read from the end and the name from the start.
            string[] fields = line.Split(',');

            if (fields.Length < 5)
                continue;

            var key = new LayerKey(short.Parse(fields[^2].Trim()), short.Parse(fields[^1].Trim()));

            if (!found.ContainsKey(key))
                found[key] = new ManualRow(fields[0].Trim(), asTheMapWritesIt(fields[1]));
        }

        Assert.NotEmpty(found);

        return found;
    }

    private static string asTheMapWritesIt(string manual)
    {
        string name = Regex.Replace(manual.Trim(), @"\s*\(\d+\)$", "");

        name = Regex.Replace(name, "^MetalT_BLK$", "MetalTop_BLK");
        name = Regex.Replace(name, "_Label$", ".label");
        name = Regex.Replace(name, "_Dummy$", ".dummy");
        name = Regex.Replace(name, "_Slot$", ".slot");

        return Regex.Replace(name, "_BLK$", ".blockage");
    }

    ///<summary>Every row of the map is a pair the manual draws, under the manual's name for it.</summary>
    [Fact]
    public void Every_row_is_a_pair_the_manual_draws_under_its_name()
    {
        var manual = Gf180Layers();
        var read = Shipped();

        foreach (var named in read.Names)
        {
            Assert.True(manual.TryGetValue(named.Key, out var row), $"{named.Value} is on {named.Key}, which the manual does not draw");
            Assert.Equal(row!.Name, named.Value);
        }

        Assert.Equal(104, read.Names.Count);
    }

    ///
    ///**And every pair the manual draws is in the map** - but two kinds left out on purpose. The purposes of MetalTop,
    ///which only the six-metal stack has: this is gf180mcuD, whose top metal is Metal5, and MetalTop itself and Via5
    ///are named, so a six-metal file reads, but their labels, fill, slots and blockages would be rows for a level this
    ///stack does not build. **And PR_bndry on 0/0**, where tool after tool puts whatever has no layer - naming it made a
    ///two-layer DXF drawing count as GF180's, and dxf.spec caught its layer names giving way. See the map's header.
    ///
    [Fact]
    public void Every_pair_the_manual_draws_is_named_but_0_0_and_the_purposes_of_the_six_metal_top()
    {
        var read = Shipped();

        var missing = Gf180Layers().Keys.Where(key => !read.Names.ContainsKey(key)).OrderBy(key => key).ToList();

        Assert.Equal(new[] { new LayerKey(0, 0), new LayerKey(53, 3), new LayerKey(53, 4), new LayerKey(53, 5), new LayerKey(53, 10) }, missing);
    }

    ///
    ///A via joins what it overlaps and a metal carries a net along itself - the test that would have caught sky130's
    ///first map. COMP takes no part, for the reason the map gives: a conductor there joins source to drain through the
    ///channel.
    ///
    [Theory]
    [InlineData("Poly2", LayerRole.Conductor)]
    [InlineData("Metal1", LayerRole.Conductor)]
    [InlineData("Metal2", LayerRole.Conductor)]
    [InlineData("Metal3", LayerRole.Conductor)]
    [InlineData("Metal4", LayerRole.Conductor)]
    [InlineData("Metal5", LayerRole.Conductor)]
    [InlineData("MetalTop", LayerRole.Conductor)]
    [InlineData("Contact", LayerRole.Via)]
    [InlineData("Via1", LayerRole.Via)]
    [InlineData("Via2", LayerRole.Via)]
    [InlineData("Via3", LayerRole.Via)]
    [InlineData("Via4", LayerRole.Via)]
    [InlineData("Via5", LayerRole.Via)]
    [InlineData("COMP", LayerRole.None)]
    [InlineData("FuseTop", LayerRole.None)]
    [InlineData("Pad", LayerRole.None)]
    public void The_metals_conduct_the_vias_join_and_COMP_takes_no_part(string name, LayerRole role)
    {
        var read = Shipped();
        var key = read.Names.Single(named => named.Value == name).Key;

        read.Roles.TryGetValue(key, out var given);

        Assert.Equal(role, given);
    }

    ///
    ///**A via reaches from the top of the conductor under it to the bottom of the one over it**, and the contact from
    ///the silicon to Metal1 - so each one meets both and passes neither in the 3D view.
    ///
    [Theory]
    [InlineData(null, "Contact", "Metal1")]
    [InlineData("Metal1", "Via1", "Metal2")]
    [InlineData("Metal2", "Via2", "Metal3")]
    [InlineData("Metal3", "Via3", "Metal4")]
    [InlineData("Metal4", "Via4", "Metal5")]
    public void Each_via_spans_the_gap_between_its_metals(string? below, string through, string above)
    {
        var read = Shipped();

        var via = stackOf(read, through);
        var over = stackOf(read, above);
        int bottom = 0;

        if (below is not null)
        {
            var under = stackOf(read, below);

            bottom = under.Height + under.Thickness;
        }

        Assert.Equal(bottom, via.Height);
        Assert.Equal(over.Height, via.Height + via.Thickness);
    }

    ///
    ///The stack is the PDK's cross-section of gf180mcuD, "1P5M (TM 11KA with MIM Option B) Structure": Metal1 910 over
    ///the silicon, every metal 0.55 thick but the top one, 1.1925 to the nearest nanometer, and Via4 0.9 tall where the
    ///others are 0.6, since IMD4 holds the MIM capacitor. Pinned so a slip in one row cannot move the stack above it.
    ///
    [Theory]
    [InlineData("Poly2", 0, 200)]
    [InlineData("Metal1", 910, 550)]
    [InlineData("Metal2", 2060, 550)]
    [InlineData("Metal3", 3210, 550)]
    [InlineData("Metal4", 4360, 550)]
    [InlineData("Via4", 4910, 900)]
    [InlineData("Metal5", 5810, 1193)]
    [InlineData("FuseTop", 4952, 295)]
    [InlineData("COMP", -320, 320)]
    public void The_stack_is_the_cross_sections(string name, int height, int thickness)
    {
        Assert.Equal((height, thickness), stackOf(Shipped(), name));
    }

    ///
    ///A label or a dummy fill takes the height of the layer it is on - a label sits on its metal, and fill is metal
    ///poured on the same level - and a slot, which is a hole, or a blockage, which is an instruction to a router, has
    ///none. The judgment the map's header records, stated here so a row that drifts from it fails.
    ///
    [Fact]
    public void A_label_or_fill_takes_its_layers_height_and_a_slot_or_blockage_has_none()
    {
        var read = Shipped();

        var byName = read.Names.ToDictionary(named => named.Value, named => named.Key);
        int counted = 0;

        foreach (var named in read.Names)
        {
            int dot = named.Value.IndexOf('.');

            if (dot < 0)
                continue;

            string on = named.Value[..dot];
            string purpose = named.Value[(dot + 1)..];

            Assert.True(byName.ContainsKey(on), $"{named.Value} is on {on}, which the map does not name");

            if (purpose == "label" || purpose == "dummy")
                Assert.Equal(stackOf(read, on), stackOf(read, named.Value));
            else
                Assert.False(read.Stack.ContainsKey(named.Key), $"{named.Value} has a height");

            counted++;
        }

        Assert.Equal(26, counted);
    }

    ///
    ///**The markers are named and nothing else.** Every row the manual calls a marking layer - for DRC, for LVS, for the
    ///router, or the border - has no height, so the 3D view leaves it out rather than hanging a cell-sized slab in the
    ///sky, and no role, so a trace does not walk through a marker.
    ///
    [Fact]
    public void A_marker_has_no_height_and_no_role()
    {
        var read = Shipped();
        int counted = 0;

        foreach (var row in Gf180Layers())
        {
            if (row.Value.Type == "Drawn" || !read.Names.ContainsKey(row.Key))
                continue;

            Assert.False(read.Stack.ContainsKey(row.Key), $"{row.Value.Name} has a height");
            Assert.False(read.Roles.ContainsKey(row.Key), $"{row.Value.Name} has a role");

            counted++;
        }

        Assert.Equal(47, counted);
    }

    ///
    ///**Every pair the bundled GF180 cells draw on is named** but their boundary, 0/0 - see the test above - and
    ///whatever carries a net in them stands in the 3D view. They are GF180's by every other layer they have.
    ///
    ///A new pair appearing here fails this test, which is the point: somebody then decides what it is - a film with a
    ///height, a label on one, or a marker with none - rather than it coming in as a number on a file the app ships.
    ///
    ///**The GF180 designs were read the same way when they were chosen**, each downloaded once, since none ships: every
    ///pair named but 58/0, which only GlobalFoundries' own I/O pad and SRAM carry and no public table names - the PDK's
    ///KLayout layer file lists it bare, as "58/0", and Magic does not read it at all. The wells' labels and SUBCUT, which
    ///the map takes from Magic, arrived with them.
    ///
    [Fact]
    public void Every_pair_the_bundled_gf180_cells_draw_on_is_named_but_their_boundary()
    {
        var read = Shipped();
        var carried = new HashSet<LayerKey>();
        int files = 0;

        foreach (string path in GdsTestData.Gf180SampleFiles())
        {
            foreach (var layer in new GDS(File.ReadAllBytes(path)).AdditionalInformation.Layers)
                carried.Add(layer.Key);

            files++;
        }

        Assert.Equal(229, files);

        Assert.Equal(new[] { new LayerKey(0, 0) }, carried.Where(key => !read.Names.ContainsKey(key)).OrderBy(key => key));

        foreach (var key in carried.Where(key => read.Roles.ContainsKey(key)))
            Assert.True(read.Stack.ContainsKey(key), $"{read.Names[key]} carries a net in the bundled cells and has no height");
    }

    ///<summary>Every bundled GF180 cell is written in nanometers, which is what the map's heights and the deck's values are in.</summary>
    [Fact]
    public void Every_bundled_gf180_cell_is_drawn_in_nanometers()
    {
        foreach (string path in GdsTestData.Gf180SampleFiles())
        {
            var gds = new GDS(File.ReadAllBytes(path));

            Assert.True(gds.MicronsPerDatabaseUnit is double microns && Math.Abs(microns - 0.001) < 1e-12, $"{Path.GetFileName(path)} is in {gds.MicronsPerDatabaseUnit} um units");
        }
    }

    ///<summary>MetalTop and Via5 belong to the six-metal stack: named, with their roles, and no height in this one.</summary>
    [Theory]
    [InlineData("MetalTop")]
    [InlineData("Via5")]
    public void The_six_metal_stacks_top_has_no_height_here(string name)
    {
        var read = Shipped();
        var key = read.Names.Single(named => named.Value == name).Key;

        Assert.False(read.Stack.ContainsKey(key));
        Assert.True(read.Roles.ContainsKey(key));
    }

    private static (int Height, int Thickness) stackOf(LayerNames read, string name)
    {
        var key = read.Names.Single(named => named.Value == name).Key;

        Assert.True(read.Stack.TryGetValue(key, out var stack), $"{name} has no height");

        return stack;
    }
}
