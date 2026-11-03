using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///The app's examples as the AI assistant reads them (2026-11-01): list_examples names them, narrowed by a filter, and
///read_example opens one beside the layout - a real sky130 cell off disk here, as the page fetches it - and reads it
///with the layout's own reading tools, without touching the layout the conversation is about.
///
public class AiExampleToolsTests
{
    private const string Inverter = "sky130_fd_sc_hd__inv_1.gds";

    private static ExampleLibrary Library(List<string>? opened = null)
    {
        var files = new List<ExampleFile>
        {
            new ExampleFile { Name = "Mosfet.gds", Kind = "transistor", About = "A single sky130 MOSFET." },
            new ExampleFile { Name = "sky130_ef_ip__opamp.gds", Kind = "design", About = "Op-amp, by Efabless." },
            new ExampleFile { Name = Inverter, Kind = "cell" },
            new ExampleFile { Name = "sky130_fd_sc_hd__nand2_1.gds", Kind = "cell" }
        };

        return new ExampleLibrary
        {
            Files = files,
            Open = (name, _) =>
            {
                opened?.Add(name);

                var gds = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, "Sky130 GDS", name)));

                return Task.FromResult(gds);
            }
        };
    }

    [Fact]
    public void The_list_names_every_example_and_says_what_the_designs_are()
    {
        var listed = ExampleTools.List(Library(), "{}");

        Assert.False(listed.IsError);
        Assert.StartsWith("4 examples", listed.Text);
        Assert.Contains("sky130_ef_ip__opamp.gds (design) - Op-amp, by Efabless.", listed.Text);
        Assert.Contains("Mosfet.gds (transistor)", listed.Text);
        Assert.Contains(Inverter, listed.Text);
    }

    ///
    ///**Each example says the PDK it is drawn in** (2026-11-02, with GF180's beside sky130's), so a GF180 inverter is not
    ///drawn from a sky130 one: in the list after its kind, and in the reading after its name.
    ///
    [Fact]
    public async Task Each_example_says_the_pdk_it_is_drawn_in()
    {
        const string Gf180Inverter = "gf180mcu_fd_sc_mcu7t5v0__inv_1.gds";

        var library = new ExampleLibrary
        {
            Files = new List<ExampleFile>
            {
                new ExampleFile { Name = "tt_um_vga_clock.oas", Kind = "design", About = "VGA clock, by Matt Venn.", Pdk = "gf180mcu" },
                new ExampleFile { Name = Gf180Inverter, Kind = "cell", Pdk = "gf180mcu" },
                new ExampleFile { Name = "sg13g2_inv_1.gds", Kind = "cell", Pdk = "ihp-sg13g2" },
                new ExampleFile { Name = Inverter, Kind = "cell", Pdk = "sky130" }
            },
            Open = (name, _) => Task.FromResult(new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, "GF180 GDS", name))))
        };

        var listed = ExampleTools.List(library, "{}");

        Assert.Contains("tt_um_vga_clock.oas (design, gf180mcu) - VGA clock, by Matt Venn.", listed.Text);
        Assert.Contains($"{Gf180Inverter} (gf180mcu)", listed.Text);
        Assert.Contains("sg13g2_inv_1.gds (ihp-sg13g2)", listed.Text);
        Assert.Contains($"{Inverter} (sky130)", listed.Text);

        var read = await ExampleTools.ReadAsync(library, new Dictionary<string, GDS>(), $$"""{"name":"{{Gf180Inverter}}"}""", CancellationToken.None);

        Assert.False(read.IsError, read.Text);
        Assert.Contains("It is drawn in gf180mcu, whose layermap names its layers.", read.Text);
    }

    [Fact]
    public void A_filter_narrows_the_list_ignoring_case()
    {
        var listed = ExampleTools.List(Library(), """{"filter":"NAND"}""");

        Assert.StartsWith("1 examples whose names contain \"NAND\"", listed.Text);
        Assert.Contains("nand2_1", listed.Text);
        Assert.DoesNotContain("inv_1", listed.Text);
    }

    [Fact]
    public async Task An_example_is_read_with_its_cells_layers_and_shapes()
    {
        var read = await ExampleTools.ReadAsync(Library(), new Dictionary<string, GDS>(), $$"""{"name":"{{Inverter}}"}""", CancellationToken.None);

        Assert.False(read.IsError, read.Text);
        Assert.Contains("opened beside the person's layout", read.Text);
        Assert.Contains("sky130_fd_sc_hd__inv_1", read.Text);
        Assert.Contains("The shapes in sky130_fd_sc_hd__inv_1:", read.Text);
        Assert.Contains("#1", read.Text);
    }

    [Fact]
    public async Task An_example_read_again_is_not_opened_again()
    {
        var opened = new List<string>();
        var kept = new Dictionary<string, GDS>();
        var library = Library(opened);

        await ExampleTools.ReadAsync(library, kept, $$"""{"name":"{{Inverter}}"}""", CancellationToken.None);
        await ExampleTools.ReadAsync(library, kept, $$"""{"name":"{{Inverter}}","limit":5}""", CancellationToken.None);

        Assert.Single(opened);
    }

    [Fact]
    public async Task A_name_that_is_not_an_example_is_refused_with_where_to_look()
    {
        var read = await ExampleTools.ReadAsync(Library(), new Dictionary<string, GDS>(), """{"name":"my_chip.gds"}""", CancellationToken.None);

        Assert.True(read.IsError);
        Assert.Contains("list_examples", read.Text);
    }

    ///<summary>Through the runner: offered when the page gives examples, and reading one leaves the layout the conversation is about as it was.</summary>
    [Fact]
    public async Task The_runner_offers_them_and_reading_one_leaves_the_layout_alone()
    {
        var gds = GDS.NewLibrary(stamp: new DateTime(2026, 11, 1));
        var session = new AgentSession(gds, new EditHistory());
        var runner = new LayoutToolRunner(session) { Examples = Library() };

        Assert.Contains(runner.Tools, tool => tool.Name == ExampleTools.ListName);
        Assert.Contains(runner.Tools, tool => tool.Name == ExampleTools.ReadName);
        Assert.DoesNotContain(new LayoutToolRunner(session).Tools, tool => tool.Name == ExampleTools.ReadName);

        int records = gds.Records.Count;
        var read = await runner.RunToolAsync(ExampleTools.ReadName, $$"""{"name":"{{Inverter}}"}""", CancellationToken.None);

        Assert.False(read.IsError, read.Text);
        Assert.Equal(records, gds.Records.Count);
        Assert.Equal(0, session.History.Count);
    }

    [Fact]
    public void The_prompt_points_at_the_examples_only_when_they_are_offered()
    {
        Assert.Contains("read_example", LayoutPrompt.Build(new LayoutFacts { HasExamples = true }, ToolApprovalMode.AutoRead));
        Assert.DoesNotContain("read_example", LayoutPrompt.Build(new LayoutFacts(), ToolApprovalMode.AutoRead));
    }
}
