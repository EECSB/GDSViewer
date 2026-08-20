# Writing a design rule deck

**There is no standard file to download.** Design rules have no interchange format: a foundry supporting
three tools ships three separately maintained decks that are supposed to agree, and none of them converts
to any other. KLayout's deck is a Ruby program, Magic's is entangled with its technology file, and
Calibre's SVRF is proprietary to Siemens and usually under NDA. So a deck for this viewer is a small file
you write, from the same PDK documentation a human reads.

This page is the whole format, and it is written to be **handed to an AI along with your PDK's rule
document**. Copy everything between the two markers below into a chat, attach or paste your PDK's design
rule manual and its layer table, and ask for a deck. Then load the result with **Import** in the Rules
panel, or run it from the command line:

```bash
gds drc cell.gds --deck mypdk.drc
```

A worked example ships with the app: [`sky130A.drc`](GDS%20Files/sky130A.drc), 30
rules over 21 layers. Load it from the Rules panel with **Load sky130A example** and read it beside this
page.

> **What a clean report means.** This is not a signoff tool and a clean run here is not a tapeout. It
> catches the obvious — a wire too thin, two shapes too close — against the rules you gave it. Any rule
> this build cannot measure is named in the result rather than skipped, so a report is either complete or
> says it is not. See [DRC.md](../../docs/DRC.md#what-this-engine-cannot-check) for what it does not do.

---

## ✂️ Everything below here is the prompt ✂️

You are writing a design rule deck for the GDS Viewer DRC engine. Produce a single plain-text `.drc` file
in exactly the format below. Do not invent syntax: the parser accepts only what is listed here, and a line
it cannot read is reported as a problem rather than guessed at.

### The file

Line-based. `#` starts a comment. Blank lines are ignored. Three kinds of statement:

```
layer  <name> <number>/<datatype>
derive <name> = <operand> <and|or|not|xor> <operand> ...
rule   <id> <check> <operands...> <value> [modifiers] "<description>"
```

`layer` names a drawn layer by its GDSII number and datatype. `derive` builds a new layer from ones
already named, evaluated left to right. `rule` measures something and is the only statement that produces
violations.

### Units

**Every value is in database units** — whole numbers, no decimals, no unit suffix. For sky130 that is
nanometers, so a 0.15 µm width is `150`. Nothing is scaled on the way in. Check your PDK's database unit
before converting: read it off the GDSII file's UNITS record if you are unsure.

Two exceptions:

- `density` values are **tenths of a percent**, so 30% is `300`.
- `area` and `holearea` values are in **square database units**, so 0.265 µm² on a nanometer grid is
  `265000`.

### The checks

Each takes one or two layer operands, then the limit.

| Check | Form | Means |
|---|---|---|
| `width` | `width A n` | Anything on A narrower than n |
| `space` | `space A n` | Two shapes on A closer than n |
| `space` | `space A B n` | A shape on A closer than n to one on B, measured in the ground outside both |
| `notch` | `notch A n` | A gap narrower than n *inside* one merged shape |
| `enclosure` | `enclosure A B n` | Where B fails to surround A by at least n |
| `area` | `area A n` | A shape on A smaller than n square units |
| `holearea` | `holearea A n` | A hole in A smaller than n square units |
| `density` | `density A n window w step s` | A w-by-w window, stepped s at a time, where A covers less than n tenths of a percent |
| `antenna` | `antenna A B n` | Per net, metal area on A over gate area on B exceeding ratio n. **Needs layer roles loaded, or the rule is refused** |
| `offgrid` | `offgrid * n` | Any coordinate that is not a multiple of n. `*` means every layer the deck declares |

Nothing else is a check. `extension`, `overlap`, `angle`, `length` and anything else will be **refused by
name** — parsed, listed, and reported as not run, so a report that omits them says so out loud.

### Modifiers

Optional words after the value, before the description:

| Modifier | Effect |
|---|---|
| `except <layer>` | Drop violations that fall inside that layer. Use for rules a PDK exempts inside a marker |
| `parallel` or `projection` | Measure only between edges that face each other. This is what makes a "parallel edges only" rule expressible |
| `euclidean` | True straight-line distance |
| `square` | The default. Distance measured as a square, which is what sizing gives |
| `window <n>` `step <n>` | Required by `density`, meaningless elsewhere |

### Worked example

```
# Layers, from the PDK's layer table.
layer nwell  64/20
layer diff   65/20
layer poly   66/20
layer psdm   94/20

# Derived layers. A gate is where poly crosses diffusion; field poly is the rest.
derive gate      = poly and diff
derive fieldpoly = poly not diff
derive pdiff     = diff and psdm

# Rules. The id is the PDK's own, so a violation can be looked up.
rule nwell.1    width      nwell            840   "N-well width"
rule nwell.2    space      nwell           1270   "N-well spacing"
rule poly.1a    width      poly             150   "Poly width"
rule poly.4     space      fieldpoly         75   parallel  "Poly on field to diff, parallel edges only"
rule difftap.8  enclosure  pdiff  nwell     180   "N-well enclosure of p+ diffusion"
rule grid.1     offgrid    *                  5   "Manufacturing grid"
```

### Rules for you, the author

1. **Use the PDK's own rule ids** (`poly.4`, `m1.5`). A violation is only actionable if it can be looked
   up in the document it came from.
2. **Transcribe, do not interpret.** If a rule's real meaning needs a check that is not in the table
   above, leave it out and say so — do not approximate it with a different check. An over-broad rule that
   fires on correct layout is worse than a missing one, because it teaches the user to ignore the report.
   This has happened twice to the shipped deck.
3. **State the manufacturing grid** in an `offgrid` rule. It cannot be recovered from the file.
4. **Every value in database units**, converted from whatever the PDK document uses. Show your conversion
   in a comment if it is not obvious.
5. **Comment each section** with the PDK and version you worked from.
6. Prefer fewer rules you are sure of over many you are not.

### Before you hand it over

Say plainly which rules from the source document you left out and why. A deck is trusted or it is useless,
and the user cannot check your work against a document they asked you to read for them.

## ✂️ End of the prompt ✂️

---

## How the app takes a deck

Four ways in, all reading the same format:

| Where | How |
|---|---|
| **The app**, Rules panel | **Import** opens a file picker. Any extension, any text encoding the browser reads as UTF-8, up to 4 MB |
| **The app**, Rules panel | **Load sky130A example** fetches the bundled deck. Only offered while no deck is loaded |
| **The command line** | `gds drc cell.gds --deck mypdk.drc` |
| **The library** | `DrcDeck.Parse(text)`, then `Drc.Check(deck, layout)` |

**It reads as far as it parses**, the same as a layermap and deliberately so. A line it cannot understand
does not stop the load: the rules around it still load and still run. But it is remembered, and this is
the part worth knowing:

- **Anything the parser could not read makes the whole report incomplete.** One bad line and every result
  from that deck says *"not fully checked"* — not just the rule on that line. `Complete` is
  `NotRun.Count == 0 && Problems.Count == 0`, and `Clean` requires `Complete`, so a deck with a typo in it
  can never report a layout clean.
- A rule naming a **check this build cannot measure** is treated the same way: it loads, the panel lists
  it as *not measurable*, and the report says it did not run. It is never silently dropped.
- A file with **no rules at all** is rejected outright, with a message listing what went wrong, rather
  than loading as an empty deck that would call every layout clean.

That is the whole point of the format. A count of faults is only an answer when every rule actually ran,
so the engine would rather tell you it is incomplete than let a clean report be misread. It shows up like
this from the command line:

```
1 problem(s) reading the deck:
  Line 2 starts with "this", where a deck line is layer, derive or rule.

This layout has NOT been fully checked. Nothing here says it is clean.
```

**What persists and what does not.** The deck survives a reload — it is kept in the session the way a
layermap is, so a refresh does not send you back to the file picker. The *result* does not: a run belongs
to the layout it ran against, and the file can be edited between visits, so restored markers would point
at where a fault used to be. **Export** writes back the text that came in rather than the parse printed
out, so your comments, blank lines and refused rules all survive the round trip.

## Checking what came back

Load it and run it. Two things to look at first:

- **Does it report anything on a cell you know is good?** The bundled examples are signed-off sky130
  standard cells; a deck that flags them is over-broad, not vigilant.
- **Does it say anything was refused?** The panel and the CLI both list rules that could not be measured.
  A count of faults is only an answer when every rule actually ran.

From code, the same two questions:

```csharp
var deck = DrcDeck.Parse(File.ReadAllText("mypdk.drc"));
var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

//What did not run comes first: a count of faults is only an answer when every rule actually ran.
if (!result.Complete)
    Console.WriteLine($"Not fully checked: {string.Join("; ", result.NotRun)}");

if (result.Clean)
    Console.WriteLine("No violations.");
else
    Console.WriteLine($"{result.Violations.Count} violation(s).");
```

See [NUGET.md](../../docs/NUGET.md#design-rule-checking) for the library API and
[CLI.md](../../docs/CLI.md#gds-drc) for the command.
