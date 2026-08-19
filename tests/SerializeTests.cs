using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers GDS.Serialize. The strongest assertion available is a round trip against the bundled corpus:
///parse a real file, write it back out, and require the bytes to match. That pins the writer to the
///reader on ~9 MB of real layout data rather than on hand-made input.
///</summary>
public class SerializeTests
{
    #region Record framing **************************************************************

    [Fact]
    public void A_record_is_written_as_length_then_type_then_payload()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var layer = gds.Records.First(record => record.Type == RecordType.LAYER);

        byte[] bytes = layer.Serialize();

        Assert.Equal(6, bytes.Length);
        Assert.Equal(0x00, bytes[0]);
        Assert.Equal(0x06, bytes[1]);
        Assert.Equal(0x0D, bytes[2]);
        Assert.Equal(0x02, bytes[3]);
        Assert.Equal(new byte[] { 0x00, 0x05 }, bytes[4..]);
    }

    [Fact]
    public void A_record_with_no_data_is_just_its_four_byte_header()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var endlib = gds.Records.Last();

        Assert.Equal(RecordType.ENDLIB, endlib.Type);
        Assert.Equal(new byte[] { 0x00, 0x04, 0x04, 0x00 }, endlib.Serialize());
    }

    [Fact]
    public void Every_written_record_has_an_even_length()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        foreach (var record in gds.Records)
            Assert.True(record.Serialize().Length % 2 == 0, $"{record.Type} wrote an odd length");
    }

    ///<summary>The length field is recomputed, so growing a value grows the record rather than corrupting it.</summary>
    [Fact]
    public void Editing_a_value_updates_the_record_length()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var libname = gds.Records.First(record => record.Type == RecordType.LIBNAME);

        Assert.Equal(4 + 8, libname.Serialize().Length);//"TESTLIB" padded to 8

        libname.Data = new AsciiData("A_MUCH_LONGER_LIBRARY_NAME");

        Assert.Equal(4 + 26, libname.Serialize().Length);
    }

    #endregion *************************************************************************



    #region Payload encoding ***********************************************************

    ///<summary>
    ///Hand-written canonical encodings again, this time as the expected output, so the writer is not
    ///merely consistent with the reader but correct against the format.
    ///</summary>
    [Theory]
    [InlineData(1.0, new byte[] { 0x41, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(-1.0, new byte[] { 0xC1, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0.5, new byte[] { 0x40, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(2.0, new byte[] { 0x41, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(16.0, new byte[] { 0x42, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0.0, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public void Real8_is_written_in_the_canonical_encoding(double value, byte[] expected)
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var mag = gds.Records.First(record => record.Type == RecordType.UNITS);

        mag.Data = new Real8Data(value);

        Assert.Equal(expected, mag.Serialize()[4..]);
    }

    [Fact]
    public void Int4_coordinates_are_written_big_endian()
    {
        //Built directly rather than inside a library, because this is about the encoder and the framing -
        //and a single coordinate pair is not a boundary the parser would accept.
        var xy = new GDS.Record((short)RecordType.XY, new Int4Data(1, -1).Encode());

        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF, 0xFF },
            xy.Serialize()[4..]);
    }

    [Fact]
    public void An_odd_length_string_is_padded_with_a_null()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var strname = gds.Records.First(record => record.Type == RecordType.STRNAME);

        strname.Data = new AsciiData("ODD");

        byte[] payload = strname.Serialize()[4..];

        Assert.Equal(new byte[] { (byte)'O', (byte)'D', (byte)'D', 0x00 }, payload);
    }

    [Fact]
    public void An_even_length_string_is_written_unpadded()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var strname = gds.Records.First(record => record.Type == RecordType.STRNAME);

        strname.Data = new AsciiData("EVEN");

        Assert.Equal(new byte[] { (byte)'E', (byte)'V', (byte)'E', (byte)'N' }, strname.Serialize()[4..]);
    }

    #endregion *************************************************************************



    #region Round trip *****************************************************************

    ///<summary>
    ///A record type the enum does not know still has to survive a round trip. Its data-type code comes
    ///out of the type word and may be anything at all, so the payload is kept as raw bytes rather than
    ///decoded - dropping it would quietly shrink the file on the way out.
    ///</summary>
    [Theory]
    [InlineData(0x9904)]//data type 4, REAL4, which no known record declares
    [InlineData(0x9A07)]//a data type code outside the enum entirely
    [InlineData(0x9B00)]//claims NODATA but carries a payload anyway
    public void An_unknown_record_type_keeps_its_payload(int typeWord)
    {
        byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF };

        byte[] stream = GdsTestData.Concat(
            GdsTestData.MinimalLibrary(),
            GdsTestData.Record((RecordType)typeWord, payload));

        var gds = new GDS(stream);
        var unknown = gds.Records[^1];

        Assert.NotNull(unknown.Data);
        Assert.Equal(payload, ((RawData)unknown.Data!).Value);
        Assert.Equal(stream, gds.Serialize());
    }

    [Fact]
    public void A_hand_built_library_round_trips_byte_for_byte()
    {
        byte[] original = GdsTestData.MinimalLibrary();

        Assert.Equal(original, new GDS(original).Serialize());
    }

    [Fact]
    public void A_round_tripped_library_reparses_to_the_same_model()
    {
        var original = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var reparsed = new GDS(original.Serialize());

        Assert.Equal(original.Records.Count, reparsed.Records.Count);
        Assert.Equal(((AsciiData)original.StreamFormat.LIBNAME.Data!).Value, ((AsciiData)reparsed.StreamFormat.LIBNAME.Data!).Value);
        Assert.Equal(original.StreamFormat.Structures.Count, reparsed.StreamFormat.Structures.Count);
        Assert.Equal(original.AdditionalInformation.Layers.Count, reparsed.AdditionalInformation.Layers.Count);
        Assert.Equal(original.AsText(), reparsed.AsText());
    }

    ///<summary>
    ///The headline assertion: every bundled file, parsed and written back, must produce the exact bytes
    ///it came from. Failures are collected so a regression shows its whole blast radius, and the first
    ///differing offset is reported to make it diagnosable.
    ///</summary>
    [Fact]
    public void Every_bundled_sample_file_round_trips_byte_for_byte()
    {
        var failures = new List<string>();
        int total = 0;

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            total++;

            byte[] original = File.ReadAllBytes(path);
            byte[] written = new GDS(original).Serialize();

            if (written.AsSpan().SequenceEqual(original))
                continue;

            failures.Add($"{Path.GetFileName(path)}: {DescribeDifference(original, written)}");
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {total} files did not round trip:\n{string.Join("\n", failures.Take(20))}");
    }

    private static string DescribeDifference(byte[] original, byte[] written)
    {
        if (original.Length != written.Length)
            return $"length {original.Length} -> {written.Length}";

        for (int i = 0; i < original.Length; i++)
        {
            if (original[i] != written[i])
                return $"first difference at byte {i}: 0x{original[i]:X2} -> 0x{written[i]:X2}";
        }

        return "no difference found";
    }

    #endregion *************************************************************************



    #region Measuring before writing ****************************************************

    //Serialize sizes one buffer up front from EncodedLength and then fills it, so these two have to
    //agree exactly. If they ever drifted the buffer would be mis-sized - too small throws, too large
    //leaves trailing zeros on the end of a file - and neither says which payload type was wrong. Pinned
    //per type, because the trap is adding a payload type and implementing only one of the pair.

    public static TheoryData<RecordData> EveryPayloadType()
    {
        return new TheoryData<RecordData>
        {
            new Int2Data(600),
            new Int2Data(1, 2, 3, 4, 5),
            new Int4Data(-1000),
            new Int4Data(0, 0, 100, 200),
            new Real8Data(0.001),
            new Real8Data(0.001, 1e-9),
            //Even and odd, since an odd string is padded to even and the length has to know that.
            new AsciiData("EVEN"),
            new AsciiData("ODD"),
            new AsciiData(""),
            new BitArrayData(new byte[] { 0x80, 0x00 }),
            new RawData(RecordDataType.REAL4, new byte[] { 1, 2, 3, 4 })
        };
    }

    [Theory]
    [MemberData(nameof(EveryPayloadType))]
    public void A_payloads_measured_length_is_what_it_encodes_to(RecordData payload)
    {
        Assert.Equal(payload.Encode().Length, payload.EncodedLength);
    }

    [Fact]
    public void A_records_measured_length_is_what_it_serializes_to()
    {
        foreach (var record in new GDS(GdsTestData.MinimalLibrary()).Records)
            Assert.Equal(record.Serialize().Length, record.SerializedLength);
    }

    ///<summary>A record with no payload is its header alone.</summary>
    [Fact]
    public void An_empty_record_measures_four_bytes()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var endlib = gds.Records.Last();

        Assert.Null(endlib.Data);
        Assert.Equal(4, endlib.SerializedLength);
    }

    ///<summary>And the whole file is the sum of its records, which is what the buffer is sized from.</summary>
    [Fact]
    public void A_libraries_measured_length_is_what_it_serializes_to()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        Assert.Equal(gds.Serialize().Length, gds.Records.Sum(record => record.SerializedLength));
    }

    #endregion *************************************************************************
}
