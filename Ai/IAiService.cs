namespace GDSViewer.Ai
{
    ///
    ///**Everything the app's AI stands on, behind one seam** (2026-10-31, asked for): the models it may use, how each
    ///is talked to, and the AI assistant chats kept for whoever is using it. Registered in Program.cs; the windows and the
    ///PDK manager ask for it and never for a store.
    ///
    ///**This app's own is <see cref="BrowserAiService"/>**: models with the person's own keys and chats, both kept in
    ///this browser, and each provider called straight from the page. **Built into another app, another is registered
    ///in its place** for the account signed in there - its models and connections, perhaps with no key in the page at
    ///all and every request sent through its server by the providers <see cref="CreateProvider"/> makes, and the chats
    ///kept with the account. **Who registers it is GDS Viewer, not the host**: a host like Treeality runs a plugin's own
    ///module (`IPluginModule.ConfigureServices`) and replaces nothing of a plugin's, so the plugin's module registers
    ///one written over that host's services. Nothing else changes.
    ///
    public interface IAiService
    {
        ///<summary>Where the models and chats are kept, said in a sentence - "in this browser", "with your account".</summary>
        string KeptWhere { get; }

        ///
        ///Whether models can be added, changed and deleted under Settings, AI settings. False for a service whose models
        ///are the host's to give, where the settings window lists them and the chat picks among them.
        ///
        bool ModelsEditable { get; }

        ///<summary>The models by the name each is shown under. Never null.</summary>
        Task<Dictionary<string, LlmConnection>> LoadModelsAsync();

        ///<summary>Keeps the models, and raises <see cref="ModelsChanged"/>. Called only while <see cref="ModelsEditable"/>.</summary>
        Task SaveModelsAsync(Dictionary<string, LlmConnection> models);

        ///
        ///Raised after the models change, so every picker shows them again - the settings window edits the list and the
        ///chat and the PDK manager pick from it, and all of them are made with the page and then only hidden.
        ///
        event Func<Task>? ModelsChanged;

        ///
        ///What talks to a model: a request and its answer, its tools and its usage. The browser's own calls the provider
        ///straight from the page with the model's key; a host's can send it through its own server instead. The
        ///connection is a copy, with the effort of the one request on it - see AskAi's sendAsync.
        ///
        ILlmProvider CreateProvider(LlmConnection connection);

        ///
        ///Writes out a recording of speech - a 16 kHz mono WAV - with the model's provider, for the AI assistant's microphone when it
        ///is set to the provider (see <see cref="SpeechTranscriber"/>): the words heard, or why there are none. Told the
        ///words a layout conversation is full of, so it writes them as the editor does.
        ///
        Task<(string? Text, string? Problem)> TranscribeAsync(LlmConnection connection, byte[] wav, string vocabulary, CancellationToken ct);

        ///<summary>The chats kept, the one changed last first. Never null.</summary>
        Task<List<ChatSummary>> ListChatsAsync();

        ///<summary>A chat by its id, or null when there is none - deleted in another tab, say.</summary>
        Task<SavedChat?> LoadChatAsync(string id);

        ///<summary>Keeps a chat, new or changed, and raises <see cref="ChatsChanged"/>.</summary>
        Task SaveChatAsync(SavedChat chat);

        ///<summary>Throws a chat away, and raises <see cref="ChatsChanged"/>.</summary>
        Task DeleteChatAsync(string id);

        ///<summary>Raised after a chat is kept or thrown away, so the list shows it.</summary>
        event Func<Task>? ChatsChanged;
    }
}
