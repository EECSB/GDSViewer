using GdsII;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///The PDKs the PDK manager keeps, and the layermap edits underneath them: a PDK's layers are a layermap kept as text,
///written back out after every edit - from the manager's table, or from the layer panel on a file the PDK is laid over -
///so what is written has to read back as what was there. And the two answers a PDK gives a file: whether the file is
///drawn in it, and which layers a new layout starts with.
///</summary>
public class PdkTests
{
    private static string shippedLayers()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "sky130-roles.csv"));
    }

    private static string shippedRules()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc"));
    }

    private static string gf180Layers()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "gf180mcu-roles.csv"));
    }

    private static string gf180Rules()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "gf180mcuD.drc"));
    }

    private static string ihpLayers()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "ihp-sg13g2-roles.csv"));
    }

    private static string ihpRules()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.SampleDirectory, "sg13g2.drc"));
    }

    #region Writing a layermap back out ***********************************************

    ///<summary>The shipped layermap through the writer and back: every column of every row as it was read.</summary>
    [Fact]
    public void A_layermap_written_out_reads_back_as_the_same_layermap()
    {
        var read = LayerNames.Parse(shippedLayers());
        var again = LayerNames.Parse(read.Write());

        Assert.Empty(again.Problems);
        Assert.Equal(read.Pairs, again.Pairs);
        Assert.Equal(read.Names, again.Names);
        Assert.Equal(read.Colors, again.Colors);
        Assert.Equal(read.Stack, again.Stack);
        Assert.Equal(read.Roles, again.Roles);
        Assert.Equal(read.Fills, again.Fills);
    }

    [Fact]
    public void Every_column_a_row_can_hold_comes_back()
    {
        var key = new LayerKey(68, 20);
        var names = new LayerNames();

        names.Names[key] = "met1, the first";
        names.Colors[key] = "#00ff00";
        names.Stack[key] = (1370, 360);
        names.Roles[key] = LayerRole.Conductor;
        names.Fills[key] = LayerFill.CrossHatch;
        names.PatternColors[key] = "#ff0000";
        names.PatternSizes[key] = 12;
        names.Textures[key] = "copper.png";
        names.TextureSizes[key] = 500;

        var again = LayerNames.Parse(names.Write());

        Assert.Equal("met1, the first", again.Names[key]);
        Assert.Equal("#00ff00", again.Colors[key]);
        Assert.Equal((1370, 360), again.Stack[key]);
        Assert.Equal(LayerRole.Conductor, again.Roles[key]);
        Assert.Equal(LayerFill.CrossHatch, again.Fills[key]);
        Assert.Equal("#ff0000", again.PatternColors[key]);
        Assert.Equal(12, again.PatternSizes[key]);
        Assert.Equal("copper.png", again.Textures[key]);
        Assert.Equal(500, again.TextureSizes[key]);
    }

    ///<summary>The layer panel's edit, taken into a PDK: the row becomes what the layer says, and only what was chosen on it.</summary>
    [Fact]
    public void A_row_set_from_a_layer_says_what_the_layer_says()
    {
        var key = new LayerKey(68, 20);
        var rows = LayerNames.Parse(shippedLayers());
        var layer = new Layer(key, "#123456") { Name = "metal one", Role = LayerRole.Conductor };

        rows.SetRow(key, layer);

        Assert.Equal("metal one", rows.Names[key]);
        Assert.Equal(LayerRole.Conductor, rows.Roles[key]);

        //A palette color is not a choice, so it is not written - and neither is a stack nobody placed.
        Assert.False(rows.Colors.ContainsKey(key));
        Assert.False(rows.Stack.ContainsKey(key));
    }

    [Fact]
    public void A_layer_nothing_is_said_about_takes_its_row_away()
    {
        var key = new LayerKey(68, 20);
        var rows = LayerNames.Parse(shippedLayers());

        rows.SetRow(key, new Layer(key, "#123456"));

        Assert.DoesNotContain(key, rows.Pairs);
    }

    ///<summary>An imported layermap of names alone keeps the PDK's heights and roles - the reason a merge is not a replace.</summary>
    [Fact]
    public void A_merge_keeps_what_the_other_layermap_leaves_out()
    {
        var key = new LayerKey(68, 20);
        var rows = LayerNames.Parse(shippedLayers());
        var names = LayerNames.Parse("68,20,M1\n200,0,extra");

        rows.MergeFrom(names);

        Assert.Equal("M1", rows.Names[key]);
        Assert.Equal((1370, 360), rows.Stack[key]);
        Assert.Equal(LayerRole.Conductor, rows.Roles[key]);
        Assert.Equal("extra", rows.Names[new LayerKey(200, 0)]);
    }

    [Fact]
    public void A_row_removed_says_nothing_any_more()
    {
        var key = new LayerKey(68, 20);
        var rows = LayerNames.Parse(shippedLayers());

        rows.Remove(key);

        Assert.DoesNotContain(key, rows.Pairs);
        Assert.False(rows.Stack.ContainsKey(key));
        Assert.False(rows.Roles.ContainsKey(key));
    }

    #endregion ************************************************************************



    #region Whether a file is drawn in a PDK ****************************************

    public static IEnumerable<object[]> Files()
    {
        //sky130's metals, a whole cell's worth, and a cell with markers the layermap does not list.
        yield return new object[] { "68/20 69/20 70/20", true };
        yield return new object[] { "68/20 1/0", true };
        yield return new object[] { "68/20 1/0 2/0", false };

        //gf180mcu's COMP, Metal1 and Via1, which sky130 has no row for.
        yield return new object[] { "22/0 34/0 35/0", false };

        //A new layout, which is drawn in whatever the PDK is.
        yield return new object[] { "", true };
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void A_file_is_drawn_in_a_pdk_when_most_of_its_layers_are_the_pdks(string pairs, bool expected)
    {
        var layers = pairs.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(pair => Pdk.PairOf(pair)!.Value);

        Assert.Equal(expected, LayerNames.Parse(shippedLayers()).Fits(layers));
    }

    ///<summary>Every bundled example is sky130 - the cells and the designs - so each is drawn in the built-in: the files the bundled names were for.</summary>
    [Fact]
    public void Every_bundled_example_is_drawn_in_the_built_in()
    {
        var sky130 = LayerNames.Parse(shippedLayers());
        var examples = Directory.GetFiles(Path.Combine(GdsTestData.SampleDirectory, "Sky130 Designs"), "*.gds")
            .Concat(Directory.GetFiles(Path.Combine(GdsTestData.SampleDirectory, "Sky130 GDS"), "*.gds"))
            .ToList();

        Assert.True(examples.Count > 800, $"only {examples.Count} examples found");

        var unfit = examples
            .Where(example => !sky130.Fits(new GDS(File.ReadAllBytes(example)).AdditionalInformation.Layers.Keys))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(unfit);
    }

    #endregion ************************************************************************



    #region What a new layout starts with *******************************************

    ///<summary>sky130's drawing layers - the 21 its rules check - with the 16 pins and labels on them. Asked for 2026-10-18.</summary>
    [Fact]
    public void The_built_in_starts_a_layout_with_its_rules_layers_and_their_pins_and_labels()
    {
        var builtIn = PdkList.Fresh().Default;

        var starts = builtIn.StartingLayers(LayerNames.Parse(shippedLayers()), DrcDeck.Parse(shippedRules()));

        Assert.Equal(37, starts.Count);
        Assert.Equal(starts.Distinct().Count(), starts.Count);

        foreach (string pair in new[] { "68/20", "68/44", "72/20", "64/20", "68/16", "68/5", "64/59", "122/16", "67/16" })
            Assert.Contains(Pdk.PairOf(pair)!.Value, starts);

        //The markers and the implants nobody starts a layout on.
        foreach (string pair in new[] { "83/44", "89/44", "76/20", "64/18" })
            Assert.DoesNotContain(Pdk.PairOf(pair)!.Value, starts);
    }

    ///
    ///gf180mcu the same way (2026-11-02): the 19 layers its deck checks, from DNWELL to Metal5, with the 9 labels the
    ///layermap names on the wells, COMP, Poly2 and the metals - and not the fill, the markers, or the six-metal stack's
    ///MetalTop.
    ///
    [Fact]
    public void Gf180mcu_starts_a_layout_with_its_rules_layers_and_their_labels()
    {
        var gf180 = PdkList.Fresh().Find(Pdk.Gf180mcu)!;

        var starts = gf180.StartingLayers(LayerNames.Parse(gf180Layers()), DrcDeck.Parse(gf180Rules()));

        Assert.Equal(28, starts.Count);
        Assert.Equal(starts.Distinct().Count(), starts.Count);

        foreach (string pair in new[] { "22/0", "30/0", "33/0", "34/0", "41/0", "81/0", "12/0", "34/10", "81/10", "22/10", "21/10" })
            Assert.Contains(Pdk.PairOf(pair)!.Value, starts);

        foreach (string pair in new[] { "34/4", "110/5", "0/0", "53/0", "82/0", "75/0" })
            Assert.DoesNotContain(Pdk.PairOf(pair)!.Value, starts);
    }

    ///
    ///ihp-sg13g2 the same way (2026-11-02): the 22 layers its deck checks, from NWell to Passiv, with the 26 pins, labels
    ///and texts the layermap names - **text** among them, which is where IHP writes a net's name: Metal1.text, 8/25, is
    ///what its LVS reads and its cells write. Not the fill, the slits, the markers or the cells' boundary.
    ///
    [Fact]
    public void Ihp_sg13g2_starts_a_layout_with_its_rules_layers_and_their_pins_and_names()
    {
        var ihp = PdkList.Fresh().Find(Pdk.IhpSg13g2)!;

        var starts = ihp.StartingLayers(LayerNames.Parse(ihpLayers()), DrcDeck.Parse(ihpRules()));

        Assert.Equal(48, starts.Count);
        Assert.Equal(starts.Distinct().Count(), starts.Count);

        foreach (string pair in new[] { "1/0", "5/0", "6/0", "8/0", "19/0", "126/0", "134/0", "36/0", "8/2", "8/25", "8/1", "134/25" })
            Assert.Contains(Pdk.PairOf(pair)!.Value, starts);

        foreach (string pair in new[] { "8/22", "8/24", "189/4", "99/31", "32/0", "7/0", "129/0" })
            Assert.DoesNotContain(Pdk.PairOf(pair)!.Value, starts);
    }

    [Fact]
    public void Another_pdk_starts_with_every_layer_it_has()
    {
        var list = PdkList.Fresh();
        var mine = list.Add("Mine", "1,0,one\n2,0,two\n3,0,three", "");

        var starts = mine.StartingLayers(LayerNames.Parse(mine.Layers!), DrcDeck.Parse(""));

        Assert.Equal(new[] { new LayerKey(1, 0), new LayerKey(2, 0), new LayerKey(3, 0) }, starts);
    }

    [Fact]
    public void A_pdk_told_which_layers_to_start_with_starts_with_those()
    {
        var list = PdkList.Fresh();
        var mine = list.Add("Mine", "1,0,one\n2,0,two\n3,0,three", "");

        mine.Start = new List<string> { "3/0", "1/0", "not a pair" };

        var starts = mine.StartingLayers(LayerNames.Parse(mine.Layers!), DrcDeck.Parse(""));

        Assert.Equal(new[] { new LayerKey(1, 0), new LayerKey(3, 0) }, starts);
    }

    #endregion ************************************************************************



    #region The list ****************************************************************

    [Fact]
    public void A_fresh_list_has_the_built_ins_alone_and_sky130_in_use()
    {
        var list = PdkList.Fresh();

        Assert.Equal(new[] { Pdk.Sky130, Pdk.Gf180mcu, Pdk.IhpSg13g2 }, list.Pdks.Select(pdk => pdk.Id));
        Assert.Equal(new[] { "sky130", "gf180mcu", "ihp-sg13g2" }, list.Pdks.Select(pdk => pdk.Name));
        Assert.All(list.Pdks, pdk => Assert.True(pdk.BuiltIn));
        Assert.All(list.Pdks, pdk => Assert.False(pdk.Changed));
        Assert.Equal(Pdk.Sky130, list.Selected);
        Assert.Same(list.Default, list.InUse);
    }

    ///<summary>Each built-in is read from its own three files, and nothing made here is a built-in.</summary>
    [Fact]
    public void Each_built_in_ships_its_own_layermap_deck_and_README()
    {
        var list = PdkList.Fresh();
        var gf180 = list.Find(Pdk.Gf180mcu)!;

        Assert.Equal("resources/GDS Files/gf180mcu-roles.csv", gf180.Shipped!.LayersPath);
        Assert.Equal("resources/GDS Files/gf180mcuD.drc", gf180.Shipped.RulesPath);
        Assert.Equal("resources/GDS Files/gf180mcu-README.md", gf180.Shipped.ReadmePath);
        Assert.Equal("resources/GDS Files/sky130A.drc", list.Default.Shipped!.RulesPath);

        var ihp = list.Find(Pdk.IhpSg13g2)!;

        Assert.Equal("resources/GDS Files/ihp-sg13g2-roles.csv", ihp.Shipped!.LayersPath);
        Assert.Equal("resources/GDS Files/sg13g2.drc", ihp.Shipped.RulesPath);
        Assert.Equal("resources/GDS Files/ihp-sg13g2-README.md", ihp.Shipped.ReadmePath);

        //Served from the folder the app ships, every one of them.
        foreach (var shipped in Pdk.BuiltIns)
        {
            foreach (string path in new[] { shipped.LayersPath, shipped.RulesPath, shipped.ReadmePath })
                Assert.True(File.Exists(Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", path)), path);
        }

        var mine = list.Add("Mine", "", "");

        Assert.Null(mine.Shipped);
        Assert.False(mine.BuiltIn);
    }

    [Fact]
    public void A_new_pdk_takes_a_name_no_other_has()
    {
        var list = PdkList.Fresh();

        var first = list.Add("Mine", "", "");
        var second = list.Add("mine", "", "");
        var third = list.Add("Mine", "", "");

        Assert.Equal("Mine", first.Name);
        Assert.Equal("mine 2", second.Name);
        Assert.Equal("Mine 3", third.Name);
        Assert.Equal(3, new[] { first.Id, second.Id, third.Id }.Distinct().Count());
    }

    [Fact]
    public void The_list_is_kept_as_it_was()
    {
        var list = PdkList.Fresh();
        var mine = list.Add("Mine", "68,20,met1", "rule x width met1 100");

        mine.Start = new List<string> { "68/20" };
        list.Selected = mine.Id;
        list.Default.Rules = "edited";

        var read = PdkStore.Read(PdkStore.Write(list));

        Assert.NotNull(read);
        Assert.Equal(mine.Id, read.Selected);
        Assert.Equal(4, read.Pdks.Count);
        Assert.Equal("edited", read.Default.Rules);
        Assert.Null(read.Default.Layers);
        Assert.Null(read.Find(Pdk.Gf180mcu)!.Rules);
        Assert.Equal("Mine", read.InUse.Name);
        Assert.Equal("68,20,met1", read.InUse.Layers);
        Assert.Equal(new[] { "68/20" }, read.InUse.Start);
    }

    ///<summary>A stored list that lost the built-ins gets them back, so there is always one to fall back on.</summary>
    [Fact]
    public void A_list_without_the_built_ins_gets_them_back()
    {
        var read = PdkStore.Read("{\"selected\":\"gone\",\"pdks\":[{\"id\":\"pdk-1\",\"name\":\"Mine\",\"layers\":\"\",\"rules\":\"\"}]}");

        Assert.NotNull(read);
        Assert.Equal(new[] { Pdk.Sky130, Pdk.Gf180mcu, Pdk.IhpSg13g2, "pdk-1" }, read.Pdks.Select(pdk => pdk.Id));
        Assert.Equal(Pdk.Sky130, read.InUse.Id);
    }

    ///
    ///**A list kept before gf180mcu shipped gets it under sky130**, over the PDKs made here, and nothing else moves:
    ///sky130 as it was edited, the PDK in use still in use. Every browser that kept a list until 2026-11-02 reads one of
    ///these - and gets ihp-sg13g2 under gf180mcu with it.
    ///
    [Fact]
    public void A_list_kept_before_gf180mcu_shipped_gets_it_under_sky130()
    {
        var read = PdkStore.Read("{\"selected\":\"pdk-1\",\"pdks\":[{\"id\":\"sky130\",\"name\":\"sky130\",\"rules\":\"edited\"},"
            + "{\"id\":\"pdk-1\",\"name\":\"Mine\",\"layers\":\"1,0,one\",\"rules\":\"\"}]}");

        Assert.NotNull(read);
        Assert.Equal(new[] { Pdk.Sky130, Pdk.Gf180mcu, Pdk.IhpSg13g2, "pdk-1" }, read.Pdks.Select(pdk => pdk.Id));
        Assert.Equal("pdk-1", read.InUse.Id);
        Assert.Equal("edited", read.Default.Rules);
        Assert.False(read.Find(Pdk.Gf180mcu)!.Changed);
    }

    ///
    ///**And a list kept with gf180mcu in it, before ihp-sg13g2 shipped, gets that under gf180mcu** - the same day, so
    ///a browser that kept a list in between reads one of these: gf180mcu as it was edited stays edited.
    ///
    [Fact]
    public void A_list_kept_before_ihp_sg13g2_shipped_gets_it_under_gf180mcu()
    {
        var read = PdkStore.Read("{\"selected\":\"gf180mcu\",\"pdks\":[{\"id\":\"sky130\",\"name\":\"sky130\"},"
            + "{\"id\":\"gf180mcu\",\"name\":\"gf180mcu\",\"rules\":\"edited\"},"
            + "{\"id\":\"pdk-1\",\"name\":\"Mine\",\"layers\":\"1,0,one\",\"rules\":\"\"}]}");

        Assert.NotNull(read);
        Assert.Equal(new[] { Pdk.Sky130, Pdk.Gf180mcu, Pdk.IhpSg13g2, "pdk-1" }, read.Pdks.Select(pdk => pdk.Id));
        Assert.Equal(Pdk.Gf180mcu, read.InUse.Id);
        Assert.Equal("edited", read.InUse.Rules);
        Assert.False(read.Find(Pdk.IhpSg13g2)!.Changed);
    }

    ///<summary>A PDK somebody brought in as gf180mcu before it shipped is kept, under a name of its own, so the two are not one name.</summary>
    [Fact]
    public void A_pdk_already_called_gf180mcu_takes_another_name()
    {
        var read = PdkStore.Read("{\"selected\":\"pdk-1\",\"pdks\":[{\"id\":\"sky130\",\"name\":\"sky130\"},"
            + "{\"id\":\"pdk-1\",\"name\":\"GF180MCU\",\"layers\":\"34,0,Metal1\",\"rules\":\"\"}]}");

        Assert.NotNull(read);
        Assert.Equal(new[] { "sky130", "gf180mcu", "ihp-sg13g2", "GF180MCU 2" }, read.Pdks.Select(pdk => pdk.Name));
        Assert.Equal("34,0,Metal1", read.InUse.Layers);
    }

    ///<summary>And one brought in as ihp-sg13g2 the same way.</summary>
    [Fact]
    public void A_pdk_already_called_ihp_sg13g2_takes_another_name()
    {
        var read = PdkStore.Read("{\"selected\":\"pdk-1\",\"pdks\":[{\"id\":\"sky130\",\"name\":\"sky130\"},"
            + "{\"id\":\"pdk-1\",\"name\":\"IHP-SG13G2\",\"layers\":\"8,0,Metal1\",\"rules\":\"\"}]}");

        Assert.NotNull(read);
        Assert.Equal(new[] { "sky130", "gf180mcu", "ihp-sg13g2", "IHP-SG13G2 2" }, read.Pdks.Select(pdk => pdk.Name));
        Assert.Equal("8,0,Metal1", read.InUse.Layers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Anything_that_is_not_a_list_reads_as_none(string? stored)
    {
        Assert.Null(PdkStore.Read(stored));
    }

    #endregion ************************************************************************
}
