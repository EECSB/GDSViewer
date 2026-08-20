# Design rule checking

Phases 1 to 7 are built. What follows is the design and the reasoning behind it, kept current with the code.

- [What this would be, and what it would not](#what-this-would-be-and-what-it-would-not)
- [What a design rule check is](#what-a-design-rule-check-is)
- [How the other tools do it](#how-the-other-tools-do-it)
- [Why the deck has to be ours](#why-the-deck-has-to-be-ours)
- [What is already here](#what-is-already-here)
- [The deck format](#the-deck-format)
- [The checks](#the-checks)
- [The edge engine](#the-edge-engine)
- [What this engine cannot check](#what-this-engine-cannot-check)
- [Antenna rules](#antenna-rules)
- [Order of work](#order-of-work)
- [The panel](#the-panel)
- [What the comparison against KLayout said](#what-the-comparison-against-klayout-said)
- [What it costs](#what-it-costs)
- [Testing](#testing)

## What this would be, and what it would not

**A viewer that catches obvious mistakes, not a signoff tool.** The distinction is worth making at the top
because it decides nearly every trade-off below. Signoff DRC is a foundry contract: it runs the foundry's
own deck, on the foundry's own tool, and its output is what a tapeout is accepted against. Nothing here is
going to be that, and pretending otherwise would be worse than not having it — a clean report from a tool
that only looked at half the rules is a more dangerous artifact than no report at all.

What it would be is closer to what Magic gives you: an answer to *"is this wire too thin"* while you are
looking at the wire. That is genuinely useful, and it is reachable from what this codebase already has.

## What a design rule check is

Every engine, commercial or open, is the same two phases.

**Layer derivation.** Real rules are not written against drawn layers. A transistor gate is not a layer
anybody draws — it is `poly AND diff`, the region where polysilicon crosses diffusion. Field poly is
`poly NOT diff`. P+ diffusion is `diff AND psdm`. So a deck first computes a set of derived layers from
booleans and sizing on the drawn ones, and the rules are written against those.

**Geometric checks.** Each check is a distance relation over edges: width is two edges facing each other
across material, spacing is two edges facing each other across empty space, and enclosure, extension,
overlap and notch are the same idea with different bookkeeping. The output of a real engine is a set of
**edge pairs** — two edges and the gap between them — which is what gets drawn as a marker.

The second phase splits into two strategies, and which one is chosen is the single biggest decision in
this plan.

**Size-and-boolean.** A minimum width check is a shape shrunk by half the limit and grown back: whatever
did not survive was narrower than the limit. A minimum spacing check is the same in reverse — grow by half
the limit and shrink back, and whatever gap got filled was too narrow. Enclosure is one layer grown by the
limit with the other subtracted from it. All of this is `Booleans.Grow` and `Booleans.Combine`, which are
already written and already tested.

**Edge-pair scanline.** A sweep line over the edge set, emitting violating edge pairs directly, with a real
distance metric and filters on edge orientation. This is what KLayout has. It is exact, it is what the
qualifiers in a real rule deck are written against, and it is months of work.

**The cheap route is a legitimate metric, not a fudge.** Mitered sizing on rectilinear geometry is exactly
the Minkowski sum with a square, which is exactly the **square metric** — one of the three KLayout offers
by name, alongside Euclidean and projection. It diverges from Euclidean only on non-axis-aligned edges. So
the honest description of size-and-boolean is not "an approximation of DRC" but "DRC in the square metric,
reporting regions rather than edge pairs".

**And it does not round-trip, which was the worst bug in this feature.** Shrinking and growing by the same
amount does not return the same shape: Clipper works in integers and a mitered offset of a 45° edge lands
between them, so the corner rounds inward on the way in and inward again on the way out. One regular
octagon 400 across, shrunk by 24 and grown back by 24, comes back **359 square units smaller** — a ring
about a third of a unit thick all the way round — and `merged NOT opened` reports that ring as a width
violation.

It is invisible on rectilinear geometry, where every offset lands on an integer and the same test on a
square loses exactly nothing. That is why sky130 layouts never showed it and a generated layout of 320,000
octagons showed **188,742 violations, every one of them false**.

Found by putting the two engines on the same layout and asking which was wrong: KLayout said no violation,
the edge engine said no violation, the sizing engine said 188,742. The fix is that an opening grows back
one unit further than it shrank and clips to the shape, and a closing does the mirror. That is a
compensation rather than a cure — the round trip still loses the ring — so a test pins the loss itself, and
will say so if Clipper ever offsets exactly.

## How the other tools do it

**KLayout** is a Ruby DSL — the deck is a program, not data. `poly & diff` is a real expression producing a
real object, and `.width(0.15.um).output(...)` is a method call on it. Underneath is a scanline engine over
an integer coordinate database, with a flat mode, a **deep** mode that checks each cell once and resolves
only what crosses a cell boundary, and a tiled mode for bounding memory. Results go to a report database,
`.lyrdb`, which the GUI loads as a browsable marker list.

**Magic** puts the rules in the technology file and checks **continuously and incrementally** as you edit.
There is no separate deck and no batch run: you move a wire, and within a second the violation appears as
a hashed area. That works because Magic's database is corner-stitched tiles rather than polygons, so an
edit invalidates a small region and only that region is rechecked. The cost is expressiveness — there is
no arbitrary derived-layer algebra, and the harder rules get pushed into CIF layer generation.

**Calibre** is the signoff tool at essentially every foundry, and its deck language is **SVRF**, the
Standard Verification Rule Format. Despite the name it is proprietary to Siemens: there is no specification
to implement against, and foundry decks are usually under NDA and often encrypted.

## Why the deck has to be ours

**There is no interchange format for design rules.** A foundry supporting three tools ships three
separately maintained decks that are supposed to agree. The open PDKs ship a KLayout deck and a Magic
technology file; the commercial ones ship SVRF, ICV and Pegasus under NDA. Nothing converts between them.

So this cannot consume a PDK's deck, and the two reasons are practical rather than philosophical:
KLayout's deck is Ruby and needs a Ruby interpreter, and Magic's is entangled with CIF generation and
extraction. Both are the wrong shape to read.

**And it would not be bundled even if it could be.** That decision is already made, for layer names, and
recorded in [Known gaps](DOCUMENTATION.md#known-gaps): a PDK's tables are somebody else's licensed work in
a repository that is public domain, and one PDK's table does not belong compiled into a viewer that opens
any GDSII file. KLayout and Magic both make it a file the user chooses. So does this.

The starter deck at [`wwwroot/resources/GDS Files/sky130A.drc`](../wwwroot/resources/GDS%20Files/sky130A.drc) is
documentation the user loads, never something the build links.

## What is already here

More than expected, because the geometry that DRC needs is geometry the viewer already needed.

| Needed | Where | State |
|---|---|---|
| Booleans, two-way and n-ary | `Booleans.Combine`, `Booleans.CombineAll` | Done |
| Sizing, mitered, integer | `Booleans.Grow` | Done, and already on screen as the 2D view's "Grow by" |
| Per-layer merge, holes kept as holes | `Booleans.MergeByLayer` | Done |
| Polygon area, covered area | `Measure.AreaOf`, `Measure.CoveredAreaOf` | Done, with a caveat below |
| Manufacturing grid, recovered from the file | `Grid.Of` | Done |
| Connectivity, role-aware | `Nets.Reaching`, `LayerRole` | Done |
| Layer semantics supplied from outside the file | `LayerNames`, its seventh column | Done — the precedent this deck follows |
| A flat shape's way back to the cell that holds it | `ElementSource` | Done |
| Hit testing, hierarchy, transforms | `Picking`, `Hierarchy`, `Transform` | Done |
| Deck parser | `DrcDeck` | Done |
| Derived layers, ordered and cycle-checked | `DrcLayers` | Done |
| The checks | `DrcChecks` | Done |
| Results, and what did not run | `Drc`, `DrcResult`, `DrcViolation` | Done |
| Markers in the 2D view | `SvgWriter.Markers` | Done |
| A command | `gds drc` | Done |
| Report database out | `DrcReport` | Done |
| Density, through a sliding window | `Measure.DensityWindows`, `DrcChecks.Density` | Done |
| Antenna, per net | `Nets.All`, the runner | Done |

**Both things in `Measure` are fixed now.**

`CoveredAreaOf` called `MergeByLayer(layout.Elements)`, which merged *every* layer and then filtered to
one — the right answer at one clipping pass per layer when something walks the layers in turn, which a rule
sweep does. It merges the layer it was asked about now.

`DensityOf` measures a layer against its own bounding box, which answers "how solid is this layer overall"
— a fair question, and not the one a process asks. A real density rule is a **sliding window**: a fixed
square stepped across the layout, with the worst window reported, because polishing dishes where metal is
sparse and erodes where it is dense and both faults are local. A layer averaging 40% can still have a
hundred-micron square with nothing in it, and the average is exactly what hides that.

`DensityOf` is unchanged and still answers its own question — the properties panel asks it. What a rule
runs through is `DensityRange` and `DensityWindows`, which sweep. **Only windows that fit entirely inside
the layout are measured**: one hanging over the edge is measuring the empty ground beyond the drawing, the
corner ones hang over twice, and a sweep without that reports every layout ever drawn as too sparse at its
own boundary. A test on two solid blocks side by side caught it.

## The deck format

**To actually write one, see [WRITING-A-DECK.md](../wwwroot/resources/WRITING-A-DECK.md)** - the same grammar as a reference you
can hand to an AI along with a PDK document. What follows is why it has this shape.

Line-based, `#` comments, read as far as it parses — the same shape and the same failure behavior as
[`LayerNames`](../GdsII/LayerNames.cs), which is deliberate: a user who has already written a layermap for
this app should recognize the file.

```
layer  <name> <number>/<datatype>
derive <name> = <operand> <and|or|not|xor> <operand> ...
rule   <id> <check> <operands...> <value> "<description>"
```

Values are **database units**, which for sky130 is nanometers. Nothing is scaled on the way in, because
nothing needs to be: `Booleans` works in integers and the layout is on an integer grid.

**Why a declarative format rather than an expression language.** The temptation is to build something
closer to KLayout's, where a rule is an expression. The argument against is that the moment the deck can
express something the engine cannot check, the deck becomes a place to write a rule that silently does not
run. A fixed vocabulary of checks means an unsupported rule is a **parse error naming the rule**, which is
the behavior that keeps a clean report honest.

## The checks

| Check | Written as | Notes |
|---|---|---|
| `width A n` | `A NOT open(A, n/2)` | Opening: shrink then grow. What vanished was too narrow |
| `space A n` | `close(A, n/2) NOT A` | Closing: grow then shrink. What filled in was too close |
| `space A B n` | `Grow(A, n) AND B` | Two-layer form |
| `notch A n` | as `space A A n`, internal only | Internal spacing within one merged shape |
| `enclosure A B n` | `Grow(A, n) NOT B` | Non-empty means B does not enclose A by n |
| ~~`extension A B n`~~ | — | **Removed.** It was `enclosure` with the arguments swapped, and omnidirectional, so it reported the sides of a channel for a rule about its ends. Refused by name |
| `area A n` | `MergeByLayer`, then `AreaOf` per outline | Merged first, or overlap counts twice |
| `holearea A n` | the same, over `Outline.Holes` | The documentation states hole-area rules separately, and `MergeByLayer` already returns holes separately |
| `offgrid * n` | coordinate mod `n` | The deck states the manufacturing grid. It cannot be recovered — see below |
| `density A n window w step s` | a window stepped across the layout, clipped to the merged layer | Tenths of a percent, so 300 is 30%. Only windows that fit inside the layout — one hanging over the edge fails every layout ever drawn |
| `antenna A B n` | per-net metal area over the gate area it reaches | Needs layer roles. Without them nothing is connected and the rule is **refused**, not passed |
| `except <layer>` | `violations NOT layer` | A modifier, not a check. Several sky130 rules are exempted inside a marker region, and that is one more boolean |

**Every one of these is a call into code that already exists.** That is the whole argument for doing it
this way first.

**The off-grid rule states its grid, and an earlier draft of this plan had that wrong.** It said the grid
would come from `Grid.Of` rather than from the deck, which reads well and cannot work: `Grid.Of` returns the
*greatest common divisor* of every coordinate in the library, so a single coordinate at 3 among a file of
multiples of 5 drags the answer to 1 — and nothing is ever off a grid of 1. The stray coordinate defines
away the grid that was supposed to catch it, and the check could never fire. `tests/GridTests.cs` already
pinned that behavior before any of this was written. The manufacturing grid is PDK data like every other
number in a deck, so the deck states it: 5, on sky130.

## The edge engine

**Built**, and it is what makes `poly.4` expressible. `DrcEdges` walks the edges themselves rather than
sizing regions, so it can measure Euclidean, it is exact at every limit, and it can be told to consider only
edges that face each other.

A rule opts into it by naming a metric — `space fieldpoly 75 parallel`. Naming none keeps the rule on the
sizing engine, which is what every rule did before there was a second one. `parallel` rather than
`projection` is the word a deck writes, because that is what the rule manual says.

**Material is always to the left.** Every ring is wound so walking it keeps the shape's inside on the left —
outer rings counter-clockwise, holes clockwise. That one invariant lets an edge know which way is out
without carrying a flag, and it is why `MergeToRings` is used rather than `Merge`: a keyholed ring has the
channel's two sides on top of each other and neither knows which side the material is on.

### Against KLayout, in counts

The region checks could only ever be compared on *whether* either engine found anything, because one region
too narrow is any number of edges facing each other. Both engines answer in edge pairs now, so the numbers
themselves can be held against each other — the first time in this feature that was possible.

| Check on the bundled transistor | KLayout | Here |
|---|---|---|
| poly width 200, Euclidean | 1 | **1** |
| met1 width 400, Euclidean | 3 | **3** |
| poly width 300, Euclidean | 2 | **2** |
| poly width 2000, **projection** | 5 | **5** |
| poly width 2000, Euclidean | 7 | 9 |

Exact agreement on the projection metric — the one `poly.4` needs and the reason the engine exists — and on
Euclidean at limits a real rule would carry.

**Sharp corners are decided by angle, not by adjacency.** Excluding every pair of edges that meet stops a
plain square reporting four faults at its own corners — and loses a spike, where the two edges genuinely
close to a point and the material between them genuinely is narrower than any limit. The threshold is the
corner's own interior angle at ninety degrees, which is KLayout's default `angle_limit`; asked the same
question KLayout gives the same two answers, three on a wedge and none on a square. The wedge is reported
from its point out to where it reaches the limit rather than along its whole length.

**Two causes of the Euclidean over-report were found and fixed.** *Occlusion* — a pair whose ground between
it is not what the check is about. And *duplication* — two edges meet at every corner and both face
whatever is across from it, so a nearest approach landing on a corner was reported twice, which is one
fault written out twice.

```
limit         200   300   500   2000
KLayout         1     2     6      7
at first        1     3     9     12
+ occlusion     1     3     8     10
+ dedup         1     2     7      9
```

**What remains is a counting difference, not a measurement one** — and that was read off both engines'
output rather than guessed. At a limit of 500, KLayout reports the base's bottom edge against a single span
covering a step and the stripe edge above it; this reports it against each of those edges separately. Same
ground, same distances, coalesced there and not here. Closing it means merging pairs that continue each
other along a boundary, which changes the shape of the answer rather than its correctness. Pinned by a test
so it stays a known limit rather than a surprise.

### What the edge engine costs

**Faster than sizing, not slower.** On the 320,000-element generated layout, one width rule over 96,000
shapes: **5.2 seconds** against the sizing engine's 26.4. Sizing pays for a merge, an opening and a boolean
over every shape; this pays for one merge and then walks edges through a uniform grid sized to the limit, so
each edge looks at a handful of neighbors.

It is **not a scanline**. A real engine sweeps a line across the layout, which is the way to do this when
edges number in the millions. The grid is the same complexity for layout-shaped input — edges are spread
over the extent rather than piled up — and a great deal less code to be wrong in.

## What this engine cannot check

Named rules rather than hypotheticals, because sky130 uses three qualifiers and only one of them is now
expressible.

**`poly.4` — "Spacing of poly on field to diff, parallel edges only", 0.075 um. Expressible now**, and in
the bundled deck. It was the rule the edge engine was written for: measured any way that counts corners,
every corner approach in a cell is a violation and the real ones are buried among them. `parallel` puts it
on the edge engine, which asks which edges actually run alongside each other.

**`poly.2` — spacing that excludes certain adjacent poly pairs.** Still not expressible. The exclusion is a
predicate over the *pair* — which poly is adjacent to which — and neither engine carries that. Edge pairs
are the right shape for it, so this is now a question of naming the pairs rather than of the geometry, but
it is not done.

**`difftap.8` — enclosure "exempted inside UHVI".** This one *is* reachable — an exemption region is one
more subtraction, which is why `except` is in the table above.

So the split is two out of three not expressible and one cheap. Across a forty-rule starter deck the
expectation is roughly thirty-two exact, five over-reporting near corners where the square metric and the
Euclidean one differ, and three refused at parse time.

**A refused rule must be loud.** The parser rejects it by name, the report says which rules did not run,
and the summary never reads "clean" when something was skipped. This is the single most important
behavioral requirement in the plan, and it is why the deck vocabulary is fixed.

## Antenna rules

**Built.** They bound the ratio of a whole net's metal to the gate area it reaches, and they need
connectivity — which is why they are normally out of reach for anything that is not a full extraction flow.

They were reachable here because [`Nets.cs`](../GdsII/Nets.cs) already walks a net with `LayerRole` telling
it which layers are conductors and which are vias, roles arriving through the layermap's seventh column.
What it lacked was a way to ask about *every* net at once — `Reaching` answers about one shape somebody
clicked and rebuilds its adjacency to do it, which is right for one question and quadratic for all of them.
`Nets.All` builds it once.

**The rule is refused when no layer has a role, and that is the line the whole check hangs on.** A GDSII
file does not say which of its numbers are metal, so without roles nothing is joined to anything: every net
is a single shape, every ratio is tiny, and the rule would pass a layout it never looked at. It is reported
as a rule that did not run instead — the same treatment a check this build cannot measure gets.

A net reaching no gate is skipped rather than reported. A run of metal attached to no gate has no oxide to
damage, and dividing by its absent area would make every dangling wire the worst antenna in the file.

Writing the test for this found a real thing about the net model: **metal1 laid over poly is not connected
by touching.** Two conductors are one net when they share a layer number or when something between them is
a via, so the fixture needed a contact — which is exactly how a real stack works, and the first version of
it produced no net, no gate and no violation.

This is also the one place where being flat is an advantage. KLayout's deep mode makes per-net questions
awkward precisely because it works hard not to look at the flat layout; this app flattens by design and
keeps `ElementSource` so a flat shape can still name the cell it came from.

## Order of work

1. **Deck parser.** `layer`, `derive`, `rule`, with `LayerNames`-style partial reading and a hard refusal
   for an unsupported check.
2. **Derivation graph.** Topological sort with cycle detection, evaluated through `Booleans.CombineAll`,
   cached for the run.
3. **The checks**, plus the single-layer `CoveredAreaOf` overload they need.
4. **Results model.** A violation carries its rule id, description, marker outline, bounds, the measured
   value where there is one, and its `ElementSource` — so a marker can say which cell instance is at fault
   rather than only where on the screen it is.
5. **The CLI first.** `gds drc cell.gds --deck sky130A.drc`. Batch mode is testable under `dotnet test`
   with no browser, which is where the correctness work belongs.
6. **Then the view.** Markers drawn through the existing selection and measurement overlay, and a panel
   to run them from. **What that panel became is below.**
7. **`.lyrdb` export**, and then the real validation: run the same rule in KLayout, which is installed on
   the development machine, and compare. **Done — and what it settled is below.**

## The panel

**The layer panel showing something else**, rather than a second panel beside it. **Both names sit in the
heading and the one in force is lit** — press "Rules" and the rules come up, press "Layers" and they go
back. Pressing the name you are already on leaves you there, because the pair names both lists rather than
describing a swap.

It went through two worse shapes first. A third button in the toolbar said nothing about which panel it
would change, in a bar that was already crowded. A single heading that swapped its own word fixed that but
could not say a second list existed at all — it needed arrows and a tooltip to hint, and anyone who never
hovered never found out. Two words say so in the first glance. Outside the 2D view it goes back to being a
plain heading, since there is nothing to switch to.

They are the same shape — a list down the side of the drawing with a file loaded into it — and they answer
different questions about the same layout, so they are never both wanted at once. Import and Export keep
their places and their meanings; what they carry is the deck rather than the layermap. Export writes back
the text that came in rather than the parse printed out, because a deck round-tripped through the parser
would come back without its comments, its blank lines and any rule this build refused, and Export would
quietly be a way of losing part of your own file.

**A bundled example arrives with the deck already loaded**, the way it arrives with its layer names —
every file in the picker is a sky130 cell, and the deck for them ships in the same folder as the layermap.
Opening one and finding the rules empty sent people to fetch a file the app was already holding.

**Clear drops it, for that file.** It has to outlast a reload or Clear would read as a button that does
nothing, so the session carries the decline; but it used to outlast *everything*, and one Clear meant every
example opened afterwards arrived empty. `reArmBundledPdkData` keeps it to the file it was made on — the
same method the layermap goes through, since the two controls sit in the same place and had better mean the
same thing.

**What is left when it is empty is Example**, in the control row beside Import, and only while the list is
empty: with thirty rules on screen it would be spending width to offer a deck you already have. Pointing at
it opens **Load sky130**. Two steps on purpose, and the popup waits a third of a second before it opens —
loading replaces what is in the panel, and a pointer crossing the row on its way somewhere else should not
put a button under it. Without that delay it opened in passing and covered the rows underneath, which three
specs caught by clicking it instead of what they were aiming at.

Under a rule sit **DRC Check** and **continuous DRC check**. The switch is beside the button rather than in
a settings menu because the two are one question — when the layout gets measured — and reading the answer
should not mean opening a menu to find half of it. Turning it on runs a check straight away: the switch
claims the panel is current, and a stale result underneath it would say otherwise.

**The button stays live with the switch on.** It went disabled first, on the argument that a control with
nothing left to do should say so. There is always something left to do: a check runs on an *edit*, and
plenty worth checking is not one — a deck imported, a cell flattened, or simply wanting the marks back after
reading the message away. Taking the only manual run away because an automatic one exists left no way to ask
for the thing the panel is for.

**Without it, an edit takes the last result off rather than leaving it.** A marker is a claim about where
something is, and the moment the geometry under it moves the claim is about a layout that no longer exists.
So after any edit the result is either freshly computed or gone; it is never old.

Then the rules themselves, one row each, in the place the layers are listed: number, id, what it measures,
and the limit. A rule the last run found something under is marked in the marker's own orange with a count
beside it, and clicking it frames the view on the first fault under it — a different attention color there
would read as a second, unrelated warning about the same drawing. A rule the deck holds and this build
cannot measure is listed too, and listed as refused; left out, the panel would say the deck is smaller than
it is.

### Where the result is said

In the view, in the drawing hint's place and its shape — one line over the drawing, appearing and going
away. It is the same kind of statement the hint is, and last in the same `@if` chain, so a result and a
drawing instruction can never be stacked on each other. Green when nothing was found, the marker orange
when something was.

**What did not run is in that line, and is in it even when nothing was found.** A count of faults reads as
an answer, and it is only an answer when every rule actually ran — so "no violations" under a rule that was
skipped would be the one sentence in the app that is not true.

The faults themselves are the markers. What the line adds is the part a marker cannot say: how many there
are, and whether the answer can be trusted.

## What the comparison against KLayout said

The engine agrees with KLayout, and the deck is where the remaining doubt lives.

**The format was learned rather than guessed.** A deck was run through KLayout and the report it wrote was
read. Two things would not have been guessed right: the coordinates are in **microns** where everything
else here is database units, and an item names its category *in single quotes* —
`<category>'met1.2'</category>` — while the declaration names it bare. KLayout opens what this writes;
there is a test that makes it do so and count the items back.

**Counts are not compared, and cannot be.** KLayout answers in **edge pairs** and this answers in
**regions**: one region too narrow is any number of edges facing each other. What is comparable is whether
either engine found anything, which is the question a rule check exists to answer.

**The open question from phase 5 is closed.** A signed-off sky130 standard cell reported one `difftap.2`
diffusion-spacing violation, and the honest position was that only a reference tool could say whether it
was real. KLayout's own engine, on the same cell against the same limit, finds it too. So the geometry is
there and the engine is not what is wrong — what remains is the **rule**: the real `difftap.2` carries
qualifications this deck does not transcribe, and a gap between two diffusions of different types across a
well boundary is not what that rule is about. That is a question about a text file somebody edits, not
about the code.

## What it costs

Measured with `gds bench` over a generated layout of **320,000 elements**, checking one width rule against
the busiest layer's 96,000 shapes. **Both rows are historical**: the violations in the second are the
round-trip artifacts the sizing fix later removed, so that row describes an engine state that no longer
exists and cannot be re-measured. The nearest current invocation is
`gds bench --shapes 20000 --columns 4 --rows 4 --corners 8`, whose busiest layer holds 40,896 shapes -
the original run's exact arguments were not recorded, which is why these two shapes differ.

| | Before | After |
|---|---|---|
| A run finding nothing | 22.7 s | 21.6 s |
| The same run finding 188,742 violations | **478 s** | **23.1 s** |

**Attribution was the whole of the difference, and nearly the whole of the run.** Naming the cell a
violation came from meant every violation looking at every shape on its layer — 188,742 by 96,000 is
eighteen billion box comparisons for an answer that is always within a few hundred units of the marker.
The candidates are bucketed into a uniform grid now, so a marker only sees what is near it: the same run is
23 seconds, and the cost of attributing 188,742 faults is about a second and a half.

A uniform grid rather than anything cleverer, because layout is spread fairly evenly over its own extent —
that is what a chip is — so the case a quadtree exists for is not this case, and a grid is an array index
instead of a walk.

**What is left is the geometry**, and it is inherent to the approach rather than a defect: about 21 seconds
for a merge, an opening and a boolean over 96,000 shapes. Halving it would mean tiling the work or not
doing it flat, both of which are the hierarchical question below.

`gds bench` still runs both decks, and **the loud one finds nothing any more**: its limit sits under every
shape's bounding box, and the sliver violations it used to catch were exactly the round-trip artifacts the
sizing fix removed. Since then no instrument exercises attribution at all - a run that finds nothing never
attributes anything - so the 1.5-second cost above is pinned by nothing and would drift silently if the
grid regressed. The bench's violation leg needs a deck that real geometry actually fails.

## Testing

**Hand-built fixtures with exact violations.** A 130 nm gap on met1 that must fail `met1.2` at 140, and a
140 nm gap that must pass. A rectangle exactly at the limit, which is where an off-by-one in the sizing
shows. A 45-degree edge, which is where the square metric and the Euclidean one visibly part company, so
the test documents the difference rather than being surprised by it.

**Deck parser tests** mirroring `LayerNamesTests`, including the case that matters most: an unsupported
check is refused, by name, and does not silently vanish.

**The 897-file corpus for crashes and timing only.** Those cells are signed off against the foundry's
Calibre deck, not against this one. Asserting they come back clean would pin a number that means nothing,
and a disagreement would be evidence that the square metric differs from Euclidean — which is already
known — rather than evidence that a cell is broken.

**And mutation-test every guard**, which is the habit this codebase has already paid for repeatedly: a
test can be green for a reason unrelated to its name.
