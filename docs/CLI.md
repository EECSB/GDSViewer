# The `gds` command line

Everything the viewer does to a file, without a browser: read it, check it, name its layers, draw it, extrude
it, combine its geometry and convert it to another format.

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
- [`gds cells`](#gds-cells)
- [`gds nets`](#gds-nets)
- [`gds measure`](#gds-measure)
- [Pipes, exit codes and scripting](#pipes-exit-codes-and-scripting)
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

`gds --help` prints the whole surface, and `gds --version` the version it reads off its own assembly.

## The commands

| Command | What it does |
|---|---|
| `gds info <file>` | Units, structures, elements, layers, unresolved references |
| `gds dump <file>` | Every record as text, one per line |
| `gds build <text>` | Reads that text back into a `.gds` |
| `gds validate <path...>` | Parses and reports; a directory is searched for layout files |
| `gds layers <file>` | Layer/datatype pairs with a count of what is on each, named if a layermap says so |
| `gds svg <file>` | The layout as a standalone SVG |
| `gds model <file>` | The layout extruded into a 3D model: `.stl`, `.obj`, `.gltf`, `.glb` |
| `gds boolean <file>` | Combines two layers into a third — `--op and\|or\|not\|xor` |
| `gds size <file>` | Moves every edge of a layer out or in — `--by`, negative to shrink |
| `gds convert <file>` | Between GDSII, OASIS and DXF, any direction |
| `gds cells <file>` | The library's cells: what places what, and what is in each |
| `gds nets <file>` | Everything joined to the shape at a point. Needs a layermap |
| `gds measure <file>` | The distance between two points, the way the 2D view's ruler reads it |
| `gds drc <file>` | Checks it against a deck of design rules. Needs `--deck` |
| `gds generate` | A layout of a chosen size, to measure against |
| `gds bench [<file>]` | Times parse, flatten, svg, merge and the OASIS write over one, with the size |

**Every command reads GDSII, OASIS and DXF already**, told apart by what the file starts with rather than by
what it is called — so a renamed file still opens, and `convert` is only needed to *write* a different format.

## Layermaps

A GDSII file carries only numbers. Nothing in the format records that `65/20` is diffusion, which is why
KLayout wants a `.lyp` and Magic its techfile — so what a layer is called, what color it is really drawn in,
what it is *for*, and where it sits in the wafer all come from a file you supply.

`--layermap <file>` takes that file. It is the same CSV the web app's **Import** button takes and the same one
`--write-layermap` hands back, so a mapping made in either place works in the other.

```
layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize
65,20,diff,#e69ac5,0,120,conductor
66,20,poly,#d80000,180,180,conductor
66,44,licon1,,300,180,via
```

Everything past the third column is optional. **The columns are positional, so a gap is a comma** —
`66,44,licon1,,300,180` gives licon1 a stack and no color of its own. A row that cannot be read is reported
by line number and the readable rows still apply; a mapping that matches *none* of the file's layers is called
out, because that means the wrong technology or the columns in the wrong order rather than an ordinary miss.

Which columns reach which command:

| Command | Uses |
|---|---|
| `gds layers` | the names — the first column becomes `diff (65/20)` |
| `gds svg` | the names, the colors, and the fill patterns |
| `gds model` | the heights and thicknesses, so the stack is the real one |

```bash
gds layers cell.gds --layermap sky130.csv
gds svg cell.gds --layermap sky130.csv -o cell.svg
gds model cell.gds --layermap sky130.csv -o wafer.glb
```

**A layer the mapping placed keeps its own height**, and `--spacing` only spaces out the ones it said nothing
about — so a partial process table is worth having rather than all-or-nothing.

`gds layers <file> --write-layermap <file>` writes the open file's layers *as* a mapping, every column filled
in with what is currently being drawn. That is the app's **Export**, and the point of it is that a layermap is
far easier to start from a real file than from a blank page: every pair is already listed, so filling in names
means typing over rather than typing out.

```bash
gds layers cell.gds --write-layermap starting-point.csv
```

A destination of `-` prints it instead, so it can be piped.

## Choosing layers

`svg` and `model` both take `--layers` and `--hide`, which is what the layer sidebar's checkboxes do in the
app. A layer is written the way `gds layers` prints it, and a bare number means every data type on it — `65`
is both `65/16` and `65/20`, where `65/20` is only the drawn geometry.

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

Y is flipped on the way out, because GDSII counts upward where SVG counts down.

## `gds model`

The same solid the 3D view builds, without a browser to build it in. Every shape is extruded to its layer's
depth and lifted to its layer's place in the stack; the format comes from the extension.

```bash
gds model cell.gds -o cell.glb
```

`--spacing <n>` sets the gap between layers, the same number the 3D view's slider shows, and `--scale <n>`
multiplies every coordinate — `--scale 0.001` turns sky130's nanometer database units into micrometers.
`.stl` is written binary unless `--ascii` is passed; `.obj` gets a `.mtl` beside it carrying the layer colors
unless `--no-mtl` is. A `.gltf` writes its geometry to a `.bin` beside it, which is how that format works;
`.glb` is the same model in one file.

Two things differ from what the browser downloads, both deliberate:

- **The model is not tipped over.** The 3D view rotates the stack 1.5 radians to look at it, which belongs to
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
```

The output's name picks the format, or `--to gds|oas|dxf` says it outright. **The hierarchy is kept** — cells
stay cells and placements stay placements — which is what makes writing OASIS worth doing at all.

What each format cannot carry is reported rather than dropped in silence: a GDSII `NODE` has no OASIS
equivalent, a round path end becomes a square one, and a label loses its justification. DXF goes out as release
12, which every reader still opens, with the layer numbers in the layer names (`L68D20`) since that is the only
place a DXF has to put them.

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
| `4` | `drc` only: some rule could not be run, so nothing may be concluded either way |

`3` and `4` are apart on purpose, and `4` is the worse of the two. A run that reports faults knows what it
found; a run that skipped a rule does not know what it did not look at, and the fix is to the deck rather
than to the layout. `4` is returned whether or not violations were also found.

A missing file is a *file* error where a missing argument is a *usage* error, deliberately, so a script can
tell "you called me wrong" from "that file is broken".

`gds validate <directory> -r` walks a tree and reports every file that will not parse, which is the shape of
a pre-commit check over a cell library.

## `gds cells`

The library's cells: what places what, and how much is in each.

```bash
gds cells cell.gds
gds cells cell.gds --tree
```

Flat by default, indented under `--tree` — the same pair of shapes the app's Cells sidebar offers, off the
same two library calls, so the two cannot come to disagree about what places what.

A cell nothing places is marked **top**. That is the answer to "which of these is the layout": the flattener
draws such a cell on its own, and a bare list of names does not say which one that is.

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
diff (65/20)             1
licon1 (66/44)           2
li1 (67/20)              2
mcon (67/44)             2
met1 (68/20)             2

9 shape(s) across 5 layer(s).
Carries 2 distinct names, which is either two spellings or a short: drain, source
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

`--at` is a point in whole database units, which is what a coordinate in these formats is. A fractional one is
somebody thinking in microns, and rounding it silently would trace from a neighboring shape and answer a
different question. `gds layers <file> --area` prints each layer's bounds, which is how to find a point to aim
inside.

`--shapes` lists the net by index, for feeding into something else.

**One net from one point, not every net in the file.** That is what the library does and it is deliberate on
its side: a full extraction over a large layout is the expensive thing it does not do, and one net is what
somebody asking about a wire wants.

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

**This is not signoff DRC and does not try to be.** It measures in the square metric and reports regions
rather than edge pairs, so a rule qualified by edge direction cannot be expressed — those are refused *by
name* rather than approximated, and the run then exits `4` rather than `0`. A report that said a layout was
clean while three rules never ran would be the one genuinely dangerous thing this could produce.

`-o <file>` also writes the violations as a **KLayout marker database**, which KLayout opens with
Tools > Marker Browser:

```bash
gds drc cell.gds --deck sky130A.drc -o faults.lyrdb
```

The `.lyrdb` format is the one part of this feature somebody else defined, and it was learned by running a
deck through KLayout and reading what came back rather than guessed. Coordinates in it are **microns**,
where everything else here is database units. There is a test that makes KLayout open what this writes and
count the items back — the same interoperability standard the GDSII and OASIS writers are held to.

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
`jstests/viewGeometry.test.js` pins for the ruler itself and the one the CLI test uses, so the two are held to
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

## What the app can do and this cannot

Named so it is not rediscovered:

- **Editing.** Drawing shapes, moving them, turning, aligning, arraying, grouping into a cell, renaming or
  deleting cells, and undo. All of it is a gesture over a picture, and a command line is the wrong shape for
  it — the library's `LayoutEdit` is public, so a program can do any of it without the browser.
- **Snapping.** The ruler in the app snaps its ends to a corner or an edge of what is drawn; `--from` and
  `--to` here are exactly the points given. Aim with `gds layers --area` for the bounds and `gds dump` for a
  shape's own coordinates.
