using System.Text;
using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The three shape records the corpus never produces: TRAPEZOID, CTRAPEZOID and CIRCLE.
///
///**KLayout's OASIS writer does not emit any of them** for the bundled files - checked, by making the
///reader throw on those records and watching all 897 still pass - so the corpus says nothing at all about
///this code. It is also the part that was wrong twice while being written, which is exactly what an
///untested path looks like.
///
///So the file is built here instead, and **KLayout is still the oracle**: it reads the same bytes and
///writes them out as GDSII, and what this reads out of the OASIS has to match what it reads out of
///KLayout's GDSII. Nothing is compared against a table transcribed twice into the same repository, which
///would only prove the two copies agree.
///
///The circle is the exception and is checked directly: GDSII has no circle, so both sides polygonize it
///and there is no reason two tools would choose the same number of segments.
///
///**KLayout has to be installed for these**, since it is the oracle. Traited so a machine without it can
///run everything else with `--filter "Needs!=KLayout"`.
///</summary>
[Trait("Needs", "KLayout")]
public class OasisShapeTests
{
    #region Writing an OASIS file by hand *********************************************

    ///<summary>Just enough of a writer to make a file with the shapes in it.</summary>
    private sealed class Builder
    {
        private readonly List<byte> bytes = new List<byte>();

        public Builder()
        {
            bytes.AddRange(Encoding.ASCII.GetBytes("%SEMI-OASIS\r\n"));

            Byte(1);//START
            Text("1.0");
            Byte(0);//A real of type 0, a positive whole number
            Unsigned(1000);//Database units per micron, matching the bundled samples
            Unsigned(0);//The table offsets are here rather than in END

            for (int i = 0; i < 12; i++)
                Unsigned(0);
        }

        public void Byte(byte value)
        {
            bytes.Add(value);
        }

        ///<summary>Seven bits a byte, low group first, the top bit saying more follows.</summary>
        public void Unsigned(ulong value)
        {
            while (value > 0x7F)
            {
                bytes.Add((byte)((value & 0x7F) | 0x80));

                value >>= 7;
            }

            bytes.Add((byte)value);
        }

        ///<summary>The same, with the sign in the lowest bit.</summary>
        public void Signed(long value)
        {
            ulong magnitude = (ulong)Math.Abs(value);
            ulong signBit = 0UL;

            if (value < 0)
                signBit = 1UL;

            Unsigned((magnitude << 1) | signBit);
        }

        public void Text(string value)
        {
            Unsigned((ulong)value.Length);

            bytes.AddRange(Encoding.ASCII.GetBytes(value));
        }

        public void Cell(string name)
        {
            Byte(14);
            Text(name);
            Byte(15);//XYABSOLUTE
        }

        ///<summary>
        ///Closes the file off.
        ///
        ///**The END record is padded to exactly 256 bytes**, which the specification requires and KLayout
        ///enforces - it refused a file with a bare END outright, where this reader had not noticed. That is
        ///the second reader earning its place before it has compared a single coordinate.
        ///
        ///One byte for the record, two for the padding string's own length, 252 of padding, one for the
        ///validation scheme.
        ///</summary>
        public byte[] Done()
        {
            int before = bytes.Count;

            Byte(2);//END
            Text(new string(' ', 252));//Its padding
            Unsigned(0);//No validation

            if (bytes.Count - before != 256)
                throw new InvalidOperationException($"The END record came to {bytes.Count - before} bytes rather than 256.");

            return bytes.ToArray();
        }
    }

    ///<summary>
    ///One trapezoid. Both dimensions and both position fields are always written, so nothing here depends
    ///on what the record before it left behind - the modal carry-over is the corpus's job.
    ///</summary>
    private static void Trapezoid(Builder file, byte record, bool vertical, int w, int h, int a, int b, int x, int y)
    {
        byte info = 0x40 | 0x20 | 0x10 | 0x08 | 0x02 | 0x01;

        if (vertical)
            info |= 0x80;

        file.Byte(record);
        file.Byte(info);
        file.Unsigned(1);//Layer
        file.Unsigned(0);//Data type
        file.Unsigned((ulong)w);
        file.Unsigned((ulong)h);

        if (record == 23 || record == 24)
            file.Signed(a);

        if (record == 23 || record == 25)
            file.Signed(b);

        file.Signed(x);
        file.Signed(y);
    }

    private static void CTrapezoid(Builder file, byte type, int w, int h, int x, int y)
    {
        file.Byte(26);
        file.Byte(0x80 | 0x40 | 0x20 | 0x10 | 0x08 | 0x02 | 0x01);
        file.Unsigned(1);//Layer
        file.Unsigned(0);//Data type
        file.Byte(type);
        file.Unsigned((ulong)w);
        file.Unsigned((ulong)h);
        file.Signed(x);
        file.Signed(y);
    }

    private static void Circle(Builder file, int radius, int x, int y)
    {
        file.Byte(27);
        file.Byte(0x20 | 0x10 | 0x08 | 0x02 | 0x01);
        file.Unsigned(1);//Layer
        file.Unsigned(0);//Data type
        file.Unsigned((ulong)radius);
        file.Signed(x);
        file.Signed(y);
    }

    #endregion ***********************************************************************



    #region Comparing against KLayout ************************************************

    ///<summary>Every shape, as a sorted list of its distinct corners - the same shape the corpus test uses.</summary>
    private static List<string> Geometry(GDS gds)
    {
        var shapes = new List<string>();

        foreach (var element in GdsFlattener.Flatten(gds).Elements)
        {
            var corners = element.Points
                .Select(point => $"{point.X},{point.Y}")
                .Distinct()
                .OrderBy(each => each, StringComparer.Ordinal);

            shapes.Add(string.Join(' ', corners));
        }

        shapes.Sort(StringComparer.Ordinal);

        return shapes;
    }

    ///<summary>Reads the built file both ways and asserts they agree.</summary>
    private static void AssertKLayoutAgrees(byte[] oasis, string name)
    {
        //Nothing to compare against without it. Asserted rather than skipped quietly: this is the only
        //check these three records have, and a silent skip would read as coverage.
        Assert.True(OasisTestData.Available, "KLayout is needed to check these against a second reader.");

        var mine = OasisReader.Read(oasis);
        var theirs = new GDS(OasisTestData.ConvertBytesToGds(oasis, name));

        Assert.NotEmpty(Geometry(mine));
        Assert.Equal(Geometry(theirs), Geometry(mine));
    }

    ///<summary>
    ///Trapezoids, in both orientations and all three ways of giving the deltas.
    ///
    ///The deltas are kept smaller than the dimension they lean across, so every shape stays inside its own
    ///box and none of them come out degenerate - a zero-area polygon is something the two sides could
    ///legitimately disagree about keeping.
    ///</summary>
    [Fact]
    public void Trapezoids_match_what_klayout_reads()
    {
        var file = new Builder();

        file.Cell("TRAPS");

        int x = 0;

        foreach (byte record in new byte[] { 23, 24, 25 })
        {
            foreach (int a in new[] { 40, -40 })
            {
                foreach (int b in new[] { 30, -30 })
                {
                    Trapezoid(file, record, vertical: true, w: 200, h: 160, a: a, b: b, x: x, y: 0);
                    x += 400;

                    Trapezoid(file, record, vertical: false, w: 200, h: 160, a: a, b: b, x: x, y: 0);
                    x += 400;
                }
            }
        }

        AssertKLayoutAgrees(file.Done(), "traps");
    }

    ///<summary>
    ///All twenty-six named trapezoids.
    ///
    ///The box is sized per type so none of them fold through themselves: the first eight lean across the
    ///height and need a box wider than it is tall, the next eight lean across the width and need the
    ///opposite.
    ///</summary>
    [Fact]
    public void Every_named_trapezoid_matches_what_klayout_reads()
    {
        var file = new Builder();

        file.Cell("CTRAPS");

        for (byte type = 0; type <= 25; type++)
        {
            bool leansAcrossWidth = type >= 8 && type <= 15;

            int w = 200;
            int h = 80;

            if (leansAcrossWidth)
            {
                w = 80;
                h = 200;
            }

            CTrapezoid(file, type, w, h, type * 500, 0);
        }

        AssertKLayoutAgrees(file.Done(), "ctraps");
    }

    #endregion ***********************************************************************



    #region The circle ***************************************************************

    ///<summary>
    ///A circle becomes a polygon, since GDSII has no circle.
    ///
    ///Checked directly rather than against KLayout: both sides have to choose a number of segments and
    ///there is no reason two tools would choose the same one. What is worth asserting is what does not
    ///depend on that choice - that every corner is on the circle, and that it is centered where the record
    ///said.
    ///</summary>
    [Fact]
    public void A_circle_becomes_a_polygon_on_the_circle()
    {
        var file = new Builder();

        file.Cell("CIRCLE");

        Circle(file, radius: 500, x: 1000, y: 2000);

        var element = GdsFlattener.Flatten(OasisReader.Read(file.Done())).Elements.Single();

        Assert.True(element.Points.Count >= 16, $"a circle of {element.Points.Count} corners is not round enough to be one");

        foreach (var point in element.Points)
        {
            double distance = Math.Sqrt(Math.Pow(point.X - 1000, 2) + Math.Pow(point.Y - 2000, 2));

            //Within a database unit: the corners are rounded to whole units on the way out.
            Assert.InRange(distance, 499, 501);
        }

        //And it goes all the way round rather than being an arc.
        Assert.Contains(element.Points, point => point.X > 1400);
        Assert.Contains(element.Points, point => point.X < 600);
        Assert.Contains(element.Points, point => point.Y > 2400);
        Assert.Contains(element.Points, point => point.Y < 1600);
    }

    #endregion ***********************************************************************
}
