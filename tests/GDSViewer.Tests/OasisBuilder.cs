using System.Text;
using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Just enough of an OASIS writer to make a file by hand, for what the writer here never produces: OasisShapeTests has
///its own for the three shape records, and this one is for the modal variables a cell may leave to the cell before -
///see OasisModalTests, and the CLI's validate, which says so about such a file.
///
internal sealed class OasisBuilder
{
    private readonly List<byte> bytes = new List<byte>();

    public OasisBuilder()
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

    ///<summary>A rectangle saying everything: its layer and datatype, its two sizes, and where it is.</summary>
    public void Rectangle(int layer, int dataType, int width, int height, int x, int y)
    {
        Byte(20);
        Byte(0x7B);//L, D, W, H, X and Y all present

        Unsigned((ulong)layer);
        Unsigned((ulong)dataType);
        Unsigned((ulong)width);
        Unsigned((ulong)height);
        Signed(x);
        Signed(y);
    }

    ///<summary>A rectangle saying only where it is, which leaves its layer and sizes to the modal variables.</summary>
    public void RectangleAt(int x, int y)
    {
        Byte(20);
        Byte(0x18);//X and Y alone

        Signed(x);
        Signed(y);
    }

    ///<summary>A placement of a named cell at a point, the way up it comes.</summary>
    public void Placement(string name, int x, int y)
    {
        Byte(17);
        Byte(0xB0);//A name, X and Y

        Text(name);
        Signed(x);
        Signed(y);
    }

    ///<summary>A placement naming no cell, which leaves the cell to the modal variable.</summary>
    public void PlacementAt(int x, int y)
    {
        Byte(17);
        Byte(0x30);//X and Y alone

        Signed(x);
        Signed(y);
    }

    ///
    ///Closes the file off. The END record is padded to exactly 256 bytes, which the specification requires and
    ///KLayout enforces: one byte for the record, two for the padding string's own length, 252 of padding, one for
    ///the validation scheme.
    ///
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

    ///
    ///A GDSII library of three cells for the writer: ONE and TWO holding the same rectangle on 1/0, and THREE the same
    ///rectangle on 9/1. The second cell's records leave the first's out when the writer leans on the cell before, and
    ///the third's size does - which is what KLayout warned about.
    ///
    public static GDS ThreeCellLibrary()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("THREE")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9)))
        };

        foreach ((string name, short layer, short dataType) in new[] { ("ONE", (short)1, (short)0), ("TWO", (short)1, (short)0), ("THREE", (short)9, (short)1) })
        {
            records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
            records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)));
            records.Add(GdsTestData.Record(RecordType.BOUNDARY));
            records.Add(GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)));
            records.Add(GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(dataType)));
            records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0, 100, 60, 0, 60, 0, 0)));
            records.Add(GdsTestData.Record(RecordType.ENDEL));
            records.Add(GdsTestData.Record(RecordType.ENDSTR));
        }

        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return new GDS(GdsTestData.Concat(records.ToArray()));
    }

    ///<summary>The one cell of that name, which every one of these libraries has exactly one of.</summary>
    public static GDS.StructureModel Named(GDS gds, string name)
    {
        return gds.StreamFormat.Structures.Single(structure => ((AsciiData)structure.STRNAME!.Data!).Value == name);
    }
}
