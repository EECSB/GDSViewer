# Writing a layermap

**A GDSII file carries only numbers.** Nothing in the format records that `65/20` is diffusion, what color
it should be drawn in, what it is *for*, or where it sits in the wafer. That all comes from a file you
supply — which is why KLayout wants a `.lyp` and Magic its technology file, and why none of them is the
same file.

This page is the whole format, and it is written to be **handed to an AI along with your PDK's layer
table**. Copy everything between the two markers below into a chat, paste or attach the table, and ask for
a layermap. Then import the result into a PDK in the app's **PDK manager** - pick the PDK's Layers in its tree
and press **Import**, or pick a PDK itself to import a whole one as a zip with its pictures - or use it from the
command line:

```bash
gds layers cell.gds --layermap mypdk.csv
```

**The fastest way to start one is not from a blank page.** `gds layers cell.gds --write-layermap mine.csv`
writes every layer pair already in the file, with whatever is currently being drawn. Filling that in means
typing over rather than typing out. In the app, **Export** with a PDK's Layers picked in the PDK manager writes
its layermap the same way - sky130's is a whole one to start from. With the PDK itself picked, it writes the whole
PDK as a zip.

---

## ✂️ Everything below here is the prompt ✂️

You are writing a layermap for the GDS Viewer. Produce a single CSV file in exactly the format below.

### The file

One header row, then one row per layer/datatype pair:

```
layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize,texture,texturesize
65,20,diff,#e69ac5,0,120
66,20,poly,#d80000,180,180,conductor
66,44,licon1,,300,180,via
```

**Everything past the third column is optional, and the columns are positional — so a gap is a comma.**
`66,44,licon1,,300,180` gives licon1 a stack and no color of its own. A name that has a comma in it goes in
double quotes: `65,20,"poly, gate"`.

### The columns

| Column | What it is |
|---|---|
| `layer` | The GDSII layer number |
| `datatype` | The GDSII datatype. Together with `layer` this is what identifies a layer |
| `name` | What to call it — `diff`, `met1`. Shown as `diff (65/20)` |
| `color` | `#rrggbb`. Leave empty to keep the palette color |
| `height` | Where the bottom of the layer sits in the stack, in database units — a nanometer in every bundled file |
| `thickness` | How thick it is, in the same units |
| `role` | What it is for. See below — this is what makes net tracing and antenna rules work |
| `fill` | A fill pattern name, for telling similar layers apart in 2D |
| `patterncolor` | The pattern's own color, if not the layer's |
| `patternsize` | How big the pattern repeats |
| `texture` | A picture to draw the layer with in 3D, by its file name beside the CSV - `copper.png`, or a path below the CSV's folder such as `textures/copper.png`. Leave it empty: a person adds pictures by hand |
| `texturesize` | How big one repeat of that picture is, in database units |

### Roles

The `role` column is the one worth getting right, because two features depend on it and neither can guess:

**Three values, and nothing else.** Anything other than these is not understood:

| Role | Means |
|---|---|
| `conductor` | Carries a net along itself, and joins anything of the same layer number it touches — metal and poly |
| `via` | Joins the conductors under it and over it, which is how two different conductors ever meet. With heights given, that is what its height range touches; without, whatever it overlaps |
| `none` | Takes no part in connectivity. The same as leaving it empty |

Implant, well and marker layers get `none` or nothing — they are not conductors even though they are
drawn over ones that are.

**Give the vias heights.** Seen from above, a contact between the first two metals also lies under every wire
routed over it, and a via with no height joins all of them - which on a real chip shorts every route to every
other. With a height and a thickness, a via reaches only the layers its range touches.

**Diffusion is best left without a role.** A transistor's diffusion is one shape with the gate across it, so as
a conductor it joins the source to the drain, and every net on a chip to its supplies. Left out, a trace stops at
the contact down to it, which is where a net leaves its wiring.

**Net tracing** follows conductors through vias, so a via layer with no role stops a net dead. **Antenna
rules** need to know which layers are metal and which are gate, and are refused outright rather than
guessed at when the roles are missing.

### Units

`height` and `thickness` are in **database units** — the file's own, which every bundled example makes a
nanometer, so for those a process table in nanometers is typed in as it stands. They describe the process, not
the drawing.

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
| **The app**, PDK manager | with a PDK's Layers picked in its tree, **Import** lays a CSV or a `.lyp` over that PDK and **Export** writes its layers out as a CSV; with the PDK itself picked, they take and write a whole PDK - its CSV, its deck and the pictures it names - as one zip, and its files chosen or dropped together come in the same way |
| **The app**, address bar | `?layermap=https://example.com/pdk.csv` loads one on open, with its pictures fetched from beside it — see [DOCUMENTATION.md](../../docs/DOCUMENTATION.md#embedding-the-viewer) |
| **The command line** | `gds layers cell.gds --layermap mypdk.csv`, and `--write-layermap` to get one back |
| **The library** | `LayerNames.Parse(text)`, then apply it to the file's `AdditionalInformation` |

**A KLayout `.lyp` is taken by all four as well**, for the names and the colors it carries - read as KLayout
reads it, groups and brightness included. It has no heights and no roles, so export it again as a CSV to add them.

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
| `gds nets` | the roles |

The textures are the app's 3D view's alone; the command line carries them through.

A layer the mapping placed keeps its own height as the place it rests, and `--spacing` only opens a gap on top
of wherever each layer already rests — so a partial process table is worth having rather than all-or-nothing.

## Putting a whole PDK together

A PDK goes into the app's PDK manager as **one zip**, which its Import brings in and its Export writes out — a
folder named for the PDK, holding:

| File | What it is | Needed |
|---|---|---|
| `<PDK> layers.csv` | the layermap this page describes, or a KLayout `.lyp` | this or the deck |
| `<PDK> rules.drc` | the rule deck — see `WRITING-A-DECK.md` | this or the layermap |
| the pictures | every file the layermap's texture column names, beside the CSV | for the 3D view |
| `README.md` | what the PDK is — see below | no, but worth it |

**The README is what the manager shows when the PDK itself is picked**, in the place of its lists, so it is the
first thing somebody meeting the PDK reads. Write it in Markdown and it is shown as a page; a `README.html`,
`README.pdf` or `README.txt` is taken too and shown as it is. A file named README comes ahead of any other
document in the zip. **Pictures** go in the zip beside the README - `![The metal stack](images/stack.svg)` shows
`images/stack.svg` from the README's own folder - as PNG, JPEG, GIF, WebP or SVG; a picture named by a web address,
or outside that folder, is not shown. Say:

1. **What the process is** — the foundry, the node, who made the PDK and under what license, and how mature it is.
2. **Its stack** — the metals and vias, local interconnect, the voltages, and the devices it offers.
3. **Its layers** — at least the drawing layers by their `layer/datatype` and what each one is for, and where the
   full table is.
4. **A few of its rules** — the minimum width and spacing of the layers most drawn on, and what the deck in the
   zip checks and leaves out.
5. **Where to read more** — links to the PDK's own documentation, which stays the authority.

The built-in sky130's is a whole one to start from: pick sky130 in the PDK manager to read it, and **Export** to
get it in a zip beside its layermap and its deck.

## Why this is a file you supply

The same reason the design rule deck is: a PDK's tables are somebody else's licensed work, and one PDK's
table does not belong compiled into a viewer that opens any GDSII file. KLayout and Magic both make it a
file the user chooses, and so does this for every PDK but the built-in sky130, whose layermap -
[`sky130-roles.csv`](GDS%20Files/sky130-roles.csv) - the app fetches at run time rather than compiling it in.
See [DRC.md](../../docs/DRC.md#why-the-deck-has-to-be-ours) for the longer version of the argument, and
[WRITING-A-DECK.md](WRITING-A-DECK.md) for the rules half.
