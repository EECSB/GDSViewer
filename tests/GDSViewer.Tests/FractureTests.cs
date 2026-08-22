using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Cutting a shape too large for one GDSII record into several that add up to it.
///
///**Two properties decide whether this works**, and they are the two asserted everywhere below: every piece
///fits, and the pieces together cover exactly what went in. The first is what the file needs. The second is
///what makes it the same layout - and it is checked by area rather than by looking at the corners, because
///the corners are deliberately not the same ones: the cut adds its own.
///
public class FractureTests
{
    ///<summary>A spine with `teeth` fingers standing on it, merged - the shape this exists for.</summary>
    private static List<Element.Point> Comb(int teeth)
    {
        var shapes = new List<IReadOnlyList<Element.Point>>
        {
            new List<Element.Point>
            {
                new Element.Point(0, 0),
                new Element.Point(teeth * 10, 0),
                new Element.Point(teeth * 10, 5),
                new Element.Point(0, 5)
            }
        };

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

        var merged = Booleans.CombineAll(shapes, BooleanOperation.Or);

        Assert.Single(merged);

        return merged[0];
    }

    ///<summary>Twice the area of a ring, by the shoelace sum - twice, so it stays whole numbers.</summary>
    private static long TwiceArea(IReadOnlyList<Element.Point> ring)
    {
        long sum = 0;

        for (int i = 0; i < ring.Count; i++)
        {
            var one = ring[i];
            var next = ring[(i + 1) % ring.Count];

            sum += ((long)one.X * next.Y) - ((long)next.X * one.Y);
        }

        return Math.Abs(sum);
    }

    private static long TwiceArea(IEnumerable<List<Element.Point>> rings)
    {
        long sum = 0;

        foreach (var ring in rings)
            sum += TwiceArea(ring);

        return sum;
    }

    ///<summary>8,190 - one less than a record holds, because the first corner is written again at the end.</summary>
    [Fact]
    public void The_limit_leaves_room_for_the_closing_corner()
    {
        Assert.Equal(8190, Fracture.MostCorners);

        //
        //A shape of that many fits once it is closed, and one more does not.
        //
        //Both halves, because the interesting thing about this number is that it is not the one the byte
        //count suggests. 8,190 corners write as 8,191 points, which is 65,528 bytes with the header - three
        //short of the ceiling, and those three are not slack anything can use, since a point is eight.
        //
        Assert.True(((Fracture.MostCorners + 1) * 8) + 4 <= MostBytes);
        Assert.True(((Fracture.MostCorners + 2) * 8) + 4 > MostBytes);

        Assert.Equal(65532, ((Fracture.MostCorners + 1) * 8) + 4);
    }

    ///<summary>A strip with a zigzag along its top, to a corner count chosen exactly.</summary>
    private static List<Element.Point> Staircase(int corners)
    {
        var ring = new List<Element.Point> { new Element.Point(0, 0), new Element.Point(corners - 3, 0) };

        //Up the right-hand end, then back along the top, one corner a step.
        for (int x = corners - 3; x >= 0; x--)
            ring.Add(new Element.Point(x, 10 + (x % 2)));

        return ring;
    }

    ///
    ///The limit at its own boundary, through the writer rather than through arithmetic.
    ///
    ///**Because the arithmetic test alone does not catch an off-by-one here.** Setting MostCorners one too
    ///high leaves the comb passing: the cut halves a shape until the pieces are well under the limit, so
    ///none of them ever lands on it. This puts a ring of exactly that many corners through the record that
    ///has to hold it, and one more through the cut that has to divide it.
    ///
    [Fact]
    public void A_ring_of_exactly_the_limit_writes_as_one_record_and_one_more_does_not()
    {
        var fits = Staircase(Fracture.MostCorners);

        Assert.Equal(Fracture.MostCorners, fits.Count);
        Assert.Single(Fracture.Into(fits));

        //Written the way a writer writes one, closing corner included. This must not throw.
        var record = new GDS.Record((short)RecordType.XY, new Int4Data(Closed(fits)).Encode());

        Assert.Equal(65532, record.Serialize().Length);

        //And one corner more is over, so it comes back as pieces rather than as itself.
        var over = Staircase(Fracture.MostCorners + 1);

        Assert.Equal(Fracture.MostCorners + 1, over.Count);

        var pieces = Fracture.Into(over);

        Assert.True(pieces.Count > 1);

        foreach (var piece in pieces)
        {
            Assert.True(piece.Count <= Fracture.MostCorners);

            //Every piece writes, which is the only thing the limit is for.
            new GDS.Record((short)RecordType.XY, new Int4Data(Closed(piece)).Encode()).Serialize();
        }
    }

    ///<summary>Corners as an XY holds them, first one repeated at the end.</summary>
    private static int[] Closed(List<Element.Point> ring)
    {
        var values = new int[(ring.Count + 1) * 2];

        for (int i = 0; i < ring.Count; i++)
        {
            values[i * 2] = ring[i].X;
            values[(i * 2) + 1] = ring[i].Y;
        }

        values[ring.Count * 2] = ring[0].X;
        values[(ring.Count * 2) + 1] = ring[0].Y;

        return values;
    }

    ///<summary>A shape that already fits is handed back rather than cut for the sake of it.</summary>
    [Fact]
    public void A_shape_that_fits_is_left_alone()
    {
        var square = new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(100, 0),
            new Element.Point(100, 100),
            new Element.Point(0, 100)
        };

        var pieces = Fracture.Into(square);

        Assert.Single(pieces);
        Assert.Equal(4, pieces[0].Count);
        Assert.Equal(TwiceArea(square), TwiceArea(pieces));
    }

    ///
    ///The comb, which is what somebody actually presses Combine on.
    ///
    [Fact]
    public void A_merged_comb_is_cut_into_pieces_that_each_fit()
    {
        var comb = Comb(2500);

        Assert.True(comb.Count > Fracture.MostCorners, $"The comb came to {comb.Count} corners, which is under the limit - this test no longer demonstrates anything.");

        var pieces = Fracture.Into(comb);

        Assert.True(pieces.Count > 1);

        foreach (var piece in pieces)
            Assert.True(piece.Count <= Fracture.MostCorners, $"A piece came out with {piece.Count} corners, over the {Fracture.MostCorners} a record holds.");

        //And the same ground, exactly - the cut runs along an integer and Clipper works in integers.
        Assert.Equal(TwiceArea(comb), TwiceArea(pieces));
    }

    ///
    ///The rule that makes it terminate, on the shape that breaks the obvious one.
    ///
    ///Halving the bounding box does not work here: every corner of a comb is crowded along the teeth, so a
    ///cut down the geometric middle can leave one side holding nearly all of them and the next round asks
    ///the same question of the same points. Cutting at the median corner makes progress by construction.
    ///
    ///Driven at a small limit so the recursion is deep - 40 corners a piece out of a few thousand is far
    ///more rounds than the real limit ever asks for, which is the point.
    ///
    [Fact]
    public void A_comb_cut_to_a_small_limit_still_terminates_and_still_adds_up()
    {
        var comb = Comb(400);

        var pieces = Fracture.Into(comb, 40);

        Assert.True(pieces.Count > 10);

        foreach (var piece in pieces)
            Assert.True(piece.Count <= 40, $"A piece came out with {piece.Count} corners against a limit of 40.");

        Assert.Equal(TwiceArea(comb), TwiceArea(pieces));
    }

    ///
    ///The shape that decides the cutting rule: a long strip with all its detail crowded into one end.
    ///
    ///**A comb does not decide it**, which is worth saying because a comb is the shape this feature exists
    ///for. Its teeth are spread evenly across its width, so a cut down the middle of the bounding box parts
    ///them as well as a cut at the median corner does - measured, by making the cut the midpoint and
    ///watching every other test here still pass.
    ///
    ///This one is different. A strip a million units wide with a fine zigzag in its first two thousand has
    ///a bounding-box middle at half a million, which is empty: every corner lands on one side, that side is
    ///as large as what went in, and the same question comes round again. The median corner sits inside the
    ///zigzag, so cutting there halves the corners by construction whatever the shape is doing with its
    ///extent.
    ///
    [Fact]
    public void A_strip_with_its_detail_at_one_end_is_still_cut()
    {
        var ring = new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(1000000, 0),
            new Element.Point(1000000, 10)
        };

        //Back along the top, plain until the last two thousand.
        ring.Add(new Element.Point(2000, 10));

        for (int x = 2000; x >= 0; x--)
            ring.Add(new Element.Point(x, 10 + (x % 2)));

        var pieces = Fracture.Into(ring, 40);

        Assert.True(pieces.Count > 10);

        foreach (var piece in pieces)
            Assert.True(piece.Count <= 40, $"A piece came out with {piece.Count} corners against a limit of 40.");

        Assert.Equal(TwiceArea(ring), TwiceArea(pieces));
    }

    ///<summary>And a shape that is tall rather than wide, so the axis choice is exercised both ways.</summary>
    [Fact]
    public void A_tall_comb_is_cut_the_other_way()
    {
        var teeth = new List<IReadOnlyList<Element.Point>>
        {
            new List<Element.Point>
            {
                new Element.Point(0, 0),
                new Element.Point(5, 0),
                new Element.Point(5, 400 * 10),
                new Element.Point(0, 400 * 10)
            }
        };

        for (int i = 0; i < 400; i++)
        {
            int bottom = i * 10;

            teeth.Add(new List<Element.Point>
            {
                new Element.Point(0, bottom),
                new Element.Point(100, bottom),
                new Element.Point(100, bottom + 5),
                new Element.Point(0, bottom + 5)
            });
        }

        var merged = Booleans.CombineAll(teeth, BooleanOperation.Or);

        Assert.Single(merged);

        var pieces = Fracture.Into(merged[0], 40);

        foreach (var piece in pieces)
            Assert.True(piece.Count <= 40);

        Assert.Equal(TwiceArea(merged[0]), TwiceArea(pieces));
    }

    ///
    ///And the whole way through: a library holding one impossible shape, written and read back.
    ///
    ///This is the test that says the feature works, because it goes through the paths a download goes
    ///through - Serialize, then the parser, with nothing in between.
    ///
    [Fact]
    public void A_library_holding_a_shape_too_large_writes_and_reads_back()
    {
        var comb = Comb(2500);

        var source = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var layout = GdsFlattener.Flatten(source);

        layout.Elements[0].Points = new List<Element.Point>(comb);

        var library = LayoutWriter.ToGds(source, layout);

        //Which used to throw, and is the whole of what changed.
        byte[] bytes = library.Serialize();

        Assert.NotEmpty(bytes);

        var read = new GDS(bytes);

        Assert.NotEmpty(read.Records);

        //The shape is there, as several boundaries whose areas add up to the one that went in.
        var back = GdsFlattener.Flatten(read);

        long wrote = TwiceArea(comb);
        long readBack = 0;

        foreach (var element in back.Elements)
        {
            if (element.Points.Count > 2 && TwiceArea(element.Points) > 0)
                readBack += TwiceArea(element.Points);
        }

        //Everything else in the file is there too, so the comb's area is a floor rather than the total.
        Assert.True(readBack >= wrote, $"Read back {readBack} against {wrote} written.");
    }

    ///
    ///**The library on screen is not changed by saving it.**
    ///
    ///Serialize fractures a copy of the record list. Somebody who saves a file mid-edit and carries on has
    ///the shape they made, not the pieces the format needed - and the undo stack still points at records
    ///that exist.
    ///
    [Fact]
    public void Writing_a_file_does_not_change_the_library_it_was_written_from()
    {
        var comb = Comb(2500);

        var source = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var layout = GdsFlattener.Flatten(source);

        layout.Elements[0].Points = new List<Element.Point>(comb);

        var library = LayoutWriter.ToGds(source, layout);

        int before = library.Records.Count;

        library.Serialize();

        Assert.Equal(before, library.Records.Count);

        //And writing it twice gives the same bytes, rather than fracturing what was already fractured.
        Assert.Equal(library.Serialize(), library.Serialize());
    }

    ///
    ///An ordinary file goes through untouched, which is what keeps this free for everybody who never
    ///draws a shape like that. Reference equality: the same list back, not a copy of it.
    ///
    [Fact]
    public void An_ordinary_library_is_not_copied_on_the_way_out()
    {
        var source = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        Assert.Same(source.Records, Fracture.ForGdsii(source.Records));
    }

    ///<summary>And its bytes are unchanged, which is the same claim from the other end.</summary>
    [Fact]
    public void An_ordinary_file_round_trips_to_the_same_bytes()
    {
        byte[] original = File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample));

        var library = new GDS(original);

        Assert.Equal(original, library.Serialize());
    }
}
