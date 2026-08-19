using System.Text.Json;
using System.Text.Json.Serialization;

namespace GDSViewer.Models
{
    ///<summary>
    ///One line of the history list: what the file was called and enough about it to draw the row.
    ///
    ///**Deliberately small.** What the entry actually holds - the file's bytes and everything that was set
    ///on it - is a <see cref="SavedSession"/> stored under its own key; this is only what the popup needs to
    ///list it. The two are split because a history of twenty layouts is megabytes, and opening the popup
    ///must not read all of it to draw twenty names.
    ///
    ///Short property names for the same reason <see cref="SavedSession"/> uses them: the index is compressed
    ///and base64'd, and key text is paid for in every entry.
    ///</summary>
    public class HistoryEntry
    {
        ///<summary>
        ///The file as it is named on screen, extension included - "Mosfet.gds". Also the identity: opening
        ///the same file again updates this entry rather than adding a second one, which is what makes the
        ///list a history rather than a log.
        ///</summary>
        [JsonPropertyName("n")]
        public string Name { get; set; } = "";

        ///<summary>
        ///The bundled example this came from, or empty for a file off the user's machine. Kept because an
        ///unedited example's entry holds no bytes - it can be fetched again by name.
        ///</summary>
        [JsonPropertyName("e")]
        public string ExampleName { get; set; } = "";

        ///<summary>
        ///Whether the records themselves were changed, as opposed to only how they are drawn. Shown on the
        ///row, because "Mosfet.gds" twice over - once as the bundled cell and once as the copy someone cut
        ///a transistor out of - is otherwise the same line of text.
        ///</summary>
        [JsonPropertyName("d")]
        public bool Edited { get; set; }

        ///<summary>
        ///When this was last written, as a round-trip UTC string. Not what the list is ordered by - the
        ///order is the list's own, so bumping an entry is moving it rather than restamping it - but it is
        ///what the row's tooltip says, and a clock that has been wound back cannot scramble the order.
        ///</summary>
        [JsonPropertyName("t")]
        public string When { get; set; } = "";
    }

    ///<summary>
    ///The history list itself: the entries, newest first.
    ///
    ///A wrapper rather than a bare array so it can carry a version, for the same reason
    ///<see cref="SavedSession"/> does - a shape change has to be recognizable rather than misread.
    ///</summary>
    public class HistoryIndex
    {
        [JsonPropertyName("v")]
        public int Version { get; set; } = CurrentVersion;

        ///<summary>Newest first. Position is the ordering; nothing is sorted on read.</summary>
        [JsonPropertyName("i")]
        public List<HistoryEntry> Entries { get; set; } = new List<HistoryEntry>();

        public const int CurrentVersion = 1;

        ///<summary>
        ///Puts an entry at the front, and hands back the files that fell off the end so their payloads can
        ///be deleted along with their rows.
        ///
        ///Removing first is what makes this a history rather than a log: opening the same file twice moves
        ///the one row up instead of leaving an older copy of it further down the list, which would then be
        ///restorable and hand back a state that has been superseded.
        ///
        ///Ordering is the list's own order, so this is a move rather than a restamp. A timestamp would have
        ///to be trusted, and a clock that has been wound back - or a machine that never set one - would
        ///scramble the list.
        ///</summary>
        public IReadOnlyList<string> Remember(HistoryEntry entry, int capacity)
        {
            Entries.RemoveAll(each => IsSameFile(each.Name, entry.Name));
            Entries.Insert(0, entry);

            var dropped = new List<string>();

            //Capped because each entry can hold a whole layout. Without a limit the store grows for as long
            //as the app is used and the browser eventually refuses a write - which would cost the *current*
            //session, since that is written to the same quota.
            while (Entries.Count > capacity && capacity > 0)
            {
                dropped.Add(Entries[^1].Name);
                Entries.RemoveAt(Entries.Count - 1);
            }

            return dropped;
        }

        ///<summary>Drops one file's row. False if it was not listed, so a caller can skip the write.</summary>
        public bool Forget(string name)
        {
            return Entries.RemoveAll(each => IsSameFile(each.Name, name)) > 0;
        }

        public bool Contains(string name)
        {
            return Entries.Any(each => IsSameFile(each.Name, name));
        }

        ///<summary>
        ///Whether two rows are the same file. By name, ignoring case: a file's identity here is what it is
        ///called, and Windows would otherwise let "Mosfet.gds" and "mosfet.gds" be two rows for one file.
        ///</summary>
        private static bool IsSameFile(string one, string other)
        {
            return string.Equals(one, other, StringComparison.OrdinalIgnoreCase);
        }

        public static string Serialize(HistoryIndex index)
        {
            return JsonSerializer.Serialize(index, SavedJson.Default.HistoryIndex);
        }

        ///<summary>
        ///Reads an index back, or an empty one if it cannot be trusted. Never throws and never returns null:
        ///this is read on the path that starts the app, and a corrupted index has to cost the history rather
        ///than the page.
        ///</summary>
        public static HistoryIndex Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new HistoryIndex();

            try
            {
                var index = JsonSerializer.Deserialize(json, SavedJson.Default.HistoryIndex);

                if (index is null || index.Version != CurrentVersion)
                    return new HistoryIndex();

                //A null list is what a hand-edited or truncated value deserializes to, and every caller
                //here walks it.
                index.Entries ??= new List<HistoryEntry>();

                //An entry with no name has no payload key and cannot be opened or deleted, so it would sit
                //in the list forever doing nothing.
                index.Entries.RemoveAll(entry => entry is null || string.IsNullOrEmpty(entry.Name));

                return index;
            }
            catch
            {
                return new HistoryIndex();
            }
        }
    }
}
