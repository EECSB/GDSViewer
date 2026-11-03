namespace GDSViewer.Models
{
    ///
    ///The Examples pictures worth keeping between visits - the big designs', which take a second or more to draw on a slow
    ///machine, where a cell's takes a few milliseconds and is drawn again rather than kept. See PreviewPictures.
    ///
    ///**Keyed by what the picture is of**: the file's name, its length and a sample of its bytes, and the version of the
    ///drawing. A picture of a file that has changed since, or one drawn the way an older version of the app drew, is
    ///under a key nothing asks for again, rather than shown. The file is fetched to make the key - which costs no more
    ///than it did, since a fetch was always the first thing a preview did, and skips the reading, the flattening and the
    ///drawing, which are what the store is for.
    ///
    ///**A few at most**, the oldest going first, listed under <see cref="ListKey"/> so the store does not grow with every
    ///design a visit points at. Over <see cref="AppStorage"/> like everything else kept, and best effort the same way: a
    ///write that fails costs the picture on the next visit, never the one on screen.
    ///
    public class PreviewStore
    {
        ///<summary>What every picture's key starts with.</summary>
        public const string Prefix = "gdsviewer.preview.";

        ///<summary>Where the keys of the pictures kept are listed, oldest first, a line each.</summary>
        public const string ListKey = "gdsviewer.previews";

        ///
        ///How many elements a layout draws before its picture is worth keeping: the designs rather than the cells. The
        ///smallest bundled design draws eight thousand and the largest cell under a thousand.
        ///
        public const int WorthKeeping = 5_000;

        ///<summary>
        ///Which way pictures are drawn. **Raised whenever PngWriter draws differently**, so pictures kept from before are
        ///not found and shown as they were.
        ///</summary>
        private const int Drawing = 1;

        ///<summary>How many are kept: every bundled design twice over.</summary>
        private const int MostKept = 24;

        ///<summary>Every how many bytes the sample takes one - a prime, so a file's repeating records do not line up with it.</summary>
        private const int SampleEvery = 1021;

        private readonly AppStorage storage;

        public PreviewStore(AppStorage storage)
        {
            this.storage = storage;
        }

        ///
        ///Where a picture of this file is kept: its name, its length and a sample of its bytes.
        ///
        ///**A sample, not a hash of the whole file**, which for the largest design is eight megabytes walked on the page's
        ///thread to save a quarter of a second. Every thousandth byte and the last sixty-four, with the length beside
        ///them, change when any of the files that ship with the app does, which is the only way one changes.
        ///
        public static string KeyFor(string name, byte[] bytes)
        {
            uint sample = 2166136261;

            for (int i = 0; i < bytes.Length; i += SampleEvery)
                sample = (sample ^ bytes[i]) * 16777619;

            for (int i = Math.Max(0, bytes.Length - 64); i < bytes.Length; i++)
                sample = (sample ^ bytes[i]) * 16777619;

            return FormattableString.Invariant($"{Prefix}{Drawing}.{name}.{bytes.Length}.{sample:x8}");
        }

        ///<summary>The picture kept under this key, or null when there is none - or what is there is not one.</summary>
        public async Task<string?> GetAsync(string key)
        {
            string? stored = await storage.GetAsync(key);

            if (stored is null || !stored.StartsWith(PreviewPictures.PngAddress, StringComparison.Ordinal))
                return null;

            return stored;
        }

        ///<summary>Keeps a picture under its key, and lets the oldest go once there are more than the most kept.</summary>
        public async Task SetAsync(string key, string picture)
        {
            //As it is: a PNG is compressed already, and deflating it again would be a pass over it on the page's thread
            //to make it no smaller.
            await storage.SetAsItIsAsync(key, picture);

            string? listing = await storage.GetAsync(ListKey);
            var listed = new List<string>();

            if (!string.IsNullOrEmpty(listing))
                listed.AddRange(listing.Split('\n', StringSplitOptions.RemoveEmptyEntries));

            listed.Remove(key);
            listed.Add(key);

            while (listed.Count > MostKept)
            {
                await storage.RemoveAsync(listed[0]);

                listed.RemoveAt(0);
            }

            await storage.SetAsync(ListKey, string.Join('\n', listed));
        }
    }
}
