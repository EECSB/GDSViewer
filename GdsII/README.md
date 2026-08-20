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

//And back out, every column filled in with what is currently set.
File.WriteAllText("sky130.csv", LayerNames.Export(gds.AdditionalInformation));
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
