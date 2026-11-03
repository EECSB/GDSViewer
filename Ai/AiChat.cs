using System.Globalization;
using System.Text;

namespace GDSViewer.Ai
{
    ///<summary>One entry in the conversation, including the ones that are not something anybody said.</summary>
    public sealed class ChatTurn
    {
        public const string User = "user";
        public const string Assistant = "assistant";
        public const string Tool = "tool";
        public const string Error = "error";

        ///
        ///A run of tool calls paused at its limit of rounds with nothing wrong (2026-11-01, asked for): shown as the
        ///question whether to go on for another as many, with Continue on it rather than as an error, and left out of what the model is sent, as
        ///an error is - the calls before it go, said in words, with whatever is sent next.
        ///
        public const string Paused = "paused";

        ///<summary>A line from the app rather than from either side - a new file opened, say - and where the conversation the model is sent starts again.</summary>
        public const string Note = "note";

        ///
        ///The briefing a compaction put in place of the conversation before it: sent as the person's message, with the
        ///files that conversation carried kept on it unticked, and where the conversation the model is sent starts. See
        ///<see cref="AiChat.CompactAsync"/>.
        ///
        public const string Summary = "summary";

        ///
        ///A line from the app where a kept chat was opened again (2026-10-31): shown as a note, but **not where the
        ///conversation the model is sent starts** - carrying a chat on is the point of opening it. What it says goes to
        ///the model too, with the warning that the shape numbers above were another session's. See
        ///<see cref="AiChat.Restore"/>.
        ///
        public const string Reopened = "reopened";

        public required string Kind { get; init; }

        public string Text { get; set; } = "";

        ///<summary>The tool a tool turn asked to call.</summary>
        public string ToolName { get; init; } = "";

        ///<summary>What it was asked to call it with, as the model sent it.</summary>
        public string ArgumentsJson { get; init; } = "";

        ///<summary>Whether the call changes the layout, which is worth showing differently from a read.</summary>
        public bool Changes { get; init; }

        ///<summary>Whether the mode answered for the person. The card is shown either way; only the wording differs.</summary>
        public bool Auto { get; set; }

        ///<summary>Null while the card is still waiting to be answered.</summary>
        public bool? Decision { get; set; }

        ///<summary>What the call answered. Null until it has, and on a call that was declined.</summary>
        public string? Result { get; set; }

        public bool ResultIsError { get; set; }

        ///<summary>The picture the call took, as base64, for the card to show what the model was shown.</summary>
        public string? Png { get; set; }

        ///<summary>What a change did to the file's text, for the card to show as a diff. Null for a read, and for a change that was not made.</summary>
        public TextChange? TextChange { get; set; }

        ///<summary>How the waiting call is let go when one of the card's buttons is pressed.</summary>
        public TaskCompletionSource<bool>? Gate { get; set; }

        ///<summary>The files a question was sent with, in the order they were attached. Empty on every other kind of turn, and on a question that failed - see <see cref="AiChat.GaveFilesBack"/>.</summary>
        public List<AiAttachment> Attachments { get; } = new List<AiAttachment>();

        ///<summary>What the run this turn ended was billed for, every request of it. Null on a turn that ended none, and where the provider said nothing.</summary>
        public LlmUsage? Usage { get; set; }

        ///<summary>The model that ran it, which the rates are a model's.</summary>
        public string Model { get; set; } = "";

        ///<summary>What <see cref="Usage"/> cost at the model's rates, or null where there is no rate for it.</summary>
        public decimal? Cost { get; set; }
    }

    ///<summary>How a message went, for the window to do what follows: put a failed question's words back with its files, say.</summary>
    public enum ChatOutcome
    {
        ///<summary>Nothing was sent: an answer was still coming, or there were no words.</summary>
        NotSent,

        Answered,

        ///<summary>A refusal or an error ended it.</summary>
        Failed,

        ///<summary>The calls reached the cap on rounds still going somewhere, and wait for Continue.</summary>
        Paused,

        ///<summary>Stop, or Clear, ended it.</summary>
        Stopped
    }

    ///<summary>What one model has been billed for in this window.</summary>
    public sealed class ModelSpend
    {
        public LlmUsage Usage { get; } = new LlmUsage();

        ///<summary>What the priced part of it cost - null until an answer from it had a rate to price it at.</summary>
        public decimal? Cost { get; set; }
    }

    ///
    ///The conversation behind the AI assistant window: what has been said, what has been run, and the run in progress - and,
    ///since 2026-10-29, the files waiting to go with the next question, what it has all been billed for, the tools
    ///switched off, and the briefings compacting it leaves.
    ///
    ///**Out of the component and into a class of its own**, which is the one structural change from GraphDBViewer,
    ///where all of this lives in NlQueryModal: here a whole conversation - approvals, declines, a stop in the middle
    ///of one - can be driven by a test, with a stand-in provider and a real layout, and no browser.
    ///
    public sealed class AiChat
    {
        ///<summary>The most of a call's arguments and of its answer replayed into later messages - enough to say what was done, short of a polygon's every corner.</summary>
        private const int MostReplayed = 600;

        ///
        ///How much history every question has to be re-sending before compacting it is suggested, in tokens -
        ///ConfigurationRuleEditor's default. Counted over the conversation alone, files included: a short conversation
        ///about one big file measured with the instructions and the tools tripped it where compacting saves nothing.
        ///
        public const long CompactAfterTokens = 20_000;

        ///
        ///How many questions have to have been asked since the conversation began, was compacted or started afresh
        ///before compacting it is suggested - so one question with a big file does not trip it on its own. That file
        ///is better unticked in the files panel than summarized while the next question may still need it.
        ///
        public const int QuestionsBeforeCompacting = 3;

        ///<summary>About how long a briefing comes back, in tokens, for pricing a compaction before it runs.</summary>
        public const long BriefingTokens = 2_000;

        ///
        ///What a compaction asks for: a briefing the assistant can carry on from, with the things a layout conversation
        ///turns on kept exactly - ConfigurationRuleEditor's CompactSystem, for layouts. Shape numbers above all: they
        ///stay valid through a compaction, since the tools' session is the same, and a briefing that said "the
        ///rectangle" where the conversation said #12 would leave the next question to find it again.
        ///
        public const string CompactSystem =
            "You compress a conversation between a person and the layout assistant in GDS Viewer, a GDSII layout editor, so "
            + "that it can go on in a smaller context. Write a briefing for the assistant that picks the conversation up: what "
            + "the person is working on; the file, cells and layers that came up, by the names and numbers used; shape numbers "
            + "like #12, coordinates and sizes in microns, exactly as they were and never rounded or paraphrased, since the "
            + "shape numbers stay valid; what was drawn or changed, what the person declined and what a tool refused, and why; "
            + "and what is still open. Where an attached file mattered, carry over the specific facts that were used from it, "
            + "not the file. Plain prose under short headings, no preamble and no closing remarks, and length in proportion "
            + "to what happened: a short conversation compresses to a short briefing.";

        ///<summary>How a briefing is introduced when it is sent, since nobody typed it.</summary>
        public const string SummaryPreface = "Summary of the earlier conversation, compacted to save tokens:";

        private readonly List<ChatTurn> turns = new List<ChatTurn>();

        private CancellationTokenSource? cancel;

        ///<summary>The card a call is waiting on, so what it answered goes on the right one. One is enough: both providers run calls one after another.</summary>
        private ChatTurn? pendingTool;

        ///<summary>Counts conversations, so a run stopped by Clear does not write its ending into the one that replaced it.</summary>
        private int generation;

        private readonly List<AiAttachment> pending = new List<AiAttachment>();

        private readonly Dictionary<string, ModelSpend> spend = new Dictionary<string, ModelSpend>();

        public IReadOnlyList<ChatTurn> Turns
        {
            get { return turns; }
        }

        public bool Generating { get; private set; }

        ///<summary>Whether what is running is a compaction rather than an answer - see <see cref="CompactAsync"/>.</summary>
        public bool Compacting { get; private set; }

        ///
        ///The tools switched off in the window's tool picker, by name: not offered to the model, refused if it calls one
        ///anyway, and named in its instructions so it says what it would have used rather than that it cannot -
        ///GraphDBViewer's finding about a model with the changing tools taken away, which this is a person's own choice
        ///to do. ConfigurationRuleEditor's context picker, 2026-10-29.
        ///
        ///**Kept through Clear and another file opened**, as ConfigurationRuleEditor keeps its picker's choices:
        ///somebody who switched a tool off wants it off in the next conversation too. Not through a reload.
        ///
        public HashSet<string> ToolsOff { get; } = new HashSet<string>(StringComparer.Ordinal);

        ///<summary>Whether the suggestion to compact was put away. It comes back after the next compaction, and in a new conversation.</summary>
        public bool CompactSuggestionDismissed { get; set; }

        ///
        ///The files waiting to go with the next question, in the order they were attached - the chips over the box.
        ///Kept through Clear and through another file opened, as the words in the box are.
        ///
        public IReadOnlyList<AiAttachment> Pending
        {
            get { return pending; }
        }

        ///
        ///Whether the last message failed with files, and gave them back to the box.
        ///
        ///**A file the provider refused would be refused again with every later question**, since each one sends the
        ///conversation whole - a PDF past its page limit, a picture it cannot read. So a question that fails takes its
        ///files off itself and puts them back where one can be taken off before asking again, which is
        ///ConfigurationRuleEditor's rule; the window puts the question's words back with them when the box is empty.
        ///
        public bool GaveFilesBack { get; private set; }

        ///
        ///What has been billed, by model, since the page opened. **Clear does not take it back**: what was spent was
        ///spent, and a figure that went to nothing with every new conversation would hide what a day of them came to.
        ///
        public IReadOnlyDictionary<string, ModelSpend> Spend
        {
            get { return spend; }
        }

        ///<summary>Everything billed, every model together.</summary>
        public LlmUsage Used
        {
            get
            {
                var used = new LlmUsage();

                foreach (var model in spend.Values)
                    used.Add(model.Usage);

                return used;
            }
        }

        ///<summary>What the priced part of <see cref="Used"/> cost, or null until something had a rate to be priced at.</summary>
        public decimal? Cost
        {
            get
            {
                decimal? total = null;

                foreach (var model in spend.Values)
                {
                    if (model.Cost is decimal dollars)
                        total = (total ?? 0m) + dollars;
                }

                return total;
            }
        }

        ///<summary>The bytes of every file the conversation still sends with each question: the ticked ones since it last started afresh.</summary>
        public long SentBytes
        {
            get
            {
                long bytes = 0;

                foreach (var (_, file) in SentFiles())
                {
                    if (!file.Excluded)
                        bytes += file.Data.Length;
                }

                return bytes;
            }
        }

        public long PendingBytes
        {
            get { return pending.Sum(file => (long)file.Data.Length); }
        }

        ///
        ///How far the model may go on its own.
        ///
        ///**Reads run, changes ask, to start with** - a rung up from GraphDBViewer, which starts every conversation at
        ///Ask. A read here only looks at the file the person has open, and a model that has to stop and be waved
        ///through each look is a model nobody uses; a change still waits. The rung that changes the layout unasked is
        ///never where the window starts, since a reload should not find it already on.
        ///
        ///**In force from the moment it is chosen** (2026-10-21, asked for), in the middle of an answer too: every call
        ///reads it as it is made, and a card already waiting that the new rung would not have stopped for is let go -
        ///approved, and marked as run without asking. It was read once as a run started, so a switch to Change without
        ///asking went on putting up cards until the answer was finished. A rung that asks more leaves a waiting card
        ///waiting, and only stops the calls after it.
        ///
        ///<summary>How many rounds of tool calls a message may take before the run pauses to ask - see <see cref="ToolRoundsPreference"/>.</summary>
        public int MaxToolRounds { get; set; } = LlmProviderFactory.DefaultToolRounds;

        public ToolApprovalMode Mode
        {
            get
            {
                return mode;
            }
            set
            {
                mode = value;

                if (pendingTool is not ChatTurn waiting || waiting.Decision is not null || waiting.Gate is null)
                    return;

                if (ApprovingToolRunner.NeedsApproval(value, waiting.Changes))
                    return;

                //Marked only when this is what let it go, rather than a button pressed a moment before.
                if (waiting.Gate.TrySetResult(true))
                    waiting.Auto = true;
            }
        }

        private ToolApprovalMode mode = ToolApprovalMode.AutoRead;

        ///<summary>Raised whenever the conversation changes, so the window can draw it again.</summary>
        public event Func<Task>? Changed;

        ///<summary>What a reopened chat tells the model after the app's own line, since the tools' session that numbered its shapes is gone.</summary>
        public const string ReopenedWarning = "The shape numbers used above were given out before, and no longer name the same shapes - find a shape again before using its number.";

        ///
        ///Says something and waits for the answer: with the runner's tools, through the approval gate, or with none.
        ///
        ///The person's words are added before anything is sent, so they are on screen at once; the answer, a refusal
        ///or a stop is added when it comes. Nothing thrown gets out - a failure is a turn that says what went wrong.
        ///
        ///**The files waiting in the box go with it**, and the box is left empty for the next question. **What the run
        ///was billed for is counted however it ends** - answered, refused, stopped, or cleared away - since each request
        ///of it was billed as it was answered; the turn that ends it carries the figure.
        ///
        public async Task<ChatOutcome> SendAsync(string text, ILlmProvider provider, string systemPrompt, ILlmToolRunner? tools)
        {
            if (Generating || string.IsNullOrWhiteSpace(text))
                return ChatOutcome.NotSent;

            var question = new ChatTurn { Kind = ChatTurn.User, Text = text.Trim() };

            question.Attachments.AddRange(pending);
            pending.Clear();
            turns.Add(question);

            var conversation = BuildConversation();
            int asOf = generation;
            bool changedAnything = false;
            var before = provider.Spent.Copy();
            var outcome = ChatOutcome.Stopped;
            ChatTurn? ending = null;

            var source = new CancellationTokenSource();
            var token = source.Token;

            Generating = true;
            GaveFilesBack = false;
            cancel = source;

            await changed();

            try
            {
                LlmResult result;

                if (tools is null)
                {
                    result = await provider.CompleteAsync(systemPrompt, conversation, token);
                }
                else
                {
                    async Task done(ToolRunRequest request, LlmToolOutput? output)
                    {
                        if (output is not null && request.Changes && !output.IsError)
                            changedAnything = true;

                        await completedAsync(output);
                    }

                    //The rung as it is when each call is made, not as it was when this began - see Mode.
                    var approving = new ApprovingToolRunner(tools, approveAsync, done, () => Mode);

                    result = await provider.CompleteWithToolsAsync(systemPrompt, conversation, approving, token, MaxToolRounds);
                }

                if (asOf != generation)
                    return ChatOutcome.Stopped;

                if (result.IsError)
                {
                    ending = new ChatTurn { Kind = ChatTurn.Error, Text = result.Error + changesStay(changedAnything) };
                    outcome = ChatOutcome.Failed;
                }
                else if (result.IsPaused)
                {
                    ending = new ChatTurn { Kind = ChatTurn.Paused, Text = result.Text };
                    outcome = ChatOutcome.Paused;
                }
                else
                {
                    ending = new ChatTurn { Kind = ChatTurn.Assistant, Text = result.Text };
                    outcome = ChatOutcome.Answered;
                }

                turns.Add(ending);
            }
            catch (OperationCanceledException)
            {
                outcome = ChatOutcome.Stopped;

                if (asOf == generation)
                {
                    ending = new ChatTurn { Kind = ChatTurn.Error, Text = "Stopped." + changesStay(changedAnything) };
                    turns.Add(ending);
                }
            }
            catch (Exception failed)
            {
                outcome = ChatOutcome.Failed;

                if (asOf == generation)
                {
                    ending = new ChatTurn { Kind = ChatTurn.Error, Text = failed.Message };
                    turns.Add(ending);
                }
            }
            finally
            {
                record(provider, before, ending);

                if (asOf == generation)
                {
                    Generating = false;
                    pendingTool = null;

                    if (outcome == ChatOutcome.Failed)
                        giveFilesBack(question);
                }

                if (ReferenceEquals(cancel, source))
                    cancel = null;

                source.Dispose();

                await changed();
            }

            return outcome;
        }

        ///
        ///Asks the model for a briefing that can stand in for the conversation so far, and puts it in its place - see
        ///<see cref="ChatTurn.Summary"/>. ConfigurationRuleEditor's compaction (2026-10-29), which is there because a
        ///conversation only grows: every question sends all of it again, its files included.
        ///
        ///**One request, no tools**: the transcript - questions, answers, and each call with what came of it - under
        ///<see cref="CompactSystem"/>, with the files still ticked, on the model picked. ConfigurationRuleEditor moves a
        ///pick of a provider's top model down to its middle one; a model here is one somebody saved with its own key and
        ///address, and the one they picked is the one asked.
        ///
        ///**What was folded away stays on screen and is not sent** - the briefing is where the conversation the model is
        ///sent starts, as a note is - and its files stay on the briefing, unticked: what was used from them is in the
        ///briefing now, and sending them with every question after it would go on paying for what compacting stops paying
        ///for. Any one can be ticked again in the files panel. **On any failure - an error, a refusal, Stop - the
        ///conversation is as it was**, with a line saying so; what the request was billed for is counted either way.
        ///
        public async Task<ChatOutcome> CompactAsync(ILlmProvider provider)
        {
            if (WhyNotCompact() is not null)
                return ChatOutcome.NotSent;

            var files = new List<AiAttachment>();

            foreach (var (_, file) in SentFiles())
            {
                if (!file.Excluded)
                    files.Add(file);
            }

            var request = new List<LlmMessage> { LlmMessage.FromUser(compactionAsk(), files) };
            int asOf = generation;
            var before = provider.Spent.Copy();
            var outcome = ChatOutcome.Stopped;
            ChatTurn? ending = null;

            var source = new CancellationTokenSource();

            Generating = true;
            Compacting = true;
            cancel = source;

            await changed();

            try
            {
                var result = await provider.CompleteAsync(CompactSystem, request, source.Token);

                if (asOf != generation)
                    return ChatOutcome.Stopped;

                if (result.IsError || string.IsNullOrWhiteSpace(result.Text))
                {
                    string why = result.Error;

                    if (!result.IsError)
                        why = "The model wrote no briefing.";

                    ending = new ChatTurn { Kind = ChatTurn.Error, Text = $"The conversation could not be compacted, and is as it was. {why}" };
                    outcome = ChatOutcome.Failed;
                }
                else
                {
                    ending = new ChatTurn { Kind = ChatTurn.Summary, Text = result.Text.Trim() };

                    foreach (var (_, file) in SentFiles())
                        ending.Attachments.Add(file.Unticked());

                    outcome = ChatOutcome.Answered;
                    CompactSuggestionDismissed = false;
                }

                turns.Add(ending);
            }
            catch (OperationCanceledException)
            {
                outcome = ChatOutcome.Stopped;

                if (asOf == generation)
                {
                    ending = new ChatTurn { Kind = ChatTurn.Error, Text = "Compacting was stopped. The conversation is as it was." };
                    turns.Add(ending);
                }
            }
            catch (Exception failed)
            {
                outcome = ChatOutcome.Failed;

                if (asOf == generation)
                {
                    ending = new ChatTurn { Kind = ChatTurn.Error, Text = $"The conversation could not be compacted, and is as it was. {failed.Message}" };
                    turns.Add(ending);
                }
            }
            finally
            {
                record(provider, before, ending);

                if (asOf == generation)
                {
                    Generating = false;
                    Compacting = false;
                }

                if (ReferenceEquals(cancel, source))
                    cancel = null;

                source.Dispose();

                await changed();
            }

            return outcome;
        }

        ///<summary>Why the conversation cannot be compacted now, or null when it can.</summary>
        public string? WhyNotCompact()
        {
            if (Generating)
                return "Not while an answer is coming.";

            if (QuestionsSinceAfresh > 0)
                return null;

            int from = afresh();

            if (from < turns.Count && turns[from].Kind == ChatTurn.Summary)
                return "Nothing has been asked since it was last compacted.";

            return "There is nothing to compact yet.";
        }

        ///<summary>The questions asked since the conversation began, was last compacted, or started afresh with another file.</summary>
        public int QuestionsSinceAfresh
        {
            get
            {
                int questions = 0;

                for (int i = afresh(); i < turns.Count; i++)
                {
                    if (turns[i].Kind == ChatTurn.User)
                        questions++;
                }

                return questions;
            }
        }

        ///
        ///About how many tokens of history every question sends again - the part a compaction replaces: the conversation
        ///since it last started afresh, with its ticked files, and not the instructions or the tools, which go with every
        ///question whatever is done to it.
        ///
        public long HistoryTokens(string providerType)
        {
            long tokens = 0;

            foreach (var message in BuildConversation())
                tokens += AiEstimate.Message(message, providerType);

            return tokens;
        }

        ///
        ///Whether to suggest compacting: enough has been asked since the conversation began or was last compacted, and the
        ///history every question sends again has passed <see cref="CompactAfterTokens"/>. A compaction costs a reading of
        ///that history, so it is only worth it where there is a lot to stop sending and a conversation likely to go on.
        ///
        public bool WorthCompacting(string providerType)
        {
            if (Generating || CompactSuggestionDismissed)
                return false;

            if (QuestionsSinceAfresh < QuestionsBeforeCompacting)
                return false;

            return HistoryTokens(providerType) >= CompactAfterTokens;
        }

        ///<summary>What compacting would cost at <paramref name="price"/>: the history and the instructions read once, and a briefing written. Null where there is no rate.</summary>
        public static decimal? CompactCost(LlmPrice? price, long historyTokens)
        {
            if (price is null)
                return null;

            var usage = new LlmUsage
            {
                InputTokens = historyTokens + AiEstimate.TokensIn(CompactSystem.Length),
                OutputTokens = BriefingTokens,
                Requests = 1
            };

            return price.Cost(usage);
        }

        ///
        ///The conversation written out as text, to read back another day: every turn there is, the part a compaction
        ///folded away included, with what each call did and what each answer was billed - ConfigurationRuleEditor's
        ///Export. The files are named, with their sizes, not included: they are still on the machine they came from.
        ///
        public string Transcript(string fileName, DateTime at)
        {
            var text = new StringBuilder();

            if (fileName.Length > 0)
                text.AppendLine($"AI assistant conversation - {fileName}");
            else
                text.AppendLine("AI assistant conversation");

            text.AppendLine($"Exported {at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");

            foreach (var turn in turns)
            {
                text.AppendLine();

                if (turn.Kind == ChatTurn.User)
                {
                    text.AppendLine("YOU");
                    text.AppendLine(turn.Text);
                    appendFiles(text, "Attached", turn.Attachments);
                }
                else if (turn.Kind == ChatTurn.Assistant)
                {
                    text.AppendLine("ASSISTANT");
                    text.AppendLine(turn.Text);
                }
                else if (turn.Kind == ChatTurn.Tool)
                {
                    appendCall(text, turn);
                }
                else if (turn.Kind == ChatTurn.Summary)
                {
                    text.AppendLine("SUMMARY OF THE ABOVE (compacted)");
                    text.AppendLine(turn.Text);
                    appendFiles(text, "Kept from before it", turn.Attachments);
                }
                else
                {
                    text.AppendLine($"({turn.Text})");
                }

                if (turn.Usage is not null)
                    text.AppendLine($"  Billed: {UsageLine(turn)}, on {turn.Model}");
            }

            return text.ToString();
        }

        ///<summary>What a run was billed for, as one line: the requests it took, the tokens each way, and the cost where there is a rate.</summary>
        public static string UsageLine(ChatTurn turn)
        {
            if (turn.Usage is not LlmUsage used)
                return "";

            var line = new StringBuilder();

            if (used.Requests == 1)
                line.Append("1 request");
            else
                line.Append($"{used.Requests} requests");

            line.Append($" · {AiPricing.Count(used.InputTokens)} tokens in");

            if (used.CachedInputTokens > 0)
                line.Append($" ({AiPricing.Count(used.CachedInputTokens)} cached)");

            line.Append($" · {AiPricing.Count(used.OutputTokens)} out");

            if (turn.Cost is decimal dollars)
                line.Append($" · {AiPricing.Money(dollars)}");

            return line.ToString();
        }

        private static void appendFiles(StringBuilder text, string said, IReadOnlyList<AiAttachment> files)
        {
            if (files.Count == 0)
                return;

            var named = new List<string>();

            foreach (var file in files)
            {
                string size = AiAttachment.Size(file.Data.Length);

                if (file.Excluded)
                    named.Add($"{file.Name} ({size}, not sent)");
                else
                    named.Add($"{file.Name} ({size})");
            }

            text.AppendLine($"  {said}: {string.Join(", ", named)}");
        }

        ///<summary>A call, in the export: what was asked for, and what came of it in full.</summary>
        private static void appendCall(StringBuilder text, ChatTurn turn)
        {
            string called = $"{turn.ToolName} {turn.ArgumentsJson.Trim()}".Trim();

            if (turn.Decision == false)
            {
                text.AppendLine($"  Asked to call {called} - declined.");

                return;
            }

            if (turn.Decision is null || turn.Result is null)
            {
                text.AppendLine($"  Called {called} - it did not finish.");

                return;
            }

            if (turn.ResultIsError)
                text.AppendLine($"  Called {called} - refused:");
            else
                text.AppendLine($"  Called {called} - it answered:");

            foreach (string line in turn.Result.Trim().Split('\n'))
                text.AppendLine($"    {line.TrimEnd('\r')}");

            if (turn.Png is not null)
                text.AppendLine("    (and a picture)");
        }

        ///
        ///The one message of a compaction: the conversation since it last started afresh, as a transcript - an earlier
        ///briefing, the questions and answers, and each call with what came of it - and the ask. What was decided goes
        ///in with what was said: a briefing that lost a decline would walk the next answer into asking for the same thing.
        ///
        private string compactionAsk()
        {
            var text = new StringBuilder();

            text.AppendLine("Here is the conversation to compress. The files still sent with it, if any, are above.");
            text.AppendLine();
            text.AppendLine("<conversation>");

            var ran = new StringBuilder();

            for (int i = afresh(); i < turns.Count; i++)
            {
                var turn = turns[i];

                if (turn.Kind == ChatTurn.Tool)
                {
                    appendRunOf(ran, turn);

                    continue;
                }

                if (ran.Length > 0)
                {
                    text.AppendLine("ASSISTANT'S TOOL CALLS:");
                    text.Append(ran);
                    text.AppendLine();
                    ran.Clear();
                }

                if (turn.Kind == ChatTurn.Summary)
                {
                    text.AppendLine("SUMMARY LEFT BY AN EARLIER COMPACTION:");
                    text.AppendLine(turn.Text);
                    text.AppendLine();
                }
                else if (turn.Kind == ChatTurn.User)
                {
                    text.AppendLine("PERSON:");
                    text.AppendLine(turn.Text);
                    text.AppendLine();
                }
                else if (turn.Kind == ChatTurn.Assistant)
                {
                    text.AppendLine("ASSISTANT:");
                    text.AppendLine(turn.Text);
                    text.AppendLine();
                }
                else if (turn.Kind == ChatTurn.Error || turn.Kind == ChatTurn.Paused)
                {
                    text.AppendLine($"(The answer ended there: {turn.Text})");
                    text.AppendLine();
                }
            }

            if (ran.Length > 0)
            {
                text.AppendLine("ASSISTANT'S TOOL CALLS:");
                text.Append(ran);
                text.AppendLine();
            }

            text.AppendLine("</conversation>");
            text.AppendLine();
            text.Append("Write the briefing now.");

            return text.ToString();
        }

        ///<summary>One call in the compaction's transcript, said from outside rather than to the model that made it.</summary>
        private static void appendRunOf(StringBuilder ran, ChatTurn turn)
        {
            string called = $"{turn.ToolName} {shortened(turn.ArgumentsJson)}";

            if (turn.Decision == false)
                ran.AppendLine($"- asked to call {called}, and the person declined.");
            else if (turn.Decision is null || turn.Result is null)
                ran.AppendLine($"- called {called}, and it did not finish.");
            else if (turn.ResultIsError)
                ran.AppendLine($"- called {called}, and it was refused: {shortened(turn.Result)}");
            else
                ran.AppendLine($"- called {called}, and it answered: {shortened(turn.Result)}");
        }

        ///
        ///Puts a file in the box for the next question, or says why not: what the file is (see
        ///<see cref="AiAttachment.Problem"/>), whether a workbook or a document can be read, how many one question
        ///carries, and how much one conversation sends.
        ///
        public string? Attach(AiAttachment file)
        {
            if (AiAttachment.Problem(file.Name, file.MediaType, file.Data) is string problem)
                return problem;

            if (pending.Count >= AiAttachment.MostPerQuestion)
                return $"A question can carry {AiAttachment.MostPerQuestion} files at most, so {file.Name} was left out. Send these with a question first.";

            long total = SentBytes + PendingBytes + file.Data.Length;

            if (total > AiAttachment.MostConversationBytes)
                return $"{file.Name} would bring this conversation's files to {AiAttachment.Size(total)}, past the {AiAttachment.Size(AiAttachment.MostConversationBytes)} one conversation can send - every question sends them all again. Untick a file in the files panel, or clear the conversation.";

            //Last, as the one that reads a workbook or a document through.
            if (file.Unreadable is string unreadable)
                return unreadable;

            pending.Add(file);

            return null;
        }

        ///<summary>Takes a file out of the box before it was sent.</summary>
        public void Unattach(AiAttachment file)
        {
            pending.Remove(file);
        }

        ///
        ///Ticks a file sent earlier back in, or unticks it, from the files panel - or says why it cannot be ticked.
        ///
        ///Unticking is always allowed: it is the way out of a conversation that can take no more. Ticking one in again is
        ///held to the same budget as attaching it was.
        ///
        public string? Include(AiAttachment file, bool include)
        {
            if (!include)
            {
                file.Excluded = true;

                return null;
            }

            if (!file.Excluded)
                return null;

            long total = SentBytes + PendingBytes + file.Data.Length;

            if (total > AiAttachment.MostConversationBytes)
                return $"{file.Name} would bring this conversation's files to {AiAttachment.Size(total)}, past the {AiAttachment.Size(AiAttachment.MostConversationBytes)} one conversation can send. Untick another first.";

            file.Excluded = false;

            return null;
        }

        ///<summary>Every file sent since the conversation last started afresh, with the question it went with - or the briefing that keeps it, after a compaction: what the files panel lists, ticked or not.</summary>
        public List<(ChatTurn Question, AiAttachment File)> SentFiles()
        {
            var files = new List<(ChatTurn Question, AiAttachment File)>();

            for (int i = afresh(); i < turns.Count; i++)
            {
                foreach (var file in turns[i].Attachments)
                    files.Add((turns[i], file));
            }

            return files;
        }

        ///<summary>A failed question's files back in the box, ahead of any attached since - see <see cref="GaveFilesBack"/>.</summary>
        private void giveFilesBack(ChatTurn question)
        {
            if (question.Attachments.Count == 0)
                return;

            pending.InsertRange(0, question.Attachments);
            question.Attachments.Clear();

            GaveFilesBack = true;
        }

        ///<summary>A run's share of what its provider was billed, put on the turn that ended it and added to <see cref="Spend"/>.</summary>
        private void record(ILlmProvider provider, LlmUsage before, ChatTurn? ending)
        {
            var used = provider.Spent.Since(before);

            if (!used.Reported)
                return;

            decimal? cost = null;

            if (provider.Price is LlmPrice price)
                cost = price.Cost(used);

            if (ending is not null)
            {
                ending.Usage = used;
                ending.Model = provider.Model;
                ending.Cost = cost;
            }

            if (!spend.TryGetValue(provider.Model, out var spent))
            {
                spent = new ModelSpend();
                spend[provider.Model] = spent;
            }

            spent.Usage.Add(used);

            if (cost is decimal dollars)
                spent.Cost = (spent.Cost ?? 0m) + dollars;
        }

        ///<summary>Answers the card a call is waiting on.</summary>
        public void Decide(ChatTurn turn, bool approved)
        {
            turn.Gate?.TrySetResult(approved);
        }

        ///<summary>Stops the run in progress. A card still waiting is answered no, which is what a stop is.</summary>
        public void Cancel()
        {
            cancel?.Cancel();
        }

        ///
        ///Throws the conversation away and starts a new one.
        ///
        ///Stopped first: a run left going would add its answer to a conversation that no longer has the question in
        ///it. Its ending is not added at all - see <see cref="generation"/> - where GraphDBViewer's Clear left a
        ///"Generation canceled." behind in the empty window.
        ///
        public void Clear()
        {
            Cancel();

            generation++;
            turns.Clear();
            pendingTool = null;
            Generating = false;
            Compacting = false;
            CompactSuggestionDismissed = false;
        }

        ///
        ///Puts a kept chat back in place of the conversation, as <see cref="Clear"/> leaves it and with these turns -
        ///then, when there are any, a <see cref="ChatTurn.Reopened"/> line saying so, since the shapes they number were
        ///numbered by a session that is gone. One line however often it is opened: a chat opened twice without a word
        ///in between does not say it twice. The files waiting in the box, the tools switched off and what has been
        ///spent stay, as they do through Clear. No line when there is nothing to say - a chat only being saved as text.
        ///
        public void Restore(IEnumerable<ChatTurn> kept, string? reopened)
        {
            Clear();

            turns.AddRange(kept);

            if (turns.Count == 0 || reopened is null)
                return;

            if (turns[^1].Kind == ChatTurn.Reopened)
                turns[^1].Text = reopened;
            else
                turns.Add(new ChatTurn { Kind = ChatTurn.Reopened, Text = reopened });
        }

        ///
        ///Says why what was typed was not sent - the editor's text could not be read into the file, say. A turn of its
        ///own that the model is never sent, since it is the page talking rather than either side of the conversation.
        ///
        public void Refuse(string why)
        {
            turns.Add(new ChatTurn { Kind = ChatTurn.Error, Text = why });
        }

        ///
        ///Marks where the conversation the model is sent starts again, with a line saying why - another file opened.
        ///
        ///What came before stays on screen, since somebody may still want to read it, but none of it is sent: it was
        ///about a file that is not open, in shape numbers that meant shapes of that file.
        ///
        public void StartAfresh(string why)
        {
            Cancel();

            generation++;
            pendingTool = null;
            Generating = false;
            Compacting = false;
            CompactSuggestionDismissed = false;

            if (turns.Count > 0)
                turns.Add(new ChatTurn { Kind = ChatTurn.Note, Text = why });
        }

        ///
        ///The conversation as the model should see it: everything since the last note, or from the last briefing - which
        ///goes as the person's message, introduced as what it is, with the files kept on it.
        ///
        ///**What was run is replayed in words** rather than dropped. Its answer is what the model's next reply was
        ///built on, and neither provider's message list can carry a tool exchange across a request without rebuilding
        ///that provider's format from a copy of it - so each run is said plainly, as GraphDBViewer does. The calls of
        ///one answer go together in one turn, cut short where an argument is a polygon's worth of corners.
        ///
        public List<LlmMessage> BuildConversation()
        {
            var conversation = new List<LlmMessage>();
            var ran = new StringBuilder();

            for (int i = afresh(); i < turns.Count; i++)
            {
                var turn = turns[i];

                if (turn.Kind == ChatTurn.Tool)
                {
                    appendRun(ran, turn);

                    continue;
                }

                flushRuns(conversation, ran);

                //A copy of the question's files, which a failed run hands back to the box from under it.
                if (turn.Kind == ChatTurn.User)
                    conversation.Add(LlmMessage.FromUser(turn.Text, turn.Attachments.ToArray()));
                else if (turn.Kind == ChatTurn.Assistant)
                    conversation.Add(LlmMessage.FromAssistant(turn.Text));
                else if (turn.Kind == ChatTurn.Summary)
                    conversation.Add(LlmMessage.FromUser($"{SummaryPreface}\n\n{turn.Text}", turn.Attachments.ToArray()));
                else if (turn.Kind == ChatTurn.Reopened)
                    conversation.Add(LlmMessage.FromUser($"(From the app: {turn.Text} {ReopenedWarning})"));
            }

            flushRuns(conversation, ran);

            return conversation;
        }

        ///<summary>Where the conversation the model is sent starts: just after the last note, at the last briefing, or at the beginning.</summary>
        private int afresh()
        {
            int from = 0;

            for (int i = 0; i < turns.Count; i++)
            {
                if (turns[i].Kind == ChatTurn.Note)
                    from = i + 1;
                else if (turns[i].Kind == ChatTurn.Summary)
                    from = i;
            }

            return from;
        }

        ///
        ///Where the turns a compaction folded away end: the last briefing's place, or 0 when there is none. The window
        ///shows what is before it only when asked, dimmed - it is still there to read, and none of it is sent.
        ///
        public int FoldedBefore
        {
            get
            {
                for (int i = turns.Count - 1; i >= 0; i--)
                {
                    if (turns[i].Kind == ChatTurn.Summary)
                        return i;
                }

                return 0;
            }
        }

        private static void appendRun(StringBuilder ran, ChatTurn turn)
        {
            if (turn.Decision is null)
                return;

            if (ran.Length == 0)
                ran.Append("(What you did while answering that:");

            ran.AppendLine();

            string called = $"{turn.ToolName} {shortened(turn.ArgumentsJson)}";

            if (turn.Decision == false)
                ran.Append($"- You asked to call {called}, and the person declined.");
            else if (turn.Result is null)
                ran.Append($"- You called {called}, and it did not finish.");
            else if (turn.ResultIsError)
                ran.Append($"- You called {called}, and it was refused: {shortened(turn.Result)}");
            else
                ran.Append($"- You called {called}, and it answered: {shortened(turn.Result)}");
        }

        private static void flushRuns(List<LlmMessage> conversation, StringBuilder ran)
        {
            if (ran.Length == 0)
                return;

            ran.Append(')');

            conversation.Add(LlmMessage.FromUser(ran.ToString()));
            ran.Clear();
        }

        private static string shortened(string text)
        {
            string flat = text.Trim();

            if (flat.Length <= MostReplayed)
                return flat;

            return flat.Substring(0, MostReplayed) + "...";
        }

        ///<summary>What a stop or a failure adds when the run had already changed the layout: those changes are not taken back.</summary>
        private static string changesStay(bool changedAnything)
        {
            if (!changedAnything)
                return "";

            return " What it changed before that stays - each change is one step to undo.";
        }

        ///
        ///Puts the model's call on screen, and waits for a button to answer it unless the mode already has. A call the
        ///mode let through still gets a card - one that runs unwatched is the one most worth seeing afterwards - it
        ///just does not stop for anybody.
        ///
        private async Task<bool> approveAsync(ToolRunRequest request, CancellationToken ct)
        {
            var turn = new ChatTurn
            {
                Kind = ChatTurn.Tool,
                ToolName = request.ToolName,
                ArgumentsJson = request.ArgumentsJson,
                Changes = request.Changes
            };

            if (request.AutoApproved)
            {
                turn.Auto = true;
                turn.Decision = true;

                turns.Add(turn);
                pendingTool = turn;

                await changed();

                return true;
            }

            //Continuations run on their own, so a button's handler that answers the card does not go on to run the
            //rest of the provider's loop inside its own render.
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            turn.Gate = gate;

            turns.Add(turn);
            pendingTool = turn;

            await changed();

            //A stop while a card is unanswered has to let it go, or the loop waits for a button nobody can press. A
            //stop is not a yes, so it answers the way declining does.
            using var registration = ct.Register(() => gate.TrySetResult(false));

            bool approved = await gate.Task;

            turn.Decision = approved;

            await changed();

            return approved;
        }

        ///<summary>Writes what the call answered onto the card that asked for it.</summary>
        private async Task completedAsync(LlmToolOutput? output)
        {
            if (pendingTool is not null && output is not null)
            {
                pendingTool.Result = output.Text;
                pendingTool.ResultIsError = output.IsError;
                pendingTool.Png = output.Png;
                pendingTool.TextChange = output.TextChange;
            }

            pendingTool = null;

            await changed();
        }

        private async Task changed()
        {
            if (Changed is null)
                return;

            foreach (var listener in Changed.GetInvocationList())
                await ((Func<Task>)listener)();
        }
    }
}
