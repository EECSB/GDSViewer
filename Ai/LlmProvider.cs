using System.Net;
using System.Text.Json;

namespace GDSViewer.Ai
{
    ///<summary>What one completion came back with: the text the model wrote, or why there is none.</summary>
    public readonly struct LlmResult
    {
        private LlmResult(string text, string error, bool isError, bool isPaused = false)
        {
            Text = text;
            Error = error;
            IsError = isError;
            IsPaused = isPaused;
        }

        public string Text { get; }

        public string Error { get; }

        public bool IsError { get; }

        ///<summary>A run of tool calls that reached its limit of rounds still going somewhere: not an answer, and not an error either. <see cref="Text"/> says so.</summary>
        public bool IsPaused { get; }

        public static LlmResult Success(string text)
        {
            return new LlmResult(text, "", false);
        }

        public static LlmResult Failure(string error)
        {
            return new LlmResult("", error, true);
        }

        public static LlmResult Paused(string note)
        {
            return new LlmResult(note, "", false, true);
        }
    }

    ///
    ///A model the chat can talk to: one implementation per family of API, Anthropic's and the OpenAI-compatible one.
    ///
    ///**Ported from GraphDBViewer**, whose Ask AI panel this is the same design as - see Treeality's
    ///GraphDBViewer.Core/Code/Llm, where it is written. What is different here is said where it is: the tools are a
    ///layout's rather than a database's, a tool can answer with a picture, and the request bodies are built as JSON
    ///nodes rather than serialized from anonymous objects, because this app is trimmed and compiled ahead of time and
    ///reflection over a type nobody names is the one thing that cannot be promised to survive that.
    ///
    public interface ILlmProvider
    {
        ///<summary>The model it asks for, as it is sent - what its spend is kept under.</summary>
        string Model { get; }

        ///
        ///Everything its requests were billed for, added as each answer comes back rather than when a run ends - so a run
        ///stopped or failed partway still has what it spent counted. A caller wanting one run's share copies this before
        ///and takes <see cref="LlmUsage.Since"/> after.
        ///
        LlmUsage Spent { get; }

        ///<summary>What its tokens cost, or null when no rate is known for the model - see <see cref="AiPricing.For"/>.</summary>
        LlmPrice? Price { get; }

        ///<summary>A plain answer to a conversation, with no tools.</summary>
        Task<LlmResult> CompleteAsync(string systemPrompt, IReadOnlyList<LlmMessage> conversation, CancellationToken ct);

        ///
        ///The same conversation with the runner's tools offered, running each call the model makes and handing back
        ///what it answered, until the model replies in words or <paramref name="maxRounds"/> runs out - the person's
        ///setting, <see cref="ToolRoundsPreference"/>, where the chat passes one.
        ///
        Task<LlmResult> CompleteWithToolsAsync(string systemPrompt, IReadOnlyList<LlmMessage> conversation, ILlmToolRunner tools, CancellationToken ct, int maxRounds = LlmProviderFactory.DefaultToolRounds);
    }

    ///<summary>Builds the right <see cref="ILlmProvider"/> for a saved model, and holds what both families share.</summary>
    public static class LlmProviderFactory
    {
        ///
        ///How many rounds of tool calls one message may take before the run is paused for the person to carry on.
        ///
        ///**A pause, not an error, since 2026-11-01** (asked for). It was ten rounds and a red "kept calling tools
        ///without answering", which fired on a model doing exactly what it was asked: a cell drawn, labeled, looked at
        ///and checked is ten rounds of calls that all worked. What the count still guards against is spend - every
        ///round sends the whole conversation again - so it stays, higher, and ends the run with Continue rather than a
        ///failure. A model that is actually stuck is <see cref="ToolProgress"/>'s to stop, long before this.
        ///
        ///**Thirty by default, and the person's to set** under Settings, AI settings (2026-11-01, asked for) - see
        ///<see cref="ToolRoundsPreference"/>.
        ///
        public const int DefaultToolRounds = 30;

        ///<summary>What the run says when its rounds run out with the calls still going somewhere: the question Continue answers.</summary>
        public static string RoundsPaused(int rounds)
        {
            return $"The assistant reached the set limit of {rounds} rounds of tool calls, and is paused. Nothing went wrong - continue for another {rounds} rounds?";
        }

        ///
        ///How many rounds in a row may get nowhere before the run is stopped as stuck: every call in them refused, or one
        ///already made and answered the same way. Three, so a model that tries one thing that fails and then something
        ///else is left to find its way, and one going round in circles is stopped within a few requests.
        ///
        public const int MaxStuckRounds = 3;

        ///<summary>What the run says when <see cref="MaxStuckRounds"/> runs out - the one way a run of calls is an error.</summary>
        public static readonly string RoundsStuck = $"The model went {MaxStuckRounds} rounds of tool calls without getting anywhere - each call refused, or one it had already made - and was stopped.";

        ///
        ///How long one request may take before it is given up on.
        ///
        ///The HttpClient's own timeout is off - see <see cref="NewHttpClient"/> - so this is the only clock, and it
        ///belongs to each request rather than to the client. Five minutes is longer than a model takes to think about
        ///anything this app asks, and short enough that a request nobody is going to answer does not hold the panel
        ///until the tab is closed.
        ///
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(5);

        public static ILlmProvider Create(HttpClient http, LlmConnection connection)
        {
            if (connection.ProviderType == LlmConnection.Anthropic)
                return new AnthropicProvider(http, connection);

            return new OpenAiProvider(http, connection);
        }

        ///
        ///The client the providers send with.
        ///
        ///**With no timeout of its own.** HttpClient gives up after a hundred seconds unless told otherwise, and a
        ///model writing a long answer, or thinking hard, can take longer than that - LabEquipmentController found its
        ///datasheet extraction cut off at exactly 100 s, with the provider still working. Each request carries
        ///<see cref="RequestTimeout"/> instead, which says which request it was when it fires.
        ///
        public static HttpClient NewHttpClient()
        {
            return new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        ///
        ///Sends a request and reads what came back, turning what can go wrong on the way into a sentence.
        ///
        ///**The status is read, not only the body.** A provider that refuses says why in its body most of the time,
        ///and that is the sentence worth showing - a key that is wrong, a model that does not exist, a quota spent. But
        ///a gateway in between answers with a page of HTML, and a body that is not JSON gave "Could not parse model
        ///response" in GraphDBViewer, which names the wrong problem. So a failure that says nothing readable is
        ///reported by its status.
        ///
        ///**A request that never arrives is said as one.** In a browser, a server that refuses a page's request - by
        ///not allowing it under CORS, or by not being there - surfaces as a bare "Failed to fetch". That is the
        ///likeliest failure for a server of one's own, so the sentence says which address it was and what to check.
        ///GraphDBViewer meant to add the same hint and never did: the providers caught the exception first.
        ///
        ///Cancellation by the person is let through as it is, so the panel can tell it from a failure.
        ///
        internal static async Task<(string? Body, string? Problem)> SendAsync(HttpClient http, HttpRequestMessage request, string label, CancellationToken ct)
        {
            using var sending = request;
            using var clock = CancellationTokenSource.CreateLinkedTokenSource(ct);

            clock.CancelAfter(RequestTimeout);

            HttpResponseMessage response;

            try
            {
                response = await http.SendAsync(request, clock.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (null, $"{label} did not answer within {(int)RequestTimeout.TotalSeconds} s.");
            }
            catch (HttpRequestException failed)
            {
                string address = request.RequestUri?.GetLeftPart(UriPartial.Authority) ?? "the server";

                return (null, $"Could not reach {address}: {failed.Message}. If the address is right, the server may not allow requests from a web page (CORS), or may not be running.");
            }

            using (response)
            {
                string body;

                try
                {
                    body = await response.Content.ReadAsStringAsync(clock.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    return (null, $"{label} did not finish answering within {(int)RequestTimeout.TotalSeconds} s.");
                }

                if (response.IsSuccessStatusCode)
                    return (body, null);

                string status = $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim();

                if (ErrorIn(body) is string said)
                    return (null, $"{label} answered {status}: {said}");

                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    return (null, $"{label} answered {status}. The key may be wrong, or not allowed to use this model.");

                return (null, $"{label} answered {status}.");
            }
        }

        ///
        ///The message in an error body, or null when there is none to read.
        ///
        ///Every provider here puts it at `error.message`. Google's OpenAI-compatible endpoint sometimes wraps the
        ///object in an array - `[{ "error": ... }]` - which is read through.
        ///
        internal static string? ErrorIn(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var document = JsonDocument.Parse(body);

                var root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                    root = root[0];

                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("error", out var error))
                    return null;

                if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    return message.GetString();

                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString();

                return error.ToString();
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    ///
    ///Whether a run of tool calls is getting anywhere (2026-11-01): what tells a model at work from a stuck one, where
    ///a count of rounds could not - see <see cref="LlmProviderFactory.DefaultToolRounds"/>.
    ///
    ///**A round gets somewhere when one of its calls worked and was not a repeat**: a call already made, with the same
    ///arguments, that answered the same - the same text and the same picture. A look after a change is not a repeat,
    ///because the picture is not the same; a second look at nothing new is. A refusal is never progress, a declined
    ///change included.
    ///
    public sealed class ToolProgress
    {
        private readonly HashSet<string> seen = new();

        private int stuckRounds;

        ///<summary>Counts one round's calls, and says whether the run has now gone <see cref="LlmProviderFactory.MaxStuckRounds"/> rounds in a row without getting anywhere.</summary>
        public bool IsStuckAfter(IReadOnlyList<(LlmToolCall Call, LlmToolOutput Output)> round)
        {
            bool anythingNew = false;

            foreach (var (call, output) in round)
            {
                //A declined call is a result to the model but did not run, so it is no more progress than a refusal.
                if (output.IsError || output.IsDeclined)
                    continue;

                string picture = "";

                if (output.Png is string png)
                    picture = $"{png.Length}:{png.GetHashCode()}";

                if (seen.Add(string.Join('\u0000', call.Name, call.ArgumentsJson, output.Text, picture)))
                    anythingNew = true;
            }

            if (anythingNew)
                stuckRounds = 0;
            else
                stuckRounds++;

            return stuckRounds >= LlmProviderFactory.MaxStuckRounds;
        }
    }
}
