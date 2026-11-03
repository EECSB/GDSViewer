using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GdsII.Cli
{
    ///
    ///Puts an <see cref="McpSession"/> on the Model Context Protocol: its tools listed, its calls answered.
    ///
    ///**Transport and nothing else.** The tools, their schemas and every answer are the session's - and for
    ///the thirty-one that draw, edit and check, the library's - so what a model is told here is word for word
    ///what the viewer's AI assistant is told.
    ///
    ///**The low-level handlers rather than the SDK's attributed methods.** Those build a tool from a C# method
    ///and its parameters; these tools already exist, with schemas written from the same description their
    ///arguments are read against, and turning each into a method would be a second description of it.
    ///
    public static class McpServing
    {
        ///<summary>The server's name, version and instructions, and the two handlers that are all it answers.</summary>
        public static McpServerOptions Options(McpSession session)
        {
            var tools = new List<Tool>();

            foreach (var tool in session.Tools)
                tools.Add(toProtocol(tool));

            return new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "gds", Title = "gds - GDSII, OASIS and DXF layouts", Version = Cli.Version },
                ServerInstructions = session.Instructions,
                Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
                Handlers = new McpServerHandlers
                {
                    ListToolsHandler = (request, cancel) => ValueTask.FromResult(new ListToolsResult { Tools = tools }),
                    CallToolHandler = async (request, cancel) => await callAsync(session, request.Params, clientOf(request.Server), cancel)
                }
            };
        }

        ///<summary>Serves the session on a transport until the client closes it or <paramref name="cancel"/> fires.</summary>
        public static async Task RunAsync(McpSession session, ITransport transport, CancellationToken cancel)
        {
            await using var server = McpServer.Create(transport, Options(session));

            await server.RunAsync(cancel);
        }

        ///
        ///One tool as the protocol describes it, with what it does said in the annotations a client reads to
        ///decide what to ask the person before calling it.
        ///
        ///**Closed world, all of them**: nothing here reaches past the folder. Only the file tools that can lose
        ///something are destructive. An edit is not, though it changes the layout - it is in memory, undoable,
        ///and nothing on disk moves until save_file.
        ///
        private static Tool toProtocol(ServedTool tool)
        {
            return new Tool
            {
                Name = tool.Name,
                Description = tool.Description,
                InputSchema = elementOf(tool.InputSchema),
                Annotations = new ToolAnnotations
                {
                    ReadOnlyHint = tool.ReadsOnly,
                    DestructiveHint = tool.Destroys,
                    OpenWorldHint = false
                }
            };
        }

        ///
        ///Answers one call. The session says what went wrong with anything the model did; anything else - a
        ///fault of ours - is said the same way rather than as a protocol error, so the model reads it and can
        ///try another way instead of the client reporting a broken server.
        ///
        private static async Task<CallToolResult> callAsync(McpSession session, CallToolRequestParams? asked, string client, CancellationToken cancel)
        {
            if (asked is null)
                return answer("A tool call has to say which tool.", true);

            try
            {
                var result = await session.CallAsync(asked.Name, argumentsOf(asked.Arguments), client, cancel);

                return answer(result.Text, result.IsError, result.Png);
            }
            catch (Exception failed)
            {
                return answer($"{asked.Name} failed: {failed.Message}", true);
            }
        }

        ///<summary>The text, and the picture after it when a linked tab drew one - which the model is shown, not told about.</summary>
        private static CallToolResult answer(string text, bool isError, byte[]? png = null)
        {
            var content = new List<ContentBlock> { new TextContentBlock { Text = text } };

            if (png is not null)
                content.Add(ImageContentBlock.FromBytes(png, "image/png"));

            return new CallToolResult
            {
                Content = content,
                IsError = isError
            };
        }

        ///<summary>The client a call came from, as the tab it links is told: its title, or its name, as it introduced itself.</summary>
        private static string clientOf(McpServer server)
        {
            var info = server.ClientInfo;

            if (info?.Title is string title && title.Length > 0)
                return title;

            if (info?.Name is string name && name.Length > 0)
                return name;

            return "an MCP client";
        }

        ///<summary>The arguments as the one JSON object the session reads - or nothing, when the call sent none.</summary>
        private static JsonElement argumentsOf(IDictionary<string, JsonElement>? given)
        {
            if (given is null)
                return default;

            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();

                foreach (var (name, value) in given)
                {
                    writer.WritePropertyName(name);
                    value.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            using var document = JsonDocument.Parse(buffer.WrittenMemory);

            return document.RootElement.Clone();
        }

        private static JsonElement elementOf(JsonObject schema)
        {
            using var document = JsonDocument.Parse(schema.ToJsonString());

            return document.RootElement.Clone();
        }
    }
}
