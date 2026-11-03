using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///Layers written in the AI assistant - see LayerTools. The layermap guide's own prompt is the format sent, as the deck
///guide's is the grammar, so the first test reads the real one. The PDK is a stand-in here, a layermap in a string.
///
public class AiLayerToolsTests
{
    private const string Layermap = "#a comment\n68,20,met1,#0000ff,1376,360,conductor\n68,44,via,,1736,270,via\n";

    private static string Guide()
    {
        return File.ReadAllText(Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", "resources", "WRITING-A-LAYERMAP.md"));
    }

    ///<summary>A PDK called Mine, holding a layermap that the tools replace in place.</summary>
    private sealed class Pdk
    {
        public string Layers = Layermap;
        public int Replaced;

        public RuleBook Book(string guide)
        {
            return new RuleBook
            {
                PdkName = () => "Mine",
                Deck = () => "",
                Layers = () => Array.Empty<string>(),
                Guide = () => Task.FromResult(""),
                Replace = after => Task.CompletedTask,
                Layermap = () => Layers,
                LayermapGuide = () => Task.FromResult(guide),
                ReplaceLayers = after =>
                {
                    Layers = after;
                    Replaced++;

                    return Task.CompletedTask;
                }
            };
        }
    }

    [Fact]
    public async Task Read_layers_gives_the_layermap_and_its_format_alone()
    {
        var read = await LayerTools.ReadAsync(new Pdk().Book(Guide()));

        Assert.False(read.IsError);
        Assert.Contains("The PDK is Mine.", read.Text);
        Assert.Contains("68,20,met1,#0000ff,1376,360,conductor", read.Text);
        Assert.Contains("You are writing a layermap", read.Text);
        Assert.DoesNotContain("✂️", read.Text);
        Assert.DoesNotContain("Putting a whole PDK together", read.Text);
        Assert.False(LayerTools.ReadTool.Changes);
    }

    ///
    ///**Laid over the layermap, as an Import is**: a new layer goes in, and a row for a layer already there replaces the
    ///columns it fills and keeps the ones it leaves empty - met1 renamed keeps its color, its stack and its role.
    ///
    [Fact]
    public async Task Set_layers_adds_and_changes_and_keeps_what_a_row_leaves_out()
    {
        var pdk = new Pdk();

        var set = await LayerTools.SetAsync(pdk.Book(""), "{\"rows\":\"```csv\\n69,20,met2,#ff00ff,2000,360,conductor\\n68,20,metal1\\n```\"}");

        Assert.False(set.IsError, set.Text);
        Assert.Equal("In Mine's layermap: added 1 layer (69/20); changed 1 layer (68/20). Every file drawn in it is named and drawn by them from now on; they are changed back in the PDK manager, not with Undo.", set.Text);
        Assert.Equal(1, pdk.Replaced);

        var after = LayerNames.Parse(pdk.Layers);
        var met1 = new LayerKey(68, 20);

        Assert.Equal("metal1", after.Names[met1]);
        Assert.Equal("#0000ff", after.Colors[met1]);
        Assert.Equal(LayerRole.Conductor, after.Roles[met1]);
        Assert.Equal((2000, 360), after.Stack[new LayerKey(69, 20)]);
        Assert.NotNull(set.TextChange);
        Assert.True(LayerTools.SetTool.Changes);
    }

    [Fact]
    public async Task Set_layers_takes_layers_out()
    {
        var pdk = new Pdk();

        var set = await LayerTools.SetAsync(pdk.Book(""), "{\"remove\":[\"68/44\"]}");

        Assert.False(set.IsError, set.Text);
        Assert.Equal(new[] { new LayerKey(68, 20) }, LayerNames.Parse(pdk.Layers).Pairs);
    }

    ///<summary>Rows as an array are taken too, which some models send whatever the schema says.</summary>
    [Fact]
    public async Task Set_layers_takes_its_rows_as_an_array_too()
    {
        var pdk = new Pdk();

        var set = await LayerTools.SetAsync(pdk.Book(""), "{\"rows\":[\"70,20,met3\",\"70,44,via3,,,,via\"]}");

        Assert.False(set.IsError, set.Text);
        Assert.Equal(4, LayerNames.Parse(pdk.Layers).Count);
    }

    ///<summary>**Nothing changes with a problem in it**, and the model is told every one so it can mend them.</summary>
    [Theory]
    [InlineData("{\"rows\":\"met2,69,20\"}", "is not a layer number")]
    [InlineData("{\"rows\":\"69,20,met2,,,,metal\"}", "Line 1")]
    [InlineData("{\"rows\":\"69,20,met2\",\"remove\":[\"99/0\"]}", "99/0 in remove is not a layer of this PDK")]
    [InlineData("{\"remove\":[\"met1\"]}", "\"met1\" in remove is not a layer")]
    [InlineData("{}", "needs layermap rows")]
    public async Task Set_layers_refuses_what_has_a_problem_and_changes_nothing(string arguments, string said)
    {
        var pdk = new Pdk();

        var refused = await LayerTools.SetAsync(pdk.Book(""), arguments);

        Assert.True(refused.IsError);
        Assert.Contains(said, refused.Text);
        Assert.Equal(0, pdk.Replaced);
        Assert.Equal(Layermap, pdk.Layers);
    }

    [Fact]
    public async Task Rows_the_layermap_already_says_change_nothing()
    {
        var pdk = new Pdk();

        var set = await LayerTools.SetAsync(pdk.Book(""), "{\"rows\":\"68,20,met1\"}");

        Assert.False(set.IsError);
        Assert.Contains("already says that", set.Text);
        Assert.Equal(0, pdk.Replaced);
    }
}
