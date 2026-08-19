using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///The ceiling on how large one GDSII record can be, and what happens at it.
///
///**Because the failure this replaces was silent.** A record carries its own total length in a two-byte
///field, and the writer took those two bytes off an int - so a record past 65535 bytes had its length
///written modulo 65536: a small, plausible number, in a file whose bytes were all present and correct.
///Every record after it is framed from the wrong offset, so the next reader does not find a large element
///it cannot cope with, it finds garbage part way through a file that opened fine. Nothing anywhere said so.
///
///`SerializedLength` is an int and does not wrap, so even the buffer was the right size. Only the field lied.
///
public class RecordLengthTests
{
    ///<summary>An XY holds two four-byte coordinates a point, so the ceiling is a number of points.</summary>
    private const int MostPoints = (GDS.Record.MostBytes - 4) / 8;

    private static GDS.Record Coordinates(int points)
    {
        var values = new int[points * 2];

        for (int i = 0; i < points; i++)
        {
            values[i * 2] = i;
            values[(i * 2) + 1] = i * 2;
        }

        return Xy(values);
    }

    ///<summary>An XY record over a flat x,y,x,y list - built from bytes, which is the only way in.</summary>
    private static GDS.Record Xy(int[] values)
    {
        return new GDS.Record((short)RecordType.XY, GdsTestData.Int4(values));
    }

    ///<summary>8191, which is worth having written down somewhere a change to the arithmetic would fail.</summary>
    [Fact]
    public void The_ceiling_is_eight_thousand_one_hundred_and_ninety_one_points()
    {
        Assert.Equal(8191, MostPoints);
    }

    ///
    ///The largest XY there is still writes, both ways out.
    ///
    ///65532 rather than 65535, and the three bytes are not slack that could hold anything: a point is eight
    ///bytes and 8192 of them would be four over the ceiling. So the largest XY is the largest *whole number
    ///of points* that fits, which is what the guard has to let through.
    ///
    [Fact]
    public void A_record_at_the_ceiling_is_written()
    {
        var record = Coordinates(MostPoints);

        int expected = (MostPoints * 8) + 4;

        Assert.Equal(65532, expected);
        Assert.True(expected <= GDS.Record.MostBytes);

        byte[] bytes = record.Serialize();

        Assert.Equal(expected, bytes.Length);

        //And the length field says what the record actually is, rather than what it wrapped to.
        Assert.Equal(expected, (bytes[0] << 8) | bytes[1]);

        var buffer = new byte[GDS.Record.MostBytes];

        Assert.Equal(expected, record.WriteTo(buffer, 0));
    }

    ///
    ///And one point past it is refused rather than written wrong.
    ///
    ///Both routes out, because they are two methods with the same arithmetic in them and a guard on one is
    ///a guard on whichever half the caller happens to use - `WriteTo` fills a whole library in one pass and
    ///`Serialize` hands back a single record, and both are public.
    ///
    [Fact]
    public void A_record_one_point_over_the_ceiling_is_refused()
    {
        var record = Coordinates(MostPoints + 1);

        var thrown = Assert.Throws<InvalidDataException>(() => record.Serialize());

        Assert.Contains("cannot be written", thrown.Message);
        Assert.Contains("8191 points", thrown.Message);

        var buffer = new byte[GDS.Record.MostBytes * 2];

        Assert.Throws<InvalidDataException>(() => record.WriteTo(buffer, 0));
    }

    ///
    ///The specific number that used to get through, held so the wrap can never come back quietly.
    ///
    ///A record of 65540 bytes wrote its length as 4 - a header and nothing else. That is not a value a
    ///reader rejects; it is a perfectly ordinary empty record, and the coordinates behind it were then read
    ///as though they were the records that follow.
    ///
    [Fact]
    public void The_size_that_used_to_wrap_to_a_believable_small_number_is_refused()
    {
        //65540 bytes: four of header and 65536 of payload, which is 8192 points.
        var record = Coordinates(8192);

        Assert.Equal(65540, record.SerializedLength);

        //What two bytes off that int come to, and why it was never noticed.
        Assert.Equal(4, record.SerializedLength % 65536);

        Assert.Throws<InvalidDataException>(() => record.Serialize());
    }

    ///
    ///Whether anything in the app can build one, which decides whether the guard is a backstop or a wall.
    ///
    ///The drawing path cannot: an ellipse's side count is clamped to 512. **A boolean can, easily.** A comb
    ///is an ordinary thing to find on a layout - an interdigitated capacitor, a guard ring, a set of fingers
    ///- and merging one is a single press of Combine. Every tooth adds four corners to one outline, so the
    ///ceiling is about two thousand teeth, which is not a large structure.
    ///
    ///So it is reachable, and Fracture is what meets it: the shape is cut into boundaries that each fit
    ///before the bytes are written. This test builds the record by hand instead, which is the one way past
    ///that - what it holds is the guard underneath, so a shape arriving over the limit by some route
    ///nobody thought of is still refused rather than written wrong.
    ///
    [Fact]
    public void A_merged_comb_goes_past_the_ceiling_and_is_refused()
    {
        var teeth = new List<IReadOnlyList<Element.Point>>();

        //A spine along the bottom, and 2,500 teeth standing on it.
        teeth.Add(new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(2500 * 10, 0),
            new Element.Point(2500 * 10, 5),
            new Element.Point(0, 5)
        });

        for (int i = 0; i < 2500; i++)
        {
            int left = i * 10;

            teeth.Add(new List<Element.Point>
            {
                new Element.Point(left, 0),
                new Element.Point(left + 5, 0),
                new Element.Point(left + 5, 100),
                new Element.Point(left, 100)
            });
        }

        var merged = Booleans.CombineAll(teeth, BooleanOperation.Or);

        //One shape, because they all touch the spine.
        Assert.Single(merged);

        //And past what a record can hold, which is the whole point of this test.
        Assert.True(merged[0].Count > MostPoints, $"The comb merged to {merged[0].Count} points, which is under the ceiling - this test no longer demonstrates what it was written to.");

        var record = Xy(Flat(merged[0]));

        Assert.Throws<InvalidDataException>(() => record.Serialize());
    }

    ///
    ///What each format does with a shape this size, side by side.
    ///
    ///**GDSII has to cut it and OASIS does not**, and that difference is the whole reason the two writers
    ///behave differently here. OASIS counts a point list with an unsigned varint rather than a two-byte
    ///field, so it has no ceiling of this kind - but that is a claim about a specification, and this is the
    ///same comb going through the writers this app ships and back out of the readers it ships.
    ///
    ///The GDSII half is Fracture's doing; see FractureTests for the cut itself. What is asserted here is
    ///the outcome a user sees: both downloads work, and only one of them changes the shape into several.
    ///
    [Fact]
    public void Gdsii_cuts_a_shape_this_size_and_oasis_keeps_it_whole()
    {
        var comb = Comb(2500);

        var merged = Booleans.CombineAll(comb, BooleanOperation.Or);

        Assert.Single(merged);
        Assert.True(merged[0].Count > MostPoints);

        //
        //Into a library the way the download does it, by taking a real one and giving one of its shapes
        //the comb's corners. Built that way rather than from nothing because a library needs a header and
        //units to be written at all, and what is being asked here is about the geometry.
        //
        var source = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var layout = GdsFlattener.Flatten(source);

        layout.Elements[0].Points = new List<Element.Point>(merged[0]);

        var library = LayoutWriter.ToGds(source, layout);

        //GDSII writes it, in pieces - nothing that comes back is over what a record holds.
        var fromGds = new GDS(library.Serialize());

        Assert.All(cornerCounts(fromGds), count => Assert.True(count <= MostPoints - 1, $"A boundary of {count} corners came back out of the GDSII."));

        //OASIS writes it whole, and the shape is there in one piece.
        byte[] oasis = OasisWriter.Write(library);

        Assert.NotEmpty(oasis);

        GDS fromOasis;

        using (var stream = new MemoryStream(oasis))
            fromOasis = OasisReader.Read(stream);

        Assert.Contains(cornerCounts(fromOasis), count => count > MostPoints);
    }

    ///<summary>How many corners each boundary in a library has.</summary>
    private static List<int> cornerCounts(GDS library)
    {
        var counts = new List<int>();

        for (int i = 0; i < library.Records.Count; i++)
        {
            if (library.Records[i].Type != RecordType.BOUNDARY)
                continue;

            for (int j = i + 1; j < library.Records.Count && library.Records[j].Type != RecordType.ENDEL; j++)
            {
                if (library.Records[j].Data is Int4Data xy)
                    counts.Add(xy.Values.Length / 2);
            }
        }

        return counts;
    }

    ///<summary>A spine with `teeth` fingers standing on it - an interdigitated structure, roughly.</summary>
    private static List<IReadOnlyList<Element.Point>> Comb(int teeth)
    {
        var shapes = new List<IReadOnlyList<Element.Point>>();

        shapes.Add(new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(teeth * 10, 0),
            new Element.Point(teeth * 10, 5),
            new Element.Point(0, 5)
        });

        for (int i = 0; i < teeth; i++)
        {
            int left = i * 10;

            shapes.Add(new List<Element.Point>
            {
                new Element.Point(left, 0),
                new Element.Point(left + 5, 0),
                new Element.Point(left + 5, 100),
                new Element.Point(left, 100)
            });
        }

        return shapes;
    }

    ///<summary>A point list as an XY payload holds it - x, y, x, y - and closed on its first point.</summary>
    private static int[] Flat(IReadOnlyList<Element.Point> points)
    {
        var values = new int[(points.Count + 1) * 2];

        for (int i = 0; i < points.Count; i++)
        {
            values[i * 2] = points[i].X;
            values[(i * 2) + 1] = points[i].Y;
        }

        values[points.Count * 2] = points[0].X;
        values[(points.Count * 2) + 1] = points[0].Y;

        return values;
    }
}
