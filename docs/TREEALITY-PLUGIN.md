# GDS Viewer as a Treeality plugin

**Planned, not built.** GDS Viewer is meant to go into Treeality - the same author's platform, a sibling of this
repository at `Treeality/Code/Treeality` - as a plugin (asked 2026-10-31), with its AI assistant using the models and the
storage of the account signed in there. This is what was found reading Treeality's code that day, what GDS Viewer
already has for it, and the two ways in - written to be picked up from the Treeality side. Paths below that are not
links are relative to Treeality's `Code/Treeality`; it changes quickly, so read them again before relying on one.

## What GDS Viewer has for it already

**Everything its AI stands on is behind one interface**, [`IAiService`](../Ai/IAiService.cs) (2026-10-31): the models
and whether they can be edited here, `CreateProvider` for the thing that talks to one, and the AI assistant chats - listed,
opened, kept and deleted, each a [`SavedChat`](../Ai/SavedChat.cs) of plain JSON with pinned names. The chat window, the
settings window and the PDK manager's Write with AI ask the container for it and nothing else. The standalone app
registers [`BrowserAiService`](../Ai/BrowserAiService.cs) in [`Program.cs`](../Program.cs): models with the person's
own keys and chats, both in this browser's IndexedDB, providers called from the page. See
[Whose AI it is](DOCUMENTATION.md#whose-ai-it-is) and [Chats](DOCUMENTATION.md#chats).

## How Treeality hosts a plugin

- **Treeality is a Blazor Web App whose UI is WebAssembly**, and a plugin runs in the browser alone: a Razor class
  library referencing `GraphDBViewer.Core`, which is the whole host API.
- **A plugin implements `IPluginModule`** (`GraphDBViewer.Core/Code/Plugins/IPluginModule.cs`):
  `ConfigureServices(IServiceCollection)`, called before the client's `Build()`, and `Register(IPageRegistry)` for its
  pages - each an `@page` component and a `PluginPage` entry, shown as a tab in the top bar and under Plugins.
- **The plugin registers its own services; the host replaces none of them**, since it does not know a plugin's types.
  So `Program.cs` does not run inside Treeality at all: **GDS Viewer's module registers the `IAiService`**, one written
  over Treeality's services, and both it and the standalone `Program.cs` should call one shared registration method
  (`services.AddGdsViewer(...)`, say) so the rest of the editor's services are set up once.
- **Two kinds of plugin.** A *bundled* one is a build-time project reference, registered by hand in
  `Treeality.Client/Program.cs` - its static files served at `_content/<Assembly>/`, and any server half living in the
  host project. An *installed* one is fetched at run time and loaded with `AssemblyLoadContext.Default.LoadFromStream`
  (`Treeality.Client/PluginBoot.cs`, `DotNetAssemblyLoader.cs`), held to its `manifest.json`: one plugin, the manifest's
  id, only the routes it declares.
- **An installed plugin's package is two files**: `manifest.json` and the one assembly its `entry` names; every other
  entry in the zip is ignored (`GraphDBViewer.Core/Code/Plugins/PluginPackage.cs`). No second assembly, no `wwwroot`,
  no JavaScript - and no server code of its own, by decision.
- **No `App.razor`, router or `index.html` of its own**: a plugin's pages render inside Treeality's layout.
- The manifest: `id` (reverse-DNS; `treeality.` reserved for bundled ones), `name`, `version`, `hostApiVersion`
  (`1.1` or `1.0`), `kind: "dotnet"`, `entry` (`Asm.dll!Full.Type`), `routes`, and `permissions`, each under the id
  (`GraphDBViewer.Core/Code/Plugins/PluginManifest.cs`). Authoring is
  `wiki/pages/integrating/writing-a-plugin.md`; the design is `specs/features/plugin-system/design.md`.

## What Treeality has for AI, accounts and storage

- **Models: `LlmConnectionStore`** (`GraphDBViewer.Core/Code/Llm/LlmConnectionStore.cs`), a dictionary of
  `LlmConnection` under `graphdbviewer:llmConnections` through `IAppStorage` - which in Treeality is **the signed-in
  person's workspace on the server**, mirrored in the browser (`Treeality.Client/Program.cs`, `FallbackAppStorage`). Per
  person, not per organization; each brings their own key, added under the top bar's AI settings. It raises `Changed`.
  Its `LlmConnection` has the same fields as GDS Viewer's (GDS Viewer's was ported from it), as a different type.
- **Calls go from the browser with the key in the page**, through `LlmProviderFactory` and `LlmHttpClient`. There is no
  AI proxy on the server; Instruments' bench is the one place a key is put on a request server-side.
- **No AI service for plugins**: nothing to inject but the store, the picker (`LlmConnectionPicker`) and the providers -
  Parts' `ProvidersTab.razor` uses them directly. Treeality's own assistant takes one tool runner and no tools from a
  plugin.
- **No saved chats anywhere**: its assistant keeps the conversation in memory.
- **Who is signed in**: `MainLayout` cascades an `AccountInfo` (`Id`, `Email`, `Permissions`, `Can(...)`) to every
  page; `SignedInAccount.IdAsync()` is the injectable way to the id. One instance per customer, so no tenant.
- **Where a plugin keeps data**: per person in `IAppStorage` under keys of its own (Parts uses
  `graphdbviewer:parts:...`); per instance in one settings blob, `plugins:<id>:settings`; the graph read-only.
- **Other things a plugin may add**: an `IAssetPreview`, so the asset library previews `.gds` and `.oas` as Bim's does
  for models; an `IBoardPanel`; one toolbar control through `ShellState.ToolbarContribution`.

## Treeality's `IAiService`, as GDS Viewer would write it

| `IAiService` | From Treeality |
|---|---|
| `LoadModelsAsync`, `ModelsChanged` | `LlmConnectionStore.LoadAsync` and `Changed`, each `LlmConnection` mapped across field for field |
| `ModelsEditable`, `SaveModelsAsync` | Either `true`, saving through the store, so GDS Viewer's AI settings and Treeality's are one list; or `false`, and the settings window points at Treeality's |
| `KeptWhere` | `ViewerHostOptions.WhereAiKeysAreKept`, or "with your account" |
| `CreateProvider` | GDS Viewer's own providers, which carry its usage, effort and file handling, with the client `LlmHttpClient` gives |
| Chats | `IAppStorage`, under keys prefixed with the plugin's id: per person, following the account, kept on the server |

The layout tools stay GDS Viewer's own (`LayoutToolRunner`); the chats panel, the files and the costs come with it.

## The two ways in

1. **A bundled plugin** (recommended). GDS Viewer's UI moves into a Razor class library - the pages, components, `Ai/`
   and `wwwroot/js` - that both the standalone app and Treeality reference; the standalone app becomes a thin shell of
   `Program.cs`, `App.razor` and `index.html` around it. Treeality references it and `GdsII` from
   `Treeality.Client.csproj`, registers its module in the client's `Program.cs` as Bim's is, and adds it to the server's
   routing so its `_content/` is served. Things to settle on the way:
   - **Monaco and three.js are in both.** GDS Viewer vendors Monaco 0.41.0 and three.js 0.152.0
     ([`wwwroot/lib/README.md`](../wwwroot/lib/README.md)); `GraphDBViewer.Core` ships its own Monaco and three.js. Two
     copies on one page will fight over globals and the AMD loader - one has to go, and the other has to be the
     version both work with.
   - **`Net.Codecrete.QrCodeGenerator`** is 2.0.6 here and 3.1.0 in the core; one assembly version loads.
   - **The page's own frame**: `index.html`'s script tags and styles become the library's, loaded by the page that
     needs them; the app's header, About and `Embedding` stay with the standalone shell.
   - **Its id and permissions**: `treeality.gds` or the like, bundled, with a page such as `/gds` declared in `routes`.
2. **A frame, as Instruments hosts the Lab Equipment Controller**: GDS Viewer deployed as it is, in an `<iframe>` on a
   bundled page, served same-origin through the host. Nothing of GDS Viewer moves - but dependency injection does not
   cross a frame, so its `IAiService` would call Treeality endpoints for the models and the chats, which is server code
   in the host, and only a bundled plugin can have that. It would let keys stay on the server, as the bench's do.

**Not as an installed plugin today**: GDS Viewer is two assemblies and a few megabytes of JavaScript, and an installed
package carries one assembly and nothing else.

## Found in Treeality on the way

Written into Treeality's own `TODO.md` (2026-10-31, not fixed): its wiki's plugin page still lists the host API check
as not done, which it is since 2026-10-31; and `treeality-plugins/host-api-versions.json` still lists only 1.0, where
the app runs 1.1 too, so the registry would refuse an entry built for 1.1.
