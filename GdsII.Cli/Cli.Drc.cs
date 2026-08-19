using System.Globalization;
using System.Text;

namespace GdsII.Cli
{
    ///<summary>
    ///Checking a layout against a deck of design rules.
    ///
    ///**The reason the engine got a command before it got a view.** Everything about a check is testable
    ///without a browser - a deck goes in, a list of faults comes out - and correctness work belongs where
    ///`dotnet test` can reach it. A view can come afterwards and be about drawing.
    ///
    ///It is also the shape the feature is most useful in. A rule check that runs in a script is a gate on a
    ///cell library; one that only runs when somebody is looking at a picture is a thing somebody has to
    ///remember to do.
    ///</summary>
    public static partial class Cli
    {
        #region drc *************************************************************************

        private static int drc(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "drc", error, out string path))
                return UsageError;

            string? deckPath = valueOf(args, "--deck");

            if (deckPath is null)
            {
                error.WriteLine("drc needs --deck <file>, the design rules to check against.");
                error.WriteLine("There is no standard format for one and no deck ships with this tool - a starter deck for sky130 is in wwwroot/resources/GDS Files/sky130A.drc, and docs/DRC.md says how to write one.");

                return UsageError;
            }

            if (!File.Exists(deckPath))
            {
                error.WriteLine($"No deck at \"{deckPath}\".");

                return FileError;
            }

            var deck = DrcDeck.Parse(File.ReadAllText(deckPath));

            if (deck.Rules.Count == 0 && deck.Refused.Count == 0)
            {
                error.WriteLine($"\"{deckPath}\" holds no rules.");

                foreach (string problem in deck.Problems)
                    error.WriteLine($"  {problem}");

                return FileError;
            }

            if (!read(path, error, out GDS? gds))
                return FileError;

            var layout = GdsFlattener.Flatten(gds!);

            var result = Drc.Check(deck, layout);

            string? only = valueOf(args, "--rule");

            report(result, deck, describe(path), describe(deckPath), only, args.Contains("--markers"), output);

            if (outputPath(args) is string destination && destination != "-")
            {
                File.WriteAllText(
                    destination,
                    DrcReport.Write(result, deck, gds!, topCellName(gds!), $"{describe(path)} against {describe(deckPath)}"));

                output.WriteLine();
                output.WriteLine($"Written to {destination}. KLayout opens it with Tools > Marker Browser.");
            }

            //Incomplete outranks a violation count, because it is the answer nobody can act on: a run that
            //skipped a rule does not know what it did not look at, where a run that found faults knows
            //exactly what it found.
            if (!result.Complete)
                return IncompleteCheck;

            if (result.Violations.Count > 0)
                return ViolationsFound;

            return Ok;
        }

        ///<summary>
        ///The cell a report is filed against: the first one nothing places.
        ///
        ///A report database names a top cell, and KLayout opens the marker browser onto it. Empty for a
        ///library that is all loop and so has no top at all, which the flattener already reports in its own
        ///way - a report naming no cell is a report that opens onto nothing, which is the honest outcome
        ///for a file nothing can be drawn from either.
        ///</summary>
        private static string topCellName(GDS gds)
        {
            foreach (var summary in Hierarchy.Summarize(gds))
            {
                if (summary.IsTop)
                    return summary.Name;
            }

            return "";
        }

        #endregion **************************************************************************



        #region Reporting *******************************************************************

        private static void report(
            DrcResult result,
            DrcDeck deck,
            string layout,
            string deckName,
            string? only,
            bool markers,
            TextWriter output)
        {
            output.WriteLine($"Checked {layout} against {deckName}.");
            output.WriteLine();

            var counted = countByRule(result, only);

            if (counted.Count > 0)
            {
                writeCounts(deck, counted, output);

                output.WriteLine();
            }

            if (markers)
                writeMarkers(result, only, output);

            writeNotRun(result, output);

            writeSummary(result, deck, counted, only, output);
        }

        ///<summary>
        ///How many each rule found, in the order the deck lists them rather than by count.
        ///
        ///A deck is written in an order somebody chose - front end before back end, and the metal stack in
        ///the order it is laid down - and a report that sorted by count would throw that away for a ranking
        ///nobody asked for.
        ///</summary>
        private static List<KeyValuePair<string, int>> countByRule(DrcResult result, string? only)
        {
            var counts = new Dictionary<string, int>();

            foreach (var violation in result.Violations)
            {
                if (only is not null && violation.RuleId != only)
                    continue;

                counts.TryGetValue(violation.RuleId, out int found);
                counts[violation.RuleId] = found + 1;
            }

            var ordered = new List<KeyValuePair<string, int>>();

            foreach (var violation in result.Violations)
            {
                if (counts.TryGetValue(violation.RuleId, out int found))
                {
                    ordered.Add(new KeyValuePair<string, int>(violation.RuleId, found));
                    counts.Remove(violation.RuleId);
                }
            }

            return ordered;
        }

        private static void writeCounts(DrcDeck deck, List<KeyValuePair<string, int>> counted, TextWriter output)
        {
            var described = new Dictionary<string, DrcRule>();

            foreach (var rule in deck.Rules)
                described[rule.Id] = rule;

            int idColumn = Math.Max(8, counted.Max(entry => entry.Key.Length) + 2);

            output.WriteLine($"{"rule".PadRight(idColumn)} {"check",-10} {"limit",10} {"found",7}   description");

            foreach (var entry in counted)
            {
                string check = "";
                string limit = "";
                string says = "";

                if (described.TryGetValue(entry.Key, out var rule))
                {
                    check = rule.Check.ToString().ToLowerInvariant();
                    limit = rule.Value.ToString(CultureInfo.InvariantCulture);
                    says = rule.Description;
                }

                output.WriteLine($"{entry.Key.PadRight(idColumn)} {check,-10} {limit,10} {entry.Value,7}   {says}");
            }
        }

        ///<summary>
        ///Every violation with where it is and, where one could be found, which cell it belongs to.
        ///
        ///The cell is the column worth having. A fault is found on flattened geometry where a shape may be
        ///one of a thousand placements, and the coordinate to change is the one inside the cell rather than
        ///the one printed beside it.
        ///</summary>
        private static void writeMarkers(DrcResult result, string? only, TextWriter output)
        {
            var shown = new List<DrcViolation>();

            foreach (var violation in result.Violations)
            {
                if (only is null || violation.RuleId == only)
                    shown.Add(violation);
            }

            if (shown.Count == 0)
                return;

            int idColumn = Math.Max(8, shown.Max(violation => violation.RuleId.Length) + 2);

            output.WriteLine($"{"rule".PadRight(idColumn)} {"where",-34} cell");

            foreach (var violation in shown)
            {
                string where = at(violation);

                string cell = "";

                if (violation.Source is ElementSource source)
                    cell = string.Join(" > ", source.Path);

                output.WriteLine($"{violation.RuleId.PadRight(idColumn)} {where,-34} {cell}");
            }

            output.WriteLine();
        }

        ///<summary>
        ///Where a violation is, in database units.
        ///
        ///A box for every check but off-grid, whose fault is a single coordinate and has no box to give -
        ///printing one would be inventing an extent for something that has none.
        ///</summary>
        private static string at(DrcViolation violation)
        {
            var bounds = violation.Bounds;

            if (violation.Check == DrcCheck.OffGrid)
                return $"{bounds.Left},{bounds.Bottom}";

            return $"{bounds.Left},{bounds.Bottom} to {bounds.Right},{bounds.Top}";
        }

        private static void writeNotRun(DrcResult result, TextWriter output)
        {
            if (result.NotRun.Count > 0)
            {
                output.WriteLine($"{result.NotRun.Count} rule(s) did not run:");

                foreach (string entry in result.NotRun)
                    output.WriteLine($"  {entry}");

                output.WriteLine();
            }

            if (result.Problems.Count == 0)
                return;

            output.WriteLine($"{result.Problems.Count} problem(s) reading the deck:");

            foreach (string problem in result.Problems)
                output.WriteLine($"  {problem}");

            output.WriteLine();
        }

        ///<summary>
        ///The line somebody actually reads, and the one place the word "clean" is allowed.
        ///
        ///**It is never printed over a run that skipped something.** A deck may hold a check this build
        ///cannot measure or a derivation that goes round in a circle, and either is a rule that quietly
        ///measured nothing - so a summary saying the layout is clean would be a claim nobody checked. The
        ///exit code says the same thing to whatever is scripting this.
        ///</summary>
        private static void writeSummary(
            DrcResult result,
            DrcDeck deck,
            List<KeyValuePair<string, int>> counted,
            string? only,
            TextWriter output)
        {
            int found = 0;

            foreach (var entry in counted)
                found += entry.Value;

            if (only is not null)
                output.WriteLine($"Only {only} was reported; the rest of the deck still ran.");

            if (!result.Complete)
            {
                output.WriteLine($"{found} violation(s) from {counted.Count} rule(s), of {deck.Rules.Count} that the deck holds.");
                output.WriteLine();
                output.WriteLine("This layout has NOT been fully checked. Nothing here says it is clean.");

                return;
            }

            if (found == 0)
            {
                output.WriteLine($"No violations. All {deck.Rules.Count} rule(s) ran.");

                return;
            }

            output.WriteLine($"{found} violation(s) from {counted.Count} of {deck.Rules.Count} rule(s).");
        }

        #endregion **************************************************************************
    }
}
