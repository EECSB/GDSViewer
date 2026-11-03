using System.Text.Json;
using GDSViewer.Models;

namespace GDSViewer.Ai
{
    ///
    ///The saved AI models, kept in this browser through <see cref="AppStorage"/> under one key of their own.
    ///
    ///**Their own key, and nowhere else.** Not in the session and not in a history row: those are written into on
    ///every save and carried about with a file, and a key has no business traveling with a layout. Nothing reads
    ///this key but this class.
    ///
    ///**Stored as the person typed it.** AppStorage deflates a long value, which is compression and not secrecy, so
    ///the settings window says plainly where keys are kept - the same as GraphDBViewer, which this is a port of.
    ///
    public sealed class LlmConnectionStore
    {
        public const string StorageKey = "gdsviewer.llmConnections";

        private readonly AppStorage storage;

        public LlmConnectionStore(AppStorage storage)
        {
            this.storage = storage;
        }

        ///
        ///Where a developer's dev-secrets.json is asked for, or null for nowhere - which is every build but a Development
        ///one. Set in Program.cs; see <see cref="DevSecrets"/>.
        ///
        public string? DevSecretsPath { get; init; }

        ///
        ///The models in a developer's dev-secrets.json, added to the saved ones - see <see cref="DevSecrets.AddTo"/> for
        ///which. Saved, and so heard by the windows showing the list, only when something was added. Says whether it was.
        ///
        public async Task<bool> SeedAsync(HttpClient http)
        {
            var secrets = await DevSecrets.LoadAsync(http, DevSecretsPath);

            if (secrets is null)
                return false;

            var saved = await LoadAsync();

            if (!DevSecrets.AddTo(secrets, saved))
                return false;

            await SaveAsync(saved);

            return true;
        }

        ///<summary>The saved models by name. Never null - none saved reads as an empty dictionary.</summary>
        public async Task<Dictionary<string, LlmConnection>> LoadAsync()
        {
            return Read(await storage.GetAsync(StorageKey));
        }

        ///
        ///Raised after the saved set changes, so anything showing it can show it again.
        ///
        ///Needed because the list is edited in one window and used in another, and both are made with the page and
        ///then only hidden - without this, a model added under Settings would stay out of the chat's picker until a
        ///reload, which looks exactly like it not having been saved.
        ///
        public event Func<Task>? Changed;

        public async Task SaveAsync(Dictionary<string, LlmConnection> connections)
        {
            await storage.SetAsync(StorageKey, Write(connections));

            if (Changed is null)
                return;

            foreach (var listener in Changed.GetInvocationList())
                await ((Func<Task>)listener)();
        }

        ///<summary>The models out of their stored text. Text that is not readable is no models rather than an error: a viewer that will not start over a setting is worse than one that forgot it.</summary>
        public static Dictionary<string, LlmConnection> Read(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, LlmConnection>();

            try
            {
                return JsonSerializer.Deserialize(json, AiJson.Default.DictionaryStringLlmConnection) ?? new Dictionary<string, LlmConnection>();
            }
            catch (JsonException)
            {
                return new Dictionary<string, LlmConnection>();
            }
        }

        public static string Write(Dictionary<string, LlmConnection> connections)
        {
            return JsonSerializer.Serialize(connections, AiJson.Default.DictionaryStringLlmConnection);
        }
    }
}
