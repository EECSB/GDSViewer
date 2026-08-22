# gds

A command-line tool for **GDSII**, **OASIS** and **DXF** layout files: look inside one, check it, convert
between them, name its layers from a layermap, run booleans over them, and export an SVG or a 3D model.

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

Every command reads **all three formats**, told apart by what the file starts with rather than by what it is
called — so a `.oas` mailed as `.gds` still opens. `convert` is only needed to *write* a different one.

```bash
gds convert cell.gds -o cell.oas          # the name picks the format, or --to gds|oas|dxf
gds boolean cell.gds --op and --a 66/20 --b 65/20 --into 100/0 -o gate.gds
gds model cell.gds -o cell.glb --layers 67,68,69
gds dump cell.gds | gds build - -o roundtripped.gds
```

A path of `-` is standard input, so the record dump round-trips through a pipe. A layer is written the way
`gds layers` prints it, and in `--layers` / `--hide` a bare number means every data type on it.

## Naming the layers

Nothing in a layout file says that `65/20` is diffusion, so `--layermap <file>` takes a CSV that does — the
same file the [web viewer's](https://github.com/EECSB/GDSViewer) Import button takes:

```bash
gds layers cell.gds --write-layermap starting-point.csv   # this file's layers, as a mapping to edit
gds layers cell.gds --layermap sky130.csv                 # named, not numbered
gds svg cell.gds --layermap sky130.csv -o cell.svg        # its real colors and fills
gds model cell.gds --layermap sky130.csv -o wafer.glb     # its real process stack
```

`layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize`, everything past the third
column optional. A layer the mapping placed keeps its own height, and `--spacing` opens a gap on top of
wherever each layer already rests — nought by default, so the model comes out at the process stack the
mapping describes. Raise it to pull the layers apart and see between them.

## Two things worth knowing

**`gds model` is unusual.** A layout to STL, OBJ or GLTF from a console, with each layer at its own height and
thickness. Nothing else on NuGet does it, and the geometry is merged per layer first so the result is a
manifold solid rather than a pile of overlapping slabs a slicer refuses.

**`convert` keeps the hierarchy.** Cells stay cells and placements stay placements, which is the whole
reason to write OASIS rather than GDSII. `boolean` and `size` flatten, because the operation needs it.

Exit codes: `0` fine, `1` the command line was wrong, `2` the file was. `gds --help` has the rest, and the
full guide is [docs/CLI.md](https://github.com/EECSB/GDSViewer/blob/master/docs/CLI.md).

## License

Public domain — [The Unlicense](https://unlicense.org/). The tool bundles a few components that keep their
own permissive terms; see `THIRD-PARTY-NOTICES.md` in the package.

Source, documentation and a browser-based viewer built on the same library:
[github.com/EECSB/GDSViewer](https://github.com/EECSB/GDSViewer)
