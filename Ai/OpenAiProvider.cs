using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GDSViewer.Ai
{
    ///
    ///Calls any OpenAI-compatible chat-completions endpoint straight from the browser: OpenAI itself, Google's Gemini
    ///through its OpenAI-compatible endpoint, or a server of one's own - Ollama, LM Studio, vLLM, a company's own.
    ///
    ///**One adapter for three families**, which is GraphDBViewer's choice and is kept: Google answers this shape at
    ///its own address, so a Gemini key works here without a provider of its own.
    ///
    ///**A picture goes back in a user turn.** Chat completions take an image only from the user, never in a tool's
    ///result, so a tool that took one answers in words in its tool turn and the picture follows in a user turn of its
    ///own, saying which call it came from.
    ///
    public sealed class OpenAiProvider : ILlmProvider
    {
        public const string DefaultBaseUrl = "https://api.openai.com/v1";

        ///<summary>Google's OpenAI-compatible endpoint. A key from Google AI Studio works here as it is.</summary>
        public const string GeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";

        ///<summary>The model ids the form shows as examples. Only examples: the field is the person's, and is required.</summary>
        public const string OpenAiExample = "gpt-5-mini";

        ///<summary>The newest stable Flash Google listed when the live tests last ran, on 2026-10-10.</summary>
        public const string GeminiExample = "gemini-3.8-flash";

        ///<summary>What a server of one's own answers when it was given no address, and so is sent nothing.</summary>
        public const string NoAddress = "This model is a server of your own and has no address to send to. Give it its base URL under Settings, AI settings.";

        private readonly HttpClient http;
        private readonly string? endpoint;
        private readonly string model;
        private readonly string apiKey;
        private readonly string effort;
        private readonly string providerType;
        private readonly string label;

        public OpenAiProvider(HttpClient http, LlmConnection connection)
        {
            this.http = http;

            apiKey = (connection.ApiKey ?? "").Trim();
            model = (connection.Model ?? "").Trim();
            effort = connection.Effort;
            providerType = connection.ProviderType;
            endpoint = ChatCompletionsUrl(connection.BaseUrl, DefaultBaseUrlFor(connection.ProviderType));
            label = labelFor(connection.ProviderType, endpoint);
            Price = AiPricing.For(connection, DateTime.UtcNow);
        }

        public string Model => model;

        public LlmUsage Spent { get; } = new LlmUsage();

        public LlmPrice? Price { get; }

        ///
        ///Where a connection with a blank base URL is sent: Google's endpoint for Gemini, OpenAI's for OpenAI - and
        ///nowhere for a server of one's own, whose address is only ever the one it is given.
        ///
        ///**Nowhere, not OpenAI.** GraphDBViewer sent a blank one to OpenAI until 2026-10-03, which put the key meant
        ///for somebody's own server, and their question, in front of a company they had not chosen.
        ///
        public static string? DefaultBaseUrlFor(string providerType)
        {
            if (providerType == LlmConnection.Gemini)
                return GeminiBaseUrl;

            if (providerType == LlmConnection.Custom)
                return null;

            return DefaultBaseUrl;
        }

        ///<summary>The chat-completions address under a base URL, or the default's when it is blank; null when there is neither.</summary>
        public static string? ChatCompletionsUrl(string? baseUrl, string? defaultBaseUrl)
        {
            string? root = defaultBaseUrl;

            if (!string.IsNullOrWhiteSpace(baseUrl))
                root = baseUrl.Trim().TrimEnd('/');

            if (string.IsNullOrWhiteSpace(root))
                return null;

            return $"{root}/chat/completions";
        }

        ///
        ///The conversation in the chat-completions shape, led by the system turn.
        ///
        ///Empty turns are dropped: one is what a stopped or failed answer leaves behind, and some servers refuse an
        ///empty content outright.
        ///
        ///A question with files is a list of parts, the files ahead of its words, written for the provider it goes to -
        ///which decides whether a PDF can be one of them; see <see cref="AiAttachment.SentTo"/>.
        ///
        public static JsonArray ToWireMessages(string system, IReadOnlyList<LlmMessage> conversation, string providerType = LlmConnection.OpenAI)
        {
            var messages = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] = system
                }
            };

            foreach (var turn in conversation)
            {
                if (turn.Attachments.Count > 0)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = turn.Role,
                        ["content"] = ContentWithFiles(turn, providerType)
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

        ///<summary>A question's files as parts ahead of its words: a picture as an image_url, a PDF as a file, a text file as text between tags.</summary>
        public static JsonArray ContentWithFiles(LlmMessage turn, string providerType)
        {
            var content = new JsonArray();

            foreach (var part in AiAttachment.Parts(turn.Attachments, providerType))
                content.Add(Part(part));

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

        ///
        ///One part of a question's files as chat completions take it.
        ///
        ///A picture is a data URL in an image_url part, as look's pictures are. A PDF is a file part with its bytes as a
        ///data URL in file_data - the one kind of file OpenAI's chat completions take, and only ever sent to OpenAI.
        ///
        public static JsonObject Part(AttachmentPart part)
        {
            if (part.Kind == AttachmentKind.Image)
            {
                return new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = $"data:{part.MediaType};base64,{part.Base64}" }
                };
            }

            if (part.Kind == AttachmentKind.Pdf)
            {
                return new JsonObject
                {
                    ["type"] = "file",
                    ["file"] = new JsonObject
                    {
                        ["filename"] = part.Name,
                        ["file_data"] = $"data:application/pdf;base64,{part.Base64}"
                    }
                };
            }

            return new JsonObject
            {
                ["type"] = "text",
                ["text"] = part.Text
            };
        }

        ///<summary>The tools as the tools array: each a function with a name, a description and its parameters.</summary>
        public static JsonArray BuildToolsArray(IReadOnlyList<LlmTool> tools)
        {
            var array = new JsonArray();

            foreach (var tool in tools)
            {
                array.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.InputSchemaJson)
                    }
                });
            }

            return array;
        }

        ///
        ///A request: the model, the conversation so far, the tools when there are any, and an effort when one was
        ///chosen.
        ///
        ///**The effort only when asked for.** A reasoning model reads it and a chat model refuses the whole request
        ///over it, so the person decides - see <see cref="LlmConnection.SupportsEffort"/> - rather than this guessing
        ///from the model's name.
        ///
        public static string BuildRequestBody(string model, JsonArray messages, JsonArray? tools, string effort)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["messages"] = messages.DeepClone()
            };

            if (tools is not null && tools.Count > 0)
                body["tools"] = tools.DeepClone();

            if (!string.IsNullOrEmpty(effort))
                body["reasoning_effort"] = effort;

            return body.ToJsonString();
        }

        ///<summary>The calls in an assistant message: empty when it answered in words.</summary>
        public static List<LlmToolCall> ParseToolCalls(JsonElement message)
        {
            var calls = new List<LlmToolCall>();

            if (!message.TryGetProperty("tool_calls", out var asked) || asked.ValueKind != JsonValueKind.Array)
                return calls;

            foreach (var call in asked.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var function) || !function.TryGetProperty("name", out var name) || name.GetString() is not string called)
                    continue;

                string id = "";

                if (call.TryGetProperty("id", out var given) && given.ValueKind == JsonValueKind.String)
                    id = given.GetString() ?? "";

                string arguments = "{}";

                if (function.TryGetProperty("arguments", out var said))
                {
                    if (said.ValueKind == JsonValueKind.String)
                        arguments = said.GetString() ?? "{}";
                    else if (said.ValueKind == JsonValueKind.Object)
                        arguments = said.GetRawText();
                }

                calls.Add(new LlmToolCall { Id = id, Name = called, ArgumentsJson = arguments });
            }

            return calls;
        }

        ///
        ///The assistant's message as it goes back into the next request: its words and its calls.
        ///
        ///**The calls are copied as they came**, not rebuilt from what was read out of them. Gemini 3 hands back a
        ///signature of its thinking inside each call - `extra_content` - and refuses the next request if it does not
        ///come back with it. Anything else a server adds rides along the same way.
        ///
        ///Only the role, the content and the calls, though, rather than the whole message: OpenAI's reply carries
        ///fields of its own that its requests do not take.
        ///
        public static JsonObject EchoAssistantMessage(JsonElement message)
        {
            var echo = new JsonObject { ["role"] = "assistant" };

            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                echo["content"] = content.GetString();
            else
                echo["content"] = null;

            if (message.TryGetProperty("tool_calls", out var calls))
                echo["tool_calls"] = JsonNode.Parse(calls.GetRawText());

            return echo;
        }

        ///<summary>A call's answer, as a tool turn. A refusal says so in words, there being no flag for it here.</summary>
        public static JsonObject ToolTurn(string id, LlmToolOutput output)
        {
            string text = output.Text;

            if (output.IsError)
                text = "Refused: " + text;

            return new JsonObject
            {
                ["role"] = "tool",
                ["tool_call_id"] = id,
                ["content"] = text
            };
        }

        ///<summary>The pictures a round of calls took, as one user turn after their tool turns, each named by its call.</summary>
        public static JsonObject PictureTurn(IReadOnlyList<(string Tool, string Png)> pictures)
        {
            var parts = new JsonArray();

            foreach (var (tool, png) in pictures)
            {
                parts.Add(new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = $"The picture {tool} took:"
                });

                parts.Add(new JsonObject
                {
                    ["type"] = "image_url",
                    ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + png }
                });
            }

            return new JsonObject
            {
                ["role"] = "user",
                ["content"] = parts
            };
        }

        ///<summary>A plain reply read: choices[0].message.content, or what the server said was wrong.</summary>
        public static LlmResult ParseResponse(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                if (LlmProviderFactory.ErrorIn(json) is string said)
                    return LlmResult.Failure(said);

                if (firstChoice(document.RootElement) is not JsonElement choice || !choice.TryGetProperty("message", out var message))
                    return LlmResult.Failure("The server answered in a shape this app does not know.");

                return answered(contentOf(message), finishReason(choice));
            }
            catch (JsonException)
            {
                return LlmResult.Failure("The server answered with something that is not JSON.");
            }
        }

        public async Task<LlmResult> CompleteAsync(string systemPrompt, IReadOnlyList<LlmMessage> conversation, CancellationToken ct)
        {
            if (refusal() is string why)
                return LlmResult.Failure(why);

            string body = BuildRequestBody(model, ToWireMessages(systemPrompt, conversation, providerType), null, effort);
            var (json, problem) = await LlmProviderFactory.SendAsync(http, request(body), label, ct);

            if (problem is not null || json is null)
                return LlmResult.Failure(problem ?? $"{label} sent nothing back.");

            recordUsage(json);

            return ParseResponse(json);
        }

        public async Task<LlmResult> CompleteWithToolsAsync(string systemPrompt, IReadOnlyList<LlmMessage> conversation, ILlmToolRunner tools, CancellationToken ct, int maxRounds = LlmProviderFactory.DefaultToolRounds)
        {
            if (refusal() is string why)
                return LlmResult.Failure(why);

            var toolsArray = BuildToolsArray(tools.Tools);
            var messages = ToWireMessages(systemPrompt, conversation, providerType);
            var progress = new ToolProgress();

            for (int round = 0; round < maxRounds; round++)
            {
                string body = BuildRequestBody(model, messages, toolsArray, effort);
                var (json, problem) = await LlmProviderFactory.SendAsync(http, request(body), label, ct);

                if (problem is not null || json is null)
                    return LlmResult.Failure(problem ?? $"{label} sent nothing back.");

                JsonDocument document;

                try
                {
                    document = JsonDocument.Parse(json);
                }
                catch (JsonException)
                {
                    return LlmResult.Failure($"{label} answered with something that is not JSON.");
                }

                using (document)
                {
                    //Before anything can end the run: a round is billed whatever comes of it.
                    Spent.Add(LlmUsage.FromOpenAi(document.RootElement));

                    if (LlmProviderFactory.ErrorIn(json) is string said)
                        return LlmResult.Failure(said);

                    if (firstChoice(document.RootElement) is not JsonElement choice || !choice.TryGetProperty("message", out var message))
                        return LlmResult.Failure($"{label} answered in a shape this app does not know.");

                    var calls = ParseToolCalls(message);

                    if (calls.Count == 0)
                        return answered(contentOf(message), finishReason(choice));

                    if (finishReason(choice) == "length")
                        return LlmResult.Failure("The model ran into its length limit partway through a call, so the call was not run.");

                    messages.Add(EchoAssistantMessage(message));

                    var pictures = new List<(string Tool, string Png)>();
                    var ran = new List<(LlmToolCall, LlmToolOutput)>();

                    foreach (var call in calls)
                    {
                        var output = await tools.RunToolAsync(call.Name, call.ArgumentsJson, ct);

                        messages.Add(ToolTurn(call.Id, output));
                        ran.Add((call, output));

                        if (output.Png is string png)
                            pictures.Add((call.Name, png));
                    }

                    if (pictures.Count > 0)
                        messages.Add(PictureTurn(pictures));

                    if (progress.IsStuckAfter(ran))
                        return LlmResult.Failure(LlmProviderFactory.RoundsStuck);
                }
            }

            return LlmResult.Paused(LlmProviderFactory.RoundsPaused(maxRounds));
        }

        private static JsonElement? firstChoice(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices))
                return null;

            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return null;

            return choices[0];
        }

        private static string contentOf(JsonElement message)
        {
            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return (content.GetString() ?? "").Trim();

            return "";
        }

        private static string finishReason(JsonElement choice)
        {
            if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                return reason.GetString() ?? "";

            return "";
        }

        ///<summary>The text of a finished reply, said as cut off when the length limit is what finished it.</summary>
        private static LlmResult answered(string text, string reason)
        {
            if (text.Length == 0 && reason == "length")
                return LlmResult.Failure("The model ran into its length limit before it wrote anything.");

            if (text.Length == 0)
                return LlmResult.Failure("The model sent back an empty answer.");

            if (reason == "length")
                return LlmResult.Success(text + Environment.NewLine + Environment.NewLine + "(Cut off at the length limit.)");

            return LlmResult.Success(text);
        }

        ///<summary>What the answers name the server as: the provider's name, or the host of a server of one's own.</summary>
        private static string labelFor(string providerType, string? endpoint)
        {
            if (providerType == LlmConnection.Gemini)
                return "Gemini";

            if (providerType == LlmConnection.OpenAI)
                return "OpenAI";

            if (endpoint is not null && Uri.TryCreate(endpoint, UriKind.Absolute, out var address))
                return address.Host;

            return "The server";
        }

        ///<summary>Why nothing should be sent, or null when the connection is fit to send with.</summary>
        private string? refusal()
        {
            if (endpoint is null)
                return NoAddress;

            if (model.Length == 0)
                return "This model has no model id. Give it one under Settings, AI settings.";

            //A server of one's own may need no key; the hosted ones always do, and would only say so with a 401.
            if (apiKey.Length == 0 && providerType != LlmConnection.Custom)
                return "This model has no API key. Add one under Settings, AI settings.";

            return null;
        }

        ///<summary>Adds what an answer says it cost to <see cref="Spent"/>.</summary>
        private void recordUsage(string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                Spent.Add(LlmUsage.FromOpenAi(document.RootElement));
            }
            catch (JsonException) { }
        }

        private HttpRequestMessage request(string body)
        {
            var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            if (apiKey.Length > 0)
                message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

            return message;
        }
    }
}
