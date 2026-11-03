using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdsII;

namespace GDSViewer.Ai
{
    ///
    ///What the rule and layer tools need from the page: the PDK they write into, its deck and layermap, the guides to
    ///each, and a way to put a new deck or layermap in place. Callbacks rather than the PDK list itself, so the tools know
    ///nothing of where PDKs are kept - see Viewer.razor's ruleBook, which aims them at the PDK the PDK manager's Write with
    ///AI was pressed on, or the one in use. See <see cref="RuleTools"/> and <see cref="LayerTools"/>.
    ///
    public sealed class RuleBook
    {
        ///<summary>The PDK's name, as the person sees it.</summary>
        public required Func<string> PdkName { get; init; }

        ///<summary>Its deck as it stands, as text.</summary>
        public required Func<string> Deck { get; init; }

        ///<summary>Its layers, one to a line: name, number/datatype and role.</summary>
        public required Func<IReadOnlyList<string>> Layers { get; init; }

        ///<summary>The deck-writing guide, whose prompt part is the grammar - see RulePrompt.Grammar.</summary>
        public required Func<Task<string>> Guide { get; init; }

        ///<summary>Puts a new deck in place of the PDK's, kept and laid over the open file as any change in the PDK manager is.</summary>
        public required Func<string, Task> Replace { get; init; }

        ///<summary>Its layermap as it stands, as text.</summary>
        public required Func<string> Layermap { get; init; }

        ///<summary>The layermap-writing guide, whose prompt part is the format - see RulePrompt.Grammar.</summary>
        public required Func<Task<string>> LayermapGuide { get; init; }

        ///<summary>Puts a new layermap in place of the PDK's, kept and laid over the open file as any change in the PDK manager is.</summary>
        public required Func<string, Task> ReplaceLayers { get; init; }
    }

    ///<summary>What the deck's reader makes of lines to add: how many rules they hold, and every problem they bring.</summary>
    public sealed record RuleLinesWeighed(int Rules, IReadOnlyList<string> Problems, IReadOnlyList<string> Ids);

    ///
    ///**Rules written in the AI assistant** (2026-10-31, asked for), in place of the PDK manager's box that asked a model for deck lines
    ///from one sentence: the assistant reads a PDK's deck, layers and grammar with <see cref="ReadName"/> and adds lines with
    ///<see cref="AddName"/> - a change, so a card waits on it on the rungs that ask before changes, and shows the deck's
    ///diff. The lines are read as the deck would read them before anything goes in, and refused with every problem when
    ///they bring any, so the model can mend them; what goes in goes where each kind goes (DrcDeck.WithLinesAdded).
    ///
    public static class RuleTools
    {
        public const string ReadName = "read_rules";
        public const string AddName = "add_rules";

        public static LlmTool ReadTool
        {
            get
            {
                return new LlmTool
                {
                    Name = ReadName,
                    Description = "The design rules of the PDK the person is writing rules for: its rule deck as it stands, its layers by name, number and role, "
                        + "and the deck's grammar - how a layer, a derived layer and each kind of rule is written, in database units. "
                        + "Call it before add_rules, and when asked what a rule says.",
                    InputSchemaJson = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }.ToJsonString()
                };
            }
        }

        public static LlmTool AddTool
        {
            get
            {
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["lines"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "The deck lines to add, one statement to a line, exactly as they would stand in the file: a layer line for each layer the "
                                + "rules name that the deck does not declare, a derive line for each derived layer they need that it does not have, then the rule "
                                + "lines, each with a short description in double quotes and an id the deck does not use. Values in database units."
                        }
                    },
                    ["required"] = new JsonArray("lines")
                };

                return new LlmTool
                {
                    Name = AddName,
                    Description = "Adds rules to that PDK's deck, written in its grammar - read it with read_rules first. Every file drawn in the PDK is checked "
                        + "against them from then on. Lines the deck cannot read, a check it does not have, or an id it already uses are refused, with why, and nothing goes in.",
                    InputSchemaJson = schema.ToJsonString(),
                    Changes = true
                };
            }
        }

        ///
        ///What the deck's reader makes of lines to add - read after the deck's own declarations and none of its rules, so a
        ///problem the deck already had is not blamed on them. An id the deck already uses is one too. Moved here from the
        ///PDK manager's box, which weighed a model's answer the same way.
        ///
        public static RuleLinesWeighed Weigh(IReadOnlyList<string> lines, string deckNow)
        {
            string declared = DrcDeck.WithoutRules(deckNow);
            var before = DrcDeck.Parse(declared);
            var alone = DrcDeck.Parse(declared + "\n" + string.Join("\n", lines));
            var taken = new HashSet<string>(DrcDeck.Parse(deckNow).Rules.Select(each => each.Id), StringComparer.OrdinalIgnoreCase);

            var problems = alone.Problems.Concat(alone.Refused).Where(each => !before.Problems.Contains(each) && !before.Refused.Contains(each)).ToList();

            foreach (var rule in alone.Rules)
            {
                if (taken.Contains(rule.Id))
                    problems.Add($"{rule.Id} is already a rule in this deck - give it an id the deck does not use.");
            }

            return new RuleLinesWeighed(alone.Rules.Count, problems, alone.Rules.Select(each => each.Id).ToList());
        }

        public static async Task<LlmToolOutput> ReadAsync(RuleBook book)
        {
            string guide = await book.Guide();
            var text = new StringBuilder();

            text.AppendLine($"The PDK is {book.PdkName()}.");
            text.AppendLine();
            text.AppendLine("Its layers (name, number/datatype, role):");

            var layers = book.Layers();

            if (layers.Count == 0)
                text.AppendLine("(none yet)");

            foreach (string layer in layers)
                text.AppendLine(layer);

            text.AppendLine();
            text.AppendLine("Its deck as it stands:");

            string deck = book.Deck();

            if (deck.Trim().Length == 0)
                text.AppendLine("(empty)");
            else
                text.AppendLine(deck.TrimEnd());

            text.AppendLine();
            text.AppendLine("The deck's grammar:");
            text.AppendLine(RulePrompt.Grammar(guide));

            return new LlmToolOutput { Text = text.ToString() };
        }

        ///<summary>Adds the lines asked for, or refuses them with every problem. The card shows the deck's diff.</summary>
        public static async Task<LlmToolOutput> AddAsync(RuleBook book, string argumentsJson)
        {
            string asked;

            try
            {
                asked = linesIn(argumentsJson);
            }
            catch (JsonException wrong)
            {
                return refused($"The arguments to {AddName} are not JSON: {wrong.Message}");
            }

            var lines = RulePrompt.LinesIn(asked);

            if (lines.Count == 0)
                return refused($"{AddName} needs the deck lines to add, in lines.");

            string deckNow = book.Deck();
            var weighed = Weigh(lines, deckNow);

            if (weighed.Rules == 0)
                return refused("No rule in those lines, so nothing went in." + listed(weighed.Problems));

            if (weighed.Problems.Count > 0)
                return refused($"Nothing went in: the deck would read {counted(weighed.Problems.Count, "problem")} in those lines." + listed(weighed.Problems));

            string after = DrcDeck.WithLinesAdded(deckNow, lines);

            await book.Replace(after);

            return new LlmToolOutput
            {
                Text = $"Added {counted(weighed.Rules, "rule")} to {book.PdkName()}'s deck: {string.Join(", ", weighed.Ids)}. Every file drawn in it is checked against them from now on; "
                    + "they are taken out again in the PDK manager, not with Undo.",
                TextChange = TextChange.Between(deckNow, after)
            };
        }

        private static string linesIn(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return "";

            using var document = JsonDocument.Parse(argumentsJson);

            var root = document.RootElement;

            //Some providers send the arguments as a JSON string holding the JSON - read through, the way AgentTools does.
            if (root.ValueKind == JsonValueKind.String)
                return linesIn(root.GetString() ?? "");

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("lines", out var lines))
                return "";

            if (lines.ValueKind == JsonValueKind.String)
                return lines.GetString() ?? "";

            //An array of lines, which some models send for "lines" however they are told.
            if (lines.ValueKind == JsonValueKind.Array)
                return string.Join("\n", lines.EnumerateArray().Where(each => each.ValueKind == JsonValueKind.String).Select(each => each.GetString()));

            return "";
        }

        private static string listed(IReadOnlyList<string> problems)
        {
            if (problems.Count == 0)
                return "";

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
