# gds

A command-line tool for **GDSII**, **OASIS** and **DXF** layout files: look inside one, check it, convert
between them, name its layers from a layermap, run booleans over them, and export an SVG or a 3D model - or
hand a folder of them to an AI model such as Claude to edit, over MCP.

```bash
dotnet tool install -g GdsII.Cli
gds info cell.gds
```

Cross-platform, no native dependency — the same command on Windows, macOS and Linux.

## Commands

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
| `gds convert <file>` | Between GDSII, OASIS and DXF, any direction |
| `gds cells <file>` | The library's cells: what places what, and what is in each |
| `gds nets <file>` | Everything joined to the shape at a point. Needs a layermap |
| `gds measure <file>` | The distance between two points, the way the 2D view's ruler reads it |
| `gds drc <file>` | Checks it against a deck of design rules. Needs `--deck` |
| `gds generate` | A layout of a chosen size, to measure against |
| `gds bench [<file>]` | Times parse, flatten, svg, merge, a rule check and the OASIS write over one, with the size - and a whole rule deck with `--deck`, every net with `--layermap` |
| `gds mcp [<folder>]` | Lets an AI model open, draw on, check and save the layouts in a folder |

Every command reads **all three formats, and an SVG drawing**, told apart by what the file starts with rather than by what it is
called — so a `.oas` mailed as `.gds` still opens. `convert` is only needed to *write* a different one.

```bash
gds convert cell.gds -o cell.oas          # the name picks the format, or --to gds|oas|dxf
gds boolean cell.gds --op and --a 66/20 --b 65/20 --into 100/0 -o gate.gds
gds model cell.gds -o cell.glb --layers 67,68,69
gds dump cell.gds | gds build - -o roundtripped.gds
```

A path of `-` is standard input, so the record dump round-trips through a pipe. A layer is written as numbers,
the way `gds layers` prints it without a layermap, and in `--layers` / `--hide` a bare number means every data
type on it.

## Naming the layers

Nothing in a layout file says that `65/20` is diffusion, so `--layermap <file>` takes a CSV that does — the
same file the [web viewer's](https://github.com/EECSB/GDSViewer) PDK manager imports into a PDK:

```bash
gds layers cell.gds --write-layermap starting-point.csv   # this file's layers, as a mapping to edit
gds layers cell.gds --layermap sky130.csv                 # named, not numbered
gds svg cell.gds --layermap sky130.csv -o cell.svg        # its real colors and fills
gds model cell.gds --layermap sky130.csv -o wafer.glb     # its real process stack
```

`layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize,texture,texturesize`,
everything past the third column optional, and the last two - a picture for the web app's 3D view - only
carried through. A KLayout `.lyp` is read too, for its names and fill colors. A layer the mapping placed keeps its own height, and `--spacing` opens a gap on top of
wherever each layer already rests — zero by default, so the model comes out at the process stack the
mapping describes. Raise it to pull the layers apart and see between them.

## For an AI model

`gds mcp` serves the library's layout tools to any client of the
[Model Context Protocol](https://modelcontextprotocol.io/), which starts it and talks to it over standard input
and output. For Claude Code, in the folder the layouts are in:

```bash
claude mcp add gds -- gds mcp
```

For Claude Desktop, in `claude_desktop_config.json` (**Settings > Developer > Edit Config**), naming the folder:

```json
{
  "mcpServers": {
    "gds": { "command": "gds", "args": ["mcp", "C:\\Users\\you\\layouts"] }
  }
}
```

The model gets the tools the web viewer's AI assistant edits with - list layers and cells, find and measure shapes,
draw, move, copy, array, turn, align, combine, grow, make and place cells, flatten, check a rule deck, undo - and
six for files: list, open, start and save a layout, and load a layermap or a deck. Every path it names stays
inside the folder; nothing is written until it saves, then only as `.gds`, `.oas` or `.dxf`, and never over
another file unless it is told to. `--layermap` and `--deck` after the folder are loaded for every file.

And a live link to the web viewer: its **Settings, AI settings, MCP server** shows a code, and once the model is told it, the
same tools work on the layout open in that browser tab, as you watch. The tab connects to this computer only,
on port 43707 - `--link-port` gives another, and `0` turns the link off.

## Two things worth knowing

**`gds model` is unusual.** A layout to STL, OBJ or GLTF from a console, with each layer at its own height and
thickness. Nothing else on NuGet does it, and the geometry is merged per layer first so the result is a
manifold solid rather than a pile of overlapping slabs a slicer refuses.

**`convert` keeps the hierarchy.** Cells stay cells and placements stay placements, which is the whole
reason to write OASIS rather than GDSII. `boolean` and `size` flatten, because the operation needs it.

Exit codes: `0` fine, `1` the command line was wrong, `2` the file was; `drc` adds `3` for violations found and `4`
for a check that did not complete. `gds --help` has the rest, and the
full guide is [docs/CLI.md](https://github.com/EECSB/GDSViewer/blob/master/docs/CLI.md).

## License

Public domain — [The Unlicense](https://unlicense.org/). The tool bundles a few components that keep their
own permissive terms; see `THIRD-PARTY-NOTICES.md` in the package.

Source, documentation and a browser-based viewer built on the same library:
[github.com/EECSB/GDSViewer](https://github.com/EECSB/GDSViewer)
