using System.Net;
using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///The AI assistant's microphone written out by the AI provider (2026-10-31): which saved model a recording goes to, the words it is
///told, what each provider is sent and how its answer is read - and the setting that chooses it. The recording itself,
///the browser's recognizer and the button are the ask-ai spec's; the WAV is speechWav.test.js's.
///
public class AiSpeechTests
{
    private static readonly byte[] Wav = { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4 };

    private static LlmConnection Model(string name, string type, string? baseUrl = null)
    {
        return new LlmConnection { Name = name, ProviderType = type, ApiKey = "test-key", Model = "gemini-test", BaseUrl = baseUrl };
    }

    ///<summary>The model picked when it can write out speech; a Claude model leaves it to the first saved one that can; a server of one's own cannot.</summary>
    [Fact]
    public void A_recording_goes_to_the_model_picked_or_else_the_first_saved_that_takes_audio()
    {
        var claude = Model("Claude", LlmConnection.Anthropic);
        var local = Model("Local", LlmConnection.Custom, "http://localhost:11434/v1");
        var ownOpenAi = Model("Proxy", LlmConnection.OpenAI, "https://proxy.example/v1");
        var gemini = Model("Gemini", LlmConnection.Gemini);
        var openAi = Model("OpenAI", LlmConnection.OpenAI);

        Assert.Same(gemini, SpeechTranscriber.Pick(gemini, new[] { claude, openAi }));
        Assert.Same(openAi, SpeechTranscriber.Pick(claude, new[] { claude, local, ownOpenAi, openAi, gemini }));
        Assert.Null(SpeechTranscriber.Pick(claude, new[] { claude, local, ownOpenAi }));
        Assert.Null(SpeechTranscriber.Pick(null, Array.Empty<LlmConnection>()));
    }

    [Fact]
    public void The_words_told_are_the_editors_and_the_files_layer_names_once_each()
    {
        string words = SpeechTranscriber.Vocabulary(new[] { "met1", "mylayer", " ", "MyLayer", "nwell" });

        Assert.Contains("met1", words);
        Assert.Contains("µm", words);
        Assert.Contains("mylayer", words);
        Assert.Equal(1, words.Split(", ").Count(word => string.Equals(word, "mylayer", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, words.Split(", ").Count(word => word == "nwell"));
    }

    [Fact]
    public async Task OpenAi_is_sent_the_wav_as_a_file_with_its_transcription_model_and_the_words()
    {
        using var request = SpeechTranscriber.OpenAiRequest(Model("OpenAI", LlmConnection.OpenAI), Wav, "met1, poly");

        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", request.RequestUri!.ToString());
        Assert.Equal("Bearer test-key", request.Headers.Authorization!.ToString());

        var form = Assert.IsType<MultipartFormDataContent>(request.Content);
        var parts = form.ToList();
        var file = parts.Single(part => part.Headers.ContentDisposition!.Name == "file");

        Assert.Equal("audio/wav", file.Headers.ContentType!.MediaType);
        Assert.Equal(Wav, await file.ReadAsByteArrayAsync());
        Assert.Equal(SpeechTranscriber.OpenAiModel, await parts.Single(part => part.Headers.ContentDisposition!.Name == "model").ReadAsStringAsync());
        Assert.Contains("met1, poly", await parts.Single(part => part.Headers.ContentDisposition!.Name == "prompt").ReadAsStringAsync());
    }

    ///<summary>Gemini's own API, with the WAV inline and an instruction that names the words; the key in its header, not the address.</summary>
    [Fact]
    public async Task Gemini_is_sent_the_wav_inline_with_an_instruction_to_write_it_out()
    {
        var standIn = new AiStandIn("""{"candidates":[{"content":{"parts":[{"text":" draw a met1 square "}]}}]}""");

        var (text, problem) = await SpeechTranscriber.TranscribeAsync(standIn.Client(), Model("Gemini", LlmConnection.Gemini), Wav, "met1, poly", CancellationToken.None);

        Assert.Null(problem);
        Assert.Equal("draw a met1 square", text);

        var sent = Assert.Single(standIn.Requests);

        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-test:generateContent", sent.Uri);
        Assert.Equal("test-key", sent.Headers["x-goog-api-key"]);
        Assert.DoesNotContain("key=", sent.Uri);

        var parts = sent.Body["contents"]![0]!["parts"]!.AsArray();

        Assert.Contains("met1, poly", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal("audio/wav", parts[1]!["inline_data"]!["mime_type"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(Wav), parts[1]!["inline_data"]!["data"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_refusal_is_said_in_the_providers_words_and_a_claude_model_is_told_why_not()
    {
        var standIn = new AiStandIn((_, _) => AiStandIn.Json("""{"error":{"message":"API key not valid"}}""", HttpStatusCode.BadRequest));

        var (text, problem) = await SpeechTranscriber.TranscribeAsync(standIn.Client(), Model("Gemini", LlmConnection.Gemini), Wav, "", CancellationToken.None);

        Assert.Null(text);
        Assert.Contains("API key not valid", problem);

        var (_, notAudio) = await SpeechTranscriber.TranscribeAsync(standIn.Client(), Model("Claude", LlmConnection.Anthropic), Wav, "", CancellationToken.None);

        Assert.Contains("takes an OpenAI or Gemini model", notAudio);
        Assert.Single(standIn.Requests);
    }

    [Fact]
    public void Each_providers_answer_is_read_for_its_words_and_one_with_none_is_null()
    {
        Assert.Equal("hello", SpeechTranscriber.TextIn(LlmConnection.OpenAI, """{"text":" hello "}"""));
        Assert.Equal("one two", SpeechTranscriber.TextIn(LlmConnection.Gemini, """{"candidates":[{"content":{"parts":[{"text":"one "},{"text":"two"}]}}]}"""));
        Assert.Null(SpeechTranscriber.TextIn(LlmConnection.Gemini, """{"candidates":[]}"""));
        Assert.Null(SpeechTranscriber.TextIn(LlmConnection.OpenAI, "not json"));
    }

    [Fact]
    public void The_setting_is_the_browsers_unless_the_provider_was_chosen()
    {
        Assert.Equal(SpeechMode.Browser, SpeechPreference.Read(null));
        Assert.Equal(SpeechMode.Browser, SpeechPreference.Read("Browser"));
        Assert.Equal(SpeechMode.Browser, SpeechPreference.Read("something else"));
        Assert.Equal(SpeechMode.Provider, SpeechPreference.Read("Provider"));
    }
}
