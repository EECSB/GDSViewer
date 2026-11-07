namespace GDSViewer.Components
{
    ///
    ///One entry of a <see cref="SavedPicker"/>: its key and the name it is shown by. From GraphDBViewer (2026-11-07), where
    ///its connections and its AI models share the control; here the AI models use it.
    ///
    public sealed record SavedPickerItem(string Key, string Name)
    {
        ///<summary>The name Duplicate gives the copy of <paramref name="name"/>: "Claude (copy)", then "(copy 2)" and on.</summary>
        public static string CopyName(string name, IEnumerable<string> taken)
        {
            var names = new HashSet<string>(taken);
            string wanted = $"{name} (copy)";

            if (!names.Contains(wanted))
                return wanted;

            for (int n = 2; ; n++)
            {
                string candidate = $"{name} (copy {n})";

                if (!names.Contains(candidate))
                    return candidate;
            }
        }

        ///
        ///<paramref name="entries"/> with the one under <paramref name="from"/> moved to <paramref name="to"/>, where it was
        ///in the order, rather than last as a remove and an add would put it.
        ///
        public static Dictionary<string, T> Renamed<T>(Dictionary<string, T> entries, string from, string to)
        {
            var renamed = new Dictionary<string, T>();

            foreach (var entry in entries)
            {
                if (entry.Key == from)
                    renamed[to] = entry.Value;
                else
                    renamed[entry.Key] = entry.Value;
            }

            return renamed;
        }

        ///<summary><paramref name="entries"/> with <paramref name="value"/> under <paramref name="key"/> right after <paramref name="after"/>.</summary>
        public static Dictionary<string, T> InsertedAfter<T>(Dictionary<string, T> entries, string after, string key, T value)
        {
            var inserted = new Dictionary<string, T>();

            foreach (var entry in entries)
            {
                inserted[entry.Key] = entry.Value;

                if (entry.Key == after)
                    inserted[key] = value;
            }

            if (!inserted.ContainsKey(key))
                inserted[key] = value;

            return inserted;
        }
    }
}
