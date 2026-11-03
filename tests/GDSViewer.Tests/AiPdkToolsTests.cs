using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///A PDK put in use from the AI assistant - see PdkTools. The PDKs are a stand-in here: two names, the one in use, whether
///the layout waits for one's layers, the layers it has, and whether it is drawn in the PDK asked for. The page's own half,
///Viewer.razor's usePdkForAssistant, is Set PDK's and is held in the browser by ask-ai.spec.
///
public class AiPdkToolsTests
{
    private static readonly string[] PdkLayers = { "nwell (64/20)", "met1 (68/20)" };

    ///<summary>sky130 in use and Mine beside it, on a layout that waits for a PDK's layers until one is used.</summary>
    private sealed class Shelf
    {
        public string InUse = "sky130";
        public bool Waiting = true;
        public bool Fits = true;
        public List<string> Has = new();
        public IReadOnlyList<string> Layers = PdkLayers;
        public DrcDeck? Deck;
        public List<string> Used = new();

        public PdkShelf Make()
        {
            return new PdkShelf
            {
                Names = () => new[] { "sky130", "Mine" },
                InUse = () => InUse,
                Waiting = () => Waiting,
                Use = name =>
                {
                    Used.Add(name);

                    bool was = InUse == name;

                    InUse = name;

                    //As the page does: every layer on a layout that waits, and the ones a layout drawn in the PDK lacks.
                    var laid = new List<string>();

                    if (Waiting || Fits)
                        laid.AddRange(Layers.Where(layer => !Has.Contains(layer)));

                    Has.AddRange(laid);
                    Waiting = false;
                    Deck = DrcDeck.Parse("layer met1 68/20\nrule m.1 width met1 140 \"Width\"");

                    return Task.FromResult(new PdkUsed(laid, Fits, was));
                },
                Deck = () => Deck
            };
        }
    }

    [Fact]
    public async Task A_layout_that_waits_gets_the_named_pdks_layers_and_is_told_them()
    {
        var shelf = new Shelf();
        var used = await PdkTools.UseAsync(shelf.Make(), """{"pdk":"sky130"}""");

        Assert.False(used.IsError, used.Text);
        Assert.Equal(new[] { "sky130" }, shelf.Used);
        Assert.Contains("sky130 is in use, and its 2 layers are laid on this layout, ready to draw on by name: nwell (64/20), met1 (68/20).", used.Text);
        Assert.Contains("Undo does not take them back", used.Text);
    }

    ///
    ///**A layout drawn in the PDK gets the layers of it it lacks** (2026-11-01, asked for): one whose first layers came in
    ///one at a time, gray, on numbers drawn before any PDK was set, is told which came in round them.
    ///
    [Fact]
    public async Task A_layout_drawn_in_the_pdk_gets_the_layers_it_lacks()
    {
        var shelf = new Shelf { Waiting = false, Has = new List<string> { "met1 (68/20)" } };
        var used = await PdkTools.UseAsync(shelf.Make(), """{"pdk":"sky130"}""");

        Assert.False(used.IsError, used.Text);
        Assert.Contains("sky130 is in use, and the 1 of its layers this layout did not have is laid on beside the ones it had, ready to draw on by name: nwell (64/20).", used.Text);

        var two = await PdkTools.UseAsync(new Shelf { Waiting = false, Has = new List<string> { "71/20" } }.Make(), """{"pdk":"sky130"}""");

        Assert.Contains("the 2 of its layers this layout did not have are laid on", two.Text);
    }

    [Fact]
    public async Task A_name_is_matched_whatever_its_case_and_one_there_is_none_of_is_refused_with_the_names_there_are()
    {
        var shelf = new Shelf();
        var matched = await PdkTools.UseAsync(shelf.Make(), """{"pdk":"MINE"}""");

        Assert.False(matched.IsError, matched.Text);
        Assert.Equal(new[] { "Mine" }, shelf.Used);

        var unknown = await PdkTools.UseAsync(new Shelf().Make(), """{"pdk":"gf180"}""");

        Assert.True(unknown.IsError);
        Assert.Contains("There is no PDK called gf180. The person's PDKs are sky130, Mine", unknown.Text);

        var blank = await PdkTools.UseAsync(new Shelf().Make(), "{}");

        Assert.True(blank.IsError);
        Assert.Contains("needs the PDK's name", blank.Text);
    }

    [Fact]
    public async Task The_one_in_use_on_a_layout_with_every_one_of_its_layers_changes_nothing()
    {
        var shelf = new Shelf { Waiting = false, Has = PdkLayers.ToList() };
        var used = await PdkTools.UseAsync(shelf.Make(), """{"pdk":"sky130"}""");

        Assert.False(used.IsError);
        Assert.Contains("sky130 is already in use, and this layout has every one of its layers: nothing changed.", used.Text);

        var other = await PdkTools.UseAsync(new Shelf { Waiting = false, Has = PdkLayers.ToList() }.Make(), """{"pdk":"Mine"}""");

        Assert.Contains("Mine is in use: it names this file's layers and checks it against its rules, and the layout has every one of its layers already.", other.Text);
    }

    ///<summary>Not over a file from another process, whose layers the PDK would name wrongly: said, and nothing laid on.</summary>
    [Fact]
    public async Task Another_pdk_over_a_file_it_does_not_name_lays_nothing_on_and_says_so()
    {
        var shelf = new Shelf { Waiting = false, Fits = false };
        var used = await PdkTools.UseAsync(shelf.Make(), """{"pdk":"Mine"}""");

        Assert.False(used.IsError);
        Assert.Empty(shelf.Has);
        Assert.Contains("Mine is in use, for every file drawn in it - but this file's layers are not mostly Mine's, so it names and checks nothing here, and its layers were not laid on.", used.Text);
    }

    [Fact]
    public async Task A_pdk_with_no_layers_on_a_layout_that_waits_says_there_are_none_to_lay_on()
    {
        var shelf = new Shelf { Layers = Array.Empty<string>() };
        var used = await PdkTools.UseAsync(shelf.Make(), """{"pdk":"Mine"}""");

        Assert.False(used.IsError);
        Assert.Contains("Mine is in use, but it has no layers to lay on: its layermap is empty.", used.Text);
    }

    ///<summary>Through the runner: a change, offered only where changes are, and the rest of the answer checked against the rules it brought.</summary>
    [Fact]
    public async Task The_runner_offers_it_only_where_changes_are_and_checks_against_the_deck_it_brought()
    {
        Assert.True(PdkTools.UseTool.Changes);

        var session = new AgentSession(GDS.NewLibrary(stamp: new DateTime(2026, 11, 1)), new EditHistory());
        var shelf = new Shelf();
        var runner = new LayoutToolRunner(session) { Pdks = shelf.Make() };

        Assert.DoesNotContain(runner.Tools, tool => tool.Name == PdkTools.UseName);

        var refused = await runner.RunToolAsync(PdkTools.UseName, """{"pdk":"sky130"}""", CancellationToken.None);

        Assert.True(refused.IsError);
        Assert.Empty(shelf.Used);

        runner.AllowEdits = true;

        Assert.Contains(runner.Tools, tool => tool.Name == PdkTools.UseName);
        Assert.DoesNotContain(new LayoutToolRunner(session) { Pdks = shelf.Make(), AllowEdits = true, Off = new HashSet<string> { PdkTools.UseName } }.Tools, tool => tool.Name == PdkTools.UseName);

        var used = await runner.RunToolAsync(PdkTools.UseName, """{"pdk":"sky130"}""", CancellationToken.None);

        Assert.False(used.IsError, used.Text);
        Assert.NotNull(session.Deck);
        Assert.Same(shelf.Deck, session.Deck);
    }

    [Fact]
    public void The_prompt_names_the_pdks_and_says_a_new_layout_waits_for_one_only_when_use_pdk_is_offered()
    {
        var waiting = new LayoutFacts { PdkNames = new[] { "sky130", "Mine" }, PdkInUse = "sky130", WaitsForPdk = true };
        string told = LayoutPrompt.Build(waiting, ToolApprovalMode.AutoRead);

        Assert.Contains("The person's PDKs are sky130, Mine, and sky130 is in use.", told);
        Assert.Contains("use_pdk puts one in use and lays on whichever of its layers the layout lacks.", told);
        Assert.Contains("call use_pdk before drawing anything", told);

        string past = LayoutPrompt.Build(new LayoutFacts { PdkNames = new[] { "sky130" }, PdkInUse = "sky130" }, ToolApprovalMode.AutoRead);

        Assert.Contains("use_pdk puts one in use", past);
        Assert.DoesNotContain("call use_pdk before drawing anything", past);

        //Switched off, it is named only among the tools that are.
        string off = LayoutPrompt.Build(waiting, ToolApprovalMode.AutoRead, new HashSet<string> { PdkTools.UseName });

        Assert.DoesNotContain("use_pdk puts one in use", off);
        Assert.DoesNotContain("call use_pdk before drawing", off);
        Assert.DoesNotContain("use_pdk", LayoutPrompt.Build(new LayoutFacts(), ToolApprovalMode.AutoRead));
    }
}
