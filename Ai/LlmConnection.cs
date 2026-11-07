using System.Globalization;
using System.Text.Json.Serialization;

namespace GDSViewer.Ai
{
    ///
    ///A saved AI model: which provider, where it is, the key, and which model to ask for.
    ///
    ///**Bring your own key.** The person supplies all of it under Settings, and it is kept in this browser by
    ///<see cref="LlmConnectionStore"/> - the app has no key of its own and no server to keep one on. The same shape
    ///as GraphDBViewer's, names and all, so the two describe a model the same way.
    ///
    public sealed class LlmConnection
    {
        public const string Anthropic = "Anthropic";
        public const string OpenAI = "OpenAI";
        public const string Gemini = "Gemini";

        ///<summary>Any other OpenAI-compatible server: Ollama, LM Studio, vLLM, a company's own.</summary>
        public const string Custom = "Custom";

        ///
        ///The output cap Anthropic is sent, which it requires; the OpenAI-compatible adapter sends none.
        ///
        ///Generous on purpose: a ceiling, not an allocation. What is billed is what the model writes, and a cap set
        ///too low cuts an answer off mid-sentence rather than costing less. A model that calls tools spends this on
        ///its calls too, and a polygon of a hundred corners is a long call.
        ///
        public const int DefaultMaxTokens = 8192;

        public LlmConnection()
        {
        }

        public LlmConnection(LlmConnection copy)
        {
            Name = copy.Name;
            ProviderType = copy.ProviderType;
            BaseUrl = copy.BaseUrl;
            ApiKey = copy.ApiKey;
            Model = copy.Model;
            MaxTokens = copy.MaxTokens;
            Effort = copy.Effort;
            Fast = copy.Fast;
            InputPrice = copy.InputPrice;
            CachedPrice = copy.CachedPrice;
            OutputPrice = copy.OutputPrice;
            PdfAsText = copy.PdfAsText;
        }

        ///
        ///Whether this holds the same saved settings as <paramref name="other"/> - everything but the name, and but the
        ///effort and fast mode, which are chosen per request and never saved. What the models' Save asks to say whether it
        ///has anything to do (2026-11-07, from GraphDBViewer). A text left blank is the same as one never set.
        ///
        public bool SameSettings(LlmConnection other)
        {
            return sameText(ProviderType, other.ProviderType)
                && sameText(BaseUrl, other.BaseUrl)
                && sameText(ApiKey, other.ApiKey)
                && sameText(Model, other.Model)
                && MaxTokens == other.MaxTokens
                && InputPrice == other.InputPrice
                && CachedPrice == other.CachedPrice
                && OutputPrice == other.OutputPrice
                && PdfAsText == other.PdfAsText;
        }

        private static bool sameText(string? left, string? right)
        {
            return string.Equals(left ?? "", right ?? "", StringComparison.Ordinal);
        }

        [JsonPropertyName("Name")]
        public string Name { get; set; } = "";

        ///<summary>Which family: <see cref="Anthropic"/>, <see cref="OpenAI"/>, <see cref="Gemini"/> or <see cref="Custom"/>. The last three share the OpenAI-compatible adapter.</summary>
        [JsonPropertyName("ProviderType")]
        public string ProviderType { get; set; } = Anthropic;

        ///<summary>Where an OpenAI-compatible server is, such as http://localhost:11434/v1 for Ollama. Not used for Anthropic, whose address is fixed.</summary>
        [JsonPropertyName("BaseUrl")]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("ApiKey")]
        public string? ApiKey { get; set; }

        ///<summary>The model's id, such as claude-opus-5-5 or gemini-3.8-flash.</summary>
        [JsonPropertyName("Model")]
        public string? Model { get; set; }

        [JsonPropertyName("MaxTokens")]
        public int MaxTokens { get; set; } = DefaultMaxTokens;

        ///
        ///How hard a reasoning model should think - one of <see cref="EffortLevels"/> - or empty to say nothing and leave
        ///it to the model.
        ///
        ///**Never saved.** It is chosen in the chat, per request, and set on a copy of the connection there - set on
        ///the saved one, it would be written out against the model for every conversation after.
        ///
        [JsonIgnore]
        public string Effort { get; set; } = "";

        ///
        ///Whether Anthropic runs the request in fast mode: the same model writing up to two and a half times as fast,
        ///at twice the price - see <see cref="SupportsFast"/>. Chosen in the chat and never saved, as
        ///<see cref="Effort"/> is not.
        ///
        [JsonIgnore]
        public bool Fast { get; set; }

        ///
        ///What a million input tokens cost on this model, in US dollars, when the person has said - null to take the
        ///published rate, or to show no cost where there is none. With <see cref="CachedPrice"/> and
        ///<see cref="OutputPrice"/>, the rates <see cref="AiPricing.For"/> prices a run at.
        ///
        [JsonPropertyName("InputPrice")]
        public decimal? InputPrice { get; set; }

        ///<summary>A million input tokens read back from the provider's cache, in US dollars, or null for the published rate.</summary>
        [JsonPropertyName("CachedPrice")]
        public decimal? CachedPrice { get; set; }

        ///<summary>A million output tokens, thinking included, in US dollars, or null for the published rate.</summary>
        [JsonPropertyName("OutputPrice")]
        public decimal? OutputPrice { get; set; }

        ///
        ///Whether a PDF goes to this model as the words read out of it in the page (<see cref="PdfText"/>) rather than as
        ///the file: for a model that cannot read one - Gemini, or a server of one's own - or to spend less than a page's
        ///picture costs (2026-11-07, from GraphDBViewer). Off until ticked; a scan, which has no words to read, needs it off.
        ///
        [JsonPropertyName("PdfAsText")]
        public bool PdfAsText { get; set; }

        ///
        ///Whether this connection's requests go over TLS: Anthropic's always, since its address is fixed; the others'
        ///when the address they go to is https, their own default included where the base URL is left blank.
        ///
        ///What the model form asks so that a key saved for an encrypted address is not sent to one that is not - the
        ///rule GraphDBViewer's form keeps since 2026-10-03, and see <see cref="SavedSecretRule"/>.
        ///
        public static bool Encrypts(LlmConnection? connection)
        {
            if (connection is null)
                return false;

            if (connection.ProviderType == Anthropic)
                return true;

            string? address = connection.BaseUrl;

            if (string.IsNullOrWhiteSpace(address))
                address = OpenAiProvider.DefaultBaseUrlFor(connection.ProviderType);

            //A server of one's own with no address is sent nothing at all, so it encrypts nothing either.
            if (string.IsNullOrWhiteSpace(address))
                return false;

            return address.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        ///<summary>Whether an effort level means anything here - whether <see cref="EffortLevels"/> offers any.</summary>
        public static bool SupportsEffort(LlmConnection? connection)
        {
            return EffortLevels(connection).Count > 0;
        }

        ///
        ///The effort levels a model takes, lowest first, which depend on the provider and on the model - none where it
        ///takes no level at all.
        ///
        ///OpenAI's reasoning models read reasoning_effort, and its chat models treat it as an error rather than ignoring
        ///it. Google's OpenAI-compatible endpoint takes the same field on the models that think, which is 2.5 and up.
        ///Both go to high.
        ///
        ///Anthropic reads output_config.effort - from Opus 4.5, which took three levels; Opus and Sonnet 4.6 added max,
        ///and Opus 4.7 xhigh, which every model since takes. It was left out here until 2026-11-01, written when Claude's
        ///only control was a budget of thinking tokens. Sonnet 4.5 and Haiku 4.5 refuse the field rather than ignoring
        ///it, so they are offered none. A server behind Other may be anything at all, so it is not offered one either.
        ///
        public static IReadOnlyList<string> EffortLevels(LlmConnection? connection)
        {
            string? model = connection?.Model;

            if (connection is null || string.IsNullOrWhiteSpace(model))
                return Array.Empty<string>();

            model = model.Trim();

            if (connection.ProviderType == OpenAI)
            {
                if (model.StartsWith("o1", StringComparison.OrdinalIgnoreCase)
                    || model.StartsWith("o3", StringComparison.OrdinalIgnoreCase)
                    || model.StartsWith("o4", StringComparison.OrdinalIgnoreCase)
                    || model.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase))
                    return ThreeLevels;
            }
            else if (connection.ProviderType == Gemini)
            {
                if (isThinkingGemini(model))
                    return ThreeLevels;
            }
            else if (connection.ProviderType == Anthropic)
            {
                return claudeLevels(model);
            }

            return Array.Empty<string>();
        }

        ///
        ///Whether a model runs in fast mode, which only Anthropic offers and only on Opus 4.8 and Opus 5 on - Opus 4.7's
        ///was taken away, and a request asking it of any other model is refused rather than run slowly. By version, so
        ///an Opus released later is offered it too; the provider's documents, 2026-11-01.
        ///
        public static bool SupportsFast(LlmConnection? connection)
        {
            if (connection is null || connection.ProviderType != Anthropic || string.IsNullOrWhiteSpace(connection.Model))
                return false;

            var (family, version) = claudeVersion(connection.Model.Trim());

            if (family != "opus")
                return false;

            return version >= 5 || version == 4.8;
        }

        private static readonly string[] ThreeLevels = { "low", "medium", "high" };

        private static readonly string[] ClaudeFourFive = { "low", "medium", "high" };

        private static readonly string[] ClaudeFourSix = { "low", "medium", "high", "max" };

        private static readonly string[] ClaudeAll = { "low", "medium", "high", "xhigh", "max" };

        private static IReadOnlyList<string> claudeLevels(string model)
        {
            var (family, version) = claudeVersion(model);

            if (family == "opus")
            {
                if (version >= 4.7)
                    return ClaudeAll;

                if (version >= 4.6)
                    return ClaudeFourSix;

                if (version >= 4.5)
                    return ClaudeFourFive;
            }
            else if (family == "sonnet")
            {
                if (version >= 5)
                    return ClaudeAll;

                if (version >= 4.6)
                    return ClaudeFourSix;
            }
            else if (family == "haiku" || family == "fable" || family == "mythos")
            {
                if (version >= 5)
                    return ClaudeAll;
            }

            return Array.Empty<string>();
        }

        ///
        ///A Claude model id read as its family and version: claude-opus-5-5 is opus 5.5, claude-sonnet-5 sonnet 5, and
        ///claude-opus-4-5-20251101 opus 4.5 - a part longer than two digits is a date, not a minor version. An id in
        ///no such shape is no family and version -1.
        ///
        private static (string Family, double Version) claudeVersion(string model)
        {
            string[] parts = model.ToLowerInvariant().Split('-');

            if (parts.Length < 3 || parts[0] != "claude" || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int major))
                return ("", -1);

            double version = major;

            if (parts.Length > 3 && parts[3].Length <= 2 && int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
                version = major + (minor / 10.0);

            return (parts[1], version);
        }

        ///
        ///Whether a Gemini model id names a thinking model, read as a version number rather than from a list of
        ///names - gemini-2.5-flash is 2.5 and gemini-3-pro is 3 - so a family released after this was written is
        ///offered an effort level instead of silently missing one.
        ///
        private static bool isThinkingGemini(string model)
        {
            //Thinking arrived with 2.5, and everything after it thinks too.
            return versionAfter(model, "gemini-") >= 2.5;
        }

        ///
        ///What a model does when it is sent no effort level, as its provider documents it - for the effort list's first
        ///row, which said only "default" and so told nobody how hard the model would think. Asked 2026-10-18.
        ///
        ///By family, read from the version number like <see cref="SupportsEffort"/>, so a model released later lands in
        ///its family: OpenAI's o-series and GPT-5 reason at medium unless told otherwise, and GPT-5.1 and after at none;
        ///Gemini 3 and after think at high, 2.5 Flash-Lite not at all, and the rest of 2.5 as much as a question needs.
        ///Empty where no effort is offered, which is also where the list is not shown.
        ///
        ///**The providers' documents, not a measurement**: nothing in an answer says what level it was given, so a
        ///family that changes its default is only noticed here by somebody reading the change.
        ///
        public static string DefaultEffort(LlmConnection? connection)
        {
            if (!SupportsEffort(connection))
                return "";

            string model = connection!.Model!.Trim();

            //Opus 5.5 and Haiku 5.5 think at medium when told nothing; every other Claude that takes a level, at high.
            if (connection.ProviderType == Anthropic)
            {
                var (family, version) = claudeVersion(model);

                if ((family == "opus" || family == "haiku") && version >= 5.5)
                    return "medium";

                return "high";
            }

            if (connection.ProviderType == Gemini)
            {
                if (versionAfter(model, "gemini-") >= 3)
                    return "high";

                if (model.Contains("flash-lite", StringComparison.OrdinalIgnoreCase))
                    return "none";

                return "as needed";
            }

            if (versionAfter(model, "gpt-") > 5)
                return "none";

            return "medium";
        }

        ///
        ///The version at the front of a model id, after its family's prefix - 2.5 for gemini-2.5-flash, 5 for
        ///gpt-5-mini, 5.1 for gpt-5.1-codex - or -1 for an id that does not start with the prefix and a number.
        ///
        private static double versionAfter(string model, string prefix)
        {
            if (!model.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return -1;

            string rest = model.Substring(prefix.Length);
            int end = 0;

            while (end < rest.Length && (char.IsDigit(rest[end]) || rest[end] == '.'))
                end++;

            if (!double.TryParse(rest.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out double version))
                return -1;

            return version;
        }
    }
}
