using System.Text.Json.Serialization;

namespace GDSViewer.Ai
{
    ///<summary>
    ///How the things this folder keeps as JSON are read and written: the saved AI models, under their names, a
    ///developer's dev-secrets.json, and the AI assistant chats and their list - see ChatStore.
    ///
    ///Generated at compile time for the reasons <see cref="Models.SavedJson"/> gives at length - a published build
    ///trims and compiles ahead of time, and a serializer handed a type at run time cannot say what it will touch. Its
    ///own context rather than two more roots on that one so that `Models/` names nothing in this folder: the layers
    ///are used downward, and these two shapes were the one place that was not true.
    ///
    ///The names are pinned by <see cref="JsonPropertyNameAttribute"/> on the types, so what is written is what the
    ///reflecting serializer wrote and a model saved by an earlier version still reads.
    ///</summary>
    [JsonSerializable(typeof(Dictionary<string, LlmConnection>))]
    [JsonSerializable(typeof(DevSecrets))]
    [JsonSerializable(typeof(SavedChat))]
    [JsonSerializable(typeof(List<ChatSummary>))]
    internal partial class AiJson : JsonSerializerContext
    {
    }
}
