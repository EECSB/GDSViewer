using System.Globalization;
using ModelContextProtocol.Server;

namespace GdsII.Cli
{
    ///<summary>
    ///`gds mcp`: the layouts in a folder, served to an AI model over the Model Context Protocol.
    ///
    ///Started by an MCP client - Claude Desktop, Claude Code or any other - rather than typed, and talking to
    ///it over standard input and output until the client closes them. What the model can do is
    ///<see cref="McpSession"/>; this reads the command line, loads what it names, and hands the session to the
    ///protocol.
    ///
    ///**Standard output is the protocol's**, so nothing here writes to it: one line of prose in that stream is
    ///a message the client cannot parse. What a person running it by hand needs to know goes to standard
    ///error, which clients keep as the server's log.
    ///</summary>
    public static partial class Cli
    {
        #region mcp *************************************************************************

        private static int mcp(string[] args, TextWriter output, TextWriter error)
        {
            int read = McpSessionFrom(args, error, out McpSession? session);

            if (read != Ok)
                return read;

            error.WriteLine($"gds mcp {Version}: the layouts in {session!.Folder.Root}, for an MCP client talking over standard input and output.");
            error.WriteLine("It is meant to be started by one - Claude Desktop, Claude Code or another - rather than typed. Ctrl+C stops it.");

            //
            //**The live link listens from the start**, so a tab whose MCP server is already started finds it at once and
            //waits, and link_tab has nothing to wait for. One gds mcp per Claude window and one port: the first one up
            //has it, and the rest say so here and try again when link_tab is called.
            //
            if (session.Link is TabLink link)
            {
                if (link.TryListen(out string why))
                    error.WriteLine($"A GDS Viewer tab can link on port {link.Port}, by the code its AI settings show under MCP server.");
                else
                    error.WriteLine($"Not listening for a GDS Viewer tab yet. {why}");
            }

            try
            {
                McpServing.RunAsync(session, new StdioServerTransport("gds"), CancellationToken.None).GetAwaiter().GetResult();
            }
            finally
            {
                session.Link?.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return Ok;
        }

        ///
        ///Reads `gds mcp`'s command line into a session: the folder, the layermap and deck to load for every file, and
        ///how far its tools flatten one. Ok, or the exit code for what was wrong, said on <paramref name="error"/>.
        ///
        ///Apart from serving so that a test can check it, since serving waits on standard input for good.
        ///
        ///The layermap and deck are named by the person starting the tool, so they are read where they are -
        ///relative to the working directory, like every other command's options - rather than kept to the
        ///folder, which is a fence for the model.
        ///
        public static int McpSessionFrom(string[] args, TextWriter error, out McpSession? session)
        {
            session = null;

            var folders = positional(args).ToList();

            if (folders.Count > 1)
            {
                error.WriteLine($"mcp works in one folder, but was given {folders.Count}.");

                return UsageError;
            }

            string folder = Directory.GetCurrentDirectory();

            if (folders.Count == 1)
                folder = folders[0];

            if (!Directory.Exists(folder))
            {
                error.WriteLine($"There is no folder at {folder}.");

                return FileError;
            }

            LayerNames? layermap = null;
            string? layermapPath = valueOf(args, "--layermap");

            if (layermapPath is not null)
            {
                string text;

                try
                {
                    text = File.ReadAllText(layermapPath);
                }
                catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
                {
                    error.WriteLine($"Could not read the layermap {layermapPath}: {problem.Message}");

                    return FileError;
                }

                layermap = LayerNames.Parse(text);

                foreach (string trouble in layermap.Problems)
                    error.WriteLine(trouble);

                if (layermap.Count == 0)
                {
                    error.WriteLine($"{layermapPath} says nothing about any layer.");

                    return FileError;
                }
            }

            DrcDeck? deck = null;
            string? deckPath = valueOf(args, "--deck");

            if (deckPath is not null)
            {
                string text;

                try
                {
                    text = File.ReadAllText(deckPath);
                }
                catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
                {
                    error.WriteLine($"Could not read the deck {deckPath}: {problem.Message}");

                    return FileError;
                }

                deck = DrcDeck.Parse(text);

                foreach (string problem in deck.Problems)
                    error.WriteLine(problem);

                if (deck.Rules.Count == 0 && deck.Refused.Count == 0)
                {
                    error.WriteLine($"{deckPath} holds no rules.");

                    return FileError;
                }
            }

            string? layermapName = null;

            if (layermapPath is not null)
                layermapName = Path.GetFileName(layermapPath);

            string? deckName = null;

            if (deckPath is not null)
                deckName = Path.GetFileName(deckPath);

            int linkPort = TabLink.DefaultPort;

            if (valueOf(args, "--link-port") is string port)
            {
                if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out linkPort) || linkPort > 65535)
                {
                    error.WriteLine($"--link-port is the port a GDS Viewer tab links on, from 1 to 65535 - or 0 for no live link - and {port} is not one.");

                    return UsageError;
                }
            }

            //As far as every other command flattens, and moved the same way - see McpSession.MostElements.
            if (!tryReadCount(args, "--most-elements", error, out int most, MostElements))
                return UsageError;

            session = new McpSession(new ServedFolder(folder), layermap, layermapName, deck, deckName, linkPort, error.WriteLine)
            {
                MostElements = most
            };

            return Ok;
        }

        #endregion **************************************************************************
    }
}
