using System.Text;

using GdsII;

namespace GDSViewer.Tests;

///
///The binary flavor of DXF: the same group codes and values, written as bytes rather than as lines.
///
///**Two kinds of test, and both are needed.**
///
///The first few build the bytes here, one field at a time, with the type of every group code written out
///rather than taken from the reader's own table. That is the point of them: if the reader has a code in
///the wrong range, a test that encodes with the same table encodes it wrongly too and passes. These say
///what the specification says.
///
///The last one converts the KLayout drawing the suite already has and reads it both ways. That covers far
///more ground than anything hand-built - every code that a real exporter emits, in the order it emits them
///- and it is only worth anything because the assertions above pin the ranges separately.
///
public class DxfBinaryTests
{
    #region Building the bytes ******************************************************

    ///<summary>The sentinel a binary DXF opens with, and the file starts straight after it.</summary>
    private static void Open(List<byte> into)
    {
        into.AddRange(Encoding.ASCII.GetBytes(DxfBinary.Sentinel));
    }

    ///<summary>A group code: one byte, or 255 and then two more for anything past that.</summary>
    private static void Code(List<byte> into, int code)
    {
        if (code < 255)
        {
            into.Add((byte)code);

            return;
        }

        into.Add(255);
        into.AddRange(BitConverter.GetBytes((short)code));
    }

    private static void Text(List<byte> into, int code, string value)
    {
        Code(into, code);
        into.AddRange(Encoding.Latin1.GetBytes(value));
        into.Add(0);
    }

    private static void Real(List<byte> into, int code, double value)
    {
        Code(into, code);
        into.AddRange(BitConverter.GetBytes(value));
    }

    private static void Short(List<byte> into, int code, short value)
    {
        Code(into, code);
        into.AddRange(BitConverter.GetBytes(value));
    }

    private static void Long(List<byte> into, int code, int value)
    {
        Code(into, code);
        into.AddRange(BitConverter.GetBytes(value));
    }

    ///<summary>A square on one layer, as a whole binary drawing.</summary>
    private static byte[] ASquare(double size = 10, string layer = "M1")
    {
        var bytes = new List<byte>();

        Open(bytes);

        Text(bytes, 0, "SECTION");
        Text(bytes, 2, "ENTITIES");

        Text(bytes, 0, "LWPOLYLINE");
        Text(bytes, 8, layer);
        Long(bytes, 90, 4);
        Short(bytes, 70, 1);

        Real(bytes, 10, 0);
        Real(bytes, 20, 0);
        Real(bytes, 10, size);
        Real(bytes, 20, 0);
        Real(bytes, 10, size);
        Real(bytes, 20, size);
        Real(bytes, 10, 0);
        Real(bytes, 20, size);

        Text(bytes, 0, "ENDSEC");
        Text(bytes, 0, "EOF");

        return bytes.ToArray();
    }

    #endregion **********************************************************************



    #region Telling one apart *******************************************************

    [Fact]
    public void A_binary_drawing_is_recognized_by_what_it_starts_with()
    {
        Assert.True(DxfBinary.LooksLikeBinaryDxf(ASquare()));
        Assert.True(DxfReader.LooksLikeAnyDxf(ASquare()));
    }

    ///<summary>And is not mistaken for the text one, which is what it was refused as before.</summary>
    [Fact]
    public void The_text_reader_still_does_not_claim_it()
    {
        Assert.False(DxfReader.LooksLikeDxf(ASquare()));
    }

    ///<summary>Nor the other way round: a text DXF is not the binary flavor.</summary>
    [Fact]
    public void A_text_drawing_is_not_taken_for_the_binary_one()
    {
        var text = Encoding.ASCII.GetBytes("999\nQCAD\n0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");

        Assert.False(DxfBinary.LooksLikeBinaryDxf(text));
        Assert.True(DxfReader.LooksLikeAnyDxf(text));
    }

    ///<summary>Something short enough to be neither is neither, rather than an index past the end.</summary>
    [Fact]
    public void Something_shorter_than_the_sentinel_is_not_one()
    {
        Assert.False(DxfBinary.LooksLikeBinaryDxf(Encoding.ASCII.GetBytes("AutoCAD")));
        Assert.False(DxfBinary.LooksLikeBinaryDxf(Array.Empty<byte>()));
    }

    #endregion **********************************************************************



    #region The pairs it produces ***************************************************

    ///
    ///Each group code's value is the number of bytes its range says, which is the whole of the format.
    ///
    ///The code is the only thing saying how long the value is, so a range read wrongly does not corrupt one
    ///value - it loses the position, and every pair after it is read out of the middle of something.
    ///
    [Fact]
    public void Every_kind_of_value_is_read_as_its_own_length()
    {
        var bytes = new List<byte>();

        Open(bytes);

        Text(bytes, 1, "a string");
        Real(bytes, 10, -1234.5);
        Short(bytes, 70, -7);
        Long(bytes, 90, 70000);
        Text(bytes, 0, "EOF");

        var pairs = DxfBinary.Pairs(bytes.ToArray());

        Assert.Equal(5, pairs.Count);

        Assert.Equal(new DxfReader.Pair(1, "a string"), pairs[0]);
        Assert.Equal(-1234.5, DxfReader.Number(pairs[1].Value));
        Assert.Equal(new DxfReader.Pair(70, "-7"), pairs[2]);
        Assert.Equal(new DxfReader.Pair(90, "70000"), pairs[3]);
        Assert.Equal(new DxfReader.Pair(0, "EOF"), pairs[4]);
    }

    ///
    ///A code past 255 is written as 255 and then the real one, which is the format's own escape.
    ///
    ///Worth its own case because it is the only place a pair is not one byte and then a value - and 1071,
    ///which is a four-byte extended-data integer, is both past the escape and in a range of its own.
    ///
    [Fact]
    public void A_group_code_past_the_first_byte_is_escaped()
    {
        var bytes = new List<byte>();

        Open(bytes);

        Text(bytes, 1000, "extended");
        Long(bytes, 1071, 123456);
        Text(bytes, 0, "EOF");

        var pairs = DxfBinary.Pairs(bytes.ToArray());

        Assert.Equal(new DxfReader.Pair(1000, "extended"), pairs[0]);
        Assert.Equal(new DxfReader.Pair(1071, "123456"), pairs[1]);
    }

    ///
    ///A double comes back as the same double, to the last bit.
    ///
    ///Everything downstream takes a string and parses it, because that is what the text flavor hands it -
    ///so a binary value goes out through text on the way. A short round trip loses the low bits of a
    ///coordinate, which at a nanometer database unit is exactly the kind of loss nobody sees.
    ///
    [Fact]
    public void A_double_survives_the_trip_through_text()
    {
        var awkward = new double[] { 0.1, 1.0 / 3, -1234.56789012345, 1e-9, 12345678.9012345 };

        var bytes = new List<byte>();

        Open(bytes);

        foreach (double value in awkward)
            Real(bytes, 10, value);

        Text(bytes, 0, "EOF");

        var pairs = DxfBinary.Pairs(bytes.ToArray());

        for (int i = 0; i < awkward.Length; i++)
            Assert.Equal(awkward[i], DxfReader.Number(pairs[i].Value));
    }

    ///<summary>A file that stops partway is read as far as it goes, which is the rule the text one follows.</summary>
    [Fact]
    public void A_drawing_that_stops_partway_is_read_as_far_as_it_goes()
    {
        byte[] whole = ASquare();
        var cut = new byte[whole.Length - 5];

        Array.Copy(whole, cut, cut.Length);

        //Still a drawing with a square in it, rather than nothing at all.
        var drawn = GdsFlattener.Flatten(new GDS(DxfReader.Read(cut).Serialize()));

        Assert.Single(drawn.Elements);
    }

    #endregion **********************************************************************



    #region As a library ************************************************************

    ///<summary>The same square, read through the whole reader rather than only into pairs.</summary>
    [Fact]
    public void A_binary_drawing_becomes_a_library()
    {
        var drawn = GdsFlattener.Flatten(new GDS(DxfReader.Read(ASquare()).Serialize()));

        var box = Bounds.Of(drawn.Elements.Single().Points);

        Assert.Equal(10000, box.Width);
        Assert.Equal(10000, box.Height);
    }

    ///
    ///And the two flavors of the same drawing are the same library, byte for byte.
    ///
    ///**The breadth test.** The KLayout file is converted here rather than fetched, because nothing writes
    ///binary DXF on this machine - what it proves is that the binary path agrees with the text path over
    ///every group code a real exporter emits, in the order it emits them, which is a great deal more than
    ///any file written by hand covers.
    ///
    ///It proves that and not more: a code both paths have in the wrong range would be encoded wrongly here
    ///and read back wrongly, and would match. That is what the ranges above are pinned separately for.
    ///
    [Fact]
    public void The_two_flavors_of_one_drawing_are_the_same_library()
    {
        string path = Path.Combine(GdsTestData.RepositoryRoot, "tests", "fixtures", "klayout-written.dxf");

        string text = File.ReadAllText(path);

        byte[] fromText = DxfReader.Read(text).Serialize();
        byte[] fromBinary = DxfReader.Read(AsBinary(text)).Serialize();

        Assert.Equal(fromText, fromBinary);
    }

    ///
    ///A text DXF written back out as the binary one.
    ///
    ///The types come from the specification's ranges, read off the same table the reader was written
    ///against - which is why this lives here rather than beside it: a converter in the library would be
    ///code nothing ships, and one sharing the reader's own predicates would prove less than this does.
    ///
    private static byte[] AsBinary(string text)
    {
        var bytes = new List<byte>();

        Open(bytes);

        foreach (var pair in DxfReader.Pairs(text))
        {
            if (DxfBinary.IsText(pair.Code))
                Text(bytes, pair.Code, pair.Value);
            else if (DxfBinary.IsDouble(pair.Code))
                Real(bytes, pair.Code, DxfReader.Number(pair.Value));
            else if (DxfBinary.IsShort(pair.Code))
                Short(bytes, pair.Code, (short)DxfReader.Number(pair.Value));
            else if (DxfBinary.IsLong(pair.Code))
                Long(bytes, pair.Code, (int)DxfReader.Number(pair.Value));
            else
                throw new InvalidOperationException($"Group code {pair.Code} has no type in the table.");
        }

        return bytes.ToArray();
    }

    #endregion **********************************************************************
}
