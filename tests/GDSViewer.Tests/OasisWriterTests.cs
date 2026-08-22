using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Writing OASIS.
///
///**The corpus is not enough here, and that is worth knowing.** Of the 897 bundled files, exactly one has
///a placement in it, none has an array, none has a box or a node, and none has more than a single cell.
///Run against that alone a writer could get every placement record wrong and still pass 897 times - so the
///hierarchy, the arrays, the four right angles, the mirror, the magnification and the four kinds of path
///end are exercised by a library built here that has one of each.
///
///The corpus still runs, and is still worth running: it is 897 real layouts' worth of coordinates,
///point counts and layer numbers, which is the part no hand-written fixture covers.
///
///**And KLayout reads what comes out.** A file this project writes and this project reads only proves the
///two agree. The reader's tests are measured against KLayout for that reason and the writer's are measured
///against it for the same one, in the other direction.
///</summary>
public class OasisWriterTests
{
    #region A library with one of everything in it ***********************************

    ///<summary>
    ///A two-cell library holding every element the writer has a case for.
    ///
    ///Built rather than found, because the corpus does not contain these - see the class summary. The leaf
    ///holds the shapes and the top places it eight ways, so what comes back can be compared flattened:
    ///a placement that lands in the wrong spot or faces the wrong way moves geometry, which is a thing
    ///<see cref="GdsTestData.Geometry"/> can see.
    ///</summary>
    private static byte[] Everything(bool withNode = false, bool withRoundPath = false)
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("EVERYTHING")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF"))
        };

        //An axis-aligned rectangle, which the writer should recognize and write as a RECTANGLE.
        records.AddRange(Boundary(1, 0, new[] { 0, 0, 100, 0, 100, 60, 0, 60, 0, 0 }));

        //The same shape written clockwise from a different corner. Nothing in GDSII fixes either, so both
        //have to come out as the same rectangle rather than one of them falling through to a polygon.
        records.AddRange(Boundary(1, 0, new[] { 300, 60, 300, 0, 400, 0, 400, 60, 300, 60 }));

        //Four points, right-angled edges, and still not a rectangle: it doubles back on itself. The kind
        //of thing a looser test would wave through.
        records.AddRange(Boundary(1, 1, new[] { 600, 0, 700, 0, 600, 0, 700, 0, 600, 0 }));

        //An L, so the polygon path is exercised by something a rectangle cannot cover.
        records.AddRange(Boundary(2, 5, new[] { 0, 200, 200, 200, 200, 260, 80, 260, 80, 400, 0, 400, 0, 200 }));

        //A triangle: the fewest corners a boundary can have, and no two edges along the same axis.
        records.AddRange(Boundary(3, 0, new[] { -400, -400, -300, -400, -350, -300, -400, -400 }));

        //The three path ends that survive exactly. Even widths, since OASIS stores a half-width and an
        //odd one has nowhere to put its last unit.
        records.AddRange(PathElement(4, 0, 0, 20, null, null, new[] { 0, 600, 300, 600, 300, 900 }));
        records.AddRange(PathElement(4, 1, 2, 20, null, null, new[] { 400, 600, 700, 600 }));
        records.AddRange(PathElement(4, 2, 4, 20, 7, 13, new[] { 800, 600, 1100, 600 }));

        if (withRoundPath)
            records.AddRange(PathElement(4, 3, 1, 20, null, null, new[] { 1200, 600, 1500, 600 }));

        records.AddRange(Text(5, 0, "PIN A", 50, 50));

        //A box is drawn as an area by everything here, so the writer treats it as one.
        records.AddRange(Box(6, 0, new[] { 900, 0, 1000, 0, 1000, 100, 900, 100, 900, 0 }));

        if (withNode)
            records.AddRange(Node(7, 0, new[] { 1200, 0, 1300, 0 }));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));

        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")));

        //Plain, then each of the three other right angles, then a mirror, then the one that no amount of
        //bit-packing covers - an angle that is not a right one, at twice the size.
        records.AddRange(Sref("LEAF", 2000, 0, null, null, false));
        records.AddRange(Sref("LEAF", 4000, 0, 90, null, false));
        records.AddRange(Sref("LEAF", 6000, 0, 180, null, false));
        records.AddRange(Sref("LEAF", 8000, 0, 270, null, false));
        records.AddRange(Sref("LEAF", 10000, 0, null, null, true));
        records.AddRange(Sref("LEAF", 12000, 0, 90, null, true));
        records.AddRange(Sref("LEAF", 14000, 0, 45, 2, false));

        //An angle GDSII is free to write out of range, which has to normalize before it is called a right
        //one rather than falling through to the long form.
        records.AddRange(Sref("LEAF", 16000, 0, 450, null, false));

        //A grid, a single row, a single column, and the degenerate one that is not an array at all.
        records.AddRange(Aref("LEAF", 3, 4, 0, 20000, 500, 700));
        records.AddRange(Aref("LEAF", 5, 1, 0, 30000, 500, 700));
        records.AddRange(Aref("LEAF", 1, 6, 0, 40000, 500, 700));
        records.AddRange(Aref("LEAF", 1, 1, 0, 50000, 500, 700));

        //A skewed array, whose two steps are not along the axes - which is the case a rectangular
        //repetition cannot hold and the two-vector one can. Both spans divide by three, so the steps are
        //whole numbers and the repetition is exact; the one that does not divide has its own test.
        records.AddRange(SkewedAref("LEAF", 3, 3, 0, 60000, 1200, 120, -90, 510));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return GdsTestData.Concat(records.ToArray());
    }

    private static byte[][] Boundary(short layer, short dataType, int[] xy)
    {
        return new[]
        {
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(dataType)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL)
        };
    }

    private static byte[][] Box(short layer, short boxType, int[] xy)
    {
        return new[]
        {
            GdsTestData.Record(RecordType.BOX),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.BOXTYPE, GdsTestData.Int2(boxType)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL)
        };
    }

    private static byte[][] Node(short layer, short nodeType, int[] xy)
    {
        return new[]
        {
            GdsTestData.Record(RecordType.NODE),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.NODETYPE, GdsTestData.Int2(nodeType)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)),
            GdsTestData.Record(RecordType.ENDEL)
        };
    }

    private static byte[][] PathElement(short layer, short dataType, short pathType, int width, int? begin, int? end, int[] xy)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(dataType)),
            GdsTestData.Record(RecordType.PATHTYPE, GdsTestData.Int2(pathType)),
            GdsTestData.Record(RecordType.WIDTH, GdsTestData.Int4(width))
        };

        if (begin is int beginExtension)
            records.Add(GdsTestData.Record(RecordType.BGNEXTN, GdsTestData.Int4(beginExtension)));

        if (end is int endExtension)
            records.Add(GdsTestData.Record(RecordType.ENDEXTN, GdsTestData.Int4(endExtension)));

        records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(xy)));
        records.Add(GdsTestData.Record(RecordType.ENDEL));

        return records.ToArray();
    }

    private static byte[][] Text(short layer, short textType, string value, int x, int y)
    {
        return new[]
        {
            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(textType)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii(value)),
            GdsTestData.Record(RecordType.ENDEL)
        };
    }

    private static byte[][] Sref(string name, int x, int y, double? angle, double? magnification, bool mirrored)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii(name))
        };

        records.AddRange(Transform(angle, magnification, mirrored));

        records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y)));
        records.Add(GdsTestData.Record(RecordType.ENDEL));

        return records.ToArray();
    }

    ///<summary>An axis-aligned array: the two reference points are where the last column and row end.</summary>
    private static byte[][] Aref(string name, short columns, short rows, int x, int y, int columnPitch, int rowPitch)
    {
        return SkewedAref(name, columns, rows, x, y, columnPitch * columns, 0, 0, rowPitch * rows);
    }

    private static byte[][] SkewedAref(string name, short columns, short rows, int x, int y, int spanX, int spanY, int downX, int downY)
    {
        return new[]
        {
            GdsTestData.Record(RecordType.AREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.COLROW, GdsTestData.Int2(columns, rows)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y, x + spanX, y + spanY, x + downX, y + downY)),
            GdsTestData.Record(RecordType.ENDEL)
        };
    }

    private static byte[][] Transform(double? angle, double? magnification, bool mirrored)
    {
        if (angle is null && magnification is null && !mirrored)
            return Array.Empty<byte[]>();

        var records = new List<byte[]>();

        //Bit 0 in the format's numbering is the top bit of the word: mirror about the x axis, before any
        //rotation.
        byte[] flags = { 0x00, 0x00 };

        if (mirrored)
            flags = new byte[] { 0x80, 0x00 };

        records.Add(GdsTestData.Record(RecordType.STRANS, flags));

        if (magnification is double scale)
            records.Add(GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(scale)));

        if (angle is double turn)
            records.Add(GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(turn)));

        return records.ToArray();
    }

    ///<summary>What comes back from a trip out to OASIS and straight back in.</summary>
    private static GDS RoundTrip(GDS gds)
    {
        return OasisReader.Read(OasisWriter.Write(gds));
    }

    #endregion ***********************************************************************



    #region The shape of the file ****************************************************

    [Fact]
    public void What_is_written_is_recognized_as_oasis()
    {
        Assert.True(OasisReader.LooksLikeOasis(OasisWriter.Write(new GDS(Everything()))));
    }

    ///<summary>
    ///The specification fixes the END record at 256 bytes and KLayout enforces it - it refused a file with
    ///a bare END outright. The length is made up by padding, and the padding's own length prefix is part of
    ///what has to add up.
    ///</summary>
    [Fact]
    public void The_end_record_is_padded_to_the_length_the_format_fixes()
    {
        byte[] bytes = OasisWriter.Write(new GDS(Everything()));

        //Back 256 from the end is where the record byte has to be, and 2 is END.
        Assert.Equal(2, bytes[^256]);
    }

    [Fact]
    public void An_empty_library_still_makes_a_readable_file()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(RoundTrip(gds)));
    }

    ///<summary>
    ///How big a database unit is, which is the one thing about a layout the coordinates do not carry.
    ///
    ///Compared as decoded numbers rather than as bytes: a GDSII real is eight bytes of a base-16 float and
    ///a thousandth is not exactly representable in it, so a value that survives perfectly still re-encodes
    ///to 1.0000000000000002e-9.
    ///</summary>
    [Fact]
    public void The_units_survive()
    {
        var before = new GDS(Everything());
        var after = RoundTrip(before);

        var original = (Real8Data)before.StreamFormat.UNITS.Data!;
        var returned = (Real8Data)after.StreamFormat.UNITS.Data!;

        Assert.Equal(original.Values[0], returned.Values[0], 1e-15);
        Assert.Equal(original.Values[1], returned.Values[1], 1e-24);
    }

    ///<summary>
    ///A unit that is not a whole number of database units per micron.
    ///
    ///Which is what an angstrom grid gives - 10000 per micron is whole, but the eight-byte real UNITS holds
    ///it in is not exact, so the division comes out a hair off and the writer has to fall through from the
    ///whole-number form to the double one rather than truncating.
    ///</summary>
    [Fact]
    public void A_unit_that_is_not_a_whole_number_survives()
    {
        var gds = new GDS(GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("ODD")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.00025), GdsTestData.Real8(2.5e-10))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("C")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(40))),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));

        var returned = (Real8Data)RoundTrip(gds).StreamFormat.UNITS.Data!;

        Assert.Equal(2.5e-10, returned.Values[1], 1e-25);
    }

    #endregion ***********************************************************************



    #region What survives the trip ***************************************************

    ///<summary>
    ///The one that matters: every shape in a library with one of everything, in the same place afterwards.
    ///
    ///Flattened, so the eight placements and the five arrays are compared as the geometry they put on the
    ///page - which is what catches a placement written at the right coordinates facing the wrong way.
    ///</summary>
    [Fact]
    public void Everything_with_an_oasis_spelling_survives_the_trip()
    {
        var before = new GDS(Everything());

        Assert.Equal(GdsTestData.Geometry(before), GdsTestData.Geometry(RoundTrip(before)));
    }

    [Fact]
    public void Nothing_is_reported_missing_from_a_library_of_things_that_convert()
    {
        OasisWriter.Write(new GDS(Everything()), out int skipped);

        Assert.Equal(0, skipped);
    }

    ///<summary>
    ///A label is an anchor and a string in both formats, so both come through.
    ///
    ///Its PRESENTATION does not, and there is nothing to be done about that: an OASIS text has no
    ///justification of its own. Asserted so the loss is written down rather than discovered.
    ///</summary>
    [Fact]
    public void A_label_keeps_its_text_and_its_anchor()
    {
        var labels = GdsFlattener.Flatten(RoundTrip(new GDS(Everything())))
            .Elements
            .Where(element => element.Text is not null && element.Text.Length > 0)
            .ToList();

        //One per placement of the leaf, plus the leaf's own - every one of them the same string.
        Assert.NotEmpty(labels);
        Assert.All(labels, label => Assert.Equal("PIN A", label.Text));
    }

    ///<summary>
    ///The hierarchy is kept rather than flattened.
    ///
    ///The whole reason to write this format: a library of cells placed many times stays cells and
    ///placements. Counted on the records that come back, since the flattened comparison above would pass
    ///just as well against a file that had expanded everything.
    ///</summary>
    [Fact]
    public void The_hierarchy_is_kept_rather_than_flattened()
    {
        var after = RoundTrip(new GDS(Everything()));

        Assert.Equal(2, after.StreamFormat.Structures.Count);

        var top = after.StreamFormat.Structures.Single(structure =>
            ((AsciiData)structure.STRNAME.Data!).Value == "TOP");

        //Eight placements, then the arrays expanded to one placement each: 12, 5, 6, 1 and 9.
        Assert.Equal(8 + 12 + 5 + 6 + 1 + 9, top.Elements.Count(element => element.Element is GDS.SrefModel));

        //And nothing was drawn into the top cell, which is what flattening would have done.
        Assert.DoesNotContain(top.Elements, element => element.Element is GDS.BoundaryModel);
    }

    ///<summary>
    ///A GDSII array goes over as one placement carrying a repetition, rather than as one placement per
    ///copy - which is the difference between a record and a hundred of them.
    ///
    ///Measured by size against the same array written out longhand, because the record count on the way
    ///back is the same either way: the reader expands a repetition into one placement per position.
    ///</summary>
    [Fact]
    public void An_array_is_written_as_one_placement_and_a_repetition()
    {
        var withArray = new GDS(GdsTestData.Concat(Library(Aref("LEAF", 20, 20, 0, 0, 500, 500))));
        var longhand = new List<byte[]>();

        for (int column = 0; column < 20; column++)
        {
            for (int row = 0; row < 20; row++)
                longhand.AddRange(Sref("LEAF", column * 500, row * 500, null, null, false));
        }

        var expanded = new GDS(GdsTestData.Concat(Library(longhand.ToArray())));

        //Both draw the same 400 copies.
        Assert.Equal(GdsTestData.Geometry(expanded), GdsTestData.Geometry(RoundTrip(withArray)));

        //
        //
        //The array is still the smaller of the two, and the gap is now small.
        //
        //**Both are collapsed now, which is the point.** It was a tenth when nothing was compressed, a
        //quarter once the cell bodies were, and it is 335 against 413 now that a row of separate placements
        //is found and collapsed like any other run. What is left between them is one dimension: an AREF
        //arrives as a repetition covering a grid both ways, where run-finding takes the rows and leaves
        //twenty of them.
        //
        //So the assertion that carries weight is no longer the ratio between these two. It is that four
        //hundred placements written out one at a time do not cost four hundred placements - under two bytes
        //each, where before they were three and a half.
        //
        int asArray = OasisWriter.Write(withArray).Length;
        int asPlacements = OasisWriter.Write(expanded).Length;

        Assert.True(asArray < asPlacements, $"the array came to {asArray} bytes against {asPlacements} written out longhand");

        Assert.True(asPlacements < 400 * 2, $"four hundred placements came to {asPlacements} bytes, which is what they cost before a run of them was collapsed");
    }

    ///<summary>A library holding one cell of whatever is handed in, and a leaf for it to place.</summary>
    private static byte[][] Library(params byte[][] elements)
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("L")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF"))
        };

        records.AddRange(Boundary(1, 0, GdsTestData.ClosedSquare(100)));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")));

        records.AddRange(elements);

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return records.ToArray();
    }

    ///
    ///A rectangle is written as a RECTANGLE, not as a four-point polygon.
    ///
    ///Most of a layout is rectangles, and the difference is a position and two lengths against a point list
    ///and three g-deltas. A rectangle read back is a boundary either way, so the shape alone cannot say
    ///which record was written and the round-trip tests cannot see this at all.
    ///
    ///**Not by looking for the record byte, which is what this did and why it did nothing.** It asserted
    ///that the values 20 and 21 appeared somewhere in the file - and a record id is one byte with nothing
    ///to distinguish it from a coordinate, a length or a layer number that happens to be 20. Both values
    ///occur constantly as ordinary payload, so it passed on any file at all, including one with no
    ///rectangle in it. Reading the ids properly means decoding every record to know where the next one
    ///starts, which is a second OASIS parser living in the tests.
    ///
    ///So it is asked as a size question instead, which needs no parser and cannot be answered by accident.
    ///
    ///**Against the same rectangle with one corner moved a single unit**, and that pairing is the whole of
    ///why this works. The obvious comparison - a rectangle against a diamond of the same corner count -
    ///does not: measured, the rectangle is the smaller file *either way*, because its steps are axis
    ///aligned and a diagonal g-delta costs more than a straight one. 325 against 334 with the shortcut, and
    ///342 against 343 without it, so a test asking only "is it smaller" passes on a writer that never
    ///writes a RECTANGLE at all. Which is what the first attempt at fixing this did, and what disabling the
    ///shortcut and re-running proved.
    ///
    ///Nudging one corner keeps the steps the same size and the coordinates the same magnitude, and changes
    ///only whether the shape *is* a rectangle: **325 against 333 with the shortcut and 342 against 342
    ///without it** - the same length, to the byte, when both go out as point lists.
    ///
    [Fact]
    public void A_rectangle_costs_less_than_the_same_shape_with_a_corner_moved()
    {
        //Not square, since a square is a case the writer could shorten further and deliberately does not.
        var rectangle = new GDS(GdsTestData.Concat(Library(Boundary(1, 0, new[] { 0, 0, 100, 0, 100, 60, 0, 60, 0, 0 }))));

        //One corner up by one unit: four corners still, three of the edges still on an axis, not a rectangle.
        var nearly = new GDS(GdsTestData.Concat(Library(Boundary(1, 0, new[] { 0, 0, 100, 0, 100, 60, 0, 61, 0, 0 }))));

        int asRectangle = OasisWriter.Write(rectangle).Length;
        int asPolygon = OasisWriter.Write(nearly).Length;

        //Measured at eight. Four, so a change to how a length or a coordinate packs does not fail this for
        //no reason - and nowhere near the zero a writer that stopped using RECTANGLE would produce.
        Assert.True(asPolygon - asRectangle >= 4, $"the rectangle came to {asRectangle} bytes and the same shape with a corner moved to {asPolygon} - too close to say a RECTANGLE was written.");

        //And both are still the shapes that went in, which is the half a size test cannot speak for.
        Assert.Equal(GdsTestData.Geometry(rectangle), GdsTestData.Geometry(RoundTrip(rectangle)));
        Assert.Equal(GdsTestData.Geometry(nearly), GdsTestData.Geometry(RoundTrip(nearly)));
    }

    ///
    ///And an L is not squeezed into one, which is the other half of the same decision.
    ///
    ///A shape the shortcut wrongly accepted would come back as a rectangle - four corners where six went
    ///in - so this is the case the geometry can speak for, and it is asserted here beside the size rather
    ///than left to the corpus.
    ///
    [Fact]
    public void An_l_is_written_as_a_polygon()
    {
        var l = new GDS(GdsTestData.Concat(Library(Boundary(2, 5, new[] { 0, 200, 200, 200, 200, 260, 80, 260, 80, 400, 0, 400, 0, 200 }))));

        var rectangle = new GDS(GdsTestData.Concat(Library(Boundary(1, 0, new[] { 0, 0, 100, 0, 100, 60, 0, 60, 0, 0 }))));

        Assert.True(OasisWriter.Write(l).Length > OasisWriter.Write(rectangle).Length);

        //Six corners in and six corners out, rather than the four a rectangle would have left.
        Assert.Equal(GdsTestData.Geometry(l), GdsTestData.Geometry(RoundTrip(l)));
    }

    #endregion ***********************************************************************



    #region What does not survive, and says so ***************************************

    ///<summary>
    ///A node has no OASIS spelling - it marks an electrical connection rather than an area - so it is
    ///counted rather than dropped in silence.
    ///</summary>
    [Fact]
    public void A_node_is_reported_rather_than_dropped_quietly()
    {
        OasisWriter.Write(new GDS(Everything(withNode: true)), out int skipped);

        Assert.Equal(1, skipped);
    }

    ///<summary>
    ///A round-ended path becomes a square-ended one.
    ///
    ///The format offers three ends - flush, half-width, and a distance - and a semicircle is none of them.
    ///The outline keeps its length and loses its curve, which is a real loss and is written down here so it
    ///is a decision rather than a surprise.
    ///</summary>
    [Fact]
    public void A_round_path_end_comes_back_square()
    {
        var before = new GDS(GdsTestData.Concat(Library(
            PathElement(4, 3, 1, 20, null, null, new[] { 0, 0, 300, 0 }))));

        var original = GdsFlattener.Flatten(before).Elements.Single(element => element.Layer.Key.Number == 4);
        var returned = GdsFlattener.Flatten(RoundTrip(before)).Elements.Single(element => element.Layer.Key.Number == 4);

        //Eight segments a side going in; four corners coming back.
        Assert.True(original.Points.Count > returned.Points.Count);
        Assert.Equal(4, returned.Points.Count);

        //And the same length, so what was lost is the curve rather than the reach: a round end and a
        //half-width one both carry the outline the same distance past the last point.
        Assert.Equal(original.Points.Max(point => point.X), returned.Points.Max(point => point.X));
        Assert.Equal(original.Points.Min(point => point.X), returned.Points.Min(point => point.X));
    }

    ///<summary>
    ///An array whose span does not divide by its count is written a copy at a time.
    ///
    ///GDSII stores where the array ends and divides; OASIS stores the step, and a step is a whole number of
    ///database units - so three copies across four hundred units have no repetition that holds them. Written
    ///out one at a time each copy is within half a unit of where it belongs, where a rounded step would put
    ///the last one further out than the first.
    ///</summary>
    [Fact]
    public void An_array_that_does_not_divide_evenly_stays_where_it_belongs()
    {
        var before = new GDS(GdsTestData.Concat(Library(
            SkewedAref("LEAF", 3, 3, 0, 0, 400, 120, -90, 500))));

        var original = GdsTestData.Geometry(before);
        var returned = GdsTestData.Geometry(RoundTrip(before));

        //The same number of copies, and each of them a whole shape.
        Assert.Equal(original.Count, returned.Count);

        //Every corner within a database unit of the one it came from, which is the most the grid allows.
        var was = Corners(before);
        var now = Corners(RoundTrip(before));

        Assert.Equal(was.Count, now.Count);

        for (int i = 0; i < was.Count; i++)
        {
            Assert.True(Math.Abs(was[i].X - now[i].X) <= 1, $"corner {i} moved from {was[i].X} to {now[i].X}");
            Assert.True(Math.Abs(was[i].Y - now[i].Y) <= 1, $"corner {i} moved from {was[i].Y} to {now[i].Y}");
        }

        //As it happens they all land exactly, because a third and two thirds of a unit round the same way
        //whatever whole number they are added to - only an exact half could disagree. That is worth having
        //rather than settling for, so it is asserted here and the unit of slack above is the guarantee.
        Assert.Equal(was, now);

        //And this really was the awkward case: the same array over a span that divides is one placement and
        //a repetition, where this one is nine placements written out.
        var divides = new GDS(GdsTestData.Concat(Library(
            SkewedAref("LEAF", 3, 3, 0, 0, 402, 120, -90, 501))));

        Assert.True(
            OasisWriter.Write(divides).Length < OasisWriter.Write(before).Length,
            $"the dividing array came to {OasisWriter.Write(divides).Length} bytes and the one that does not to {OasisWriter.Write(before).Length}");
    }

    ///<summary>Every corner a file draws, in a stable order, for comparing two that are nearly the same.</summary>
    private static List<Element.Point> Corners(GDS gds)
    {
        return GdsFlattener.Flatten(gds)
            .Elements
            .SelectMany(element => element.Points)
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToList();
    }

    #endregion ***********************************************************************



    #region The corpus ***************************************************************

    ///
    ///A cell's records go into the file compressed, which is most of what the format is smaller for.
    ///
    ///**Asked of a layout DEFLATE can do something with**, since that is the case the feature is for: two
    ///thousand shapes across eight layers, which is a page of nearly identical records. Measured at 128,106
    ///bytes of GDSII against 5,653 of OASIS - a factor of twenty-two, where the same layout came to about
    ///twenty-four thousand before the bodies were compressed. A tenth is asked for, which the packed form
    ///clears easily and the plain form cannot reach.
    ///
    ///The round trip is the half that matters more. Every byte inside a block is a byte this writer already
    ///produced, so a compression bug cannot make a wrong shape - only a block that will not inflate, and
    ///that fails at the first record read rather than quietly.
    ///
    [Fact]
    public void A_repetitive_layout_is_written_compressed()
    {
        var layout = Synthetic.Layout(2000);

        int asGds = layout.Serialize().Length;
        int asOasis = OasisWriter.Write(layout).Length;

        Assert.True(asOasis * 10 < asGds, $"the layout came to {asOasis} bytes of OASIS against {asGds} of GDSII, which is not the reduction compressing the cell bodies gives.");

        //And it is still the same layout, read back through our own reader.
        Assert.Equal(GdsTestData.Geometry(layout), GdsTestData.Geometry(RoundTrip(layout)));
    }

    ///
    ///And a cell too small to gain is left alone, so nothing is ever made bigger by trying.
    ///
    ///DEFLATE on a few dozen bytes is usually longer than what it compresses, and the block header costs
    ///four or five more on top. Measured on a library holding one rectangle: **308 bytes with the guard and
    ///314 without it**, so the packed form really is the worse one there. Most of this repository's own
    ///examples are standard cells, which is the size where this matters.
    ///
    ///Pinned as a ceiling rather than an equality, so a writer that gets smaller for some other reason does
    ///not fail here - only one that starts paying for a block it does not gain from.
    ///
    [Fact]
    public void A_cell_too_small_to_gain_is_left_uncompressed()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("T")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("A"))
        };

        records.AddRange(Boundary(1, 0, new[] { 0, 0, 100, 0, 100, 60, 0, 60, 0, 0 }));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        var tiny = new GDS(GdsTestData.Concat(records.ToArray()));

        int written = OasisWriter.Write(tiny).Length;

        Assert.True(written <= 308, $"one rectangle came to {written} bytes, where storing it plainly is 308 - a block was written that does not pay for itself.");

        Assert.Equal(GdsTestData.Geometry(tiny), GdsTestData.Geometry(RoundTrip(tiny)));
    }

    ///
    ///A manhattan outline keeps its corners, in order and without gaining one.
    ///
    ///**This is the assertion the rest of the suite cannot make.** GdsTestData.Geometry compares corners as
    ///a sorted, de-duplicated set - so a point list that writes one step too many, which makes the reader
    ///append a corner duplicating the first, comes back looking identical to 897 corpus files and to every
    ///round trip here. The count is exactly the thing kinds 0 and 1 are easy to get wrong: a closed ring
    ///writes N-2 steps and lets the reader work the last corner out.
    ///
    ///So this one reads the corners back in the order the file has them and compares the list.
    ///
    [Fact]
    public void A_manhattan_polygon_keeps_its_corners_in_order()
    {
        //An L: six corners, every edge on an axis, strictly alternating - the case kinds 0 and 1 are for.
        var corners = new[] { 0, 0, 200, 0, 200, 60, 80, 60, 80, 400, 0, 400 };

        Assert.Equal(new[] { "0,0", "200,0", "200,60", "80,60", "80,400", "0,400" }, RoundTripCorners(corners));
    }

    ///
    ///And one with two edges along the same axis falls back rather than being written wrongly.
    ///
    ///A collinear pair is a corner that is not a corner. The reader takes each step's axis from the
    ///alternation rather than from the file, so an outline that goes twice the same way cannot be said in
    ///kind 0 at all - it has to be kind 4, which carries the axis in every step. Nothing in the bundled
    ///corpus has one, so without this fixture the fallback would have no coverage whatsoever.
    ///
    [Fact]
    public void An_outline_with_a_collinear_pair_falls_back_and_still_round_trips()
    {
        //
        //Along the bottom in two hops and back along the top in two, which alternation cannot express.
        //
        //**Six corners, not five.** The first version of this had five, and it fell back for the wrong
        //reason: an odd corner count cannot alternate and close, so manhattanKind refuses it on parity
        //before it ever looks at the edges. Removing the alternation guard left that fixture passing.
        //
        var corners = new[] { 0, 0, 100, 0, 200, 0, 200, 60, 100, 60, 0, 60 };

        Assert.Equal(new[] { "0,0", "100,0", "200,0", "200,60", "100,60", "0,60" }, RoundTripCorners(corners));
    }

    ///<summary>And a shape with no axis-aligned edges at all, which is the other way to reach kind 4.</summary>
    [Fact]
    public void A_diagonal_outline_still_round_trips()
    {
        var corners = new[] { 0, 0, 100, 50, 50, 120 };

        Assert.Equal(new[] { "0,0", "100,50", "50,120" }, RoundTripCorners(corners));
    }

    ///
    ///The corners of a one-shape library, written out and read back, in the file's own order.
    ///
    ///Deliberately not GdsTestData.Geometry, which sorts and de-duplicates - see the test above for why
    ///that matters here and nowhere else.
    ///
    private static List<string> RoundTripCorners(int[] corners)
    {
        byte[] stamps = GdsTestData.Timestamps();

        var closed = corners.ToList();

        //A GDSII boundary repeats its first corner.
        closed.Add(corners[0]);
        closed.Add(corners[1]);

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("ONE")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("ONE"))
        };

        records.AddRange(Boundary(11, 0, closed.ToArray()));
        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        var library = new GDS(GdsTestData.Concat(records.ToArray()));

        var back = GdsFlattener.Flatten(RoundTrip(library)).Elements.Single();

        var read = back.Points.Select(point => FormattableString.Invariant($"{point.X},{point.Y}")).ToList();

        //
        //The ring comes back closed, GDSII style - the first corner again at the end. Dropped here so the
        //list is the corners themselves, which is what an off-by-one in the step count would add to: one
        //step too many makes the reader invent a corner, and it would sit *before* this closing repeat.
        //
        if (read.Count > 1 && read[^1] == read[0])
            read.RemoveAt(read.Count - 1);

        return read;
    }

    ///
    ///A placement leaves out the cell it names when the last one named the same.
    ///
    ///**Isolated by placing the same cell twice against placing two different ones.** Two cells with names
    ///of equal length, so the only thing that can differ between the two files is whether the second record
    ///carries a name at all. Not a run - two placements are never collapsed - so this is the modal name and
    ///nothing else.
    ///
    ///It needs its own test because nothing else can fail on it. Writing the name every time is *correct*,
    ///just longer, so every round trip passes and KLayout is happy; and the corpus, which is where a size
    ///regression would otherwise show, has seven placements in one file out of 897.
    ///
    [Fact]
    public void A_placement_leaves_out_the_cell_the_last_one_named()
    {
        int same = TwoPlacements("AAAA", "AAAA");
        int different = TwoPlacements("AAAA", "BBBB");

        Assert.True(same < different, $"placing one cell twice came to {same} bytes and placing two came to {different} - the repeated name is not being left out.");
    }

    ///<summary>A library of two leaf cells and a top that places the two named, in order.</summary>
    private static int TwoPlacements(string first, string second)
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("TWO")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9)))
        };

        foreach (string name in new[] { "AAAA", "BBBB" })
        {
            records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
            records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)));
            records.AddRange(Boundary(1, 0, GdsTestData.ClosedSquare(100)));
            records.Add(GdsTestData.Record(RecordType.ENDSTR));
        }

        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")));
        records.AddRange(Sref(first, 0, 0, null, null, false));
        records.AddRange(Sref(second, 500, 0, null, null, false));
        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        var library = new GDS(GdsTestData.Concat(records.ToArray()));

        //Both placements come back naming the cell they went in naming, which the modal name could lose.
        Assert.Equal(GdsTestData.Geometry(library), GdsTestData.Geometry(RoundTrip(library)));

        return OasisWriter.Write(library).Length;
    }

    ///
    ///A row of the same rectangle is written once, with a repetition behind it.
    ///
    ///**Compared against the same shapes at irregular spacing**, which is the only comparison that isolates
    ///the feature. Six rectangles are six rectangles either way - same layer, same size, same count, same
    ///corner coordinates in the same range - and only one of the two rows steps evenly enough to collapse.
    ///A test that just checked the even row was small would pass on a writer that never collapsed anything,
    ///since the even row compresses better regardless.
    ///
    [Fact]
    public void A_row_of_the_same_rectangle_is_written_once()
    {
        int even = Row(new[] { 0, 500, 1000, 1500, 2000, 2500 });
        int uneven = Row(new[] { 0, 500, 1100, 1700, 2050, 2500 });

        Assert.True(even < uneven, $"the even row came to {even} bytes and the uneven one to {uneven} - a run that steps evenly is not being collapsed.");
    }

    ///<summary>Six identical rectangles at the given x positions, as a written file, round-tripped.</summary>
    private static int Row(int[] positions)
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("ROW")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("ROW"))
        };

        foreach (int x in positions)
            records.AddRange(Boundary(4, 0, new[] { x, 0, x + 100, 0, x + 100, 60, x, 60, x, 0 }));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        var library = new GDS(GdsTestData.Concat(records.ToArray()));

        //Every one of them comes back where it went in, which a repetition laid out from the wrong end does
        //not - and that is exactly how the corpus caught this being written the wrong way round first time.
        Assert.Equal(GdsTestData.Geometry(library), GdsTestData.Geometry(RoundTrip(library)));

        return OasisWriter.Write(library).Length;
    }

    ///
    ///Three cells whose layers differ, which is the shape the 897 bundled files cannot make.
    ///
    ///**Every one of them holds exactly one cell.** So the corpus - the thing that catches nearly
    ///everything else about this writer - is blind to the whole question of what carries from the last
    ///record of one cell into the first record of the next, and until modal state existed there was nothing
    ///to carry. Now there is.
    ///
    ///The reader keeps layer, datatype and the sizes across a cell boundary and resets only the addressing
    ///mode and the x/y pairs - see OasisReader.resetCellState. This writer keeps the same set, so the two
    ///agree by both not resetting. What this fixture is for is the day one of them changes alone: a reader
    ///that starts resetting, or a writer that does, puts every shape in the second cell on the layer the
    ///first cell ended on, and the file still parses perfectly.
    ///
    ///**The labels are the other half, and their numbers are chosen rather than picked.** textlayer and
    ///texttype are separate modal variables from layer and datatype. A writer sharing one pair between them
    ///only goes wrong when a label's numbers *match the shape before it* - then it leaves them out, and the
    ///reader fills them from its own textlayer, which is whatever the last label said. So the order here is
    ///a label on 42/7, then a shape on 9/1, then a label on 9/1: the last one comes back on 42/7 if the
    ///pairs are shared, and on 9/1 if they are not. A label on some third layer would prove nothing, which
    ///is what this fixture said first time and why it did not catch the mutation it was written for.
    ///
    [Fact]
    public void Layers_carry_across_a_cell_boundary_the_way_the_reader_expects()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("THREE")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9)))
        };

        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("ONE")));

        //A label first, so the reader's text pair is genuinely 42/7 and not its starting zero.
        records.AddRange(Text(42, 7, "FAR", 10, 10));

        //Two shapes on one layer, so the second leaves the layer out - the ordinary case.
        records.AddRange(Boundary(9, 1, new[] { 0, 0, 100, 0, 100, 60, 0, 60, 0, 0 }));
        records.AddRange(Boundary(9, 1, new[] { 200, 0, 300, 0, 300, 60, 200, 60, 200, 0 }));

        //And a label whose numbers are the shapes' numbers. Shared modal state leaves these out and the
        //label comes back on 42/7.
        records.AddRange(Text(9, 1, "PIN", 50, 30));
        records.Add(GdsTestData.Record(RecordType.ENDSTR));

        //Second cell: a different layer, and the same size as the last rectangle so the sizes are modal too.
        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TWO")));
        records.AddRange(Boundary(3, 0, new[] { 0, 0, 100, 0, 100, 60, 0, 60, 0, 0 }));
        records.Add(GdsTestData.Record(RecordType.ENDSTR));

        //Third: back to the first cell's layer, which a writer that reset would write out again.
        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("THREE")));
        records.AddRange(Boundary(9, 1, new[] { 400, 0, 500, 0, 500, 60, 400, 60, 400, 0 }));
        records.Add(GdsTestData.Record(RecordType.ENDSTR));

        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        var library = new GDS(GdsTestData.Concat(records.ToArray()));

        //Every shape on the layer it went in on, in all three cells.
        Assert.Equal(GdsTestData.Geometry(library), GdsTestData.Geometry(RoundTrip(library)));

        //And the layers are genuinely different, so the comparison above has something to be wrong about.
        var layers = GdsFlattener.Flatten(RoundTrip(library)).Elements.Select(element => element.Layer.Key.ToString()).Distinct().OrderBy(each => each).ToList();

        Assert.Equal(new[] { "3/0", "42/7", "9/1" }, layers);

        //And the label on the shapes' own numbers is there, which is the one shared modal state loses.
        var labels = GdsFlattener.Flatten(RoundTrip(library)).Elements
            .Where(element => element.Text == "PIN")
            .Select(element => element.Layer.Key.ToString())
            .ToList();

        Assert.Equal(new[] { "9/1" }, labels);
    }

    ///
    ///What the whole corpus comes to, held so it cannot grow unnoticed.
    ///
    ///**Nothing pinned this before, and the size is the reason the format is written at all.** Every claim
    ///about how small the writer is had been made by a throwaway measurement and then deleted, so a change
    ///that made the output half again as large would have passed the entire suite - the round trips only
    ///ask whether the shapes come back, and they come back either way.
    ///
    ///A ceiling rather than an equality, so a writer that gets *smaller* does not fail here. Deflate's
    ///output is deterministic for a given input, but not across .NET versions, so there is a couple of per
    ///cent of room in it - enough to absorb a library change and nowhere near enough to hide a technique
    ///being lost. Measured at 1,191,819 bytes against the 9,632,982 of GDSII they came from - which is
    ///under the 1,206,187 KLayout writes the same corpus in.
    ///
    ///The file count is asserted too, since a total is only a measurement if it is over what you think.
    ///
    [Fact]
    public void The_corpus_is_no_larger_than_it_was_measured_at()
    {
        long asGds = 0;
        long asOasis = 0;
        int files = 0;

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            byte[] bytes = File.ReadAllBytes(path);

            asGds += bytes.Length;
            asOasis += OasisWriter.Write(new GDS(bytes)).Length;
            files++;
        }

        Assert.Equal(897, files);
        Assert.Equal(9632982, asGds);

        //Invariant, so the number in a failure reads the same wherever it was produced - this machine
        //writes thousands with dots, and a report saying 2.444.116 is a report nobody can search for.
        Assert.True(asOasis <= 1_225_000, FormattableString.Invariant($"the 897 files came to {asOasis:N0} bytes of OASIS, where 1,191,819 was measured - something the writer used to do it is no longer doing."));

        //And a fifth of what it came from, which is the headline the documentation carries.
        Assert.True(asOasis * 5 < asGds, FormattableString.Invariant($"{asOasis:N0} against {asGds:N0} is not the reduction this format is written for."));
    }

    ///<summary>
    ///Every bundled file, written out and read back.
    ///
    ///897 layouts' worth of coordinates, point counts and layer numbers - the part no hand-written fixture
    ///covers. What it does *not* cover is in the class summary, and is why the fixture above exists.
    ///</summary>
    [Fact]
    public void Every_sample_file_survives_the_trip()
    {
        var disagreed = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            string relative = Path.GetRelativePath(GdsTestData.SampleDirectory, path);

            var before = new GDS(File.ReadAllBytes(path));

            try
            {
                if (!GdsTestData.Geometry(before).SequenceEqual(GdsTestData.Geometry(RoundTrip(before))))
                    disagreed.Add($"{relative}: geometry differs");
            }
            catch (Exception problem)
            {
                disagreed.Add($"{relative}: {problem.Message}");
            }
        }

        Assert.Empty(disagreed);
    }

    ///<summary>
    ///Nothing in the corpus is left out. A file that quietly loses a shape on the way through is the
    ///failure this whole exercise is about.
    ///</summary>
    [Fact]
    public void Nothing_in_the_corpus_is_left_out()
    {
        var lost = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            OasisWriter.Write(new GDS(File.ReadAllBytes(path)), out int skipped);

            if (skipped > 0)
                lost.Add($"{Path.GetRelativePath(GdsTestData.SampleDirectory, path)}: {skipped}");
        }

        Assert.Empty(lost);
    }

    #endregion ***********************************************************************



    #region A second reader **********************************************************

    ///<summary>
    ///KLayout reads what this writes.
    ///
    ///The reader's tests are measured against KLayout's writer; this is the same check turned around, and
    ///it is the only one that is not this project agreeing with itself. It carries every case the fixture
    ///has - the placements, the arrays, the paths - so a record this writes in a way only its own reader
    ///forgives is caught here.
    ///
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_what_this_writes()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var before = new GDS(Everything());

        var throughKlayout = new GDS(OasisTestData.ConvertBytesToGds(OasisWriter.Write(before), "writer"));

        Assert.Equal(GdsTestData.Geometry(before), GdsTestData.Geometry(throughKlayout));
    }

    ///<summary>
    ///And a real layout, not only the fixture: the corpus is where the awkward coordinates are.
    ///
    ///One file rather than 897, because each one is a KLayout launch - the corpus round trip above is what
    ///covers the rest, and it has our own reader on the other end.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_a_written_sample()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var before = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        var throughKlayout = new GDS(OasisTestData.ConvertBytesToGds(OasisWriter.Write(before), "mosfet"));

        Assert.Equal(GdsTestData.Geometry(before), GdsTestData.Geometry(throughKlayout));
    }

    #endregion ***********************************************************************
}
