using System.Globalization;
using System.Text.RegularExpressions;

namespace GDSViewer.Ai
{
    ///
    ///What a model's tokens cost, in US dollars per million: read, read back from a cache, written into one, and
    ///written out.
    ///
    public sealed class LlmPrice
    {
        public required decimal Input { get; init; }

        public required decimal Cached { get; init; }

        public required decimal CacheWrite { get; init; }

        public required decimal Output { get; init; }

        ///<summary>Where the rates came from, for the line that shows them: the published table and the day it was read, or the model's own under Settings.</summary>
        public string Source { get; init; } = "";

        ///
        ///What a run cost.
        ///
        ///**Cached and written tokens are taken out of the input before it is priced**, and priced at their own rates -
        ///they are parts of it, not additions, and charging all of it at the fresh rate over-reported every cache hit,
        ///which is the fix ConfigurationRuleEditor's CostOf records.
        ///
        public decimal Cost(LlmUsage usage)
        {
            long fresh = Math.Max(0, usage.InputTokens - usage.CachedInputTokens - usage.CacheWriteTokens);

            decimal dollars = fresh * Input + usage.CachedInputTokens * Cached + usage.CacheWriteTokens * CacheWrite + usage.OutputTokens * Output;

            return dollars / 1_000_000m;
        }

        ///<summary>What reading this many fresh tokens costs - an estimate's input, before anything is cached.</summary>
        public decimal CostOfInput(long tokens)
        {
            return tokens * Input / 1_000_000m;
        }
    }

    ///
    ///The published rates, and the rates a saved model runs at.
    ///
    ///**Published, dated, and second to the person's own.** A saved model can carry its own rates under Settings, AI
    ///settings, which win; without them the table below is used, read from each provider's own pricing page on the day
    ///it says. Rates move, and the table only stands in until somebody looks. **A model with no rate shows no cost** -
    ///its tokens are still shown - because a guessed price read as fact is worse than an empty space, the rule
    ///ConfigurationRuleEditor's ChatClientFactory keeps.
    ///
    public static class AiPricing
    {
        ///<summary>The day the table was read from the three pages <see cref="PricingPage"/> links to.</summary>
        public const string ReadOn = "2026-10-29";

        ///<summary>Anthropic bills a token written into its cache for five minutes at a quarter over the input rate. Nobody else bills a write.</summary>
        public const decimal AnthropicWriteFactor = 1.25m;

        ///<summary>What fast mode bills each token at against the model's own rate - see <see cref="LlmConnection.SupportsFast"/>.</summary>
        public const decimal FastMultiplier = 2m;

        ///
        ///One row of a published table: a model's input, cached input and output rates, and the day they start when the
        ///page announced a change - the row without one is what applies until then.
        ///
        private sealed record Rate(string Model, decimal Input, decimal Cached, decimal Output, string From = "");

        ///
        ///US dollars per million tokens, as the providers published them on <see cref="ReadOn"/>: Anthropic's at
        ///platform.claude.com/docs/en/about-claude/pricing, OpenAI's at developers.openai.com/api/docs/pricing and
        ///Google's at ai.google.dev/gemini-api/docs/pricing (updated 2026-10-28).
        ///
        ///**The standard, short-prompt rates.** Claude Haiku 5.5's hold for prompts up to 100,000 tokens and are five
        ///times that past it; OpenAI's to 272,000 tokens; Gemini 3.1 Pro's and 2.5 Pro's to 200,000. A prompt past those is
        ///priced low here. Google's cached rate is its context-caching rate, which is what an implicit cache hit is billed
        ///at. GPT-5.6 Sol's is a promotion OpenAI promised until at least 2026-11-21, and said nothing after; Gemini 4 Argon's
        ///and the 3.6 and 3.8 Flash models' are the rises Google announced, from the day each starts.
        ///
        private static readonly Rate[] published =
        {
            new Rate("claude-fable-5-1", 10.00m, 0.25m, 50.00m),
            new Rate("claude-fable-5", 10.00m, 1.00m, 50.00m),
            new Rate("claude-opus-5-5", 4.00m, 0.20m, 20.00m),
            new Rate("claude-opus-5", 5.00m, 0.50m, 25.00m),
            new Rate("claude-opus-4-8", 5.00m, 0.50m, 25.00m),
            new Rate("claude-opus-4-7", 5.00m, 0.50m, 25.00m),
            new Rate("claude-opus-4-6", 5.00m, 0.50m, 25.00m),
            new Rate("claude-opus-4-5", 5.00m, 0.50m, 25.00m),
            new Rate("claude-sonnet-5-5", 2.00m, 0.10m, 10.00m),
            new Rate("claude-sonnet-5", 2.00m, 0.20m, 10.00m),
            new Rate("claude-sonnet-4-6", 3.00m, 0.30m, 15.00m),
            new Rate("claude-sonnet-4-5", 3.00m, 0.30m, 15.00m),
            new Rate("claude-haiku-5-5", 0.10m, 0.01m, 0.50m),
            new Rate("claude-haiku-4-5", 1.00m, 0.10m, 5.00m),

            new Rate("gpt-6-astra", 10.00m, 1.00m, 50.00m),
            new Rate("gpt-6.1-sol", 2.00m, 0.10m, 10.00m),
            new Rate("gpt-6.1-terra", 1.00m, 0.05m, 5.00m),
            new Rate("gpt-6-sol", 2.00m, 0.20m, 10.00m),
            new Rate("gpt-6-luna", 0.10m, 0.01m, 0.50m),
            new Rate("gpt-5.6-sol", 4.00m, 0.40m, 20.00m),
            new Rate("gpt-5.6-terra", 2.00m, 0.20m, 12.00m),
            new Rate("gpt-5.6-luna", 0.20m, 0.02m, 1.20m),
            new Rate("gpt-5.5", 5.00m, 0.50m, 30.00m),
            new Rate("gpt-5.4", 2.50m, 0.25m, 15.00m),
            new Rate("gpt-5.4-mini", 0.75m, 0.075m, 4.50m),
            new Rate("gpt-5.4-nano", 0.20m, 0.02m, 1.25m),
            new Rate("gpt-5.2", 1.75m, 0.175m, 14.00m),
            new Rate("gpt-5.1", 1.25m, 0.125m, 10.00m),
            new Rate("gpt-5", 1.25m, 0.125m, 10.00m),
            new Rate("gpt-5-mini", 0.25m, 0.025m, 2.00m),
            new Rate("gpt-5-nano", 0.05m, 0.005m, 0.40m),
            new Rate("gpt-4.1", 2.00m, 0.50m, 8.00m),
            new Rate("gpt-4.1-mini", 0.40m, 0.10m, 1.60m),
            new Rate("gpt-4.1-nano", 0.10m, 0.025m, 0.40m),
            new Rate("gpt-4o", 2.50m, 1.25m, 10.00m),
            new Rate("gpt-4o-2024-05-13", 5.00m, 5.00m, 15.00m),
            new Rate("gpt-4o-mini", 0.15m, 0.075m, 0.60m),
            new Rate("o3", 2.00m, 0.50m, 8.00m),
            new Rate("o4-mini", 1.10m, 0.275m, 4.40m),

            new Rate("gemini-4-argon", 2.00m, 0.10m, 10.00m),
            new Rate("gemini-4-argon", 4.00m, 0.20m, 20.00m, "2026-12-01"),
            new Rate("gemini-3.8-flash", 0.75m, 0.075m, 3.75m),
            new Rate("gemini-3.8-flash", 1.50m, 0.15m, 7.50m, "2027-01-01"),
            new Rate("gemini-3.6-flash", 0.75m, 0.075m, 3.75m),
            new Rate("gemini-3.6-flash", 1.50m, 0.15m, 7.50m, "2027-01-01"),
            new Rate("gemini-3.5-flash-lite", 0.30m, 0.03m, 2.50m),
            new Rate("gemini-3.1-flash-lite", 0.25m, 0.025m, 1.50m),
            new Rate("gemini-3.1-pro-preview", 2.00m, 0.20m, 12.00m),
            new Rate("gemini-3-flash-preview", 0.50m, 0.05m, 3.00m),
            new Rate("gemini-2.5-pro", 1.25m, 0.125m, 10.00m),
            new Rate("gemini-2.5-flash", 0.30m, 0.03m, 2.50m),
            new Rate("gemini-2.5-flash-lite", 0.10m, 0.01m, 0.40m)
        };

        ///<summary>A date at the end of an id - Anthropic's -20261001, OpenAI's -2026-10-01 - which names a snapshot of the model the rest names.</summary>
        private static readonly Regex dated = new Regex(@"-(\d{8}|\d{4}-\d{2}-\d{2})$", RegexOptions.CultureInvariant);

        ///<summary>Where each provider publishes its rates, so a figure here can be checked against the source rather than taken on trust.</summary>
        public static string? PricingPage(string providerType)
        {
            if (providerType == LlmConnection.Anthropic)
                return "https://platform.claude.com/docs/en/about-claude/pricing";

            if (providerType == LlmConnection.OpenAI)
                return "https://developers.openai.com/api/docs/pricing";

            if (providerType == LlmConnection.Gemini)
                return "https://ai.google.dev/gemini-api/docs/pricing";

            return null;
        }

        ///<summary>The model a saved model asks for: its own id, or Anthropic's default for an Anthropic one that names none.</summary>
        public static string ModelOf(LlmConnection connection)
        {
            if (connection.ProviderType == LlmConnection.Anthropic)
                return AnthropicProvider.ResolveModel(connection);

            return (connection.Model ?? "").Trim();
        }

        ///
        ///The published rates for a model on a day, or null when the table has none.
        ///
        ///Matched on the id as the provider spells it, ignoring case and Google's "models/" in front - and, failing that,
        ///without a date at its end, so a dated snapshot is priced as its model. A snapshot priced apart has a row of its
        ///own, which is found first: gpt-4o-2024-05-13 is twice gpt-4o. Never by a prefix alone: gpt-5 is not gpt-5-mini,
        ///and is billed five times what it is.
        ///
        public static LlmPrice? Published(string? model, string providerType, DateTime today)
        {
            string id = (model ?? "").Trim().ToLowerInvariant();

            if (id.StartsWith("models/", StringComparison.Ordinal))
                id = id.Substring("models/".Length);

            if (id.Length == 0)
                return null;

            var rate = rateOn(id, today);

            if (rate is null && dated.IsMatch(id))
                rate = rateOn(dated.Replace(id, ""), today);

            if (rate is null)
                return null;

            return new LlmPrice
            {
                Input = rate.Input,
                Cached = rate.Cached,
                CacheWrite = writeRate(providerType, rate.Input),
                Output = rate.Output,
                Source = $"published rates, read {ReadOn}"
            };
        }

        ///
        ///The rates a saved model runs at: its own where it has them, the published ones for the rest, or null when either
        ///the input or the output rate is known to neither.
        ///
        ///A cached rate it has none for, from either, is charged as input - more than a cache hit costs, never less.
        ///
        public static LlmPrice? For(LlmConnection connection, DateTime today)
        {
            var published = Published(ModelOf(connection), connection.ProviderType, today);

            decimal? input = connection.InputPrice ?? published?.Input;
            decimal? output = connection.OutputPrice ?? published?.Output;

            if (input is null || output is null)
                return null;

            decimal cached = connection.CachedPrice ?? published?.Cached ?? input.Value;
            bool own = connection.InputPrice is not null || connection.OutputPrice is not null || connection.CachedPrice is not null;
            string source = published?.Source ?? "";

            if (own)
                source = "this model's own rates, under Settings, AI settings";

            //Fast mode bills every token at twice its rate, cached and written ones too - the provider's documents, 2026-11-01.
            decimal times = 1m;

            if (connection.Fast && LlmConnection.SupportsFast(connection))
            {
                times = FastMultiplier;
                source += ", doubled for fast mode";
            }

            return new LlmPrice
            {
                Input = input.Value * times,
                Cached = cached * times,
                CacheWrite = writeRate(connection.ProviderType, input.Value) * times,
                Output = output.Value * times,
                Source = source
            };
        }

        ///<summary>Dollars as the window shows them: three places under a dollar, where most answers cost, and two above.</summary>
        public static string Money(decimal dollars)
        {
            if (dollars <= 0m)
                return "$0";

            if (dollars < 0.001m)
                return "under $0.001";

            if (dollars < 1m)
                return "$" + dollars.ToString("0.000", CultureInfo.InvariantCulture);

            return "$" + dollars.ToString("#,0.00", CultureInfo.InvariantCulture);
        }

        ///<summary>A count of tokens as it was reported: 12,345.</summary>
        public static string Count(long tokens)
        {
            return tokens.ToString("N0", CultureInfo.InvariantCulture);
        }

        ///
        ///An estimated count rounded to what it can claim: two figures, so 12,345 reads 12,000 and 840 reads 840. An
        ///estimate printed to the token reads as a measurement.
        ///
        public static string About(long tokens)
        {
            if (tokens < 1000)
                return Count(tokens);

            double scale = Math.Pow(10, Math.Floor(Math.Log10(tokens)) - 1);
            long rounded = (long)(Math.Round(tokens / scale) * scale);

            return Count(rounded);
        }

        private static decimal writeRate(string providerType, decimal input)
        {
            if (providerType == LlmConnection.Anthropic)
                return input * AnthropicWriteFactor;

            return input;
        }

        ///<summary>The row for an id on a day: of its rows, the one that started last on or before it.</summary>
        private static Rate? rateOn(string id, DateTime today)
        {
            string day = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Rate? found = null;

            foreach (var rate in published)
            {
                if (rate.Model != id)
                    continue;

                if (string.CompareOrdinal(rate.From, day) > 0)
                    continue;

                if (found is null || string.CompareOrdinal(rate.From, found.From) > 0)
                    found = rate;
            }

            return found;
        }
    }

    ///
    ///About how many tokens a request will be, before it is sent - for the line under the box that says what the next
    ///question costs.
    ///
    ///**Characters, not a tokenizer.** Each provider has its own and none ships one a browser can run, so text is
    ///counted at 3.5 characters a token and JSON - the tools - at 3, which is denser; a picture at
    ///<see cref="AiAttachment.ImageTokens"/> and a PDF's page at <see cref="AiAttachment.PdfPageTokens"/>. These are
    ///assumptions, not measurements, and the line says "about"; what a request really cost is what the provider
    ///reports, shown under each answer.
    ///
    public static class AiEstimate
    {
        public const double CharactersPerToken = 3.5;

        public const double JsonCharactersPerToken = 3.0;

        ///<summary>What a message costs besides its words: its role and the marks round it.</summary>
        private const int MessageOverhead = 5;

        public static long TokensIn(long characters)
        {
            return (long)Math.Ceiling(characters / CharactersPerToken);
        }

        ///<summary>The tools as they are sent: each one's name, description and schema.</summary>
        public static long ToolTokens(IReadOnlyList<LlmTool> tools)
        {
            long characters = 0;

            foreach (var tool in tools)
                characters += tool.Name.Length + tool.Description.Length + tool.InputSchemaJson.Length + 40;

            return (long)Math.Ceiling(characters / JsonCharactersPerToken);
        }

        ///<summary>One request's input: the instructions, the tools, and the conversation with its files as the provider is sent them.</summary>
        public static long Request(string systemPrompt, long toolTokens, IReadOnlyList<LlmMessage> conversation, string providerType)
        {
            long tokens = TokensIn(systemPrompt.Length) + toolTokens;

            foreach (var message in conversation)
                tokens += Message(message, providerType);

            return tokens;
        }

        public static long Message(LlmMessage message, string providerType)
        {
            long tokens = TokensIn(message.Text.Length) + MessageOverhead;

            foreach (var file in message.Attachments)
                tokens += Attachment(file, providerType);

            return tokens;
        }

        ///<summary>What a file adds to every request it goes with: nothing much once it is unticked, or where the model is not sent it - the line saying so.</summary>
        public static long Attachment(AiAttachment file, string providerType)
        {
            if (file.Excluded || !AiAttachment.SentTo(providerType, file.Kind))
                return 25;

            return file.Tokens + 15;
        }
    }
}
