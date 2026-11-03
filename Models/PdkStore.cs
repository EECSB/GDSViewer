using System.Text.Json;

namespace GDSViewer.Models
{
    ///
    ///Where the PDKs are kept, for every file and every visit - see <see cref="Pdk"/>. Over <see cref="AppStorage"/> like
    ///everything else kept, and best effort the same way: a list that will not read is treated as none kept, and the
    ///built-ins are always there to fall back on.
    ///
    public sealed class PdkStore
    {
        public const string StorageKey = "gdsviewer.pdks";

        private readonly AppStorage storage;

        public PdkStore(AppStorage storage)
        {
            this.storage = storage;
        }

        ///<summary>The list kept, or null in a browser that never kept one - which is when what came before PDKs is carried in.</summary>
        public async Task<PdkList?> LoadAsync()
        {
            return Read(await storage.GetAsync(StorageKey));
        }

        public async Task SaveAsync(PdkList list)
        {
            await storage.SetAsync(StorageKey, Write(list));
        }

        public static PdkList? Read(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return null;

            try
            {
                var list = JsonSerializer.Deserialize(stored, SavedJson.Default.PdkList);

                if (list is null)
                    return null;

                list.EnsureBuiltIns();

                return list;
            }
            catch
            {
                return null;
            }
        }

        public static string Write(PdkList list)
        {
            return JsonSerializer.Serialize(list, SavedJson.Default.PdkList);
        }
    }
}
