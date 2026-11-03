# The NuGet packages

Two come out of this repository, both public domain in their own code and each carrying the licenses of what it
bundles, which its license expression names:

| Package | What it is | Install |
|---|---|---|
| [`GdsII`](https://www.nuget.org/packages/GdsII) | The format library | `dotnet add package GdsII` |
| [`GdsII.Cli`](https://www.nuget.org/packages/GdsII.Cli) | The `gds` command | `dotnet tool install -g GdsII.Cli` |

The command-line tool has [its own guide](CLI.md). This page is about consuming the library from code, and
about how the two are built and released.

- [What the library is](#what-the-library-is)
- [Reading a file](#reading-a-file)
- [Flattening and geometry](#flattening-and-geometry)
- [Design rule checking](#design-rule-checking)
- [Comparing two layouts](#comparing-two-layouts)
- [Layers, names and the process stack](#layers-names-and-the-process-stack)
- [Creating](#creating)
- [Shapes, curves and routes](#shapes-curves-and-routes)
- [Editing](#editing)
- [Tools for an AI assistant](#tools-for-an-ai-assistant)
- [Writing](#writing)
- [What is deliberately not in it](#what-is-deliberately-not-in-it)
- [Building and releasing](#building-and-releasing)

## What the library is

Everything about the formats lives in `GdsII/`, which the web app references and is one consumer of. It reads
a file into an object model and writes one back, validates what it holds, converts to and from a text dump,
resolves the hierarchy into flat geometry, and renders that to SVG or to one bitmap a layer.

**Nothing in it touches a browser, a renderer or a UI framework.** That is the line the split is drawn on, and
it is what makes the format code usable outside a web page — a console tool, a build step, a test harness.

It targets **net10.0**, the same as everything else in the solution. It started at net8.0 to reach further as a
dependency, but .NET 8 goes out of support in November 2026 and the floor was costing a second SDK install on
every machine that opened the solution. If a net8.0 consumer turns up, the answer is to multi-target rather
than to move the floor back and carry the same problem again.

**No package dependencies.** [Clipper2](https://github.com/AngusJohnson/Clipper2) is vendored as source under
`GdsII/Clipper2/` and keeps its Boost Software License; that is the whole of what the library carries.

## Reading a file

```csharp
using GdsII;

var gds = new GDS(File.ReadAllBytes("cell.gds"));

foreach (var layer in gds.AdditionalInformation.Layers)
    Console.WriteLine($"{layer.Key}: {layer.Value.DisplayName}");
```

Four formats read into the same model, so everything downstream only ever sees one:

```csharp
var gds = GDS.FromStream(File.OpenRead("cell.gds"));      //GDSII
var oas = OasisReader.Read(File.OpenRead("cell.oas"));    //OASIS, into the same model
var dxf = DxfReader.Read(File.ReadAllBytes("plan.dxf"));  //DXF, text or binary
var svg = SvgReader.Read(File.ReadAllBytes("logo.svg"));  //SVG, a drawing outlined into shapes

//Or let the bytes decide, which is what the app and the tool both do - the one sniff they share.
var any = LayoutFile.Read(bytes);                                                    //whichever of the four it is
var read = LayoutFile.Read(stream, out LayoutFormat format, out var problems);        //and which, and what an OASIS file got wrong
bool isOasis = LayoutFile.FormatOf(firstBytes) == LayoutFormat.Oasis;
```

**An SVG is a drawing rather than a layout**, so reading one decides what a layout file would have said. Paint
is area: a fill is what its fill rule covers and a stroke is what the pen covers, joins and ends included,
both as boundaries. Each Inkscape layer is a layer - one named `68/20` is that one - and outside a layer each
color is; curves are flattened to within a database unit; and the drawing is turned over once, since SVG
counts down the page and GDSII up it. Text, images, clipping and masks are skipped rather than guessed at.

`FromStream` and `FromStreamAsync` read straight off a handle rather than holding the whole file as an array
first, which matters on a large library.

`gds.MicronsPerDatabaseUnit` is how big one database unit is, in microns, read off `UNITS` — or null when the
file does not say, whether the record is missing, zero or not a number — so that each caller picks its own
fallback rather than inheriting one: the DXF writer and the DRC report take a nanometer, the viewer a micron.

A malformed file throws where it is read rather than three views later — a boundary of too few points, a payload
that is not a whole number of values, a record claiming more bytes than remain. The same rules apply on the way
out, deliberately: a file that opens is a file that saves. The one malformation real files have been met with —
a boundary that never comes back to its first point — is repaired rather than refused: the ring is closed where
it is read, which is how KLayout reads it too.

## Flattening and geometry

```csharp
var layout = GdsFlattener.Flatten(gds);

foreach (var element in layout.Elements)
    Console.WriteLine($"{element.Layer.Key}: {element.Points.Count} points");
```

`Flatten` places every `SREF` and `AREF` with its reflection, magnification and rotation at any depth, and
outlines `PATH` centerlines into the polygons they actually occupy. Each flattened element keeps an
`ElementSource` naming where in the library it came from, which is what makes editing possible on top of a
flattened picture.

```csharp
//Where poly crosses diffusion is a transistor gate.
var poly = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(66, 20))).Select(e => e.Points).ToList();
var diff = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(65, 20))).Select(e => e.Points).ToList();

var gate = Booleans.Combine(poly, diff, BooleanOperation.And);
var grown = Booleans.Grow(gate, 50);
```

**A chain of them is better kept in rings.** Each of those answers hole-free, every hole folded into its
outline as the keyhole GDSII needs — the shape to write out, and the worst one to hand back to Clipper, which
takes every channel apart again and is pathologically slow at it on an outline with thousands of holes (see
[What it costs](DRC.md#what-it-costs)). `MergeToRings` keeps the holes apart; `Merge`, `Combine` and `Grow`
also take rings and hand rings back; and `Keyholed` folds the holes in once, at the end:

```csharp
var metal = Booleans.MergeToRings(shapes);
var opened = Booleans.Combine(Booleans.Grow(metal, -70, 71), metal, BooleanOperation.And);
var tooNarrow = Booleans.Keyholed(Booleans.Combine(metal, opened, BooleanOperation.Not));
```

`Measure` answers area and extent — drawn area counts an overlap twice where covered area merges first, which
is the difference density is computed from. `Picking.At` answers which element is under a point, and `Nets`
traces a connected net through vias.

**A flattened layout carries a spatial index**, built the first time `layout.Index` is asked for:
`Within(box)` answers which elements reach into a box, `Under(point)` which are under a point, and
`ExtentOf(layers)` how far a set of layers reaches, none of them by walking every element. `Picking` and the
SVG writer's culling both go through it, which is what keeps a design of a couple of hundred thousand shapes
interactive.

An editor that changes one shape need not flatten the whole library again.
`GdsFlattener.Reflatten(previous, gds, edit.Reshaped)` draws again only what the edit reshaped, at every
place its cell is placed, and carries the index over. It answers null whenever it cannot be sure - a
placement moved, say - and then `Flatten` is the answer, which is always right.

Corners, fills and strokes sit beside `Combine` and `Grow`:

```csharp
var chamfered = Corners.Cut(outline, size: 50);
var filleted = Corners.Round(outline, radius: 200, stepsPerQuarter: 16);

var filled = Booleans.Fill(rings, evenOdd: true);    //the area a fill rule says the rings cover
var stroked = Booleans.Stroke(runs, 100, Booleans.StrokeJoin.Round, Booleans.StrokeEnd.Butt, miterLimit: 4, tolerance: 1);
```

A corner takes at most half of each edge beside it, so a radius bigger than a shape can take becomes the
biggest it can rather than a loop. And each corner is worked where it stands rather than by shrinking the
shape and growing it back, so nothing narrower than the radius disappears.

## Design rule checking

A deck of rules is a small text file you supply — there is no standard one to download, because design
rules have no interchange format. [WRITING-A-DECK.md](../wwwroot/resources/WRITING-A-DECK.md) is the whole grammar, written so
it can be handed to an AI along with a PDK's rule document.

```csharp
var deck = DrcDeck.Parse(File.ReadAllText("sky130A.drc"));
var result = Drc.Check(deck, GdsFlattener.Flatten(gds));
```

**Read `Complete` before `Violations`.** A count of faults is only an answer when every rule actually ran,
and a deck can name checks this build cannot measure. Those are refused by name rather than skipped in
silence, which is the one behavior the whole format exists to guarantee:

```csharp
//What did not run comes first, or "no violations" is a sentence that is not true.
if (!result.Complete)
    Console.WriteLine($"Not fully checked: {string.Join("; ", result.NotRun.Concat(result.Problems))}");

if (result.Clean)
    Console.WriteLine("No violations.");
else
{
    foreach (var violation in result.Violations)
        Console.WriteLine($"{violation.RuleId}: {violation.Description} at {violation.Bounds}");
}
```

`Clean` is `Violations.Count == 0 && Complete`, so it is false when nothing was found but something did
not run. Each `DrcViolation` carries its rule id, description, which check it is and the rule's limit, the outline to
draw as a marker, its bounds, a `Key` that names the fault for a waiver, and an `ElementSource` naming the cell
instance at fault — so a report can point at the cell to edit rather than only at a coordinate.

To re-check after an edit, flatten again and call `Check` again: the layout is the input, and there is no
cached state to invalidate.

`DrcReport.Write` produces a KLayout `.lyrdb` for comparing against another tool's answer - given a set of
`DrcViolation.Key`s, the faults somebody waived carry KLayout's own `waived` tag - and `DrcReport.Read` takes one
back - KLayout's own DRC run, for instance - placed on the layout it was made from:

```csharp
var layout = GdsFlattener.Flatten(gds);
var report = DrcReport.Read(File.ReadAllBytes("faults.lyrdb"), gds, layout);

foreach (var item in report.Items)
    Console.WriteLine($"{item.Category} in {item.Cell}: {item.Markers.Count} marker(s) at {item.Bounds}");
```

Each item's markers are in the layout's database units, placed through its cell's reference the way
KLayout's marker browser places them - a cell the report gives none for is found in the layout instead.
`Categories` counts what each holds, waived items included, and `Problems` says what could not be read or
placed.

## Comparing two layouts

```csharp
var comparison = Compare.Layouts(GdsFlattener.Flatten(mine), GdsFlattener.Flatten(theirs));

if (comparison.Same)
    Console.WriteLine("The same drawing.");

foreach (var difference in comparison.Differences)
    Console.WriteLine($"{difference.Layer} {difference.Side}: {difference.Area} square units at {difference.Bounds}");
```

**What is drawn is compared, not what is written.** Each layer is merged before it is laid over the other's,
so a rectangle and the same rectangle drawn as two halves are no difference, and a shape moved a unit is two:
the strip it took (`OnlyHere`) and the strip it left (`OnlyThere`). The differences come in layer order and
down each layer, which is the order to step through them in, and `SvgWriter.Differences` draws them as
markers.

`scaleThere` brings a file in another database unit onto this one's. `ignoreUnder` leaves out slivers
narrower than that many units, for two files snapped to different grids; it is zero unless asked, since a
comparison that quietly drops a difference is the worst kind.

## Layers, names and the process stack

A GDSII file carries only numbers, so what `65/20` means comes from outside it. `LayerNames` is that:

```csharp
var mapping = LayerNames.Parse(File.ReadAllText("sky130.csv"));

foreach (string problem in mapping.Problems)
    Console.Error.WriteLine(problem);

int applied = mapping.ApplyTo(gds.AdditionalInformation.Layers);

//And back out, every column filled in with what is currently set.
File.WriteAllText("sky130.csv", LayerNames.Export(gds.AdditionalInformation));
```

The format is `layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize,texture,texturesize`,
everything past the third column optional — [the CLI guide](CLI.md#layermaps) describes it in full, and it is the same file the
web app's PDK manager imports into a PDK. `Parse` reads a KLayout `.lyp` as well, told apart by its first character, for the
names and the fill colors it carries.

**A mapping has to be followed by a restack.** `ApplyTo` writes heights onto layers; only `SetStackingOffsets`
reads them back as a stack, and it is what places the layers the table says nothing about:

```csharp
mapping.ApplyTo(gds.AdditionalInformation.Layers);        //place what the table places
gds.AdditionalInformation.SetStackingOffsets(50);          //place whatever it did not, and spread every layer 50 further apart
```

The spacing argument is a viewing control, not part of the stack. Every layer moves with it, a mapped one
included — `Layer.CustomHeight` is the height that was asked for, `Layer.Resting` is that height before the
spread, and `Layer.Offset` is where the layer actually sits. Write `Resting` back out to a layermap, never
`Offset`, or the spread is recorded as though it had been measured and compounds on the next read.

The table is built from the shapes a file draws, so it holds only the pairs that file uses. `AddLayer` puts
in one that nothing is drawn on yet — which is what makes an empty library somewhere you can start:

```csharp
gds.AdditionalInformation.AddLayer(new LayerKey(66, 44));   //false if the pair is already there
```

**GDSII has no record for a layer**, only for the shapes carrying one, so a layer added and left empty is
gone when the file is written and read back. Draw on it and it is in the file like any other.

## Creating

Shapes, labels, cells and placements are all built from code, through the same edit classes the editor uses.
There is no separate builder API and no fluent object model: a shape added by a generator and one drawn with
the mouse are the same call, which is what keeps them from drifting apart.

```csharp
//A new library with one empty cell. A database unit is a nanometer unless you say otherwise, and the
//other half of UNITS is derived from that so the two cannot disagree.
var gds = GDS.NewLibrary("AUTHORED");

var top = Hierarchy.Named(gds, "TOP")!;

//A boundary from its corners, closed for you when the last does not repeat the first.
new AddElement(gds, top, new LayerKey(68, 20), corners).Apply();

//A path down a centerline, with a width and an end style.
new AddElement(gds, top, new LayerKey(67, 20), centerline, 140, Paths.Ends.Extended).Apply();

//A label at a point.
new AddElement(gds, top, new LayerKey(68, 5), new Element.Point(500, 300), "VDD").Apply();

//A cell, and an instance of it turned 90 degrees.
new AddStructure(gds, "LEAF", contents).Apply();
new AddElement(gds, top, Hierarchy.PlacementRecords("LEAF", at, mirrored: false, angle: 90), "Place").Apply();

File.WriteAllBytes("authored.gds", gds.Serialize());
```

`AddElement` also takes a raw `List<Record>`, which is how anything the typed constructors do not cover gets
in: `Hierarchy.PlacementRecords` and `Hierarchy.AsArray` build `SREF` and `AREF` records, `Paths.Records`
builds a path's. `AddElement.CopyOf` duplicates an element **by its records rather than its outline**, so a
path copies as a path and anything carrying properties keeps them.

Every one of these is a `LayoutEdit` — `Apply`, `Revert`, `Describe` — so the section below applies to
creation as much as to change.

## Shapes, curves and routes

**A layout format has no curves.** GDSII stores polygons and polylines and nothing else, so a circle is a
many-sided polygon and the side count is a decision somebody has to make - too few and it is visibly a
hexagon, too many and every file carrying it is larger for a difference no process can hold. These take it
as an argument rather than making it quietly.

Everything here hands back **corners rather than elements**. The corners are what varies; putting one on a
layer is `AddElement` and is the same call whichever shape it was - so a shape can be measured, moved,
combined with `Booleans` or fed to another builder before it has been in a file.

```csharp
Shapes.Rectangle(centerX: 0, centerY: 0, width: 1000, height: 600);   //centered, like the rest
Shapes.Between(0, 0, 1000, 600);                                      //or corner to corner
Shapes.Circle(centerX: 0, centerY: 0, radius: 500, vertices: 128);
Shapes.Ellipse(0, 0, radiusX: 800, radiusY: 300);
Shapes.RegularPolygon(0, 0, radius: 500, sides: 6, turnDegrees: 30);
Shapes.Ring(0, 0, outerRadius: 500, innerRadius: 300);                //two loops - GDSII has no hole
```

A circle's corners sit **on** it rather than outside, so the polygon is inscribed and its edges run inside
the radius - the conservative reading where a radius was chosen to satisfy a spacing rule. To within a
database unit: a corner at 45° on a radius of 500 is at 353.553, and the nearest whole coordinate is 0.63
further out. Rounding inward would buy the stronger claim by shrinking every shape systematically.

### Bézier curves

By de Casteljau rather than through the NURBS evaluator in `DxfCurves` - a Bézier is a NURBS with a clamped
uniform knot vector, and a caller placing four control points should not have to know what a knot is. The
curve passes through its first and last control point and no others; the ones between pull it towards them.

```csharp
var ribbon = new BezierBuilder()
    .AddPoint(0, 0).AddPoint(0, 1000).AddPoint(1000, 1000).AddPoint(1000, 0)
    .BuildPolygon(width: 200, vertices: 128);
```

`BuildPolygon` is preferred over keeping the curve as a `PATH`: a path's width and ends are applied by
whatever reads the file and readers differ about the ends, where an outline is the shape itself and cannot
be read two ways. `BuildCenterline` hands back the open run for a caller who wants the `PATH`.

### Routes

A route carries a **heading**, so `Straight` goes the way the last segment pointed and `BendDeg` turns from
there - which is the whole difference between this and a list of points. Positive turns left.

```csharp
var route = new PathBuilder(new Element.Point(-3100, -3300), headingDegrees: 0)
    .Straight(2000)
    .BendDeg(-45, radius: 500)      //a radius, not a square corner - radius 0 is the square corner
    .Straight(1000)
    .BendDeg(180, 300)
    .Bezier(b => b.AddPoint(0, 0).AddPoint(0, 1000).AddPoint(2000, 1000).AddPoint(1000, 0));

var outline = route.BuildPolygon(width: 140);
var pieces = route.Build(maxVertices: 200);   //centerlines short enough to be elements
```

A curve dropped into a route is placed **relative to where the route has reached and which way it points**,
so the same curve can be used at any angle without its numbers changing. `Build` cuts a long route into
pieces that overlap by a point, because a cut that does not overlap is a dotted line - and on a wire that is
an open circuit that looks like a rendering artifact.

### A width that changes along the route

Give the route a width to start with and any segment can change it: `widthEnd` on a straight or a bend, and
a function of how far along a curve is. The taper is spread over the points the segment already has, so a
straight becomes a wedge and a bend narrows as it turns.

```csharp
var taper = new PathBuilder(new Element.Point(0, 0), headingDegrees: 0, width: 200)
    .Straight(1000, widthEnd: 50)
    .BendDeg(90, radius: 400, widthEnd: 200)
    .Bezier(
        b => b.AddPoint(0, 0).AddPoint(0, 1000).AddPoint(2000, 1000).AddPoint(1000, 0),
        t => 250 - ((250 - 50) * t))
    .BuildPolygon();                 //no argument: the widths the route is carrying
```

**A tapering wire is not a GDSII path.** The format's `WIDTH` is one number for the whole element, so a wire
that narrows has to be written as a boundary - which is what this builds. `Widths()` hands back the width at
each point of `Centerline()`, in the shape `PathOutline.Build` takes, for a caller outlining it themselves.

**One outliner, generalized rather than duplicated.** `PathOutline` takes a width per point now and the
constant case goes through the same code with the same number repeated - the alternative was a second
offsetter, and two of them would eventually disagree about what a sharp corner does. Where the width changes
along a segment that segment's offset edge is not parallel to it: it runs from the start point offset by the
start's half-width to the end point offset by the end's, and a corner is where two such edges meet.

Outlining goes through the same `PathOutline` a drawn path and a read `PATH` go through, so what is built
here is mitered, capped and wound exactly like everything else.

### Marks

The shapes a lithography layout needs besides the device - a die frame, alignment crosses and the windows
they land in, verniers, lines and spaces, a Greek cross, and the first shapes of a photonic or microfluidic
chip - are made from their numbers rather than drawn. Each comes out centered on the origin, in database
units, without holes:

```csharp
var cross = Marks.Cross(arm: 100_000, width: 10_000);
var frame = Marks.DieFrame(width: 5_000_000, height: 5_000_000, line: 100_000);   //four bars

//Or by name, from microns, the way the editor offers them. Numbers that cannot make the mark come back as a
//problem in words rather than as a strange shape.
var made = Marks.Named("ring")!.Make(new Dictionary<string, double> { ["radius"] = 20 }, microns => (int)Math.Round(microns * 1000));

if (made.Problem is null)
{
    foreach (var outline in made.Outlines)
        new AddElement(gds, top, new LayerKey(1, 0), outline).Apply();
}
```

`Marks.All` is the whole catalog, each with the settings it is drawn from - a name, what it means, what it
starts at, and whether it is a count - so a tool can offer every one without knowing what any of them is.

## Editing

Every edit is one class, undoable, and describable so it survives being written down and rebuilt against a
reopened file:

```csharp
var edit = new MoveElement(structure, model, dx: 100, dy: 0);

edit.Apply();
edit.Revert();

EditRecord? written = edit.Describe();               //to store
LayoutEdit? rebuilt = LayoutEdit.Rebuild(written, gds);  //against a file opened again
```

`CompoundEdit` makes several into one step, which is what a band of shapes moved together is. `CellContext`
answers which cell an edit lands in and through which placement it is being seen — the two are separate on
purpose, since an edit changes the cell and every instance of it moves.

**Taking a placement apart** is `Flattening.Edits`: the placement goes, and what it draws is written into the
cell that held it, where it was in the cell's order — one level down, an array as every place it has, and the
placed cell taken out with its last placement. It is more than moving coordinates, and
`Hierarchy.Through` is the part that knows it: an element's records put through a transform with a
placement's or a label's own reflection, angle and magnification composed with it, and a path's width grown by
the magnification. Moving the point alone leaves a placement inside a turned instance drawn the way it faced
before. Turning a placement where it sits is the same call with a turn, which keeps an array an array.

```csharp
List<LayoutEdit>? edits = Flattening.Edits(gds, top, new[] { placement });   //null past MostElements

new CompoundEdit("Flatten", edits!).Apply();

List<Record>? turned = Hierarchy.Through(records, Turning.About(Turn.Quarter, pivotX, pivotY));
```

`Hierarchy.PlacementAt` finds the placement an instance came through — for an array, from any of its places.

## Tools for an AI assistant

A model that edits a layout should call typed tools, not write records. `AgentTools` is thirty-one of them over
the edits above - seven that read (`list_layers`, `list_cells`, `open_cell`, `find_shapes`, `measure`,
`check_rules`, `list_structures`), eight that draw (rectangles, polygons, circles and ellipses, rings, paths with
radiused bends, text as shapes, labels, and the structures `Marks` makes from a few numbers, put down whole) and
sixteen that edit (move, copy, array, turn, align, delete, change layer, the four
booleans, grow and shrink, round and cut corners, make, place, flatten, rename and delete cells, and take back
the assistant's own last change). A host does three things with them:

```csharp
var session = new AgentSession(gds, history);         //the same history the person undoes with

foreach (var tool in AgentTools.All)
    Offer(tool.Name, tool.Description, tool.InputSchema());   //in your provider's own format

AgentResult result = AgentTools.Call(session, name, argumentsJson);

//result.Text goes back to the model. IsError: refused, layout untouched. Changed: draw it again.
```

`AgentTools.Conventions` is the paragraph for the system prompt. Sizes go in as **microns** and come out on the
file's grid; **layers by name** from the layermap or by numbers; **shapes by number**, `#12`, which every
drawing tool and `find_shapes` hand out and which last the session, through edits and undo. `session.IdOf`
numbers a host's own selection the same way.

Every change is **one step** on the history, applied whole or not at all. With `session.Deck` set, each one is
followed by a check of the rules within reach of it, and the answer says what the change broke - not what was
already broken nearby. `session.HowToLoadRules` is what `check_rules` adds when there is no deck: how *this*
host's person or model gets one. Schemas use only the keywords Anthropic, OpenAI and Google all read.

Two hosts use them already: the viewer's AI assistant, and [`gds mcp`](CLI.md#gds-mcp), which serves them to any
client of the Model Context Protocol with six tools of its own for opening and saving files in one folder.

## Writing

```csharp
File.WriteAllBytes("cell.gds", gds.Serialize());              //byte-exact for a file read in
File.WriteAllBytes("cell.oas", OasisWriter.Write(gds));        //hierarchy kept
File.WriteAllBytes("cell.dxf", DxfWriter.Write(gds));          //release 12
DxfWriter.Write(gds, out int skipped);                         //and how many NODEs had no entity - OasisWriter.Write(gds, out skipped) says the same
File.WriteAllText("cell.svg", SvgWriter.Build(layout, SvgWriter.AllLayers(layout), 0.5f, true));   //markup for inside an <svg> element - gds svg wraps it in one

string text = gds.AsText();          //every record, one per line
var edited = GDS.FromText(text);     //and back, validated per record
```

A file read and written back out is the **same bytes**, pinned against 1,220 real layouts in the test suite —
896 sky130 standard cells, ten complete sky130 designs, 229 GF180 cells and 84 IHP SG13G2 cells among them. The text dump round-trips the same way, which is what makes an editor possible on top of it.

**Masks as pictures.** Much of the equipment a lab or a small fab has - a laser writer, a maskless aligner -
takes a black and white image at a stated pixel size rather than a GDSII file. `MaskBitmaps` makes one a
layer:

```csharp
//A micron a pixel at a nanometer a unit, and one frame for every layer so the pictures line up.
var frame = layout.Index.ShapeExtent;
var metal = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(68, 20)) && e.Text is null && !e.IsOpen).Select(e => e.Points);

var bitmap = MaskBitmaps.Rasterize(metal, frame, unitsPerPixel: 1000);

File.WriteAllBytes("met1.png", MaskBitmaps.Png(bitmap, shapesDark: true, micronsPerPixel: 1));
```

A pixel is exposed when its middle is inside a shape, with no anti-aliasing to put a gray edge on something
that has no gray, and the PNG is one bit a pixel and states its own pixel size. `MaskBitmaps.Zip` puts a set
into one archive, and `FileNameFor(layer)` names each by its numbers and its name.

## Also public

The library's own parts, public so a consumer can use them, and part of the surface the version number covers:
`TextFormat` (the text dump's reader), `DxfBinary` and `DxfCurves` (the binary flavor's pairs and the curves
flattened), `DrcChecks`, `DrcEdges`, `DrcEdgePair` and `DrcLayers` (what `Drc` is built from), `Synthetic` (the
layouts `gds bench` and the tests build), `LayoutFile` and `LayoutFormat`, `ConcatenatedStream`,
`LayerKey.TryParse` (the one grammar for `65/20`), `DrcResult.ByRule(violations)`, `AgentTools.Counted`,
`AgentTools.LookName`, `LookDescription` and `LookArea` (the `look` tool's declaration, which a host runs),
`AgentSession.Given`, and `Preview.Document` with `yUp`, the frame `gds svg` writes.

## What is deliberately not in it

- **Anything that needs a browser.** Storage over IndexedDB, the session, the history list and the embedding
  parameters live in the app's own `Models/`, not here.
- **3D meshes.** Extrusion into STL, OBJ and glTF lives in `GdsII.Cli/`, because it needs a tessellator and a
  glTF writer as package references — and the library staying dependency-free is the property that makes it
  worth depending on.
- **A bundled PDK layer table.** One PDK's names in a library that reads any layout file would be the wrong
  default, and the one piece of it under someone else's license.

## Building and releasing

Everything except the two packages is `IsPackable=false`, so `dotnet pack` on the solution produces those two
and nothing else. The version lives once, in [`Directory.Build.props`](../Directory.Build.props) — the tool
reads its own off its assembly rather than off a constant, which is how the three copies that used to exist
became one.

A release is a tag:

```bash
git tag v1.2.0 && git push origin v1.2.0
```

[`.github/workflows/publish-nuget.yml`](../.github/workflows/publish-nuget.yml) builds it, runs the tests, packs both,
pushes to nuget.org and opens a GitHub Release with the same `.nupkg` files attached. It refuses to run if the
tag and `Directory.Build.props` disagree about the version, which is the mistake that would otherwise publish
a number nobody chose.

**The release carries the web app too.** After packing, the workflow publishes `GDSViewer.csproj` and zips
its `wwwroot` as `GDSViewer-<version>-web.zip` — trimmed, AOT compiled, precompressed, with the service
worker's asset manifest written. The app has no server side, so those files behind any static web server
*are* the deployment. Published rather than built, because a `bin` folder from a plain build is none of
those things. **v1.0.0 has no such zip**: the step was added after that tag was cut.

**No API key is stored.** It authenticates by Trusted Publishing: GitHub issues a short-lived signed OIDC
token naming this repository and this workflow file, nuget.org checks it against a policy registered there,
and hands back a key that lives an hour. The only secret on the repository is `NUGET_USER`, the nuget.org
profile name. The policy is keyed to the workflow file's name, so renaming `publish-nuget.yml` stops publishing
until the policy is edited to match - and the failure reads as a credentials problem rather than a rename.
The push is the last step, because a version on nuget.org can be unlisted but never replaced.

[`ci.yml`](../.github/workflows/ci.yml) beside it runs on every push: build, the C# tests, the JS units, the
end-to-end run, and a pack — so a broken license expression or a missing package readme fails on the commit
rather than on the tag.

**The C# tests are filtered on CI** with `--filter "Needs!=KLayout"` — 3,569 of the 3,616 run there.
Forty-five use KLayout as a second implementation to check this one against, and it is a desktop EDA tool that
is not on a runner. Locally, with it installed, `dotnet test` runs every one.

The short readmes that ship *inside* each package are [`GdsII/README.md`](../GdsII/README.md) and
[`GdsII.Cli/README.md`](../GdsII.Cli/README.md) — nuget.org renders those on the listing pages, so they
stay where `PackageReadmeFile` points and are held there by a test. Most of the library readme's code samples
are run as tests too, transcribed into [`tests/GDSViewer.Tests/PackageReadmeTests.cs`](../tests/GDSViewer.Tests/PackageReadmeTests.cs), because an
example on a package page that does not compile is a worse first impression than no example at all. The ring,
the taper, the two under *Naming layers* and the `Compare.Layouts` line are not transcribed, the tool's readme
has no C# in it, and the samples on this page are not run.
