using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GDSViewer.Ai
{
    ///<summary>How the AI assistant's microphone turns speech into words - see <see cref="SpeechPreference"/>.</summary>
    public enum SpeechMode
    {
        ///<summary>The browser's own recognition, live as the words are spoken. Chrome and Edge, through Google's and Microsoft's servers.</summary>
        Browser,

        ///<summary>A recording sent to the AI provider - OpenAI's transcription, or Gemini - when the microphone is pressed again.</summary>
        Provider
    }

    ///
    ///**A recording written out by the AI provider** (2026-10-31, asked for, beside the browser's own recognition): the
    ///microphone's WAV - see speechWav.js - sent to OpenAI's transcription endpoint, or to Gemini, which takes audio in an
    ///ordinary request. Anthropic's API takes no audio, so a Claude model picked in the chat leaves it to the first saved
    ///OpenAI or Gemini model (<see cref="Pick"/>).
    ///
    ///**Told the words a layout conversation is full of** - the open file's layer names, microns, the editor's own terms -
    ///which is most of what the provider is better at than the browser: "met one" written as met1, "poly" not "Polly".
    ///OpenAI takes them as its prompt, Gemini in its instruction.
    ///
    public static class SpeechTranscriber
    {
        ///<summary>OpenAI's transcription model: more accurate than whisper-1 on technical words, at about the same price.</summary>
        public const string OpenAiModel = "gpt-4o-transcribe";

        ///<summary>Gemini's own API, which takes a WAV inline - its OpenAI-compatible endpoint has no transcription.</summary>
        public const string GeminiApi = "https://generativelanguage.googleapis.com/v1beta/models/";

        ///<summary>The model Gemini is asked with when the saved one names none.</summary>
        public const string GeminiDefaultModel = "gemini-2.5-flash";

        ///<summary>The words a layout conversation is full of, said to the provider so it writes them as the editor does.</summary>
        public const string LayoutWords = "GDSII, OASIS, layout, cell, layer, polygon, rectangle, path, via, met1, met2, met3, met4, met5, li1, poly, diff, nwell, licon, mcon, sky130, PDK, DRC, µm, nm";

        ///<summary>Whether a saved model's provider can write out a recording: OpenAI's own, and Gemini.</summary>
        public static bool CanTranscribe(LlmConnection connection)
        {
            if (connection.ProviderType == LlmConnection.Gemini)
                return true;

            //OpenAI's own address only: a server of one's own that speaks chat completions may have no transcription.
            return connection.ProviderType == LlmConnection.OpenAI && string.IsNullOrWhiteSpace(connection.BaseUrl);
        }

        ///<summary>The model a recording goes to: the one picked in the chat when it can, or else the first saved one that can, or null.</summary>
        public static LlmConnection? Pick(LlmConnection? picked, IEnumerable<LlmConnection> saved)
        {
            if (picked is not null && CanTranscribe(picked))
                return picked;

            return saved.FirstOrDefault(CanTranscribe);
        }

        ///<summary>The words to tell the provider: the editor's own and the open file's layer names, once each.</summary>
        public static string Vocabulary(IEnumerable<string> layerNames)
        {
            var words = new List<string>(LayoutWords.Split(", "));

            foreach (string name in layerNames)
            {
                string trimmed = name.Trim();

                if (trimmed.Length > 0 && !words.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    words.Add(trimmed);
            }

            return string.Join(", ", words.Take(80));
        }

        public static HttpRequestMessage OpenAiRequest(LlmConnection connection, byte[] wav, string vocabulary)
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(wav);

            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            form.Add(file, "file", "speech.wav");
            form.Add(new StringContent(OpenAiModel), "model");
            form.Add(new StringContent($"A question to a chip layout editor's assistant. Words that may come up: {vocabulary}."), "prompt");
            form.Add(new StringContent("json"), "response_format");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{OpenAiProvider.DefaultBaseUrl}/audio/transcriptions") { Content = form };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey ?? "");

            return request;
        }

        public static HttpRequestMessage GeminiRequest(LlmConnection connection, byte[] wav, string vocabulary)
        {
            string model = connection.Model ?? "";

            if (string.IsNullOrWhiteSpace(model))
                model = GeminiDefaultModel;

            var body = new JsonObject
            {
                ["contents"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["parts"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["text"] = "Write out exactly what is said in this recording - a question to a chip layout editor's assistant. "
                                    + "Reply with the words alone: no quotes, no notes, nothing about the recording. Write numbers as digits and units "
                                    + $"as the editor writes them (µm, nm). Words that may come up: {vocabulary}."
                            },
                            new JsonObject
                            {
                                ["inline_data"] = new JsonObject { ["mime_type"] = "audio/wav", ["data"] = Convert.ToBase64String(wav) }
                            }
                        }
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{GeminiApi}{Uri.EscapeDataString(model)}:generateContent")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };

            request.Headers.Add("x-goog-api-key", connection.ApiKey ?? "");

            return request;
        }

        ///<summary>The words in a provider's answer, or null when it holds none.</summary>
        public static string? TextIn(string providerType, string json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);

                var root = document.RootElement;

                if (providerType == LlmConnection.Gemini)
                    return geminiText(root);

                if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    return text.GetString()?.Trim();

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? geminiText(JsonElement root)
        {
            if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                return null;

            if (!candidates[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
                return null;

            var said = new StringBuilder();

            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    said.Append(text.GetString());
            }

            return said.ToString().Trim();
        }

        ///<summary>Sends a recording to the model's provider, and answers the words it heard or why there are none.</summary>
        public static async Task<(string? Text, string? Problem)> TranscribeAsync(HttpClient http, LlmConnection connection, byte[] wav, string vocabulary, CancellationToken ct)
        {
            if (!CanTranscribe(connection))
                return (null, $"{connection.Name} cannot write out speech: that takes an OpenAI or Gemini model.");

            HttpRequestMessage request;

            if (connection.ProviderType == LlmConnection.Gemini)
                request = GeminiRequest(connection, wav, vocabulary);
            else
                request = OpenAiRequest(connection, wav, vocabulary);

            string label = connection.ProviderType;
            var (json, problem) = await LlmProviderFactory.SendAsync(http, request, label, ct);

            if (problem is not null)
                return (null, problem);

            if (json is null || TextIn(connection.ProviderType, json) is not string text)
                return (null, $"{label} answered, with no words in it.");

            return (text, null);
        }
    }
}
