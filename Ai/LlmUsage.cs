using System.Text.Json;
using System.Text.Json.Serialization;

namespace GDSViewer.Ai
{
    ///
    ///What requests were billed for, in tokens: one answer's, a whole run's, or everything a provider has sent.
    ///
    ///**Three of the five are parts, not additions** - the shape ConfigurationRuleEditor's Spent keeps.
    ///<see cref="InputTokens"/> is everything read, and <see cref="CachedInputTokens"/> and
    ///<see cref="CacheWriteTokens"/> are the parts of it read from a provider's cache and written into it, each billed
    ///at a rate of its own; <see cref="OutputTokens"/> is everything written, and <see cref="ReasoningTokens"/> the
    ///part of it that was thinking. Priced in <see cref="LlmPrice.Cost"/>.
    ///
    ///
    ///The names are pinned, as <see cref="AiJson"/> says every kept shape's are: a saved chat carries what it cost, and a
    ///rename here would read an earlier version's as nothing spent.
    ///
    public sealed class LlmUsage
    {
        [JsonPropertyName("InputTokens")]
        public long InputTokens { get; set; }

        ///<summary>Part of <see cref="InputTokens"/>: read back from the provider's cache, at a tenth of the rate or less.</summary>
        [JsonPropertyName("CachedInputTokens")]
        public long CachedInputTokens { get; set; }

        ///<summary>Part of <see cref="InputTokens"/>: put into Anthropic's cache, at a quarter over the rate. Nobody else reports writes.</summary>
        [JsonPropertyName("CacheWriteTokens")]
        public long CacheWriteTokens { get; set; }

        [JsonPropertyName("OutputTokens")]
        public long OutputTokens { get; set; }

        ///<summary>Part of <see cref="OutputTokens"/>: the thinking rather than the answer, billed the same.</summary>
        [JsonPropertyName("ReasoningTokens")]
        public long ReasoningTokens { get; set; }

        ///<summary>How many answers said what they cost. A run of tool calls is several billed requests.</summary>
        [JsonPropertyName("Requests")]
        public int Requests { get; set; }

        ///<summary>False until an answer has said what it cost - a server of one's own may never, and then nothing is shown rather than a zero.</summary>
        [JsonIgnore]
        public bool Reported => Requests > 0;

        public void Add(LlmUsage? more)
        {
            if (more is null)
                return;

            InputTokens += more.InputTokens;
            CachedInputTokens += more.CachedInputTokens;
            CacheWriteTokens += more.CacheWriteTokens;
            OutputTokens += more.OutputTokens;
            ReasoningTokens += more.ReasoningTokens;
            Requests += more.Requests;
        }

        public LlmUsage Copy()
        {
            var copy = new LlmUsage();

            copy.Add(this);

            return copy;
        }

        ///<summary>What was added since <paramref name="before"/> was copied from this - one run's share of a provider's total.</summary>
        public LlmUsage Since(LlmUsage before)
        {
            return new LlmUsage
            {
                InputTokens = InputTokens - before.InputTokens,
                CachedInputTokens = CachedInputTokens - before.CachedInputTokens,
                CacheWriteTokens = CacheWriteTokens - before.CacheWriteTokens,
                OutputTokens = OutputTokens - before.OutputTokens,
                ReasoningTokens = ReasoningTokens - before.ReasoningTokens,
                Requests = Requests - before.Requests
            };
        }

        ///
        ///What one Messages API answer says it cost, or null when it says nothing.
        ///
        ///**Anthropic's input_tokens leaves its cache out**: it counts what came after the last place the cache was read
        ///or written, and cache_read_input_tokens and cache_creation_input_tokens count the rest. So everything read is
        ///the three together, which is the shape the other providers report it in.
        ///
        public static LlmUsage? FromAnthropic(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return null;

            long fresh = count(usage, "input_tokens");
            long read = count(usage, "cache_read_input_tokens");
            long written = count(usage, "cache_creation_input_tokens");

            return new LlmUsage
            {
                InputTokens = fresh + read + written,
                CachedInputTokens = read,
                CacheWriteTokens = written,
                OutputTokens = count(usage, "output_tokens"),
                Requests = 1
            };
        }

        ///
        ///What one chat-completions answer says it cost, or null when it says nothing - which a server of one's own may.
        ///
        ///prompt_tokens counts everything read, its cached part in prompt_tokens_details; completion_tokens everything
        ///written, its reasoning in completion_tokens_details. **Gemini, through Google's compatible endpoint, may leave
        ///its thinking out of completion_tokens** and count it in total_tokens alone - so what total_tokens holds beyond
        ///the other two is taken for thinking, which Google bills as output. Where it holds nothing more, nothing is added.
        ///That is how the endpoint answers as people using it report it, not anything Google documents for it - its page
        ///says nothing of the fields (read 2026-10-29) - so a reasoning count said beside the surplus is taken for the
        ///same thinking said twice, and the larger of the two is the thinking.
        ///
        public static LlmUsage? FromOpenAi(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
                return null;

            long prompt = count(usage, "prompt_tokens");
            long completion = count(usage, "completion_tokens");
            long reasoning = 0;
            long cached = 0;

            if (usage.TryGetProperty("prompt_tokens_details", out var read) && read.ValueKind == JsonValueKind.Object)
                cached = count(read, "cached_tokens");

            if (usage.TryGetProperty("completion_tokens_details", out var written) && written.ValueKind == JsonValueKind.Object)
                reasoning = count(written, "reasoning_tokens");

            long unsaid = count(usage, "total_tokens") - prompt - completion;

            if (unsaid > 0)
            {
                completion += unsaid;
                reasoning = Math.Max(reasoning, unsaid);
            }

            return new LlmUsage
            {
                InputTokens = prompt,
                CachedInputTokens = cached,
                OutputTokens = completion,
                ReasoningTokens = reasoning,
                Requests = 1
            };
        }

        private static long count(JsonElement owner, string name)
        {
            if (owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
                return Math.Max(0, number);

            return 0;
        }
    }
}
