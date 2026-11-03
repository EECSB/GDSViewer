using System.Text.Json;
using GDSViewer.Models;

namespace GDSViewer.Ai
{
    ///
    ///The AI assistant chats kept in this browser, through <see cref="AppStorage"/> - which is IndexedDB, so a chat with
    ///pictures and files in it fits where localStorage's few megabytes would not. What <see cref="BrowserAiService"/>
    ///keeps them in; a host app's own <see cref="IAiService"/> keeps them wherever its accounts are.
    ///
    ///**A list and the chats, under keys of their own**: the list is read whenever the window shows it, and reading
    ///every chat whole to name it would read every picture in every one. Each chat is written before the list that
    ///names it, so a write cut short leaves a chat the list does not name rather than a row that opens nothing - and a
    ///row whose chat is gone anyway is dropped from the list when it is opened (see <see cref="LoadAsync"/>).
    ///
    public sealed class ChatStore
    {
        public const string ListKey = "gdsviewer.aiChats";

        public const string ChatKeyPrefix = "gdsviewer.aiChat.";

        private readonly AppStorage storage;

        public ChatStore(AppStorage storage)
        {
            this.storage = storage;
        }

        ///<summary>The chats kept, the one changed last first. Never null.</summary>
        public async Task<List<ChatSummary>> ListAsync()
        {
            return ReadList(await storage.GetAsync(ListKey));
        }

        ///<summary>A chat by its id, or null when there is none kept under it - in which case the list stops naming it.</summary>
        public async Task<SavedChat?> LoadAsync(string id)
        {
            var chat = ReadChat(await storage.GetAsync(ChatKeyPrefix + id));

            if (chat is null)
                await dropFromListAsync(id);

            return chat;
        }

        ///<summary>Keeps a chat, and puts it at the top of the list.</summary>
        public async Task SaveAsync(SavedChat chat)
        {
            await storage.SetAsync(ChatKeyPrefix + chat.Id, WriteChat(chat));

            var list = await ListAsync();

            list.RemoveAll(each => each.Id == chat.Id);
            list.Insert(0, chat.Summary());

            await storage.SetAsync(ListKey, WriteList(Ordered(list)));
        }

        public async Task DeleteAsync(string id)
        {
            await dropFromListAsync(id);
            await storage.RemoveAsync(ChatKeyPrefix + id);
        }

        private async Task dropFromListAsync(string id)
        {
            var list = await ListAsync();

            if (list.RemoveAll(each => each.Id == id) > 0)
                await storage.SetAsync(ListKey, WriteList(list));
        }

        ///<summary>The list in the order it is shown: changed last first.</summary>
        public static List<ChatSummary> Ordered(IEnumerable<ChatSummary> list)
        {
            return list.OrderByDescending(each => each.Updated).ToList();
        }

        ///<summary>The list out of its stored text. Text that will not read is no chats rather than an error, as the models' is.</summary>
        public static List<ChatSummary> ReadList(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<ChatSummary>();

            try
            {
                return JsonSerializer.Deserialize(json, AiJson.Default.ListChatSummary) ?? new List<ChatSummary>();
            }
            catch (JsonException)
            {
                return new List<ChatSummary>();
            }
        }

        public static string WriteList(List<ChatSummary> list)
        {
            return JsonSerializer.Serialize(list, AiJson.Default.ListChatSummary);
        }

        ///<summary>A chat out of its stored text, or null when there is none or it will not read.</summary>
        public static SavedChat? ReadChat(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonSerializer.Deserialize(json, AiJson.Default.SavedChat);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static string WriteChat(SavedChat chat)
        {
            return JsonSerializer.Serialize(chat, AiJson.Default.SavedChat);
        }
    }
}
