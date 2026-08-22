# Code style guidelines

These conventions apply to all code in this project — **C#**, **Razor (`.razor`)**, and **JavaScript**. Follow them when writing or editing code.

## Language

### Always write in US English

All comments, identifiers (variable / method / property / class names), UI text (labels, tooltips, messages), and documentation must use **US English** spelling — `color` not `colour`, `center` not `centre`, `behavior` not `behaviour`, `gray` not `grey`, `initialize`/`organize`/`recognize` not `initialise`/`organise`/`recognise`, `labeled`/`canceled` not `labelled`/`cancelled`, `meter`/`nanometer`/`millimeter` not `metre`/`nanometre`/`millimetre`. This applies to Markdown docs too.

#### The one exception: strings this app *accepts* from somebody else

**Input the app does not control is matched as it arrives, whatever it is spelled like.** The embedding API in [`Models/Embedding.cs`](Models/Embedding.cs) accepts `nanometre` and `millimetre` beside their US spellings, because a page that has been passing `unit=nanometre` since it was written goes on working and stops the day somebody tidies the label away. Those four `case` labels are data, not prose.

The rule still holds for everything the app *emits*: that same file answers `"Nanometer"` whichever spelling it was asked with. **Tolerant on the way in, consistent on the way out.**

So a sweep for a British spelling has to read what it lands on. A `case` label in a parser is an accepted value; anything else is prose and gets corrected.

## Control flow

### `if`: put the body on its own line — never on the same line as the condition

This applies even to a single-statement guard clause. Single statements do not get braces.

✅ Do this:
```csharp
if (gds is null || showLayers is null)
    return;
```

❌ Not this:
```csharp
if (gds is null || showLayers is null) return;
```

### Keep an `if` condition on one line; split each clause only when too long

Same idea as parameter lists: keep the whole condition on one line unless it runs off-screen, then put each `&&` / `||` clause on its own line.

✅ One line:
```csharp
if (records[i].Type == RecordType.MASK && MASKS.Count > 0)
    return true;
```

✅ Too long → one clause per line:
```csharp
if (elementModel.Element is IHasLayer
    && element.XY is not null
    && element.XY.Data.Length >= 4)
    return true;
```

### Separate consecutive `if` statements with a blank line (and before a trailing `return`)

Put a blank line between consecutive `if` blocks/statements, and before a final fall-through `return`. Each guard's body still goes on its own indented line.

✅ Do this:
```csharp
if (elementModel.Element is not IHasLayer)
    return false;

if (element.XY is null)
    return false;

if (Layers.ContainsKey(layerNumber) && Layers[layerNumber].Depth > 0)
    return true;

return false;
```

❌ Not this (no blank lines between the guards):
```csharp
if (elementModel.Element is not IHasLayer)
    return false;
if (element.XY is null)
    return false;
if (Layers.ContainsKey(layerNumber) && Layers[layerNumber].Depth > 0)
    return true;
return false;
```

This also applies to consecutive braced blocks:
```csharp
if (records[i].Type == RecordType.ELFLAGS)
{
    ...
}

if (records[i].Type == RecordType.PLEX)
{
    ...
}
```

### Use `if` / `else` instead of the ternary (`?:`) operator

✅ Do this:
```csharp
if (value)
    wrap = "on";
else
    wrap = "off";
```

❌ Not this:
```csharp
wrap = value ? "on" : "off";
```

This applies in JavaScript too (use `let` + `if`/`else` where a `const` ternary was used). The `||` and `&&` short-circuit operators are **not** ternaries and are fine to keep, and neither is `??` / `??=`.

**Watch for a side effect in a branch.** A ternary only evaluates the branch it takes, so `type == 3 ? 1 : 2 + cursor.ReadUnsigned()` reads from the cursor on one path and not the other — several in `OasisReader` were exactly this, where skipping a read the record does not carry is the difference between reading the record and reading past it. Converting one of those means keeping the read inside the `if`, not hoisting it above:

```csharp
long columns = 1;

if (type != 3)
    columns = 2 + (long)cursor.ReadUnsigned();
```

**Exception — UI markup.** A ternary is acceptable in Razor markup (e.g. inside an attribute) when it keeps the markup compact and an `if`/`else` block would be clumsier:
```razor
<img class="icon @(item.IsSelected ? "icon-on" : "icon-off")" />
```

### Use `if` / `else if` for mutually exclusive conditions

Don't write separate consecutive `if` (or Razor `@if`) blocks that test the same variable for different values.

✅ Do this:
```razor
@if (view == ViewType.View2DSvg)
{
    ...
}
else if (view == ViewType.View3D)
{
    ...
}
```

❌ Not this:
```razor
@if (view == ViewType.View2DSvg) { ... }
@if (view == ViewType.View3D) { ... }
```

### `try` / `catch`: use full block braces — never collapse onto one line

The `try` keyword, its braces, and its body each go on their own line (the body indented), even when the body is a single statement. When the `try` follows another statement, separate them with a blank line. An empty `catch { }` may stay on one line.

✅ Do this:
```csharp
view = (ViewType)Enum.Parse(typeof(ViewType), e.Value.ToString());

try
{
    await js.InvokeVoidAsync("drawInterOp", new { elements = elements });
}
catch { }
```

❌ Not this:
```csharp
view = (ViewType)Enum.Parse(typeof(ViewType), e.Value.ToString());
try { await js.InvokeVoidAsync("drawInterOp", new { elements = elements }); }
catch { }
```

## Methods

### Use block bodies with an explicit `return` — not expression-bodied members (`=>`)

✅ Do this:
```csharp
public static bool IsElementRecord(RecordType type)
{
    return type == RecordType.BOUNDARY;
}
```

❌ Not this:
```csharp
public static bool IsElementRecord(RecordType type) => type == RecordType.BOUNDARY;
```

**Exception — properties.** A simple computed *property* may use an expression body (`=>`) when that reads cleanly. This exception is for properties, not methods:
```csharp
public bool HasGeometry => XY?.Data is int[] points && points.Length > 0;
```

### Keep signatures and calls on one line; split only when too long

Prefer a single line for a method's signature, its parameter list, or a call's arguments. Only break onto multiple lines when the line would otherwise run off-screen — and when you do, put **each parameter/argument on its own line** (don't leave some on the first line and wrap the rest).

✅ Fits on one line:
```csharp
public Layer(short layerNumber, string layerColor, int layerOffset = 10, int layerDepth = 50)
```

✅ Too long → one parameter per line:
```csharp
public BoundaryModel(
    Record boundary,
    Record layer,
    Record dataType,
    Record xy,
    Record elFlags = null,
    Record plex = null)
```

❌ Don't half-wrap:
```csharp
public BoundaryModel(Record boundary, Record layer, Record dataType, Record xy,
    Record elFlags = null, Record plex = null)
```

## Strings

### Use a `StringBuilder` or a multi-line verbatim string instead of `+=` in a loop

Appending to a `string` inside a loop reallocates on every iteration — the compiler does not hoist it into a `StringBuilder`. Use `StringBuilder` for loops, and a verbatim (`@"..."`) or interpolated-verbatim (`$@"..."`) string for a value assembled from a fixed set of pieces.

✅ Do this:
```csharp
var builder = new StringBuilder();

foreach (var record in Records)
    builder.Append($"{record.Type}: {data} \n");

return builder.ToString();
```

❌ Not this:
```csharp
string gdsAsText = "";

foreach (var record in Records)
    gdsAsText += $"{record.Type}: {data} \n";

return gdsAsText;
```

✅ And for a fixed set of pieces:
```csharp
return $@"<polygon points=""{points}""
    fill=""{color}""
    opacity=""{opacity}"" />";
```

❌ Not this:
```csharp
return "<polygon points=\"" + points + "\""
    + " fill=\"" + color + "\""
    + " opacity=\"" + opacity + "\" />";
```

## Comments

### No spaces between the comment slashes and the text

Remove the space after `//` and `///`. For an inline comment, also remove the space **before** the slashes (between the code and the comment). Keep the indentation in front of a line-start comment.

✅ Do this:
```csharp
///<summary>Decodes a GDSII eight-byte real.</summary>
private static double ToDoubleHelper(byte[] data)
{
    return double8;//already sign-corrected
}
```

❌ Not this:
```csharp
/// <summary>Decodes a GDSII eight-byte real.</summary>
private static double ToDoubleHelper(byte[] data)
{
    return double8; // already sign-corrected
}
```

Do **not** touch `//` that is part of a URL (e.g. `https://`, `://` inside a string) — those are not comments.

# Project conventions

## Vendored JavaScript, no bundler

Third-party JS lives under [`wwwroot/lib/`](wwwroot/lib) as plain files checked into the repo, and the app's own interop lives in [`wwwroot/js/`](wwwroot/js) as one file per library. **Nothing the app ships comes from a package manager, and there is no bundler** — `dotnet build` is the whole build, and it must stay that way. When adding a library, vendor the minimum set of files it needs and record the version in [`wwwroot/lib/README.md`](wwwroot/lib/README.md); do not add a build step or reach for a CDN (the app is a PWA and must work offline).

The rule is about what ships. The **test** tooling does use npm (Playwright, in [`package.json`](package.json)), which is fine because nothing it installs reaches `wwwroot` or the build — but do not let a dependency cross that line.

The vendored files are marked `-text -diff` in `.gitattributes` so they stay byte-identical to upstream and never produce multi-megabyte diffs. They are replaced wholesale on a version bump, never edited in place.

## Tests

Three layers. Run all of them for a change that crosses into the browser:

```bash
dotnet test          # C# units and the 897-file corpus
npm test             # browser-JS units (Node's own runner, nothing to install)
npm run test:e2e     # Playwright end-to-end (npm install first; it starts the app itself)
```

The parser and the layer/color logic are pure C# and are covered directly. Pure JS belongs in [`wwwroot/js/viewGeometry.js`](wwwroot/js/viewGeometry.js), which has no DOM or three.js dependency so Node can require it. Anything needing a real browser — WebGL, Monaco, pointer events, component lifecycle — belongs in [`e2e/`](e2e). See [DOCUMENTATION.md](docs/DOCUMENTATION.md#testing) for what is and is not covered.

When writing e2e specs: **poll rather than read once** (a view is drawn after it is mounted, so a single read races it), and do not wait on the layer sidebar — the text view has none.

**Never set a file on `#fileUpload` after a bare `page.goto` — reach the app through `gotoApp` first.** It waits for Blazor's `InputFile` to have attached its own listener, and a file set before that carries no files at all: the handler sees `FileCount` 0 and takes the same silent return a canceled file dialog takes. Nothing is drawn, nothing is logged, there is no dialog and not even a history entry — the app simply keeps whatever it opened by itself, so the spec fails much later reporting the *default example's* shape count. Only a spec can reach this. The input is hidden, so the two ways to a file are the dialog — which cannot be answered inside the window — and a drop on the view, and [`fileDrop.js`](wwwroot/js/fileDrop.js) reads that same flag and declines rather than dispatching a `change` nothing is listening for.

**Do not run `dotnet build` or `dotnet test` while an e2e run is live.** Playwright starts the app itself, so a rebuild replaces the files under the server it is serving from, and the specs then fail against a half-written app rather than against anything real. It does not look like interference: it looks like a large layout opening with 18 shapes in it, or forty-six unrelated failures. Let the e2e run finish, or run it after — a result that overlapped a build has to be thrown away and taken again.

## Verifying UI changes

Anything that touches a view, the interop, or `index.html` needs to be checked in a real browser — the three views each depend on JS that the build cannot validate. Load a sample GDS from the toolbar dropdown and confirm the affected view still renders, then check the browser console for errors.
