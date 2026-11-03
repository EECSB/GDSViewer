using System.Buffers;
using System.Buffers.Text;
using System.IO.Compression;
using System.Text;
using Microsoft.JSInterop;

namespace GDSViewer.Models
{
    ///<summary>
    ///Where the app keeps what the user did, so closing the tab does not throw it away.
    ///
    ///**IndexedDB first, localStorage second.** localStorage caps an origin at roughly 5 MB and holds
    ///strings only, which is not enough for the thing most worth keeping - the open file's bytes, where one
    ///bundled design alone is 8 MB. IndexedDB's quota is a share of free disk. localStorage stays in
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

        ///<summary>How much of a value read at a pace crosses from the browser at a time - see ReadAtAPaceAsync. A multiple of four.</summary>
        private const int PartChars = 1 << 18;

        ///<summary>How much base64 FromBase64Async decodes between pauses. A multiple of four.</summary>
        private const int RunChars = 1 << 16;

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

        ///
        ///Reads a value, falling back to localStorage and carrying anything found there across.
        ///
        ///The fallback is what makes the two stores one store from the caller's point of view: a value
        ///written by an unload handler, or by a version of this app that only had localStorage, is found on
        ///the next read and moved into IndexedDB. localStorage is left as it was rather than cleared, so a
        ///browser that loses IndexedDB still has it.
        ///
        ///**Decoded there as well**, because what the unload handler writes is what it was handed - a value
        ///already through <see cref="Encode"/>, marker and all. Returned as it was found, that went to the
        ///session reader as text it could not parse, so the session the handler had saved read as none, and
        ///was carried into IndexedDB encoded a second time. Anything an older version left is plain JSON,
        ///which cannot start with either marker, so <see cref="Decode"/> hands it back as it is.
        ///
        public async Task<string?> GetAsync(string key)
        {
            string? stored = await indexedDbGet(key);

            if (stored is not null)
                return Decode(stored);

            string? legacy = await localStorageGet(key);

            if (legacy is null)
                return null;

            string value = Decode(legacy);

            //Carried over so the next read is a single lookup, but the value is returned either way.
            await SetAsync(key, value);

            return value;
        }

        ///
        ///A value read at a pace, for one big enough to stop the page: a History row's file, whose three megabytes as
        ///stored took 47 ms to cross from the browser in one piece and 49 more to decode, nothing answering meanwhile -
        ///most of the 116 ms pause on first pointing at gpiov2's row. Paced 2026-10-18.
        ///
        ///**As the bytes of its text, never as the text.** Read as a string, a file was garbage several times over before
        ///it was a file: every part copied three times crossing from the browser, the parts joined, the inflated text
        ///decoded into eleven million characters for gpiov2 and cut again behind its name - about a hundred megabytes for
        ///its eight, which kept the collector running through the read. Now the parts cross as bytes and are decoded from
        ///base64 as they arrive, and what comes back inflates as it is read, at the reader's pace - see
        ///SavedSession.BytesFromAsync. 2026-10-18.
        ///
        ///A value IndexedDB does not have is read the ordinary way, and so is one that is not compressed: an older
        ///version's in localStorage, a raw one, which is short by definition, or any in a browser without IndexedDB.
        ///Null when there is none, or what there is will not decode.
        ///
        public async Task<Stream?> ReadAtAPaceAsync(string key, Func<ValueTask> pause)
        {
            int length;

            try
            {
                length = await js.InvokeAsync<int>("gdsStorage.getInParts", key);
            }
            catch
            {
                length = -1;
            }

            if (length < 0)
                return textOf(await GetAsync(key));

            byte[] compressed = Array.Empty<byte>();
            int written = 0;

            try
            {
                for (int from = 0; from < length; )
                {
                    //The first part a character longer, for the marker, so that every part after it starts a group of four.
                    int count = PartChars;

                    if (from == 0)
                        count++;

                    byte[]? part = await js.InvokeAsync<byte[]?>("gdsStorage.partBytes", key, from, count);

                    //Let go of between the parts: read the ordinary way, rather than decode half a value.
                    if (part is null)
                        return textOf(await GetAsync(key));

                    int start = 0;

                    if (from == 0)
                    {
                        if (part.Length == 0 || part[0] != (byte)CompressedMarker)
                            return textOf(await GetAsync(key));

                        compressed = new byte[(length - 1) / 4 * 3];
                        start = 1;
                    }

                    from += count;

                    if (Base64.DecodeFromUtf8(part.AsSpan(start), compressed.AsSpan(written), out _, out int wrote, from >= length) != OperationStatus.Done)
                        return null;

                    written += wrote;

                    await pause();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                //The browser failing part way: the ordinary read, which says what it can and never throws.
                return textOf(await GetAsync(key));
            }
            finally
            {
                try
                {
                    await js.InvokeVoidAsync("gdsStorage.doneWithParts", key);
                }
                catch { }
            }

            return new DeflateStream(new MemoryStream(compressed, 0, written), CompressionMode.Decompress);
        }

        ///<summary>A value read the ordinary way, as the bytes of its text - see ReadAtAPaceAsync.</summary>
        private static Stream? textOf(string? value)
        {
            if (value is null)
                return null;

            return new MemoryStream(Encoding.UTF8.GetBytes(value));
        }

        public async Task SetAsync(string key, string value)
        {
            await SetEncodedAsync(key, Encode(value));
        }

        ///
        ///Writes a value as it is, never deflated - for one that is compressed already, such as a picture, where deflate
        ///would spend a pass over it on the page's only thread to make it no smaller. Read back with <see cref="GetAsync"/>
        ///like any other.
        ///
        public async Task SetAsItIsAsync(string key, string value)
        {
            await SetEncodedAsync(key, RawMarker + value);
        }

        ///
        ///Writes a value that has already been through <see cref="Encode"/>.
        ///
        ///**For one value written to several places.** A session is written to its own key, handed to the
        ///unload handler and written into the history row, and each of those used to compress it afresh -
        ///three deflates of the same few megabytes per save once a large design has been edited, when one
        ///would do.
        ///
        public async Task SetEncodedAsync(string key, string encoded)
        {
            await indexedDbSet(key, encoded);
        }

        ///
        ///Writes two values already through <see cref="Encode"/> in one transaction: both land, or neither does.
        ///
        ///**For settings and the file they belong to**, which are kept under two keys so that a save need not
        ///write the file - see <see cref="SavedSession.BytesApart"/>. When the file has changed they go
        ///together, so a reload can never read the settings and undo stack of one version beside the bytes of
        ///another.
        ///
        public async Task SetEncodedTogetherAsync(string firstKey, string firstEncoded, string secondKey, string secondEncoded)
        {
            try
            {
                await js.InvokeAsync<bool>("gdsStorage.setTogether", firstKey, firstEncoded, secondKey, secondEncoded);
            }
            catch { }
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
        ///
        ///The snapshot is encoded, the way <see cref="SetEncodedAsync"/> takes it: the handler cannot run C#,
        ///so it writes what it was given, and the fallback decodes it. Encoded rather than plain, too, because
        ///localStorage holds a few megabytes at most, and deflating a session that carries an edited file is
        ///what lets a larger one fit.
        ///</summary>
        public async Task HoldForExitAsync(string key, string encodedSnapshot)
        {
            try
            {
                await js.InvokeVoidAsync("gdsExitSave.hold", key, encodedSnapshot);
            }
            catch { }
        }

        ///<summary>
        ///Hands the unload handler two snapshots in one call, replacing what it held under both keys.
        ///
        ///**For a session and the bytes it belongs to**, which have to be held as a pair: the session's saved
        ///steps are taken again only on the bytes they were saved against - see <see cref="SavedSession.Base"/>
        ///- and two calls leave a moment between them when a page going away would write the new one of one
        ///beside the old one of the other.
        ///</summary>
        public async Task HoldTogetherForExitAsync(string firstKey, string firstEncoded, string secondKey, string secondEncoded)
        {
            try
            {
                await js.InvokeVoidAsync("gdsExitSave.holdTogether", firstKey, firstEncoded, secondKey, secondEncoded);
            }
            catch { }
        }

        ///<summary>
        ///Takes back what the unload handler holds under a key, so the way out does not write it.
        ///
        ///Each key is held separately - a session and the file it belongs to - so a file that stops being part
        ///of the session has to be let go of, or a closing page would spend localStorage's few megabytes on it.
        ///</summary>
        public async Task ReleaseForExitAsync(string key)
        {
            try
            {
                await js.InvokeVoidAsync("gdsExitSave.release", key);
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

        ///
        ///Base64 into bytes a run at a time, from <paramref name="start"/> to the end of <paramref name="text"/> - what
        ///Convert.FromBase64String does in one go, which took 17 ms over a design's eight megabytes with the page
        ///stopped. Throws a FormatException for what is not base64, as that does.
        ///
        public static async ValueTask<byte[]> FromBase64Async(string text, int start, Func<ValueTask> pause)
        {
            int length = text.Length - start;

            if (length < 0 || length % 4 != 0)
                throw new FormatException("The text is not base64: it is not a whole number of four-character groups.");

            int padding = 0;

            if (length > 0 && text[^1] == '=')
                padding++;

            if (length > 1 && text[^2] == '=')
                padding++;

            byte[] bytes = new byte[length / 4 * 3 - padding];
            int written = 0;

            for (int from = start; from < text.Length; from += RunChars)
            {
                int count = Math.Min(RunChars, text.Length - from);

                if (!Convert.TryFromBase64Chars(text.AsSpan(from, count), bytes.AsSpan(written), out int wrote))
                    throw new FormatException("The text is not base64.");

                written += wrote;

                await pause();
            }

            return bytes;
        }

        #endregion **************************************************************************
    }
}
