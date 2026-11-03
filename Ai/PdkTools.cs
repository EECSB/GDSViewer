using System.Text.Json;
using System.Text.Json.Nodes;
using GdsII;

namespace GDSViewer.Ai
{
    ///
    ///What use_pdk needs from the page: the PDKs by name, the one in use, whether the open layout waits for one's layers,
    ///a way to put one in use - see Viewer.razor's pdkShelf, which does what the PDK manager's use pill and the empty layer
    ///list's Set PDK do - and the deck the open file is checked against once it is. See <see cref="PdkTools"/>.
    ///
    public sealed class PdkShelf
    {
        ///<summary>The PDKs, by the names the person sees, in the PDK manager's order.</summary>
        public required Func<IReadOnlyList<string>> Names { get; init; }

        ///<summary>The name of the one in use.</summary>
        public required Func<string> InUse { get; init; }

        ///<summary>Whether the open layout has no layers yet and waits for a PDK's - the empty layer list's Set PDK.</summary>
        public required Func<bool> Waiting { get; init; }

        ///<summary>Puts the PDK of that name in use and lays on the layers of it the layout lacks - every one, on a layout that waits for one.</summary>
        public required Func<string, Task<PdkUsed>> Use { get; init; }

        ///<summary>The deck the open file is checked against, which putting a PDK in use can change.</summary>
        public required Func<DrcDeck?> Deck { get; init; }
    }

    ///<summary>What putting a PDK in use did: the layers it laid on, as the layer list names them, whether it names this file's, and whether it was in use already.</summary>
    public sealed record PdkUsed(IReadOnlyList<string> Laid, bool NamesThisFile, bool WasInUse);

    ///
    ///**A PDK put in use from the AI assistant** (2026-11-01, asked for). Told to use sky130 on a new layout, the assistant
    ///drew on 66/20 and 95/20 one layer at a time, each coming in gray and alone, since nothing let it do what Set PDK does.
    ///<see cref="UseName"/> puts one of the person's PDKs in use - it names and checks every file drawn in it - and on a
    ///layout with no layers yet lays that PDK's layers on, named, colored and stacked, as the empty layer list's Set PDK
    ///does. A change, so a card waits on it on the rungs that ask before changes. The layers laid on are not a step on Undo,
    ///any more than Set PDK's are: a layer nothing is drawn on is not in the file until something is.
    ///
    ///**And the ones a layout drawn in it lacks** (asked for the same day): a layout whose first layers came in one at a
    ///time, gray, gets the rest of the PDK's round them, and the gray ones a color. Not over a file from another process,
    ///whose layers the PDK would name wrongly - that is said instead.
    ///
    public static class PdkTools
    {
        public const string UseName = "use_pdk";

        public static LlmTool UseTool
        {
            get
            {
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["pdk"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The PDK's name, as the person's PDKs are listed - like sky130, gf180mcu or ihp-sg13g2."
                        }
                    },
                    ["required"] = new JsonArray("pdk")
                };

                return new LlmTool
                {
                    Name = UseName,
                    Description = "Puts one of the person's PDKs in use: it names the layers of every file drawn in it and brings its design rules. "
                        + "It also lays that PDK's layers on, named, colored and stacked, ready to draw on by name: every one on a layout with no layers yet - "
                        + "what the layer panel's Set PDK does - and the ones a layout drawn in it lacks. "
                        + "Call it before drawing on a new layout when the person names a PDK, and when they ask for a PDK's layers on a layout that has only some.",
                    InputSchemaJson = schema.ToJsonString(),
                    Changes = true
                };
            }
        }

        ///<summary>Puts the PDK asked for in use, or refuses one there is none of with the names there are.</summary>
        public static async Task<LlmToolOutput> UseAsync(PdkShelf shelf, string argumentsJson)
        {
            string asked;

            try
            {
                asked = pdkIn(argumentsJson).Trim();
            }
            catch (JsonException wrong)
            {
                return refused($"The arguments to {UseName} are not JSON: {wrong.Message}");
            }

            var names = shelf.Names();

            if (asked.Length == 0)
                return refused($"{UseName} needs the PDK's name, in pdk: one of {string.Join(", ", names)}.");

            string? name = names.FirstOrDefault(each => string.Equals(each, asked, StringComparison.OrdinalIgnoreCase));

            if (name is null)
                return refused($"There is no PDK called {asked}. The person's PDKs are {string.Join(", ", names)}; another is brought in or made in the PDK manager.");

            bool waiting = shelf.Waiting();
            var used = await shelf.Use(name);
            string laid = string.Join(", ", used.Laid);
            string undo = "Undo does not take them back; a layer nothing is drawn on is not written to the file.";

            if (used.Laid.Count > 0 && waiting)
                return said($"{name} is in use, and its {used.Laid.Count} layers are laid on this layout, ready to draw on by name: {laid}. {undo}");

            if (used.Laid.Count == 1)
                return said($"{name} is in use, and the 1 of its layers this layout did not have is laid on beside the ones it had, ready to draw on by name: {laid}. {undo}");

            if (used.Laid.Count > 1)
                return said($"{name} is in use, and the {used.Laid.Count} of its layers this layout did not have are laid on beside the ones it had, ready to draw on by name: {laid}. {undo}");

            if (!used.NamesThisFile)
                return said($"{name} is in use, for every file drawn in it - but this file's layers are not mostly {name}'s, so it names and checks nothing here, and its layers were not laid on.");

            if (waiting)
                return said($"{name} is in use, but it has no layers to lay on: its layermap is empty. Give it some in the PDK manager, or draw on layer numbers.");

            if (used.WasInUse)
                return said($"{name} is already in use, and this layout has every one of its layers: nothing changed.");

            return said($"{name} is in use: it names this file's layers and checks it against its rules, and the layout has every one of its layers already.");
        }

        private static LlmToolOutput said(string text)
        {
            return new LlmToolOutput { Text = text };
        }

        private static string pdkIn(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return "";

            using var document = JsonDocument.Parse(argumentsJson);

            var root = document.RootElement;

            //Some providers send the arguments as a JSON string holding the JSON - read through, the way RuleTools does.
            if (root.ValueKind == JsonValueKind.String)
                return pdkIn(root.GetString() ?? "");

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("pdk", out var pdk) || pdk.ValueKind != JsonValueKind.String)
                return "";

            return pdk.GetString() ?? "";
        }

        private static LlmToolOutput refused(string why)
        {
            return new LlmToolOutput { Text = why, IsError = true };
        }
    }
}
