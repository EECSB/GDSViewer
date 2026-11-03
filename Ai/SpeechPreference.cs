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
        private SpeechMode mode = SpeechMode.Browser;

        public SpeechPreference(AppStorage storage)
        {
            this.storage = storage;
        }

        ///<summary>Raised after the mode changes, so the chat's microphone says what it does now.</summary>
        public event Func<Task>? Changed;

        ///<summary>The mode, read once from where it is kept. The browser's until something else was chosen.</summary>
        public async Task<SpeechMode> GetAsync()
        {
            if (loaded)
                return mode;

            loaded = true;
            mode = Read(await storage.GetAsync(StorageKey));

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
