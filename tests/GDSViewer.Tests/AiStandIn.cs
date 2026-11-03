using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace GDSViewer.Tests;

///
///A model provider that is not there: an HttpMessageHandler that answers each request from a script, and keeps
///what it was sent.
///
///What lets the whole conversation be run without a network or a key - the request bodies are read back as JSON
///and the replies are written in each provider's own shape, so the code under test is the code that talks to the
///real thing.
///
internal sealed class AiStandIn : HttpMessageHandler
{
    private readonly Func<int, JsonObject, HttpResponseMessage> reply;

    public AiStandIn(Func<int, JsonObject, HttpResponseMessage> reply)
    {
        this.reply = reply;
    }

    ///<summary>Replies with these, one per request, repeating the last when they run out.</summary>
    public AiStandIn(params string[] replies) : this((index, _) => Json(replies[Math.Min(index, replies.Length - 1)]))
    {
    }

    public sealed record Sent(string Uri, Dictionary<string, string> Headers, JsonObject Body);

    public List<Sent> Requests { get; } = new List<Sent>();

    public HttpClient Client()
    {
        return new HttpClient(this) { Timeout = Timeout.InfiniteTimeSpan };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string text = "";

        if (request.Content is not null)
            text = await request.Content.ReadAsStringAsync(cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers)
            headers[header.Key] = string.Join(",", header.Value);

        var body = (JsonObject)JsonNode.Parse(text)!;

        Requests.Add(new Sent(request.RequestUri!.ToString(), headers, body));

        return reply(Requests.Count - 1, body);
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    #region Anthropic's shape ****************************************************

    public static string AnthropicText(string text, string stopReason = "end_turn")
    {
        return new JsonObject
        {
            ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = text } },
            ["stop_reason"] = stopReason
        }.ToJsonString();
    }

    public static string AnthropicCall(string id, string name, string inputJson, string said = "")
    {
        var content = new JsonArray();

        if (said.Length > 0)
            content.Add(new JsonObject { ["type"] = "text", ["text"] = said });

        content.Add(new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = id,
            ["name"] = name,
            ["input"] = JsonNode.Parse(inputJson)
        });

        return new JsonObject { ["content"] = content, ["stop_reason"] = "tool_use" }.ToJsonString();
    }

    ///<summary>A reply that says what it cost, the way Anthropic does: input after the cache, output, and what was read from the cache and written into it.</summary>
    public static string WithAnthropicUsage(string reply, long input, long output, long read = 0, long written = 0)
    {
        var body = (JsonObject)JsonNode.Parse(reply)!;

        body["usage"] = new JsonObject
        {
            ["input_tokens"] = input,
            ["output_tokens"] = output,
            ["cache_read_input_tokens"] = read,
            ["cache_creation_input_tokens"] = written
        };

        return body.ToJsonString();
    }

    #endregion ********************************************************************



    #region The OpenAI-compatible shape *******************************************

    public static string OpenAiText(string text, string finish = "stop")
    {
        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text },
                    ["finish_reason"] = finish
                }
            }
        }.ToJsonString();
    }

    ///<summary>A call, with the thought signature Gemini 3 puts in each one - which has to come back as it went.</summary>
    public static string OpenAiCall(string id, string name, string argumentsJson)
    {
        return new JsonObject
        {
            ["choices"] = new JsonArray
            {
                new JsonObject
                {
                    ["message"] = new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = null,
                        ["refusal"] = null,
                        ["tool_calls"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["id"] = id,
                                ["type"] = "function",
                                ["function"] = new JsonObject { ["name"] = name, ["arguments"] = argumentsJson },
                                ["extra_content"] = new JsonObject
                                {
                                    ["google"] = new JsonObject { ["thought_signature"] = "c2lnbmVk" }
                                }
                            }
                        }
                    },
                    ["finish_reason"] = "tool_calls"
                }
            }
        }.ToJsonString();
    }

    ///<summary>A reply that says what it cost, the chat-completions way - with a total past the other two where a test wants Gemini's unsaid thinking.</summary>
    public static string WithOpenAiUsage(string reply, long prompt, long completion, long cached = 0, long reasoning = 0, long? total = null)
    {
        var body = (JsonObject)JsonNode.Parse(reply)!;

        body["usage"] = new JsonObject
        {
            ["prompt_tokens"] = prompt,
            ["completion_tokens"] = completion,
            ["total_tokens"] = total ?? prompt + completion,
            ["prompt_tokens_details"] = new JsonObject { ["cached_tokens"] = cached },
            ["completion_tokens_details"] = new JsonObject { ["reasoning_tokens"] = reasoning }
        };

        return body.ToJsonString();
    }

    #endregion ********************************************************************
}
