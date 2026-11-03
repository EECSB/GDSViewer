using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using GDSViewer.Models;

namespace GDSViewer.Ai
{
    ///
    ///The AI models a developer's own machine hands a Development build, so a key is not typed into Settings every time
    ///the app is tried out - the way Treeality's GraphDBViewer does it, which is where this comes from.
    ///
    ///**A git-ignored file the dev server serves, wwwroot/dev-secrets.json, asked for by a Development build only** - see
    ///Program.cs - **and left out of every publish**: the project file keeps it out of the publish folder, which is what
    ///goes to the site. dev-secrets.example.json at the root is its template. It is under wwwroot because that is all the
    ///dev server serves; it is never anywhere a published build is.
    ///
    ///**It adds, it does not own.** A model already saved under the same name is left as it is - so one changed in
    ///Settings is not put back at the next load - and one with no key, which is what the template holds, is left out.
    ///
    public sealed class DevSecrets
    {
        ///<summary>Relative, so it is asked for from wherever the app is served.</summary>
        public const string Path = "dev-secrets.json";

        ///
        ///Has to be true. A server that has no such file can answer with the app's own page instead, which is a success
        ///too - and what says the answer is this file is that it says so.
        ///
        [JsonPropertyName("devSecrets")]
        public bool IsDevSecrets { get; set; }

        ///<summary>The models to add, by the name each is saved under - the same shape <see cref="LlmConnectionStore"/> keeps.</summary>
        [JsonPropertyName("llmConnections")]
        public Dictionary<string, LlmConnection>? LlmConnections { get; set; }

        ///<summary>The file out of its text, or null when it is not one: unreadable, or without its marker.</summary>
        public static DevSecrets? Read(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            try
            {
                var secrets = JsonSerializer.Deserialize(text, AiJson.Default.DevSecrets);

                if (secrets is null || !secrets.IsDevSecrets)
                    return null;

                return secrets;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        ///
        ///The file, from the app's own server - or null, for no path, no file, or anything else that goes wrong.
        ///
        ///**No path is no request**, which is every build but a Development one. Asked past every cache and given three
        ///seconds: a convenience that held the app up, or answered with last week's key, would be worse than none.
        ///
        public static async Task<DevSecrets?> LoadAsync(HttpClient http, string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var request = new HttpRequestMessage(HttpMethod.Get, path);

                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };

                using var response = await http.SendAsync(request, giveUp.Token);

                if (!response.IsSuccessStatusCode)
                    return null;

                return Read(await response.Content.ReadAsStringAsync(giveUp.Token));
            }
            catch (Exception)
            {
                return null;
            }
        }

        ///
        ///The file's models added to the saved ones, where nothing is saved under that name and the model has a key.
        ///Says whether it added any, which is whether the saved set needs writing back.
        ///
        public static bool AddTo(DevSecrets secrets, Dictionary<string, LlmConnection> saved)
        {
            bool added = false;

            foreach (var entry in secrets.LlmConnections ?? new Dictionary<string, LlmConnection>())
            {
                if (saved.ContainsKey(entry.Key) || entry.Value is null)
                    continue;

                if (string.IsNullOrWhiteSpace(entry.Value.ApiKey))
                    continue;

                var model = new LlmConnection(entry.Value);

                //Named by where it is kept, as a model saved under Settings is.
                if (string.IsNullOrWhiteSpace(model.Name))
                    model.Name = entry.Key;

                saved[entry.Key] = model;
                added = true;
            }

            return added;
        }
    }
}
