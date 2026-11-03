namespace GDSViewer.Models
{
    ///<summary>
    ///The files that have been opened, kept so they can be opened again exactly as they were left.
    ///
    ///**A small index, and one payload per file.** The list of rows lives under one key and each file's
    ///state lives under its own, because an entry is a whole <see cref="SavedSession"/> - bytes included -
    ///and a history of twenty layouts is megabytes. Opening the popup reads the index alone; a file's bytes
    ///are read when that row is chosen, or when it is pointed at long enough to be worth drawing. The bytes
    ///sit beside the payload rather than in it, and are written only when the file has changed - see
    ///<see cref="SavedSession.BytesApart"/>.
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

        ///<summary>
        ///Which version of its file each row's bytes are, for the rows whose bytes this page has written or read.
        ///
        ///What lets a save leave the bytes alone - see <see cref="SavedSession.BytesApart"/>. The version is the
        ///caller's to define and only ever compared for equality; a row missing from here has its bytes written
        ///with the next save, which is the safe way to be wrong. A row dropped, forgotten or cleared leaves too,
        ///since its bytes went with it.
        ///</summary>
        private readonly Dictionary<string, object> bytesHeld = new Dictionary<string, object>();

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
        ///
        ///Given a <paramref name="pause"/>, the bytes are read at a pace, and into <see cref="SavedSession.FileData"/>:
        ///they are the file, megabytes of it, which read in one go stopped the page, and read as text were garbage
        ///several times their size - see AppStorage.ReadAtAPaceAsync.
        ///
        public async Task<SavedSession?> ReadAsync(string name, Func<ValueTask>? pause = null)
        {
            var session = SavedSession.Deserialize(await storage.GetAsync(payloadKey(name)));

            if (session is null || !session.BytesApart)
                return session;

            //Its bytes, from beside it - see SavedSession.BytesApart. A row whose bytes are gone opens nothing.
            if (pause is null)
            {
                string? bytes = await storage.GetAsync(bytesKey(name));

                if (string.IsNullOrEmpty(bytes))
                    return null;

                session.BytesFrom(bytes);

                return session;
            }

            using var stored = await storage.ReadAtAPaceAsync(bytesKey(name), pause);

            if (stored is null || !await session.BytesFromAsync(stored, pause))
                return null;

            return session;
        }

        ///<summary>
        ///Whether a row's bytes are already the given version of its file, so a save can leave them where they are.
        ///</summary>
        public bool HoldsBytes(string name, object version)
        {
            return bytesHeld.TryGetValue(name, out var held) && held.Equals(version);
        }

        ///<summary>
        ///Notes that a row's bytes are a version of its file without writing them - for a row just opened, whose
        ///bytes were read from beside it and are still there.
        ///</summary>
        public void HeldBytes(string name, object version)
        {
            bytesHeld[name] = version;
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
            await RememberEncodedAsync(entry, AppStorage.Encode(sessionJson));
        }

        ///<summary>
        ///The same, for a session already put through <see cref="AppStorage.Encode"/> - which the shell has
        ///done once for every place the session goes. See <see cref="AppStorage.SetEncodedAsync"/>.
        ///
        ///**The file's bytes only when they have changed.** A session that keeps them apart - see
        ///<see cref="SavedSession.BytesApart"/> - passes them here, encoded, with the version they are of, and
        ///they are written together with the payload; otherwise only the payload is written, and the bytes
        ///already beside it stay. <see cref="HoldsBytes"/> is how a caller tells which.
        ///</summary>
        public async Task RememberEncodedAsync(HistoryEntry entry, string encodedSession, string? encodedBytes = null, object? bytesVersion = null)
        {
            if (string.IsNullOrEmpty(entry.Name))
                return;

            var history = await loadAsync();

            if (encodedBytes is null)
                await storage.SetEncodedAsync(payloadKey(entry.Name), encodedSession);
            else
                await storage.SetEncodedTogetherAsync(payloadKey(entry.Name), encodedSession, bytesKey(entry.Name), encodedBytes);

            if (encodedBytes is not null && bytesVersion is not null)
                bytesHeld[entry.Name] = bytesVersion;

            foreach (string dropped in history.Remember(entry, Capacity))
                await removeRowAsync(dropped);

            await writeIndexAsync(history);
        }

        ///<summary>Removes one file, its row and its payload together.</summary>
        public async Task ForgetAsync(string name)
        {
            var history = await loadAsync();

            if (!history.Forget(name))
                return;

            await removeRowAsync(name);

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
                await removeRowAsync(entry.Name);

            history.Entries.Clear();

            await writeIndexAsync(history);
        }

        ///<summary>A row's payload and the bytes kept beside it, gone together.</summary>
        private async Task removeRowAsync(string name)
        {
            await storage.RemoveAsync(payloadKey(name));
            await storage.RemoveAsync(bytesKey(name));

            bytesHeld.Remove(name);
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

        ///<summary>
        ///Where a row's file bytes are kept when its session keeps them apart - see
        ///<see cref="SavedSession.BytesApart"/>. Beside the payload, under a suffix no row's own name can end in:
        ///a row is named for its file, extension and all.
        ///</summary>
        public static string bytesKey(string name)
        {
            return payloadKey(name) + BytesSuffix;
        }

        ///<summary>What a key holding a file's bytes ends in, beside the key of what they belong to.</summary>
        public const string BytesSuffix = "#bytes";

        #endregion **************************************************************************
    }
}
