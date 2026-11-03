using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GDSViewer.Ai;
using GdsII;
using GdsII.Cli;

namespace GDSViewer.Tests;

///
///The live link: a GDS Viewer tab and `gds mcp`, joined over a WebSocket by a code.
///
///**Both ends are the real ones, in one process.** <see cref="TabLink"/> listens as `gds mcp` does, and the app's own
///<see cref="LiveLink"/> dials it as the tab does - .NET's WebSocket where the browser's is, which is the same
///protocol. What only a browser can show - the page's own socket, the drawing, the card - is live-link.spec.js.
///
///Each test listens on a port of its own, found free, so a `gds mcp` somebody has running is never the one found.
///
public class LiveLinkTests : IDisposable
{
    #region Helpers *******************************************************************

    private readonly string folder;

    public LiveLinkTests()
    {
        folder = Path.Combine(Path.GetTempPath(), $"gdslink-{Guid.NewGuid():N}");

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Mosfet.gds"), GdsTestData.ReadSample(GdsTestData.MosfetSample));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch { }
    }

    internal static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        int port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private static async Task Until(Func<bool> condition, string what, int milliseconds = 10000)
    {
        var giveUp = DateTime.UtcNow.AddMilliseconds(milliseconds);

        while (!condition())
        {
            if (DateTime.UtcNow > giveUp)
                Assert.Fail($"Waited {milliseconds} ms for {what}.");

            await Task.Delay(20);
        }
    }

    ///<summary>A tab answering every call with what it was asked, the way a page answers with what it did.</summary>
    private static LiveLink Echoing(string? png = null, TimeSpan? heardWithin = null)
    {
        return new LiveLink((name, arguments, cancel) => Task.FromResult(new LlmToolOutput { Text = $"{name} {arguments}", Png = png }))
        {
            FileName = () => "chip.gds",
            HeardWithin = heardWithin ?? TimeSpan.FromSeconds(15)
        };
    }

    ///<summary>A listening server and a tab waiting on it - the moment after the person pressed Start.</summary>
    private static async Task<(TabLink Server, LiveLink Tab)> Waiting(LiveLink? tab = null)
    {
        var server = new TabLink(FreePort());

        Assert.True(server.TryListen(out string why), why);

        tab ??= Echoing();
        tab.Start(server.Port);

        await Until(() => server.Waiting == 1, "the tab to say hello");

        return (server, tab);
    }

    private static async Task<(TabLink Server, LiveLink Tab)> Linked(LiveLink? tab = null)
    {
        var (server, waiting) = await Waiting(tab);

        Assert.Equal("chip.gds", await server.LinkAsync(waiting.Code, "Test client", TimeSpan.FromSeconds(5), CancellationToken.None));

        await Until(() => waiting.State == LiveLinkState.Linked, "the tab to be told it is linked");

        return (server, waiting);
    }

    private static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);

        return document.RootElement.Clone();
    }

    #endregion ***********************************************************************



    #region Codes *********************************************************************

    [Fact]
    public void A_code_is_eight_digits_shown_in_two_fours()
    {
        for (int i = 0; i < 50; i++)
            Assert.Matches(@"^\d{4} \d{4}$", LiveLink.NewCode());
    }

    ///<summary>However the model writes it - spaced, dashed, run together - it is the same code.</summary>
    [Theory]
    [InlineData("4821 7356")]
    [InlineData("4821-7356")]
    [InlineData("48217356")]
    [InlineData(" 4821  7356 ")]
    public void A_code_is_its_digits_whatever_is_between_them(string written)
    {
        Assert.Equal("48217356", TabLink.Digits(written));
    }

    #endregion ***********************************************************************



    #region Linking *******************************************************************

    ///
    ///The whole of it: the tab says hello, the code links it, it is told who it is linked to, and a call goes down and
    ///its answer comes back - and is in the tab's list.
    ///
    [Fact]
    public async Task The_tab_that_offered_the_code_is_linked_and_answers_calls()
    {
        var (server, tab) = await Linked();

        await using (server)
        await using (tab)
        {
            Assert.Equal("Test client", tab.Client);
            Assert.True(server.IsLinked);
            Assert.Equal(0, server.Waiting);

            var answer = await server.CallAsync("list_layers", Json("""{"cell": "TOP"}"""), TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal("""list_layers {"cell":"TOP"}""", answer.Text);
            Assert.False(answer.IsError);

            var listed = Assert.Single(tab.Calls);

            Assert.Equal("list_layers", listed.Name);
            Assert.Equal(answer.Text, listed.Text);
        }
    }

    [Fact]
    public async Task A_picture_comes_back_whole()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        var (server, tab) = await Linked(Echoing(Convert.ToBase64String(png)));

        await using (server)
        await using (tab)
        {
            var answer = await server.CallAsync("look", default, TimeSpan.FromSeconds(5), CancellationToken.None);

            Assert.Equal(png, answer.Png);
        }
    }

    ///<summary>A code read wrong while the right tab waits is the likeliest mistake there is, and is said at once rather than in twenty seconds.</summary>
    [Fact]
    public async Task A_wrong_code_is_refused_at_once_while_a_tab_waits_with_another()
    {
        var (server, tab) = await Waiting();

        await using (server)
        await using (tab)
        {
            var started = DateTime.UtcNow;

            var refused = await Assert.ThrowsAsync<LinkRefusal>(() => server.LinkAsync("0000 0000", "Test client", TimeSpan.FromSeconds(20), CancellationToken.None));

            Assert.Contains("No GDS Viewer tab offered 0000 0000, and a tab is waiting with another code", refused.Message);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
            Assert.Equal(LiveLinkState.Waiting, tab.State);
        }
    }

    [Fact]
    public async Task With_no_tab_waiting_it_waits_and_then_says_how_to_start_one()
    {
        await using var server = new TabLink(FreePort());

        var refused = await Assert.ThrowsAsync<LinkRefusal>(() => server.LinkAsync("4821 7356", "Test client", TimeSpan.FromMilliseconds(300), CancellationToken.None));

        Assert.Contains("Settings, AI settings", refused.Message);
        Assert.Contains($"port at {server.Port}", refused.Message);
    }

    ///<summary>A tab pressing Start after the code was given is found when it comes, within the wait.</summary>
    [Fact]
    public async Task A_tab_that_comes_while_link_tab_waits_is_found()
    {
        await using var server = new TabLink(FreePort());
        await using var tab = Echoing();

        Assert.True(server.TryListen(out _));

        tab.Start(server.Port);

        string code = tab.Code;

        //Stopped and started again with the same code would be a different tab; this is one tab, arriving late.
        Assert.Equal("chip.gds", await server.LinkAsync(code, "Test client", TimeSpan.FromSeconds(10), CancellationToken.None));
    }

    ///<summary>Two tabs offering one code is something guessing, or a mistake; either way neither is linked.</summary>
    [Fact]
    public async Task Two_tabs_offering_one_code_link_neither()
    {
        await using var server = new TabLink(FreePort());

        Assert.True(server.TryListen(out _));

        using var one = await Hello(server.Port, "1111 2222");
        using var two = await Hello(server.Port, "1111-2222");

        await Until(() => server.Waiting == 2, "both to say hello");

        var refused = await Assert.ThrowsAsync<LinkRefusal>(() => server.LinkAsync("11112222", "Test client", TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Contains("Two tabs offered that code", refused.Message);
        Assert.False(server.IsLinked);
    }

    ///<summary>A socket that has said hello with a code, as a tab would.</summary>
    private static async Task<ClientWebSocket> Hello(int port, string code)
    {
        var socket = new ClientWebSocket();

        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/"), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes($$"""{"type": "hello", "code": "{{code}}"}"""), WebSocketMessageType.Text, true, CancellationToken.None);

        return socket;
    }

    ///
    ///**The tab acts on nothing before it is told it is linked.** Until then it is only offering itself, and a server
    ///that sent a call first - which gds mcp never does - is answered with nothing. A stand-in server that does, by
    ///hand, since the real one cannot be made to.
    ///
    [Fact]
    public async Task The_tab_answers_no_call_before_it_is_told_it_is_linked()
    {
        var listening = new TcpListener(IPAddress.Loopback, 0);

        listening.Start();

        int port = ((IPEndPoint)listening.LocalEndpoint).Port;
        int answered = 0;

        await using var tab = new LiveLink((name, arguments, cancel) =>
        {
            Interlocked.Increment(ref answered);

            return Task.FromResult(new LlmToolOutput { Text = "done" });
        });

        tab.Start(port);

        using var client = await listening.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var socket = await AcceptAsServer(client.GetStream());

        var buffer = new byte[4096];

        await socket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await Send(socket, """{"type": "call", "id": 1, "name": "delete", "arguments": {}}""");
        await Task.Delay(500);

        Assert.Equal(0, answered);
        Assert.Equal(LiveLinkState.Waiting, tab.State);

        await Send(socket, """{"type": "linked", "client": "Stand-in"}""");
        await Send(socket, """{"type": "call", "id": 2, "name": "list_layers", "arguments": {}}""");

        var reply = await socket.ReceiveAsync(buffer, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("\"id\":2", Encoding.UTF8.GetString(buffer, 0, reply.Count));
        Assert.Equal(1, answered);

        listening.Stop();
    }

    ///<summary>The server's half of the opening handshake, written out as RFC 6455 has it.</summary>
    private static async Task<WebSocket> AcceptAsServer(NetworkStream stream)
    {
        var head = new StringBuilder();
        var one = new byte[1];

        while (!head.ToString().EndsWith("\r\n\r\n"))
        {
            await stream.ReadExactlyAsync(one);

            head.Append((char)one[0]);
        }

        string key = head.ToString().Split("\r\n")
            .First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
            .Substring("Sec-WebSocket-Key:".Length)
            .Trim();

        string accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));

        return WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true });
    }

    private static async Task Send(WebSocket socket, string message)
    {
        await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    [Fact]
    public async Task A_code_that_is_not_eight_digits_is_refused_as_one()
    {
        await using var server = new TabLink(FreePort());

        var refused = await Assert.ThrowsAsync<LinkRefusal>(() => server.LinkAsync("4821", "Test client", TimeSpan.FromSeconds(5), CancellationToken.None));

        Assert.Contains("8 digits", refused.Message);
    }

    #endregion ***********************************************************************



    #region Ending ********************************************************************

    ///<summary>Stop in the tab ends it for the server too, and the server keeps why, to say on the model's next call.</summary>
    [Fact]
    public async Task Stop_in_the_tab_ends_the_link_and_keeps_why_for_the_next_call()
    {
        var (server, tab) = await Linked();

        await using (server)
        {
            await tab.StopAsync();

            await Until(() => !server.IsLinked, "the server to see the tab go");

            Assert.Equal(LiveLinkState.Off, tab.State);
            Assert.Contains("closed the link", server.TakeEnding());
            Assert.Null(server.TakeEnding());
        }
    }

    ///<summary>Unlinked by the model, the tab shows why - and stays ended: the code is spent.</summary>
    [Fact]
    public async Task Unlinking_ends_the_tabs_link_with_the_reason_and_it_stays_ended()
    {
        var (server, tab) = await Linked();

        await using (server)
        await using (tab)
        {
            Assert.True(await server.UnlinkAsync());

            await Until(() => tab.State == LiveLinkState.Ended, "the tab to see the link end");

            Assert.Equal("Claude ended the link.", tab.Ending);
            Assert.Null(server.TakeEnding());

            //Not offering itself again: a tab back to waiting by itself would be offered to whatever said hello next.
            //Longer than the tab's first pause between tries, so one that went back to dialing would be here by now.
            await Task.Delay(1500);

            Assert.Equal(0, server.Waiting);
        }
    }

    ///<summary>A tab going while a call waits on it is said at once, as a refusal - not two minutes of waiting.</summary>
    [Fact]
    public async Task A_tab_that_goes_mid_call_is_said_rather_than_waited_for()
    {
        var answering = new TaskCompletionSource<LlmToolOutput>();
        var tab = new LiveLink((name, arguments, cancel) => answering.Task) { FileName = () => "chip.gds" };

        var (server, linked) = await Linked(tab);

        await using (server)
        {
            var call = server.CallAsync("draw_rectangle", default, TimeSpan.FromMinutes(2), CancellationToken.None);

            await Until(() => linked.Calls.Count == 1, "the call to reach the tab");
            await linked.StopAsync();

            var refused = await Assert.ThrowsAsync<LinkRefusal>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.Contains("before it answered", refused.Message);

            answering.SetResult(new LlmToolOutput { Text = "too late" });
        }
    }

    ///<summary>The person starting the link again in another tab, and giving that code, moves the link there.</summary>
    [Fact]
    public async Task Linking_another_tab_ends_the_first_ones_link()
    {
        var (server, first) = await Linked();

        await using (server)
        await using (first)
        await using (var second = Echoing())
        {
            second.Start(server.Port);

            await Until(() => server.Waiting == 1, "the second tab to say hello");

            await server.LinkAsync(second.Code, "Test client", TimeSpan.FromSeconds(5), CancellationToken.None);

            await Until(() => first.State == LiveLinkState.Ended, "the first tab to be let go");

            Assert.Equal("Another tab was linked.", first.Ending);

            //LinkAsync is back once the server has said it; the tab hears it a moment later (red on a 2-core runner).
            await Until(() => second.State == LiveLinkState.Linked, "the second tab to be linked");
        }
    }

    #endregion ***********************************************************************



    #region The port ******************************************************************

    ///<summary>Anything on the port that is not a WebSocket asking to open is told so and closed.</summary>
    [Fact]
    public async Task A_request_that_is_not_a_websocket_is_turned_away()
    {
        await using var server = new TabLink(FreePort());

        Assert.True(server.TryListen(out _));

        using var client = new TcpClient();

        await client.ConnectAsync(IPAddress.Loopback, server.Port);

        var stream = client.GetStream();

        await stream.WriteAsync(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));

        var buffer = new byte[256];
        int read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.StartsWith("HTTP/1.1 400", Encoding.ASCII.GetString(buffer, 0, read));
    }

    ///<summary>A socket that opens and says something other than hello with a code is closed, and never waits to be linked.</summary>
    [Fact]
    public async Task A_socket_that_says_no_hello_is_closed()
    {
        await using var server = new TabLink(FreePort());

        Assert.True(server.TryListen(out _));

        using var socket = new ClientWebSocket();

        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/"), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("""{"type": "hello", "code": "48"}"""), WebSocketMessageType.Text, true, CancellationToken.None);

        var result = await socket.ReceiveAsync(new byte[256], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, result.CloseStatus);
        Assert.Equal(0, server.Waiting);
    }

    ///<summary>One port, and the first gds mcp up has it: the second says why, rather than failing to start.</summary>
    [Fact]
    public async Task A_port_another_server_has_is_said_to_be_taken()
    {
        await using var first = new TabLink(FreePort());
        await using var second = new TabLink(first.Port);

        Assert.True(first.TryListen(out _));
        Assert.False(second.TryListen(out string why));
        Assert.Contains("taken", why);
    }

    #endregion ***********************************************************************



    #region Through gds mcp ***********************************************************

    private McpSession Session(int port)
    {
        return new McpSession(new ServedFolder(folder), linkPort: port);
    }

    private static async Task<ServedAnswer> Ask(McpSession session, string tool, string arguments = "{}")
    {
        return await session.CallAsync(tool, Json(arguments), "Test client", CancellationToken.None);
    }

    ///
    ///A tab as the page is one: the library's tools over a layout of its own, and describe_tab answered as the page
    ///answers it. What it is asked to draw lands in its layout and not in the folder's.
    ///
    private static (LiveLink Tab, AgentSession Layout) TabWithLayout()
    {
        var layout = new AgentSession(GDS.NewLibrary(), new EditHistory());

        var tab = new LiveLink((name, arguments, cancel) =>
        {
            if (name == "describe_tab")
                return Task.FromResult(new LlmToolOutput { Text = "The layout:\n- The file is tab.gds." });

            var result = AgentTools.Call(layout, name, arguments);

            return Task.FromResult(new LlmToolOutput { Text = result.Text, IsError = result.IsError });
        })
        {
            FileName = () => "tab.gds"
        };

        return (tab, layout);
    }

    [Fact]
    public async Task Linked_the_layout_tools_work_on_the_tab_and_the_file_tools_wait()
    {
        var session = Session(FreePort());
        var (tab, layout) = TabWithLayout();

        await using (session.Link!)
        await using (tab)
        {
            Assert.False((await Ask(session, "open_file", """{"path": "Mosfet.gds"}""")).IsError);

            Assert.True(session.Link!.TryListen(out _));

            tab.Start(session.Link.Port);

            await Until(() => session.Link.Waiting == 1, "the tab to say hello");

            var linked = await Ask(session, "link_tab", $$"""{"code": "{{tab.Code}}"}""");

            Assert.False(linked.IsError, linked.Text);
            Assert.StartsWith("Linked to the GDS Viewer tab with tab.gds open.", linked.Text);
            Assert.Contains("The file is tab.gds.", linked.Text);

            var drawn = await Ask(session, "draw_rectangle", """{"layer": "68/20", "from": [0, 0], "to": [2, 1]}""");

            Assert.False(drawn.IsError, drawn.Text);
            Assert.StartsWith("Drew a 2 by 1 µm rectangle", drawn.Text);
            Assert.Single(layout.Gds.StreamFormat.Structures[0].Elements);
            Assert.False(session.Unsaved);

            var saving = await Ask(session, "save_file");

            Assert.True(saving.IsError);
            Assert.Contains("save_file waits", saving.Text);

            Assert.False((await Ask(session, "list_files")).IsError);

            var unlinked = await Ask(session, "unlink_tab");

            Assert.Contains("work on the folder's open layout again, Mosfet.gds", unlinked.Text);

            //The folder's again: the same call lands in the folder's layout now, and the tab's has one shape still.
            Assert.False((await Ask(session, "draw_rectangle", """{"layer": "68/20", "from": [0, 0], "to": [2, 1]}""")).IsError);
            Assert.True(session.Unsaved);
            Assert.Single(layout.Gds.StreamFormat.Structures[0].Elements);
        }
    }

    ///
    ///The tab going is said on the next call, which is not run - not quietly drawn into the folder's layout, which is
    ///not the one the model was thinking of. The call after that is the folder's.
    ///
    [Fact]
    public async Task When_the_tab_goes_the_next_call_is_refused_rather_than_sent_to_the_folder()
    {
        var session = Session(FreePort());
        var (tab, layout) = TabWithLayout();

        await using (session.Link!)
        {
            Assert.False((await Ask(session, "open_file", """{"path": "Mosfet.gds"}""")).IsError);
            Assert.True(session.Link!.TryListen(out _));

            tab.Start(session.Link.Port);

            await Until(() => session.Link.Waiting == 1, "the tab to say hello");

            Assert.False((await Ask(session, "link_tab", $$"""{"code": "{{tab.Code}}"}""")).IsError);

            await tab.StopAsync();

            await Until(() => !session.Link.IsLinked, "the server to see the tab go");

            var refused = await Ask(session, "draw_rectangle", """{"layer": "68/20", "from": [0, 0], "to": [2, 1]}""");

            Assert.True(refused.IsError);
            Assert.Contains("closed the link", refused.Text);
            Assert.Contains("draw_rectangle was not run", refused.Text);
            Assert.False(session.Unsaved);

            Assert.False((await Ask(session, "draw_rectangle", """{"layer": "68/20", "from": [0, 0], "to": [2, 1]}""")).IsError);
            Assert.True(session.Unsaved);
        }
    }

    [Fact]
    public async Task Look_without_a_linked_tab_says_how_to_link_one()
    {
        var session = Session(FreePort());

        await using (session.Link!)
        {
            var refused = await Ask(session, "look");

            Assert.True(refused.IsError);
            Assert.Contains("look needs a linked GDS Viewer tab", refused.Text);
            Assert.Contains("link_tab", refused.Text);
        }
    }

    [Fact]
    public async Task A_code_sent_as_a_number_is_read_as_one()
    {
        var session = Session(FreePort());
        var (tab, _) = TabWithLayout();

        await using (session.Link!)
        await using (tab)
        {
            Assert.True(session.Link!.TryListen(out _));

            tab.Start(session.Link.Port);

            await Until(() => session.Link.Waiting == 1, "the tab to say hello");

            var linked = await Ask(session, "link_tab", $$"""{"code": {{tab.Code.Replace(" ", "").TrimStart('0')}}}""");

            //A number loses the zeros it leads with, so one beginning with zero is refused as short - and said why.
            if (tab.Code.StartsWith('0'))
                Assert.Contains("8 digits", linked.Text);
            else
                Assert.False(linked.IsError, linked.Text);
        }
    }

    ///<summary>Listed with the rest unless the link is off, when there are no tools for it at all.</summary>
    [Fact]
    public void The_link_tools_are_listed_unless_the_link_is_off()
    {
        string[] linking = { "link_tab", "unlink_tab", "look", "describe_tab" };

        var on = new McpSession(new ServedFolder(folder)).Tools.Select(tool => tool.Name).ToList();
        var off = new McpSession(new ServedFolder(folder), linkPort: 0).Tools.Select(tool => tool.Name).ToList();

        Assert.All(linking, name => Assert.Contains(name, on));
        Assert.All(linking, name => Assert.DoesNotContain(name, off));
        Assert.Equal(on.Count - 4, off.Count);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("50123", 50123)]
    public void Link_port_on_the_command_line_sets_the_port_and_0_turns_it_off(string given, int port)
    {
        Assert.Equal(Cli.Ok, Cli.McpSessionFrom(new[] { folder, "--link-port", given }, TextWriter.Null, out var session));

        if (port == 0)
            Assert.Null(session!.Link);
        else
            Assert.Equal(port, session!.Link!.Port);
    }

    [Theory]
    [InlineData("70000")]
    [InlineData("-1")]
    [InlineData("port")]
    public void Link_port_that_is_no_port_is_refused(string given)
    {
        var error = new StringWriter();

        Assert.Equal(Cli.UsageError, Cli.McpSessionFrom(new[] { folder, "--link-port", given }, error, out _));
        Assert.Contains("--link-port", error.ToString());
    }

    #endregion ***********************************************************************

    #region What answers on the port **************************************************

    ///
    ///Something on a port that takes a WebSocket and says nothing: what an older gds mcp looks like to a tab until it
    ///links, and what anything else on the port looks like for ever.
    ///
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly List<TcpClient> held = new List<TcpClient>();

        public SilentServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0);

            listener.Start();

            Port = ((IPEndPoint)listener.LocalEndpoint).Port;

            _ = acceptAsync();
        }

        public int Port { get; }

        private async Task acceptAsync()
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();

                    held.Add(client);

                    _ = answerAsync(client);
                }
            }
            catch (Exception) { }
        }

        ///<summary>The opening handshake, and nothing after it.</summary>
        private static async Task answerAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                var read = new List<byte>();
                var one = new byte[1];

                while (true)
                {
                    if (await stream.ReadAsync(one) == 0)
                        return;

                    read.Add(one[0]);

                    int count = read.Count;

                    if (count >= 4 && read[count - 4] == '\r' && read[count - 3] == '\n' && read[count - 2] == '\r' && read[count - 1] == '\n')
                        break;
                }

                string head = Encoding.ASCII.GetString(read.ToArray());
                string key = head.Split("\r\n").First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase)).Split(':', 2)[1].Trim();
                string accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));

                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"));
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            listener.Stop();

            foreach (var client in held)
                client.Dispose();
        }
    }

    ///<summary>gds mcp says a tab is waiting the moment it has its hello, so a tab waiting on one has nothing to remark on.</summary>
    [Fact]
    public async Task A_tab_waiting_on_gds_mcp_has_nothing_to_remark_on()
    {
        var (server, tab) = await Waiting(Echoing(heardWithin: TimeSpan.FromMilliseconds(200)));

        await using (server)
        await using (tab)
        {
            await Task.Delay(700);

            Assert.Equal(LiveLinkState.Waiting, tab.State);
            Assert.Equal("", tab.Notice);
        }
    }

    ///
    ///Something else that takes the hello and says nothing is remarked on - and waited on still, since it may be an
    ///older gds mcp. A tab waited on it for ever with nothing to show for it until 2026-11-03.
    ///
    [Fact]
    public async Task A_port_that_takes_the_hello_and_says_nothing_is_remarked_on()
    {
        using var silent = new SilentServer();
        await using var tab = Echoing(heardWithin: TimeSpan.FromMilliseconds(200));

        tab.Start(silent.Port);

        await Until(() => tab.Notice.Length > 0, "the tab to remark on the silence");

        Assert.Contains("has said it is gds mcp", tab.Notice);
        Assert.Equal(LiveLinkState.Waiting, tab.State);
    }

    ///
    ///A ninth tab is let go - and ends, rather than dialing again. Dialing again made it the ninth once more, which let
    ///the next-oldest go, which dialed again: nine tabs taking turns at being turned away.
    ///
    [Fact]
    public async Task A_tab_let_go_for_being_one_too_many_ends_rather_than_dialing_again()
    {
        Assert.Equal(TabLink.TurnedAwayReason, LiveLink.TurnedAwayReason);

        await using var server = new TabLink(FreePort());

        Assert.True(server.TryListen(out _));

        var tabs = new List<LiveLink>();

        try
        {
            for (int i = 0; i <= TabLink.MostWaiting; i++)
            {
                var tab = Echoing();

                tab.Start(server.Port);
                tabs.Add(tab);

                await Until(() => server.Waiting == Math.Min(i + 1, TabLink.MostWaiting), "the tab to say hello");
            }

            await Until(() => tabs[0].State == LiveLinkState.Ended, "the first tab to be let go");

            Assert.StartsWith(LiveLink.TurnedAwayReason, tabs[0].Ending);

            //Long enough for a tab that dials again to have done so.
            await Task.Delay(1500);

            Assert.Equal(TabLink.MostWaiting, server.Waiting);
            Assert.Equal(LiveLinkState.Ended, tabs[0].State);
            Assert.All(tabs.Skip(1), tab => Assert.Equal(LiveLinkState.Waiting, tab.State));
        }
        finally
        {
            foreach (var tab in tabs)
                await tab.DisposeAsync();
        }
    }

    ///
    ///The link is made whatever describe_tab does after it. A tab that closed before answering it turned the whole
    ///answer into a refusal, and the model's retry was then told no tab was waiting - the tab had been linked by the
    ///call it was told failed.
    ///
    [Fact]
    public async Task A_link_is_made_even_when_the_tab_does_not_answer_describe_tab()
    {
        var session = new McpSession(new ServedFolder(folder), linkPort: FreePort());

        LiveLink? tab = null;

        tab = new LiveLink(async (name, arguments, cancel) =>
        {
            //Gone before it answers, which is the one way to make the call fail inside a test's patience.
            if (name == "describe_tab")
                await tab!.StopAsync();

            return new LlmToolOutput { Text = $"{name} answered" };
        })
        {
            FileName = () => "chip.gds"
        };

        var link = session.Link!;

        await using (tab)
        await using (link)
        {
            Assert.True(link.TryListen(out _));

            tab.Start(link.Port);

            await Until(() => link.Waiting == 1, "the tab to say hello");

            var answer = await session.CallAsync("link_tab", Json($$"""{"code": "{{tab.Code}}"}"""), "Test client", CancellationToken.None);

            Assert.False(answer.IsError);
            Assert.Contains("Linked to the GDS Viewer tab with chip.gds open", answer.Text);
            Assert.Contains("describe_tab did not answer yet", answer.Text);
        }
    }

    #endregion ***********************************************************************
}
