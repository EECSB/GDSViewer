using GDSViewer;
using GDSViewer.Shared;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using System.Diagnostics.Metrics;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

//Where the open file, the layer names and the view settings are kept between visits. Scoped rather than
//built by hand in the page, so the JS runtime comes from the container and a test can hand it another one.
builder.Services.AddScoped(sp => new GDSViewer.Models.AppStorage(sp.GetRequiredService<IJSRuntime>()));

//The files that have been opened, over the same store. Its own service rather than part of the page,
//because it holds the index in memory between saves and a page that rebuilt it would re-read it each time.
builder.Services.AddScoped(sp => new GDSViewer.Models.HistoryStore(sp.GetRequiredService<GDSViewer.Models.AppStorage>()));

//The pictures layers' 3D textures are drawn with, over the same store, by the names layers give them. Shared by the
//layer settings that keep them and the 3D view that draws them. See TextureStore.
builder.Services.AddScoped(sp => new GDSViewer.Models.TextureStore(sp.GetRequiredService<GDSViewer.Models.AppStorage>()));

//The Examples pictures kept between visits - the big designs', which are slow to draw. See PreviewStore.
builder.Services.AddScoped(sp => new GDSViewer.Models.PreviewStore(sp.GetRequiredService<GDSViewer.Models.AppStorage>()));

//The PDKs - the built-in sky130 and any made in the PDK manager - and which one is in use. See Models/Pdk.cs.
builder.Services.AddScoped(sp => new GDSViewer.Models.PdkStore(sp.GetRequiredService<GDSViewer.Models.AppStorage>()));

//
//A Development build - dotnet run - adds the AI models a developer's own wwwroot/dev-secrets.json holds, so a key is not
//typed in every time the app is tried out. No other build asks for the file, and no publish carries it. See DevSecrets.
//
string? devSecrets = null;

if (builder.HostEnvironment.IsDevelopment())
    devSecrets = GDSViewer.Ai.DevSecrets.Path;

//
//The AI: the models saved under Settings, how each is talked to, and the AI assistant chats - one instance, so the settings
//window and the chat window hear about each other's changes. This app's own keeps the models and the
//chats in this browser, over the same store. **Built into another app, GDS Viewer's own registration there registers
//another** - a Treeality plugin's module, from that app's models and the signed-in account's storage; this file does
//not run there at all. See IAiService and docs/TREEALITY-PLUGIN.md.
//
builder.Services.AddScoped<GDSViewer.Ai.IAiService>(sp =>
{
    var storage = sp.GetRequiredService<GDSViewer.Models.AppStorage>();
    var models = new GDSViewer.Ai.LlmConnectionStore(storage) { DevSecretsPath = devSecrets };

    return new GDSViewer.Ai.BrowserAiService(models, new GDSViewer.Ai.ChatStore(storage));
});

//How the AI assistant's microphone turns speech into words, chosen under Settings, AI settings - one instance, so the settings
//window and the chat hear each other's change. See SpeechPreference.
builder.Services.AddScoped(sp => new GDSViewer.Ai.SpeechPreference(sp.GetRequiredService<GDSViewer.Models.AppStorage>()));

//How many rounds of tool calls the AI assistant makes before it pauses to ask, set under Settings, AI settings. See ToolRoundsPreference.
builder.Services.AddScoped(sp => new GDSViewer.Ai.ToolRoundsPreference(sp.GetRequiredService<GDSViewer.Models.AppStorage>()));

var host = builder.Build();

//How a picture drawn at a pace lets the page in: a message the page posts to itself rather than a timer, and a timer
//after all when the page does not answer, so a picture is never given up for it. See Pacer.PageTurn.
var pageJs = host.Services.GetRequiredService<IJSRuntime>();

GDSViewer.Models.Pacer.PageTurn = async () =>
{
    try
    {
        await pageJs.InvokeVoidAsync("pageTurn");
    }
    catch
    {
        await Task.Delay(1);
    }
};

await host.RunAsync();
