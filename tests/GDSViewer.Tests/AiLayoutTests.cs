using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///What the assistant is told and shown: the system prompt built from where the person is, the picture look draws,
///and the saved models with the rule that keeps a key to its transport.
///
public class AiLayoutTests
{
    private static readonly LayerKey Met1 = new LayerKey(68, 20);

    ///<summary>A library on a nanometer grid with one 2 by 1 µm rectangle on met1 in TOP, from the origin.</summary>
    private static AgentSession WithRectangle()
    {
        var gds = GDS.NewLibrary(stamp: new DateTime(2026, 10, 9));

        gds.AdditionalInformation.AddLayer(Met1);
        gds.AdditionalInformation.Layers[Met1].Name = "met1";

        var session = new AgentSession(gds, new EditHistory());
        var result = AgentTools.Call(session, "draw_rectangle", """{"layer":"met1","from":[0,0],"to":[2,1]}""");

        Assert.False(result.IsError, result.Text);

        return session;
    }

    #region The prompt ************************************************************

    private static LayoutFacts Facts()
    {
        return new LayoutFacts
        {
            FileName = "inverter.gds",
            MicronsPerUnit = 0.001,
            Layers = new[] { "met1 (68/20)", "poly (66/20)" },
            Locked = new[] { "poly (66/20)" },
            CellCount = 3,
            TopCells = new[] { "INV" },
            WorkingCell = "INV",
            Editing = "INV",
            Selected = new[] { 3, 7 },
            SelectedIn = "INV",
            Shown = (0, 0, 12.5, 8),
            DeckLoaded = true,
            CanLook = true
        };
    }

    [Fact]
    public void The_prompt_says_where_the_person_is()
    {
        string prompt = LayoutPrompt.Build(Facts(), ToolApprovalMode.AutoRead);

        Assert.Contains("The file is inverter.gds.", prompt);
        Assert.Contains("One database unit is 0.001 µm.", prompt);
        Assert.Contains("It has 3 cells; the top cell is INV.", prompt);
        Assert.Contains("Its layers: met1 (68/20), poly (66/20).", prompt);
        Assert.Contains("The person has locked poly (66/20)", prompt);
        Assert.Contains("The tools work in INV.", prompt);
        Assert.Contains("The person has INV open for editing in the 2D view.", prompt);
        Assert.Contains("Selected in the 2D view in INV: #3, #7.", prompt);
        Assert.Contains("The 2D view shows x 0 to 12.5 µm and y 0 to 8 µm.", prompt);
        Assert.Contains("A rule deck is loaded", prompt);
    }

    ///<summary>The tools' own conventions - units, shape numbers, undo - are in it word for word.</summary>
    [Fact]
    public void The_prompt_carries_the_tools_conventions_and_the_screens_y()
    {
        string prompt = LayoutPrompt.Build(Facts(), ToolApprovalMode.AutoRead);

        Assert.Contains(AgentTools.Conventions, prompt);
        Assert.Contains("draws y downward", prompt);
        Assert.Contains("look draws a picture", prompt);
    }

    ///<summary>Told the Generator's structures are one call each (2026-11-01, asked for) - and not, with that tool switched off.</summary>
    [Fact]
    public void The_prompt_sends_what_the_generator_makes_to_place_structure()
    {
        Assert.Contains("are put down whole by place_structure", LayoutPrompt.Build(Facts(), ToolApprovalMode.AutoRead));

        string off = LayoutPrompt.Build(Facts(), ToolApprovalMode.AutoRead, new HashSet<string> { AgentTools.PlaceStructureName });

        Assert.DoesNotContain("put down whole by place_structure", off);
    }

    [Fact]
    public void A_long_layer_list_is_cut_short_and_says_where_the_rest_are()
    {
        var layers = Enumerable.Range(1, LayoutPrompt.MostLayers + 5).Select(n => $"{n}/0").ToArray();
        string prompt = LayoutPrompt.Build(new LayoutFacts { Layers = layers }, ToolApprovalMode.AutoRead);

        Assert.Contains($"{LayoutPrompt.MostLayers}/0, and 5 more that list_layers names", prompt);
        Assert.DoesNotContain($"{LayoutPrompt.MostLayers + 1}/0", prompt);
    }

    [Fact]
    public void With_no_cell_to_work_in_the_model_is_told_to_choose_one()
    {
        string prompt = LayoutPrompt.Build(new LayoutFacts { TopCells = new[] { "A", "B" }, CellCount = 2 }, ToolApprovalMode.AutoRead);

        Assert.Contains("call open_cell before drawing anything", prompt);
        Assert.Contains("Nothing is selected.", prompt);
    }

    ///<summary>A model has to know whether anybody is watching: below the top rung it is told calling a change is how it asks.</summary>
    [Theory]
    [InlineData(ToolApprovalMode.Ask, "reading ones included", true)]
    [InlineData(ToolApprovalMode.AutoRead, "A change is shown to them and made only once they approve it", true)]
    [InlineData(ToolApprovalMode.AutoReadWrite, "Every call runs at once, changes included", false)]
    public void The_prompt_says_how_far_the_model_may_go(ToolApprovalMode mode, string said, bool asks)
    {
        string prompt = LayoutPrompt.Build(Facts(), mode);

        Assert.Contains(said, prompt);
        Assert.Equal(asks, prompt.Contains("Never answer that you cannot make changes"));
    }

    #endregion ********************************************************************



    #region The picture ***********************************************************

    [Fact]
    public void A_cell_is_framed_on_what_it_draws_with_a_margin()
    {
        var session = WithRectangle();
        var draft = LayoutPicture.Draft(session.Gds, "TOP", null, session.MicronsPerUnit, null, out string why);

        Assert.NotNull(draft);
        Assert.Equal("", why);

        //2000 by 1000 units, and a twentieth of the longer side all round.
        Assert.Contains("viewBox=\"-100 -100 2200 1200\"", draft!.Document);
        Assert.Equal(LayoutPicture.LongestSide, draft.Width);
        Assert.Equal(559, draft.Height);
        Assert.Contains("<path", draft.Document);
    }

    ///<summary>The words give the picture a scale and a key, and say which way y runs - down, as on the person's screen.</summary>
    [Fact]
    public void The_words_give_the_area_the_scale_the_colors_and_which_way_y_runs()
    {
        var session = WithRectangle();
        var draft = LayoutPicture.Draft(session.Gds, "TOP", null, session.MicronsPerUnit, null, out _)!;
        string color = session.Gds.AdditionalInformation.Layers[Met1].Color;

        Assert.Contains("A picture of TOP from x -0.1 to 2.1 µm and y -0.1 to 1.1 µm, 1024 by 559 pixels, 0.0021 µm a pixel.", draft.Says);
        Assert.Contains("y DOWN the picture, so its top edge is y -0.1 µm and its bottom edge y 1.1 µm", draft.Says);
        Assert.Contains($"met1 68/20 {color}", draft.Says);
    }

    [Fact]
    public void An_area_asked_for_is_the_frame()
    {
        var session = WithRectangle();
        var draft = LayoutPicture.Draft(session.Gds, "TOP", (0, 0, 10, 5), session.MicronsPerUnit, null, out _)!;

        Assert.Contains("viewBox=\"0 0 10000 5000\"", draft.Document);
        Assert.Equal((1024, 512), (draft.Width, draft.Height));
    }

    ///<summary>A long thin area is widened about its middle rather than drawn a few pixels high.</summary>
    [Fact]
    public void A_slim_area_is_widened_to_something_worth_looking_at()
    {
        var session = WithRectangle();
        var draft = LayoutPicture.Draft(session.Gds, "TOP", (0, 0, 100, 1), session.MicronsPerUnit, null, out _)!;

        Assert.Equal(1024, draft.Width);
        Assert.Equal(85, draft.Height);
        Assert.Contains("y -3.6667 to 4.6667 µm", draft.Says);
    }

    ///<summary>A layer the person has hidden is left out of the picture too, and the words say so.</summary>
    [Fact]
    public void A_hidden_layer_is_left_out_and_named()
    {
        var session = WithRectangle();
        var draft = LayoutPicture.Draft(session.Gds, "TOP", null, session.MicronsPerUnit, new HashSet<LayerKey> { Met1 }, out _)!;

        Assert.Contains("Nothing is drawn in that area.", draft.Says);
        Assert.Contains("Left out, because the person has them hidden: met1 68/20.", draft.Says);
        Assert.DoesNotContain("<path", draft.Document);
    }

    [Fact]
    public void There_is_no_picture_of_a_cell_that_is_not_there_or_is_empty()
    {
        var session = WithRectangle();

        Assert.Null(LayoutPicture.Draft(session.Gds, "NOPE", null, 0.001, null, out string missing));
        Assert.Equal("There is no cell called NOPE.", missing);

        var empty = GDS.NewLibrary();

        Assert.Null(LayoutPicture.Draft(empty, "TOP", null, 0.001, null, out string nothing));
        Assert.Equal("TOP has nothing in it to look at.", nothing);
    }

    #endregion ********************************************************************



    #region The saved models ******************************************************

    [Fact]
    public void Saved_models_round_trip_and_their_effort_is_not_kept()
    {
        var saved = new Dictionary<string, LlmConnection>
        {
            ["Claude"] = new LlmConnection { Name = "Claude", ProviderType = LlmConnection.Anthropic, ApiKey = "test-key", MaxTokens = 4000, Effort = "high" },
            ["Local"] = new LlmConnection { Name = "Local", ProviderType = LlmConnection.Custom, BaseUrl = "http://localhost:11434/v1", Model = "llama" }
        };

        string written = LlmConnectionStore.Write(saved);
        var read = LlmConnectionStore.Read(written);

        Assert.Equal(new[] { "Claude", "Local" }, read.Keys);
        Assert.Equal("test-key", read["Claude"].ApiKey);
        Assert.Equal(4000, read["Claude"].MaxTokens);
        Assert.Equal("http://localhost:11434/v1", read["Local"].BaseUrl);
        Assert.Equal("", read["Claude"].Effort);

        //Named as GraphDBViewer names them, so the two describe a model the same way.
        Assert.Contains("\"ProviderType\":\"Anthropic\"", written);
        Assert.DoesNotContain("Effort", written);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Unreadable_saved_models_are_none_rather_than_an_error(string? stored)
    {
        Assert.Empty(LlmConnectionStore.Read(stored));
    }

    ///<summary>The rule of 2026-10-03: a key saved for an encrypted address is not sent to one that is not until it is typed again.</summary>
    [Fact]
    public void A_saved_key_is_held_back_from_a_plain_address_and_comes_back_on_an_encrypted_one()
    {
        var rule = new SavedSecretRule();

        rule.Remember("test-key", true);

        Assert.Null(rule.Keep("test-key", false));
        Assert.True(rule.Says(null));

        Assert.Equal("test-key", rule.Keep(null, true));
        Assert.False(rule.HeldBack);
    }

    [Fact]
    public void A_key_typed_while_one_is_held_back_is_the_persons_own()
    {
        var rule = new SavedSecretRule();

        rule.Remember("test-key", true);
        rule.Keep("test-key", false);

        Assert.Equal("typed-again", rule.Keep("typed-again", false));
        Assert.False(rule.Says("typed-again"));
        Assert.Equal("typed-again", rule.Keep("typed-again", true));
    }

    [Fact]
    public void A_key_saved_without_encryption_was_somebodys_choice_and_is_kept()
    {
        var rule = new SavedSecretRule();

        rule.Remember("test-key", false);

        Assert.Equal("test-key", rule.Keep("test-key", false));
        Assert.False(rule.HeldBack);
    }

    #endregion ********************************************************************
}
