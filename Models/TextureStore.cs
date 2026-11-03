namespace GDSViewer.Models
{
    ///<summary>
    ///The pictures layers' textures are drawn with, kept in this browser under the names layers give them - see
    ///<see cref="GdsII.Layer.Texture"/>.
    ///
    ///**By name, the way a layermap finds them**: a CSV says `copper.png` and the picture beside it is that file, so the
    ///name is the one thing a layer, a session and a layermap all already carry. A picture chosen again under a name
    ///already kept takes the place of the old one, for every layer that names it - which is what choosing a newer
    ///version of the same file means.
    ///
    ///**Over <see cref="AppStorage"/>**, like the sessions and the history: as base64, since the store holds strings.
    ///Best effort like the rest of it - a write that fails on a full quota costs the picture on the next visit, not
    ///the one on screen.
    ///</summary>
    public class TextureStore
    {
        ///<summary>What every picture's key starts with; the name follows it.</summary>
        public const string Prefix = "gdsviewer.texture.";

        ///<summary>
        ///The largest picture kept, in bytes. A texture is tiled, so a big one buys no more than a small one does - and
        ///each is held in the store and in the GPU both. Sixteen megabytes is a 4K photograph with room to spare.
        ///</summary>
        public const long LargestPicture = 16 * 1024 * 1024;

        private readonly AppStorage storage;

        public TextureStore(AppStorage storage)
        {
            this.storage = storage;
        }

        ///<summary>The picture kept under this name, or null when there is none - or what is there is not one.</summary>
        public async Task<byte[]?> GetAsync(string name)
        {
            string? stored = await storage.GetAsync(Prefix + name);

            if (string.IsNullOrEmpty(stored))
                return null;

            try
            {
                return Convert.FromBase64String(stored);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        ///<summary>Keeps a picture under this name, in place of whatever was kept under it before.</summary>
        public async Task SetAsync(string name, byte[] picture)
        {
            await storage.SetAsync(Prefix + name, Convert.ToBase64String(picture));
        }
    }
}
