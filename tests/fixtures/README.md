# Test fixtures

Files here are **input for tests**, not examples for the app. They deliberately live outside
`wwwroot/resources/` — the build globs that folder into the example picker, so anything put there would
appear in the app's dropdown.

## `klayout-written.gds`, `klayout-resaved.gds`

Written by **KLayout 0.30.9**, so the suite has output from a real third-party tool to read rather than
only files this project produced or was given.

- `klayout-written.gds` — built by KLayout from scratch: a box, a triangle (three points, so a boundary
  our own writer would never emit that shape for), and a text. 268 bytes.
- `klayout-resaved.gds` — `Mosfet.gds` read and written back out by KLayout. 1514 bytes, the same size as
  the original.

They are checked in because the alternative is a test that only runs where KLayout happens to be
installed. Regenerating them needs KLayout and the script recorded in
[DOCUMENTATION.md](../../docs/DOCUMENTATION.md#interoperability).

Neither file has padding after `ENDLIB`, which is itself one of the findings: KLayout does not write any.

## `klayout-written.dxf`

`Mosfet.gds` written out as DXF by the same KLayout, so the DXF reader has one file it did not make
itself and one it can be checked against — the original is right there, and a conversion that loses or
moves anything shows up as a difference rather than as a number somebody has to judge. 5525 bytes.

Every other DXF fixture is a string inside a test, which is the right way to isolate one group code and
has one blind spot: a hand-written file contains only what the person writing it knew to put in. This one
carries what an exporter actually writes — old-style `POLYLINE`/`VERTEX`/`SEQEND` runs rather than
`LWPOLYLINE`, a `LAYER` table with colors and linetypes, and no `$INSUNITS` at all.

Two findings from it:

- KLayout names its DXF layers `L65D20`, so a converted file carries its GDSII numbering in the only place
  DXF has to put one. Read as an index instead, this file came back as layers 0 through 8.
- Its rings are wound the opposite way to the GDSII it read them from. That is the same polygon — GDSII
  specifies no winding — so `DxfRealFileTests` normalizes before comparing.
