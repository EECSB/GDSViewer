using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///Rules written in the AI assistant - see RuleTools - and the grammar and lines RulePrompt reads for them. The guide's own prompt
///is the grammar sent, so a guide edited past its marks would send the whole guide, prose and all: the first test reads
///the real one. The PDK is a stand-in here, a deck in a string; the window and the PDK manager are the ask-ai spec's.
///
public class AiRulePromptTests
{
    private const string Deck = "layer met1 68/20\nrule m1.1 width met1 140 \"Met1 width\"\n";

    private static string Guide()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GDSViewer.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory!.FullName, "wwwroot", "resources", "WRITING-A-DECK.md"));
    }

    ///<summary>A PDK called Mine, holding a deck that the tools replace in place.</summary>
    private sealed class Pdk
    {
        public string Deck = AiRulePromptTests.Deck;
        public int Replaced;

        public RuleBook Book(string guide)
        {
            return new RuleBook
            {
                PdkName = () => "Mine",
                Deck = () => Deck,
                Layers = () => new[] { "met1 68/20 Conductor", "met2 69/20 Conductor" },
                Guide = () => Task.FromResult(guide),
                Replace = after =>
                {
                    Deck = after;
                    Replaced++;

                    return Task.CompletedTask;
                },
                Layermap = () => "",
                LayermapGuide = () => Task.FromResult(""),
                ReplaceLayers = after => Task.CompletedTask
            };
        }
    }

    [Fact]
    public void The_grammar_is_the_guides_prompt_alone()
    {
        string grammar = RulePrompt.Grammar(Guide());

        Assert.StartsWith("You are writing a design rule deck", grammar);
        Assert.Contains("derive <name>", grammar);
        Assert.DoesNotContain("✂️", grammar);
        Assert.DoesNotContain("How the app takes a deck", grammar);
        Assert.Equal("no marks here", RulePrompt.Grammar("no marks here"));
    }

    ///<summary>The fences a model puts round code however it is told, and the blank lines at the ends, dropped.</summary>
    [Fact]
    public void Lines_are_read_without_their_fences()
    {
        var lines = RulePrompt.LinesIn("\n```drc\r\nlayer met9 99/20\r\n\r\nrule m9.1 width met9 300 \"W\"  \r\n```\n\n");

        Assert.Equal(new[] { "layer met9 99/20", "", "rule m9.1 width met9 300 \"W\"" }, lines);
        Assert.Single(DrcDeck.Parse(string.Join("\n", lines)).Rules);
    }

    [Fact]
    public async Task Read_rules_gives_the_pdk_its_layers_its_deck_and_the_grammar()
    {
        var pdk = new Pdk();
        var read = await RuleTools.ReadAsync(pdk.Book(Guide()));

        Assert.False(read.IsError);
        Assert.Contains("The PDK is Mine.", read.Text);
        Assert.Contains("met2 69/20 Conductor", read.Text);
        Assert.Contains("rule m1.1 width met1 140", read.Text);
        Assert.Contains("derive <name>", read.Text);
        Assert.False(RuleTools.ReadTool.Changes);
    }

    ///<summary>Added where each kind goes, a change with the deck's diff for its card, and said back by id.</summary>
    [Fact]
    public async Task Add_rules_puts_the_lines_in_and_says_which()
    {
        var pdk = new Pdk();
        string arguments = "{\"lines\":\"```\\nlayer met2 69/20\\nrule m2.1 width met2 140 \\\"Met2 width\\\"\\nrule m2.2 space met2 140 \\\"Met2 spacing\\\"\\n```\"}";

        var added = await RuleTools.AddAsync(pdk.Book(""), arguments);

        Assert.False(added.IsError, added.Text);
        Assert.StartsWith("Added 2 rules to Mine's deck: m2.1, m2.2.", added.Text);
        Assert.Equal(1, pdk.Replaced);
        Assert.Equal(3, DrcDeck.Parse(pdk.Deck).Rules.Count);
        Assert.NotNull(added.TextChange);
        Assert.Contains("m2.1", added.TextChange!.After);
        Assert.True(RuleTools.AddTool.Changes);
    }

    ///<summary>An array of lines is taken too, which some models send whatever the schema says.</summary>
    [Fact]
    public async Task Add_rules_takes_its_lines_as_an_array_too()
    {
        var pdk = new Pdk();

        var added = await RuleTools.AddAsync(pdk.Book(""), "{\"lines\":[\"rule m1.2 space met1 140 \\\"Met1 spacing\\\"\"]}");

        Assert.False(added.IsError, added.Text);
        Assert.Equal(2, DrcDeck.Parse(pdk.Deck).Rules.Count);
    }

    ///
    ///**Nothing goes in with a problem in it**, and the model is told every one so it can mend them: a check the deck does
    ///not have, a layer it does not declare, an id it already uses - or no rule at all.
    ///
    [Theory]
    [InlineData("rule m1.x overlap met1 100 \\\"No such check\\\"", "No rule in those lines")]
    [InlineData("rule m9.1 width met9 300 \\\"Undeclared\\\"", "met9")]
    [InlineData("rule m1.1 space met1 140 \\\"Taken\\\"", "m1.1 is already a rule in this deck")]
    [InlineData("", "needs the deck lines")]
    public async Task Add_rules_refuses_lines_with_a_problem_and_changes_nothing(string lines, string said)
    {
        var pdk = new Pdk();

        var refused = await RuleTools.AddAsync(pdk.Book(""), $"{{\"lines\":\"{lines}\"}}");

        Assert.True(refused.IsError);
        Assert.Contains(said, refused.Text);
        Assert.Equal(0, pdk.Replaced);
        Assert.Equal(Deck, pdk.Deck);
    }

    [Fact]
    public void Weighing_blames_only_the_lines_added_for_a_problem()
    {
        string broken = Deck + "this line is not a statement\n";
        var weighed = RuleTools.Weigh(new[] { "rule m1.2 space met1 140 \"Met1 spacing\"" }, broken);

        Assert.Equal(1, weighed.Rules);
        Assert.Empty(weighed.Problems);
        Assert.Equal(new[] { "m1.2" }, weighed.Ids);
    }
}
