using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///The AI assistant chats as they are kept between visits (2026-10-31): a conversation written out and read back whole, the
///list the chats panel shows, and a kept chat put back to be carried on - with the line that says it was, and the
///warning the model is sent that its shape numbers were another session's.
///
///What would hurt silently is a chat that reads back different from how it was written - a card's diff gone, a
///question's files gone, so the next question no longer sends them - or a kept list that will not read and takes the
///window with it. The window itself, and IndexedDB under it, are the ask-ai spec's.
///
public class AiSavedChatTests
{
    private static List<ChatTurn> Conversation()
    {
        var question = new ChatTurn { Kind = ChatTurn.User, Text = "Draw a square\nat the origin" };

        question.Attachments.Add(new AiAttachment("notes.txt", "text/plain", "met1 only"u8.ToArray()) { Excluded = true });

        return new List<ChatTurn>
        {
            question,
            new ChatTurn
            {
                Kind = ChatTurn.Tool,
                ToolName = "draw_rectangle",
                ArgumentsJson = """{"layer":"met1"}""",
                Changes = true,
                Auto = true,
                Decision = true,
                Result = "Drew #1.",
                Png = "iVBORw0KGgo=",
                TextChange = new TextChange("before", "after", 12)
            },
            new ChatTurn
            {
                Kind = ChatTurn.Assistant,
                Text = "Done.",
                Usage = new LlmUsage { InputTokens = 1000, OutputTokens = 50, Requests = 2 },
                Model = "claude-opus-5-5",
                Cost = 0.005m
            }
        };
    }

    private static SavedChat Kept(List<ChatTurn> turns)
    {
        return new SavedChat
        {
            Id = SavedChat.NewId(),
            Title = SavedChat.TitleFor(turns),
            FileName = "Mosfet.gds",
            Created = new DateTime(2026, 10, 31, 9, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 10, 31, 9, 5, 0, DateTimeKind.Utc),
            Turns = SavedChat.Keep(turns)
        };
    }

    [Fact]
    public void A_chat_reads_back_as_it_was_written_with_its_diffs_pictures_files_and_bill()
    {
        var kept = Kept(Conversation());
        var read = ChatStore.ReadChat(ChatStore.WriteChat(kept));

        Assert.NotNull(read);
        Assert.Equal(kept.Id, read.Id);
        Assert.Equal("Draw a square at the origin", read.Title);
        Assert.Equal("Mosfet.gds", read.FileName);
        Assert.Equal(kept.Updated, read.Updated);

        var turns = read.Unkept();

        Assert.Equal(new[] { ChatTurn.User, ChatTurn.Tool, ChatTurn.Assistant }, turns.Select(turn => turn.Kind));

        var file = Assert.Single(turns[0].Attachments);

        Assert.Equal("notes.txt", file.Name);
        Assert.Equal("met1 only"u8.ToArray(), file.Data);
        Assert.True(file.Excluded);

        var card = turns[1];

        Assert.Equal("draw_rectangle", card.ToolName);
        Assert.True(card.Changes);
        Assert.True(card.Auto);
        Assert.True(card.Decision);
        Assert.Equal("Drew #1.", card.Result);
        Assert.Equal("iVBORw0KGgo=", card.Png);
        Assert.NotNull(card.TextChange);
        Assert.Equal(("before", "after", 12), (card.TextChange.Before, card.TextChange.After, card.TextChange.FirstLine));

        var answer = turns[2];

        Assert.Equal(1000, answer.Usage!.InputTokens);
        Assert.Equal(2, answer.Usage.Requests);
        Assert.True(answer.Usage.Reported);
        Assert.Equal("claude-opus-5-5", answer.Model);
        Assert.Equal(0.005m, answer.Cost);
    }

    ///<summary>A card still waiting when it was kept has nothing to let it go after a reload: it comes back declined, as a stop leaves one.</summary>
    [Fact]
    public void A_card_kept_while_it_waited_comes_back_answered_no()
    {
        var waiting = new ChatTurn { Kind = ChatTurn.Tool, ToolName = "draw_rectangle", Changes = true };

        var read = SavedTurn.From(waiting).ToTurn();

        Assert.False(read.Decision);
        Assert.Null(read.Gate);
    }

    [Fact]
    public void A_title_is_the_first_question_on_one_line_cut_to_a_row()
    {
        var turns = new List<ChatTurn>
        {
            new ChatTurn { Kind = ChatTurn.Note, Text = "A note first" },
            new ChatTurn { Kind = ChatTurn.User, Text = "  What   is\n this?  " },
            new ChatTurn { Kind = ChatTurn.User, Text = "And this?" }
        };

        Assert.Equal("What is this?", SavedChat.TitleFor(turns));
        Assert.Equal("New chat", SavedChat.TitleFor(new List<ChatTurn>()));

        string title = SavedChat.TitleFor(new[] { new ChatTurn { Kind = ChatTurn.User, Text = new string('a', 200) } });

        Assert.Equal(SavedChat.MostTitleCharacters + 3, title.Length);
        Assert.EndsWith("...", title);
    }

    [Fact]
    public void The_list_reads_back_changed_last_first_and_text_that_will_not_read_is_no_chats()
    {
        var older = new ChatSummary { Id = "a", Title = "Older", Updated = new DateTime(2026, 10, 30) };
        var newer = new ChatSummary { Id = "b", Title = "Newer", Updated = new DateTime(2026, 10, 31) };

        var read = ChatStore.ReadList(ChatStore.WriteList(ChatStore.Ordered(new[] { older, newer })));

        Assert.Equal(new[] { "b", "a" }, read.Select(each => each.Id));
        Assert.Empty(ChatStore.ReadList(null));
        Assert.Empty(ChatStore.ReadList("not json"));
        Assert.Null(ChatStore.ReadChat("{"));
        Assert.Null(ChatStore.ReadChat(""));
    }

    ///
    ///**Put back and carried on**: the turns are the window's again, with one line saying it was opened - not two, however
    ///often - and the model is sent all of it, the line and the warning about shape numbers with it. A note would have
    ///cut it off there, as another file opened does; a reopened chat is opened to be carried on.
    ///
    [Fact]
    public void A_kept_chat_put_back_says_so_once_and_the_model_is_sent_all_of_it_with_the_warning()
    {
        var chat = new AiChat();

        chat.Restore(Kept(Conversation()).Unkept(), "Opened again.");

        Assert.Equal(4, chat.Turns.Count);
        Assert.Equal(ChatTurn.Reopened, chat.Turns[^1].Kind);

        chat.Restore(SavedChat.Keep(chat.Turns).Select(turn => turn.ToTurn()), "Opened again with Inverter.gds open - it was about Mosfet.gds.");

        Assert.Equal(4, chat.Turns.Count);
        Assert.Equal("Opened again with Inverter.gds open - it was about Mosfet.gds.", chat.Turns[^1].Text);
        Assert.Equal(0, chat.FoldedBefore);

        var sent = chat.BuildConversation();
        var said = string.Join("\n", sent.Select(message => message.Text));

        Assert.Contains("Draw a square", said);
        Assert.Contains("Done.", said);
        Assert.Contains("You called draw_rectangle", said);
        Assert.Contains("it was about Mosfet.gds", said);
        Assert.Contains(AiChat.ReopenedWarning, said);
        Assert.Equal(LlmMessage.UserRole, sent[^1].Role);
    }

    ///<summary>Put back only to be saved as text, it says nothing about being opened.</summary>
    [Fact]
    public void A_kept_chat_put_back_with_nothing_to_say_has_no_line_and_its_transcript_reads_as_it_did()
    {
        var chat = new AiChat();

        chat.Restore(Kept(Conversation()).Unkept(), null);

        Assert.Equal(3, chat.Turns.Count);

        string text = chat.Transcript("Mosfet.gds", new DateTime(2026, 10, 31, 9, 10, 0));

        Assert.Contains("YOU\nDraw a square", text.Replace("\r\n", "\n"));
        Assert.Contains("ASSISTANT\nDone.", text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("Opened again", text);
    }
}
