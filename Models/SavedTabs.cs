using System.Text.Json;
using System.Text.Json.Serialization;

namespace GDSViewer.Models
{
    ///<summary>
    ///The files open in tabs, kept between visits: which one was on screen, and how each of the others was left.
    ///
    ///**The one on screen is not in here**, only its place: its state is the session's own, saved far more often
    ///than tabs change - every pan that comes to rest - and a copy here would be out of date by the next pan.
    ///Each of the others is a whole session, the file inside it when the file is not a bundled example nobody
    ///changed, because nothing else keeps it: a tab put away is not the session, and not necessarily a row of
    ///the history list.
    ///
    ///Written when the tabs change - one opened, put away or closed - rather than with every save.
    ///</summary>
    public class SavedTabs
    {
        ///<summary>Which tab was on screen, by its place.</summary>
        [JsonPropertyName("a")]
        public int Active { get; set; }

        [JsonPropertyName("t")]
        public List<SavedTab> Tabs { get; set; } = new List<SavedTab>();

        public static string Serialize(SavedTabs tabs)
        {
            return JsonSerializer.Serialize(tabs, SavedJson.Default.SavedTabs);
        }

        ///<summary>The tabs back, or null when there are none to trust. Never throws, for the reason <see cref="SavedSession.Deserialize"/> gives.</summary>
        public static SavedTabs? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var tabs = JsonSerializer.Deserialize(json, SavedJson.Default.SavedTabs);

                if (tabs is null || tabs.Tabs.Count == 0)
                    return null;

                return tabs;
            }
            catch
            {
                return null;
            }
        }
    }

    ///<summary>One tab: what it is called, whether the history list keeps its file, and the file as it was left.</summary>
    public class SavedTab
    {
        [JsonPropertyName("l")]
        public string Label { get; set; } = "";

        [JsonPropertyName("h")]
        public bool InHistory { get; set; }

        ///<summary>The file as it was left - null for the tab that was on screen, whose state is the session's.</summary>
        [JsonPropertyName("s")]
        public SavedSession? Session { get; set; }
    }
}
