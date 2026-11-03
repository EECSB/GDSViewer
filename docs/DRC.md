# Design rule checking

All eight phases are built. What follows is the design and the reasoning behind it, kept current with the code.

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
- [Reading KLayout's reports](#reading-klayouts-reports)

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

**One unit covers one rounding, and there were two.** The shrink was rounded to whole units before the
grow-back began, so the grow-back offset edges the first rounding had turned — and on a short slanted edge,
seventy units out, that is more than a unit. The assistant found it once it put curves on sky130's grid: a
rectangle's corners rounded to 0.3 µm came back two units short at one corner in four, and a wire's round end
grew a gap at its tip, both reported as rule violations on a shape nowhere near either limit. Both grows now run
in one pass with the outline between them kept to a hundredth of a unit, rounded once at the end
(`Booleans.Grow(shapes, by, thenBy)`), which is the one rounding the one unit is there for.

On the ten bundled designs that took **2 to 10% less time** — one union fewer per check — and changed findings
two ways. The GPIO cell lost 19 violations, all false: 11 `met1.2` and 6 `met2.2`, each a sliver a third of a
unit thick along a 45° edge between metal measured 140 to 159 nm apart, and 2 `met3.1` slivers about a unit
thick. And the 59 by 59 corner gap that the
sizing engine's square metric finds in `sky130_fd_sc_hd__a22o_1` is reported in all 41 places the 4004 CPU uses
that cell rather than in 34 of them — the same cell now says the same thing wherever it is placed, which put
eleven more `li1.2` on four designs.

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
any GDSII file. KLayout and Magic both make it a file the user chooses. So does this, for every PDK but the
built-in ones.

The deck at [`wwwroot/resources/GDS Files/sky130A.drc`](../wwwroot/resources/GDS%20Files/sky130A.drc) is the
built-in sky130 PDK's rules, transcribed here from sky130's public rule documentation, and
[`sky130-roles.csv`](../wwwroot/resources/GDS%20Files/sky130-roles.csv) beside it is its layers. Since 2026-11-02
[`gf180mcuD.drc`](../wwwroot/resources/GDS%20Files/gf180mcuD.drc) and
[`gf180mcu-roles.csv`](../wwwroot/resources/GDS%20Files/gf180mcu-roles.csv) are the same for the built-in gf180mcu:
56 rules transcribed from the GF180MCU design manual's tables at their 3.3 V values, for the five-metal stack whose
top metal, Metal5, keeps the MetalTop rules - and [`sg13g2.drc`](../wwwroot/resources/GDS%20Files/sg13g2.drc) and
[`ihp-sg13g2-roles.csv`](../wwwroot/resources/GDS%20Files/ihp-sg13g2-roles.csv) for the built-in ihp-sg13g2: 68
rules from the SG13G2 layout rules, each held by `DrcDeckTests` to the value in IHP's own rule tables. The app fetches
them at run time and lays a PDK's over every file drawn
in it while it is the PDK in use; none is something the build links. Any other PDK's rules and layers are files the
user brings to the PDK manager.

**The gf180mcu deck was run over GlobalFoundries' own cells before it shipped**, and that changed it. CO.3 and CO.4,
the contact enclosures, failed three of the 229 standard cells - by a sliver at an inside corner of COMP or poly,
where the contact's corner is 71.6 nm from the notch and a 70 nm rule measured as distance passes. An enclosure here
grows its shape as a square, the square's corner reaches into the notch, and the edge engine that measures as distance
does not measure enclosures. So the two are in the deck's list of rules not run, with that reason, and `DrcRunTests`
holds the deck to finding nothing in any of the 229.

**The ihp-sg13g2 deck was run the same way, over IHP's 84 cells, and it changed twice.** Cnt.c and Cnt.d, the same
two enclosures, reported seventeen cells at inside corners where every contact is enclosed by 70 nm or more measured
as distance, and left the deck as CO.3 and CO.4 did. **And the layers SG13G2 draws at 45 degrees** - Activ, GatPoly
and the metals, whose cells turn their poly in 5 nm stairs - had their widths and spaces taken off squares: measured
as squares, sg13g2_tielo's two poly pieces, 182 nm apart, read 129, and a Tiny Tapeout tile of the same cells reported
587 faults. They measure `euclidean`, as IHP's own rules do, so a diagonal line is as far away as it is and two
corners approaching each other diagonally count too. The rest of the deck - wells, implants, vias, MIM - stays on
squares, which IHP holds to right angles or plain shapes. The nine were `parallel`, between facing edges only, while
the edge engine both metrics run on reported faults that were not there where a layout's shapes overlap along a line;
since it was fixed on 2026-11-02 the two agree over IHP's cells and designs, and with KLayout - see
[What IHP's layouts showed](#what-ihps-layouts-showed).

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
| Report database in, placed on the layout | `DrcReport.Read` | Done |
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

`DensityOf` is unchanged and still answers its own question, though only the tests ask it — `gds layers --area`
works the same figure out in place. What a rule runs through is `DensityWindows`, which sweeps; `DensityRange`
gives the emptiest and the fullest window of the same sweep. **Only windows that fit entirely inside the
layer's own extent are measured**: one hanging over the edge is measuring the empty ground beyond it, the
corner ones hang over twice, and a sweep without that reports every layout ever drawn as too sparse at its
own boundary. A test on two solid blocks side by side caught it.

## The deck format

**To actually write one, see [WRITING-A-DECK.md](../wwwroot/resources/WRITING-A-DECK.md)** - the same grammar as a reference you
can hand to an AI along with a PDK document. What follows is why it has this shape.

Line-based, `#` comments on lines of their own, read as far as it parses — the same shape and the same
failure behavior as [`LayerNames`](../GdsII/Model/LayerNames.cs), which is deliberate: a user who has already
written a layermap for this app should recognize the file.

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
run. A fixed vocabulary of checks means an unsupported rule is **refused by name** when the deck is read,
listed apart from lines that did not parse, which is the behavior that keeps a clean report honest.

## The checks

| Check | Written as | Notes |
|---|---|---|
| `width A n` | `A NOT open(A, n/2)` | Opening: shrink then grow. What vanished was too narrow |
| `space A n` | `close(A, n/2) NOT A` | Closing: grow then shrink. What filled in was too close |
| `space A B n` | `(Grow(A, n/2) AND Grow(B, n/2)) NOT (A OR B)` | Two-layer form, measured in the ground outside both |
| `notch A n` | the gaps `space A n` finds, kept where they touch only one merged piece | Internal spacing within one merged shape |
| `enclosure A B n` | `Grow(A, n) NOT B` | Non-empty means B does not enclose A by n |
| ~~`extension A B n`~~ | — | **Removed.** It was `enclosure` with the arguments swapped, and omnidirectional, so it reported the sides of a channel for a rule about its ends. Refused by name |
| `area A n` | `MergeToRings`, then `AreaOf` per outline less its holes | Merged first, or overlap counts twice |
| `holearea A n` | the same, over each ring's `Holes` | The documentation states hole-area rules separately, and `MergeToRings` already returns holes separately |
| `offgrid * n` | coordinate mod `n` | The deck states the manufacturing grid. It cannot be recovered — see below |
| `density A n window w step s` | a window stepped across the layer's own extent, clipped to the merged layer | Tenths of a percent, so 300 is 30%. Only windows that fit inside that extent — one hanging over the edge fails every layout ever drawn |
| `antenna A B n` | per-net metal area over the gate area it reaches | Needs layer roles. Without them nothing is connected and the rule is **refused**, not passed |
| `except <layer>` | `violations NOT layer` | A modifier, not a check. Several sky130 rules are exempted inside a marker region, and that is one more boolean |

**Every one of these is a call into code that already exists.** That is the whole argument for doing it
this way first.

**The off-grid rule states its grid, and an earlier draft of this plan had that wrong.** It said the grid
would come from `Grid.Of` rather than from the deck, which reads well and cannot work: `Grid.Of` returns the
*greatest common divisor* of every coordinate in the library, so a single coordinate at 3 among a file of
multiples of 5 drags the answer to 1 — and nothing is ever off a grid of 1. The stray coordinate defines
away the grid that was supposed to catch it, and the check could never fire. `tests/GDSViewer.Tests/GridTests.cs` already
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

**And the edges are straightened before they are walked**, because a merge is not always the boundary drawn
the simplest way — IHP's SG13G2 showed it twice. Clipper keeps a corner wherever a corner of a shape that went
into the merge lands on a straight side: sg13g2_and2_2 draws its NWell as three overlapping rectangles, and
the merge came back as one rectangle with its top and bottom in pieces, the piece away from a corner measured
against the side across it as a 480 nm width. And merging an outline that is already keyholed does not always
take the keyhole out: a 60 GHz amplifier's Metal3, a frame with two holes whose tops lie on one line, came
back with 14.5 µm of channel still in it, a slit with metal on both sides whose end read as a wedge. So the
edges on each line are added up along it, one direction counting for and the other against: pieces of one
side join into one edge, a slit's two sides cancel, and a line with nothing to join or cancel keeps its edges
as and where they were.

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
| poly width 500, Euclidean | 6 | **6** |
| poly width 2000, Euclidean | 7 | **7** |

Exact agreement on every row: on the projection metric — the one `poly.4` needs and the reason the engine
exists — and on Euclidean, since 2026-11-02 even at limits no real rule would carry, pair for pair and
distance for distance. And since the same day a pair is reported as KLayout reports it, the part of each edge
that is too close to the other, so on the transistor the two agree on every pair's corners as well as on how
many pairs there are.

**Sharp corners are decided by angle, not by adjacency.** Excluding every pair of edges that meet stops a
plain square reporting four faults at its own corners — and loses a spike, where the two edges genuinely
close to a point and the material between them genuinely is narrower than any limit. The threshold is the
corner's own interior angle at ninety degrees, which is KLayout's default `angle_limit`; asked the same
question KLayout gives the same two answers, three on a wedge and none on a square. The wedge is reported
from its point out to where it reaches the limit rather than along its whole length.

**And the angle holds every pair, met or not**, the way KLayout's does: two edges that do not meet are a pair
only when one is within ninety degrees of facing the other squarely. Held only where edges met, it let a
corner's two sides through whenever something came between them — a 10 nm step beside the corner where two
overlapping rectangles' tops differ, every tread of IHP's 5 nm poly stairs against the risers further along
(38 width faults in sg13g2_tielo's poly alone), and on the bundled transistor the stripe's side against the
base's bottom, 460 nm straight down through poly.

**Four fixes brought the Euclidean count to KLayout's.** *Occlusion* — a pair whose ground between it is not
what the check is about. *Duplication* — two edges meet at every corner and both face whatever is
across from it, so a nearest approach landing on a corner was reported twice, which was taken for one fault
written out twice. *Angles* — the limit held at every pair, above. And *the line walked*: the ground between two
edges was tested at one point, halfway between their middles, and is now tested all along the line the distance
was measured across — see the next section for what that one point got wrong.

**The duplication was the angle limit in disguise, and is gone.** The pairs it reported twice on the transistor
were right-angled ones, which the angle limit now leaves out; where both edges of a corner genuinely face what
is across from it, KLayout reports a pair from each, its two parts in different places. Since 2026-11-02 a pair
here is those parts too, so two of them are two reports, and nothing is folded together.

```
limit              200   300   500   2000
KLayout              1     2     6      7
at first             1     3     9     12
+ occlusion          1     3     8     10
+ dedup              1     2     7      9
+ angles             1     2     5      6
+ the line walked    1     2     6      7
```

**What was left looked like a counting difference, and it was not one.** It was read as KLayout covering a
step and the stripe edge above it with one span where this took each separately. In fact every pair this
counted over KLayout was two edges at right angles, and one pair KLayout counts was missing from it: the
base's left side against the stripe's right, 400 apart along the base's top, whose single test point fell
outside the poly — a right-angled pair between the same two corners had been standing in for it. With both
fixed, the two report the same seven pairs at 2000, and the test that pinned the gap as a known limit holds
them equal.

**Where the edges are slanted, two face each other when KLayout says they do.** Two overlapping octagons from
`gds generate --shapes 4 --corners 8` — edges at 22.7 and 67.3 degrees, joined by a V-shaped neck 95 nm across —
were counted 1, 5, 19 and 31 at limits of 150, 200, 300 and 399, where KLayout counts 2, 10, 24 and 36, for two
reasons older than the fixes above. Whether two edges faced each other was decided at their midpoints, where
KLayout asks it of the part of each within the limit: a side of the V reaches across the neck to the first
octagon's bottom side, 192 nm corner to corner inside the shape, while the middle of that side lies beyond the V
side's line. And where the neck closes from both sides to two points, KLayout reports a pair from each side of
the V, where this kept one report per place. Since 2026-11-02 the facing is KLayout's own test, ported step for
step from its `euclidian_near_part_of_edge` and `var_near_part_of_edge`, rounding and all: a pair is the part of
each edge inside the other and within the limit, and is reported as those parts. The octagons count 2, 10, 24
and 36; and handed one outline, the two engines report the same pairs to the database unit under all three
metrics at every limit up to the octagons' size.

**What still differs is not the facing.** Three things, each pinned by a test in `DrcEdgeFacingTests`:

- **The merge puts a crossing a unit lower.** The octagons' sides cross at 380,247.8; Clipper cuts the fraction
  off and puts the V's corner at 380,247, where KLayout's merge rounds to 380,248. That unit tips nearly square
  pairs from one side of ninety degrees to the other: the first octagon's upper left side against the V's left
  side above is 89.8 degrees from facing on this outline and just over ninety on KLayout's, and the V's sides
  against the octagons' lower sides go the other way. So the pairs at that corner differ, though the counts, the
  octagons being symmetric, come out the same. Clipper is vendored as it ships, so the unit stays.
- **Past a shape's own size, the ground between.** At 450 KLayout counts 40 pairs and this 36: the four more
  reach from each octagon's far side to the other's near side, 405 nm across the notch above or below the neck.
  This engine wants the line between two edges to stay in the material, for a width; KLayout drops a pair only
  when a single other edge cuts across both sides of the ground between, which neither side of the V does on
  its own.
- **KLayout's spacing over a layer as drawn.** Run on the shapes as drawn rather than merged, KLayout's space
  check merges each shape with its neighbors locally, and where two drawn shapes overlap along one side it pairs
  each one's piece of that side separately — its own source says that local merge "is not necessarily giving the
  same results" as a merge of the whole layer. Asked the same rule over the layer merged first, it reports this
  engine's pairs. Its width checks merge the whole layer first and are not affected.

**Measured over IHP's and sky130's cells at two and three times every rule's limit**, where slanted pairs run to
the hundreds of thousands, against KLayout on merged layers: every pair this reports, KLayout reports, and every
pair KLayout reports passes this facing test. Euclidean at twice the limits, IHP's 84 cells give 23,492 of KLayout's
23,554 pairs and sky130's 897 files 338,640 of 342,427; at three times, 34,278 of 34,504 and 398,100 of 409,540.
Every pair short is the ground test above. Under `parallel` the two are equal at twice the limits and two pairs
apart in each set at three times. Three of IHP's designs at twice the limits say the same: the amplifier's 27,835
pairs and the bandgap's are KLayout's pair for pair, and the TIA is ten short, all of them the ground test. The
amplifier's file starts with the `$$$CONTEXT_INFO$$$` cell KLayout writes to say which cells came from a PCell; read
as a cell it was a second top, and its contacts counted twice, until it was held aside as KLayout holds it - see
[KLayout's context cell](DOCUMENTATION.md#klayouts-context-cell). At the rules' own limits nothing differs.

### What IHP's layouts showed

**Run with a metric over IHP's own layouts, which KLayout passes, the edge engine reported tens of thousands of
faults, and every one was the engine's.** The causes above are what they were. Counted rule by rule on
2026-11-02, before the fixes and after, with KLayout asked the same rules:

| | Before | After | KLayout |
|---|---|---|---|
| IHP's 84 standard cells, all 44 width and space rules of the sg13g2 deck `euclidean` | 152 in 29 cells | none | none |
| Five IHP designs, that deck's 18 rules `parallel`, as it had them then | 670, in all five | none | none |
| The same five, those 18 rules `euclidean` | 55,319 | none | none |
| sky130's 896 cells and the transistor, all 21 drawn-layer width and space rules `euclidean` | 11,307 in 590 files | 42 in 4 | the same 42 in the same 4 |

The designs are the core of a 60 GHz power amplifier, a 130 GHz TIA, a bandgap and two SRAM macros. The TIA's
51 M1.a and the bandgap's 11 were in the report this was filed with, and the amplifier's one M3.a was the
slit.

**Checked again once the facing was KLayout's**, the designs kept locally for testing and the second SRAM the
1024x16 macro: under all 44 width and space rules, `euclidean` or `parallel`, KLayout and this engine find nothing
in any of the five - with the SRAM flattened whole. That is 2,639,671 elements, KLayout's count too, where the
app's flatten stops at 1.5 million: stopped at 1,500,532, the missing Metal3 read as 138 M3.b spacings and the
missing Metal2 as 3 V2.c enclosures, before these changes and after alike, and `gds drc` did not say that it had
stopped. It says so now, and flattens further - see [Where the result is said](#where-the-result-is-said). Over all
five, one at a time, the `parallel` rules took 182 seconds before and 50 after, because a pair now asks about the
ground between it only once it is known to be too close.

**What the one test point got wrong.** A poly or metal jog has a corner diagonally across it from another and a
side running away from each, and the two sides face each other: their nearest approach is corner to corner,
straight through the jog. Halfway between the two sides' middles is somewhere else entirely — the empty ground
beside the jog — so the pair was a spacing fault, and forty-two of them in twenty-two cells were what was left
once the straightening and the angles were in. Tested along the line the distance was measured across, the
jog's own poly is in the way. That line may run along the boundary — a narrow rectangle is measured corner to
corner along its own end — but may not cross to the wrong side of it, and two edges alongside each other are
asked across the middle of the run they face as well: at either end the line can run along the foot of a third
shape standing between them, touching it and never crossing.

**What was left was not the walk.** Four M1.b in the 256x8 SRAM stayed after these fixes, where the layer reached
the check folded into keyholes and merged back with an island of Metal1 turned into a hole — the space across
that hole was the island's own 160 nm. They went with the keyholes: with every layer kept as rings, its holes as
holes, there is no keyhole to merge back, and all five designs report nothing.

**sky130's deck is untouched by any of it.** `poly.4`, its one rule on this engine, reports nothing over any
bundled cell or design, before and after.

### What the edge engine costs

**Faster than sizing, not slower.** On the 320,000-element generated layout, one width rule over 96,000
shapes: **5.2 seconds** against the sizing engine's 26.4. Sizing pays for a merge, an opening and a boolean
over every shape; this pays for one merge and then walks edges through a uniform grid sized to the limit, so
each edge looks at a handful of neighbors.

**The straightening and the walked line cost next to nothing where nothing is found, and took most of the run
away where much is.** Measured on 2026-11-02 with `gds generate --shapes 20000 --columns 4 --rows 4 --corners 8`
— 320,000 elements, 40,896 of them on the busiest layer — and one Euclidean width rule: at a limit finding
nothing, 1.7 seconds before and 1.8 after; at 150, finding 40,608 pairs, 190 seconds before and 2.9 after. The
ground between two edges used to be asked about for every pair that faced, by casting a point against every
ring on the layer; it is asked now only of a pair already known to be too close, and a point is cast only
against the rings whose box holds it.

**Deciding the facing by the parts within the limit costs nothing that shows.** On the same layout and rule,
later the same day: 1.9 seconds before and after at the limit finding nothing, and about 3 seconds either way at
150 — where it now finds 81,216 pairs rather than 40,608, a pair from each side of every neck, as KLayout does.
The parts are worked out only for edges already within ninety degrees of facing each other, and cost a handful
of multiplications each.

**`euclidean` costs what `parallel` does.** Measured on 2026-11-03 over the five IHP designs kept for testing, with
only the sg13g2 deck's 18 width and space rules and each design a `gds drc` of its own: the amplifier 0.2 seconds
either way, the TIA and the bandgap 2.0, the 256x8 SRAM about 7 and the 1024x16 between 30 and 35 - and neither
metric found anything in any of them. So nothing was traded when the deck's nine 45-degree layers went from
`parallel` to `euclidean`.

It is **not a scanline**. A real engine sweeps a line across the layout, which is the way to do this when
edges number in the millions. The grid is the same complexity for layout-shaped input — edges are spread
over the extent rather than piled up — and a great deal less code to be wrong in.

## What this engine cannot check

Named rules rather than hypotheticals, because sky130 uses three qualifiers, and two of them are now
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

So the split is two of three expressible — `poly.4` on the edge engine, `difftap.8` by an exemption — and
`poly.2` not. The bundled deck's thirty rules all parse. It carries `poly.2` as the plain spacing, without the
exclusion, and says so: it can over-report where the exclusion would have applied. A rule asking for a check this
build cannot measure would be refused when the deck is read, out loud, which is the next paragraph.

**A refused rule must be loud.** The parser rejects it by name, the report says which rules did not run,
and the summary never reads "clean" when something was skipped. This is the single most important
behavioral requirement in the plan, and it is why the deck vocabulary is fixed.

## Antenna rules

**Built.** They bound the ratio of a whole net's metal to the gate area it reaches, and they need
connectivity — which is why they are normally out of reach for anything that is not a full extraction flow.

They were reachable here because [`Nets.cs`](../GdsII/Viewing/Nets.cs) already walks a net with
`LayerRole` telling it which layers are conductors and which are vias, roles arriving through the
layermap's seventh column. What it lacked was a way to ask about *every* net at once — `Reaching` answers
about one shape somebody clicked, and `Nets.All` walks every net once, each shape asking the layout's index
what lies next to it.

**The gate a net reaches is the poly it drives.** Until 2026-10-09 the shipped sky130 map made diffusion a
conductor and let a via join everything it overlaps, so a net crossed every transistor's channel and every route
it ran under, and on a real design every net was most of the chip - every gate counted against every net. A via
reaches the layers either side of it now and diffusion takes no part; see [Tracing a
net](DOCUMENTATION.md#tracing-a-net).

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
2. **Derivation graph.** Topological sort with cycle detection, each derivation folded left to right through
   `Booleans.Combine` — not `CombineAll`, which applies one operation to many shapes where a derivation
   changes operation halfway — and cached for the run.
3. **The checks.** `CoveredAreaOf` was made single-layer in place rather than given an overload, and no check
   calls it: `area` merges through `MergeToRings` itself.
4. **Results model.** A violation carries its rule id, description, check, limit, marker outline, bounds and
   its `ElementSource` — so a marker can say which cell instance is at fault rather than only where on the
   screen it is. Not the measured value: the edge engine works out a distance for each pair and drops it at
   the marker.
5. **The CLI first.** `gds drc cell.gds --deck sky130A.drc`. Batch mode is testable under `dotnet test`
   with no browser, which is where the correctness work belongs.
6. **Then the view.** Markers drawn through the existing selection and measurement overlay, and a panel
   to run them from. **What that panel became is below.**
7. **`.lyrdb` export**, and then the real validation: run the same rule in KLayout, which is installed on
   the development machine, and compare. **Done — and what it settled is below.**
8. **`.lyrdb` import**: a report KLayout's own DRC wrote, laid over the layout and listed in the panel.
   **Done — see [Reading KLayout's reports](#reading-klayouts-reports)**, which is also where the export
   turned out to have been placing faults inside cells in the wrong place.

## The panel

**The layer panel showing something else**, rather than a second panel beside it. **Both names sit in the
heading and the one in force is lit** — press "Rules" and the rules come up, press "Layers" and they go
back. Pressing the name you are already on leaves you there, because the pair names both lists rather than
describing a swap.

It went through two worse shapes first. A third button in the toolbar said nothing about which panel it
would change, in a bar that was already crowded. A single heading that swapped its own word fixed that but
could not say a second list existed at all — it needed arrows and a tooltip to hint, and anyone who never
hovered never found out. Two words say so in the first glance. Beside the text it is the same pair, since the
rules are checked there too; in the 3D view it goes back to being a plain heading, since there is nowhere there
to show a fault.

They are the same shape — a list down the side of the drawing — and they answer different questions about the
same layout, so they are never both wanted at once. Both list the PDK in use's: what a file's layers are and the
rules it is checked against come from the PDK it is drawn in, and the PDK manager, a button in front of the two names, is
where a deck is imported, exported and changed whole. Its Export writes back the text that came in rather than
the parse printed out, because a deck round-tripped through the parser would come back without its comments, its
blank lines and any rule this build refused, and Export would quietly be a way of losing part of your own file.
The panel had an Import and an Export of its own, beside Clear and Example, until they went there on 2026-10-19.

**A bundled example arrives with the deck already loaded**, the way it arrives with its layer names —
every file in the picker is sky130, a cell or a design built from them, and the deck for them ships in the
same folder as the layermap.
Opening one and finding the rules empty sent people to fetch a file the app was already holding.

**Clear dropped it, for that file, and Example put it back**, from the row beside Import - until that row went to
the PDK manager on 2026-10-19. A file is drawn in the PDK in use or it is not, and an empty panel says which: the
file *is not drawn in* the PDK in use, or the PDK *has none yet*, with the PDK manager above to change either
(`noRulesSaid`).

**A deck loaded by hand still says so.** The one that fails on purpose, from the PDK manager's help on rules - or one imported
before decks went to the PDK manager - stays for the files after it, as a deck loaded by hand always has, and a line
where the row was says it is not the PDK's, with **Back to sky130's** to put the PDK's rules back
(`backToPdkRules`). Without Clear nothing else would take a deck that fails everywhere back off, and nothing on
screen would say why everything fails.

In a card of their own sit **DRC Check** and the **check on every edit** switch under it. The switch is with the button rather than in
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
beside it — a different attention color there would read as a second, unrelated warning about the same
drawing — and **clicking it steps through its faults**: each press frames the next, draws it solid and
heavier on top of its neighbors, counts how far through it is (`2/3`), and says under the list which fault it
is and in which cell. A rule the deck holds and this build cannot measure is listed too, and listed as
refused; left out, the panel would say the deck is smaller than it is.

**A fault can be waived** from that line: somebody has looked at it and let it stand — a pad's corner a rule
was never meant for. A waived fault is still drawn, in gray, since it was found; it is left out of the row's
count and the line over the drawing ("2 violations · 1 waived"), and a rule whose faults are all waived is
not marked broken but still steps, which is how a waiver is taken back with Unwaive. **Kept by what the fault
is**, `DrcViolation.Key` — its rule and its box — rather than by its place in the run, so a run again finds
the same faults waived, and one moved by a unit, or changed because something beside it did, is a different
fault and comes back to be looked at: the direction for a waiver to fail in. Waivers are kept with the file in
the session, through a reload, and dropped when a different file opens. **Report**, beside the Check button,
downloads the run as the `.lyrdb` `gds drc -o` writes, the waived faults carrying KLayout's own `waived` tag —
so its marker browser shows them waived too, and the report read back here comes in with them waived. And
The AI assistant is told: its checks leave out the faults waived and the rules switched off here, and say so.

**Each rule carries the eye a layer row does, and a rule switched off does not run.** It is left out of
`Drc.Check` by id — `switchedOff`, beside the deck — so it adds no marks and no count, the line only says how
many are switched off, and it is not reported as not run either: a rule put aside is not a gap in the answer
the way one that could not be measured is. By id because that is what a violation carries back and what
survives the deck being read again after an edit; two rules sharing an id go off together. Switching one with
a result on screen runs the check again rather than filtering what is there, since a rule switched back on has
nothing to show until it has been measured. The switches are kept in the session with the deck, and a
different deck loaded starts with every rule on.

**A header over the list does the same to all of them.** Its eye switches every rule on while any is off,
and every one off while none is — back to normal first, as the layer list's header does — and its × takes
every rule line out of the deck after asking, leaving the layers, derivations and comments where they were
(`DrcDeck.WithoutRules`), which is what made it different from Clear, which dropped the deck itself. Both
header controls sit over the columns they act on: the eye, the gear and the × at the end of a rule row stand
flush right in fixed columns, with the check's name the one part of a row that gives way — cut with an
ellipsis — when the sidebar is narrower than the rule.

### Where the result is said

In the view, in the drawing hint's place and its shape — one line over the drawing, appearing and going
away. It is the same kind of statement the hint is, and last in the same `@if` chain, so a result and a
drawing instruction can never be stacked on each other. Green when nothing was found, the marker orange
when something was — the orange every note over the view uses that is not an error. With every rule
switched off the line says **nothing checked**, in orange as well: "no violations" over a run that measured
nothing would be a claim about nothing, and green would say the layout passed.

**What did not run is in that line, and is in it even when nothing was found.** A count of faults reads as
an answer, and it is only an answer when every rule actually ran — so "no violations" under a rule that was
skipped would be the one sentence in the app that is not true.

**And so is what of the layout was not checked** (since 2026-11-03). A layout larger than the app draws stops
flattening at its ceiling, a million and a half elements, and a cell that contains itself has its nesting cut
short; a check over either is a check over part of the file, and what is missing can make faults where it would
have met the rest. `DrcResult.NotChecked` says which, the run is not `Complete` — so it is never `Clean` — and the
line leads its "not fully checked" with it. The Rules panel says it too, in red under DRC Check, where the counts
it qualifies are read. Before, nothing did: IHP's 1024x16 SRAM macro, 2,639,671 elements, was checked over its
first 1,500,532, and the 141 faults that came back — 138 M3.b spacings and 3 V2.c enclosures where the missing
metal began — read as the layout's own. `gds drc` says it above its summary and exits 4, as for a rule that did not
run; the command line flattens to five million rather than a million and a half, since its memory is the
machine's rather than a tab's, and checks the SRAM whole and clean — see
[`--most-elements`](CLI.md#how-much-of-a-layout-a-command-draws). So do the tools `gds mcp` serves, which run where
the command line does.

**And so does the report a run is written out as** (since 2026-11-03) — `gds drc -o` and the panel's Report alike —
which said nothing of the kind: a report of the SRAM's partial check held its 141 faults and nothing else, a list of
things to go and fix to whoever opened it in KLayout. The format has no field for a check that fell short, so it is
said the two ways KLayout's marker browser shows without being asked, which its 0.30.9 source
(`rdbMarkerBrowserDialog.cc`, `rdbMarkerBrowserPage.cc`) settles: the report's description, which the browser puts in
its choice of report at the top, ends **not fully checked**; and a category of its own, **Not checked**, comes first
— declared first and filed first, which is the order the browser lists them in until a header is clicked — with an
item for each sentence of what was not checked, which rules did not run and what of the deck could not be read, its
words on the item's row and a description over them saying these are not faults. A comment shows only on the one
item picked, and a tag only as a column, so neither was used. Read back here, the sentences come in as
`DrcReportFile.NotChecked`, said in red under the report's line as a run's own are, and not as items: an item under
that name holding text and nothing to draw is a sentence, so a rule somebody did call "Not checked" still comes back
as its faults.

The faults themselves are the markers. What the line adds is the part a marker cannot say: how many there
are, and whether the answer can be trusted.

**Beside the text it is the same line**, laid over the editor from the shell's pane, since the text view draws no
hint. There a fault is gone to rather than framed: a press on a broken rule lights the lines of the shape it is on,
its first record to its `ENDEL`. And DRC Check saves what has been typed first, so the rules are checked on what
the editor shows. See the text view's section in [DOCUMENTATION.md](DOCUMENTATION.md#the-layers-and-the-rules-beside-the-text).

## What the comparison against KLayout said

The engine agrees with KLayout, and the deck is where the remaining doubt lives.

**The format was learned rather than guessed.** A deck was run through KLayout and the report it wrote was
read. Two things would not have been guessed right: the coordinates are in **microns** where everything
else here is database units, and an item names its category *in single quotes* —
`<category>'met1.2'</category>` — while the declaration names it bare. KLayout opens what this writes;
there is a test that makes it do so and count the items back, and another that has it place every item and
compares where each lands with where this run found it.

**Counts are compared only where both engines answer in edge pairs.** The sizing engine answers in
**regions**, and one region too narrow is any number of edges facing each other, so for its checks what is
comparable is whether either engine found anything — which is the question a rule check exists to answer. The
edge engine's counts are held against KLayout's directly; see
[Against KLayout, in counts](#against-klayout-in-counts).

**The open question from phase 5 is closed.** A signed-off sky130 standard cell reported one `difftap.2`
diffusion-spacing violation, and the honest position was that only a reference tool could say whether it
was real. KLayout's own engine, on the same cell against the same limit, finds it too. So the geometry is
there and the engine is not what is wrong — what remains is the **rule**: the real `difftap.2` carries
qualifications this deck does not transcribe, and a gap between two diffusions of different types across a
well boundary is not what that rule is about. That is a question about a text file somebody edits, not
about the code.

## Reading KLayout's reports

**The other way through the same door.** `DrcReport.Read` takes a `.lyrdb` — KLayout's own DRC, or anybody's
script — and places it on the layout it was made from, and the rules panel lists it a category to a row with
its markers over the drawing. A foundry's deck is a Ruby program this app will never run (see
[Why the deck has to be ours](#why-the-deck-has-to-be-ours)), but what that program *finds* is a file anyone
can read, so the answer of a signoff-grade deck can be looked at here even though it cannot be computed here.

**Where an item lands is its cell's business, and the report says which cell.** An item is filed under a cell
and its values are in that cell's own coordinates, in microns; the cell's `<references>` carry the transform
to the top — KLayout's deep mode writes one per cell, the instance its marker browser shows the item at. That
was settled by experiment rather than read anywhere: the same three checks over the 555 timer, run deep and
flat, put **all 547 deep values on a flat one when read through their cell's reference, and 3 when read as top
coordinates** — the three filed under the top. A cell the report gives no reference for is found in the layout
instead, at the first place it is drawn, which is the marker browser's fallback too. An item in a cell neither
can place is listed and not drawn, and the summary says how many.

**A transform is KLayout's notation**: `r90 *1 10.5,-2.25`, and `m45 *1 0,0` for one that mirrors — where the
`m` names the *line* it mirrors across, so `m45` is the x axis mirrored and then turned a quarter. A reader
that took the number for a turn would put every mirrored instance somewhere else; the 555 timer has
transistors placed `m90`, `r90` and `r270`, which is what makes the deep-against-flat test catch it.

**What each value becomes.** A polygon its outline — the holes are not drawn, since the outer ring already says
where — a box its corners, an edge a line, a path its outline at its width, and a label a point with its words
kept as a note. An **edge pair**, the commonest value in a real report because it is what a width or a space
check answers in, becomes the four-sided area between its two edges, the second taken the other way round
when the first order would cross itself. Numbers and text are notes, and a kind this does not know is counted
and said rather than guessed at. Tags come through, and **`waived`** — what KLayout's marker browser sets when
somebody looks at an item and lets it stand — draws the item in gray rather than leaving it out.

**Its own small XML reader**, for the reason the SVG reader has one: everything in the library is compiled into
the app, and System.Xml is a megabyte of it. A report needs less than a drawing does — elements and their text,
no attributes — and it is read as it goes rather than built into a tree, since a chip's report runs to tens of
megabytes.

**Reading them found the writer wrong.** The export filed a fault inside a placed cell under that cell's name —
the provenance a flat checker is not supposed to have — but wrote it in the *top's* coordinates and gave the
cell no reference, so in the marker browser every fault inside a placed cell was off by that cell's own
placement. It now writes each one in its cell's coordinates with the reference that puts it back, and a cell
met in several places is told apart by variant, `LEAF:1` and `LEAF:2`, as KLayout does. A test hands KLayout
what this writes and has it place every item: with the old writer they land in the wrong places, and now each
lands on the fault this run found.

### In the panel

**The toolbar's Open, and a drop on the drawing, take a report** by its `.lyrdb` name and lay it over the file
that is open rather than opening it in that file's place. The rules panel's Import took one too, told from a deck by
what was in it, until it went to the PDK manager with the panel's row on 2026-10-19. The rules panel comes up
with the report at the top: its name, with what it is about on hover, a line counting its items — and under it in
red, when the check that wrote the report said it did not get to everything, what it did not — and **a row
per category that found something** — a report declares one for every rule its script ran, and most of a clean
deck is empty.

**Its own list rather than counts on the deck's rows.** A report's categories are whatever its script called
them, and matching them to this deck's rule ids would be a guess — one that put KLayout's count on the wrong
row would be worse than two lists.

**A row steps through its category**, the way a rule's row steps through its faults: a report is somebody
else's list of places to go and look, one after another. Each press frames the next item, draws it solid and
heavier on top of its neighbors, counts how far through it is (`3/24`), and says under the rows where it is
filed and what it says beside where it is — a waiver, a comment, a measured number.
A row's eye takes its category off the drawing, and the report's × takes all of it off. The markers are
**blue**, so they can be told from a run's orange with both on screen, which is what comparing two engines
looks like. At most ten thousand items are drawn — each marker is an element the browser lays out — and the
one being looked at always is. The markers are markup of their own beside the drawing, so none of that hands
the browser the drawing again: they used to be part of it, and an eye on the RGB mixer was a 116 to 179 ms
freeze to move forty markers — see [The cell tree, the margin and the
markers](DOCUMENTATION.md#the-cell-tree-the-margin-and-the-markers).

**Not kept in the session**, for the reason a run's result is not, and opening a different file takes it off.
An edit does not: a run's markers are this app's claim about the layout on screen, while a report is what
KLayout said about the file it was given, and that stays what it said. The report's × takes it off.

**Opening a different file takes a run's markers off now too.** They stayed: an edit dropped the result and so
did a new deck, but a different file opened under the last one's markers and the line counting them — 340 of
the 555 timer's, drawn across a transistor. The report's own test for the same thing is what showed it.

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

### One outline with thousands of holes

**An analog PLL took 190 seconds over a single area rule, and takes 0.7 now.** The layout is
`chipathon2023_PLL.gds`, drawn for GF180 and published under Apache-2.0 in ishi-kai's multi-project wafer
repository
([raw, at `ae2164f`](https://raw.githubusercontent.com/ishi-kai/ISHI-KAI_Multiple_Projects_WaferSapce-GF180-1/ae2164f4467793373d5fd351330a96c95ceb744c/gds/member_project/chipathon2023_PLL.gds)).
It is somebody else's, so it is not in this repository and does not belong in `wwwroot`: 2.8 MB, one structure,
43,563 shapes. Its Metal1 is 9,312 shapes that merge into 692 outlines, and one of those carries 2,516 holes - the
ground of an analog block is one piece of metal with everything else let into it. Over it every Metal1 and Metal2
rule in the gf180mcu deck took minutes, while every other rule took half a second.

**The holes were not the trouble; the keyholes were.** `DrcLayers` kept each layer the way `Booleans.Merge`
hands one back, the way GDSII has to hold it - every hole folded into its outline as a channel in and back out,
here one outline of 32,203 corners - and every check handed that straight back to Clipper, which had to take each
channel apart again. That is the one input it is pathologically slow on. Each channel comes back as a join that
is really a split, and to build its tree Clipper decides which half of each split lies inside the other by walking
corners of one round the whole of the other. The two halves share the channel, so corner after corner answers
"on the boundary" and the walk goes on. An instrumented copy of the library merging that one outline again made
51,385 of those tests and 893,081 walks round the ring in its first minute, each slower than the last. The library
itself took 214 seconds over that merge; asked for flat paths rather than a tree, 0.59; handed the boundary and its
2,516 holes rather than the keyhole, 0.07.

**So a layer is kept as rings now** - a boundary and its holes, wound opposite ways so the non-zero rule covers
exactly the ground they mean - and `Booleans` has a ring form of `Merge`, `Combine` and both `Grow`s, taking rings
and handing rings back. Every check makes the calls to Clipper it always made, in the order it made them, and only
the markers are keyholed, at the end. Wherever nothing a check works out has a hole in it - the layer, and every
step between it and the marker - what goes into Clipper is the same to the corner as it was, and so is everything
that comes out. Clipper itself is untouched.

| Release, in-process, nothing else running | Before | After |
|---|---|---|
| The PLL, `M1.3` alone - the area of Metal1 | 190 s | 0.7 s |
| The PLL, the whole gf180mcu deck in one run | hours | 4.5 s |
| A mesh like `DrcHoleTests`' under the area rule, 100 holes | 0.07 s | 0.04 s |
| 400 holes | 1.4 s | 0.04 s |
| 900 holes | 33 s | 0.05 s |
| 1,600 holes, the mesh the test draws | 512 s | 0.06 s |

"Hours" is not a guess: each of the 56 rules was also run on its own, nine at a time, and the Metal1 rules took
367 to 389 seconds each that way and the Metal2 ones 2,136 to 2,254 - 12,458 seconds in all against 33 for the
same 56 runs now. The seconds move with whatever else a machine is doing - the same `M1.3` run took 259 and 295
on busier runs - and the ratio does not. Nothing ordinary got slower: the sky130 deck over the ten bundled designs
took 174 seconds before and 165 after, and over the 897 sky130 cells 71 and 65.

**And the keyholes had been changing answers, not only times.** A merge of an outline that is already keyholed
does not always give its holes back: it can leave a channel in place, two holes glued together along the
channel between them or a slit left in the outline, and the checks then measured the slit as though it had been
drawn. A width check's opening shrinks a layer before growing it back, and shrunk, a slit's two sides move
apart, so a strip of material between a channel and an edge running beside it, narrower than the gap they open,
never grows back - and is reported as too narrow. Every run of the decks this app ships was held up against the
engine before the change:

| Compared before and after | The same, corner for corner | Changed |
|---|---|---|
| The PLL, the gf180mcu deck, rule by rule | 53 of 56 rules | M1.1, M2.1 and PP.1 lose 3, 3 and 6 markers |
| 229 gf180mcu standard cells, the gf180mcu deck | all 229 | - |
| 897 sky130 cells, the sky130 deck | 896 | one loses a `poly.1a` marker |
| The ten bundled designs, the sky130 deck | 4 | the other six lose 43 width markers between them |
| The designs, `sky130A-strict.drc` | none of 8 | all eight, by losing ground or covering the same ground in other pieces - and the other two used to crash |

**Nothing is found that was not found before, and nothing was lost that was a fault.** Every changed run only lost
markers, every one of them a width fault, and KLayout - which shares no code with this engine - was asked the same
rules in both its Euclidean and square metrics: it finds nothing at any of the 12 on the PLL or the 43 on the
designs. The strict deck's limits are absurd on purpose, contacts a whole micron apart, so its closings build
enormous regions full of holes, and it changed on every design. There every marker the new engine reports covers
ground the old one also reported, and the ground no longer reported - with the cell's lost `poly.1a` marker - was
checked against what the rule means rather than against another tool. A point is a width fault only if no square of
the rule's size that holds it fits inside the material, and a spacing fault only if every such square touches the
material; for each of 174,073 points sampled across the lost ground, a square that fits or touches nothing was
found, or the opening or closing worked out at that one point said it was no fault. 170,347 of them lie on the
contacts themselves, which a spacing fault never can. And on two designs, the GPIO and the 4004 CPU,
that deck used to crash the process - Clipper's recursion through the splits of a keyholed closing went three
thousand calls deep and overflowed the stack. Both finish now.

A mesh like the one `DrcHoleTests` draws shows it plainest. At 900 holes, merged back from its keyholed outline, it
came out with holes glued together in pairs and threes, so the hole area rule found 803 of them, and the edge engine
found 54 width faults where every bar is wider than the limit, each the two sides of a leftover channel lying on top
of each other.

**The library's own density sweep had the same fault.** `Measure.DensityRange` - the emptiest and the fullest
window of a layer, which the library offers and no rule runs through - merged its layer keyholed and clipped every
window out of that, and a window clipped from a keyhole can count a hole as metal. It merges into rings now too.
Over every layer of every bundled file it gives the same figures as before to the last bit - 15,121 layers of
sky130 cells, 2,496 of gf180mcu cells, 306 of the 307 in the designs and 26 of the PLL's 27 - and the two that
changed had been wrong: a window of the PLL's COMP that is 7.1% metal had come back as 82.8%, and one of the VGA
clock's psdm as 43.15% where it is 43.46%, each checked by clipping the drawn shapes with no merge at all. Its
slowest layers, the PLL's Metal1 and Metal2, went from 12.5 and 10.9 seconds to 2.9 and 2.0. The density rule over
that COMP finds the same 161 windows before and after.

**What pins it** is `DrcHoleTests`: a mesh of 1,600 holes in one outline, with every check that reads a layer
given a 20-second deadline and its count asserted, and `Measure.DensityRange` over it held to 69/169 in every
window. Broken on purpose - each layer kept as its keyholed outline again - ten of its twelve tests fail, nine by
the deadline; with the density range merged keyholed again, its own test fails and nothing else. `BooleanTests`
holds the ring operations, and holds rings keyholed to exactly what a merge would have handed back.

**To measure it again**, download the PLL into a scratch folder and run `gds drc` over it with a deck of two
lines, `layer Metal1 34/0` and `rule M1.3 area Metal1 144400 "a"`.

## Testing

**Hand-built fixtures with exact violations.** A 135 nm gap — one 5 nm grid step under a 140 nm limit, a
sky130 metal minimum — that must fail, and a 140 nm gap that must pass. A rectangle exactly at the limit,
which is where an off-by-one in the sizing shows. Two squares set corner to corner, 100 apart on each axis and
so about 141 apart Euclidean, which is where the square metric and the Euclidean one visibly part company — a
limit of 120 is broken in the one and met in the other — so the test documents the difference rather than
being surprised by it.

**Deck parser tests** mirroring `LayerNamesTests`, including the case that matters most: an unsupported
check is refused, by name, and does not silently vanish.

**Not the 897-file corpus.** Those cells are signed off against the foundry's
Calibre deck, not against this one. Asserting they come back clean would pin a number that means nothing,
and a disagreement would be evidence that the square metric differs from Euclidean — which is already
known — rather than evidence that a cell is broken. No test runs the checker over them, not even for
crashes; `gds bench` times it on a generated layout, or on the one file it is given.

**KLayout's own reports, checked in**, for the reading: one built to hold every kind of value, tag, cell and
category the format has, and the same DRC run over the 555 timer deep and flat. The deep one has to land on the
flat one once placed, what this writes has to read back exactly where it was found, and — where KLayout is
installed — KLayout has to place what this writes in the same places. `DrcReportReadingTests`, and
`klayout-report.spec` for the panel.

**And mutation-test every guard**, which is the habit this codebase has already paid for repeatedly: a
test can be green for a reason unrelated to its name.
