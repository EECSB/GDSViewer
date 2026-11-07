namespace GDSViewer.Ai
{
    ///<summary>A tool the model may call, described the way every provider can be told about one.</summary>
    public sealed class LlmTool
    {
        public required string Name { get; init; }

        public required string Description { get; init; }

        ///<summary>JSON Schema, as text, for the object of arguments it takes.</summary>
        public required string InputSchemaJson { get; init; }

        ///
        ///Whether calling it changes the layout, rather than only reading it.
        ///
        ///What the approval mode reads: a change is what waits for a person on the middle rung, where a read runs
        ///at once. GraphDBViewer tells the two apart by the tool's name, having only one tool of each kind.
        ///
        public bool Changes { get; init; }
    }

    ///<summary>A call the model asked for. The id goes back with the answer, so the model knows which call it answers.</summary>
    public sealed class LlmToolCall
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string ArgumentsJson { get; init; }
    }

    ///
    ///What a tool answered: the text the model reads, and a picture when the tool took one.
    ///
    ///**Richer than GraphDBViewer's string**, for two reasons. A picture of the layout is something the model
    ///should see rather than read about, and both providers can take one back - Anthropic inside the tool's result,
    ///the OpenAI-compatible family in a user turn after it. And a refusal is said as one: Anthropic's tool_result has
    ///an is_error flag that GraphDBViewer never set, and a model told that a call failed tries something else rather
    ///than building on it.
    ///
    public sealed class LlmToolOutput
    {
        public required string Text { get; init; }

        ///<summary>Whether the call was refused or failed, with <see cref="Text"/> saying why.</summary>
        public bool IsError { get; init; }

        ///
        ///Whether a person declined the call, so it was not run (<see cref="ApprovingToolRunner.DeclinedMessage"/>). Not an
        ///error to the model, which is asked not to try another way; but no progress either, which <see cref="ToolProgress"/>
        ///could not tell from the words alone until 2026-11-07 (from GraphDBViewer), so a run declined every round was never
        ///stopped as stuck. The providers never read it: the model is told what it was, unmarked.
        ///
        public bool IsDeclined { get; init; }

        ///<summary>A PNG for the model to look at, as base64, or null for an answer in words alone.</summary>
        public string? Png { get; init; }

        ///<summary>What a change did to the file's text, for its card to show - never sent to the model. Null for anything else.</summary>
        public TextChange? TextChange { get; init; }

        ///<summary>A call a person declined: a result to the model, and no progress to the run.</summary>
        public static LlmToolOutput Declined(string text)
        {
            return new LlmToolOutput { Text = text ?? "", IsDeclined = true };
        }
    }

    ///
    ///Runs the tools the model calls.
    ///
    ///Implemented by the app and handed to a provider's loop, so the provider code knows nothing about layouts -
    ///which is what lets it be the same code as GraphDBViewer's, where the runner is a database's.
    ///
    public interface ILlmToolRunner
    {
        ///<summary>The tools offered to the model.</summary>
        IReadOnlyList<LlmTool> Tools { get; }

        ///<summary>Runs one call and says what came of it. Never throws for anything the model did.</summary>
        Task<LlmToolOutput> RunToolAsync(string name, string argumentsJson, CancellationToken ct);
    }

    ///
    ///A runner that owns tools which change the layout, and can be told whether to offer them.
    ///
    ///Off is the default and stays off unless something deliberately turns it on, so a caller that knows nothing
    ///about changes cannot hand the model one by accident. <see cref="ApprovingToolRunner"/> is what turns it on,
    ///because the gate it puts in front of every change is what makes offering them safe - GraphDBViewer's rule,
    ///kept as it is.
    ///
    public interface IEditCapableToolRunner
    {
        ///<summary>Whether the tools that change the layout are offered, and honored when one is called.</summary>
        bool AllowEdits { get; set; }
    }
}
