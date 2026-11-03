using System.Text.Json.Serialization;

namespace GDSViewer.Ai
{
    ///
    ///**A conversation as it is kept between visits** - the AI assistant window's chats list, asked for 2026-10-31 like the
    ///Claude app's: every question, answer and call, so a chat can be opened again and carried on, read back or saved
    ///as text. Kept by an <see cref="IAiService"/>, in this browser unless a host app keeps them for its account.
    ///
    ///**Its own shape rather than the window's turns written out**: a turn holds things that only mean anything while
    ///the page is up - the gate a waiting card is let go by, a file's place in the box - and the names written here
    ///are pinned, so a chat kept by an earlier version still reads. See <see cref="SavedTurn"/>.
    ///
    public sealed class SavedChat
    {
        ///<summary>How long a title is before it is cut - about a row of the chats list.</summary>
        public const int MostTitleCharacters = 80;

        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        ///<summary>What the list calls it: the first question, cut to a row.</summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        ///<summary>The layout that was open when it was last kept, for the list to say which file it was about.</summary>
        [JsonPropertyName("file")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("created")]
        public DateTime Created { get; set; }

        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }

        [JsonPropertyName("turns")]
        public List<SavedTurn> Turns { get; set; } = new List<SavedTurn>();

        ///<summary>A new id, unique enough for one person's chats wherever they are kept.</summary>
        public static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }

        ///<summary>The row the list shows for it.</summary>
        public ChatSummary Summary()
        {
            return new ChatSummary { Id = Id, Title = Title, FileName = FileName, Updated = Updated };
        }

        ///
        ///What a conversation is called: its first question, on one line and cut to <see cref="MostTitleCharacters"/>.
        ///"New chat" when nothing has been asked yet, which the list never shows, since a chat is kept from its first
        ///question on.
        ///
        public static string TitleFor(IEnumerable<ChatTurn> turns)
        {
            var first = turns.FirstOrDefault(turn => turn.Kind == ChatTurn.User);

            if (first is null)
                return "New chat";

            string flat = string.Join(" ", first.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (flat.Length <= MostTitleCharacters)
                return flat;

            return flat.Substring(0, MostTitleCharacters).TrimEnd() + "...";
        }

        ///<summary>A conversation's turns, kept. The window's own copies are left as they are.</summary>
        public static List<SavedTurn> Keep(IEnumerable<ChatTurn> turns)
        {
            return turns.Select(SavedTurn.From).ToList();
        }

        ///<summary>The turns again, to put back into a conversation - see <see cref="AiChat.Restore"/>.</summary>
        public List<ChatTurn> Unkept()
        {
            return Turns.Select(turn => turn.ToTurn()).ToList();
        }
    }

    ///<summary>One row of the chats list: enough to name a chat and order it, without reading every turn of every chat.</summary>
    public sealed class ChatSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("file")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }
    }

    ///
    ///One <see cref="ChatTurn"/> as it is kept. A card that was still waiting when it was kept comes back answered no -
    ///nothing can let it go after a reload - which is what a stop does to one.
    ///
    public sealed class SavedTurn
    {
        [JsonPropertyName("kind")]
        public string Kind { get; set; } = ChatTurn.Note;

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        [JsonPropertyName("tool")]
        public string ToolName { get; set; } = "";

        [JsonPropertyName("arguments")]
        public string ArgumentsJson { get; set; } = "";

        [JsonPropertyName("changes")]
        public bool Changes { get; set; }

        [JsonPropertyName("auto")]
        public bool Auto { get; set; }

        [JsonPropertyName("decision")]
        public bool? Decision { get; set; }

        [JsonPropertyName("result")]
        public string? Result { get; set; }

        [JsonPropertyName("resultIsError")]
        public bool ResultIsError { get; set; }

        [JsonPropertyName("png")]
        public string? Png { get; set; }

        [JsonPropertyName("textBefore")]
        public string? TextBefore { get; set; }

        [JsonPropertyName("textAfter")]
        public string? TextAfter { get; set; }

        [JsonPropertyName("textLine")]
        public int TextLine { get; set; }

        [JsonPropertyName("files")]
        public List<SavedFile> Files { get; set; } = new List<SavedFile>();

        [JsonPropertyName("usage")]
        public LlmUsage? Usage { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("cost")]
        public decimal? Cost { get; set; }

        public static SavedTurn From(ChatTurn turn)
        {
            var kept = new SavedTurn
            {
                Kind = turn.Kind,
                Text = turn.Text,
                ToolName = turn.ToolName,
                ArgumentsJson = turn.ArgumentsJson,
                Changes = turn.Changes,
                Auto = turn.Auto,
                Decision = turn.Decision,
                Result = turn.Result,
                ResultIsError = turn.ResultIsError,
                Png = turn.Png,
                Usage = turn.Usage?.Copy(),
                Model = turn.Model,
                Cost = turn.Cost
            };

            if (turn.Kind == ChatTurn.Tool && kept.Decision is null)
                kept.Decision = false;

            if (turn.TextChange is TextChange change)
            {
                kept.TextBefore = change.Before;
                kept.TextAfter = change.After;
                kept.TextLine = change.FirstLine;
            }

            foreach (var file in turn.Attachments)
                kept.Files.Add(new SavedFile { Name = file.Name, MediaType = file.MediaType, Data = file.Data, Excluded = file.Excluded });

            return kept;
        }

        public ChatTurn ToTurn()
        {
            var turn = new ChatTurn
            {
                Kind = Kind,
                Text = Text,
                ToolName = ToolName,
                ArgumentsJson = ArgumentsJson,
                Changes = Changes,
                Auto = Auto,
                Decision = Decision,
                Result = Result,
                ResultIsError = ResultIsError,
                Png = Png,
                Usage = Usage,
                Model = Model,
                Cost = Cost
            };

            if (TextBefore is not null && TextAfter is not null)
                turn.TextChange = new TextChange(TextBefore, TextAfter, TextLine);

            foreach (var file in Files)
                turn.Attachments.Add(new AiAttachment(file.Name, file.MediaType, file.Data) { Excluded = file.Excluded });

            return turn;
        }
    }

    ///<summary>A file a question was sent with, kept with its bytes so it goes with the questions after it as it did before.</summary>
    public sealed class SavedFile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("type")]
        public string MediaType { get; set; } = "";

        [JsonPropertyName("data")]
        public byte[] Data { get; set; } = Array.Empty<byte>();

        [JsonPropertyName("excluded")]
        public bool Excluded { get; set; }
    }
}
