namespace GDSViewer.Ai
{
    ///
    ///The app's own <see cref="IAiService"/>: the models saved under Settings with the person's own keys, the chats,
    ///both kept in this browser, and every provider called straight from the page - there is no server here to send
    ///anything through. See <see cref="LlmConnectionStore"/> and <see cref="ChatStore"/>.
    ///
    public sealed class BrowserAiService : IAiService, IDisposable
    {
        private readonly LlmConnectionStore models;
        private readonly ChatStore chats;

        ///<summary>One client for every request, with no timeout of its own - see LlmProviderFactory.NewHttpClient.</summary>
        private readonly HttpClient http = LlmProviderFactory.NewHttpClient();

        public BrowserAiService(LlmConnectionStore models, ChatStore chats)
        {
            this.models = models;
            this.chats = chats;
        }

        public string KeptWhere
        {
            get { return "in this browser"; }
        }

        public bool ModelsEditable
        {
            get { return true; }
        }

        ///<summary>Adds the models in a developer's dev-secrets.json, in a Development build - see <see cref="LlmConnectionStore.SeedAsync"/>.</summary>
        public async Task<bool> SeedAsync(HttpClient appHttp)
        {
            return await models.SeedAsync(appHttp);
        }

        public async Task<Dictionary<string, LlmConnection>> LoadModelsAsync()
        {
            return await models.LoadAsync();
        }

        public async Task SaveModelsAsync(Dictionary<string, LlmConnection> saved)
        {
            await models.SaveAsync(saved);
        }

        public event Func<Task>? ModelsChanged
        {
            add
            {
                models.Changed += value;
            }
            remove
            {
                models.Changed -= value;
            }
        }

        public ILlmProvider CreateProvider(LlmConnection connection)
        {
            return LlmProviderFactory.Create(http, connection);
        }

        public async Task<(string? Text, string? Problem)> TranscribeAsync(LlmConnection connection, byte[] wav, string vocabulary, CancellationToken ct)
        {
            return await SpeechTranscriber.TranscribeAsync(http, connection, wav, vocabulary, ct);
        }

        public async Task<List<ChatSummary>> ListChatsAsync()
        {
            return await chats.ListAsync();
        }

        public async Task<SavedChat?> LoadChatAsync(string id)
        {
            return await chats.LoadAsync(id);
        }

        public async Task SaveChatAsync(SavedChat chat)
        {
            await chats.SaveAsync(chat);
            await chatsChanged();
        }

        public async Task DeleteChatAsync(string id)
        {
            await chats.DeleteAsync(id);
            await chatsChanged();
        }

        public event Func<Task>? ChatsChanged;

        private async Task chatsChanged()
        {
            if (ChatsChanged is null)
                return;

            foreach (var listener in ChatsChanged.GetInvocationList())
                await ((Func<Task>)listener)();
        }

        public void Dispose()
        {
            http.Dispose();
        }
    }
}
