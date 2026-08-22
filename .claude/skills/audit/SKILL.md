---
name: audit
description: Full-project health check for GDSViewer. Brings every document back in line with the code, sweeps for bugs and inefficiencies, measures performance against the recorded numbers, and checks CLAUDE.md style and the project's structural rules. Ends with a categorized findings list. Use when asked to audit, review, or health-check the project as a whole - not for reviewing one change or one file.
argument-hint: "[docs|style|bugs|perf|structure ...]"
---

# Project audit

Five passes over the whole project. Arguments name the passes to run (`docs`, `style`, `bugs`, `perf`,
`structure`); no arguments runs all five, in that order. Documentation goes first on purpose: reading what
the project says about itself is what makes drift and contradiction visible in every pass after it.

## What gets fixed and what gets listed

- **Documentation drift: fixed in place**, in each document's own voice.
- **Style violations living entirely inside comments or docs: fixed in place** - a spelling or a spacing
  there changes no behavior.
- **Everything else: listed, never fixed during the audit.** Above all, no identifier renames - interop
  functions cross the C#/JS boundary by string name, so a rename that compiles cleanly can still break the
  app silently. A style fix that touches executable code goes on the findings list with everything else.

## Judgment rules

These separate a useful audit from a list of opinions, and each was earned here.

- **A decision with a recorded reason is not a finding.** This codebase writes down *why* - in comments, in
  the docs, in commit bodies. Labels are not extruded, `reuseExistingServer` is false, the tolerance table
  refuses what it refuses, OASIS timestamps are fixed at 1970: all deliberate, all documented where they
  live. Reopen one only when its recorded reason no longer holds, and cite what changed.
- **Measure rather than reason.** Performance findings come from the instruments in pass 4, never from
  reading. Reachability claims - "this guard can never fire", "this fix is redundant" - get a mutation or a
  reproduction before they get reported.
- **Confirm bugs by reproduction.** One that cannot be reproduced is reported as suspected, together with
  the step that would settle it. Format-behavior questions are settled against the spec and against KLayout
  (installed at `%APPDATA%\KLayout\klayout_app.exe`), not by preference.
- **Vendored code is exempt from every check.** `wwwroot/lib/**` and `GdsII/Clipper2/**` are byte-identical
  to upstream on purpose (`-text -diff` in `.gitattributes`) and are replaced wholesale, never edited. Style
  findings there are noise; the only finding worth having is a version worth bumping.
- **Check what is already done before proposing it.** The task list and `git log` first; a finished piece of
  work does not restart as a recommendation.
- **Report coverage, not just findings.** Say what was checked and found clean, and name any pass that
  sampled rather than swept. An audit that lists only problems hides what it never looked at.

## Pass 0 - baseline

`git status` first - know what is uncommitted before editing anything, or the audit's changes and someone
else's blur together. Then `dotnet test` from the root and `npm test` from `tests/` for a green baseline;
the 33 `Needs=KLayout` tests run for real on this machine. Skip the e2e suite here - it comes in only if
a later pass needs a browser fact, and then under the hazards below.

## Pass 1 - documentation against code

The prose that carries claims:

- `README.md`, `GdsII/README.md`, `GdsII.Cli/README.md` - three audiences, three link bases; the package
  readmes render on nuget.org, which resolves no repo-relative link.
- `docs/DOCUMENTATION.md`, `docs/CLI.md`, `docs/NUGET.md`, `docs/FEATURES-DEMO.md`,
  `docs/THIRD-PARTY-NOTICES.md`, `wwwroot/lib/README.md`, and `CLAUDE.md` itself.
- **Prose comments in the two packable `.csproj` files** - they carry claims about formats, licenses and
  packaging, and they have drifted before.
- Header comments in tests and specs that state facts. Above all the tolerance table in
  `docs/DOCUMENTATION.md`, whose every row is pinned by a named test in `tests/GDSViewer.Tests/ToleranceTests.cs` - the
  table and the tests must agree.

Every checkable claim gets checked: counts, byte totals, tables, file paths, anchors, feature lists, "the
only place that..." sentences. Numbers quoted in prose - test counts, corpus size, measured sizes and
times - are the likeliest drift. Fix what is wrong in place; the findings list records what was fixed.

## Pass 2 - CLAUDE.md compliance

Grep produces candidates; the rules have stated exceptions, so every hit needs eyes before it counts as a
violation. Exclude the vendored trees and this skill's own file, whose word list below matches itself.

- **US English** - case-insensitive word search for: centre, colour, behaviour, grey, licence, initialise,
  organise, recognise, serialise, normalise, optimise, analyse, labelled, cancelled, modelled. Watch for
  proper nouns - the Unlicense is one.
- **No ternaries** in `.cs`, `.razor`, `.js` - search ` ? ` with a matching ` : ` on the line; noisy, since
  `?.`, `??` and `int?` are not ternaries. A ternary in Razor markup attributes is allowed. When one is
  converted, a side effect stays inside the branch that had it.
- **No `=>` method bodies** - `^\s*(public|private|protected|internal).*\(.*\)\s*=>` in `.cs`. A simple
  computed *property* may use `=>`; lambdas inside bodies are fine.
- **Comment spacing** - `// ` or `/// ` with a space after the slashes, and a space before an inline `//`.
  `://` inside a URL is not a comment.
- **`if` body on its own line** - `if \(.*\)\s+\S` catching a statement on the condition's line.
- **No `+=` on strings in loops** - search `+=` near string types; `StringBuilder` or a verbatim string is
  the fix.

The rules that do not grep well - blank lines between consecutive `if`s, one-clause-per-line wrapping,
full-brace `try`/`catch`, `if`/`else if` for mutually exclusive conditions - are read for in whatever files
the searches already opened, plus the most recently touched code.

## Pass 3 - bugs

Read the seams, which is where every past bug in this project has lived:

- **The C#/JS interop boundary** - calls by string name in both directions, checked by nothing but running
  the app.
- **State-symmetry pairs** - the OASIS reader's modal resets against the writer's. `resetCellState` clears
  *only* the positions; layer, sizes and names persist across cells, and both sides must agree.
- **Format limits** - the 65,535-byte record, 8,190 corners, fracture at the boundary; off-by-ones live at
  edges like these.
- **Culture** - anything formatted for output or compared in a message wants `FormattableString.Invariant`;
  `2.444.116` has been printed here before.
- **Silent early returns** - the pattern where nothing happens and nothing says so. A `FileCount` of 0
  reads exactly like a canceled file dialog.
- **The one-list contract** - the models, the text view and the drawing are all built over the same
  `Records` list; an edit path that forks or bypasses it desynchronizes what the user sees from what the
  user edits.

Spot-check the tests themselves: pick a few guards where a silent fault would cost most, break the code
they claim to pin, and confirm the test goes red before restoring. A test can be green for a reason
unrelated to its name - it has happened here more than once. And remember the class no correctness test
sees: **a size-only or speed-only fault stays green everywhere** except in the instruments of pass 4.

## Pass 4 - performance

Never from reading. The instruments:

- `gds bench` (`GdsII.Cli/Commands/Cli.Bench.cs`) - staged timings over the corpus, `oasis` among them.
- The size harness in `tests/` - it asserts thresholds, so running it *is* the comparison.
- `tests/e2e/large-layout.spec.js` and the in-page frame timer - open, markup, pan and edit on a generated large
  layout.

Compare against the numbers the repo has recorded - in `docs/DOCUMENTATION.md` and in recent commit
bodies - not against feel. A finding is a measured delta with both numbers in it.

## Pass 5 - structure and maintainability

The separations this project has drawn, each one checkable:

- **`GdsII` depends on nothing.** Zero `PackageReference` in `GdsII/GdsII.csproj`; that property is what
  makes the library worth depending on. LibTessDotNet and SharpGLTF stop at the CLI.
- **Nothing browser-shaped in the library**, and no app types in its public surface - the precedent is
  `SvgWriter` taking `LayerKey` rather than `CheckboxItem`.
- **Nothing from npm ships.** Third-party JS is vendored under `wwwroot/lib/` with versions in its README,
  `dotnet build` is the whole build, and the test tooling is the only npm consumer - nothing it installs
  reaches `wwwroot` or the build.
- **Tests in the right layer.** Pure C# under `tests/`; JS that Node can require stays in
  `wwwroot/js/viewGeometry.js`, which has no DOM or three.js dependency; anything needing a real browser is
  in `tests/e2e/`.
- **Duplication across the app, CLI and test seams** - the usual fix moves the shared logic into the
  library. `GDS.FromText` exists because the CLI needed it.

On generic principles - design patterns, dependency injection, the rest: the app uses Blazor's built-in
container, and the library takes no dependencies at all, which is a feature rather than a gap. Judge
maintainability against CLAUDE.md and the project's own separations; where a textbook principle and the
project's recorded idiom disagree, the idiom wins, and the finding - if there is one - names a real cost.

## Hazards while auditing

- **Never `dotnet build` or `dotnet test` while an e2e run is live**, and never rebuild while any
  `dotnet run` server is up - the served files stop matching their integrity manifest, and whoever has the
  app open gets a server that will not boot on reload.
- If a browser fact is needed while the user may have a server on port 5105: from `tests/`, copy the
  config with `sed 's/5105/5199/g' playwright.config.js > playwright.probe.config.js`, run with
  `--config=playwright.probe.config.js`, and **delete the copy afterwards**. Know that
  `tests/e2e/embedding.spec.js` hardcodes `localhost:5105` in four places and false-reds on any other port.
- In any spec written during the audit: reach the app through `gotoApp`, poll rather than read once, and
  never wait on the layer sidebar - the text view has none.

## The findings list

End with one list, most severe first within each group:

1. **Bugs, confirmed** - with the reproduction.
2. **Bugs, suspected** - with what would confirm each.
3. **Fixed in place** - the documentation and comment-only corrections already made.
4. **Style needing a code change** - renames, ternaries, anything executable.
5. **Performance** - measured deltas only, both numbers shown.
6. **Structure** - with the cost each one carries, named.
7. **Checked clean** - what was verified and found in order.

Every entry: `file:line` - what - why it matters - rough size (S/M/L). Offer to file chosen entries as
tasks. Do not begin fixing code findings unasked.

If the session's instructions say to commit the in-place fixes, use the repo's commit voice: a full
sentence about what changed, a body that says why, the test-count line - and read `git log` first to match
it.
