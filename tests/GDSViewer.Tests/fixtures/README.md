# Test fixtures

Files here are **input for tests**, not examples for the app. They deliberately live outside
`wwwroot/resources/`, where they would ship with the site: the build globs its `GDS Files/Sky130 GDS/*.gds`
into the example picker, so one put there would appear in the app's dropdown, and the corpus tests read every
`.gds` anywhere under `GDS Files/`.

## `klayout-written.gds`, `klayout-resaved.gds`

Written by **KLayout 0.30.9**, so the suite has output from a real third-party tool to read rather than
only files this project produced or was given.

- `klayout-written.gds` — built by KLayout from scratch: a box, a triangle (three points, so a boundary
  our own writer would never emit that shape for), and a text. 268 bytes.
- `klayout-resaved.gds` — `Mosfet.gds` read and written back out by KLayout. 1514 bytes, the same size as
  the original.

They are checked in because the alternative is a test that only runs where KLayout happens to be
installed. Regenerating them needs KLayout and the script recorded in
[DOCUMENTATION.md](../../../docs/DOCUMENTATION.md#interoperability).

Neither file has padding after `ENDLIB`, which is itself one of the findings: KLayout does not write any.

## `placed.gds`

A two-cell library of this project's own: `TOP` holds four elements and places `LEAF` three times by `SREF`;
`LEAF` holds one. 360 bytes. The starting point for the editing, hierarchy, journal, net, path, scaling and
SVG-writer tests, through `GdsTestData.ReadFixture("placed.gds")`.

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

## `klayout-deep.lyrdb`, `klayout-flat.lyrdb`, `klayout-every-value.lyrdb`

Report databases written by the same **KLayout 0.30.9**, for `DrcReportReadingTests`.

- `klayout-deep.lyrdb` and `klayout-flat.lyrdb` — one DRC script over the bundled 555 timer
  (`Sky130 Designs/tt_um_vaf_555_timer.gds`), run in deep mode and flat. Deep files each of its 57 items under
  the cell it was found in, with a reference placing that cell — transistors among them placed `m90`, `r90` and
  `r270` — and flat puts all 167 in the top. Once placed, every deep item has to land on a flat one, which is
  the test that the transforms are read the way KLayout writes them. 19,805 and 47,267 bytes.
- `klayout-every-value.lyrdb` — built through KLayout's API to hold one of everything the format has: every kind
  of value, a waived item and a tag of somebody's own, a category inside another, two variants of one cell
  with references, a polygon with a hole. 2,844 bytes.

The DRC script, `klayout-report.drc`, run as
`klayout_app -b -r klayout-report.drc -rd input=<the timer> -rd output=<report> -rd mode=deep` (and
`mode=flat`). **Run it from the folder it is in, by its bare name**: KLayout writes the path it was given into
the report's `generator`, and a full path would put somebody's home folder into a checked-in file.

```ruby
source($input)
report("555 timer, " + $mode, $output)

if $mode == "deep"
  deep
end

poly = input(66, 20)
diff = input(65, 20)

poly.and(diff).output("gate", "Poly over diffusion")
diff.space(0.34.um).output("difftap.2", "Diffusion spacing")
poly.edges.with_length(0, 0.16.um).output("poly.short", "Short poly edges")
```

The API one, `klayout-every-value.rb`, run with `-b -r klayout-every-value.rb -rd output=<report>`:

```ruby
rdb = RBA::ReportDatabase.new("API sample")
rdb.generator = "klayout-every-value.rb"
rdb.original_file = "sample.gds"
rdb.top_cell_name = "TOP"

top = rdb.create_cell("TOP")
sub = rdb.create_cell("SUB")
variant = rdb.create_cell("SUB", "1")

sub.add_reference(RBA::RdbReference.new(RBA::DCplxTrans.new(1.0, 90.0, false, RBA::DVector.new(10.5, -2.25)), top.rdb_id))
sub.add_reference(RBA::RdbReference.new(RBA::DCplxTrans.new(1.0, 0.0, false, RBA::DVector.new(100, 0)), top.rdb_id))
variant.add_reference(RBA::RdbReference.new(RBA::DCplxTrans.new(2.0, 0.0, true, RBA::DVector.new(-1, 3)), top.rdb_id))

width = rdb.create_category("m1.width")
width.description = "Metal 1 width < 0.14 & stuff <tag>"
child = rdb.create_category(width, "sub.cat")
child.description = "A subcategory"
plain = rdb.create_category("plain")
spaced = rdb.create_category("has space")

one = rdb.create_item(top.rdb_id, width.rdb_id)
one.add_value(RBA::DPolygon.new(RBA::DBox.new(0, 0, 1.5, 2.25)))
one.add_value(RBA::DEdgePair.new(RBA::DEdge.new(0, 0, 0, 1), RBA::DEdge.new(1, 1, 1, 0)))
one.add_value(RBA::DEdge.new(0, 0, 3, 4))
one.add_value(RBA::DBox.new(-1, -2, 3, 4))
one.add_value(RBA::DPath.new([RBA::DPoint.new(0, 0), RBA::DPoint.new(10, 0)], 0.5))
one.add_value(RBA::DText.new("lbl", RBA::DTrans.new(RBA::DVector.new(1.5, 2.5))))
one.add_value(2.5)
one.add_value("some text, with 'quotes'")
one.comment = "a comment"
one.add_tag(rdb.tag_id("waived"))
one.add_tag(rdb.user_tag_id("mine"))

holed = RBA::DPolygon.new(RBA::DBox.new(0, 0, 10, 10))
holed.insert_hole(RBA::DBox.new(2, 2, 8, 8))
two = rdb.create_item(sub.rdb_id, child.rdb_id)
two.add_value(holed)

three = rdb.create_item(variant.rdb_id, plain.rdb_id)
three.add_value(RBA::DEdgePair.new(RBA::DEdge.new(0, 0, 0, 1), RBA::DEdge.new(0.5, 0, 0.5, 1), true))

four = rdb.create_item(top.rdb_id, spaced.rdb_id)
four.add_value(RBA::DBox.new(5, 5, 6, 6))
rdb.set_item_visited(four, true)

rdb.save($output)
```

Two findings from them, both now in `DrcReport`:

- **A value is in the coordinates of the cell its item is filed under**, not the top's. Read through each cell's
  reference, all 547 values of a fuller deep run landed on a flat one; read as top coordinates, 3 did.
- **`m45` names the line a transform mirrors across**, which is half the turn that follows the mirror -
  `DCplxTrans.new(1, 90, true, ...)` prints as `m45`.
