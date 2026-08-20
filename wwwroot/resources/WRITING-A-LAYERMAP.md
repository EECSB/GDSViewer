# Writing a layermap

**A GDSII file carries only numbers.** Nothing in the format records that `65/20` is diffusion, what color
it should be drawn in, what it is *for*, or where it sits in the wafer. That all comes from a file you
supply — which is why KLayout wants a `.lyp` and Magic its technology file, and why none of them is the
same file.

This page is the whole format, and it is written to be **handed to an AI along with your PDK's layer
table**. Copy everything between the two markers below into a chat, paste or attach the table, and ask for
a layermap. Then load the result with **Import** in the Layers panel, or from the command line:

```bash
gds layers cell.gds --layermap mypdk.csv
```

**The fastest way to start one is not from a blank page.** Open your file and press **Export**: it writes
every layer pair already in the file, with whatever is currently being drawn. Filling that in means typing
over rather than typing out.

---

## ✂️ Everything below here is the prompt ✂️

You are writing a layermap for the GDS Viewer. Produce a single CSV file in exactly the format below.

### The file

One header row, then one row per layer/datatype pair:

```
layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize
65,20,diff,#e69ac5,0,120,conductor
66,20,poly,#d80000,180,180,conductor
66,44,licon1,,300,180,via
```

**Everything past the third column is optional, and the columns are positional — so a gap is a comma.**
`66,44,licon1,,300,180` gives licon1 a stack and no color of its own.

### The columns

| Column | What it is |
|---|---|
| `layer` | The GDSII layer number |
| `datatype` | The GDSII datatype. Together with `layer` this is what identifies a layer |
| `name` | What to call it — `diff`, `met1`. Shown as `diff (65/20)` |
| `color` | `#rrggbb`. Leave empty to keep the palette color |
| `height` | Where the bottom of the layer sits in the stack, in nanometers |
| `thickness` | How thick it is, in nanometers |
| `role` | What it is for. See below — this is what makes net tracing and antenna rules work |
| `fill` | A fill pattern name, for telling similar layers apart in 2D |
| `patterncolor` | The pattern's own color, if not the layer's |
| `patternsize` | How big the pattern repeats |

### Roles

The `role` column is the one worth getting right, because two features depend on it and neither can guess:

**Three values, and nothing else.** Anything other than these is not understood:

| Role | Means |
|---|---|
| `conductor` | Carries a net along itself, and joins anything of the same layer number it touches — metal, poly, diffusion |
| `via` | Joins whatever it overlaps, which is how two different conductors ever meet |
| `none` | Takes no part in connectivity. The same as leaving it empty |

Implant, well and marker layers get `none` or nothing — they are not conductors even though they are
drawn over ones that are.

**Net tracing** follows conductors through vias, so a via layer with no role stops a net dead. **Antenna
rules** need to know which layers are metal and which are gate, and are refused outright rather than
guessed at when the roles are missing.

### Units

`height` and `thickness` are in **nanometers**, regardless of the file's own database unit. They describe
the process, not the drawing.

### Rules for you, the author

1. **Every pair the PDK documents**, not only the ones you think will be used. A row costs nothing and a
   missing one shows as a bare number.
2. **Heights must stack sensibly** — a via's height should span the gap between the conductors it joins.
   The 3D view draws exactly what you write, so a wrong height is a visibly wrong wafer.
3. **Fill in `role` wherever you know it.** It is the difference between net tracing working and silently
   doing nothing.
4. **Colors are yours to choose** if the PDK does not specify them, but keep layers that are read together
   distinguishable — a stack of six blues is not a layermap.
5. Say which rows you were unsure of.

## ✂️ End of the prompt ✂️

---

## How the app takes a layermap

| Where | How |
|---|---|
| **The app**, Layers panel | **Import** opens a file picker; **Export** writes the current layers back out |
| **The app**, address bar | `?layermap=https://example.com/pdk.csv` loads one on open — see [DOCUMENTATION.md](../../docs/DOCUMENTATION.md#embedding-the-viewer) |
| **The command line** | `gds layers cell.gds --layermap mypdk.csv`, and `--write-layermap` to get one back |
| **The library** | `LayerNames.Parse(text)`, then apply it to the file's `AdditionalInformation` |

**It reads as far as it parses.** A row that cannot be read is reported by line number and the readable
rows still apply. One case is called out specially: a mapping that matches **none** of the file's layers
is reported as such, because that means the wrong technology or the columns in the wrong order rather
than an ordinary miss.

Which columns reach which command:

| Command | Uses |
|---|---|
| `gds layers` | the names |
| `gds svg` | the names, colors and fill patterns |
| `gds model` | the heights and thicknesses, so the stack is the real one |

A layer the mapping placed keeps its own height, and `--spacing` only spaces out the ones it said nothing
about — so a partial process table is worth having rather than all-or-nothing.

## Why this is a file you supply

The same reason the design rule deck is: a PDK's tables are somebody else's licensed work, and one PDK's
table does not belong compiled into a viewer that opens any GDSII file. KLayout and Magic both make it a
file the user chooses. So does this. See [DRC.md](../../docs/DRC.md#why-the-deck-has-to-be-ours) for the longer
version of the argument, and [WRITING-A-DECK.md](WRITING-A-DECK.md) for the rules half.
