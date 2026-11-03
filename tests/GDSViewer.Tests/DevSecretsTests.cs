using System.Net;
using System.Text;
using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///A developer's dev-secrets.json: the AI models a Development build adds under Settings, so a key is not typed in every
///time the app is tried out. See <see cref="DevSecrets"/>.
///
///**What would go wrong quietly is covered here**: a page that is not the file read as one, a saved model overwritten by
///the file's, a template's blank key saved as a model - and, above all, the file published. It holds real keys and
///lives under wwwroot, which is the site, so the line in the project file that keeps it out of every publish is held
///in place by a test, and so is the one in .gitignore.
///
public class DevSecretsTests
{
    private const string OneModel = """
        {
          "devSecrets": true,
          "llmConnections": {
            "Gemini": { "Name": "Gemini", "ProviderType": "Gemini", "Model": "gemini-3.8-flash", "ApiKey": "not-a-real-key", "MaxTokens": 8192 }
          }
        }
        """;

    #region Reading *******************************************************************

    [Fact]
    public void The_file_is_read_with_its_models()
    {
        var secrets = DevSecrets.Read(OneModel);

        Assert.NotNull(secrets);
        Assert.True(secrets!.IsDevSecrets);
        Assert.Equal("gemini-3.8-flash", secrets.LlmConnections!["Gemini"].Model);
        Assert.Equal(LlmConnection.Gemini, secrets.LlmConnections["Gemini"].ProviderType);
    }

    ///
    ///A server with no such file can answer with the app's own page, which is a success too. The marker is what says the
    ///answer is the file - and without it, or with it false, nothing is taken from it.
    ///
    [Theory]
    [InlineData("<!DOCTYPE html><html><body>GDS Viewer</body></html>")]
    [InlineData("""{ "llmConnections": { "Gemini": { "ApiKey": "x" } } }""")]
    [InlineData("""{ "devSecrets": false, "llmConnections": { "Gemini": { "ApiKey": "x" } } }""")]
    [InlineData("{ not json")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_but_the_file_is_no_file(string? text)
    {
        Assert.Null(DevSecrets.Read(text));
    }

    #endregion ************************************************************************



    #region Adding ********************************************************************

    [Fact]
    public void Its_models_are_added_to_the_saved_ones()
    {
        var saved = new Dictionary<string, LlmConnection>
        {
            ["Mine"] = new LlmConnection { Name = "Mine", ProviderType = LlmConnection.Anthropic, ApiKey = "a", Model = "claude-opus-5-5" }
        };

        Assert.True(DevSecrets.AddTo(DevSecrets.Read(OneModel)!, saved));

        Assert.Equal(2, saved.Count);
        Assert.Equal("not-a-real-key", saved["Gemini"].ApiKey);
        Assert.Equal("claude-opus-5-5", saved["Mine"].Model);
    }

    ///<summary>A model already saved under the name is left as it is: one changed in Settings is not put back at the next load.</summary>
    [Fact]
    public void A_saved_model_of_the_same_name_is_left_alone()
    {
        var saved = new Dictionary<string, LlmConnection>
        {
            ["Gemini"] = new LlmConnection { Name = "Gemini", ProviderType = LlmConnection.Gemini, ApiKey = "changed", Model = "gemini-other" }
        };

        Assert.False(DevSecrets.AddTo(DevSecrets.Read(OneModel)!, saved));

        Assert.Single(saved);
        Assert.Equal("changed", saved["Gemini"].ApiKey);
        Assert.Equal("gemini-other", saved["Gemini"].Model);
    }

    ///<summary>The template's entries have no key, and a model with no key is not one anybody can use.</summary>
    [Fact]
    public void A_model_with_no_key_is_left_out()
    {
        var secrets = DevSecrets.Read("""
            { "devSecrets": true, "llmConnections": { "Blank": { "Name": "Blank", "ProviderType": "Gemini", "ApiKey": "  " }, "None": { "Name": "None" } } }
            """)!;

        var saved = new Dictionary<string, LlmConnection>();

        Assert.False(DevSecrets.AddTo(secrets, saved));
        Assert.Empty(saved);
    }

    [Fact]
    public void A_model_with_no_name_is_named_by_where_it_is_kept()
    {
        var secrets = DevSecrets.Read("""
            { "devSecrets": true, "llmConnections": { "Local": { "ProviderType": "Custom", "BaseUrl": "http://localhost:11434/v1", "ApiKey": "k" } } }
            """)!;

        var saved = new Dictionary<string, LlmConnection>();

        Assert.True(DevSecrets.AddTo(secrets, saved));
        Assert.Equal("Local", saved["Local"].Name);
    }

    #endregion ************************************************************************



    #region Asking ********************************************************************

    ///<summary>Answers every request with the same thing, and counts them.</summary>
    private sealed class Answering : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> answer;

        public Answering(Func<HttpResponseMessage> answer)
        {
            this.answer = answer;
        }

        public int Asked { get; private set; }

        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Asked++;
            Last = request;

            return Task.FromResult(answer());
        }
    }

    private static HttpResponseMessage Text(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) };
    }

    ///<summary>Every build but a Development one has no path, and asks for nothing at all.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task No_path_is_no_request(string? path)
    {
        var handler = new Answering(() => Text(HttpStatusCode.OK, OneModel));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        Assert.Null(await DevSecrets.LoadAsync(http, path));
        Assert.Equal(0, handler.Asked);
    }

    [Fact]
    public async Task The_file_is_asked_for_past_every_cache()
    {
        var handler = new Answering(() => Text(HttpStatusCode.OK, OneModel));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };

        var secrets = await DevSecrets.LoadAsync(http, DevSecrets.Path);

        Assert.NotNull(secrets);
        Assert.Equal(1, handler.Asked);
        Assert.Equal("http://localhost/dev-secrets.json", handler.Last!.RequestUri!.ToString());
        Assert.True(handler.Last.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task No_file_and_a_page_instead_of_one_are_both_nothing()
    {
        var missing = new HttpClient(new Answering(() => Text(HttpStatusCode.NotFound, ""))) { BaseAddress = new Uri("http://localhost/") };
        var page = new HttpClient(new Answering(() => Text(HttpStatusCode.OK, "<!DOCTYPE html><html></html>"))) { BaseAddress = new Uri("http://localhost/") };
        var failing = new HttpClient(new Answering(() => throw new HttpRequestException("refused"))) { BaseAddress = new Uri("http://localhost/") };

        Assert.Null(await DevSecrets.LoadAsync(missing, DevSecrets.Path));
        Assert.Null(await DevSecrets.LoadAsync(page, DevSecrets.Path));
        Assert.Null(await DevSecrets.LoadAsync(failing, DevSecrets.Path));
    }

    #endregion ************************************************************************



    #region Never shipped *************************************************************

    ///
    ///**The one line between a developer's keys and the public site.** Everything under wwwroot is published, and this
    ///file is under wwwroot because that is all the dev server serves. Taken out of the project file, the next publish
    ///would carry it to the site.
    ///
    [Fact]
    public void The_project_file_keeps_it_out_of_every_publish()
    {
        string project = File.ReadAllText(Path.Combine(GdsTestData.RepositoryRoot, "GDSViewer.csproj"));

        Assert.Contains("<Content Update=\"wwwroot\\dev-secrets.json\" CopyToPublishDirectory=\"Never\" />", project);
    }

    [Fact]
    public void Git_ignores_it()
    {
        var lines = File.ReadAllLines(Path.Combine(GdsTestData.RepositoryRoot, ".gitignore")).Select(line => line.Trim());

        Assert.Contains("dev-secrets.json", lines);
    }

    ///
    ///And the same for a developer's MCP registration, .mcp.json: git-ignored, and a JSON file at the root the SDK
    ///takes for content, with absolute paths of the developer's machine in it. Found evaluating as publishable on
    ///2026-11-03, with nothing holding the line that keeps it out - so this does.
    ///
    [Fact]
    public void The_project_file_keeps_the_mcp_registration_out_of_every_publish_too()
    {
        string project = File.ReadAllText(Path.Combine(GdsTestData.RepositoryRoot, "GDSViewer.csproj"));
        var ignored = File.ReadAllLines(Path.Combine(GdsTestData.RepositoryRoot, ".gitignore")).Select(line => line.Trim());

        Assert.Contains("<Content Update=\".mcp.json\" CopyToPublishDirectory=\"Never\" />", project);
        Assert.Contains(".mcp.json", ignored);
    }

    ///<summary>The committed template is a file the app reads, and carries no key - so copying it is all setting up takes, and committing it leaks nothing.</summary>
    [Fact]
    public void The_template_is_the_file_with_every_key_left_blank()
    {
        string template = File.ReadAllText(Path.Combine(GdsTestData.RepositoryRoot, "dev-secrets.example.json"));
        var secrets = DevSecrets.Read(template);

        Assert.NotNull(secrets);
        Assert.NotEmpty(secrets!.LlmConnections!);
        Assert.All(secrets.LlmConnections!.Values, model => Assert.True(string.IsNullOrEmpty(model.ApiKey)));
    }

    #endregion ************************************************************************
}
