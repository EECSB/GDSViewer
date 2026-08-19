using System.IO.Compression;
using System.Text;
using Microsoft.JSInterop;

namespace GDSViewer.Models
{
    ///<summary>
    ///Where the app keeps what the user did, so closing the tab does not throw it away.
    ///
    ///**IndexedDB first, localStorage second.** localStorage caps an origin at roughly 5 MB and holds
    ///strings only, which is not enough for the thing most worth keeping - the open file's bytes, where the
    ///bundled examples alone reach 9 MB. IndexedDB's quota is a share of free disk. localStorage stays in
    ///the picture for two jobs it is better at: it is the only store that can still be written from a
    ///page-unload handler, and it holds whatever a previous version of this app wrote before there was an
    ///IndexedDB store.
    ///
    ///**Values are compressed past a threshold.** A GDSII file is highly repetitive - record headers,
    ///coordinate runs - so deflate takes a large bite out of it, and the base64 that makes it a string costs
    ///a third back. Below the threshold the overhead is not worth it, so a one-character marker says which
    ///of the two a stored value is.
    ///
    ///**Nothing here throws.** Storage is unavailable outright in some private-browsing modes and a write
    ///can fail on a full quota. This is a viewer; losing a saved session is a disappointment, and taking the
    ///app down over it would not be.
    ///</summary>
    public class AppStorage
    {
        #region Constants *******************************************************************

        ///<summary>
        ///Values longer than this are stored compressed. Below it, deflate plus base64 usually makes the
        ///value *larger*, and the whole point is to spend less room rather than more.
        ///</summary>
        private const int CompressionThreshold = 256;

        private const char RawMarker = 'r';
        private const char CompressedMarker = 'z';

        #endregion **************************************************************************



        #region Fields **********************************************************************

        private readonly IJSRuntime js;

        ///<summary>
        ///Whether IndexedDB answered at all. Checked once and remembered, so a browser without it is not
        ///asked again on every save.
        ///</summary>
        private bool? indexedDbAvailable;

        #endregion **************************************************************************



        public AppStorage(IJSRuntime js)
        {
            this.js = js;
        }

        #region Reading and writing *********************************************************

        ///<summary>
        ///Reads a value, falling back to localStorage and carrying anything found there across.
        ///
        ///The fallback is what makes the two stores one store from the caller's point of view: a value
        ///written by an unload handler, or by a version of this app that only had localStorage, is found on
        ///the next read and moved into IndexedDB. localStorage is left as it was rather than cleared, so a
        ///browser that loses IndexedDB still has it.
        ///</summary>
        public async Task<string?> GetAsync(string key)
        {
            string? stored = await indexedDbGet(key);

            if (stored is not null)
                return Decode(stored);

            string? legacy = await localStorageGet(key);

            if (legacy is null)
                return null;

            //Carried over so the next read is a single lookup, but the value is returned either way.
            await SetAsync(key, legacy);

            return legacy;
        }

        public async Task SetAsync(string key, string value)
        {
            await indexedDbSet(key, Encode(value));
        }

        ///<summary>
        ///Also writes to localStorage, for the values small enough to belong there and important enough to
        ///want back even if IndexedDB is unavailable. Kept uncompressed there so the unload handler, which
        ///cannot run C#, writes the same shape.
        ///</summary>
        public async Task SetSmallAsync(string key, string value)
        {
            await SetAsync(key, value);

            await localStorageSet(key, value);
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                await js.InvokeVoidAsync("gdsStorage.remove", key);
            }
            catch { }

            try
            {
                await js.InvokeVoidAsync("gdsLocalStorage.remove", key);
            }
            catch { }
        }

        ///<summary>
        ///Hands the unload handler a snapshot to write if the page goes away, replacing whatever it held.
        ///
        ///Needed because there is no event that can await an asynchronous save: beforeunload cannot hold a
        ///page open for IndexedDB, and a tab on mobile is often killed without firing it at all. So the
        ///snapshot is kept current here and written synchronously to localStorage on the way out - the one
        ///API still callable at that point - and the next read finds it through the fallback above.
        ///</summary>
        public async Task HoldForExitAsync(string key, string snapshot)
        {
            try
            {
                await js.InvokeVoidAsync("gdsExitSave.hold", key, snapshot);
            }
            catch { }
        }

        public async Task<bool> IsAvailableAsync()
        {
            if (indexedDbAvailable is not null)
                return indexedDbAvailable.Value;

            try
            {
                indexedDbAvailable = await js.InvokeAsync<bool>("gdsStorage.available");
            }
            catch
            {
                indexedDbAvailable = false;
            }

            return indexedDbAvailable.Value;
        }

        #endregion **************************************************************************



        #region Interop *********************************************************************

        private async Task<string?> indexedDbGet(string key)
        {
            try
            {
                return await js.InvokeAsync<string?>("gdsStorage.get", key);
            }
            catch
            {
                return null;
            }
        }

        private async Task indexedDbSet(string key, string value)
        {
            try
            {
                await js.InvokeAsync<bool>("gdsStorage.set", key, value);
            }
            catch { }
        }

        private async Task<string?> localStorageGet(string key)
        {
            try
            {
                return await js.InvokeAsync<string?>("gdsLocalStorage.get", key);
            }
            catch
            {
                return null;
            }
        }

        private async Task localStorageSet(string key, string value)
        {
            try
            {
                await js.InvokeAsync<bool>("gdsLocalStorage.set", key, value);
            }
            catch { }
        }

        #endregion **************************************************************************



        #region Encoding ********************************************************************

        ///<summary>
        ///Prefixes a value with what it is, and deflates it when that is worth doing. Pairs with
        ///<see cref="Decode"/>; the two have to agree on the marker or a stored session reads as gibberish.
        ///</summary>
        public static string Encode(string value)
        {
            if (value.Length < CompressionThreshold)
                return RawMarker + value;

            byte[] bytes = Encoding.UTF8.GetBytes(value);

            using var output = new MemoryStream();

            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
                deflate.Write(bytes, 0, bytes.Length);

            return CompressedMarker + Convert.ToBase64String(output.ToArray());
        }

        ///<summary>
        ///The inverse. A value with no marker this knows is handed back untouched, which is the safe answer
        ///for something another version of the app wrote - better a caller that cannot parse it than a
        ///decoder that throws on the path that opens a file.
        ///</summary>
        public static string Decode(string stored)
        {
            if (stored.Length == 0)
                return stored;

            char marker = stored[0];
            string payload = stored[1..];

            if (marker == RawMarker)
                return payload;

            if (marker != CompressedMarker)
                return stored;

            try
            {
                byte[] data = Convert.FromBase64String(payload);

                using var input = new MemoryStream(data);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();

                deflate.CopyTo(output);

                return Encoding.UTF8.GetString(output.ToArray());
            }
            catch
            {
                return "";
            }
        }

        #endregion **************************************************************************
    }
}
