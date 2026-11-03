using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdsII;

namespace GDSViewer.Ai
{
    ///
    ///**Layers written in the AI assistant** (2026-11-01, asked for), as its rules are (see <see cref="RuleTools"/>): the
    ///assistant reads a PDK's layermap and its format with <see cref="ReadName"/>, and adds or changes rows with
    ///<see cref="SetName"/> - a change, so a card waits on it on the rungs that ask before changes, and shows the layermap's
    ///diff. The rows are laid over the layermap the way an Import into the PDK manager lays a file over it: what a row says
    ///about a layer replaces what the layermap said, and a column it leaves empty keeps what was there. Rows that do not
    ///read are refused with every problem, so the model can mend them, and nothing goes in.
    ///
    public static class LayerTools
    {
        public const string ReadName = "read_layers";
        public const string SetName = "set_layers";

        public static LlmTool ReadTool
        {
            get
            {
                return new LlmTool
                {
                    Name = ReadName,
                    Description = "The layers of the PDK the person is working on: its layermap as it stands - a CSV row per layer with its number, datatype, name, "
                        + "color, height, thickness and role - and the layermap's format. Call it before set_layers, and when asked what a PDK's layers are.",
                    InputSchemaJson = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }.ToJsonString()
                };
            }
        }

        public static LlmTool SetTool
        {
            get
            {
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["rows"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Layermap rows to add or change, one layer to a line, in the layermap's CSV format: layer,datatype,name and then the optional "
                                + "columns. A row for a layer already there replaces the columns it fills in and keeps the ones it leaves empty."
                        },
                        ["remove"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject { ["type"] = "string" },
                            ["description"] = "Layers to take out of the PDK, each as number/datatype, like 68/20. Leave it out to remove none."
                        }
                    }
                };

                return new LlmTool
                {
                    Name = SetName,
                    Description = "Adds layers to that PDK's layermap, changes ones there, or takes them out - read it with read_layers first. Every file drawn in the PDK "
                        + "is named and drawn by it from then on. Rows that do not read, or a layer to remove that the PDK does not have, are refused, with why, and nothing changes.",
                    InputSchemaJson = schema.ToJsonString(),
                    Changes = true
                };
            }
        }

        public static async Task<LlmToolOutput> ReadAsync(RuleBook book)
        {
            string guide = await book.LayermapGuide();
            var text = new StringBuilder();

            text.AppendLine($"The PDK is {book.PdkName()}.");
            text.AppendLine();
            text.AppendLine("Its layermap as it stands:");

            var layermap = LayerNames.Parse(book.Layermap());

            if (layermap.Count == 0)
                text.AppendLine("(no layers yet)");
            else
                text.AppendLine(layermap.Write().TrimEnd());

            text.AppendLine();
            text.AppendLine("The layermap's format - set_layers takes rows in it, laid over the layermap above rather than replacing it:");
            text.AppendLine(RulePrompt.Grammar(guide));

            return new LlmToolOutput { Text = text.ToString() };
        }

        ///<summary>Lays the rows asked for over the layermap and takes out the layers asked for, or refuses them with every problem. The card shows the layermap's diff.</summary>
        public static async Task<LlmToolOutput> SetAsync(RuleBook book, string argumentsJson)
        {
            string rows;
            List<string> removing;

            try
            {
                (rows, removing) = argumentsIn(argumentsJson);
            }
            catch (JsonException wrong)
            {
                return refused($"The arguments to {SetName} are not JSON: {wrong.Message}");
            }

            var lines = RulePrompt.LinesIn(rows);

            if (lines.Count == 0 && removing.Count == 0)
                return refused($"{SetName} needs layermap rows in rows, or layers to take out in remove.");

            var asked = LayerNames.Parse(string.Join("\n", lines));
            var problems = new List<string>(asked.Problems);

            if (lines.Count > 0 && asked.Count == 0 && problems.Count == 0)
                problems.Add("Those rows name no layer.");

            string before = LayerNames.Parse(book.Layermap()).Write();
            var layermap = LayerNames.Parse(before);
            var present = new HashSet<LayerKey>(layermap.Pairs);
            var taken = new List<LayerKey>();

            foreach (string pair in removing)
            {
                if (pairIn(pair) is not LayerKey key)
                    problems.Add($"\"{pair}\" in remove is not a layer - write it as number/datatype, like 68/20.");
                else if (!present.Contains(key))
                    problems.Add($"{key} in remove is not a layer of this PDK.");
                else
                    taken.Add(key);
            }

            if (problems.Count > 0)
                return refused($"Nothing changed: {counted(problems.Count, "problem")} in what was asked." + listed(problems));

            foreach (var key in taken)
                layermap.Remove(key);

            layermap.MergeFrom(asked);

            string after = layermap.Write();

            if (after == before)
                return new LlmToolOutput { Text = $"{book.PdkName()}'s layermap already says that, so nothing changed." };

            await book.ReplaceLayers(after);

            return new LlmToolOutput
            {
                Text = said(book.PdkName(), asked, present, taken) + " Every file drawn in it is named and drawn by them from now on; they are changed back in the PDK manager, not with Undo.",
                TextChange = TextChange.Between(before, after)
            };
        }

        ///<summary>What a change did, by layer: which were added, which changed and which were taken out.</summary>
        private static string said(string pdk, LayerNames asked, HashSet<LayerKey> present, List<LayerKey> taken)
        {
            var added = asked.Pairs.Where(key => !present.Contains(key)).ToList();
            var changed = asked.Pairs.Where(key => present.Contains(key)).ToList();
            var parts = new List<string>();

            if (added.Count > 0)
                parts.Add($"added {counted(added.Count, "layer")} ({string.Join(", ", added)})");

            if (changed.Count > 0)
                parts.Add($"changed {counted(changed.Count, "layer")} ({string.Join(", ", changed)})");

            if (taken.Count > 0)
                parts.Add($"took out {counted(taken.Count, "layer")} ({string.Join(", ", taken)})");

            return $"In {pdk}'s layermap: {string.Join("; ", parts)}.";
        }

        ///<summary>A layer written as number/datatype, or null when it is not one.</summary>
        private static LayerKey? pairIn(string text)
        {
            string[] parts = text.Trim().Split('/');

            if (parts.Length != 2)
                return null;

            if (!short.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out short number))
                return null;

            if (!short.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out short dataType))
                return null;

            return new LayerKey(number, dataType);
        }

        private static (string Rows, List<string> Remove) argumentsIn(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return ("", new List<string>());

            using var document = JsonDocument.Parse(argumentsJson);

            var root = document.RootElement;

            //Some providers send the arguments as a JSON string holding the JSON - read through, the way RuleTools does.
            if (root.ValueKind == JsonValueKind.String)
                return argumentsIn(root.GetString() ?? "");

            if (root.ValueKind != JsonValueKind.Object)
                return ("", new List<string>());

            string rows = "";

            if (root.TryGetProperty("rows", out var given))
            {
                if (given.ValueKind == JsonValueKind.String)
                    rows = given.GetString() ?? "";

                //An array of rows, which some models send however they are told.
                else if (given.ValueKind == JsonValueKind.Array)
                    rows = string.Join("\n", given.EnumerateArray().Where(each => each.ValueKind == JsonValueKind.String).Select(each => each.GetString()));
            }

            var remove = new List<string>();

            if (root.TryGetProperty("remove", out var removing))
            {
                if (removing.ValueKind == JsonValueKind.Array)
                    remove.AddRange(removing.EnumerateArray().Where(each => each.ValueKind == JsonValueKind.String).Select(each => each.GetString() ?? ""));

                //One pair, or several with commas, as a string.
                else if (removing.ValueKind == JsonValueKind.String)
                    remove.AddRange((removing.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }

            return (rows, remove);
        }

        private static string listed(IReadOnlyList<string> problems)
        {
            return "\n- " + string.Join("\n- ", problems.Take(20));
        }

        private static string counted(int count, string thing)
        {
            if (count == 1)
                return $"1 {thing}";

            return $"{count} {thing}s";
        }

        private static LlmToolOutput refused(string why)
        {
            return new LlmToolOutput { Text = why, IsError = true };
        }
    }
}
