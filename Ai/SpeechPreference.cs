using GDSViewer.Models;

namespace GDSViewer.Ai
{
    ///
    ///Which way the AI assistant's microphone turns speech into words - see <see cref="SpeechMode"/> - kept in this browser and
    ///chosen under Settings, AI settings (2026-10-31, asked for: the browser's to begin with, the provider for accuracy).
    ///One instance, so the settings window and the chat hear each other's change.
    ///
    public sealed class SpeechPreference
    {
        public const string StorageKey = "gdsviewer.speechInput";

        private readonly AppStorage storage;
        private bool loaded;
        private Task<SpeechMode>? loading;
        private SpeechMode mode = SpeechMode.Browser;

        public SpeechPreference(AppStorage storage)
        {
            this.storage = storage;
        }

        ///<summary>Raised after the mode changes, so the chat's microphone says what it does now.</summary>
        public event Func<Task>? Changed;

        ///
        ///The mode, read once from where it is kept. The browser's until something else was chosen.
        ///
        ///One read, shared by everybody who asks before it answers: AI settings and the chat can both ask as a page comes
        ///up, and until 2026-11-07 (from GraphDBViewer) the first was told what was kept and the second the default,
        ///since the first ask marked it loaded before the storage had answered.
        ///
        public Task<SpeechMode> GetAsync()
        {
            if (loaded)
                return Task.FromResult(mode);

            loading ??= LoadAsync();

            return loading;
        }

        private async Task<SpeechMode> LoadAsync()
        {
            string? text;

            try
            {
                text = await storage.GetAsync(StorageKey);
            }
            catch
            {
                //A read that failed is not kept as the answer: the next ask reads again.
                loading = null;

                throw;
            }

            //A choice made while the read was out is newer than what was read.
            if (!loaded)
            {
                loaded = true;
                mode = Read(text);
            }

            return mode;
        }

        public async Task SetAsync(SpeechMode chosen)
        {
            loaded = true;

            if (chosen == mode)
                return;

            mode = chosen;

            await storage.SetAsync(StorageKey, chosen.ToString());

            if (Changed is null)
                return;

            foreach (var listener in Changed.GetInvocationList())
                await ((Func<Task>)listener)();
        }

        ///<summary>A kept mode out of its text: anything that is not the provider's is the browser's.</summary>
        public static SpeechMode Read(string? text)
        {
            if (string.Equals(text, nameof(SpeechMode.Provider), StringComparison.Ordinal))
                return SpeechMode.Provider;

            return SpeechMode.Browser;
        }
    }
}
