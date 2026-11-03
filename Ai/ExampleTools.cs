using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdsII;

namespace GDSViewer.Ai
{
    ///<summary>One of the layouts the app ships: its file name, what kind it is, and what it is when the app knows.</summary>
    public sealed class ExampleFile
    {
        public required string Name { get; init; }

        ///<summary>"cell" for a standard cell or device, "design" for a whole chip or block, "transistor" for Mosfet.gds.</summary>
        public required string Kind { get; init; }

        ///<summary>What it is and whose, for a design; empty for a cell, whose name says it.</summary>
        public string About { get; init; } = "";

        ///<summary>The PDK it is drawn in, as the PDK manager names it - sky130, gf180mcu or ihp-sg13g2 - or empty where the host does not say.</summary>
        public string Pdk { get; init; } = "";
    }

    ///<summary>The examples the host serves, and the way to open one - the page's to give, as the picture is.</summary>
    public sealed class ExampleLibrary
    {
        public required IReadOnlyList<ExampleFile> Files { get; init; }

        ///<summary>Fetches and reads one by its name, with its layers named by the layermap that ships.</summary>
        public required Func<string, CancellationToken, Task<GDS>> Open { get; init; }
    }

    ///
    ///**The app's own examples, for the AI assistant to learn from** (2026-11-01, asked for): the standard cells and the
    ///whole designs the Examples popup lists, which are real layouts drawn by people who knew the process. A model
    ///asked to draw an inverter or a contact can look at how the PDK's own are drawn - their layers, sizes and spacing -
    ///rather than drawing from what it half remembers. sky130's since 2026-11-01, and gf180mcu's and ihp-sg13g2's beside
    ///them since 2026-11-02, each listed with the PDK it is drawn in, so a GF180 inverter is not drawn from a sky130 one.
    ///
    ///<see cref="ListName"/> names them, narrowed by a filter, and <see cref="ReadName"/> opens one **beside** the
    ///layout, never in place of it: on a session of its own, read with the same list_cells, list_layers and find_shapes
    ///the model already knows, so nothing it does there can change the person's file. Only reading, so both run without
    ///asking on every rung but the first.
    ///
    public static class ExampleTools
    {
        public const string ListName = "list_examples";
        public const string ReadName = "read_example";

        ///<summary>How many names a list gives before saying how many more there are.</summary>
        private const int MostListed = 150;

        ///<summary>How many opened examples are kept read, so asking about one again does not fetch and parse it again.</summary>
        private const int MostKept = 3;

        public static LlmTool ListTool
        {
            get
            {
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["filter"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Only names containing this, ignoring case - like inv, nand2, dfxtp or opamp. Leave it out to list them all."
                        }
                    }
                };

                return new LlmTool
                {
                    Name = ListName,
                    Description = "Lists the example layouts that ship with GDS Viewer, each with the PDK it is drawn in: about nine hundred sky130 "
                        + "standard cells and devices (sky130_fd_sc_hd__inv_1, nand2, flip-flops, ...), over two hundred gf180mcu ones "
                        + "(gf180mcu_fd_sc_mcu7t5v0__inv_1, ...), eighty-four ihp-sg13g2 ones (sg13g2_inv_1, ...), a handful of whole designs "
                        + "in each, and a single sky130 transistor. Real layouts "
                        + "drawn by people who knew the process - look at one drawn in the layout's PDK with read_example before drawing something like it.",
                    InputSchemaJson = schema.ToJsonString()
                };
            }
        }

        public static LlmTool ReadTool
        {
            get
            {
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The example's file name, as list_examples gives it."
                        },
                        ["cell"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The cell in it to read. Leave it out for its top cell."
                        },
                        ["layer"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Only the shapes on this layer, by name or numbers."
                        },
                        ["area"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "number" },
                            ["description"] = "Only the shapes touching this box, [left, bottom, right, top] in microns in the cell's own coordinates."
                        },
                        ["limit"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "The most shapes to list, 1 to 500. 100 when not given."
                        }
                    },
                    ["required"] = new JsonArray("name")
                };

                return new LlmTool
                {
                    Name = ReadName,
                    Description = "Opens one of the app's example layouts beside the person's, without changing theirs, and reads it: its cells, its layers "
                        + "with how many shapes each has, and the shapes of one cell with their layers, sizes and positions in microns. For learning how "
                        + "a PDK draws something - a cell's rails, its contacts, its spacing - before drawing it in the person's layout, from an example "
                        + "drawn in the same PDK.",
                    InputSchemaJson = schema.ToJsonString()
                };
            }
        }

        public static LlmToolOutput List(ExampleLibrary library, string argumentsJson)
        {
            string filter = "";

            if (argumentsOf(argumentsJson) is JsonElement arguments && arguments.TryGetProperty("filter", out var asked) && asked.ValueKind == JsonValueKind.String)
                filter = asked.GetString()?.Trim() ?? "";

            var matching = library.Files.Where(file => filter.Length == 0 || file.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matching.Count == 0)
                return new LlmToolOutput { Text = $"No example's name contains \"{filter}\". Call list_examples with a shorter filter, or none." };

            var text = new StringBuilder();

            if (filter.Length == 0)
                text.Append($"{matching.Count} examples:");
            else
                text.Append($"{matching.Count} examples whose names contain \"{filter}\":");

            foreach (var file in matching.Take(MostListed))
            {
                text.Append('\n').Append(file.Name);

                //What kind, where it is not a cell, and the PDK it is drawn in: (design, gf180mcu), or (sky130) for a cell.
                var said = new List<string>();

                if (file.Kind != "cell")
                    said.Add(file.Kind);

                if (file.Pdk.Length > 0)
                    said.Add(file.Pdk);

                if (said.Count > 0)
                    text.Append(" (").Append(string.Join(", ", said)).Append(')');

                if (file.About.Length > 0)
                    text.Append(" - ").Append(file.About);
            }

            if (matching.Count > MostListed)
                text.Append($"\n...and {matching.Count - MostListed} more. Narrow the list with a filter.");

            return new LlmToolOutput { Text = text.ToString() };
        }

        ///
        ///Reads an example, opening it the first time it is asked about and keeping the last few read - see
        ///<see cref="MostKept"/>. Whole designs are large; the cells are a few kilobytes.
        ///
        public static async Task<LlmToolOutput> ReadAsync(ExampleLibrary library, Dictionary<string, GDS> kept, string argumentsJson, CancellationToken ct)
        {
            if (argumentsOf(argumentsJson) is not JsonElement arguments
                || !arguments.TryGetProperty("name", out var named)
                || named.ValueKind != JsonValueKind.String)
                return refused("read_example needs the example's name, as list_examples gives it.");

            string name = named.GetString()?.Trim() ?? "";

            if (library.Files.FirstOrDefault(file => string.Equals(file.Name, name, StringComparison.OrdinalIgnoreCase)) is not ExampleFile example)
                return refused($"There is no example called {name}. list_examples gives their names.");

            if (!kept.TryGetValue(example.Name, out var gds))
            {
                try
                {
                    gds = await library.Open(example.Name, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception failed)
                {
                    return refused($"{example.Name} could not be opened: {failed.Message}");
                }

                if (kept.Count >= MostKept)
                    kept.Remove(kept.Keys.First());

                kept[example.Name] = gds;
            }

            var session = new AgentSession(gds, new EditHistory());
            var text = new StringBuilder();

            text.AppendLine($"The example {example.Name}, opened beside the person's layout - nothing here changes theirs, and its shape numbers are its own.");

            if (example.Pdk.Length > 0)
                text.AppendLine($"It is drawn in {example.Pdk}, whose layermap names its layers.");

            if (example.About.Length > 0)
                text.AppendLine(example.About);

            text.AppendLine();
            text.AppendLine(AgentTools.Call(session, "list_cells", "{}").Text);

            string? cell = null;

            if (arguments.TryGetProperty("cell", out var askedCell) && askedCell.ValueKind == JsonValueKind.String)
                cell = askedCell.GetString();

            var tops = Hierarchy.Summarize(gds).Where(summary => summary.IsTop).Select(summary => summary.Name).ToList();

            if (cell is null && tops.Count == 1)
                cell = tops[0];

            if (cell is null)
            {
                text.AppendLine();
                text.Append("It has several top cells: read_example again with the one to read as cell.");

                return new LlmToolOutput { Text = text.ToString() };
            }

            var opened = AgentTools.Call(session, "open_cell", new JsonObject { ["name"] = cell }.ToJsonString());

            if (opened.IsError)
                return refused($"{opened.Text} ({example.Name})");

            text.AppendLine();
            text.AppendLine(AgentTools.Call(session, "list_layers", "{}").Text);

            var finding = new JsonObject { ["limit"] = 100 };

            foreach (string property in new[] { "layer", "area", "limit" })
            {
                if (arguments.TryGetProperty(property, out var value))
                    finding[property] = JsonNode.Parse(value.GetRawText());
            }

            var found = AgentTools.Call(session, "find_shapes", finding.ToJsonString());

            text.AppendLine();
            text.AppendLine($"The shapes in {cell}:");
            text.Append(found.Text);

            return new LlmToolOutput { Text = text.ToString(), IsError = found.IsError };
        }

        private static JsonElement? argumentsOf(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return null;

            try
            {
                using var document = JsonDocument.Parse(argumentsJson);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    return null;

                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static LlmToolOutput refused(string why)
        {
            return new LlmToolOutput { Text = why, IsError = true };
        }
    }
}
