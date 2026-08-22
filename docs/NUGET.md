# The NuGet packages

Two come out of this repository, both public domain:

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
- [Layers, names and the process stack](#layers-names-and-the-process-stack)
- [Creating](#creating)
- [Shapes, curves and routes](#shapes-curves-and-routes)
- [Editing](#editing)
- [Writing](#writing)
- [What is deliberately not in it](#what-is-deliberately-not-in-it)
- [Building and releasing](#building-and-releasing)

## What the library is

Everything about the formats lives in `GdsII/`, which the web app references and is one consumer of. It reads
a file into an object model and writes one back, validates what it holds, converts to and from a text dump,
resolves the hierarchy into flat geometry, and renders that to SVG.

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

Three formats read into the same model, so everything downstream only ever sees one:

```csharp
var gds = GDS.FromStream(File.OpenRead("cell.gds"));      //GDSII
var oas = OasisReader.Read(File.OpenRead("cell.oas"));    //OASIS, into the same model
var dxf = DxfReader.Read(File.ReadAllBytes("plan.dxf"));  //DXF, text or binary

//Or let the bytes decide, which is what the app and the tool both do.
bool isOasis = OasisReader.LooksLikeOasis(firstThirteenBytes);
```

`FromStream` and `FromStreamAsync` read straight off a handle rather than holding the whole file as an array
first, which matters on a large library.

A malformed file throws where it is read rather than three views later — an unclosed boundary, a payload that
is not a whole number of values, a record claiming more bytes than remain. The same rules apply on the way out,
deliberately: a file that opens is a file that saves.

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
var grown = Booleans.Offset(gate, 50);
```

`Measure` answers area and extent — drawn area counts an overlap twice where covered area merges first, which
is the difference density is computed from. `Picking.At` answers which element is under a point, and `Nets`
traces a connected net through vias.

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
not run. Each `DrcViolation` carries its rule id, description, the outline to draw as a marker, its
bounds, the measured value where there is one, and an `ElementSource` naming the cell instance at fault —
so a report can point at the cell to edit rather than only at a coordinate.

To re-check after an edit, flatten again and call `Check` again: the layout is the input, and there is no
cached state to invalidate.

`DrcReport.Write` produces a KLayout `.lyrdb` for comparing against another tool's answer.

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

The format is `layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize`, everything past
the third column optional — [the CLI guide](CLI.md#layermaps) describes it in full, and it is the same file the
web app's Import button takes.

**A mapping has to be followed by a restack.** `ApplyTo` writes heights onto layers; only `SetStackingOffsets`
reads them back as a stack, and it is what places the layers the table says nothing about:

```csharp
mapping.ApplyTo(gds.AdditionalInformation.Layers);        //place what the table places
gds.AdditionalInformation.SetStackingOffsets(50);          //space out whatever it did not
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
an open circuit that looks like a rendering artefact.

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

## Editing

Every edit is one class, undoable, and describable so it survives being written down and rebuilt against a
reopened file:

```csharp
var edit = new MoveElement(gds, structure, model, dx: 100, dy: 0);

edit.Apply();
edit.Undo();

EditRecord? written = edit.Describe();               //to store
LayoutEdit? rebuilt = LayoutEdit.Rebuild(written, gds);  //against a file opened again
```

`CompoundEdit` makes several into one step, which is what a band of shapes moved together is. `CellContext`
answers which cell an edit lands in and through which placement it is being seen — the two are separate on
purpose, since an edit changes the cell and every instance of it moves.

## Writing

```csharp
File.WriteAllBytes("cell.gds", gds.Serialize());              //byte-exact for a file read in
File.WriteAllBytes("cell.oas", OasisWriter.Write(gds));        //hierarchy kept
File.WriteAllText("cell.dxf", DxfWriter.Write(gds));           //release 12
File.WriteAllText("cell.svg", SvgWriter.Build(layout, SvgWriter.AllLayers(layout), 0.5f, true));

string text = gds.AsText();          //every record, one per line
var edited = GDS.FromText(text);     //and back, validated per record
```

A file read and written back out is the **same bytes**, pinned against 897 real sky130 layouts in the test
suite. The text dump round-trips the same way, which is what makes an editor possible on top of it.

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
git tag v1.0.0 && git push origin v1.0.0
```

[`.github/workflows/publish-nuget.yml`](../.github/workflows/publish-nuget.yml) builds it, runs the tests, packs both,
pushes to nuget.org and opens a GitHub Release with the same `.nupkg` files attached. It refuses to run if the
tag and `Directory.Build.props` disagree about the version, which is the mistake that would otherwise publish
a number nobody chose.

**No API key is stored.** It authenticates by Trusted Publishing: GitHub issues a short-lived signed OIDC
token naming this repository and this workflow file, nuget.org checks it against a policy registered there,
and hands back a key that lives an hour. The only secret on the repository is `NUGET_USER`, the nuget.org
profile name. The policy is keyed to the workflow file's name, so renaming `publish-nuget.yml` stops publishing
until the policy is edited to match - and the failure reads as a credentials problem rather than a rename.
The push is the last step, because a version on nuget.org can be unlisted but never replaced.

[`ci.yml`](../.github/workflows/ci.yml) beside it runs on every push: build, the C# tests, the JS units, the
end-to-end run, and a pack — so a broken license expression or a missing package readme fails on the commit
rather than on the tag.

**The C# tests are filtered on CI** with `--filter "Needs!=KLayout"` — 1,919 of the 1,952 run there.
Thirty-three use KLayout as a second implementation to check this one against, and it is a desktop EDA tool that
is not on a runner. Locally, with it installed, `dotnet test` runs every one.

The short readmes that ship *inside* each package are [`GdsII/README.md`](../GdsII/README.md) and
[`GdsII.Cli/README.md`](../GdsII.Cli/README.md) — nuget.org renders those on the listing pages, so they stay
where `PackageReadmeFile` points and are held there by a test. Their code samples are run as tests too, in
[`tests/PackageReadmeTests.cs`](../tests/PackageReadmeTests.cs), because an example on a package page that does
not compile is a worse first impression than no example at all.
