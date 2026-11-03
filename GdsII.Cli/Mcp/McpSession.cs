using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GdsII.Cli
{
    ///
    ///One tool as `gds mcp` lists it: what the model is told, and what the client is told about what it does.
    ///
    public sealed class ServedTool
    {
        public required string Name { get; init; }

        public required string Description { get; init; }

        public required JsonObject InputSchema { get; init; }

        ///<summary>Whether it changes nothing - neither the layout nor anything on disk.</summary>
        public bool ReadsOnly { get; init; }

        ///<summary>Whether it can lose something: a file written over, or changes that were never saved.</summary>
        public bool Destroys { get; init; }
    }

    ///
    ///What one `gds mcp` holds: the folder it works in, the layout open from it, the rule deck and the
    ///layermap, and the tools over them.
    ///
    ///**The library's thirty-one tools, and six of its own** - and four more for a linked tab, in McpSession.Link.cs.
    ///Everything that draws, edits, measures or checks
    ///is <see cref="AgentTools"/>, called exactly as the viewer's AI assistant calls it. This adds only what a chat in
    ///the viewer never needed, because the viewer has a file open and a person to save it: listing the folder,
    ///opening a layout, starting one, saving it, and loading a layermap or a rule deck from beside it.
    ///
    ///**Nothing reaches the disk until save_file**, and save_file writes only layout formats, only in the
    ///folder, and over another file only when told to. Opening or starting a layout will not drop changes that
    ///were never saved unless told to either: the model is the only one who can save them, and there is no
    ///screen for anybody to notice they are about to go.
    ///
    ///**One call at a time.** A client may send several at once, and the layout is one object with one undo
    ///history, so they take turns.
    ///
    ///**And a live link**, unless it was turned off: a GDS Viewer tab the person links by a code, which the layout
    ///tools then work on instead of the folder's file - see <see cref="TabLink"/> and McpSession.Link.cs.
    ///
    ///Free of the MCP SDK, so the tools can be tested as calls and answers; <see cref="McpServing"/> is what
    ///puts them on the protocol.
    ///
    public sealed partial class McpSession
    {
        public McpSession(
            ServedFolder folder,
            LayerNames? layermap = null,
            string? layermapName = null,
            DrcDeck? deck = null,
            string? deckName = null,
            int linkPort = TabLink.DefaultPort,
            Action<string>? log = null)
        {
            Folder = folder;

            this.layermap = layermap;
            this.layermapName = layermapName;
            this.deck = deck;
            this.deckName = deckName;

            //Made here and opened later: a session a test makes binds no port until something links.
            if (linkPort > 0)
                Link = new TabLink(linkPort) { Log = log };

            Tools = describeTools();
        }

        public ServedFolder Folder { get; }

        ///<summary>The open layout's session - its shape numbers and its undo history - or null before anything is open.</summary>
        public AgentSession? Layout { get; private set; }

        ///<summary>Where save_file writes when it is not told: the file the layout came from, was started for, or was last saved to.</summary>
        public string? FilePath { get; private set; }

        ///<summary>What that file is: "GDSII", "OASIS", "DXF" or "SVG".</summary>
        public string? FileFormat { get; private set; }

        ///
        ///Whether the layout has changed since it was opened or last saved.
        ///
        ///An undo counts as a change, since the history cannot tell going back to where the file was from going
        ///somewhere new - so a change taken back still asks to be saved or dropped, which is the safe way round.
        ///
        public bool Unsaved => Layout is not null && Layout.History.Version != savedVersion;

        public IReadOnlyList<ServedTool> Tools { get; }

        ///
        ///How many elements the tools flatten a layout to before they stop - see <see cref="AgentSession.MostElements"/>.
        ///
        ///**The command line's ceiling rather than the app's**, because this runs where the command line does: on a
        ///desktop, with the machine's memory rather than a browser tab's. `gds mcp --most-elements` moves it. A tab
        ///linked by link_tab flattens in the tab, under the tab's own.
        ///
        public int MostElements { get; init; } = Cli.MostElements;

        private int savedVersion;

        private LayerNames? layermap;
        private string? layermapName;
        private DrcDeck? deck;
        private string? deckName;

        private readonly object gate = new object();

        ///<summary>What check_rules says to do when no deck is loaded.</summary>
        private const string HowToLoadRules = "Load one from the folder with load_rules.";

        ///
        ///What the client is told when it connects, for its model: the folder, how files come and go, and the
        ///library's own conventions for everything else.
        ///
        public string Instructions
        {
            get
            {
                var said = new StringBuilder();

                said.Append($"These tools work on layout files in one folder, {Folder.Root}, and read and write nowhere else. ");
                said.Append("list_files says what is in it. Open a layout with open_file, or start an empty one with new_file; the other tools work on the one that is open. ");
                said.Append("Changes stay in memory until save_file writes them - GDSII, OASIS or DXF, chosen by the name's extension - and nothing saves them for you, ");
                said.Append("so say so before finishing with changes unsaved. open_file and new_file will not drop unsaved changes unless told to.\n\n");
                said.Append(AgentTools.Conventions);
                said.Append(" Nobody else edits the layout here, so undo_last_change can take back each of your changes in turn, the latest first.");

                if (Link is not null)
                {
                    said.Append("\n\nWhen the person has GDS Viewer open in a browser and wants you to work on what is on their screen, they can open its AI settings - ");
                    said.Append("Settings, AI settings - and press Start under MCP server, which shows a code. link_tab with that code, and the layout tools work on their tab instead of the folder: ");
                    said.Append("they watch each change land, and can undo it there too.");
                }

                if (layermapName is not null)
                    said.Append($"\n\nThe layermap {layermapName} names the layers of every file opened.");

                if (deckName is not null)
                    said.Append($"\n\nEvery change is checked against the rules in {deckName}.");

                return said.ToString();
            }
        }

        #region Calls ***********************************************************************

        ///
        ///Runs one call, with its arguments as a JSON object.
        ///
        ///**Never throws for anything the model did**, the library's own contract: a tool that does not exist,
        ///arguments that are wrong, a path outside the folder, a file that will not read - each is an answer
        ///that says so, because the model is the one who has to act on it.
        ///
        public AgentResult Call(string name, JsonElement arguments)
        {
            lock (gate)
            {
                foreach (var file in FileTools)
                {
                    if (file.Name != name)
                        continue;

                    try
                    {
                        return answered(file.Run(this, new Given(file.Name, file.Parameters, arguments)));
                    }
                    catch (McpRefusal refusal)
                    {
                        return refused(refusal.Message);
                    }
                    catch (Exception failed) when (failed is IOException || failed is UnauthorizedAccessException)
                    {
                        return refused($"{name} could not read or write the file: {failed.Message}");
                    }
                }

                if (AgentTools.Named(name) is null)
                    return refused($"There is no tool called {name}. The tools are {string.Join(", ", Tools.Select(tool => tool.Name))}.");

                if (Layout is null)
                    return refused("No layout is open. list_files says what is in the folder; open one with open_file, or start one with new_file.");

                return AgentTools.Call(Layout, name, arguments);
            }
        }

        private static AgentResult answered(string text)
        {
            return new AgentResult { Text = text };
        }

        private static AgentResult refused(string why)
        {
            return new AgentResult { Text = why, IsError = true };
        }

        private List<ServedTool> describeTools()
        {
            var tools = new List<ServedTool>();

            foreach (var file in FileTools)
                tools.Add(served(file.Name, file.Description, file.Parameters, file.ReadsOnly, file.Destroys));

            if (Link is not null)
            {
                foreach (var link in LinkTools)
                    tools.Add(served(link.Name, link.Description, link.Parameters, link.ReadsOnly, false));
            }

            foreach (var tool in AgentTools.All)
            {
                tools.Add(new ServedTool
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = tool.InputSchema(),
                    ReadsOnly = !tool.Edits
                });
            }

            return tools;
        }

        ///<summary>One of this session's own tools as it is listed, with a schema written from its parameters as the library writes its own.</summary>
        private static ServedTool served(string name, string description, AgentParameter[] parameters, bool readsOnly, bool destroys)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var parameter in parameters)
            {
                properties[parameter.Name] = parameter.Schema();

                if (parameter.Required)
                    required.Add(parameter.Name);
            }

            var schema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (required.Count > 0)
                schema["required"] = required;

            return new ServedTool
            {
                Name = name,
                Description = description,
                InputSchema = schema,
                ReadsOnly = readsOnly,
                Destroys = destroys
            };
        }

        #endregion **************************************************************************



        #region The file tools **************************************************************

        ///<summary>One of the tools this adds to the library's: what it is called, takes and does.</summary>
        private sealed class FileTool
        {
            public required string Name { get; init; }

            public required string Description { get; init; }

            public required AgentParameter[] Parameters { get; init; }

            public bool ReadsOnly { get; init; }

            public bool Destroys { get; init; }

            ///<summary>What the model is told, or an <see cref="McpRefusal"/> saying why not.</summary>
            public required Func<McpSession, Given, string> Run { get; init; }
        }

        private static readonly FileTool[] FileTools =
        {
            new FileTool
            {
                Name = "list_files",
                Description = "Lists the layouts in the folder - GDSII, OASIS, DXF and SVG - and the layermaps and rule decks beside "
                    + "them, by the paths the other file tools take, and says which layout is open. Folders whose names start "
                    + "with a dot, and bin, obj and node_modules, are not looked in.",
                Parameters = new[] { may("folder", AgentValue.Text, "Only this folder inside it, by its path, like cells.") },
                ReadsOnly = true,
                Run = listFiles
            },
            new FileTool
            {
                Name = "open_file",
                Description = "Opens a layout from the folder in place of the one open - GDSII, OASIS, DXF or SVG, told apart by "
                    + "what is in the file. Every other tool works on it from then on, and its shape numbers start again. "
                    + "A layermap or rule deck that is loaded stays loaded. Refuses while the open layout has changes that "
                    + "are not saved, unless discard_changes is true.",
                Parameters = new[]
                {
                    needs("path", AgentValue.Text, "The file, by its path in the folder, like chip.gds or cells/inv.gds."),
                    may("discard_changes", AgentValue.Flag, "Drop the open layout's unsaved changes rather than refusing.")
                },
                Destroys = true,
                Run = openFile
            },
            new FileTool
            {
                Name = "new_file",
                Description = "Starts an empty layout with one cell, in place of the one open, to be saved at path. Nothing is "
                    + "written until save_file. Refuses a path where there is already a file, and refuses while the open "
                    + "layout has changes that are not saved, unless discard_changes is true.",
                Parameters = new[]
                {
                    needs("path", AgentValue.Text, "Where save_file will write it, in the folder: a .gds, .oas or .dxf name, like chip.gds."),
                    may("cell", AgentValue.Text, "What its one cell is called. TOP when not given."),
                    may("grid", AgentValue.Number, "How big one database unit is, in microns - the finest step a coordinate can take. 0.001, a nanometer, when not given, which is what nearly every process uses."),
                    may("discard_changes", AgentValue.Flag, "Drop the open layout's unsaved changes rather than refusing.")
                },
                Destroys = true,
                Run = newFile
            },
            new FileTool
            {
                Name = "save_file",
                Description = "Writes the open layout to the folder: to its own file, or to path. The name's extension chooses the "
                    + "format - .gds for GDSII, .oas for OASIS, .dxf for DXF - and a layout saved under a new name is that file "
                    + "from then on. Refuses to write over a different file that is already there unless replace is true. "
                    + "Nothing else writes a layout.",
                Parameters = new[]
                {
                    may("path", AgentValue.Text, "Where to write it, in the folder. Its own file when not given."),
                    may("replace", AgentValue.Flag, "Write over a file that is already at path and is not the open layout's own.")
                },
                Destroys = true,
                Run = saveFile
            },
            new FileTool
            {
                Name = "load_layermap",
                Description = "Loads a layermap from the folder: a CSV of layer,datatype,name - and color, height, thickness and "
                    + "role after it - or a KLayout .lyp, whose layer names and fill colors it takes. It names the layers of the "
                    + "open layout and of every file opened after, so they can be asked for by name.",
                Parameters = new[] { needs("path", AgentValue.Text, "The layermap, by its path in the folder.") },
                Run = loadLayermap
            },
            new FileTool
            {
                Name = "load_rules",
                Description = "Loads a design rule deck from the folder. Every change is then followed by a check of the rules "
                    + "around it, and check_rules checks against it. It stays loaded for every file opened after.",
                Parameters = new[] { needs("path", AgentValue.Text, "The deck, by its path in the folder, like sky130A.drc.") },
                Run = loadRules
            }
        };

        private static AgentParameter needs(string name, AgentValue value, string description)
        {
            return new AgentParameter { Name = name, Value = value, Description = description, Required = true };
        }

        private static AgentParameter may(string name, AgentValue value, string description)
        {
            return new AgentParameter { Name = name, Value = value, Description = description };
        }

        #endregion **************************************************************************



        #region Listing *********************************************************************

        ///<summary>How many of each kind list_files lists before saying how many more there are.</summary>
        private const int MostListed = 200;

        private static string listFiles(McpSession session, Given given)
        {
            string start = session.Folder.Root;

            if (given.OptionalText("folder") is string folder)
            {
                start = session.Folder.ResolveFolder(folder);

                if (!Directory.Exists(start))
                    throw new McpRefusal($"There is no folder at {folder}. list_files with no folder lists everything.");
            }

            var files = session.Folder.Files(start);

            string where = session.Folder.Root;

            if (!ServedFolder.Same(start, session.Folder.Root))
                where = session.Folder.Relative(start);

            if (files.Count == 0)
                return $"There are no layouts, layermaps or rule decks in {where}. Start a layout with new_file.";

            var said = new StringBuilder($"In {where}:");

            session.listKind(said, files, ServedFolder.Kind.Layout, "Layouts");
            session.listKind(said, files, ServedFolder.Kind.Layermap, "Layermaps, for load_layermap");
            session.listKind(said, files, ServedFolder.Kind.Deck, "Rule decks, for load_rules");

            if (session.FilePath is not null && !File.Exists(session.FilePath))
            {
                said.Append($"\nOpen: {session.Folder.Relative(session.FilePath)}, which is not on disk yet");

                if (session.Unsaved)
                    said.Append(", with changes not saved");

                said.Append('.');
            }

            return said.ToString();
        }

        private void listKind(StringBuilder said, List<string> files, ServedFolder.Kind kind, string heading)
        {
            var these = files.Where(file => ServedFolder.KindOf(file) == kind).ToList();

            if (these.Count == 0)
                return;

            said.Append($"\n{heading}:");

            foreach (string file in these.Take(MostListed))
            {
                said.Append($"\n  {Folder.Relative(file)}, {sizeOf(file)}");

                if (!ServedFolder.Same(file, FilePath))
                    continue;

                said.Append(" - open");

                if (Unsaved)
                    said.Append(", with changes not saved");
            }

            if (these.Count > MostListed)
                said.Append($"\n  and {these.Count - MostListed} more; name a folder to list fewer at a time");
        }

        ///<summary>A file's size as somebody reads one: "812 bytes", "12 KB", "3.4 MB".</summary>
        private static string sizeOf(string file)
        {
            long bytes;

            try
            {
                bytes = new FileInfo(file).Length;
            }
            catch (IOException)
            {
                return "size unknown";
            }

            if (bytes < 1024)
                return $"{bytes} bytes";

            if (bytes < 1024 * 1024)
                return $"{(bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture)} KB";

            return $"{(bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture)} MB";
        }

        #endregion **************************************************************************



        #region Opening and starting ********************************************************

        private static string openFile(McpSession session, Given given)
        {
            string path = given.Text("path");
            string full = session.Folder.Resolve(path);
            string named = session.Folder.Relative(full);

            if (Directory.Exists(full))
                throw new McpRefusal($"{named} is a folder. list_files with folder {named} says what is in it.");

            if (!File.Exists(full))
                throw new McpRefusal($"There is no file at {named}. list_files says what is in the folder.");

            session.refuseToDrop(given, "open another");

            GDS gds;
            string format;

            try
            {
                using var stream = File.OpenRead(full);

                gds = Cli.ReadLayout(stream, out format);
            }
            catch (Exception unreadable) when (unreadable is InvalidDataException || unreadable is EndOfStreamException)
            {
                throw new McpRefusal($"{named} is not a layout this reads - GDSII, OASIS, DXF or SVG: {unreadable.Message}");
            }

            session.open(gds, full, format);

            return $"Opened {named}, {format}: {session.describe()} Shape numbers start again with this file.";
        }

        private static string newFile(McpSession session, Given given)
        {
            string path = given.Text("path");
            string full = session.Folder.Resolve(path);
            string named = session.Folder.Relative(full);

            if (formatOf(full) is not Format format)
                throw new McpRefusal($"new_file needs a .gds, .oas or .dxf name, which is what save_file will write, and {named} is none of them.");

            if (File.Exists(full) || Directory.Exists(full))
                throw new McpRefusal($"There is already a file at {named}. Open it with open_file, or choose another name.");

            string cell = AddElement.AsAscii(given.OptionalText("cell") ?? "TOP").Trim();

            if (cell.Length == 0)
                throw new McpRefusal("A cell needs a name, in printable ASCII.");

            double grid = given.OptionalNumber("grid") ?? 0.001;

            if (grid <= 0 || grid > 1)
                throw new McpRefusal($"\"grid\" is how big one database unit is, in microns: more than 0 and at most 1. {plain(grid)} is not.");

            session.refuseToDrop(given, "start a new one");

            var gds = GDS.NewLibrary(Path.GetFileNameWithoutExtension(full).ToUpperInvariant(), cell, grid * 1e-6);

            session.open(gds, full, nameOf(format));

            return $"Started {named}: an empty layout with one cell, {cell}, which is the one being worked in. "
                + $"A database unit is {plain(grid)} µm. Nothing is written until save_file.";
        }

        ///<summary>Refuses a call that would drop changes nobody saved, unless it said to.</summary>
        private void refuseToDrop(Given given, string doing)
        {
            if (!Unsaved || given.Flag("discard_changes", false))
                return;

            throw new McpRefusal($"{Folder.Relative(FilePath!)} has changes that are not saved. Save them with save_file first, or pass discard_changes: true to {doing} without them.");
        }

        ///<summary>Makes a layout the open one, with a new session and history, and the layermap and deck in hand.</summary>
        private void open(GDS gds, string full, string format)
        {
            if (layermap is not null)
                layermap.ApplyTo(gds.AdditionalInformation.Layers);

            var history = new EditHistory();

            Layout = new AgentSession(gds, history) { Deck = deck, HowToLoadRules = HowToLoadRules, MostElements = MostElements };

            FilePath = full;
            FileFormat = format;
            savedVersion = history.Version;
        }

        ///
        ///The open layout in a sentence: its cells, which one is worked in, its layers and its grid.
        ///
        ///**The cell is settled here**, when the library has one top, rather than at the first call - so what
        ///the model is told about which cell it is in is already true.
        ///
        private string describe()
        {
            var gds = Layout!.Gds;
            var tops = Hierarchy.Summarize(gds).Where(cell => cell.IsTop).Select(cell => cell.Name).ToList();

            var said = new StringBuilder(counted(gds.StreamFormat.Structures.Count, "cell"));

            if (tops.Count == 1)
            {
                Layout.Cell = tops[0];

                said.Append($", with {tops[0]} on top, which is the one being worked in");
            }
            else if (tops.Count > 1)
            {
                said.Append($", {tops.Count} of them on top - {string.Join(", ", tops.Take(20))}");

                if (tops.Count > 20)
                    said.Append($" and {tops.Count - 20} more");

                said.Append(" - so open_cell chooses which to work in");
            }

            var layers = gds.AdditionalInformation.Layers.Values;
            int named = layers.Count(layer => !string.IsNullOrWhiteSpace(layer.Name));

            said.Append($"; {counted(layers.Count, "layer")}");

            if (named == layers.Count && named > 0)
                said.Append(", all named");
            else if (named > 0)
                said.Append($", {named} of them named");

            said.Append($". A database unit is {plain(Layout.MicronsPerUnit)} µm.");

            if (deckName is not null)
                said.Append($" Changes are checked against {deckName}.");

            return said.ToString();
        }

        #endregion **************************************************************************



        #region Saving **********************************************************************

        ///<summary>The formats save_file writes.</summary>
        private enum Format
        {
            Gds,
            Oasis,
            Dxf
        }

        ///<summary>What a name says to write, or null for a name that is none of the three.</summary>
        private static Format? formatOf(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension is ".gds" or ".gds2" or ".gdsii")
                return Format.Gds;

            if (extension is ".oas" or ".oasis")
                return Format.Oasis;

            if (extension == ".dxf")
                return Format.Dxf;

            return null;
        }

        private static string nameOf(Format format)
        {
            if (format == Format.Oasis)
                return "OASIS";

            if (format == Format.Dxf)
                return "DXF";

            return "GDSII";
        }

        private static string saveFile(McpSession session, Given given)
        {
            if (session.Layout is null)
                throw new McpRefusal("There is no layout open to save. Open one with open_file, or start one with new_file.");

            string? path = given.OptionalText("path");

            string full = session.FilePath!;

            if (path is not null)
                full = session.Folder.Resolve(path);

            string named = session.Folder.Relative(full);

            if (formatOf(full) is not Format format)
            {
                if (path is null)
                    throw new McpRefusal($"{named} was read from {session.FileFormat}, which save_file does not write. Give it a path ending .gds, .oas or .dxf to save it as one of those.");

                throw new McpRefusal($"save_file writes GDSII, OASIS or DXF, chosen by the name - .gds, .oas or .dxf - and {named} is none of them.");
            }

            if (Directory.Exists(full))
                throw new McpRefusal($"{named} is a folder.");

            bool itsOwn = ServedFolder.Same(full, session.FilePath);

            if (File.Exists(full) && !itsOwn && !given.Flag("replace", false))
                throw new McpRefusal($"There is already a file at {named}, and it is not the open layout's own. Pass replace: true to write over it, or choose another name.");

            var gds = session.Layout.Gds;
            byte[] bytes;
            string note = "";

            if (format == Format.Oasis)
            {
                bytes = OasisWriter.Write(gds, out int skipped);

                //A node has no OASIS spelling, and a shape missing from a saved file is found much later.
                if (skipped > 0)
                    note = $" {counted(skipped, "element")} had no OASIS equivalent and were left out - NODE elements, usually.";
            }
            else if (format == Format.Dxf)
            {
                bytes = DxfWriter.Write(gds, out int skipped);
                note = " Its layers are named L<layer>D<datatype>, since DXF has no layer numbers.";

                if (skipped > 0)
                    note += $" {counted(skipped, "element")} had no DXF equivalent and were left out - NODE elements, usually.";
            }
            else
            {
                bytes = gds.Serialize();
            }

            writeWhole(full, bytes);

            session.FilePath = full;
            session.FileFormat = nameOf(format);
            session.savedVersion = session.Layout.History.Version;

            string said = $"Saved {named}: {nameOf(format)}, {counted(gds.StreamFormat.Structures.Count, "cell")}, "
                + $"{bytes.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes.{note}";

            if (!itsOwn)
                said += " save_file with no path writes here from now on.";

            return said;
        }

        ///
        ///Writes a file whole or not at all: into a new file beside it first, then moved over it.
        ///
        ///A save that fails halfway - a full disk, another program holding the file - leaves the file as it was
        ///rather than cut short, which for somebody's only copy of a layout is the difference that matters.
        ///
        private static void writeWhole(string full, byte[] bytes)
        {
            string folder = Path.GetDirectoryName(full)!;

            Directory.CreateDirectory(folder);

            string beside = Path.Combine(folder, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.saving");

            try
            {
                File.WriteAllBytes(beside, bytes);
                File.Move(beside, full, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(beside))
                        File.Delete(beside);
                }
                catch { }
            }
        }

        #endregion **************************************************************************



        #region Layermaps and decks *********************************************************

        private static string loadLayermap(McpSession session, Given given)
        {
            string full = session.Folder.Resolve(given.Text("path"));
            string named = session.Folder.Relative(full);

            if (!File.Exists(full))
                throw new McpRefusal($"There is no file at {named}. list_files says what is in the folder.");

            var parsed = LayerNames.Parse(File.ReadAllText(full));

            if (parsed.Count == 0)
                throw new McpRefusal($"{named} says nothing about any layer.{problems(parsed.Problems)}");

            session.layermap = parsed;
            session.layermapName = named;

            var said = new StringBuilder($"Loaded {named}, which says something about {counted(parsed.Count, "layer")}.");

            if (session.Layout is null)
            {
                said.Append(" It names the layers of each file opened from now on.");
            }
            else
            {
                var layers = session.Layout.Gds.AdditionalInformation.Layers;
                int applied = parsed.ApplyTo(layers);

                if (applied == 0)
                    said.Append(" It says nothing about any layer the open layout uses: check it is for the right process, and that a CSV's columns are layer,datatype,name.");
                else
                    said.Append($" It names {applied} of the open layout's {counted(layers.Count, "layer")}; list_layers lists them.");
            }

            said.Append(problems(parsed.Problems));

            return said.ToString();
        }

        private static string loadRules(McpSession session, Given given)
        {
            string full = session.Folder.Resolve(given.Text("path"));
            string named = session.Folder.Relative(full);

            if (!File.Exists(full))
                throw new McpRefusal($"There is no file at {named}. list_files says what is in the folder.");

            var parsed = DrcDeck.Parse(File.ReadAllText(full));

            if (parsed.Rules.Count == 0 && parsed.Refused.Count == 0)
                throw new McpRefusal($"{named} holds no rules.{problems(parsed.Problems)}");

            session.deck = parsed;
            session.deckName = named;

            if (session.Layout is not null)
                session.Layout.Deck = parsed;

            var said = new StringBuilder($"Loaded {named}: {counted(parsed.Rules.Count, "rule")}. From now on each change is checked against them around itself, and check_rules checks a whole cell.");

            if (parsed.Refused.Count > 0)
                said.Append($" {counted(parsed.Refused.Count, "rule")} use a check this does not have, and will not be run: {string.Join(", ", parsed.Refused.Take(10))}.");

            said.Append(problems(parsed.Problems));

            return said.ToString();
        }

        ///<summary>What could not be read in a file, as the end of an answer - the first few, and how many more.</summary>
        private static string problems(List<string> found)
        {
            if (found.Count == 0)
                return "";

            var said = new StringBuilder($" {counted(found.Count, "line")} could not be read: {string.Join("; ", found.Take(5))}");

            if (found.Count > 5)
                said.Append($"; and {found.Count - 5} more");

            said.Append('.');

            return said.ToString();
        }

        #endregion **************************************************************************



        #region Arguments *******************************************************************

        ///<summary>A number of microns as it was given or worked out, and no more places than it has: "0.001", "2.5" - the library's own spelling.</summary>
        private static string plain(double microns)
        {
            return AgentSession.Given(microns);
        }

        ///<summary>A count and its noun, singular for one: "1 cell", "3 cells" - spelled as the library's tools spell one, so a count reads the same across a conversation.</summary>
        private static string counted(int count, string noun)
        {
            return AgentTools.Counted(count, noun);
        }

        ///
        ///The arguments of a call to one of the file tools, read the way the library reads its own: strict about
        ///names, forgiving about how a value is written.
        ///
        ///An argument the tool does not take is refused with the ones it does, since a misspelled one would
        ///otherwise be a default nobody asked for; but "true" for true and "0.001" for 0.001 are what was meant.
        ///
        private sealed class Given
        {
            private readonly string tool;
            private readonly AgentParameter[] parameters;
            private readonly Dictionary<string, JsonElement> values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            public Given(string tool, AgentParameter[] parameters, JsonElement arguments)
            {
                this.tool = tool;
                this.parameters = parameters;

                Arguments = arguments;

                //No arguments at all is how a client sends a call to a tool that takes none.
                bool none = arguments.ValueKind == JsonValueKind.Undefined || arguments.ValueKind == JsonValueKind.Null;

                if (!none && arguments.ValueKind != JsonValueKind.Object)
                    throw new McpRefusal($"The arguments to {tool} have to be a JSON object, like {{\"path\": \"chip.gds\"}}.");

                if (!none)
                {
                    foreach (var property in arguments.EnumerateObject())
                    {
                        if (!parameters.Any(parameter => parameter.Name == property.Name))
                            throw new McpRefusal($"{tool} does not take \"{property.Name}\". {accepted()}");

                        //A null is an argument left out, which is how some models write "use the default".
                        if (property.Value.ValueKind == JsonValueKind.Null)
                            continue;

                        values[property.Name] = property.Value;
                    }
                }

                foreach (var expected in parameters)
                {
                    if (expected.Required && !values.ContainsKey(expected.Name))
                        throw new McpRefusal($"{tool} needs \"{expected.Name}\". {expected.Description}");
                }
            }

            ///<summary>The arguments as they came, for a call passed on whole - to a linked tab, which reads them itself.</summary>
            public JsonElement Arguments { get; }

            public string Text(string name)
            {
                return OptionalText(name) ?? throw new McpRefusal($"{tool} needs \"{name}\".");
            }

            ///<summary>Text, or a number as it was written - for a code, which a model may send either way: "4821 7356" or 48217356.</summary>
            public string TextOrNumber(string name)
            {
                if (values.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.Number)
                    return value.GetRawText();

                return Text(name);
            }

            public string? OptionalText(string name)
            {
                if (!values.TryGetValue(name, out var value))
                    return null;

                if (value.ValueKind == JsonValueKind.String)
                    return value.GetString() ?? "";

                throw new McpRefusal($"\"{name}\" has to be text, and {describe(value)} is not.");
            }

            public bool Flag(string name, bool otherwise)
            {
                if (!values.TryGetValue(name, out var value))
                    return otherwise;

                if (value.ValueKind == JsonValueKind.True)
                    return true;

                if (value.ValueKind == JsonValueKind.False)
                    return false;

                if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool said))
                    return said;

                throw new McpRefusal($"\"{name}\" has to be true or false, and {describe(value)} is neither.");
            }

            public double? OptionalNumber(string name)
            {
                if (!values.TryGetValue(name, out var value))
                    return null;

                if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number))
                    return number;

                if (value.ValueKind == JsonValueKind.String
                    && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                    && double.IsFinite(parsed))
                    return parsed;

                throw new McpRefusal($"\"{name}\" has to be a number, and {describe(value)} is not one.");
            }

            private string accepted()
            {
                if (parameters.Length == 0)
                    return "It takes no arguments.";

                return $"It takes {string.Join(", ", parameters.Select(parameter => parameter.Name))}.";
            }

            private static string describe(JsonElement value)
            {
                string raw = value.GetRawText();

                if (raw.Length > 60)
                    raw = raw.Substring(0, 57) + "...";

                return raw;
            }
        }

        #endregion **************************************************************************
    }
}
