namespace GDSViewer.Ai
{
    ///
    ///How much the model may do to the layout on its own.
    ///
    ///A ladder, not a set of switches: each rung includes the one below it. What moves between rungs is what still
    ///stops for a person, never what the model may propose - it can always ask to change something, and on every rung
    ///but the top one that ask becomes a card somebody has to answer.
    ///
    public enum ToolApprovalMode
    {
        ///<summary>Nothing runs until a person says so, reads included. One card at a time.</summary>
        Ask,

        ///<summary>Reads run the moment the model asks. A change still waits for a person.</summary>
        AutoRead,

        ///<summary>Reads and changes both run unattended. Every change is still one step on the undo history.</summary>
        AutoReadWrite
    }

    ///<summary>What the model asked to run, in the form a person is asked to approve.</summary>
    public sealed class ToolRunRequest
    {
        public ToolRunRequest(string toolName, string argumentsJson, bool changes, ToolApprovalMode mode)
        {
            ToolName = toolName;
            ArgumentsJson = argumentsJson;
            Changes = changes;
            AutoApproved = !ApprovingToolRunner.NeedsApproval(mode, changes);
        }

        public string ToolName { get; }

        public string ArgumentsJson { get; }

        ///<summary>Whether the call changes the layout rather than only reading it.</summary>
        public bool Changes { get; }

        ///
        ///Whether the mode in force has already answered this. It is still shown - a call that runs unwatched is
        ///exactly the one worth seeing - but as something that happened, not something to decide.
        ///
        public bool AutoApproved { get; }
    }

    ///
    ///Stands between the model and the layout and decides, per call, whether it runs now or waits.
    ///
    ///**Every tool is offered on every rung, changes included.** Withholding the changing tools was worse than gating
    ///them, GraphDBViewer found: the model answers that it is unable to make changes and stops, when the useful thing
    ///is to propose the change and let somebody approve it. So the mode decides only what runs unattended, and
    ///everything else becomes a card.
    ///
    ///A wrapper rather than a flag inside the runner, and nothing in either provider knows about it: the tool loop
    ///already awaits <see cref="ILlmToolRunner.RunToolAsync"/>, so waiting on a person is that await taking longer.
    ///
    public sealed class ApprovingToolRunner : ILlmToolRunner
    {
        ///<summary>What the model is told when a person declines. Phrased as a result, since that is what it is.</summary>
        public const string DeclinedMessage = "The person did not approve that call, so it was not run. Do not run it again. Answer with what you already know, or ask what they would like instead.";

        private readonly ILlmToolRunner inner;
        private readonly Func<ToolRunRequest, CancellationToken, Task<bool>>? approve;
        private readonly Func<ToolRunRequest, LlmToolOutput?, Task>? completed;
        private readonly Func<ToolApprovalMode> mode;

        ///<param name="approve">
        ///Shows the call and answers it. A request whose <see cref="ToolRunRequest.AutoApproved"/> is set has been
        ///answered by the mode already, and should be shown rather than waited on. Awaited otherwise, so it may take as
        ///long as the person does.
        ///</param>
        ///<param name="completed">Told what the tool answered, or null when it was declined, so the same card can show it.</param>
        public ApprovingToolRunner(
            ILlmToolRunner inner,
            Func<ToolRunRequest, CancellationToken, Task<bool>>? approve,
            Func<ToolRunRequest, LlmToolOutput?, Task>? completed = null,
            ToolApprovalMode mode = ToolApprovalMode.Ask)
            : this(inner, approve, completed, () => mode)
        {
        }

        ///
        ///<param name="mode">
        ///Asked for every call rather than once, so a rung chosen while a run is going is the one its next call meets -
        ///asked for 2026-10-21. Read once, a switch to Change without asking went on asking until the answer was done.
        ///</param>
        ///
        public ApprovingToolRunner(
            ILlmToolRunner inner,
            Func<ToolRunRequest, CancellationToken, Task<bool>>? approve,
            Func<ToolRunRequest, LlmToolOutput?, Task>? completed,
            Func<ToolApprovalMode> mode)
        {
            this.inner = inner;
            this.approve = approve;
            this.completed = completed;
            this.mode = mode;

            //The changing tools stay holstered until something asks for them, and this is the something: a runner
            //reached through a gate can offer them, because the gate is what makes them safe.
            if (inner is IEditCapableToolRunner editable)
                editable.AllowEdits = true;
        }

        ///<summary>The rung in force now, which is what the next call is put to.</summary>
        public ToolApprovalMode Mode
        {
            get { return mode(); }
        }

        ///<summary>Whether a call of this kind still has to be put to a person.</summary>
        public static bool NeedsApproval(ToolApprovalMode mode, bool changes)
        {
            if (mode == ToolApprovalMode.AutoReadWrite)
                return false;

            if (mode == ToolApprovalMode.AutoRead && !changes)
                return false;

            return true;
        }

        ///<summary>Every tool the runner has. What the mode changes is which of them stop for a person.</summary>
        public IReadOnlyList<LlmTool> Tools
        {
            get { return inner.Tools; }
        }

        public async Task<LlmToolOutput> RunToolAsync(string name, string argumentsJson, CancellationToken ct)
        {
            var request = new ToolRunRequest(name, argumentsJson, changes(name), Mode);

            //No approver is a misconfiguration, not permission. Refusing is the safe reading of it - in the auto modes
            //too, where the approver is also how what ran gets shown.
            if (approve is null)
                return LlmToolOutput.Declined(DeclinedMessage);

            if (!await approve(request, ct))
            {
                if (completed is not null)
                    await completed(request, null);

                return LlmToolOutput.Declined(DeclinedMessage);
            }

            var output = await inner.RunToolAsync(name, argumentsJson, ct);

            if (completed is not null)
                await completed(request, output);

            return output;
        }

        ///
        ///Whether the tool called changes anything. A name that is no tool changes nothing - the runner will say so -
        ///so it is taken as a read rather than put to a person as though it were a change.
        ///
        private bool changes(string name)
        {
            foreach (var tool in inner.Tools)
            {
                if (tool.Name == name)
                    return tool.Changes;
            }

            return false;
        }
    }
}
