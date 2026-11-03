using System.Text;
using System.Text.Json;

namespace GdsII.Cli
{
    ///<summary>What a call is answered: the text the model reads, whether it is a refusal, and a picture when there is one.</summary>
    public sealed record ServedAnswer(string Text, bool IsError, byte[]? Png = null);

    ///
    ///The live link, as the session serves it: link_tab and unlink_tab, the two tools only a linked tab can answer -
    ///look and describe_tab - and every layout tool sent to the tab while one is linked.
    ///
    ///**What the model works on is said, never assumed.** Linked, the layout tools go to the person's tab; and when the
    ///tab goes - Stop pressed, the page reloaded - the next call is refused with why, rather than quietly drawn into
    ///the folder's file, which is not the one the model was thinking of. The call after that is the folder's again.
    ///
    ///**The file tools wait while a tab is linked**, list_files apart. The tab's file is the person's: they open it,
    ///save it and load its rules in the app, and a save_file would write the folder's layout - not what they are
    ///looking at.
    ///
    public sealed partial class McpSession
    {
        ///<summary>The live link, or null when `--link-port 0` turned it off.</summary>
        public TabLink? Link { get; }

        ///<summary>How long link_tab waits for a tab when none has said hello yet.</summary>
        public static readonly TimeSpan LinkWait = TimeSpan.FromSeconds(20);

        ///<summary>How long a call sent to a tab is waited for. A rule check of a large chip in a browser takes a while.</summary>
        public static readonly TimeSpan CallWithin = TimeSpan.FromMinutes(2);

        ///<summary>The MCP client the call came from, as link_tab tells the tab it is linked to.</summary>
        private string client = "";

        ///<summary>One call at a time across the awaits a linked call takes, as <see cref="gate"/> is for the rest.</summary>
        private readonly SemaphoreSlim turn = new SemaphoreSlim(1, 1);

        ///<summary>One of the live link's tools.</summary>
        private sealed class LinkTool
        {
            public required string Name { get; init; }

            public required string Description { get; init; }

            public required AgentParameter[] Parameters { get; init; }

            public bool ReadsOnly { get; init; }

            public required Func<McpSession, Given, CancellationToken, Task<ServedAnswer>> RunAsync { get; init; }
        }

        private static readonly LinkTool[] LinkTools =
        {
            new LinkTool
            {
                Name = "link_tab",
                Description = "Links these tools to a GDS Viewer tab, so they work on the layout the person has open in it, as they "
                    + "watch: each change is drawn as it is made, and is a step of the tab's own undo. The person starts it in GDS "
                    + "Viewer, under MCP server in Settings, AI settings, which shows a code - ask them for it. Waits up to 20 seconds for the tab. "
                    + "While linked, look shows the tab's 2D view, describe_tab says what the person has open and selected, and the "
                    + "file tools wait until unlink_tab.",
                Parameters = new[] { needs("code", AgentValue.Text, "The eight digits GDS Viewer shows under MCP server in its AI settings, like 4821 7356.") },
                RunAsync = linkTabAsync
            },
            new LinkTool
            {
                Name = "unlink_tab",
                Description = "Ends the link to the GDS Viewer tab. The layout tools work on the folder's open layout again.",
                Parameters = Array.Empty<AgentParameter>(),
                RunAsync = unlinkTabAsync
            },
            new LinkTool
            {
                //The library's declaration of it - the AI assistant's is the same one - with where the picture comes from here.
                Name = AgentTools.LookName,
                Description = AgentTools.LookDescription + " From a linked GDS Viewer tab, which is what draws it.",
                Parameters = new[] { AgentTools.LookArea },
                ReadsOnly = true,
                RunAsync = (session, given, cancel) => session.askTabAsync(AgentTools.LookName, given, cancel)
            },
            new LinkTool
            {
                Name = "describe_tab",
                Description = "What the person has open in the linked GDS Viewer tab: the file and its layers, the cell they are in, "
                    + "what they have selected and the part of it their 2D view shows. Call it when they say this, these, here or "
                    + "the selection.",
                Parameters = Array.Empty<AgentParameter>(),
                ReadsOnly = true,
                RunAsync = (session, given, cancel) => session.askTabAsync("describe_tab", given, cancel)
            }
        };

        ///
        ///Runs one call as the protocol sends it, the live link included: <see cref="Call"/> for everything else.
        ///
        ///<paramref name="calling"/> is the client the call came from, which the tab shows it is linked to.
        ///
        public async Task<ServedAnswer> CallAsync(string name, JsonElement arguments, string calling, CancellationToken cancel)
        {
            await turn.WaitAsync(cancel);

            try
            {
                client = calling;

                return await callAsync(name, arguments, cancel);
            }
            finally
            {
                turn.Release();
            }
        }

        private async Task<ServedAnswer> callAsync(string name, JsonElement arguments, CancellationToken cancel)
        {
            if (Link is not null)
            {
                foreach (var tool in LinkTools)
                {
                    if (tool.Name != name)
                        continue;

                    try
                    {
                        return await tool.RunAsync(this, new Given(tool.Name, tool.Parameters, arguments), cancel);
                    }
                    catch (McpRefusal refusal)
                    {
                        return refusedAnswer(refusal.Message);
                    }
                    catch (LinkRefusal refusal)
                    {
                        return refusedAnswer(refusal.Message);
                    }
                }

                if (Link.TakeEnding() is string ended)
                {
                    return refusedAnswer(
                        $"{ended} So {name} was not run. From the next call the tools work on the folder again; to work on the tab, ask the person "
                        + "to press Start under MCP server in its AI settings again and give you the new code.");
                }

                if (Link.IsLinked)
                {
                    if (AgentTools.Named(name) is not null)
                        return await toTabAsync(name, arguments, cancel);

                    if (FileTools.Any(file => file.Name == name && file.Name != "list_files"))
                    {
                        return refusedAnswer(
                            $"These tools are linked to the GDS Viewer tab, so {name} waits: the tab's file is the person's, to open, save and load rules "
                            + "for in the app. unlink_tab works on the folder again.");
                    }
                }
            }

            var result = Call(name, arguments);

            return new ServedAnswer(result.Text, result.IsError);
        }

        private static async Task<ServedAnswer> linkTabAsync(McpSession session, Given given, CancellationToken cancel)
        {
            var link = session.Link!;

            string file = await link.LinkAsync(given.TextOrNumber("code"), session.client, LinkWait, cancel);

            var said = new StringBuilder("Linked to the GDS Viewer tab");

            if (file.Length > 0)
                said.Append($" with {file} open");

            said.Append(". The layout tools work on that tab's layout from now on, as the person watches: each change is drawn as it is made, ");
            said.Append("and is a step of the tab's own undo. look shows its 2D view; the file tools wait until unlink_tab.");

            //
            //What the person has open, so the model starts from where they are rather than asking.
            //
            //**Its refusal is not the link's.** The tab is linked by now, whatever describe_tab does; a tab that
            //took too long over it, or closed, used to turn this whole answer into a refusal, and the model's
            //retry was then told no tab was waiting - the tab had been linked by the call it was told failed.
            //
            try
            {
                var described = await link.CallAsync("describe_tab", default, CallWithin, cancel);

                if (!described.IsError)
                    said.Append($"\n\n{described.Text}");
            }
            catch (LinkRefusal refusal)
            {
                said.Append($"\n\ndescribe_tab did not answer yet: {refusal.Message} Call it when you need what the person has open.");
            }

            return answeredWith(said.ToString());
        }

        private static async Task<ServedAnswer> unlinkTabAsync(McpSession session, Given given, CancellationToken cancel)
        {
            if (!await session.Link!.UnlinkAsync())
                return answeredWith("No GDS Viewer tab was linked. The layout tools work on the folder's open layout.");

            string open = "";

            if (session.FilePath is not null)
                open = $", {session.Folder.Relative(session.FilePath)}";

            return answeredWith($"The link to the GDS Viewer tab is ended. The layout tools work on the folder's open layout again{open}.");
        }

        ///<summary>look and describe_tab: the tab's to answer, and refused with how to link one when none is.</summary>
        private async Task<ServedAnswer> askTabAsync(string name, Given given, CancellationToken cancel)
        {
            if (!Link!.IsLinked)
            {
                //Said here, so not said again on the next call.
                Link.TakeEnding();

                return refusedAnswer(
                    $"{name} needs a linked GDS Viewer tab, which is what answers it. Ask the person to open Settings, AI settings in GDS Viewer, "
                    + "press Start under MCP server, and give you the code it shows; then link_tab with it.");
            }

            return await toTabAsync(name, given.Arguments, cancel);
        }

        private async Task<ServedAnswer> toTabAsync(string name, JsonElement arguments, CancellationToken cancel)
        {
            try
            {
                var answer = await Link!.CallAsync(name, arguments, CallWithin, cancel);

                return new ServedAnswer(answer.Text, answer.IsError, answer.Png);
            }
            catch (LinkRefusal refusal)
            {
                return refusedAnswer(refusal.Message);
            }
        }

        private static ServedAnswer answeredWith(string text)
        {
            return new ServedAnswer(text, false);
        }

        private static ServedAnswer refusedAnswer(string why)
        {
            return new ServedAnswer(why, true);
        }
    }
}
