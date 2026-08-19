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

await builder.Build().RunAsync();
