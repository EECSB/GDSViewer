# GdsII

Read, write, validate and convert **GDSII**, **OASIS** and **DXF** layout files from .NET. No dependencies.

```csharp
using GdsII;

var gds = new GDS(File.ReadAllBytes("cell.gds"));

foreach (var layer in gds.AdditionalInformation.Layers)
    Console.WriteLine($"{layer.Key}: {layer.Value.DisplayName}");

//Out to OASIS, hierarchy and all.
File.WriteAllBytes("cell.oas", OasisWriter.Write(gds));
```

## What it does

- **Three formats, all directions.** GDSII, OASIS (SEMI P39) and DXF read and written. Which one a file is
  comes off its first bytes rather than its extension, so a renamed file still opens. Converting keeps the
  hierarchy: cells stay cells and placements stay placements.
- **Byte-exact round trip.** A file read and written back out is the same bytes, pinned against 897 real
  sky130 layouts in the test suite.
- **Full record coverage**, including the rare Calma extensions — `SOFTFENCE`, `HARDWIRE`, `PATHPORT`,
  `CONTACT` and the rest — that most readers skip.
- **Hierarchy resolved on request.** `GdsFlattener` places every `SREF` and `AREF` with its reflection,
  magnification and rotation, at any depth, and outlines `PATH` centerlines into polygons.
- **Layouts built from code.** Boundaries, paths, labels, cells and placements are added through edits that
  are undoable, replayable and record-level faithful — a path stays a `PATH` rather than becoming a polygon.
- **Shapes, curves and routes.** Rectangles, circles, ellipses and regular polygons; Bézier curves; and a
  path builder that goes straight, bends by a radius and follows a curve, cut into elements at the vertex
  limit. A layout format has no curves, so the side count is an argument rather than a guess.
- **Boolean operations and sizing** over layout geometry — AND, OR, NOT, XOR and offset, via a vendored
  Clipper2.
- **Design rule checking** against a deck you supply - a small text file of width, spacing, enclosure,
  area, density and off-grid rules. `DrcDeck.Parse` reads it, `Drc.Check` runs it, and a rule this build
  cannot measure is refused by name rather than skipped, so a clean report is either complete or says so.
- **Renders to SVG**, and dumps every record as text that reads back into an identical file — which is
  what makes an editor possible on top of it.
- **Streaming.** `GDS.FromStream` and `FromStreamAsync` read straight off a handle rather than holding the
  whole file as an array first.

Nothing in here touches a renderer, a UI framework or a browser. It is the format and nothing else.

## Reading

```csharp
var gds = GDS.FromStream(File.OpenRead("cell.gds"));         //GDSII
var oas = OasisReader.Read(File.OpenRead("cell.oas"));       //OASIS, into the same model

//Or let the bytes decide.
bool isOasis = OasisReader.LooksLikeOasis(firstThirteenBytes);
```

An OASIS file becomes a GDSII library rather than a model of its own, so everything downstream only ever
sees one format.

## Flattening and geometry

```csharp
var layout = GdsFlattener.Flatten(gds);

foreach (var element in layout.Elements)
    Console.WriteLine($"{element.Layer.Key}: {element.Points.Count} points");

//Where poly crosses diffusion is a transistor gate.
var poly = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(66, 20))).Select(e => e.Points).ToList();
var diff = layout.Elements.Where(e => e.Layer.Key.Equals(new LayerKey(65, 20))).Select(e => e.Points).ToList();

var gate = Booleans.Combine(poly, diff, BooleanOperation.And);
```

## Creating a layout

Shapes, labels, cells and placements are all built from code. Every creation is an **undoable edit** —
`Apply()` puts it in, `Revert()` takes it back out — so the same calls drive a generator or an editor:

```csharp
//A new library with one empty cell. A database unit is a nanometer unless you say otherwise.
var gds = GDS.NewLibrary("AUTHORED");

var top = Hierarchy.Named(gds, "TOP")!;

//A boundary, from its corners. Closed for you when the last does not repeat the first.
var square = new[]
{
    new Element.Point(0, 0),
    new Element.Point(1000, 0),
    new Element.Point(1000, 600),
    new Element.Point(0, 600)
};

new AddElement(gds, top, new LayerKey(68, 20), square).Apply();

//A path down a centerline, with a width and an end style.
var run = new[] { new Element.Point(0, 900), new Element.Point(2000, 900) };

new AddElement(gds, top, new LayerKey(67, 20), run, 140, Paths.Ends.Extended).Apply();

//A label at a point.
new AddElement(gds, top, new LayerKey(68, 5), new Element.Point(500, 300), "VDD").Apply();

//A second cell, then an instance of it placed in TOP and turned 90 degrees.
new AddStructure(gds, "LEAF", Paths.Records(new LayerKey(66, 20), run, 200, Paths.Ends.Flush)!).Apply();

new AddElement(gds, top, Hierarchy.PlacementRecords("LEAF", new Element.Point(3000, 0), false, 90), "Place").Apply();

File.WriteAllBytes("authored.gds", gds.Serialize());
```

`Hierarchy.AsArray` turns a placement into an `AREF`. `MoveElement`, `MoveVertex`, `ReshapeElement`,
`RelayerElement`, `RetextElement`, `RenameStructure` and `RemoveStructure` change what is already there,
`AddElement.CopyOf` duplicates it, and `CompoundEdit` makes several into one undo step. Each one can
`Describe()` itself into a record you can store and `LayoutEdit.Rebuild` against the file reopened.

## Shapes, curves and routes

**A layout format has no curves** — GDSII stores polygons and polylines and nothing else — so a circle is a
many-sided polygon and the side count is a decision. These make it the argument rather than a guess, and hand
back **corners rather than elements**, so a shape can be measured, combined or fed to another builder before
it goes on a layer:

```csharp
Shapes.Rectangle(centerX: 0, centerY: 0, width: 1000, height: 600);   //centered, like the rest
Shapes.Between(0, 0, 1000, 600);                                      //or corner to corner
Shapes.Circle(centerX: 0, centerY: 0, radius: 500, vertices: 128);
Shapes.Ellipse(0, 0, radiusX: 800, radiusY: 300);
Shapes.RegularPolygon(0, 0, radius: 500, sides: 6, turnDegrees: 30);
```

A **Bézier** curve runs between its first and last control point; the ones between pull it towards them
without being on it. `BuildPolygon` outlines it at a width, which is better than keeping it as a `PATH`:
a path's ends are applied by whatever reads the file and readers differ, where an outline is the shape itself.

```csharp
var ribbon = new BezierBuilder()
    .AddPoint(0, 0).AddPoint(0, 1000).AddPoint(1000, 1000).AddPoint(1000, 0)
    .BuildPolygon(width: 200, vertices: 128);
```

A **route** is built a segment at a time and carries a heading, so the pieces join without you tracking an
angle. Positive turns left. `Build` cuts it into centerlines short enough to be elements, overlapping by a
point so the run stays continuous:

```csharp
var route = new PathBuilder(new Element.Point(-3100, -3300), headingDegrees: 0)
    .Straight(2000)
    .BendDeg(-45, radius: 500)          //a radius, not a square corner
    .Straight(1000)
    .BendDeg(180, 300)
    .Bezier(b => b.AddPoint(0, 0).AddPoint(0, 1000).AddPoint(2000, 1000).AddPoint(1000, 0));

new AddElement(gds, top, new LayerKey(68, 20), route.BuildPolygon(width: 140)).Apply();

foreach (var piece in route.Build(maxVertices: 200))
    new AddElement(gds, top, new LayerKey(68, 20), piece, 140, Paths.Ends.Flush).Apply();
```

**The width can change along the route.** Give it one to start with and any segment can taper — `widthEnd`
on a straight or a bend, and a function of how far along a curve is. `BuildPolygon()` with no argument then
outlines at the widths the route is carrying:

```csharp
var taper = new PathBuilder(new Element.Point(0, 0), headingDegrees: 0, width: 200)
    .Straight(1000, widthEnd: 50)                      //a wedge
    .BendDeg(90, radius: 400, widthEnd: 200)           //widening round the turn
    .Bezier(
        b => b.AddPoint(0, 0).AddPoint(0, 1000).AddPoint(2000, 1000).AddPoint(1000, 0),
        t => 250 - ((250 - 50) * t))                   //width along the curve, t from 0 to 1
    .BuildPolygon();
```

A tapering wire is **not** a GDSII path — the format's `WIDTH` is one number for the whole element — so this
comes out as a boundary. `Widths()` hands back the width at each point for a caller outlining it themselves.

Outlining goes through the same `PathOutline` a drawn path and a read `PATH` go through, constant width and
tapering alike, so what is built here is mitered and capped exactly like one drawn with the mouse.

## Editing through text

```csharp
string text = gds.AsText();                 //every record, one per line
var edited = GDS.FromText(text);            //and back, with per-record validation
```

## Naming layers

A layout file carries only numbers, so what `65/20` means comes from a mapping you supply — names, colors,
the process stack, and what each layer is for:

```csharp
var mapping = LayerNames.Parse(File.ReadAllText("sky130.csv"));

int applied = mapping.ApplyTo(gds.AdditionalInformation.Layers);

//A mapping writes heights; only this reads them back as a stack.
gds.AdditionalInformation.SetStackingOffsets(50);

//And back out, every column filled in with what is currently set.
File.WriteAllText("sky130.csv", LayerNames.Export(gds.AdditionalInformation));
```

The table holds the pairs a file actually draws on. `AddLayer` puts in one that nothing is drawn on yet —
what makes a new library somewhere you can start, since GDSII records a layer only through the shapes on it:

```csharp
gds.AdditionalInformation.AddLayer(new LayerKey(66, 44));   //false if the pair is already there
```

## Command-line tool

The same library behind a command: `dotnet tool install -g GdsII.Cli`, then `gds`.

## More

The full library guide is [docs/NUGET.md](https://github.com/EECSB/GDSViewer/blob/master/docs/NUGET.md), and the
command's is [docs/CLI.md](https://github.com/EECSB/GDSViewer/blob/master/docs/CLI.md).

## License

Public domain — [The Unlicense](https://unlicense.org/). The vendored
[Clipper2](https://github.com/AngusJohnson/Clipper2) is compiled in and keeps its Boost Software License;
see `THIRD-PARTY-NOTICES.md` in the package.

Source, documentation and a browser-based viewer built on this:
[github.com/EECSB/GDSViewer](https://github.com/EECSB/GDSViewer)
