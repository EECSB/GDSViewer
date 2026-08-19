namespace GDSViewer.Models
{
    ///<summary>
    ///The files that have been opened, kept so they can be opened again exactly as they were left.
    ///
    ///**A small index, and one payload per file.** The list of rows lives under one key and each file's
    ///state lives under its own, because an entry is a whole <see cref="SavedSession"/> - bytes included -
    ///and a history of twenty layouts is megabytes. Opening the popup reads the index alone; a file's bytes
    ///are read when that row is chosen, or when it is pointed at long enough to be worth drawing.
    ///
    ///**Best effort, like everything else over <see cref="AppStorage"/>.** A write that fails on a full
    ///quota costs a row rather than the file on screen.
    ///</summary>
    public class HistoryStore
    {
        #region Constants *******************************************************************

        ///<summary>Where the list of rows lives. The payloads hang off it, one key per file.</summary>
        public const string IndexKey = "gdsviewer.history";

        ///<summary>
        ///How many files are kept.
        ///
        ///A number rather than a size, because a size cannot be known before the write: what a layout costs
        ///in the store is what it deflates to. Twenty is enough to cover a session's worth of moving between
        ///cells and small enough that the worst case - twenty large uploads - stays well inside a quota that
        ///is a share of free disk.
        ///</summary>
        public const int Capacity = 20;

        #endregion **************************************************************************



        #region Fields **********************************************************************

        private readonly AppStorage storage;

        ///<summary>
        ///The index, once read. Cached because this is its only writer and it is consulted on every save -
        ///re-reading it there would put an IndexedDB round trip in front of every change.
        ///</summary>
        private HistoryIndex? index;

        #endregion **************************************************************************



        public HistoryStore(AppStorage storage)
        {
            this.storage = storage;
        }

        #region Reading *********************************************************************

        ///<summary>The rows, newest first. Read once and then held.</summary>
        public async Task<IReadOnlyList<HistoryEntry>> ListAsync()
        {
            return (await loadAsync()).Entries;
        }

        ///<summary>Whether this file already has a row, which is what decides if a save updates one.</summary>
        public async Task<bool> ContainsAsync(string name)
        {
            return (await loadAsync()).Contains(name);
        }

        ///<summary>
        ///One file's saved state, or null when it cannot be read.
        ///
        ///Null covers a payload that was lost - a quota that refused the write, or storage cleared out from
        ///under the app - which the caller has to handle rather than assume away: the row exists, so it is
        ///offered, and choosing it has to say something rather than open nothing.
        ///</summary>
        public async Task<SavedSession?> ReadAsync(string name)
        {
            return SavedSession.Deserialize(await storage.GetAsync(payloadKey(name)));
        }

        #endregion **************************************************************************



        #region Writing *********************************************************************

        ///<summary>
        ///Records a file's state and moves it to the front of the list.
        ///
        ///The payload is written before the index, so a failure between the two leaves an unlisted payload
        ///rather than a listed row with nothing behind it. The first is invisible and is overwritten the
        ///next time that file is saved; the second is a row that opens nothing.
        ///</summary>
        public async Task RememberAsync(HistoryEntry entry, string sessionJson)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return;

            var history = await loadAsync();

            await storage.SetAsync(payloadKey(entry.Name), sessionJson);

            foreach (string dropped in history.Remember(entry, Capacity))
                await storage.RemoveAsync(payloadKey(dropped));

            await writeIndexAsync(history);
        }

        ///<summary>Removes one file, its row and its payload together.</summary>
        public async Task ForgetAsync(string name)
        {
            var history = await loadAsync();

            if (!history.Forget(name))
                return;

            await storage.RemoveAsync(payloadKey(name));

            await writeIndexAsync(history);
        }

        ///<summary>
        ///Empties the history.
        ///
        ///Every payload is removed rather than only the index, or the files would stay in the browser's
        ///storage forever with nothing left pointing at them - which is not what "clear" means to somebody
        ///deleting layouts off their machine.
        ///</summary>
        public async Task ClearAsync()
        {
            var history = await loadAsync();

            foreach (var entry in history.Entries)
                await storage.RemoveAsync(payloadKey(entry.Name));

            history.Entries.Clear();

            await writeIndexAsync(history);
        }

        #endregion **************************************************************************



        #region Storage *********************************************************************

        private async Task<HistoryIndex> loadAsync()
        {
            index ??= HistoryIndex.Deserialize(await storage.GetAsync(IndexKey));

            return index;
        }

        private async Task writeIndexAsync(HistoryIndex history)
        {
            await storage.SetAsync(IndexKey, HistoryIndex.Serialize(history));
        }

        ///<summary>
        ///Where one file's state is kept. Under the index's own key plus the name, so a browser's storage
        ///inspector groups them and clearing by prefix would find all of them.
        ///</summary>
        public static string payloadKey(string name)
        {
            return $"{IndexKey}.{name}";
        }

        #endregion **************************************************************************
    }
}
