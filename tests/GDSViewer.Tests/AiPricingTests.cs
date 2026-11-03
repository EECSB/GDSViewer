using System.Text.Json;
using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///What an answer cost: the tokens each provider reports, the rates a model runs at, and what the two come to.
///
///**A wrong price is worse than none**, so these hold the matching as tightly as the arithmetic: a dated snapshot priced
///as its model, a model never priced as another whose name it starts with, and no figure at all where nothing is known.
///
public class AiPricingTests
{
    private static readonly DateTime Today = new DateTime(2026, 10, 29);

    private static JsonElement Json(string text)
    {
        return JsonDocument.Parse(text).RootElement;
    }

    #region The published rates ***************************************************

    [Fact]
    public void A_model_is_priced_by_its_id_however_it_is_spelled()
    {
        var opus = AiPricing.Published("claude-opus-5-5", LlmConnection.Anthropic, Today)!;

        Assert.Equal(4.00m, opus.Input);
        Assert.Equal(0.20m, opus.Cached);
        Assert.Equal(20.00m, opus.Output);

        Assert.Equal(4.00m, AiPricing.Published("Claude-Opus-5-5", LlmConnection.Anthropic, Today)!.Input);
        Assert.Equal(4.00m, AiPricing.Published("claude-opus-5-5-20261001", LlmConnection.Anthropic, Today)!.Input);
        Assert.Equal(0.30m, AiPricing.Published("models/gemini-2.5-flash", LlmConnection.Gemini, Today)!.Input);
        Assert.Equal(2.00m, AiPricing.Published("gpt-5.6-terra-2026-08-01", LlmConnection.OpenAI, Today)!.Input);
    }

    ///<summary>Never by a prefix alone: gpt-5 is billed five times gpt-5-mini, and a model the table does not know has no price.</summary>
    [Fact]
    public void A_model_is_never_priced_as_another_its_name_starts_with()
    {
        Assert.Equal(1.25m, AiPricing.Published("gpt-5", LlmConnection.OpenAI, Today)!.Input);
        Assert.Equal(0.25m, AiPricing.Published("gpt-5-mini", LlmConnection.OpenAI, Today)!.Input);

        //A snapshot priced apart is found before its date is taken off.
        Assert.Equal(5.00m, AiPricing.Published("gpt-4o-2024-05-13", LlmConnection.OpenAI, Today)!.Input);

        Assert.Null(AiPricing.Published("gpt-5-turbo-imagined", LlmConnection.OpenAI, Today));
        Assert.Null(AiPricing.Published("llama3.3", LlmConnection.Custom, Today));
        Assert.Null(AiPricing.Published("", LlmConnection.OpenAI, Today));
    }

    ///<summary>A rise the provider announced applies from its day, and not before.</summary>
    [Fact]
    public void An_announced_change_starts_on_its_day()
    {
        Assert.Equal(0.75m, AiPricing.Published("gemini-3.8-flash", LlmConnection.Gemini, new DateTime(2026, 12, 31))!.Input);
        Assert.Equal(1.50m, AiPricing.Published("gemini-3.8-flash", LlmConnection.Gemini, new DateTime(2027, 1, 1))!.Input);
        Assert.Equal(7.50m, AiPricing.Published("gemini-3.8-flash", LlmConnection.Gemini, new DateTime(2027, 3, 1))!.Output);
    }

    ///<summary>Anthropic bills a write into its cache at a quarter over the input rate; nobody else bills one.</summary>
    [Fact]
    public void Only_anthropic_charges_for_a_cache_write()
    {
        Assert.Equal(5.00m, AiPricing.Published("claude-opus-5-5", LlmConnection.Anthropic, Today)!.CacheWrite);
        Assert.Equal(2.00m, AiPricing.Published("gpt-5.6-terra", LlmConnection.OpenAI, Today)!.CacheWrite);
    }

    #endregion ********************************************************************



    #region A saved model's own rates *********************************************

    [Fact]
    public void A_models_own_rates_win_and_the_published_fill_in()
    {
        var connection = new LlmConnection { ProviderType = LlmConnection.Anthropic, Model = "claude-sonnet-5-5", OutputPrice = 12m };

        var price = AiPricing.For(connection, Today)!;

        Assert.Equal(2.00m, price.Input);
        Assert.Equal(0.10m, price.Cached);
        Assert.Equal(12m, price.Output);
        Assert.Contains("own rates", price.Source);
    }

    ///<summary>Fast mode bills every token at twice its rate, and only on a model that has it.</summary>
    [Fact]
    public void Fast_mode_doubles_the_rates()
    {
        var opus = new LlmConnection { ProviderType = LlmConnection.Anthropic, Model = "claude-opus-5-5" };
        var plain = AiPricing.For(opus, Today)!;

        opus.Fast = true;

        var fast = AiPricing.For(opus, Today)!;

        Assert.Equal(plain.Input * 2, fast.Input);
        Assert.Equal(plain.Cached * 2, fast.Cached);
        Assert.Equal(plain.CacheWrite * 2, fast.CacheWrite);
        Assert.Equal(plain.Output * 2, fast.Output);
        Assert.Contains("fast mode", fast.Source);

        var sonnet = new LlmConnection { ProviderType = LlmConnection.Anthropic, Model = "claude-sonnet-5-5", Fast = true };

        Assert.Equal(AiPricing.Published("claude-sonnet-5-5", LlmConnection.Anthropic, Today)!.Input, AiPricing.For(sonnet, Today)!.Input);
    }

    [Fact]
    public void A_model_nobody_priced_shows_no_cost()
    {
        var unknown = new LlmConnection { ProviderType = LlmConnection.Custom, Model = "llama3.3" };

        Assert.Null(AiPricing.For(unknown, Today));

        unknown.InputPrice = 0.5m;

        Assert.Null(AiPricing.For(unknown, Today));

        unknown.OutputPrice = 1.5m;

        var price = AiPricing.For(unknown, Today)!;

        //No cached rate from anywhere: charged as input, more than a cache hit costs and never less.
        Assert.Equal(0.5m, price.Cached);
    }

    [Fact]
    public void An_anthropic_model_left_blank_is_priced_as_the_one_it_runs()
    {
        var blank = new LlmConnection { ProviderType = LlmConnection.Anthropic };

        Assert.Equal(AnthropicProvider.DefaultModel, AiPricing.ModelOf(blank));
        Assert.Equal(4.00m, AiPricing.For(blank, Today)!.Input);
    }

    [Fact]
    public void Its_rates_are_saved_with_it()
    {
        var saved = new Dictionary<string, LlmConnection> { ["Local"] = new LlmConnection { Name = "Local", InputPrice = 0.25m, OutputPrice = 1.5m } };

        var read = LlmConnectionStore.Read(LlmConnectionStore.Write(saved))["Local"];

        Assert.Equal(0.25m, read.InputPrice);
        Assert.Null(read.CachedPrice);
        Assert.Equal(1.5m, read.OutputPrice);
        Assert.Equal(1.5m, new LlmConnection(read).OutputPrice);
    }

    #endregion ********************************************************************



    #region What it comes to ******************************************************

    ///<summary>Cached and written tokens are parts of the input, priced at their own rates rather than at the fresh one on top.</summary>
    [Fact]
    public void Cached_and_written_tokens_are_priced_at_their_own_rates()
    {
        var opus = AiPricing.Published("claude-opus-5-5", LlmConnection.Anthropic, Today)!;
        var used = new LlmUsage { InputTokens = 1_000_000, CachedInputTokens = 600_000, CacheWriteTokens = 100_000, OutputTokens = 50_000, Requests = 3 };

        //300,000 fresh at $4, 600,000 cached at $0.20, 100,000 written at $5, 50,000 out at $20.
        Assert.Equal(1.20m + 0.12m + 0.50m + 1.00m, opus.Cost(used));
        Assert.Equal(0.004m, opus.CostOfInput(1000));
    }

    [Fact]
    public void Money_and_counts_read_as_the_window_shows_them()
    {
        Assert.Equal("$0", AiPricing.Money(0m));
        Assert.Equal("under $0.001", AiPricing.Money(0.0004m));
        Assert.Equal("$0.034", AiPricing.Money(0.0341m));
        Assert.Equal("$1.23", AiPricing.Money(1.234m));
        Assert.Equal("$1,234.50", AiPricing.Money(1234.5m));

        Assert.Equal("12,345", AiPricing.Count(12345));
        Assert.Equal("12,000", AiPricing.About(12345));
        Assert.Equal("840", AiPricing.About(840));
        Assert.Equal("1,200,000", AiPricing.About(1_234_567));
    }

    #endregion ********************************************************************



    #region What the providers report *********************************************

    ///<summary>Anthropic's input_tokens leaves its cache out, so everything read is the three together.</summary>
    [Fact]
    public void Anthropics_usage_counts_the_cache_into_the_input()
    {
        var used = LlmUsage.FromAnthropic(Json("""{"usage":{"input_tokens":200,"output_tokens":50,"cache_read_input_tokens":9000,"cache_creation_input_tokens":800}}"""))!;

        Assert.Equal(10_000, used.InputTokens);
        Assert.Equal(9000, used.CachedInputTokens);
        Assert.Equal(800, used.CacheWriteTokens);
        Assert.Equal(50, used.OutputTokens);
        Assert.Equal(1, used.Requests);
    }

    [Fact]
    public void Chat_completions_usage_carries_its_cached_and_reasoning_parts()
    {
        var used = LlmUsage.FromOpenAi(Json("""{"usage":{"prompt_tokens":5000,"completion_tokens":700,"total_tokens":5700,"prompt_tokens_details":{"cached_tokens":4096},"completion_tokens_details":{"reasoning_tokens":300}}}"""))!;

        Assert.Equal(5000, used.InputTokens);
        Assert.Equal(4096, used.CachedInputTokens);
        Assert.Equal(700, used.OutputTokens);
        Assert.Equal(300, used.ReasoningTokens);
    }

    ///<summary>What a total holds past the other two is thinking that went unsaid - Gemini's, through Google's compatible endpoint - and billed as output.</summary>
    [Fact]
    public void A_total_past_its_parts_is_counted_as_thinking()
    {
        var used = LlmUsage.FromOpenAi(Json("""{"usage":{"prompt_tokens":5000,"completion_tokens":100,"total_tokens":5900}}"""))!;

        Assert.Equal(900, used.OutputTokens);
        Assert.Equal(800, used.ReasoningTokens);
    }

    ///<summary>A reasoning count said beside a total past its parts is the same thinking said twice, and counts once.</summary>
    [Fact]
    public void Thinking_said_twice_is_counted_once()
    {
        var used = LlmUsage.FromOpenAi(Json("""{"usage":{"prompt_tokens":5000,"completion_tokens":100,"total_tokens":5900,"completion_tokens_details":{"reasoning_tokens":800}}}"""))!;

        Assert.Equal(900, used.OutputTokens);
        Assert.Equal(800, used.ReasoningTokens);
    }

    [Fact]
    public void An_answer_that_says_nothing_of_its_cost_counts_for_nothing()
    {
        Assert.Null(LlmUsage.FromAnthropic(Json("""{"content":[]}""")));
        Assert.Null(LlmUsage.FromOpenAi(Json("""{"choices":[]}""")));
        Assert.False(new LlmUsage().Reported);
    }

    [Fact]
    public void A_runs_share_is_what_was_added_since_it_began()
    {
        var spent = new LlmUsage { InputTokens = 100, OutputTokens = 10, Requests = 1 };
        var before = spent.Copy();

        spent.Add(new LlmUsage { InputTokens = 250, CachedInputTokens = 90, OutputTokens = 30, Requests = 2 });

        var run = spent.Since(before);

        Assert.Equal(250, run.InputTokens);
        Assert.Equal(90, run.CachedInputTokens);
        Assert.Equal(30, run.OutputTokens);
        Assert.Equal(2, run.Requests);
    }

    #endregion ********************************************************************
}
