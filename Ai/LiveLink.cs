using System.Buffers;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GDSViewer.Ai
{
    ///<summary>Where a live link is: not started, waiting for gds mcp to link it, linked, or ended by somebody.</summary>
    public enum LiveLinkState
    {
        Off,
        Waiting,
        Linked,
        Ended
    }

    ///<summary>One call that came over the link, and what this tab answered - for the window's list of them.</summary>
    public sealed class LiveLinkCall
    {
        public required string Name { get; init; }

        public required string ArgumentsJson { get; init; }

        ///<summary>The answer, or null while it is being worked out.</summary>
        public string? Text { get; set; }

        public bool IsError { get; set; }
    }

    ///
    ///This tab's end of a live link to `gds mcp`: a code to show, a WebSocket to 127.0.0.1, and every call that comes
    ///down it answered on the file open here - see GdsII.Cli's TabLink for the other end and what is said between them.
    ///
    ///**The tab calls, because a page cannot listen.** Started, it dials the port until something answers - gds mcp
    ///listens from when its Claude window starts it - says hello with the code, is told it is waiting, and waits.
    ///Nothing it is sent is acted on until it is told it is linked, which is the server saying the person gave a
    ///model that code.
    ///
    ///**A link that ends stays ended.** The code is spent and the next link is a new Start, with a new one: a tab that
    ///went back to waiting by itself after a link would be a tab offering itself to whatever said hello next.
    ///
    ///**Calls are answered one at a time, in the order they come** - the server sends them that way, and the layout is
    ///one object with one undo history. What answers them is the page's, handed in as a function, so this is testable
    ///against the real server without a browser.
    ///
    public sealed class LiveLink : IAsyncDisposable
    {
        ///<summary>The port gds mcp listens on unless started with --link-port.</summary>
        public const int DefaultPort = 43707;

        ///<summary>How many calls the window lists. The oldest go first.</summary>
        public const int MostCalls = 200;

        ///<summary>How long between tries while nothing answers on the port: a short while at first, then less often.</summary>
        private static readonly TimeSpan FirstPause = TimeSpan.FromSeconds(1);

        private static readonly TimeSpan LongestPause = TimeSpan.FromSeconds(8);

        ///
        ///The words gds mcp closes a waiting tab with when it is one too many - TabLink.TurnedAwayReason, which this
        ///cannot share, since the app does not reference the CLI. A tab closed with them ends rather than dialing
        ///again: dialing again made it one too many once more, and the next-oldest was let go for it, and dialed
        ///again - nine tabs taking turns at being turned away.
        ///
        public const string TurnedAwayReason = "Too many tabs were waiting.";

        ///<summary>The most a call from the server may be. A call is a tool's name and its arguments.</summary>
        private const int MostCall = 4 * 1024 * 1024;

        private readonly Func<string, string, CancellationToken, Task<LlmToolOutput>> answer;

        ///<summary>The run in progress, which a Start replaces and a Stop ends. Each run checks it is still the one before it says anything.</summary>
        private CancellationTokenSource? running;

        private ClientWebSocket? socket;

        ///<summary>One message out at a time: an answer going out and a Stop's goodbye must not cross.</summary>
        private readonly SemaphoreSlim sending = new SemaphoreSlim(1, 1);

        ///<param name="answer">Answers one call - a tool's name and its arguments as JSON - on the file open here.</param>
        public LiveLink(Func<string, string, CancellationToken, Task<LlmToolOutput>> answer)
        {
            this.answer = answer;
        }

        public LiveLinkState State { get; private set; } = LiveLinkState.Off;

        ///<summary>The code to tell the model, as it is shown: two groups of four digits.</summary>
        public string Code { get; private set; } = "";

        public int Port { get; private set; } = DefaultPort;

        ///<summary>The MCP client the link is to, as gds mcp said it introduced itself - "Claude Code", say.</summary>
        public string Client { get; private set; } = "";

        ///<summary>Why the last link ended.</summary>
        public string Ending { get; private set; } = "";

        ///
        ///What is odd about the wait, while there is something: nothing on the port has said it is gds mcp within
        ///<see cref="HeardWithin"/> of the hello. gds mcp answers a hello with a word since 2026-11-03; an older one
        ///says nothing until it links, and so does anything else on the port that takes a WebSocket and keeps quiet
        ///- which a tab waited on for ever, with nothing to show for it. The wait goes on, since it may be the older
        ///gds mcp; this says why it may not end.
        ///
        public string Notice { get; private set; } = "";

        ///<summary>How long after the hello something has to answer before <see cref="Notice"/> says nothing has.</summary>
        public TimeSpan HeardWithin { get; init; } = TimeSpan.FromSeconds(15);

        ///<summary>How many times this has been started, so the page can tell a new link from the one it was answering.</summary>
        public int Started { get; private set; }

        ///<summary>The calls this link has answered, oldest first.</summary>
        public List<LiveLinkCall> Calls { get; } = new List<LiveLinkCall>();

        ///<summary>The name of the file open here, said in the hello so gds mcp can tell the model what it linked.</summary>
        public Func<string>? FileName { get; set; }

        ///<summary>Raised whenever anything shown changes - the state, the client, a call or its answer.</summary>
        public event Func<Task>? Changed;

        ///
        ///Starts waiting for gds mcp on <paramref name="port"/> with a new code, ending whatever link there was.
        ///
        public void Start(int port)
        {
            running?.Cancel();

            var mine = new CancellationTokenSource();

            running = mine;

            Port = port;
            Code = NewCode();
            Client = "";
            Ending = "";
            Notice = "";
            State = LiveLinkState.Waiting;
            Started++;

            Calls.Clear();

            _ = runAsync(mine);
        }

        ///<summary>Ends the link, or the wait for one, saying goodbye to gds mcp so it knows the tab went rather than fell over.</summary>
        public async Task StopAsync()
        {
            var was = running;

            running = null;

            if (socket is ClientWebSocket open && open.State == WebSocketState.Open)
                await goodbyeAsync(open, "Stopped in the tab.");

            was?.Cancel();

            if (State == LiveLinkState.Off)
                return;

            State = LiveLinkState.Off;
            Client = "";

            await changed();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }

        ///<summary>A new code: eight digits from the browser's own random source, shown in two fours.</summary>
        public static string NewCode()
        {
            string digits = RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8", CultureInfo.InvariantCulture);

            return digits.Substring(0, 4) + " " + digits.Substring(4);
        }

        #region The run *********************************************************************

        ///
        ///Dials until something answers, then serves it until it closes. Before a link that is only waiting again - the
        ///server went, or turned the tab away - and after one it is the end.
        ///
        private async Task runAsync(CancellationTokenSource mine)
        {
            var cancel = mine.Token;
            var pause = FirstPause;

            while (!cancel.IsCancellationRequested)
            {
                var trying = new ClientWebSocket();
                string? closing = null;

                try
                {
                    await trying.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}/"), cancel);

                    socket = trying;
                    pause = FirstPause;

                    await sendAsync(trying, helloMessage(), cancel);

                    closing = await serveAsync(trying, mine);
                }
                catch (Exception) { }
                finally
                {
                    if (ReferenceEquals(socket, trying))
                        socket = null;

                    trying.Dispose();
                }

                if (cancel.IsCancellationRequested || !ReferenceEquals(running, mine))
                    return;

                if (State == LiveLinkState.Linked)
                {
                    State = LiveLinkState.Ended;
                    Client = "";
                    Ending = closing ?? "gds mcp went away - the Claude window that started it closed, or restarted it.";
                    running = null;

                    await changed();

                    return;
                }

                //Let go for being one tab too many: the end, rather than the ninth tab again - see TurnedAwayReason.
                if (closing == TurnedAwayReason)
                {
                    State = LiveLinkState.Ended;
                    Ending = $"{closing} Press Start again when fewer tabs are waiting on gds mcp, or press Stop in one of them.";
                    running = null;

                    await changed();

                    return;
                }

                try
                {
                    await Task.Delay(pause, cancel);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                pause = TimeSpan.FromTicks(Math.Min(pause.Ticks * 2, LongestPause.Ticks));
            }
        }

        private string helloMessage()
        {
            var hello = new JsonObject
            {
                ["type"] = "hello",
                ["app"] = "GDS Viewer",
                ["code"] = Code.Replace(" ", ""),
                ["file"] = FileName?.Invoke() ?? ""
            };

            return hello.ToJsonString();
        }

        ///<summary>The server's messages until it closes, and the reason it gave for closing if it gave one.</summary>
        private async Task<string?> serveAsync(ClientWebSocket open, CancellationTokenSource mine)
        {
            //
            //**The first message is waited for with a clock on it** - see Notice. The receive goes on past the
            //clock rather than being given up: cancelling a receive aborts the socket, and the quiet thing on the
            //port may be an older gds mcp, which links this tab the moment the person gives its code.
            //
            var receiving = receiveAsync(open, mine.Token);

            if (await Task.WhenAny(receiving, Task.Delay(HeardWithin, mine.Token)) != receiving && ReferenceEquals(running, mine))
            {
                Notice = $"Nothing on port {Port} has said it is gds mcp in {HeardWithin.TotalSeconds:0} seconds. An older gds mcp says nothing until it links; "
                    + "anything else on the port never will. Still waiting.";

                await changed();
            }

            while (true)
            {
                string? message = await receiving;

                if (message is null)
                    return open.CloseStatusDescription;

                //Something answered, whatever it said: the wait is an ordinary one again.
                if (Notice.Length > 0)
                {
                    Notice = "";

                    await changed();
                }

                await handleAsync(open, message, mine);

                receiving = receiveAsync(open, mine.Token);
            }
        }

        private async Task handleAsync(ClientWebSocket open, string message, CancellationTokenSource mine)
        {
            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(message);
            }
            catch (JsonException)
            {
                return;
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object || !ReferenceEquals(running, mine))
                    return;

                string? type = text(root, "type");

                if (type == "linked")
                {
                    Client = text(root, "client") ?? "";
                    State = LiveLinkState.Linked;

                    await changed();

                    return;
                }

                //Nothing is answered before the link is made: until then the tab is only offering itself.
                if (type != "call" || State != LiveLinkState.Linked)
                    return;

                if (!root.TryGetProperty("id", out var number) || !number.TryGetInt32(out int id))
                    return;

                string name = text(root, "name") ?? "";
                string arguments = "{}";

                if (root.TryGetProperty("arguments", out var given) && given.ValueKind == JsonValueKind.Object)
                    arguments = given.GetRawText();

                await callAsync(open, id, name, arguments, mine.Token);
            }
        }

        ///<summary>One call: listed, answered by the page, and the answer sent back - a failure here an answer that says so.</summary>
        private async Task callAsync(ClientWebSocket open, int id, string name, string arguments, CancellationToken cancel)
        {
            var call = new LiveLinkCall { Name = name, ArgumentsJson = arguments };

            Calls.Add(call);

            if (Calls.Count > MostCalls)
                Calls.RemoveAt(0);

            await changed();

            LlmToolOutput output;

            try
            {
                output = await answer(name, arguments, cancel);
            }
            catch (Exception failed) when (failed is not OperationCanceledException)
            {
                output = new LlmToolOutput { Text = $"{name} failed in the tab: {failed.Message}", IsError = true };
            }

            call.Text = output.Text;
            call.IsError = output.IsError;

            await changed();

            var reply = new JsonObject
            {
                ["type"] = "answer",
                ["id"] = id,
                ["text"] = output.Text,
                ["isError"] = output.IsError,
                ["png"] = output.Png
            };

            await sendAsync(open, reply.ToJsonString(), cancel);
        }

        private static string? text(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();

            return null;
        }

        #endregion **************************************************************************



        #region The socket ******************************************************************

        private async Task sendAsync(ClientWebSocket open, string message, CancellationToken cancel)
        {
            await sending.WaitAsync(cancel);

            try
            {
                await open.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, cancel);
            }
            finally
            {
                sending.Release();
            }
        }

        private async Task goodbyeAsync(ClientWebSocket open, string reason)
        {
            if (!await sending.WaitAsync(TimeSpan.FromSeconds(2)))
                return;

            try
            {
                using var hurry = new CancellationTokenSource(TimeSpan.FromSeconds(2));

                await open.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, reason, hurry.Token);
            }
            catch (Exception) { }
            finally
            {
                sending.Release();
            }
        }

        ///<summary>One whole message as text, or null when the server closed.</summary>
        private static async Task<string?> receiveAsync(ClientWebSocket open, CancellationToken cancel)
        {
            var buffer = new ArrayBufferWriter<byte>();

            while (true)
            {
                var result = await open.ReceiveAsync(buffer.GetMemory(16 * 1024), cancel);

                if (result.MessageType == WebSocketMessageType.Close)
                    return null;

                buffer.Advance(result.Count);

                if (buffer.WrittenCount > MostCall)
                    throw new InvalidDataException($"A message over {MostCall} bytes.");

                if (result.EndOfMessage)
                    break;
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private async Task changed()
        {
            if (Changed is null)
                return;

            foreach (var listener in Changed.GetInvocationList())
                await ((Func<Task>)listener)();
        }

        #endregion **************************************************************************
    }
}
