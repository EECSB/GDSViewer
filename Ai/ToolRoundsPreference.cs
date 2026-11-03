using System.Globalization;
using GDSViewer.Models;

namespace GDSViewer.Ai
{
    ///
    ///How many rounds of tool calls the AI assistant may make for one message before it pauses to ask whether to go on
    ///for another as many - kept in this browser and set under Settings, AI settings (2026-11-01, asked for). Thirty
    ///until something else is set. One instance, so the settings window and the chat hear each other's change.
    ///
    public sealed class ToolRoundsPreference
    {
        public const string StorageKey = "gdsviewer.aiToolRounds";

        ///<summary>The fewest a person may set: one round is a single look or change, and anything less is no tools at all.</summary>
        public const int Fewest = 1;

        ///<summary>The most: a cap on a cap, so a slip of the keyboard cannot hand a run unwatched spend without end.</summary>
        public const int Most = 1000;

        private readonly AppStorage storage;
        private bool loaded;
        private int rounds = LlmProviderFactory.DefaultToolRounds;

        public ToolRoundsPreference(AppStorage storage)
        {
            this.storage = storage;
        }

        ///<summary>The rounds, read once from where they are kept.</summary>
        public async Task<int> GetAsync()
        {
            if (loaded)
                return rounds;

            loaded = true;
            rounds = Read(await storage.GetAsync(StorageKey));

            return rounds;
        }

        ///<summary>Keeps a new number, held between <see cref="Fewest"/> and <see cref="Most"/>, and says what was kept.</summary>
        public async Task<int> SetAsync(int chosen)
        {
            loaded = true;
            chosen = Math.Clamp(chosen, Fewest, Most);

            if (chosen == rounds)
                return rounds;

            rounds = chosen;

            await storage.SetAsync(StorageKey, chosen.ToString(CultureInfo.InvariantCulture));

            return rounds;
        }

        ///<summary>Kept rounds out of their text: anything that is not a number is the default, and a number out of range is brought into it.</summary>
        public static int Read(string? text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int kept))
                return LlmProviderFactory.DefaultToolRounds;

            return Math.Clamp(kept, Fewest, Most);
        }
    }
}
