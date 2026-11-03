namespace GDSViewer.Ai
{
    ///
    ///The deck's grammar as a model is given it, and deck lines read out of what a model sends - for the rule tools the AI assistant
    ///writes rules with (see <see cref="RuleTools"/>). Until 2026-10-31 this built the whole prompt of the PDK manager's
    ///own box, which asked a model for deck lines from one sentence; that box gave way to the AI assistant, asked for that day.
    ///
    ///**The grammar is the deck-writing guide's own prompt**, the part between its two ✂️ marks, so there is one
    ///description of the format and it is the one the guide hands out.
    ///
    public static class RulePrompt
    {
        private const string PromptStarts = "Everything below here is the prompt";
        private const string PromptEnds = "End of the prompt";

        ///
        ///The deck lines in what a model sent: the code fences dropped that models put round them however they are told,
        ///and the blank lines at either end, with the rest kept as it came.
        ///
        public static List<string> LinesIn(string answer)
        {
            var lines = new List<string>();

            foreach (string line in answer.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.TrimStart().StartsWith("```"))
                    continue;

                lines.Add(line.TrimEnd());
            }

            while (lines.Count > 0 && lines[0].Length == 0)
                lines.RemoveAt(0);

            while (lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);

            return lines;
        }

        ///<summary>The guide's prompt part, or the whole guide when its marks are not found.</summary>
        public static string Grammar(string guide)
        {
            int start = guide.IndexOf(PromptStarts, StringComparison.Ordinal);
            int end = guide.IndexOf(PromptEnds, StringComparison.Ordinal);

            if (start < 0 || end <= start)
                return guide.Trim();

            int from = guide.IndexOf('\n', start);

            if (from < 0 || from > end)
                return guide.Trim();

            int to = guide.LastIndexOf('\n', end);

            return guide.Substring(from + 1, to - from - 1).Trim();
        }
    }
}
