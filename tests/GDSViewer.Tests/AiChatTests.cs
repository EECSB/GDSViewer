using System.Net;
using System.Text.Json.Nodes;
using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///A whole conversation, run against a stand-in provider with a real layout under it.
///
///The provider answers from a script in its own wire format, and the tools are the layout's own - so what is
///checked is what a conversation leaves in the file and on the history, what goes back to the model after each call,
///and what a person sees on the way: the cards, the waits, a decline, a stop.
///
public class AiChatTests
{
    private static readonly LayerKey Met1 = new LayerKey(68, 20);

    private static AgentSession Session()
    {
        var gds = GDS.NewLibrary(stamp: new DateTime(2026, 10, 9));

        gds.AdditionalInformation.AddLayer(Met1);
        gds.AdditionalInformation.Layers[Met1].Name = "met1";

        return new AgentSession(gds, new EditHistory());
    }

    private static int ShapesIn(AgentSession session)
    {
        return Hierarchy.Named(session.Gds, "TOP")!.Elements.Count;
    }

    private const string Rectangle = """{"layer":"met1","from":[0,0],"to":[2,1]}""";

    private static LlmConnection Claude()
    {
        return new LlmConnection { Name = "Claude", ProviderType = LlmConnection.Anthropic, ApiKey = "test-key" };
    }

    private static LlmConnection Gemini()
    {
        return new LlmConnection { Name = "Gemini", ProviderType = LlmConnection.Gemini, ApiKey = "test-key", Model = "gemini-test" };
    }

    ///<summary>A message's words, whether they went as a string or as blocks - the last one of a request is a block, to carry Anthropic's cache mark.</summary>
    private static string? WordsOf(JsonNode? content)
    {
        if (content is JsonValue words)
            return (string?)words;

        if (content is JsonArray blocks)
            return string.Concat(blocks.Select(block => (string?)block?["text"] ?? ""));

        return null;
    }

    ///
    ///A run awaited for no longer than a stand-in ever takes, so a gate that never lets go fails its test rather than
    ///hanging the whole run - which two deliberate breaks of the approval switch did, found by the 2026-10-21 audit.
    ///
    private static async Task<T> Within<T>(Task<T> running)
    {
        return await running.WaitAsync(TimeSpan.FromSeconds(30));
    }

    ///<summary>Waits until a card is waiting for an answer, which happens partway through a send.</summary>
    private static async Task<ChatTurn> Waiting(AiChat chat)
    {
        for (int i = 0; i < 200; i++)
        {
            var card = chat.Turns.LastOrDefault(turn => turn.Kind == ChatTurn.Tool && turn.Decision is null);

            if (card is not null)
                return card;

            await Task.Delay(10);
        }

        throw new TimeoutException("No card came up to be answered.");
    }

    #region The loop **************************************************************

    ///<summary>The whole round trip with Anthropic: a call, the change it makes, the answer that goes back, and the reply.</summary>
    [Fact]
    public async Task A_change_asked_of_claude_is_made_and_answered()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle, "Drawing it."), AiStandIn.AnthropicText("Drew a 2 by 1 µm rectangle on met1."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        await chat.SendAsync("Draw a 2 by 1 rectangle on met1", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        Assert.Equal(1, ShapesIn(session));
        Assert.Equal(1, session.History.Count);

        Assert.Equal(new[] { ChatTurn.User, ChatTurn.Tool, ChatTurn.Assistant }, chat.Turns.Select(turn => turn.Kind));
        Assert.Equal("Drew a 2 by 1 µm rectangle on met1.", chat.Turns[2].Text);

        var card = chat.Turns[1];

        Assert.True(card.Auto);
        Assert.True(card.Changes);
        Assert.True(card.Decision);
        Assert.StartsWith("Drew a 2 by 1 µm rectangle on met1", card.Result);

        //The second request carries the model's own call back, and the tool's answer to it under the call's id.
        Assert.Equal(2, standIn.Requests.Count);

        var messages = standIn.Requests[1].Body["messages"]!.AsArray();
        var answer = messages[^1]!["content"]![0]!;

        Assert.Equal("assistant", (string?)messages[^2]!["role"]);
        Assert.Equal("tool_result", (string?)answer["type"]);
        Assert.Equal("toolu_1", (string?)answer["tool_use_id"]);
        Assert.Equal(card.Result, (string?)answer["content"]);
    }

    ///
    ///**The card of a change carries what it did to the file's text**, for the window to show as a diff - see
    ///TextChange - and the model is sent the tool's words alone.
    ///
    [Fact]
    public async Task A_change_s_card_carries_what_it_did_to_the_text()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle, "Drawing it."), AiStandIn.AnthropicText("Drew it."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        await chat.SendAsync("Draw a 2 by 1 rectangle on met1", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session) { DiffsText = true });

        var change = chat.Turns[1].TextChange!;

        Assert.DoesNotContain("BOUNDARY", change.Before);
        Assert.Contains("BOUNDARY", change.After);
        Assert.DoesNotContain("BOUNDARY", standIn.Requests[1].Body.ToJsonString());
    }

    ///<summary>The same through the OpenAI-compatible adapter at Google's endpoint, which is how the Gemini key is used.</summary>
    [Fact]
    public async Task A_change_asked_of_gemini_is_made_and_answered()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.OpenAiCall("call_1", "draw_rectangle", Rectangle), AiStandIn.OpenAiText("Done."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        await chat.SendAsync("Draw it", new OpenAiProvider(standIn.Client(), Gemini()), "system", new LayoutToolRunner(session));

        Assert.Equal(1, ShapesIn(session));
        Assert.Equal("Done.", chat.Turns[^1].Text);

        var messages = standIn.Requests[1].Body["messages"]!.AsArray();

        Assert.Equal("assistant", (string?)messages[^2]!["role"]);
        Assert.Equal("c2lnbmVk", (string?)messages[^2]!["tool_calls"]![0]!["extra_content"]!["google"]!["thought_signature"]);
        Assert.Equal("tool", (string?)messages[^1]!["role"]);
        Assert.Equal("call_1", (string?)messages[^1]!["tool_call_id"]);
    }

    ///<summary>A refusal goes back as one, the file untouched, and the model gets to try again.</summary>
    [Fact]
    public async Task A_refused_call_goes_back_marked_and_leaves_the_file_alone()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", """{"layer":"mett1","from":[0,0],"to":[1,1]}"""), AiStandIn.AnthropicText("There is no such layer."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        await chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        Assert.Equal(0, ShapesIn(session));
        Assert.Equal(0, session.History.Count);
        Assert.True(chat.Turns[1].ResultIsError);

        var answer = standIn.Requests[1].Body["messages"]!.AsArray()[^1]!["content"]![0]!;

        Assert.True((bool?)answer["is_error"]);
    }

    ///
    ///A model that keeps calling, every call working, is paused at the cap rather than failed (2026-11-01): what it
    ///changed stays, each change an undo step, and the turn is a pause the next message carries on from.
    ///
    [Fact]
    public async Task A_model_that_keeps_working_is_paused_at_the_cap_not_failed()
    {
        var session = Session();
        int calls = 0;
        var standIn = new AiStandIn((_, _) => AiStandIn.Json(AiStandIn.AnthropicCall("toolu_" + calls++, "draw_rectangle", Rectangle)));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        var outcome = await chat.SendAsync("Draw forever", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        Assert.Equal(ChatOutcome.Paused, outcome);
        Assert.Equal(LlmProviderFactory.DefaultToolRounds, standIn.Requests.Count);
        Assert.Equal(LlmProviderFactory.DefaultToolRounds, session.History.Count);

        var ending = chat.Turns[^1];

        Assert.Equal(ChatTurn.Paused, ending.Kind);
        Assert.Equal(LlmProviderFactory.RoundsPaused(LlmProviderFactory.DefaultToolRounds), ending.Text);
        Assert.False(chat.Generating);

        //Carried on: the calls before the pause go with the next message, said in words.
        var sent = chat.BuildConversation();

        Assert.Contains("draw_rectangle", sent[^1].Text);
    }

    ///<summary>The rounds set under AI settings are the ones a run pauses at, and the pause asks about another as many.</summary>
    [Fact]
    public async Task A_run_pauses_at_the_rounds_set_and_asks_about_as_many_again()
    {
        var session = Session();
        int calls = 0;
        var standIn = new AiStandIn((_, _) => AiStandIn.Json(AiStandIn.AnthropicCall("toolu_" + calls++, "draw_rectangle", Rectangle)));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite, MaxToolRounds = 5 };

        var outcome = await chat.SendAsync("Draw forever", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        Assert.Equal(ChatOutcome.Paused, outcome);
        Assert.Equal(5, standIn.Requests.Count);
        Assert.Contains("limit of 5 rounds", chat.Turns[^1].Text);
        Assert.Contains("another 5 rounds?", chat.Turns[^1].Text);
    }

    ///<summary>The rounds kept: a number out of range is brought into it, and anything else is the default.</summary>
    [Theory]
    [InlineData(null, LlmProviderFactory.DefaultToolRounds)]
    [InlineData("", LlmProviderFactory.DefaultToolRounds)]
    [InlineData("many", LlmProviderFactory.DefaultToolRounds)]
    [InlineData("45", 45)]
    [InlineData("0", ToolRoundsPreference.Fewest)]
    [InlineData("-3", ToolRoundsPreference.Fewest)]
    [InlineData("100000", ToolRoundsPreference.Most)]
    public void Kept_rounds_are_read_into_range(string? kept, int expected)
    {
        Assert.Equal(expected, ToolRoundsPreference.Read(kept));
    }

    ///<summary>A model whose every call is refused is stopped as stuck after a few rounds, not left to run to the cap.</summary>
    [Fact]
    public async Task A_model_whose_calls_keep_failing_is_stopped_as_stuck()
    {
        var session = Session();
        int calls = 0;
        var standIn = new AiStandIn((_, _) => AiStandIn.Json(AiStandIn.AnthropicCall("toolu_" + calls++, "draw_rectangle", """{"layer":"mett1","from":[0,0],"to":[1,1]}""")));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        var outcome = await chat.SendAsync("Draw on a layer that is not there", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        Assert.Equal(ChatOutcome.Failed, outcome);
        Assert.Equal(LlmProviderFactory.MaxStuckRounds, standIn.Requests.Count);
        Assert.Equal(ChatTurn.Error, chat.Turns[^1].Kind);
        Assert.StartsWith(LlmProviderFactory.RoundsStuck, chat.Turns[^1].Text);
    }

    ///<summary>A model asking the same thing over and over, and told the same each time, is stopped as stuck - the first ask was progress.</summary>
    [Fact]
    public async Task A_model_repeating_the_same_read_is_stopped_as_stuck()
    {
        var session = Session();
        int calls = 0;
        var standIn = new AiStandIn((_, _) => AiStandIn.Json(AiStandIn.AnthropicCall("toolu_" + calls++, "list_layers", "{}")));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        var outcome = await chat.SendAsync("List the layers", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        Assert.Equal(ChatOutcome.Failed, outcome);
        Assert.Equal(1 + LlmProviderFactory.MaxStuckRounds, standIn.Requests.Count);
        Assert.StartsWith(LlmProviderFactory.RoundsStuck, chat.Turns[^1].Text);
    }

    ///<summary>The OpenAI-compatible loop pauses at the cap the same way.</summary>
    [Fact]
    public async Task The_openai_loop_is_paused_at_the_cap_too()
    {
        var session = Session();
        int calls = 0;
        var standIn = new AiStandIn((_, _) => AiStandIn.Json(AiStandIn.OpenAiCall("call_" + calls++, "draw_rectangle", Rectangle)));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        var outcome = await chat.SendAsync("Draw forever", new OpenAiProvider(standIn.Client(), Gemini()), "system", new LayoutToolRunner(session));

        Assert.Equal(ChatOutcome.Paused, outcome);
        Assert.Equal(LlmProviderFactory.DefaultToolRounds, standIn.Requests.Count);
    }

    [Fact]
    public async Task A_failure_is_a_turn_that_says_what_went_wrong()
    {
        var standIn = new AiStandIn((_, _) => AiStandIn.Json("""{"error":{"message":"model not found"}}""", System.Net.HttpStatusCode.NotFound));
        var chat = new AiChat();

        await chat.SendAsync("Hi", new OpenAiProvider(standIn.Client(), Gemini()), "system", new LayoutToolRunner(Session()));

        Assert.Equal(ChatTurn.Error, chat.Turns[^1].Kind);
        Assert.Equal("Gemini answered 404 Not Found: model not found", chat.Turns[^1].Text);
    }

    #endregion ********************************************************************



    #region Approval **************************************************************

    ///<summary>The middle rung, where the window starts: a read runs at once, a change waits for a person.</summary>
    [Fact]
    public async Task On_the_middle_rung_a_read_runs_and_a_change_waits()
    {
        var session = Session();
        var standIn = new AiStandIn(
            AiStandIn.AnthropicCall("toolu_1", "list_layers", "{}"),
            AiStandIn.AnthropicCall("toolu_2", "draw_rectangle", Rectangle),
            AiStandIn.AnthropicText("Drew it."));
        var chat = new AiChat();

        Assert.Equal(ToolApprovalMode.AutoRead, chat.Mode);

        var sending = chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));
        var card = await Waiting(chat);

        //The read went through on its own and is on screen as having run.
        var read = chat.Turns.Single(turn => turn.ToolName == "list_layers");

        Assert.True(read.Auto);
        Assert.NotNull(read.Result);

        //The change is waiting, and nothing has been drawn.
        Assert.Equal("draw_rectangle", card.ToolName);
        Assert.Equal(0, ShapesIn(session));

        chat.Decide(card, true);

        await Within(sending);

        Assert.Equal(1, ShapesIn(session));
        Assert.Equal("Drew it.", chat.Turns[^1].Text);
    }

    [Fact]
    public async Task On_the_bottom_rung_even_a_read_waits()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "list_layers", "{}"), AiStandIn.AnthropicText("One layer."));
        var chat = new AiChat { Mode = ToolApprovalMode.Ask };

        var sending = chat.SendAsync("What layers?", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(Session()));
        var card = await Waiting(chat);

        Assert.Equal("list_layers", card.ToolName);
        Assert.Single(standIn.Requests);

        chat.Decide(card, true);

        await Within(sending);

        Assert.Equal("One layer.", chat.Turns[^1].Text);
    }

    ///<summary>A decline is an answer to the call: the model is told it was not done, and the file is as it was.</summary>
    [Fact]
    public async Task A_declined_change_is_not_made_and_the_model_is_told()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("All right, I left it."));
        var chat = new AiChat();

        var sending = chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        chat.Decide(await Waiting(chat), false);

        await Within(sending);

        Assert.Equal(0, ShapesIn(session));
        Assert.Equal(0, session.History.Count);
        Assert.False(chat.Turns[1].Decision);

        var answer = standIn.Requests[1].Body["messages"]!.AsArray()[^1]!["content"]![0]!;

        Assert.Equal(ApprovingToolRunner.DeclinedMessage, (string?)answer["content"]);
    }

    ///<summary>A stop while a card waits lets it go as a no, and no request follows.</summary>
    [Fact]
    public async Task A_stop_while_a_card_waits_ends_the_answer()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("never"));
        var chat = new AiChat();

        var sending = chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));

        await Waiting(chat);

        chat.Cancel();

        await Within(sending);

        Assert.Equal(0, ShapesIn(session));
        Assert.Single(standIn.Requests);
        Assert.Equal("Stopped.", chat.Turns[^1].Text);
        Assert.False(chat.Generating);
    }

    ///
    ///**A rung chosen in the middle of an answer is in force at once** (asked for 2026-10-21): a change waiting on the
    ///middle rung is let go by a switch to the top one, marked as run without asking, and the change after it in the same
    ///answer runs unasked. The rung was read once as the answer started, so both still waited.
    ///
    [Fact]
    public async Task A_switch_to_the_top_rung_lets_a_waiting_change_go_and_the_next_runs_unasked()
    {
        var session = Session();
        var standIn = new AiStandIn(
            AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle),
            AiStandIn.AnthropicCall("toolu_2", "draw_rectangle", """{"layer":"met1","from":[3,0],"to":[4,1]}"""),
            AiStandIn.AnthropicText("Drew both."));
        var chat = new AiChat();

        var sending = chat.SendAsync("Draw two", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));
        var card = await Waiting(chat);

        Assert.Equal(0, ShapesIn(session));

        chat.Mode = ToolApprovalMode.AutoReadWrite;

        await Within(sending);

        Assert.Equal(2, ShapesIn(session));
        Assert.Equal("Drew both.", chat.Turns[^1].Text);

        var cards = chat.Turns.Where(turn => turn.Kind == ChatTurn.Tool).ToList();

        Assert.Equal(2, cards.Count);
        Assert.Same(card, cards[0]);
        Assert.All(cards, ran => Assert.True(ran.Auto));
        Assert.All(cards, ran => Assert.True(ran.Decision));
    }

    ///<summary>A switch to a rung that still asks for changes leaves a waiting change waiting, for a person to answer.</summary>
    [Fact]
    public async Task A_switch_that_still_asks_leaves_a_waiting_change_waiting()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("Drew it."));
        var chat = new AiChat { Mode = ToolApprovalMode.Ask };

        var sending = chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session));
        var card = await Waiting(chat);

        chat.Mode = ToolApprovalMode.AutoRead;

        Assert.False(card.Gate!.Task.IsCompleted);
        Assert.False(card.Auto);

        chat.Decide(card, true);

        await Within(sending);

        Assert.Equal(1, ShapesIn(session));
        Assert.False(card.Auto);
    }

    ///<summary>A read waiting on the bottom rung goes when the middle one is chosen, since that rung reads unasked.</summary>
    [Fact]
    public async Task A_switch_to_the_middle_rung_lets_a_waiting_read_go()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "list_layers", "{}"), AiStandIn.AnthropicText("One layer."));
        var chat = new AiChat { Mode = ToolApprovalMode.Ask };

        var sending = chat.SendAsync("What layers?", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(Session()));
        var card = await Waiting(chat);

        chat.Mode = ToolApprovalMode.AutoRead;

        await Within(sending);

        Assert.True(card.Auto);
        Assert.True(card.Decision);
        Assert.Equal("One layer.", chat.Turns[^1].Text);
    }

    [Theory]
    [InlineData(ToolApprovalMode.Ask, false, true)]
    [InlineData(ToolApprovalMode.Ask, true, true)]
    [InlineData(ToolApprovalMode.AutoRead, false, false)]
    [InlineData(ToolApprovalMode.AutoRead, true, true)]
    [InlineData(ToolApprovalMode.AutoReadWrite, false, false)]
    [InlineData(ToolApprovalMode.AutoReadWrite, true, false)]
    public void Each_rung_says_what_still_has_to_be_asked(ToolApprovalMode mode, bool changes, bool expected)
    {
        Assert.Equal(expected, ApprovingToolRunner.NeedsApproval(mode, changes));
    }

    ///<summary>The changing tools are not offered, nor honored, until the gate is in front of them.</summary>
    [Fact]
    public async Task Without_the_gate_no_change_is_offered_or_made()
    {
        var session = Session();
        var runner = new LayoutToolRunner(session);

        Assert.DoesNotContain(runner.Tools, tool => tool.Changes);
        Assert.Contains(runner.Tools, tool => tool.Name == "find_shapes");

        var output = await runner.RunToolAsync("draw_rectangle", Rectangle, CancellationToken.None);

        Assert.True(output.IsError);
        Assert.Equal(0, ShapesIn(session));

        _ = new ApprovingToolRunner(runner, (_, _) => Task.FromResult(true));

        Assert.Contains(runner.Tools, tool => tool.Name == "draw_rectangle" && tool.Changes);
    }

    [Fact]
    public async Task With_no_approver_nothing_runs()
    {
        var session = Session();
        var gate = new ApprovingToolRunner(new LayoutToolRunner(session), null, null, ToolApprovalMode.AutoReadWrite);

        var output = await gate.RunToolAsync("draw_rectangle", Rectangle, CancellationToken.None);

        Assert.Equal(ApprovingToolRunner.DeclinedMessage, output.Text);
        Assert.Equal(0, ShapesIn(session));
    }

    #endregion ********************************************************************



    #region The runner ************************************************************

    ///<summary>Each change is told to the page as it lands, with the shapes it made, so the view can draw them.</summary>
    [Fact]
    public async Task The_page_hears_of_each_change_with_what_it_made()
    {
        var session = Session();
        var heard = new List<AgentResult>();
        var runner = new LayoutToolRunner(session) { AllowEdits = true };

        runner.Changed = result =>
        {
            heard.Add(result);

            return Task.CompletedTask;
        };

        await runner.RunToolAsync("list_layers", "{}", CancellationToken.None);
        await runner.RunToolAsync("draw_rectangle", Rectangle, CancellationToken.None);

        var change = Assert.Single(heard);

        Assert.True(runner.ChangedAnything);
        Assert.Same(Hierarchy.Named(session.Gds, "TOP")!.Elements[0], session.ShapeNumbered(Assert.Single(change.Made)));
    }

    [Fact]
    public async Task Look_draws_the_cell_being_worked_in()
    {
        var session = Session();
        LookRequest? asked = null;

        var runner = new LayoutToolRunner(session, (request, _) =>
        {
            asked = request;

            return Task.FromResult(new LlmToolOutput { Text = "A picture.", Png = "iVBORw0KGgo=" });
        });

        Assert.Contains(runner.Tools, tool => tool.Name == LayoutToolRunner.LookName && !tool.Changes);

        var output = await runner.RunToolAsync("look", """{"area":[0,0,10,5]}""", CancellationToken.None);

        Assert.Equal("iVBORw0KGgo=", output.Png);
        Assert.Equal("TOP", asked!.Cell);
        Assert.Equal((0.0, 0.0, 10.0, 5.0), asked.Area);
        Assert.Equal(session.MicronsPerUnit, asked.MicronsPerUnit);
    }

    [Theory]
    [InlineData("""{"area":[0,0,10]}""")]
    [InlineData("""{"area":[10,0,0,5]}""")]
    [InlineData("""{"area":"all"}""")]
    [InlineData("not json")]
    public async Task Look_refuses_an_area_that_is_not_one(string arguments)
    {
        bool drawn = false;

        var runner = new LayoutToolRunner(Session(), (_, _) =>
        {
            drawn = true;

            return Task.FromResult(new LlmToolOutput { Text = "" });
        });

        var output = await runner.RunToolAsync("look", arguments, CancellationToken.None);

        Assert.True(output.IsError);
        Assert.False(drawn);
    }

    ///<summary>A picture travels with Anthropic inside the tool's result, and with the OpenAI-compatible family in a user turn after it.</summary>
    [Fact]
    public async Task A_look_reaches_both_providers_as_a_picture()
    {
        static LayoutToolRunner looking()
        {
            return new LayoutToolRunner(Session(), (_, _) => Task.FromResult(new LlmToolOutput { Text = "A picture.", Png = "iVBORw0KGgo=" }));
        }

        var claude = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "look", "{}"), AiStandIn.AnthropicText("I see it."));

        await new AiChat().SendAsync("Look", new AnthropicProvider(claude.Client(), Claude()), "system", looking());

        var block = claude.Requests[1].Body["messages"]!.AsArray()[^1]!["content"]![0]!["content"]!.AsArray();

        Assert.Equal("image", (string?)block[1]!["type"]);

        var gemini = new AiStandIn(AiStandIn.OpenAiCall("call_1", "look", "{}"), AiStandIn.OpenAiText("I see it."));

        await new AiChat().SendAsync("Look", new OpenAiProvider(gemini.Client(), Gemini()), "system", looking());

        var messages = gemini.Requests[1].Body["messages"]!.AsArray();

        Assert.Equal("tool", (string?)messages[^2]!["role"]);
        Assert.Equal("user", (string?)messages[^1]!["role"]);
        Assert.Equal("image_url", (string?)messages[^1]!["content"]![1]!["type"]);
    }

    #endregion ********************************************************************



    #region The conversation ******************************************************

    ///<summary>What was run is said in words in the next message, since a tool exchange cannot cross from one request to the next.</summary>
    [Fact]
    public async Task What_was_run_is_replayed_in_words()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("Drew it."), AiStandIn.AnthropicText("It is 2 µm wide."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };
        var runner = new LayoutToolRunner(Session());
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        await chat.SendAsync("Draw it", provider, "system", runner);
        await chat.SendAsync("How wide?", provider, "system", runner);

        var conversation = standIn.Requests[2].Body["messages"]!.AsArray().Select(each => ((string?)each!["role"], WordsOf(each["content"]))).ToList();

        Assert.Equal(("user", "Draw it"), conversation[0]);
        Assert.Equal("user", conversation[1].Item1);
        Assert.Contains("You called draw_rectangle", conversation[1].Item2);
        Assert.Contains("Drew a 2 by 1 µm rectangle", conversation[1].Item2);
        Assert.Equal(("assistant", "Drew it."), conversation[2]);
        Assert.Equal(("user", "How wide?"), conversation[3]);
    }

    ///<summary>Another file opened: what came before stays on screen and is not sent.</summary>
    [Fact]
    public async Task After_a_new_file_the_model_starts_afresh()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("First."), AiStandIn.AnthropicText("Second."));
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        await chat.SendAsync("About the old file", provider, "system", null);

        chat.StartAfresh("Another file is open now.");

        await chat.SendAsync("About the new one", provider, "system", null);

        Assert.Contains(chat.Turns, turn => turn.Text == "About the old file");
        Assert.Contains(chat.Turns, turn => turn.Kind == ChatTurn.Note);

        var sent = Assert.Single(standIn.Requests[1].Body["messages"]!.AsArray());

        Assert.Equal("About the new one", (string?)sent!["content"]);
    }

    ///<summary>Clear during an answer: the answer's ending is not written into the conversation that replaced it.</summary>
    [Fact]
    public async Task Clear_during_an_answer_leaves_an_empty_conversation()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("never"));
        var chat = new AiChat();

        var sending = chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(Session()));

        await Waiting(chat);

        chat.Clear();

        await Within(sending);

        Assert.Empty(chat.Turns);
        Assert.False(chat.Generating);
    }

    [Fact]
    public async Task Nothing_is_sent_for_an_empty_message_or_during_an_answer()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("Done."));
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        await chat.SendAsync("   ", provider, "system", null);

        Assert.Empty(chat.Turns);

        var sending = chat.SendAsync("Draw it", provider, "system", new LayoutToolRunner(Session()));
        var card = await Waiting(chat);

        await chat.SendAsync("And another", provider, "system", null);

        Assert.DoesNotContain(chat.Turns, turn => turn.Text == "And another");

        chat.Decide(card, true);

        await Within(sending);
    }

    #endregion ********************************************************************



    #region Files, and what it cost ***********************************************

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    ///<summary>A PDF of nine megabytes - two of them are past what one conversation sends.</summary>
    private static byte[] BigPdf()
    {
        var data = new byte[9 * 1024 * 1024];

        "%PDF-"u8.CopyTo(data);

        return data;
    }

    ///<summary>The files in the box go with the question and leave the box; and every later question sends them again, the whole conversation going each time.</summary>
    [Fact]
    public async Task Files_go_with_their_question_and_again_with_the_next()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("A transistor."), AiStandIn.AnthropicText("Its gate."));
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());
        var shot = new AiAttachment("shot.png", "image/png", Png);

        Assert.Null(chat.Attach(shot));

        var outcome = await chat.SendAsync("What is this?", provider, "system", null);

        Assert.Equal(ChatOutcome.Answered, outcome);
        Assert.Empty(chat.Pending);
        Assert.Same(shot, Assert.Single(chat.Turns[0].Attachments));
        Assert.Equal("image", (string?)standIn.Requests[0].Body["messages"]![0]!["content"]![1]!["type"]);

        await chat.SendAsync("And this part?", provider, "system", null);

        var first = standIn.Requests[1].Body["messages"]![0]!["content"]!.AsArray();

        Assert.Equal("image", (string?)first[1]!["type"]);
        Assert.Equal(Png.Length, chat.SentBytes);
        Assert.Single(chat.SentFiles());
    }

    ///<summary>Unticked in the files panel: on screen still, not sent, and said in a line where it was.</summary>
    [Fact]
    public async Task An_unticked_file_is_said_and_not_sent()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("A transistor."), AiStandIn.AnthropicText("Fine."));
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());
        var shot = new AiAttachment("shot.png", "image/png", Png);

        chat.Attach(shot);

        await chat.SendAsync("What is this?", provider, "system", null);

        Assert.Null(chat.Include(shot, false));
        Assert.Equal(0, chat.SentBytes);

        await chat.SendAsync("Without the picture now", provider, "system", null);

        string sent = standIn.Requests[1].Body.ToJsonString();

        Assert.DoesNotContain(Convert.ToBase64String(Png), sent);
        Assert.Contains("has since left it out of the conversation", sent);
        Assert.Single(chat.SentFiles());
    }

    ///<summary>A refused file would be refused with every later question, so a failed question gives its files back to the box.</summary>
    [Fact]
    public async Task A_failed_question_gives_its_files_back()
    {
        var standIn = new AiStandIn((_, _) => AiStandIn.Json("""{"error":{"message":"The PDF could not be read."}}""", HttpStatusCode.BadRequest));
        var chat = new AiChat();
        var pdf = new AiAttachment("rules.pdf", "application/pdf", "%PDF-1.7"u8.ToArray());

        chat.Attach(pdf);

        var outcome = await chat.SendAsync("Summarize it", new AnthropicProvider(standIn.Client(), Claude()), "system", null);

        Assert.Equal(ChatOutcome.Failed, outcome);
        Assert.True(chat.GaveFilesBack);
        Assert.Same(pdf, Assert.Single(chat.Pending));
        Assert.Empty(chat.Turns[0].Attachments);
        Assert.Equal(ChatTurn.Error, chat.Turns[^1].Kind);
    }

    ///
    ///**A stop keeps its files where they were**, it being no sign that one was refused - and what the run was billed
    ///for before it stopped is counted on the stop: each request was billed as it was answered.
    ///
    [Fact]
    public async Task A_stopped_question_keeps_its_files_and_its_spend_is_counted()
    {
        var standIn = new AiStandIn(AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), 2000, 50));
        var chat = new AiChat();

        chat.Attach(new AiAttachment("shot.png", "image/png", Png));

        var sending = chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(Session()));

        await Waiting(chat);

        chat.Cancel();

        Assert.Equal(ChatOutcome.Stopped, await Within(sending));
        Assert.False(chat.GaveFilesBack);
        Assert.Single(chat.Turns[0].Attachments);
        Assert.Empty(chat.Pending);

        var stopped = chat.Turns[^1];

        Assert.StartsWith("Stopped.", stopped.Text);
        Assert.Equal(2000, stopped.Usage!.InputTokens);
        Assert.Equal(AnthropicProvider.DefaultModel, stopped.Model);

        //2,000 in at $4 and 50 out at $20 a million.
        Assert.Equal(0.009m, stopped.Cost);
        Assert.Equal(0.009m, chat.Cost);
    }

    ///<summary>Spend is kept by model, each at its own rates - none for a model nobody priced - and Clear does not take it back.</summary>
    [Fact]
    public async Task Spend_is_kept_by_model_and_outlives_clear()
    {
        var claude = new AiStandIn(AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicText("Hi."), 1000, 100));
        var gemini = new AiStandIn(AiStandIn.WithOpenAiUsage(AiStandIn.OpenAiText("Hello."), 3000, 200));
        var chat = new AiChat();

        await chat.SendAsync("Hi", new AnthropicProvider(claude.Client(), Claude()), "system", null);
        await chat.SendAsync("Hi again", new OpenAiProvider(gemini.Client(), Gemini()), "system", null);

        Assert.Equal(new[] { "claude-opus-5-5", "gemini-test" }, chat.Spend.Keys.OrderBy(model => model, StringComparer.Ordinal));
        Assert.Equal(0.006m, chat.Spend["claude-opus-5-5"].Cost);
        Assert.Null(chat.Spend["gemini-test"].Cost);
        Assert.Equal(4000, chat.Used.InputTokens);

        var answer = chat.Turns[1];

        Assert.Equal("claude-opus-5-5", answer.Model);
        Assert.Equal(1, answer.Usage!.Requests);
        Assert.Null(chat.Turns[3].Cost);

        chat.Clear();

        Assert.Equal(0.006m, chat.Cost);
    }

    [Fact]
    public void A_question_carries_ten_files_and_a_conversation_twelve_megabytes()
    {
        var chat = new AiChat();

        for (int i = 0; i < AiAttachment.MostPerQuestion; i++)
            Assert.Null(chat.Attach(new AiAttachment($"note{i}.txt", "", "a"u8.ToArray())));

        Assert.Contains("10 files at most", chat.Attach(new AiAttachment("one more.txt", "", "a"u8.ToArray())));

        var heavy = new AiChat();

        Assert.Null(heavy.Attach(new AiAttachment("a.pdf", "", BigPdf())));
        Assert.Contains("past the 12 MB one conversation can send", heavy.Attach(new AiAttachment("b.pdf", "", BigPdf())));
    }

    ///<summary>Unticking is the way out of a conversation that can take no more; ticking back in is held to the same budget.</summary>
    [Fact]
    public async Task A_file_ticked_back_in_is_held_to_the_budget()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Read it."));
        var chat = new AiChat();
        var first = new AiAttachment("a.pdf", "", BigPdf());

        chat.Attach(first);

        await chat.SendAsync("Read this", new AnthropicProvider(standIn.Client(), Claude()), "system", null);

        Assert.Null(chat.Include(first, false));
        Assert.Null(chat.Attach(new AiAttachment("b.pdf", "", BigPdf())));
        Assert.Contains("Untick another first", chat.Include(first, true));
        Assert.True(first.Excluded);
    }

    ///<summary>Another file opened: the files sent before it are not sent any more, and do not count against the budget.</summary>
    [Fact]
    public async Task Files_from_before_a_new_file_are_no_longer_sent()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("A transistor."));
        var chat = new AiChat();

        chat.Attach(new AiAttachment("shot.png", "image/png", Png));

        await chat.SendAsync("What is this?", new AnthropicProvider(standIn.Client(), Claude()), "system", null);

        chat.StartAfresh("Another file is open now.");

        Assert.Empty(chat.SentFiles());
        Assert.Equal(0, chat.SentBytes);
    }

    #endregion ********************************************************************



    #region Compacting, the tool picker and the transcript ************************

    ///<summary>A provider that never answers, for a stop to land on.</summary>
    private sealed class Silent : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);

            return new HttpResponseMessage();
        }
    }

    ///
    ///**Compacting puts a briefing in place of the conversation**: one request under the compaction's own instructions
    ///and no tools, carrying the transcript - each call with what came of it - and the files still ticked; and the next
    ///question goes with the briefing alone, the picture kept on it unticked.
    ///
    [Fact]
    public async Task Compacting_puts_a_briefing_in_place_of_the_conversation()
    {
        var session = Session();
        var standIn = new AiStandIn(
            AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle),
            AiStandIn.AnthropicText("Drew #1."),
            AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicText("Working on: a met1 rectangle, #1, from (0, 0) to (2, 1) µm."), 3000, 400),
            AiStandIn.AnthropicText("It is 2 µm wide."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };
        var provider = new AnthropicProvider(standIn.Client(), Claude());
        var shot = new AiAttachment("shot.png", "image/png", Png);

        chat.Attach(shot);

        await chat.SendAsync("Draw a 2 by 1 rectangle on met1", provider, "system", new LayoutToolRunner(session));

        Assert.Equal(ChatOutcome.Answered, await chat.CompactAsync(provider));

        //The compaction's own request: its instructions, no tools, and one message holding the picture and the transcript.
        var compaction = standIn.Requests[2].Body;
        var message = Assert.Single(compaction["messages"]!.AsArray())!;
        string transcript = WordsOf(message["content"]) ?? "";

        Assert.Equal(AiChat.CompactSystem, (string?)compaction["system"]);
        Assert.Null(compaction["tools"]);
        Assert.Contains(message["content"]!.AsArray(), block => (string?)block!["type"] == "image");
        Assert.Contains("PERSON:", transcript);
        Assert.Contains("Draw a 2 by 1 rectangle on met1", transcript);
        Assert.Contains("- called draw_rectangle {", transcript);
        Assert.Contains("and it answered: Drew a 2 by 1 µm rectangle on met1", transcript);
        Assert.Contains("Drew #1.", transcript);

        //In the conversation: the briefing after what it stands in for, with another of the picture on it, unticked.
        var briefing = chat.Turns[^1];
        var kept = Assert.Single(briefing.Attachments);

        Assert.Equal(ChatTurn.Summary, briefing.Kind);
        Assert.Equal(3, chat.FoldedBefore);
        Assert.NotSame(shot, kept);
        Assert.Same(shot.Data, kept.Data);
        Assert.True(kept.Excluded);
        Assert.False(shot.Excluded);
        Assert.Equal(0, chat.SentBytes);
        Assert.Equal(400, briefing.Usage!.OutputTokens);

        //And the next question goes with the briefing alone, introduced as what it is.
        await chat.SendAsync("How wide is it?", provider, "system", null);

        string sent = standIn.Requests[3].Body.ToJsonString();

        Assert.Contains(AiChat.SummaryPreface, sent);
        Assert.Contains("a met1 rectangle, #1", sent);
        Assert.Contains("How wide is it?", sent);
        Assert.DoesNotContain("Draw a 2 by 1 rectangle", sent);
        Assert.DoesNotContain(Convert.ToBase64String(Png), sent);
    }

    ///<summary>A compaction refused leaves the conversation exactly as it was, with a line saying so.</summary>
    [Fact]
    public async Task A_compaction_that_fails_leaves_the_conversation_as_it_was()
    {
        var standIn = new AiStandIn((index, _) =>
        {
            if (index == 0)
                return AiStandIn.Json(AiStandIn.AnthropicText("A transistor."));

            return AiStandIn.Json("""{"error":{"message":"The request was too large."}}""", HttpStatusCode.BadRequest);
        });
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        await chat.SendAsync("What is this?", provider, "system", null);

        var before = chat.BuildConversation().Select(message => message.Text).ToList();

        Assert.Equal(ChatOutcome.Failed, await chat.CompactAsync(provider));

        Assert.DoesNotContain(chat.Turns, turn => turn.Kind == ChatTurn.Summary);
        Assert.StartsWith("The conversation could not be compacted, and is as it was.", chat.Turns[^1].Text);
        Assert.Contains("The request was too large.", chat.Turns[^1].Text);
        Assert.Equal(before, chat.BuildConversation().Select(message => message.Text));
        Assert.Equal(0, chat.FoldedBefore);
        Assert.False(chat.Generating);
    }

    [Fact]
    public async Task A_compaction_stopped_leaves_the_conversation_as_it_was()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("A transistor."));
        var chat = new AiChat();

        await chat.SendAsync("What is this?", new AnthropicProvider(standIn.Client(), Claude()), "system", null);

        var compacting = chat.CompactAsync(new AnthropicProvider(new HttpClient(new Silent()), Claude()));

        Assert.True(chat.Compacting);

        chat.Cancel();

        Assert.Equal(ChatOutcome.Stopped, await Within(compacting));
        Assert.Equal("Compacting was stopped. The conversation is as it was.", chat.Turns[^1].Text);
        Assert.DoesNotContain(chat.Turns, turn => turn.Kind == ChatTurn.Summary);
        Assert.False(chat.Compacting);
        Assert.False(chat.Generating);
    }

    ///
    ///**Suggested once there is a lot to stop sending and a conversation likely to go on**: three questions since it
    ///began or was last compacted, and 20,000 tokens of history - two answers of 40,000 characters are past that alone.
    ///Put away until the next compaction, after which there is nothing new to compact.
    ///
    [Fact]
    public async Task Compacting_is_suggested_after_three_questions_and_twenty_thousand_tokens()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText(new string('a', 40_000)));
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        Assert.Equal("There is nothing to compact yet.", chat.WhyNotCompact());

        await chat.SendAsync("One", provider, "system", null);
        await chat.SendAsync("Two", provider, "system", null);

        Assert.True(chat.HistoryTokens(LlmConnection.Anthropic) >= AiChat.CompactAfterTokens);
        Assert.False(chat.WorthCompacting(LlmConnection.Anthropic));

        await chat.SendAsync("Three", provider, "system", null);

        Assert.True(chat.WorthCompacting(LlmConnection.Anthropic));

        chat.CompactSuggestionDismissed = true;

        Assert.False(chat.WorthCompacting(LlmConnection.Anthropic));

        await chat.CompactAsync(provider);

        Assert.False(chat.CompactSuggestionDismissed);
        Assert.Equal(0, chat.QuestionsSinceAfresh);
        Assert.False(chat.WorthCompacting(LlmConnection.Anthropic));
        Assert.Equal("Nothing has been asked since it was last compacted.", chat.WhyNotCompact());
    }

    ///<summary>A second compaction reads the first one's briefing in place of what it stood for, and carries its files on, unticked again.</summary>
    [Fact]
    public async Task A_second_compaction_reads_the_first_briefing_and_keeps_its_files()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Fine."), AiStandIn.AnthropicText("First briefing."), AiStandIn.AnthropicText("Sure."), AiStandIn.AnthropicText("Second briefing."));
        var chat = new AiChat();
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        chat.Attach(new AiAttachment("shot.png", "image/png", Png));

        await chat.SendAsync("Look at this", provider, "system", null);
        await chat.CompactAsync(provider);
        await chat.SendAsync("And now this", provider, "system", null);
        await chat.CompactAsync(provider);

        string second = standIn.Requests[3].Body.ToJsonString();

        Assert.Contains("SUMMARY LEFT BY AN EARLIER COMPACTION:", second);
        Assert.Contains("First briefing.", second);
        Assert.Contains("And now this", second);
        Assert.DoesNotContain("Look at this", second);

        Assert.Equal(5, chat.FoldedBefore);
        Assert.Equal("Second briefing.", chat.Turns[5].Text);

        var (onBriefing, file) = Assert.Single(chat.SentFiles());

        Assert.Same(chat.Turns[5], onBriefing);
        Assert.True(file.Excluded);
    }

    [Fact]
    public void A_compaction_is_priced_as_the_history_read_once_and_a_briefing_written()
    {
        var price = new LlmPrice { Input = 4, Cached = 0.2m, CacheWrite = 5, Output = 20 };

        //20,000 tokens of history and the instructions read at $4 a million, and 2,000 written at $20.
        decimal expected = ((20_000 + AiEstimate.TokensIn(AiChat.CompactSystem.Length)) * 4m + 2_000 * 20m) / 1_000_000m;

        Assert.Equal(expected, AiChat.CompactCost(price, 20_000));
        Assert.Null(AiChat.CompactCost(null, 20_000));
    }

    ///<summary>A tool switched off in the picker is not offered, and a model that calls it anyway is told why rather than obeyed.</summary>
    [Fact]
    public async Task A_tool_switched_off_is_not_offered_and_is_refused_if_called()
    {
        var session = Session();
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), AiStandIn.AnthropicText("It is switched off."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };

        chat.ToolsOff.Add("draw_rectangle");

        await chat.SendAsync("Draw it", new AnthropicProvider(standIn.Client(), Claude()), "system", new LayoutToolRunner(session) { Off = chat.ToolsOff });

        var offered = standIn.Requests[0].Body["tools"]!.AsArray().Select(tool => (string?)tool!["name"]).ToList();

        Assert.DoesNotContain("draw_rectangle", offered);
        Assert.Contains("draw_polygon", offered);
        Assert.Equal(0, ShapesIn(session));
        Assert.True(chat.Turns[1].ResultIsError);
        Assert.Contains("switched off in this conversation", chat.Turns[1].Result);
        Assert.DoesNotContain(LayoutToolRunner.ToolsFor(true, true, new HashSet<string> { LayoutToolRunner.LookName }), tool => tool.Name == LayoutToolRunner.LookName);
    }

    [Fact]
    public void Tools_switched_off_stay_off_through_clear_and_another_file()
    {
        var chat = new AiChat();

        chat.ToolsOff.Add(LayoutToolRunner.LookName);
        chat.Clear();
        chat.StartAfresh("Another file is open now.");

        Assert.Contains(LayoutToolRunner.LookName, chat.ToolsOff);
    }

    ///
    ///The instructions name the tools switched off, so a request that needs one is answered with which - and with
    ///every changing tool off, the conversation is said to only read, rather than the rung's "never answer that you
    ///cannot make changes", which would be telling it to call what it does not have.
    ///
    [Fact]
    public void The_prompt_names_the_tools_switched_off_and_a_conversation_that_only_reads()
    {
        var facts = new LayoutFacts { CanLook = true };
        string some = LayoutPrompt.Build(facts, ToolApprovalMode.AutoRead, new HashSet<string> { "look", "boolean" });

        Assert.Contains("switched these tools off in this conversation: boolean, look.", some);
        Assert.DoesNotContain("look draws a picture", some);
        Assert.Contains("Calling a tool that changes the layout is how you ask", some);

        var changing = AgentTools.All.Where(tool => tool.Edits).Select(tool => tool.Name).ToHashSet();
        string reading = LayoutPrompt.Build(facts, ToolApprovalMode.AutoRead, changing);

        Assert.Contains("this conversation only reads it", reading);
        Assert.DoesNotContain("Never answer that you cannot make changes", reading);
        Assert.Contains("look draws a picture", reading);
    }

    ///<summary>The export: every turn, a compaction's folded part included, each call and what it answered in full, what was billed, and the files by name alone.</summary>
    [Fact]
    public async Task The_transcript_has_every_turn_the_calls_in_full_and_the_files_by_name()
    {
        var session = Session();
        var standIn = new AiStandIn(
            AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicCall("toolu_1", "draw_rectangle", Rectangle), 1000, 50),
            AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicText("Drew #1."), 1200, 20),
            AiStandIn.AnthropicText("The briefing."));
        var chat = new AiChat { Mode = ToolApprovalMode.AutoReadWrite };
        var provider = new AnthropicProvider(standIn.Client(), Claude());

        chat.Attach(new AiAttachment("shot.png", "image/png", Png));

        await chat.SendAsync("Draw a 2 by 1 rectangle on met1", provider, "system", new LayoutToolRunner(session));
        await chat.CompactAsync(provider);

        string text = chat.Transcript("mosfet.gds", new DateTime(2026, 10, 29, 14, 5, 0));

        Assert.StartsWith("AI assistant conversation - mosfet.gds", text);
        Assert.Contains("Exported 2026-10-29 14:05", text);
        Assert.Contains("Draw a 2 by 1 rectangle on met1", text);
        Assert.Contains("  Attached: shot.png (11 bytes)", text);
        Assert.Contains("  Called draw_rectangle {", text);
        Assert.Contains(" - it answered:", text);
        Assert.Contains("    Drew a 2 by 1 µm rectangle on met1", text);
        Assert.Contains("Drew #1.", text);
        Assert.Contains("  Billed: 2 requests · 2,200 tokens in · 70 out", text);
        Assert.Contains(", on claude-opus-5-5", text);
        Assert.Contains("SUMMARY OF THE ABOVE (compacted)", text);
        Assert.Contains("  Kept from before it: shot.png (11 bytes, not sent)", text);
        Assert.DoesNotContain(Convert.ToBase64String(Png), text);
    }

    #endregion ********************************************************************
}
