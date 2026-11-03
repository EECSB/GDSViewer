# The `gds` command line

Everything the viewer does to a file, without a browser: read it, check it, name its layers, draw it, extrude
it, combine its geometry and convert it to another format - and, through `gds mcp`, hand a folder of layouts to
an AI model to edit.

`GdsII.Cli/` is a console front end for [the same library](NUGET.md) the web app uses. It is a portable .NET
app with no native dependency, so it runs anywhere the runtime does — Windows, macOS, Linux, the same build.

- [Getting it](#getting-it)
- [The commands](#the-commands)
- [Layermaps](#layermaps)
- [Choosing layers](#choosing-layers)
- [`gds svg`](#gds-svg)
- [`gds model`](#gds-model)
- [`gds boolean` and `gds size`](#gds-boolean-and-gds-size)
- [`gds convert`](#gds-convert)
- [Pipes, exit codes and scripting](#pipes-exit-codes-and-scripting)
- [How much of a layout a command draws](#how-much-of-a-layout-a-command-draws)
- [`gds cells`](#gds-cells)
- [`gds nets`](#gds-nets)
- [`gds drc`](#gds-drc)
- [`gds measure`](#gds-measure)
- [`gds generate` and `gds bench`](#gds-generate-and-gds-bench)
- [`gds mcp`](#gds-mcp)
  - [The layout open in the app: AI settings, MCP server](#the-layout-open-in-the-app-ai-settings-mcp-server)
- [What the app can do and this cannot](#what-the-app-can-do-and-this-cannot)

## Getting it

```bash
dotnet tool install -g GdsII.Cli
gds info "wwwroot/resources/GDS Files/Sky130 GDS/Mosfet.gds"
```

Or straight out of the repository, without installing anything:

```bash
dotnet run --project GdsII.Cli -- info "wwwroot/resources/GDS Files/Sky130 GDS/Mosfet.gds"
```

To install a local build as a real command:

```bash
dotnet pack GdsII.Cli -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg GdsII.Cli
```

`gds --help` prints every command and every option — the ones that size the layout `generate` and `bench` make
are also [under their own heading](#gds-generate-and-gds-bench) — and `gds --version` the version it reads off
its own assembly.

## The commands

| Command | What it does |
|---|---|
| `gds info <file>` | Units, structures, elements, layers, unresolved references |
| `gds dump <file>` | Every record as text, one per line |
| `gds build <text>` | Reads that text back into a `.gds` |
| `gds validate <path...>` | Parses and reports; a directory is searched for `.gds` files |
| `gds layers <file>` | Layer/datatype pairs with a count of what is on each, named if a layermap says so |
| `gds svg <file>` | The layout as a standalone SVG |
| `gds model <file>` | The layout extruded into a 3D model: `.stl`, `.obj`, `.gltf`, `.glb` |
| `gds boolean <file>` | Combines two layers into a third — `--op and\|or\|not\|xor` |
| `gds size <file>` | Moves every edge of a layer out or in — `--by`, negative to shrink |
| `gds convert <file>` | Between GDSII, OASIS and DXF, any direction - and from SVG |
| `gds cells <file>` | The library's cells: what places what, and what is in each |
| `gds nets <file>` | Everything joined to the shape at a point. Needs a layermap |
| `gds measure <file>` | The distance between two points, the way the 2D view's ruler reads it |
| `gds drc <file>` | Checks it against a deck of design rules. Needs `--deck` |
| `gds generate` | A layout of a chosen size, to measure against |
| `gds bench [<file>]` | Times parse, flatten, svg, merge, a rule check and the OASIS write over one, with the size - and a whole rule deck with `--deck`, every net with `--layermap` |
| `gds mcp [<folder>]` | Lets an AI model open, draw on, check and save the layouts in a folder, over MCP |

**Every command reads GDSII, OASIS, DXF and SVG already**, told apart by what the file starts with rather than
by what it is called — so a renamed file still opens, and `convert` is only needed to *write* a different format.
An SVG is read as the drawing it is - filled and stroked paint as area, a layer per Inkscape layer or per color;
see [Reading SVG](DOCUMENTATION.md#reading-svg).

## Layermaps

A GDSII file carries only numbers. Nothing in the format records that `65/20` is diffusion, which is why
KLayout wants a `.lyp` and Magic its techfile — so what a layer is called, what color it is really drawn in,
what it is *for*, and where it sits in the wafer all come from a file you supply.

`--layermap <file>` takes that file. It is the same CSV the web app's PDK manager imports into a PDK and the same
one `--write-layermap` hands back, so a mapping made in either place works in the other.

```
layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize,texture,texturesize
65,20,diff,#e69ac5,0,120,conductor
66,20,poly,#d80000,180,180,conductor
66,44,licon1,,300,180,via
```

Everything past the third column is optional. **The columns are positional, so a gap is a comma** —
`66,44,licon1,,300,180` gives licon1 a stack and no color of its own. A name with a comma in it goes in double
quotes, the way a spreadsheet writes one: `65,20,"poly, gate"`. A row that cannot be read is reported
by line number and the readable rows still apply; a mapping that matches *none* of the file's layers is called
out, because that means the wrong technology or the columns in the wrong order rather than an ordinary miss.

Which columns reach which command:

| Command | Uses |
|---|---|
| `gds layers` | the names — the first column becomes `diff (65/20)` |
| `gds svg` | the names, the colors, and the fill patterns |
| `gds model` | the heights and thicknesses, so the stack is the real one, and the names and colors each layer's mesh and material carry in `.obj` and glTF |

```bash
gds layers cell.gds --layermap sky130.csv
gds svg cell.gds --layermap sky130.csv -o cell.svg
gds model cell.gds --layermap sky130.csv -o wafer.glb
```

The last two columns are the web app's: a picture a layer is drawn with in its 3D view, named from the CSV's own
folder, and how big one repeat of it is in database units. No command draws them, and `--write-layermap` carries
them through, so a mapping edited here keeps the pictures somebody gave it there.

**A KLayout `.lyp` works too**, for the two things it carries: each layer's name, and the color KLayout fills it
with - a group's color and the brightness included, as KLayout draws it. It has no heights and no roles, so it is
also the quickest start on a CSV that does:

```bash
gds layers cell.gds --layermap sky130.lyp --write-layermap sky130.csv
```

**A layer the mapping placed rests at its own height**, and one it says nothing about takes its place in the
order — so a partial process table is worth having rather than all-or-nothing. `--spacing` then opens the same
extra gap on top of every layer, a mapped one included. It is zero by default, so the model comes out at the
stack the mapping describes.

`gds layers <file> --write-layermap <file>` writes the open file's layers *as* a mapping, every column filled
in with what is currently being drawn. The app's layer panel had an **Export** that did the same, until it went
to the PDK manager on 2026-10-19, whose Export writes a PDK's layermap; the point of this one is that a layermap is
far easier to start from a real file than from a blank page: every pair is already listed, so filling in names
means typing over rather than typing out.

```bash
gds layers cell.gds --write-layermap starting-point.csv
```

A destination of `-` prints it on standard output, after the table and a blank line.

## Choosing layers

`svg` and `model` both take `--layers` and `--hide`, which is what the layer sidebar's checkboxes do in the
app. A layer is written as numbers, the way `gds layers` prints it without a layermap, and a bare number means
every data type on it — `65` is both `65/16` and `65/20`, where `65/20` is only the drawn geometry.

```bash
gds model cell.gds -o metal.glb --layers 67,68,69
gds svg cell.gds -o drawn.svg --hide 67/16,68/16
```

`--layers` is applied first and `--hide` takes back out of it, so the two narrow together. The bounds follow
what is left rather than the whole file, so one layer fills the picture instead of sitting in a frame sized for
layers that were not drawn. A layer this particular file has nothing on is named on stderr and carried on from
— refusing would stop a run over a directory at the first cell that does not use it — while something that is
not a layer at all is a usage error.

## `gds svg`

The same markup the 2D view draws, wrapped in an `<svg>` element sized to what was drawn, so the result is a
file something can open rather than a fragment.

```bash
gds svg cell.gds -o cell.svg
gds svg cell.gds --layermap sky130.csv --opacity 0.8 --no-labels -o cell.svg
```

`--opacity <n>` is 0 to 1 and defaults to 0.5, the same as the app's slider. `--no-labels` leaves the `TEXT`
elements out.

With no `-o` the markup goes to standard output, so `gds svg cell.gds > cell.svg` works — which is why the
layermap's own report goes to standard **error**: a line of prose ahead of the markup would be a line of prose
inside the file.

Y is flipped on the way out, because GDSII counts upward where SVG counts down. The frame is the app's own —
`Preview.Document`, the margin the thumbnails and the download get — turned over about its middle; until
2026-11-03 the command framed the markup on its own, with a margin of its own.

## `gds model`

The same solid the 3D view builds, without a browser to build it in. Every shape is extruded to its layer's
depth and lifted to its layer's place in the stack; the format comes from the extension.

```bash
gds model cell.gds -o cell.glb
```

`--spacing <n>` opens an extra gap between stacked layers, zero by default and the same number the 3D view's
slider shows, and `--scale <n>` multiplies every coordinate — `--scale 0.001` turns sky130's nanometer
database units into micrometers.
`.stl` is written binary unless `--ascii` is passed; `.obj` gets a `.mtl` beside it carrying the layer colors
unless `--no-mtl` is. A `.gltf` writes its geometry to a `.bin` beside it, which is how that format works;
`.glb` is the same model in one file.

Two things differ from what the browser downloads, both deliberate:

- **The model is not tipped over.** The 3D view turns the stack a right angle to look at it, which belongs to
  the camera rather than the layout, so a file written here keeps X and Y as the layout has them with the
  layers stacked up Z.
- **Layers stay separate and keep their colors** in `.obj` and glTF, where the browser's export flattens the
  scene. STL has no way to hold either, so there it is one heap of triangles — a limit of the format.

Labels are left out of all four: a `TEXT` element is an anchor and a string, which no mesh format can hold, and
the browser's exports drop them for the same reason.

## `gds boolean` and `gds size`

The two that change geometry rather than report on it. A transistor gate is where polysilicon crosses
diffusion, which is exactly what the first of these says:

```bash
gds boolean cell.gds --op and --a 66/20 --b 65/20 --into 100/0 -o gate.gds
gds size cell.gds --a 67/20 --by -50 -o undersized.gds
```

Both write a **flat** file: a boolean between two layers means nothing until the references that place them are
resolved, and putting the hierarchy back would mean deciding which cell a derived shape belongs to. The result
is added to the rest of the layout so it can be looked at against what it came from; `--only` writes the result
on its own.

A hole comes out as a keyhole, since GDSII has no hole of its own.

## `gds convert`

```bash
gds convert cell.gds -o cell.oas
gds convert cell.oas -o cell.gds
gds convert cell.gds -o cell.dxf
gds convert drawing.dxf -o drawing.gds
gds convert logo.svg -o logo.gds
```

The output's name picks the format, or `--to gds|oas|dxf` says it outright. **The hierarchy is kept** — cells
stay cells and placements stay placements — which is what makes writing OASIS worth doing at all.

A GDSII `NODE` has no OASIS equivalent and no DXF one, and is reported rather than dropped in silence either
way. A round path end becomes
a square one and a label loses its justification, since OASIS has neither; those two are not reported. DXF goes
out as release 12, which every reader still opens, with the layer numbers in the layer names (`L68D20`) since
that is the only place a DXF has to put them.

KLayout's `$$$CONTEXT_INFO$$$` — the structure KLayout writes first in a GDSII file to say which cells it made from
a PCell or a library — goes out first in GDSII as it came in, and is named beside the structures counted, as
`gds validate` names it. OASIS and DXF leave it out, since neither has a place for it and written as a cell it
would be a second top to whatever reads the result; the cells it names go as the plain cells they are.

## Pipes, exit codes and scripting

A file argument of `-` reads standard input, so the dump round-trips through a pipe:

```bash
gds dump cell.gds | gds build - -o roundtripped.gds
```

Exit codes are part of the contract, because something scripting this branches on them:

| Code | Means |
|---|---|
| `0` | It did what was asked |
| `1` | The command line was wrong — a missing argument, an unknown option value |
| `2` | The file was wrong — not there, or not a layout |
| `3` | `drc` only: every rule ran, and the layout breaks some of them |
| `4` | `drc` only: some rule could not be run, or not over the whole layout, so nothing may be concluded either way |

`3` and `4` are apart on purpose, and `4` is the worse of the two. A run that reports faults knows what it
found; a run that skipped a rule does not know what it did not look at, and the fix is to the deck rather
than to the layout. `4` is returned whether or not violations were also found. The same for a layout that
stopped flattening at [the ceiling](#how-much-of-a-layout-a-command-draws), where the fix is `--most-elements`.

A missing layout is a *file* error where a missing argument is a *usage* error, so a script can tell "you
called me wrong" from "that file is broken". A file an option names is the same: a deck `drc` cannot find is `2`,
and so is a layermap `layers`, `svg`, `model`, `nets` or `gds mcp` cannot read, or a deck `gds mcp` cannot - the
first four said `1` until the 2026-10-21 audit. `nets` also returns `2` when nothing is drawn at `--at`.

Numbers are written the same on every machine. A coordinate `nets` and `drc` print is one `--at` takes back, where
on Swedish, Finnish and Norwegian machines .NET 10 wrote a negative one with U+2212 until the 2026-10-21 audit.

`gds validate <directory> -r` walks a tree and reports every file that will not parse, which is the shape of
a pre-commit check over a cell library. An OASIS file that leans on the modal variables of the cell before —
one this project's own writer wrote until 1.2.0, see the library's `OasisReader` — reads, and every command says
so on standard error in `warn` lines; `gds validate` is where to look for them.

## How much of a layout a command draws

Every command that resolves the hierarchy — `info`, `layers`, `svg`, `model`, `boolean`, `size`, `nets` and `drc`, and
the layout tools [`gds mcp`](#gds-mcp) serves — flattens at most **five million elements**, and `--most-elements <n>`
moves that. The app stops at a million and a
half, because a browser tab has two gigabytes of WebAssembly memory and an element drawn there costs about a
kilobyte of it. This is not a tab: measured on the desktop, a flattened element is about 250 bytes of managed heap and
a rule check peaks near a kilobyte an element — IHP's 1024x16 SRAM macro, 2,639,671 elements, took 2.5 GB to check
whole. Five million is also as many as `gds generate` will make, for the same reason: past it a machine runs out of
memory rather than answering, about five gigabytes at the peak of a check.

**A layout larger than that is flattened that far, and said to be.** `drc` says it in its report and exits `4`, `info`
in a note under what it counted, and the rest on standard error, so a picture written to standard output is still
only the picture — a picture, a model or a boolean of part of a layout being what the app would show under a banner.
`gds mcp`'s tools say it in their answers, beside whatever they worked out from part of a layout.
`gds bench` measures the app's own path and keeps the app's ceiling.

## `gds cells`

The library's cells: what places what, and how much is in each.

```bash
gds cells cell.gds
gds cells cell.gds --tree
```

Flat by default, indented under `--tree`. Both come from `Hierarchy.Summarize`, which the app's Cells tree is
built on too, so the two cannot come to disagree about what places what.

A cell nothing places is marked **top**. That is the answer to "which of these is the layout": the flattener
draws such a cell on its own, and a bare list of names does not say which one that is.

**KLayout's `$$$CONTEXT_INFO$$$` is not listed.** KLayout writes it first in a GDSII file to say which cells it
made from a PCell or a library, and makes no cell of it reading the file back; so it is no cell here, no top, and
nothing it names counts as placed by it. A line under the list says the file holds one, since a reader that does
make a cell of it lists one more — and so does `gds info`, under the structures.

In the file's own order rather than sorted. That is the order the cells were written, which in a library built
by a tool usually means leaves first and the thing you actually want last — and sorting it would hide that.

**A cell placed by two different parents appears under both** in `--tree`, and is marked `(again)` the second
time. That is where this parts company with a folder tree: a directory is in one place, and a GDS cell is
genuinely shared, so showing it once would mean picking a parent to call the real one. Placed twice by *one*
parent is one row — the placement count is already on it, and two identical lines would say it worse.

## `gds nets`

Everything electrically joined to the shape at a point.

```bash
gds nets cell.gds --layermap sky130.csv --at -700,850
```

```
Traced from li1 (67/20) at -700,850.

layer               shapes
licon1 (66/44)           1
li1 (67/20)              1
mcon (67/44)             1
met1 (68/20)             1

4 shape(s) across 4 layer(s).
Named source.
```

**It needs a layermap and says so plainly when it has none.** Nothing in a layout file records which of its
numbers carry a net and which join what they overlap — that is the `role` column, and it is the one thing no
PDK table already carries. Without it no layer takes part, and the honest answer is that the question cannot
be asked yet rather than that the wire connects to nothing. Those are different answers and would otherwise
read identically.

Counted per layer, because "forty shapes" says less than which layers they are on: a net that climbs to met3
and one that stops at li1 are different answers to the same question. The labels sitting on it are reported
too — more than one distinct name is worth seeing rather than hiding, since it is either two spellings of one
thing or two nets shorted together.

A via joins the conductor under it and the one over it, as the layermap's heights place them, and the bundled
sky130 map gives diffusion no role - so a trace stops at the contact down to a source or a drain rather than
crossing the transistor to the other one. See [Tracing a net](DOCUMENTATION.md#tracing-a-net).

`--at` is a point in whole database units, which is what a coordinate in these formats is. A fractional one is
somebody thinking in microns, and rounding it silently would trace from a neighboring shape and answer a
different question. `gds info <file>` prints the extent of what is drawn, and `gds dump` each shape's own
coordinates, which is how to find a point to aim inside.

`--shapes` lists the net by index, for feeding into something else.

**One net from one point, not every net in the file.** The library can extract every net (`Nets.All`, which
the antenna rule needs), but over a large layout that is the expensive thing, and one net is what somebody
asking about a wire wants.

## `gds drc`

Checks a layout against a deck of design rules.

```bash
gds drc cell.gds --deck sky130A.drc
```

**No deck ships with the tool, and there is no standard format for one.** A foundry supporting three tools
ships three separately maintained decks; KLayout's is a Ruby program and Magic's is entangled with its
technology file, and nothing converts between them. So the format is this tool's own, and a deck is a file
you supply — the same arrangement as a layermap, and for the same licensing reason. A starter deck for
sky130 is in [`wwwroot/resources/GDS Files/sky130A.drc`](../wwwroot/resources/GDS%20Files/sky130A.drc) and
[WRITING-A-DECK.md](../wwwroot/resources/WRITING-A-DECK.md) is the grammar, written so it can be handed to an AI along with your
PDK's rule document. [DRC.md](DRC.md) is the reasoning behind it and what the engine cannot check.

```
layer  met1 68/20
derive gate = poly and diff
rule   met1.2 space met1 140 "Met1 spacing"
```

Values are database units, which for sky130 is nanometers. `--markers` lists every violation with where it
is and which cell it is in — the cell being the column worth having, since a fault found on flattened
geometry may be one of a thousand placements and the coordinate to change is the one inside the cell.
`--rule <id>` reports one rule; the rest of the deck still runs.

**This is not signoff DRC and does not try to be.** A rule is measured in the square metric and reported as
regions unless it names a metric. A width or one-layer spacing rule that says `euclidean`, `square` or
`parallel` (`projection` is the same) is measured edge to edge instead, which is how the starter deck writes
sky130's poly.4, parallel edges only. A metric on any other check is refused, and so is a rule needing a
direction it cannot be given, such as an extension — *by name* rather than approximated, and the run then
exits `4` rather than `0`. A report that said a layout was clean while three rules never ran would be the one
genuinely dangerous thing this could produce.

**So would one that checked part of a layout**, and that also exits `4`. A layout past
[the ceiling](#how-much-of-a-layout-a-command-draws) stops flattening there, and a cell that contains itself has its
nesting cut short; the report says which above its summary, and that summary does not call the layout clean.
IHP's 1024x16 SRAM macro was the case: checked over the first 1,500,532 of its 2,639,671 elements, it gave 141
faults where the missing metal began, and nothing said so. At the ceiling here it is checked whole, clean, in a
minute and a half.

`-o <file>` also writes the violations as a **KLayout marker database**, which KLayout opens with
Tools > Marker Browser:

```bash
gds drc cell.gds --deck sky130A.drc -o faults.lyrdb
```

The `.lyrdb` format is the one part of this feature somebody else defined, and it was learned by running a
deck through KLayout and reading what came back rather than guessed. Coordinates in it are **microns**,
where everything else here is database units. There is a test that makes KLayout open what this writes and
count the items back — the same interoperability standard the GDSII and OASIS writers are held to.

A fault inside a placed cell is filed under that cell, **in the cell's own coordinates, with the reference
that places it** — the way KLayout's deep mode writes one — and a cell met in several places is told apart
by variant, `LEAF:1` and `LEAF:2`. A second test has KLayout place every item and checks each lands where
this found it. The viewer reads these files back as well: **Open**, or a drop on the drawing.

**A check that fell short says so in its report as well.** The format has no field for it, so it goes where KLayout's
marker browser shows things unasked: the report's description, at the top of the browser, ends *not fully checked*,
and a category of its own, **Not checked**, comes first in the browser's list, with an item for each sentence the
report above the summary gave — what of the layout was not checked, which rules did not run, what of the deck could
not be read — its words on the item's row. Read back in the viewer, those are what was not checked rather than
faults. See [DRC.md](DRC.md#where-the-result-is-said).

## `gds measure`

The 2D view's ruler, without the view.

```bash
gds measure cell.gds --from 0,0 --to 300,400
```

```
dx 300, dy 400
500.00 units  (0.5000 µm)
```

The same three numbers the ruler puts on screen, worked out the same way — a measurement here that disagreed
with the one in the app would be worse than none. The 300-by-400 case above is the one
`tests/jstests/viewGeometry.test.js` pins for the ruler itself and the one the CLI test uses, so the two are held to
one contract rather than to whatever each happens to compute.

**In microns as well, when the file says what a unit is.** The second half of a `UNITS` record is meters per
database unit — a nanometer in every bundled file — and a file that carries no usable one gets the units alone
plus a line saying why, rather than a figure invented for it.

Two decimals on the units because the endpoints are whole numbers and the diagonal between them is not; four
on the microns because a unit is usually a nanometer, and three would round a single-unit measurement away to
nothing.

**dy follows the file, not a picture.** The 2D view maps GDSII's upward Y onto SVG's downward Y, so what is
drawn is flipped and a point that looks higher has the smaller number. A measurement that agreed with the
picture would disagree with every coordinate in the text view and in the download, which is the worse of the
two to be wrong about.

## `gds generate` and `gds bench`

A layout of a chosen size, and the library's stages timed over one. `generate` writes the layout to a file,
for opening in the app; `bench` builds the same layout in memory and times parse, flatten, svg, merge, a rule
check and the OASIS write over it, each stage run twice with the first run shown beside the second. Given a
file, `bench` times that instead and the options below are not read.

```bash
gds generate -o big.gds --shapes 20000 --columns 4 --rows 4
gds bench --shapes 20000 --columns 4 --rows 4 --corners 8
gds bench cell.gds
gds bench cpu.gds --deck sky130A.drc --layermap sky130-roles.csv
```

**A real deck and every net**, since 2026-11-03. The rule check above is two made-up rules on one layer, which
says what the engine costs and little about a foundry's deck. `--deck <file>` times the whole deck over the layout
the way `gds drc` runs it and prints how many rules and violations; `--layermap <csv>` gives the layers the roles
a net is traced by, and times tracing every net in the file - the most a click on a wire can cost - with how many
and the largest. Neither is timed without its option.

`gds --help` lists these with the rest since the 2026-10-21 audit:

| Option | What it sets | Default |
|---|---|---|
| `--shapes <n>` | How many shapes one cell holds | `20000` |
| `--columns <n>`, `--rows <n>` | How many times that cell is arrayed, across and down | `1` each |
| `--layers <n>` | How many layer numbers the shapes are spread over, a row at a time — a count here, where `svg` and `model` take a list | `8` |
| `--corners <n>` | How many corners each shape has; four is a rectangle | `4` |

Each is a whole number of one or more. One column and one row write the shapes straight into the top cell, so
the file is as large as what it draws; anything more puts them in a leaf placed once as an `AREF`, so a file
of a few kilobytes flattens to as much as was asked for. A size past 5,000,000 elements flattened — shapes
times columns times rows — is refused rather than attempted. `generate` writes binary, so it needs `-o`. What
these have measured so far is in [How big a layout can be](DOCUMENTATION.md#how-big-a-layout-can-be-measured).

## `gds mcp`

The layouts in a folder, opened, drawn on, checked and saved by an AI model — Claude Desktop, Claude Code or any
other client of the [Model Context Protocol](https://modelcontextprotocol.io/). The client starts it and talks to
it over standard input and output, so it is not something to type; run by hand it says so on standard error and
waits.

```bash
claude mcp add gds -- gds mcp
```

That is all Claude Code needs. With no folder named, it works in the one it was started in, which is the one
Claude Code is working in. A folder of its own, and a layermap and a rule deck to load for every file it opens,
go after `mcp` — the layermap and the deck relative to wherever it is started, like every other command's:

```bash
claude mcp add gds -- gds mcp ./layouts --layermap sky130.csv --deck sky130A.drc
```

Claude Desktop takes the same command in `claude_desktop_config.json` — **Settings > Developer > Edit Config**
opens it; it is `%APPDATA%\Claude` on Windows and `~/Library/Application Support/Claude` on a Mac. Name the
folder there, since Claude Desktop does not start a server in one you would choose:

```json
{
  "mcpServers": {
    "gds": {
      "command": "gds",
      "args": ["mcp", "C:\\Users\\you\\layouts"]
    }
  }
}
```

Restart it after saving. If it cannot find `gds`, give the installed tool's full path as the command —
`"command": "C:\\Users\\you\\.dotnet\\tools\\gds.exe"` on Windows, `"/Users/you/.dotnet/tools/gds"` on a Mac
— since an app started from the desktop may not see the `PATH` a terminal does.

**The tools are the library's own.** The thirty-one the viewer's [AI assistant](DOCUMENTATION.md#ai-assistant) gives its
model — list the layers and cells, find and measure shapes, draw, put down the Generator's structures whole from
their numbers, move, copy, array, turn, align, combine, grow, round corners, make and place cells, flatten, check
the rules, undo — with the same descriptions and the same answers. Six more are for files, which the AI assistant never needed, because the viewer has a file open and a person to
save it, and four for [the layout open in the app](#the-layout-open-in-the-app-ai-settings-mcp-server):

| Tool | What it does |
|---|---|
| `list_files` | The layouts, layermaps and rule decks in the folder, and which layout is open |
| `open_file` | Opens a layout: GDSII, OASIS, DXF or SVG, told apart by what is in it |
| `new_file` | Starts an empty one with one cell, to be saved at the name it is given |
| `save_file` | Writes the open layout — `.gds`, `.oas` or `.dxf`, chosen by the name |
| `load_layermap` | Names the layers, of this file and every one opened after it |
| `load_rules` | A deck every change is then checked against, around itself |

**They flatten a layout as far as every other command does**: five million elements, where the app's AI assistant
stops at a tab's million and a half, and `--most-elements` moves it — see
[How much of a layout a command draws](#how-much-of-a-layout-a-command-draws). A tab linked by `link_tab` flattens in
the tab, under the app's. Where a flatten stops all the same, what was worked out from it says so: `check_rules`
under what it found, `open_cell` and a placement beside the extent they give, and the check after a change beside
its "nothing broken".

**The folder is a fence.** Every path the model names is checked before anything is opened, because what it
names can come from anywhere — a label in a layout, a line in a document it was asked to follow. `..` out of
the folder, an absolute path somewhere else, a link inside it that leads out, a Windows device such as `CON`:
each is refused, and the model is told why. A link that stays inside is followed. It reads layouts, layermaps
and decks there, and writes only layouts.

**Nothing is written until the model saves**, and then only in a layout format, chosen by the name. It will not
write over a file other than the open layout's own unless told to, and it will not open another file over
changes nobody saved unless told to either — the model is the only one who could save them, and there is no
screen for anybody to see them go. A save goes into a new file beside the old one and then takes its place, so
a save that fails halfway leaves the file as it was.

Nothing is kept between runs: the client stopping the server ends the session, along with anything not saved.

### The layout open in the app: AI settings, MCP server

The model can also work on a layout open in the GDS Viewer app, while you watch it change. In the app, under **Settings,
AI settings**, press **Start** in **MCP server**, and it shows a code; tell Claude "link to my GDS Viewer tab, code 4821 7356". From
then on the layout tools work on that tab's file: every change is drawn as it lands, and is a step you can undo in
the app. Four tools are for it:

| Tool | What it does |
|---|---|
| `link_tab` | Links the tab that shows this code, waiting up to 20 seconds for it |
| `unlink_tab` | Ends the link; the tools work on the folder's layout again |
| `look` | A picture of the cell, as the tab's 2D view draws it — only with a tab linked, since the browser draws it |
| `describe_tab` | What the person has open in the tab: the file, the cell, what is selected and what is on screen |

While a tab is linked, the file tools wait — the tab's file is yours, to open and save in the app — and
`list_files` still lists. **Stop** in the app ends the link, and the model's next call is told so rather than run
on the folder.

The tab reaches `gds mcp` over a WebSocket to this computer only, 127.0.0.1, on port 43707 — `--link-port` gives
another, and `--link-port 0` turns the link off and leaves out its four tools. Only the `gds mcp` the code is told
to can link the tab. One `gds mcp` per Claude window and one port, so the first one started has it; another says
so in its log and tries again when `link_tab` is called.

## What the app can do and this cannot

Named so it is not rediscovered:

- **Editing by hand.** Drawing shapes, moving them, turning, aligning, arraying, grouping into a cell, renaming
  or deleting cells, and undo. All of it is a gesture over a picture, and a command line is the wrong shape for
  it — the library's `LayoutEdit` is public, so a program can do any of it without the browser, and through
  [`gds mcp`](#gds-mcp) an AI model can.
- **Snapping.** The ruler in the app snaps its ends to a corner or an edge of what is drawn; `--from` and
  `--to` here are exactly the points given. Aim with `gds info` for the extent and `gds dump` for a shape's
  own coordinates.
