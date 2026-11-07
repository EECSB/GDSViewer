using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GDSViewer.Ai
{
    ///
    ///Calls Anthropic's Messages API straight from the browser, with its tool_use and tool_result loop.
    ///
    ///Request building and response reading are pure and tested on their own; the send is thin. The same split and
    ///the same loop as GraphDBViewer's, with what this app adds: a tool's picture goes back inside its result, and a
    ///refused call is marked as one.
    ///
    public sealed class AnthropicProvider : ILlmProvider
    {
        public const string Endpoint = "https://api.anthropic.com/v1/messages";

        ///
        ///The model a connection runs when its model field is left blank - the most capable one there is, since
        ///that is what somebody who did not choose is best served by. The form's placeholder reads it from here, so
        ///what runs is what was offered.
        ///
        public const string DefaultModel = "claude-opus-5-5";

        private const string Label = "Anthropic";

        ///<summary>The beta a request in fast mode is sent under, beside its speed - see <see cref="LlmConnection.SupportsFast"/>.</summary>
        public const string FastBeta = "fast-mode-2026-02-01";

        private readonly HttpClient http;
        private readonly string model;
        private readonly string apiKey;
        private readonly int maxTokens;
        private readonly string effort;
        private readonly bool fast;

        public AnthropicProvider(HttpClient http, LlmConnection connection)
        {
            this.http = http;

            apiKey = (connection.ApiKey ?? "").Trim();
            maxTokens = ResolveMaxTokens(connection);
            model = ResolveModel(connection);
            effort = ResolveEffort(connection);
            fast = LlmConnection.SupportsFast(connection) && connection.Fast;
            Price = AiPricing.For(connection, DateTime.UtcNow);
        }

        public string Model => model;

        public LlmUsage Spent { get; } = new LlmUsage();

        public LlmPrice? Price { get; }

        ///<summary>The output cap a connection runs with: its own when it has a sensible one, since the API refuses anything but a positive number.</summary>
        public static int ResolveMaxTokens(LlmConnection connection)
        {
            if (connection.MaxTokens > 0)
                return connection.MaxTokens;

            return LlmConnection.DefaultMaxTokens;
        }

        ///<summary>The model a connection runs: its own, or <see cref="DefaultModel"/> when it names none.</summary>
        public static string ResolveModel(LlmConnection connection)
        {
            if (string.IsNullOrWhiteSpace(connection.Model))
                return DefaultModel;

            return connection.Model.Trim();
        }

        ///
        ///The effort a connection is sent with: its own when the model takes that level, and none otherwise - a level
        ///chosen for another model is refused by this one rather than ignored, so it is dropped rather than sent.
        ///
        public static string ResolveEffort(LlmConnection connection)
        {
            if (LlmConnection.EffortLevels(connection).Contains(connection.Effort))
                return connection.Effort;

            return "";
        }

        ///
        ///The conversation in the Messages API's own shape.
        ///
        ///Empty turns are dropped rather than sent: the API refuses an empty content string, and an empty turn is what
        ///a stopped or failed answer leaves behind, so a conversation carrying one would fail every request after it.
        ///
        ///A question with files is a list of blocks, the files ahead of its words - see <see cref="ContentWithFiles"/>;
        ///anything else stays the string it always was.
        ///
        public static JsonArray ToWireMessages(IReadOnlyList<LlmMessage> conversation)
        {
            var messages = new JsonArray();

            foreach (var turn in conversation)
            {
                if (turn.Attachments.Count > 0)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = turn.Role,
                        ["content"] = ContentWithFiles(turn)
                    });

                    continue;
                }

                if (string.IsNullOrWhiteSpace(turn.Text))
                    continue;

                messages.Add(new JsonObject
                {
                    ["role"] = turn.Role,
                    ["content"] = turn.Text
                });
            }

            return messages;
        }

        ///<summary>A question's files as blocks ahead of its words: a picture as an image block, a PDF as a document, a text file as text between tags.</summary>
        public static JsonArray ContentWithFiles(LlmMessage turn)
        {
            var content = new JsonArray();

            foreach (var part in AiAttachment.Parts(turn.Attachments, LlmConnection.Anthropic))
                content.Add(Block(part));

            if (!string.IsNullOrWhiteSpace(turn.Text))
            {
                content.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = turn.Text
                });
            }

            return content;
        }

        ///<summary>One part of a question's files as the Messages API takes it.</summary>
        public static JsonObject Block(AttachmentPart part)
        {
            if (part.Kind == AttachmentKind.Image || part.Kind == AttachmentKind.Pdf)
            {
                string type = "image";

                if (part.Kind == AttachmentKind.Pdf)
                    type = "document";

                return new JsonObject
                {
                    ["type"] = type,
                    ["source"] = new JsonObject
                    {
                        ["type"] = "base64",
                        ["media_type"] = part.MediaType,
                        ["data"] = part.Base64
                    }
                };
            }

            return new JsonObject
            {
                ["type"] = "text",
                ["text"] = part.Text
            };
        }

        ///<summary>The tools as Anthropic's tools array: a name, a description and an input_schema each.</summary>
        public static JsonArray BuildToolsArray(IReadOnlyList<LlmTool> tools)
        {
            var array = new JsonArray();

            foreach (var tool in tools)
            {
                array.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.InputSchemaJson)
                });
            }

            return array;
        }

        ///
        ///A request: the model, the cap, the system prompt when there is one, the conversation so far and, when there are any, the tools -
        ///marked where Anthropic should cache when asked. An effort goes in output_config and fast mode as the speed,
        ///both only when chosen, so a request that chose neither is the one it always was.
        ///
        public static string BuildRequestBody(
            string model,
            int maxTokens,
            string system,
            JsonArray messages,
            JsonArray? tools,
            bool cache = false,
            string effort = "",
            bool fast = false)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["max_tokens"] = maxTokens,
                ["messages"] = messages.DeepClone()
            };

            //A prompt with nothing in it is no prompt: the field is left out, since the API refuses a null or an empty one.
            if (!string.IsNullOrWhiteSpace(system))
                body["system"] = system;

            if (tools is not null && tools.Count > 0)
                body["tools"] = tools.DeepClone();

            if (effort.Length > 0)
                body["output_config"] = new JsonObject { ["effort"] = effort };

            if (fast)
                body["speed"] = "fast";

            if (cache)
                MarkForCache(body);

            return body.ToJsonString();
        }

        ///
        ///Tells Anthropic what to cache: everything up to the last tool, and everything up to the end of the request.
        ///
        ///**Anthropic caches only what it is told to**, where OpenAI and Google cache a repeated opening by themselves -
        ///and a request reads back what an earlier one cached up to one of its own marks. So two marks:
        ///
        ///- **After the tools**, which are the same in every request of every conversation and are most of what one
        ///sends. The system prompt comes after them and is not under this mark, since it is written again for every
        ///question with the selection and the view in it.
        ///- **After the last block of the conversation**, so the next round of a run of tool calls - which sends all of
        ///this again with one more call and its answer - reads it back at a tenth of the rate, or a twentieth on Opus
        ///and Sonnet. A string is made a block to carry it.
        ///
        ///**On the copy each request is built from**, never on the conversation the loop keeps adding to: marks left on
        ///every round's last block would pass Anthropic's limit of four in three rounds. A write costs a quarter over the
        ///rate and lasts five minutes, which a run of calls uses at once; a request answered in one round pays it for
        ///nothing, which is why only the tool loop asks.
        ///
        public static void MarkForCache(JsonObject body)
        {
            if (body["tools"] is JsonArray tools && tools.Count > 0 && tools[tools.Count - 1] is JsonObject lastTool)
                lastTool["cache_control"] = ephemeral();

            if (body["messages"] is not JsonArray messages || messages.Count == 0 || messages[messages.Count - 1] is not JsonObject last)
                return;

            if (last["content"] is JsonValue words)
            {
                last["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = words.GetValue<string>()
                    }
                };
            }

            if (last["content"] is JsonArray blocks && blocks.Count > 0 && blocks[blocks.Count - 1] is JsonObject block)
                block["cache_control"] = ephemeral();
        }

        private static JsonObject ephemeral()
        {
            return new JsonObject { ["type"] = "ephemeral" };
        }

        ///
        ///What one call answered, as a tool_result block.
        ///
        ///A picture goes in beside the words, which the Messages API takes inside a tool's result - so the model sees
        ///it as the answer to the call that asked for it, rather than as something the person said.
        ///
        public static JsonObject ToolResult(string id, LlmToolOutput output)
        {
            var result = new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = id
            };

            if (output.Png is null)
            {
                result["content"] = output.Text;
            }
            else
            {
                result["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = output.Text
                    },
                    new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = "image/png",
                            ["data"] = output.Png
                        }
                    }
                };
            }

            if (output.IsError)
                result["is_error"] = true;

            return result;
        }

        ///<summary>The tool_use blocks in a reply's content: empty when the model answered in words.</summary>
        public static List<LlmToolCall> ParseToolUses(JsonElement content)
        {
            var calls = new List<LlmToolCall>();

            if (content.ValueKind != JsonValueKind.Array)
                return calls;

            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var type) || type.GetString() != "tool_use")
                    continue;

                if (!block.TryGetProperty("name", out var name) || name.GetString() is not string called)
                    continue;

                string id = "";

                if (block.TryGetProperty("id", out var given) && given.GetString() is string named)
                    id = named;

                string arguments = "{}";

                if (block.TryGetProperty("input", out var input))
                    arguments = input.GetRawText();

                calls.Add(new LlmToolCall { Id = id, Name = called, ArgumentsJson = arguments });
            }

            return calls;
        }

        ///<summary>The text blocks of a reply's content, joined.</summary>
        public static string TextOf(JsonElement content)
        {
            var builder = new StringBuilder();

            if (content.ValueKind != JsonValueKind.Array)
                return "";

            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text" && block.TryGetProperty("text", out var text))
                    builder.Append(text.GetString());
            }

            return builder.ToString().Trim();
        }

        ///<summary>A plain reply read: its text, or what the API said was wrong, or that it declined.</summary>
        public static LlmResult ParseResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                var root = document.RootElement;

                if (LlmProviderFactory.ErrorIn(json) is string said)
                    return LlmResult.Failure(said);

                if (stopReason(root) == "refusal")
                    return LlmResult.Failure("The model declined to answer.");

                if (!root.TryGetProperty("content", out var content))
                    return LlmResult.Failure("Anthropic answered in a shape this app does not know.");

                return answered(TextOf(content), stopReason(root));
            }
            catch (JsonException)
            {
                return LlmResult.Failure("Anthropic answered with something that is not JSON.");
            }
        }

        public async Task<LlmResult> CompleteAsync(string systemPrompt, IReadOnlyList<LlmMessage> conversation, CancellationToken ct)
        {
            if (refusal() is string why)
                return LlmResult.Failure(why);

            string body = BuildRequestBody(model, maxTokens, systemPrompt, ToWireMessages(conversation), null, false, effort, fast);
            var (json, problem) = await LlmProviderFactory.SendAsync(http, request(body), Label, ct);

            if (problem is not null || json is null)
                return LlmResult.Failure(problem ?? "Anthropic sent nothing back.");

            recordUsage(json);

            return ParseResponse(json);
        }

        public async Task<LlmResult> CompleteWithToolsAsync(string systemPrompt, IReadOnlyList<LlmMessage> conversation, ILlmToolRunner tools, CancellationToken ct, int maxRounds = LlmProviderFactory.DefaultToolRounds)
        {
            if (refusal() is string why)
                return LlmResult.Failure(why);

            var toolsArray = BuildToolsArray(tools.Tools);
            var messages = ToWireMessages(conversation);
            var progress = new ToolProgress();

            for (int round = 0; round < maxRounds; round++)
            {
                string body = BuildRequestBody(model, maxTokens, systemPrompt, messages, toolsArray, true, effort, fast);
                var (json, problem) = await LlmProviderFactory.SendAsync(http, request(body), Label, ct);

                if (problem is not null || json is null)
                    return LlmResult.Failure(problem ?? "Anthropic sent nothing back.");

                JsonDocument document;

                try
                {
                    document = JsonDocument.Parse(json);
                }
                catch (JsonException)
                {
                    return LlmResult.Failure("Anthropic answered with something that is not JSON.");
                }

                using (document)
                {
                    var root = document.RootElement;

                    //Before anything can end the run: a round is billed whatever comes of it.
                    Spent.Add(LlmUsage.FromAnthropic(root));

                    if (LlmProviderFactory.ErrorIn(json) is string said)
                        return LlmResult.Failure(said);

                    if (stopReason(root) == "refusal")
                        return LlmResult.Failure("The model declined to answer.");

                    if (!root.TryGetProperty("content", out var content))
                        return LlmResult.Failure("Anthropic answered in a shape this app does not know.");

                    var calls = ParseToolUses(content);

                    if (calls.Count == 0)
                        return answered(TextOf(content), stopReason(root));

                    //A call cut off at the cap has half its arguments, and running it would be running a guess.
                    if (stopReason(root) == "max_tokens")
                        return LlmResult.Failure($"The model ran into its limit of {maxTokens} tokens partway through a call. Raise Max tokens for this model under Settings, AI settings.");

                    //Its reply goes back as it came, tool_use blocks and all, followed by an answer to each call.
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = JsonNode.Parse(content.GetRawText())
                    });

                    var results = new JsonArray();
                    var ran = new List<(LlmToolCall, LlmToolOutput)>();

                    foreach (var call in calls)
                    {
                        var output = await tools.RunToolAsync(call.Name, call.ArgumentsJson, ct);

                        results.Add(ToolResult(call.Id, output));
                        ran.Add((call, output));
                    }

                    messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = results
                    });

                    if (progress.IsStuckAfter(ran))
                        return LlmResult.Failure(LlmProviderFactory.RoundsStuck);
                }
            }

            return LlmResult.Paused(LlmProviderFactory.RoundsPaused(maxRounds));
        }

        private static string stopReason(JsonElement root)
        {
            if (root.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                return reason.GetString() ?? "";

            return "";
        }

        ///<summary>The text of a finished reply, said as cut off when the cap is what finished it.</summary>
        private static LlmResult answered(string text, string reason)
        {
            if (text.Length == 0 && reason == "max_tokens")
                return LlmResult.Failure("The model ran into its token limit before it wrote anything. Raise Max tokens for this model under Settings, AI settings.");

            if (text.Length == 0)
                return LlmResult.Failure("The model sent back an empty answer.");

            if (reason == "max_tokens")
                return LlmResult.Success(text + Environment.NewLine + Environment.NewLine + "(Cut off at the token limit.)");

            return LlmResult.Success(text);
        }

        ///<summary>Why nothing should be sent, or null when the connection is fit to send with.</summary>
        private string? refusal()
        {
            if (apiKey.Length == 0)
                return "This model has no API key. Add one under Settings, AI settings.";

            return null;
        }

        ///<summary>Adds what an answer says it cost to <see cref="Spent"/>.</summary>
        private void recordUsage(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                Spent.Add(LlmUsage.FromAnthropic(document.RootElement));
            }
            catch (JsonException) { }
        }

        private HttpRequestMessage request(string body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            message.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");

            if (fast)
                message.Headers.TryAddWithoutValidation("anthropic-beta", FastBeta);

            //What Anthropic asks of a page calling it directly, rather than through a server of its own: without it the
            //browser's preflight is refused. The key is the person's, in their own browser, sent only here.
            message.Headers.TryAddWithoutValidation("anthropic-dangerous-direct-browser-access", "true");

            return message;
        }
    }
}
