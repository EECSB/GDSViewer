using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///The two providers: what they send, what they read back, and what they say when something goes wrong.
///
///Each request is checked as the JSON a provider would receive, and each reply is written in that provider's own
///shape - see AiStandIn - so nothing here needs a network or a key. The loop itself, with a real layout under it,
///is AiChatTests.
///
public class AiProviderTests
{
    private static LlmConnection Anthropic(string model = "")
    {
        return new LlmConnection { Name = "Claude", ProviderType = LlmConnection.Anthropic, ApiKey = "test-key", Model = model };
    }

    private static LlmConnection Gemini()
    {
        return new LlmConnection { Name = "Gemini", ProviderType = LlmConnection.Gemini, ApiKey = "test-key", Model = "gemini-test" };
    }

    private static IReadOnlyList<LlmMessage> Asked(string text)
    {
        return new List<LlmMessage> { LlmMessage.FromUser(text) };
    }

    #region Anthropic *************************************************************

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_anthropic_model_runs_the_default(string model)
    {
        Assert.Equal(AnthropicProvider.DefaultModel, AnthropicProvider.ResolveModel(Anthropic(model)));
    }

    [Fact]
    public void An_anthropic_model_that_was_chosen_is_kept()
    {
        Assert.Equal("claude-haiku-5-5", AnthropicProvider.ResolveModel(Anthropic("claude-haiku-5-5")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_cap_that_is_not_positive_falls_back_to_the_default(int cap)
    {
        var connection = Anthropic();

        connection.MaxTokens = cap;

        Assert.Equal(LlmConnection.DefaultMaxTokens, AnthropicProvider.ResolveMaxTokens(connection));
    }

    [Fact]
    public async Task Anthropic_is_sent_the_model_the_cap_the_system_prompt_and_the_conversation()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Hello."));
        var provider = new AnthropicProvider(standIn.Client(), Anthropic());

        var result = await provider.CompleteAsync("Be brief.", Asked("Hi"), CancellationToken.None);

        Assert.False(result.IsError, result.Error);
        Assert.Equal("Hello.", result.Text);

        var sent = Assert.Single(standIn.Requests);

        Assert.Equal(AnthropicProvider.Endpoint, sent.Uri);
        Assert.Equal(AnthropicProvider.DefaultModel, (string?)sent.Body["model"]);
        Assert.Equal(LlmConnection.DefaultMaxTokens, (int?)sent.Body["max_tokens"]);
        Assert.Equal("Be brief.", (string?)sent.Body["system"]);
        Assert.Equal("Hi", (string?)sent.Body["messages"]![0]!["content"]);
        Assert.Null(sent.Body["tools"]);
    }

    ///<summary>The headers a page has to send to call Anthropic itself: the key, the version, and the browser's own permission.</summary>
    [Fact]
    public async Task Anthropic_is_called_with_the_key_the_version_and_the_browser_header()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Hello."));

        await new AnthropicProvider(standIn.Client(), Anthropic()).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        var headers = standIn.Requests[0].Headers;

        Assert.Equal("test-key", headers["x-api-key"]);
        Assert.Equal("2023-06-01", headers["anthropic-version"]);
        Assert.Equal("true", headers["anthropic-dangerous-direct-browser-access"]);
        Assert.False(headers.ContainsKey("anthropic-beta"));
    }

    ///
    ///**An effort and fast mode go only when chosen** (2026-11-01): the effort in output_config, fast mode as the speed
    ///with its beta beside it - so a request that chose neither is the one it always was, above.
    ///
    [Fact]
    public async Task Anthropic_is_sent_an_effort_and_fast_mode_when_they_were_chosen()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Hello."));
        var connection = Anthropic("claude-opus-5-5");

        connection.Effort = "xhigh";
        connection.Fast = true;

        await new AnthropicProvider(standIn.Client(), connection).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        var sent = standIn.Requests[0];

        Assert.Equal("xhigh", (string?)sent.Body["output_config"]!["effort"]);
        Assert.Equal("fast", (string?)sent.Body["speed"]);
        Assert.Equal(AnthropicProvider.FastBeta, sent.Headers["anthropic-beta"]);
    }

    ///
    ///**What a model does not take is not sent**, since Anthropic refuses it rather than ignoring it: fast mode on a
    ///Sonnet, or a level from another model's list - which the chat drops on a change of model, and this drops again.
    ///
    [Fact]
    public async Task Anthropic_is_not_sent_what_the_model_does_not_take()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Hello."));
        var connection = Anthropic("claude-opus-4-6");

        connection.Effort = "xhigh";
        connection.Fast = true;

        await new AnthropicProvider(standIn.Client(), connection).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        var sent = standIn.Requests[0];

        Assert.Null(sent.Body["output_config"]);
        Assert.Null(sent.Body["speed"]);
        Assert.False(sent.Headers.ContainsKey("anthropic-beta"));
    }

    [Fact]
    public void Empty_turns_are_not_sent()
    {
        var conversation = new List<LlmMessage>
        {
            LlmMessage.FromUser("one"),
            LlmMessage.FromAssistant(""),
            LlmMessage.FromUser("  "),
            LlmMessage.FromUser("two")
        };

        Assert.Equal(2, AnthropicProvider.ToWireMessages(conversation).Count);
        Assert.Equal(3, OpenAiProvider.ToWireMessages("system", conversation).Count);
    }

    [Fact]
    public void The_layout_tools_go_to_anthropic_with_their_own_schemas()
    {
        var runner = new LayoutToolRunner(new AgentSession(GDS.NewLibrary(), new EditHistory())) { AllowEdits = true };
        var tools = AnthropicProvider.BuildToolsArray(runner.Tools);

        Assert.Equal(AgentTools.All.Count, tools.Count);

        foreach (var tool in AgentTools.All)
        {
            var sent = tools.Single(each => (string?)each!["name"] == tool.Name)!;

            Assert.Equal(tool.Description, (string?)sent["description"]);
            Assert.Equal(tool.InputSchema().ToJsonString(), sent["input_schema"]!.ToJsonString());
        }
    }

    [Fact]
    public void A_tool_that_answered_in_words_goes_back_as_text()
    {
        var result = AnthropicProvider.ToolResult("toolu_1", new LlmToolOutput { Text = "Drew #1." });

        Assert.Equal("tool_result", (string?)result["type"]);
        Assert.Equal("toolu_1", (string?)result["tool_use_id"]);
        Assert.Equal("Drew #1.", (string?)result["content"]);
        Assert.Null(result["is_error"]);
    }

    ///<summary>A picture goes back inside the result of the call that took it, as an image block beside the words.</summary>
    [Fact]
    public void A_picture_goes_back_to_anthropic_inside_the_tools_result()
    {
        var result = AnthropicProvider.ToolResult("toolu_2", new LlmToolOutput { Text = "A picture of TOP.", Png = "iVBORw0KGgo=" });
        var content = result["content"]!.AsArray();

        Assert.Equal("text", (string?)content[0]!["type"]);
        Assert.Equal("A picture of TOP.", (string?)content[0]!["text"]);
        Assert.Equal("image", (string?)content[1]!["type"]);
        Assert.Equal("base64", (string?)content[1]!["source"]!["type"]);
        Assert.Equal("image/png", (string?)content[1]!["source"]!["media_type"]);
        Assert.Equal("iVBORw0KGgo=", (string?)content[1]!["source"]!["data"]);
    }

    ///<summary>GraphDBViewer never set is_error; a model told a call was refused tries something else rather than building on it.</summary>
    [Fact]
    public void A_refused_call_is_marked_as_one()
    {
        var result = AnthropicProvider.ToolResult("toolu_3", new LlmToolOutput { Text = "There is no layer called mett1.", IsError = true });

        Assert.True((bool?)result["is_error"]);
    }

    [Fact]
    public void The_calls_in_a_reply_are_read_with_their_ids_and_arguments()
    {
        using var reply = JsonDocument.Parse(AiStandIn.AnthropicCall("toolu_9", "draw_rectangle", """{"layer":"met1","from":[0,0],"to":[1,1]}""", "Drawing it."));

        var call = Assert.Single(AnthropicProvider.ParseToolUses(reply.RootElement.GetProperty("content")));

        Assert.Equal("toolu_9", call.Id);
        Assert.Equal("draw_rectangle", call.Name);
        Assert.Equal("met1", JsonNode.Parse(call.ArgumentsJson)!["layer"]!.GetValue<string>());
    }

    [Fact]
    public void A_reply_in_words_has_no_calls()
    {
        using var reply = JsonDocument.Parse(AiStandIn.AnthropicText("Done."));

        Assert.Empty(AnthropicProvider.ParseToolUses(reply.RootElement.GetProperty("content")));
    }

    [Fact]
    public void Anthropics_error_is_what_is_said()
    {
        var result = AnthropicProvider.ParseResponse("""{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: too large"}}""");

        Assert.True(result.IsError);
        Assert.Equal("max_tokens: too large", result.Error);
    }

    [Fact]
    public void A_refusal_is_an_error()
    {
        var result = AnthropicProvider.ParseResponse("""{"content":[],"stop_reason":"refusal"}""");

        Assert.True(result.IsError);
        Assert.Contains("declined", result.Error);
    }

    [Fact]
    public void An_answer_cut_off_at_the_cap_says_so()
    {
        var result = AnthropicProvider.ParseResponse(AiStandIn.AnthropicText("The first half", "max_tokens"));

        Assert.False(result.IsError);
        Assert.StartsWith("The first half", result.Text);
        Assert.Contains("Cut off at the token limit", result.Text);
    }

    [Fact]
    public void An_empty_answer_is_an_error_rather_than_nothing()
    {
        Assert.True(AnthropicProvider.ParseResponse(AiStandIn.AnthropicText("")).IsError);
    }

    #endregion ********************************************************************



    #region The OpenAI-compatible adapter *****************************************

    [Theory]
    [InlineData(null, "https://api.openai.com/v1/chat/completions")]
    [InlineData("", "https://api.openai.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/v1/chat/completions")]
    public void The_endpoint_is_the_base_url_or_the_default(string? baseUrl, string expected)
    {
        Assert.Equal(expected, OpenAiProvider.ChatCompletionsUrl(baseUrl, OpenAiProvider.DefaultBaseUrl));
    }

    [Theory]
    [InlineData(LlmConnection.Gemini, OpenAiProvider.GeminiBaseUrl)]
    [InlineData(LlmConnection.OpenAI, OpenAiProvider.DefaultBaseUrl)]
    [InlineData(LlmConnection.Custom, null)]
    public void A_blank_address_is_the_providers_own_and_none_for_a_server_of_ones_own(string providerType, string? expected)
    {
        Assert.Equal(expected, OpenAiProvider.DefaultBaseUrlFor(providerType));
    }

    ///<summary>GraphDBViewer's bug of 2026-10-03: a blank address sent the key for somebody's own server to OpenAI.</summary>
    [Fact]
    public async Task A_server_of_ones_own_with_no_address_is_sent_nothing()
    {
        var standIn = new AiStandIn(AiStandIn.OpenAiText("never"));
        var connection = new LlmConnection { Name = "Mine", ProviderType = LlmConnection.Custom, Model = "llama", ApiKey = "test-key" };

        var result = await new OpenAiProvider(standIn.Client(), connection).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(OpenAiProvider.NoAddress, result.Error);
        Assert.Empty(standIn.Requests);
    }

    [Fact]
    public async Task A_model_with_no_key_is_sent_nothing()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("never"));
        var connection = Anthropic();

        connection.ApiKey = "";

        var result = await new AnthropicProvider(standIn.Client(), connection).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("no API key", result.Error);
        Assert.Empty(standIn.Requests);
    }

    ///<summary>The Gemini key goes to Google's OpenAI-compatible endpoint, as a bearer token, with the model asked for.</summary>
    [Fact]
    public async Task Gemini_goes_to_googles_openai_endpoint_with_the_key_as_a_bearer()
    {
        var standIn = new AiStandIn(AiStandIn.OpenAiText("Hello."));

        var result = await new OpenAiProvider(standIn.Client(), Gemini()).CompleteAsync("Be brief.", Asked("Hi"), CancellationToken.None);

        Assert.Equal("Hello.", result.Text);

        var sent = Assert.Single(standIn.Requests);

        Assert.Equal(OpenAiProvider.GeminiBaseUrl + "/chat/completions", sent.Uri);
        Assert.Equal("Bearer test-key", sent.Headers["Authorization"]);
        Assert.Equal("gemini-test", (string?)sent.Body["model"]);
        Assert.Equal("system", (string?)sent.Body["messages"]![0]!["role"]);
        Assert.Equal("Be brief.", (string?)sent.Body["messages"]![0]!["content"]);
        Assert.Null(sent.Body["reasoning_effort"]);
    }

    [Fact]
    public async Task An_effort_is_sent_only_when_one_was_chosen()
    {
        var standIn = new AiStandIn(AiStandIn.OpenAiText("Hello."));
        var connection = Gemini();

        connection.Effort = "high";

        await new OpenAiProvider(standIn.Client(), connection).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        Assert.Equal("high", (string?)standIn.Requests[0].Body["reasoning_effort"]);
    }

    [Fact]
    public void The_layout_tools_go_as_functions()
    {
        var runner = new LayoutToolRunner(new AgentSession(GDS.NewLibrary(), new EditHistory())) { AllowEdits = true };
        var tools = OpenAiProvider.BuildToolsArray(runner.Tools);
        var rectangle = tools.Single(each => (string?)each!["function"]!["name"] == "draw_rectangle")!;

        Assert.Equal("function", (string?)rectangle["type"]);
        Assert.Equal(AgentTools.Named("draw_rectangle")!.InputSchema().ToJsonString(), rectangle["function"]!["parameters"]!.ToJsonString());
    }

    [Theory]
    [InlineData("\"{\\\"layer\\\":\\\"met1\\\"}\"")]
    [InlineData("{\"layer\":\"met1\"}")]
    public void A_calls_arguments_are_read_as_a_string_of_json_or_as_json(string arguments)
    {
        string json = $$$"""{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"find_shapes","arguments":{{{arguments}}}}}]}""";

        using var message = JsonDocument.Parse(json);

        var call = Assert.Single(OpenAiProvider.ParseToolCalls(message.RootElement));

        Assert.Equal("call_1", call.Id);
        Assert.Equal("met1", JsonNode.Parse(call.ArgumentsJson)!["layer"]!.GetValue<string>());
    }

    ///<summary>Gemini 3 refuses the next request if a call's thought signature does not come back with it, so the calls go back as they came.</summary>
    [Fact]
    public void A_call_goes_back_with_whatever_the_server_put_in_it()
    {
        using var reply = JsonDocument.Parse(AiStandIn.OpenAiCall("call_7", "list_layers", "{}"));

        var message = reply.RootElement.GetProperty("choices")[0].GetProperty("message");
        var echo = OpenAiProvider.EchoAssistantMessage(message);

        Assert.Equal("assistant", (string?)echo["role"]);
        Assert.Equal("c2lnbmVk", (string?)echo["tool_calls"]![0]!["extra_content"]!["google"]!["thought_signature"]);

        //And nothing of the reply's own that a request does not take.
        Assert.False(echo.ContainsKey("refusal"));
    }

    [Fact]
    public void A_refused_call_says_so_in_its_tool_turn()
    {
        var turn = OpenAiProvider.ToolTurn("call_1", new LlmToolOutput { Text = "There is no layer called mett1.", IsError = true });

        Assert.Equal("tool", (string?)turn["role"]);
        Assert.Equal("call_1", (string?)turn["tool_call_id"]);
        Assert.Equal("Refused: There is no layer called mett1.", (string?)turn["content"]);
    }

    ///<summary>Chat completions take an image only from the user, so a picture follows the tool turns in a user turn of its own.</summary>
    [Fact]
    public void A_picture_goes_back_in_a_user_turn_after_the_calls()
    {
        var turn = OpenAiProvider.PictureTurn(new List<(string, string)> { ("look", "iVBORw0KGgo=") });
        var parts = turn["content"]!.AsArray();

        Assert.Equal("user", (string?)turn["role"]);
        Assert.Contains("look", (string?)parts[0]!["text"]);
        Assert.Equal("image_url", (string?)parts[1]!["type"]);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", (string?)parts[1]!["image_url"]!["url"]);
    }

    [Fact]
    public void An_error_google_wraps_in_an_array_is_read_through()
    {
        var result = OpenAiProvider.ParseResponse("""[{"error":{"code":400,"message":"API key not valid.","status":"INVALID_ARGUMENT"}}]""");

        Assert.True(result.IsError);
        Assert.Equal("API key not valid.", result.Error);
    }

    [Fact]
    public void The_factory_picks_the_adapter_by_provider()
    {
        using var http = new HttpClient();

        Assert.IsType<AnthropicProvider>(LlmProviderFactory.Create(http, Anthropic()));
        Assert.IsType<OpenAiProvider>(LlmProviderFactory.Create(http, Gemini()));
    }

    #endregion ********************************************************************



    #region When it goes wrong ****************************************************

    ///<summary>The status is read as well as the body: a refusal says which, and why, in the provider's own words.</summary>
    [Fact]
    public async Task A_refused_key_is_said_with_its_status_and_the_providers_words()
    {
        var standIn = new AiStandIn((_, _) => AiStandIn.Json("""{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""", HttpStatusCode.Unauthorized));

        var result = await new AnthropicProvider(standIn.Client(), Anthropic()).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Anthropic answered 401 Unauthorized: invalid x-api-key", result.Error);
    }

    ///<summary>GraphDBViewer said "Could not parse model response" for a gateway's error page, which names the wrong problem.</summary>
    [Fact]
    public async Task A_failure_with_no_readable_body_is_said_by_its_status()
    {
        var standIn = new AiStandIn((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>Bad gateway</html>") });

        var result = await new OpenAiProvider(standIn.Client(), Gemini()).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("Gemini answered 502 Bad Gateway.", result.Error);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_named_with_what_to_check()
    {
        var standIn = new AiStandIn((_, _) => throw new HttpRequestException("TypeError: Failed to fetch"));
        var connection = new LlmConnection { Name = "Mine", ProviderType = LlmConnection.Custom, BaseUrl = "http://localhost:11434/v1", Model = "llama" };

        var result = await new OpenAiProvider(standIn.Client(), connection).CompleteAsync("", Asked("Hi"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Could not reach http://localhost:11434", result.Error);
        Assert.Contains("CORS", result.Error);
    }

    [Fact]
    public async Task A_stop_is_let_through_rather_than_said_as_a_failure()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("never"));
        using var stop = new CancellationTokenSource();

        stop.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AnthropicProvider(standIn.Client(), Anthropic()).CompleteAsync("", Asked("Hi"), stop.Token));
    }

    [Fact]
    public void The_providers_have_no_timeout_of_their_own()
    {
        using var http = LlmProviderFactory.NewHttpClient();

        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
    }

    #endregion ********************************************************************



    #region The connection ********************************************************

    [Theory]
    [InlineData(LlmConnection.OpenAI, "gpt-5-mini", true)]
    [InlineData(LlmConnection.OpenAI, "o3", true)]
    [InlineData(LlmConnection.OpenAI, "gpt-4o-mini", false)]
    [InlineData(LlmConnection.Gemini, "gemini-2.5-flash", true)]
    [InlineData(LlmConnection.Gemini, "gemini-3.6-flash", true)]
    [InlineData(LlmConnection.Gemini, "gemini-2.0-flash", false)]
    [InlineData(LlmConnection.Anthropic, "claude-opus-5-5", true)]
    [InlineData(LlmConnection.Anthropic, "claude-haiku-4-5", false)]
    [InlineData(LlmConnection.Anthropic, "claude-sonnet-4-5", false)]
    [InlineData(LlmConnection.Custom, "o3", false)]
    public void An_effort_is_offered_only_where_it_is_understood(string provider, string model, bool expected)
    {
        Assert.Equal(expected, LlmConnection.SupportsEffort(new LlmConnection { ProviderType = provider, Model = model }));
    }

    [Theory]
    [InlineData(LlmConnection.OpenAI, "gpt-5-mini", "medium")]
    [InlineData(LlmConnection.OpenAI, "o3", "medium")]
    [InlineData(LlmConnection.OpenAI, "gpt-5.1", "none")]
    [InlineData(LlmConnection.OpenAI, "gpt-5.2-codex", "none")]
    [InlineData(LlmConnection.Gemini, "gemini-3.8-flash", "high")]
    [InlineData(LlmConnection.Gemini, "gemini-3-pro", "high")]
    [InlineData(LlmConnection.Gemini, "gemini-2.5-flash", "as needed")]
    [InlineData(LlmConnection.Gemini, "gemini-2.5-flash-lite", "none")]
    [InlineData(LlmConnection.OpenAI, "gpt-4o-mini", "")]
    [InlineData(LlmConnection.Anthropic, "claude-opus-5-5", "medium")]
    [InlineData(LlmConnection.Anthropic, "claude-haiku-5-5", "medium")]
    [InlineData(LlmConnection.Anthropic, "claude-opus-5", "high")]
    [InlineData(LlmConnection.Anthropic, "claude-sonnet-5-5", "high")]
    [InlineData(LlmConnection.Anthropic, "claude-haiku-4-5", "")]
    public void What_default_means_is_named_by_the_models_family(string provider, string model, string expected)
    {
        Assert.Equal(expected, LlmConnection.DefaultEffort(new LlmConnection { ProviderType = provider, Model = model }));
    }

    ///
    ///**Claude's levels by version** (2026-11-01): three from Opus 4.5, max with Opus and Sonnet 4.6, xhigh with Opus 4.7
    ///and every model since - a dated id read by its version, not its date.
    ///
    [Theory]
    [InlineData("claude-opus-4-5-20251101", "low medium high")]
    [InlineData("claude-opus-4-6", "low medium high max")]
    [InlineData("claude-sonnet-4-6", "low medium high max")]
    [InlineData("claude-opus-4-7", "low medium high xhigh max")]
    [InlineData("claude-opus-5-5", "low medium high xhigh max")]
    [InlineData("claude-sonnet-5", "low medium high xhigh max")]
    [InlineData("claude-fable-5-1", "low medium high xhigh max")]
    [InlineData("claude-haiku-5-5", "low medium high xhigh max")]
    [InlineData("claude-sonnet-4-20250514", "")]
    [InlineData("not-a-claude", "")]
    public void Claude_is_offered_the_levels_its_version_takes(string model, string expected)
    {
        var levels = LlmConnection.EffortLevels(new LlmConnection { ProviderType = LlmConnection.Anthropic, Model = model });

        Assert.Equal(expected, string.Join(" ", levels));
    }

    ///<summary>Fast mode is Opus 4.8 and Opus 5 on, and Anthropic's alone - Opus 4.7's was taken away.</summary>
    [Theory]
    [InlineData(LlmConnection.Anthropic, "claude-opus-5-5", true)]
    [InlineData(LlmConnection.Anthropic, "claude-opus-5", true)]
    [InlineData(LlmConnection.Anthropic, "claude-opus-4-8", true)]
    [InlineData(LlmConnection.Anthropic, "claude-opus-4-7", false)]
    [InlineData(LlmConnection.Anthropic, "claude-sonnet-5-5", false)]
    [InlineData(LlmConnection.Anthropic, "claude-fable-5-1", false)]
    [InlineData(LlmConnection.Custom, "claude-opus-5-5", false)]
    public void Fast_mode_is_offered_only_on_the_models_that_have_it(string provider, string model, bool expected)
    {
        Assert.Equal(expected, LlmConnection.SupportsFast(new LlmConnection { ProviderType = provider, Model = model }));
    }

    [Theory]
    [InlineData(LlmConnection.Anthropic, null, true)]
    [InlineData(LlmConnection.Gemini, null, true)]
    [InlineData(LlmConnection.OpenAI, "https://example.com/v1", true)]
    [InlineData(LlmConnection.Custom, "http://localhost:11434/v1", false)]
    [InlineData(LlmConnection.Custom, null, false)]
    public void Whether_a_connection_is_encrypted_is_read_off_its_address(string provider, string? baseUrl, bool expected)
    {
        Assert.Equal(expected, LlmConnection.Encrypts(new LlmConnection { ProviderType = provider, BaseUrl = baseUrl }));
    }

    #endregion ********************************************************************



    #region Files with a question, and what a request costs ************************

    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    private static AiAttachment PdfFile()
    {
        return new AiAttachment("rules.pdf", "application/pdf", System.Text.Encoding.Latin1.GetBytes("%PDF-1.7\n<< /Type /Page >>\n%%EOF"));
    }

    ///
    ///A system prompt with nothing in it is no prompt: Anthropic's body carries no system field and the chat-completions
    ///array leads with no system turn, rather than a null or an empty one the APIs refuse (2026-11-07, from GraphDBViewer).
    ///
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_system_prompt_with_nothing_in_it_is_left_out(string? system)
    {
        var body = JsonNode.Parse(AnthropicProvider.BuildRequestBody("claude-haiku-4-5", 512, system!, AnthropicProvider.ToWireMessages(Asked("Hi")), null))!.AsObject();

        Assert.False(body.ContainsKey("system"));

        var messages = OpenAiProvider.ToWireMessages(system!, Asked("Hi"));

        Assert.Equal("user", (string?)Assert.Single(messages)!.AsObject()["role"]);

        var withPrompt = JsonNode.Parse(AnthropicProvider.BuildRequestBody("claude-haiku-4-5", 512, "Be brief.", AnthropicProvider.ToWireMessages(Asked("Hi")), null))!.AsObject();

        Assert.Equal("Be brief.", (string?)withPrompt["system"]);
        Assert.Equal(2, OpenAiProvider.ToWireMessages("Be brief.", Asked("Hi")).Count);
    }

    ///<summary>A runner of one tool that looks, for a loop to have something to call.</summary>
    private sealed class OneTool : ILlmToolRunner
    {
        public IReadOnlyList<LlmTool> Tools { get; } = new[]
        {
            new LlmTool { Name = "list_layers", Description = "The layers.", InputSchemaJson = """{"type":"object","properties":{}}""" },
            new LlmTool { Name = "measure", Description = "A size.", InputSchemaJson = """{"type":"object","properties":{}}""" }
        };

        public Task<LlmToolOutput> RunToolAsync(string name, string argumentsJson, CancellationToken ct)
        {
            return Task.FromResult(new LlmToolOutput { Text = "met1 (68/20)" });
        }
    }

    ///<summary>A question with files is blocks, the files ahead of its words; one without stays the string it always was.</summary>
    [Fact]
    public void Anthropic_is_sent_a_questions_files_ahead_of_its_words()
    {
        var conversation = new List<LlmMessage>
        {
            LlmMessage.FromUser("What is this?", new[] { new AiAttachment("shot.png", "image/png", Png), PdfFile(), new AiAttachment("deck.drc", "", "width met1 0.14"u8.ToArray()) }),
            LlmMessage.FromAssistant("A transistor.")
        };

        var messages = AnthropicProvider.ToWireMessages(conversation);
        var blocks = messages[0]!["content"]!.AsArray();

        Assert.Equal(new[] { "text", "image", "text", "document", "text", "text" }, blocks.Select(block => (string?)block!["type"]));
        Assert.Equal("Attached file: shot.png", (string?)blocks[0]!["text"]);
        Assert.Equal("image/png", (string?)blocks[1]!["source"]!["media_type"]);
        Assert.Equal(Convert.ToBase64String(Png), (string?)blocks[1]!["source"]!["data"]);
        Assert.Equal("application/pdf", (string?)blocks[3]!["source"]!["media_type"]);
        Assert.StartsWith("<attachment name=\"deck.drc\">", (string?)blocks[4]!["text"]);
        Assert.Equal("What is this?", (string?)blocks[5]!["text"]);

        Assert.Equal("A transistor.", (string?)messages[1]!["content"]);
    }

    ///<summary>A picture as a data URL and a PDF as a file part to OpenAI; to Gemini the PDF is a line saying it could not go.</summary>
    [Fact]
    public void Chat_completions_are_sent_files_as_parts_the_provider_reads()
    {
        var question = LlmMessage.FromUser("What is this?", new[] { new AiAttachment("shot.png", "image/png", Png), PdfFile() });

        var toOpenAi = OpenAiProvider.ToWireMessages("system", new[] { question }, LlmConnection.OpenAI)[1]!["content"]!.AsArray();

        Assert.Equal(new[] { "text", "image_url", "text", "file", "text" }, toOpenAi.Select(part => (string?)part!["type"]));
        Assert.StartsWith("data:image/png;base64,", (string?)toOpenAi[1]!["image_url"]!["url"]);
        Assert.Equal("rules.pdf", (string?)toOpenAi[3]!["file"]!["filename"]);
        Assert.StartsWith("data:application/pdf;base64,", (string?)toOpenAi[3]!["file"]!["file_data"]);

        var toGemini = OpenAiProvider.ToWireMessages("system", new[] { question }, LlmConnection.Gemini)[1]!["content"]!.AsArray();

        Assert.Equal(new[] { "text", "image_url", "text", "text" }, toGemini.Select(part => (string?)part!["type"]));
        Assert.Contains("rules.pdf, which this model cannot be sent", (string?)toGemini[2]!["text"]);
    }

    ///
    ///**Two marks for Anthropic's cache in every round, never more**: after the tools, and on the last block of the
    ///conversation - made a block where it was a string. Put on the copy each request is built from, so they do not pile up
    ///on the conversation the loop keeps adding to: three rounds would pass Anthropic's limit of four.
    ///
    [Fact]
    public async Task Each_round_of_tool_calls_marks_the_tools_and_its_last_block_for_the_cache()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicCall("toolu_1", "list_layers", "{}"), AiStandIn.AnthropicCall("toolu_2", "measure", "{}"), AiStandIn.AnthropicText("Done."));

        var result = await new AnthropicProvider(standIn.Client(), Anthropic()).CompleteWithToolsAsync("system", Asked("Look"), new OneTool(), CancellationToken.None);

        Assert.False(result.IsError, result.Error);
        Assert.Equal(3, standIn.Requests.Count);

        foreach (var sent in standIn.Requests)
        {
            string body = sent.Body.ToJsonString();

            Assert.Equal(2, body.Split("cache_control").Length - 1);
            Assert.NotNull(sent.Body["tools"]!.AsArray()[^1]!["cache_control"]);
            Assert.NotNull(sent.Body["messages"]!.AsArray()[^1]!["content"]!.AsArray()[^1]!["cache_control"]);
        }

        //The first round's question, a string until then, went as a block to carry the mark.
        Assert.Equal("Look", (string?)standIn.Requests[0].Body["messages"]![0]!["content"]![0]!["text"]);

        //And the system prompt stays a string: it is written again for every question, so a mark on it would buy nothing.
        Assert.Equal("system", (string?)standIn.Requests[0].Body["system"]);
    }

    ///<summary>A request answered in one round would only pay the write premium, so a plain completion is not marked.</summary>
    [Fact]
    public async Task A_plain_completion_is_not_marked_for_the_cache()
    {
        var standIn = new AiStandIn(AiStandIn.AnthropicText("Hello."));

        await new AnthropicProvider(standIn.Client(), Anthropic()).CompleteAsync("system", Asked("Hi"), CancellationToken.None);

        Assert.DoesNotContain("cache_control", standIn.Requests[0].Body.ToJsonString());
    }

    ///<summary>What every round was billed for is added up as it arrives - which is what lets a stopped run still be counted.</summary>
    [Fact]
    public async Task What_each_round_cost_is_added_to_the_providers_spend()
    {
        var standIn = new AiStandIn(
            AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicCall("toolu_1", "list_layers", "{}"), 1200, 40, 0, 9000),
            AiStandIn.WithAnthropicUsage(AiStandIn.AnthropicText("Done."), 300, 60, 9000, 1100));
        var provider = new AnthropicProvider(standIn.Client(), Anthropic("claude-sonnet-5-5"));

        await provider.CompleteWithToolsAsync("system", Asked("Look"), new OneTool(), CancellationToken.None);

        Assert.Equal("claude-sonnet-5-5", provider.Model);
        Assert.Equal(2, provider.Spent.Requests);
        Assert.Equal(10_200 + 10_400, provider.Spent.InputTokens);
        Assert.Equal(9000, provider.Spent.CachedInputTokens);
        Assert.Equal(10_100, provider.Spent.CacheWriteTokens);
        Assert.Equal(100, provider.Spent.OutputTokens);
        Assert.Equal(2.00m, provider.Price!.Input);
    }

    [Fact]
    public async Task A_chat_completion_says_what_it_cost_too()
    {
        var standIn = new AiStandIn(AiStandIn.WithOpenAiUsage(AiStandIn.OpenAiText("Hello."), 5000, 100, cached: 4096, total: 5900));
        var provider = new OpenAiProvider(standIn.Client(), Gemini());

        await provider.CompleteAsync("system", Asked("Hi"), CancellationToken.None);

        Assert.Equal(5000, provider.Spent.InputTokens);
        Assert.Equal(4096, provider.Spent.CachedInputTokens);
        Assert.Equal(900, provider.Spent.OutputTokens);

        //gemini-test is no model anybody priced.
        Assert.Null(provider.Price);
    }

    #endregion ********************************************************************
}
