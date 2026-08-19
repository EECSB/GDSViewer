# Vendored third-party libraries

These are checked in on purpose: they are served as-is at runtime, with no bundler and no npm install
step in the build. Record the version here whenever one is added or updated, since there is no
`package.json` to read it from.

| Library | Version | Files | Source |
|---|---|---|---|
| Monaco Editor | 0.41.0 | `monaco/vs/` | `monaco-editor` npm package, `min/vs/` (the AMD distribution) |
| three.js | 0.152.0 | `three/` | `three` npm package, `build/` and `examples/jsm/` |

## Monaco Editor

Only the seven files the app actually needs are vendored:

| File | Why |
|---|---|
| `vs/loader.js` | Monaco's AMD loader |
| `vs/editor/editor.main.js` | the editor itself |
| `vs/editor/editor.main.css` | editor styles |
| `vs/editor/editor.main.nls.js` | the default (English) UI strings. **Required** — the loader derives this path from the bundle name at runtime, so it does not appear as a literal string in `editor.main.js`. Omitting it fails the whole load with "Failed trying to load default language strings" |
| `vs/base/browser/ui/codicons/codicon/codicon.ttf` | icon font, referenced by `editor.main.css` |
| `vs/base/worker/workerMain.js` | the editor web worker |
| `vs/base/common/worker/simpleWorker.nls.js` | the worker's counterpart to `editor.main.nls.js` |

Deliberately **not** vendored:

- `vs/basic-languages/**` — the app registers its own `GDS` Monarch grammar in
  [`../js/MonacoInterop.js`](../js/MonacoInterop.js) and uses no built-in language. Note that
  `editor.main.js` contains lazy-load stubs for every built-in language, so if a built-in language is
  ever selected its grammar file has to be vendored too or the load 404s.
- `vs/language/**` — the JSON/CSS/HTML/TypeScript language services and their workers. Unused, and
  they were the bulk of the old webpack output.
- `vs/editor/editor.main.nls.*.js`, `vs/base/common/worker/simpleWorker.nls.*.js` (the ones with a
  locale in the name) — non-English localizations.

To update: `npm view monaco-editor version`, install that version into a scratch folder, then copy the
seven files above out of its `min/vs/` and update the table. Load the text editor view afterwards and
check the browser console — a missing file shows up there as a loader error, not as a build failure.

## three.js

`three/three.module.min.js` is the ESM build. The addons under `three/addons/` come from the package's
`examples/jsm/` and keep their original directory layout, which matters because they import each other
by relative path:

| File | Used for |
|---|---|
| `addons/controls/OrbitControls.js` | mouse orbit/pan/zoom |
| `addons/exporters/{STL,OBJ,GLTF}Exporter.js` | the model download dropdown |
| `addons/webxr/{VR,AR}Button.js` | the WebXR entry buttons |

Both the bare `three` specifier and the `three/addons/` prefix are mapped by the import map in
[`../index.html`](../index.html), so `../js/ThreeInterop.js` is loaded with `type="module"` and keeps
plain `import ... from 'three'` lines. Every one of the addons above imports nothing but bare `three`,
so this list is the complete transitive set — verify that again after a version bump.

To update: install the new `three` into a scratch folder, copy `build/three.module.min.js` plus the
addon files listed above out of `examples/jsm/`, and re-check the addon imports.
