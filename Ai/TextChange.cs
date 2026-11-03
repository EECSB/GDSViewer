namespace GDSViewer.Ai
{
    ///
    ///What one change the assistant made did to the file's text - the record dump the text view shows, a line a record -
    ///as the lines round it before and after, for its card to show as a diff. Asked for 2026-10-21: whichever view a
    ///change was asked for from, the text is what changed, and a diff of it says exactly what was added, taken away and
    ///altered.
    ///
    ///**Only the part that changed**, with <see cref="Context"/> lines either side of it: a card keeps this for as long as
    ///the conversation is up, and the editor that shows it works the diff out again from what it is handed. The part is
    ///found from both ends, as the lines before it and after it that are the same in both texts - for one change in one
    ///place exactly that change, and for two far apart everything between them, which the diff shows as unchanged.
    ///
    public sealed record TextChange(string Before, string After, int FirstLine)
    {
        ///<summary>How many unchanged lines are kept either side of what changed, to show where in the text it is.</summary>
        public const int Context = 3;

        private static int made;

        ///<summary>Which change this is, for the page to tell one card's diff from another's.</summary>
        public int Id { get; } = Interlocked.Increment(ref made);

        ///
        ///The lines that differ between two texts, with <see cref="Context"/> either side; <paramref name="firstLine"/>
        ///is the line the texts start on in the file, one for the whole of it. Null when the two are the same.
        ///
        public static TextChange? Between(string before, string after, int firstLine = 1)
        {
            if (before == after)
                return null;

            string[] was = linesOf(before);
            string[] now = linesOf(after);

            int head = 0;

            while (head < was.Length && head < now.Length && was[head] == now[head])
                head++;

            int tail = 0;

            while (tail < was.Length - head && tail < now.Length - head && was[was.Length - 1 - tail] == now[now.Length - 1 - tail])
                tail++;

            int from = Math.Max(0, head - Context);
            int wasEnd = Math.Min(was.Length, was.Length - tail + Context);
            int nowEnd = Math.Min(now.Length, now.Length - tail + Context);

            return new TextChange(string.Join('\n', was[from..wasEnd]), string.Join('\n', now[from..nowEnd]), firstLine + from);
        }

        ///<summary>A text's lines. It ends every line with a newline, its last included, which starts no line of its own.</summary>
        private static string[] linesOf(string text)
        {
            if (text.EndsWith('\n'))
                return text.Substring(0, text.Length - 1).Split('\n');

            return text.Split('\n');
        }
    }
}
