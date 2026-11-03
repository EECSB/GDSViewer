using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GdsII;

namespace GDSViewer.Tests;

///
///The ihp-sg13g2 layermap that ships in wwwroot, held to IHP's own tables the way Gf180LayermapTests holds gf180mcu's -
///asked for 2026-11-02, when ihp-sg13g2 was built in beside sky130 and gf180mcu.
///
///**The authority is the layer table of the SG13G2 layout rules**, chapter 2 of SG13G2_os_layout_rules.pdf Rev. 0.4 in
///github.com/IHP-GmbH/IHP-Open-PDK at 5e6d592, read out of the PDF into OtherResources/IHP/sg13g2-layer-table.csv. That
///reading is itself held to the PDK's own KLayout layer properties, sg13g2.lyp, vendored beside it as published - the
///first test here - so the two sources check each other. Both under the PDK's Apache License 2.0.
///
public class IhpLayermapTests
{
    private const string Layermap = "ihp-sg13g2-roles.csv";

    private const string Table = "OtherResources/IHP/sg13g2-layer-table.csv";

    private const string Properties = "OtherResources/IHP/sg13g2.lyp";

    private const string Script = "OtherResources/IHP/sg13g2_beol.lyd25";

    ///
    ///**The five pairs the map takes from sg13g2.lyp rather than the table**, each drawn by IHP's own libraries or by a
    ///design the picker lists: the boundaries of the standard cells, the inductors and the seal ring, GatPoly's
    ///boundary, and Metal1.label, which the layout flows write some of their pin names on beside Metal1.text.
    ///
    private static readonly Dictionary<LayerKey, string> FromProperties = new Dictionary<LayerKey, string>
    {
        [new LayerKey(189, 4)] = "prBoundary.boundary",
        [new LayerKey(27, 4)] = "IND.boundary",
        [new LayerKey(39, 4)] = "EdgeSeal.boundary",
        [new LayerKey(5, 4)] = "GatPoly.boundary",
        [new LayerKey(8, 1)] = "Metal1.label"
    };

    ///<summary>The layers chapter 3.2 forbids in an SG13G2 design: layout carrying one is rejected at tape-in.</summary>
    private static readonly string[] Forbidden = { "BiWind", "PEmWind", "BasPoly", "DeepCo", "PEmPoly", "EmPoly", "LDMOS", "PBiWind", "NoDRC", "Flash", "ColWind" };

    ///<summary>The marking layers chapter 3.1 names - kept out of mask generation - less NoDRC, which 3.2 forbids, and Scribe, which the table does not list.</summary>
    private static readonly string[] Markers = { "DigiBnd", "DigiSub", "dfpad", "EdgeSeal", "HeatRes", "HeatTrans", "IND", "NoMetFiller", "NoRCX", "RadHard", "Recog", "RES", "SRAM", "TEXT" };

    ///<summary>The names, stack and roles the shipped map gives, as <see cref="LayerNames.Parse"/> reads them.</summary>
    private static LayerNames Shipped()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, Layermap);

        Assert.True(File.Exists(path), $"{Layermap} is not in {GdsTestData.SampleDirectory}");

        var read = LayerNames.Parse(File.ReadAllText(path));

        Assert.Empty(read.Problems);

        return read;
    }

    ///<summary>One row of the layout rules' layer table: its layer and purpose.</summary>
    private sealed record TableRow(string Layer, string Purpose)
    {
        ///<summary>As the map writes it: the drawing purpose bare, Metal1, and any other after a dot, Metal1.pin.</summary>
        public string Name
        {
            get
            {
                if (Purpose == "drawing")
                    return Layer;

                return $"{Layer}.{Purpose}";
            }
        }
    }

    ///<summary>The table by pair. Its description, last, can hold commas; the four fields before it never do.</summary>
    private static Dictionary<LayerKey, TableRow> TableRows()
    {
        var found = new Dictionary<LayerKey, TableRow>();

        foreach (string line in File.ReadAllLines(Path.Combine(GdsTestData.RepositoryRoot, Table)).Skip(1))
        {
            string[] fields = line.Split(',');

            if (fields.Length < 4)
                continue;

            var key = new LayerKey(short.Parse(fields[2], CultureInfo.InvariantCulture), short.Parse(fields[3], CultureInfo.InvariantCulture));

            Assert.False(found.ContainsKey(key), $"{key} is in the table twice");

            found[key] = new TableRow(fields[0], fields[1]);
        }

        Assert.NotEmpty(found);

        return found;
    }

    ///<summary>sg13g2.lyp's names by pair, as KLayout shows them - Metal1.drawing, Metal1.pin - from each entry's source, "8/0@1".</summary>
    private static Dictionary<LayerKey, string> PropertyRows()
    {
        var found = new Dictionary<LayerKey, string>();
        var document = XDocument.Load(Path.Combine(GdsTestData.RepositoryRoot, Properties));

        foreach (var entry in document.Descendants("properties"))
        {
            string? name = entry.Element("name")?.Value.Trim();
            string? source = entry.Element("source")?.Value.Trim();

            if (name is null || source is null)
                continue;

            string[] pair = source.Split('@')[0].Split('/');

            found[new LayerKey(short.Parse(pair[0], CultureInfo.InvariantCulture), short.Parse(pair[1], CultureInfo.InvariantCulture))] = name;
        }

        return found;
    }

    ///
    ///**The table as read out of the PDF is the PDK's own, row for row**: every one of its 296 pairs is in sg13g2.lyp,
    ///the layer properties KLayout loads for SG13G2, under the same layer and purpose. A row misread out of the PDF - a
    ///number shifted onto the line above, which pdftotext's layout mode does to this table - fails here.
    ///
    [Fact]
    public void Every_row_of_the_table_is_in_the_pdks_own_layer_properties_under_its_name()
    {
        var properties = PropertyRows();
        var table = TableRows();

        foreach (var row in table)
        {
            Assert.True(properties.TryGetValue(row.Key, out var named), $"{row.Value.Layer}.{row.Value.Purpose} {row.Key} is not in sg13g2.lyp");
            Assert.Equal($"{row.Value.Layer}.{row.Value.Purpose}", named);
        }

        Assert.Equal(296, table.Count);
    }

    ///<summary>Every row of the map is a pair the table lists, under its name - or one of the five sg13g2.lyp names that the table does not list.</summary>
    [Fact]
    public void Every_row_is_a_pair_the_table_lists_or_one_of_five_from_the_layer_properties()
    {
        var table = TableRows();
        var properties = PropertyRows();
        var read = Shipped();

        foreach (var named in read.Names)
        {
            if (FromProperties.TryGetValue(named.Key, out var extra))
            {
                Assert.False(table.ContainsKey(named.Key), $"{named.Key} is in the table after all");
                Assert.Equal(extra, named.Value);
                Assert.Equal(extra, properties[named.Key]);

                continue;
            }

            Assert.True(table.TryGetValue(named.Key, out var row), $"{named.Value} is on {named.Key}, which the table does not list");
            Assert.Equal(row!.Name, named.Value);
        }

        Assert.Equal(195, read.Names.Count);
    }

    ///
    ///**A layer the map takes comes with every row the table has for it** - its pins, its text, its fill and the
    ///instructions on it - so a purpose of a named layer is never a bare number. The layers it leaves out are the ones
    ///SG13G2's rules never govern - graphene, waveguides, sensors and MEMS - and the forbidden ones below.
    ///
    [Fact]
    public void A_layer_the_map_takes_comes_with_all_its_rows()
    {
        var read = Shipped();
        var taken = read.Names.Values.Select(name => name.Split('.')[0]).ToHashSet();
        var missing = TableRows().Where(row => taken.Contains(row.Value.Layer) && !read.Names.ContainsKey(row.Key)).Select(row => row.Value.Name).ToList();

        Assert.Empty(missing);
        Assert.Equal(50, taken.Count);
    }

    ///
    ///**No layer chapter 3.2 forbids is named**, so one drawn shows as a bare number in the layer panel. Six of its
    ///eleven are in chapter 2's table at all - BiWind, BasPoly, DeepCo, EmPoly, NoDRC and ColWind - and the other five,
    ///PEmWind, PEmPoly, LDMOS, PBiWind and Flash, are not, so the map could not name them if it tried.
    ///
    [Fact]
    public void No_layer_the_rules_forbid_is_named()
    {
        var read = Shipped();
        var forbidden = TableRows().Where(row => Forbidden.Contains(row.Value.Layer)).ToList();

        Assert.Equal(6, forbidden.Select(row => row.Value.Layer).Distinct().Count());

        foreach (var row in forbidden)
            Assert.False(read.Names.ContainsKey(row.Key), $"{row.Value.Name} is forbidden and named");
    }

    ///
    ///A via joins what it overlaps and a metal carries a net along itself. Activ takes no part, for the reason the map
    ///gives, and neither do MIM and Vmim: a via on the plate also overlaps the Metal5 under it, which IHP made Vmim to
    ///keep tools from reading as a short.
    ///
    [Theory]
    [InlineData("GatPoly", LayerRole.Conductor)]
    [InlineData("Metal1", LayerRole.Conductor)]
    [InlineData("Metal2", LayerRole.Conductor)]
    [InlineData("Metal3", LayerRole.Conductor)]
    [InlineData("Metal4", LayerRole.Conductor)]
    [InlineData("Metal5", LayerRole.Conductor)]
    [InlineData("TopMetal1", LayerRole.Conductor)]
    [InlineData("TopMetal2", LayerRole.Conductor)]
    [InlineData("Metal1.mask", LayerRole.Conductor)]
    [InlineData("Cont", LayerRole.Via)]
    [InlineData("Via1", LayerRole.Via)]
    [InlineData("Via2", LayerRole.Via)]
    [InlineData("Via3", LayerRole.Via)]
    [InlineData("Via4", LayerRole.Via)]
    [InlineData("TopVia1", LayerRole.Via)]
    [InlineData("TopVia2", LayerRole.Via)]
    [InlineData("Activ", LayerRole.None)]
    [InlineData("MIM", LayerRole.None)]
    [InlineData("Vmim", LayerRole.None)]
    [InlineData("Passiv", LayerRole.None)]
    [InlineData("Metal1.filler", LayerRole.None)]
    public void The_metals_conduct_the_vias_join_and_Activ_and_the_capacitor_take_no_part(string name, LayerRole role)
    {
        var read = Shipped();
        var key = read.Names.Single(named => named.Value == name).Key;

        read.Roles.TryGetValue(key, out var given);

        Assert.Equal(role, given);
    }

    ///<summary>**A via reaches from the top of the conductor under it to the bottom of the one over it**, and the contact from the silicon to Metal1.</summary>
    [Theory]
    [InlineData(null, "Cont", "Metal1")]
    [InlineData("Metal1", "Via1", "Metal2")]
    [InlineData("Metal2", "Via2", "Metal3")]
    [InlineData("Metal3", "Via3", "Metal4")]
    [InlineData("Metal4", "Via4", "Metal5")]
    [InlineData("Metal5", "TopVia1", "TopMetal1")]
    [InlineData("TopMetal1", "TopVia2", "TopMetal2")]
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
    ///The stack is the process specification's, 2.16: a 400 nm trench, gate poly 160 thick, 640 of oxide under Metal1,
    ///Metal1 420 thick and the others 490 over 540 each, then 850, 2000, 2800 and 3000 for the top vias and metals - and
    ///the MIM's 150 nm plate 40 nm over Metal5, with Vmim from it up to TopMetal1.
    ///
    [Theory]
    [InlineData("Activ", -400, 400)]
    [InlineData("GatPoly", 0, 160)]
    [InlineData("Metal1", 640, 420)]
    [InlineData("Metal2", 1600, 490)]
    [InlineData("Metal3", 2630, 490)]
    [InlineData("Metal4", 3660, 490)]
    [InlineData("Metal5", 4690, 490)]
    [InlineData("TopMetal1", 6030, 2000)]
    [InlineData("TopMetal2", 10830, 3000)]
    [InlineData("MIM", 5220, 150)]
    [InlineData("Vmim", 5370, 660)]
    public void The_stack_is_the_process_specifications(string name, int height, int thickness)
    {
        Assert.Equal((height, thickness), stackOf(Shipped(), name));
    }

    ///
    ///**And the PDK's own 2.5D script builds the same metals and vias**, sg13g2_beol.lyd25, 400 nm higher - it starts at
    ///the bottom of the trench rather than at the silicon surface. A cross-check from a second of IHP's files, so a
    ///thickness copied wrong out of the specification fails here too.
    ///
    [Fact]
    public void The_metals_and_vias_stand_where_the_pdks_2_5d_script_puts_them_less_the_trench()
    {
        var read = Shipped();
        string script = File.ReadAllText(Path.Combine(GdsTestData.RepositoryRoot, Script));
        var built = Regex.Matches(script, @"z\((\w+),\s*name:""[^""]*"",\s*zstart:\s*([\d.]+),\s*height:\s*([\d.]+)");
        int checkedLayers = 0;

        foreach (Match match in built)
        {
            string name = match.Groups[1].Value;

            if (!Regex.IsMatch(name, @"^(Metal\d|Via\d|TopMetal\d|TopVia\d)$"))
                continue;

            int height = (int)Math.Round(double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) * 1000) - 400;
            int thickness = (int)Math.Round(double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) * 1000);

            Assert.Equal((height, thickness), stackOf(read, name));

            checkedLayers++;
        }

        Assert.Equal(13, checkedLayers);
    }

    ///
    ///**A pin, a word or a copy takes the height of the layer it is on** - a pin and its text sit on their metal, and fill
    ///and the mask shop's additions are the same film - and every other purpose, an instruction to a tool, has none. The
    ///judgment the map's header records, stated here so a row that drifts from it fails.
    ///
    [Fact]
    public void A_pin_word_or_copy_takes_its_layers_height_and_an_instruction_has_none()
    {
        var read = Shipped();
        var material = new[] { "pin", "text", "label", "mask", "filler", "sbump", "pillar" };
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

            if (material.Contains(purpose) && read.Stack.ContainsKey(byName[on]))
                Assert.Equal(stackOf(read, on), stackOf(read, named.Value));
            else
                Assert.False(read.Stack.ContainsKey(named.Key), $"{named.Value} has a height");

            counted++;
        }

        Assert.Equal(145, counted);
    }

    ///
    ///**The marking layers are named and nothing else.** Every row on a layer chapter 3.1 keeps out of mask generation has
    ///no height, so the 3D view leaves it out, and no role, so a trace does not walk through a marker.
    ///
    [Fact]
    public void A_marker_has_no_height_and_no_role()
    {
        var read = Shipped();
        int counted = 0;

        foreach (var named in read.Names.Where(named => Markers.Contains(named.Value.Split('.')[0])))
        {
            Assert.False(read.Stack.ContainsKey(named.Key), $"{named.Value} has a height");
            Assert.False(read.Roles.ContainsKey(named.Key), $"{named.Value} has a role");

            counted++;
        }

        Assert.Equal(54, counted);
    }

    ///
    ///**Every pair the bundled IHP cells draw on is named**, and whatever carries a net in them stands in the 3D view.
    ///
    ///**The IHP designs were read the same way when they were chosen**, each downloaded once, since none ships: every
    ///pair named but three kinds of the designers' own - 235/4, the die area the Tiny Tapeout flow writes on sky130's
    ///boundary number, 236/0 and 236/1 in the bandgap, and 201/0 to 204/0, the ports of the amplifier core's EM
    ///simulation. Metal1.label and GatPoly's boundary arrived with them.
    ///
    [Fact]
    public void Every_pair_the_bundled_ihp_cells_draw_on_is_named()
    {
        var read = Shipped();
        var carried = new HashSet<LayerKey>();
        int files = 0;

        foreach (string path in GdsTestData.IhpSampleFiles())
        {
            foreach (var layer in new GDS(File.ReadAllBytes(path)).AdditionalInformation.Layers)
                carried.Add(layer.Key);

            files++;
        }

        Assert.Equal(84, files);

        Assert.DoesNotContain(carried, key => !read.Names.ContainsKey(key));

        foreach (var key in carried.Where(key => read.Roles.ContainsKey(key)))
            Assert.True(read.Stack.ContainsKey(key), $"{read.Names[key]} carries a net in the bundled cells and has no height");
    }

    ///<summary>Every bundled IHP cell is written in nanometers, which is what the map's heights and the deck's values are in.</summary>
    [Fact]
    public void Every_bundled_ihp_cell_is_drawn_in_nanometers()
    {
        foreach (string path in GdsTestData.IhpSampleFiles())
        {
            var gds = new GDS(File.ReadAllBytes(path));

            Assert.True(gds.MicronsPerDatabaseUnit is double microns && Math.Abs(microns - 0.001) < 1e-12, $"{Path.GetFileName(path)} is in {gds.MicronsPerDatabaseUnit} um units");
        }
    }

    private static (int Height, int Thickness) stackOf(LayerNames read, string name)
    {
        var key = read.Names.Single(named => named.Value == name).Key;

        Assert.True(read.Stack.TryGetValue(key, out var stack), $"{name} has no height");

        return stack;
    }
}
