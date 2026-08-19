using System.Text.Json.Serialization;

using GdsII;

namespace GDSViewer.Models
{
    ///<summary>
    ///How the three things this app writes as JSON are read and written: the session, the history index,
    ///and the manifest of bundled examples.
    ///
    ///**Generated at compile time rather than worked out by reflection at run time.** Publishing trims,
    ///and now compiles ahead of time as well; both ask what the app can possibly need before it runs.
    ///`JsonSerializer.Serialize(thing)` cannot answer, because what it touches is decided by the type it is
    ///handed at the call - so the trimmer warns (IL2026), the AOT compiler warns (IL3050), and neither can
    ///do anything but hope. The failure that warning describes is the bad kind: it appears only in a
    ///published build, only for the property that got trimmed, and it looks like a session that quietly
    ///forgot something rather than like an error.
    ///
    ///It had not actually broken - the shapes here are plain enough that the trimmer kept everything, and
    ///Blazor's AOT falls back to an interpreter for anything it could not compile. But "it happens to
    ///survive" is not a property anyone can rely on while adding a field, and this makes it structural: the
    ///generator walks the graph from the three roots below and writes the reader and writer out in full.
    ///
    ///The output is unchanged, which matters because sessions written by older versions are already in
    ///people's browsers - every name is pinned by <see cref="JsonPropertyNameAttribute"/> and the defaults
    ///here are the ones the reflecting serializer used.
    ///</summary>
    [JsonSerializable(typeof(SavedSession))]
    [JsonSerializable(typeof(HistoryIndex))]
    [JsonSerializable(typeof(List<string>))]
    internal partial class SavedJson : JsonSerializerContext
    {
    }
}
