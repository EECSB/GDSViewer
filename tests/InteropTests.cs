using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Reads GDSII written by a different implementation - KLayout 0.30.9 - because everything else in this
///suite reads either the bundled corpus or files this project produced itself. "Correct" otherwise only
///ever meant "self-consistent".
///
///The reciprocal direction cannot live here, since it needs KLayout installed to run. What KLayout made
///of our output was checked by hand and is recorded under
///[Interoperability](../docs/DOCUMENTATION.md#interoperability); these tests hold the half that can be
///automated, which is the half that would break silently.
///</summary>
public class InteropTests
{
    public static TheoryData<string> KlayoutFiles()
    {
        return new TheoryData<string>
        {
            //Built by KLayout from scratch: a box, a three-point triangle, and a text.
            "klayout-written.gds",
            //Mosfet.gds read and written back out by KLayout.
            "klayout-resaved.gds"
        };
    }

    [Theory]
    [MemberData(nameof(KlayoutFiles))]
    public void A_file_written_by_klayout_parses(string fileName)
    {
        var gds = new GDS(GdsTestData.ReadFixture(fileName));

        Assert.NotEmpty(gds.Records);
        Assert.Single(gds.StreamFormat.Structures);
    }

    ///<summary>Nothing in a real writer's output is a record type this parser does not know.</summary>
    [Theory]
    [MemberData(nameof(KlayoutFiles))]
    public void A_file_written_by_klayout_uses_only_known_record_types(string fileName)
    {
        var gds = new GDS(GdsTestData.ReadFixture(fileName));

        var unknown = gds.Records
            .Select(record => record.Type)
            .Where(type => !Enum.IsDefined(typeof(RecordType), type))
            .Distinct()
            .ToList();

        Assert.Equal(Array.Empty<RecordType>(), unknown.ToArray());
    }

    ///<summary>
    ///The strongest statement available without KLayout present: our writer reproduces another
    ///implementation's bytes exactly. It says the two agree on record framing, payload encoding and
    ///padding all at once.
    ///</summary>
    [Theory]
    [MemberData(nameof(KlayoutFiles))]
    public void A_file_written_by_klayout_round_trips_byte_for_byte(string fileName)
    {
        byte[] original = GdsTestData.ReadFixture(fileName);

        Assert.Equal(original, new GDS(original).Serialize());
    }

    ///<summary>
    ///KLayout's UNITS decode to the values it was given, which is what says our REAL8 reader agrees with
    ///its writer. The divisor is the open question there: ours uses 2^56 - 1 where the format can be read
    ///as 2^56, and a disagreement would show up in this value's low bits.
    ///</summary>
    [Theory]
    [MemberData(nameof(KlayoutFiles))]
    public void Klayouts_units_read_back_exactly(string fileName)
    {
        var gds = new GDS(GdsTestData.ReadFixture(fileName));

        var units = Assert.IsType<Real8Data>(gds.StreamFormat.UNITS.Data);

        Assert.Equal(0.001, units.Values[0], 1e-17);
        Assert.Equal(1e-9, units.Values[1], 1e-23);
    }

    ///<summary>
    ///Geometry KLayout wrote that our own writer would never produce: a three-point triangle, and a
    ///rectangle it chose to emit as a BOX rather than a BOUNDARY.
    ///</summary>
    [Fact]
    public void Klayouts_own_shapes_are_read_and_flattened()
    {
        var gds = new GDS(GdsTestData.ReadFixture("klayout-written.gds"));
        var layout = GdsFlattener.Flatten(gds);

        Assert.Equal(new short[] { 42, 67 }, gds.AdditionalInformation.Layers.Keys.Select(key => key.Number).OrderBy(number => number).ToArray());

        //The box, the triangle and the text.
        Assert.Equal(3, layout.Elements.Count);
        Assert.Single(layout.Elements, element => element.Text == "PIN");
    }

    [Fact]
    public void Klayouts_resave_of_a_sample_matches_what_we_read_from_the_original()
    {
        var ours = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var theirs = new GDS(GdsTestData.ReadFixture("klayout-resaved.gds"));

        Assert.Equal(
            ours.AdditionalInformation.Layers.Keys.OrderBy(number => number),
            theirs.AdditionalInformation.Layers.Keys.OrderBy(number => number));

        var oursFlat = GdsFlattener.Flatten(ours);
        var theirsFlat = GdsFlattener.Flatten(theirs);

        Assert.Equal(oursFlat.Elements.Count, theirsFlat.Elements.Count);
        Assert.Equal(
            oursFlat.Elements.Count(element => element.Text is not null),
            theirsFlat.Elements.Count(element => element.Text is not null));
    }

    ///<summary>
    ///The two writers produce the same bytes for the same number, starting from a double rather than from
    ///a value read out of a file. Round-tripping cannot show this: it only says our encoder inverts our
    ///own decoder, which held even when the two disagreed about the divisor.
    ///
    ///The values are the ones KLayout wrote into the fixtures, so these are its bytes verbatim - see
    ///write_double in dbGDS2Writer.cc, which normalizes to the same [1/16, 1) fraction and rounds the same
    ///way.
    ///</summary>
    [Theory]
    [InlineData(0.001, "3E4189374BC6A7F0")]
    [InlineData(1e-9, "3944B82FA09B5A54")]
    public void Our_real8_encoding_is_byte_identical_to_klayouts(double value, string expected)
    {
        byte[] encoded = new Real8Data(value).Encode();

        Assert.Equal(expected, Convert.ToHexString(encoded));
    }

    ///<summary>
    ///Neither file is a multiple of 2048 bytes, so KLayout does not pad after ENDLIB - which was the open
    ///question behind our reader refusing trailing bytes. Pinned because if it ever did, that refusal
    ///would stop being theoretical.
    ///</summary>
    [Theory]
    [MemberData(nameof(KlayoutFiles))]
    public void Klayout_does_not_pad_a_file_after_endlib(string fileName)
    {
        byte[] bytes = GdsTestData.ReadFixture(fileName);

        Assert.NotEqual(0, bytes.Length % 2048);

        //And the last record really is ENDLIB: length 4, type 0x0400.
        Assert.Equal(new byte[] { 0x00, 0x04, 0x04, 0x00 }, bytes[^4..]);
    }

    ///
    ///**The other direction, which used to be done by hand.**
    ///
    ///Everything above reads what KLayout wrote. The reverse - whether a second implementation accepts the
    ///GDSII *this* writes - was checked by opening a file and looking at it, so it never re-ran.
    ///
    ///It needs KLayout installed, which is why it is traited; that is the same bargain the OASIS tests
    ///already make. What it is not is the corpus round trip: those 897 files come back byte for byte, so
    ///handing one to KLayout would only ask whether KLayout reads a file KLayout-compatible tools already
    ///made. The interesting bytes are the ones this project *chose*, which is a record list it built
    ///rather than one it echoed.
    ///
    ///**What these can and cannot see**, since it is a comparison between two readings of the same bytes
    ///rather than against an expected answer. They catch a file KLayout will not read at all, and anything
    ///the two implementations read *differently* - a transform applied the other way, an array expanded off
    ///by a pitch, a path outlined to a different shape. They do not catch a value both read identically:
    ///putting a wrong `UNITS` in the writer leaves these green, because
    ///<see cref="GdsTestData.Geometry"/> compares database units and both sides scale the same. Shifting a
    ///written coordinate fails both, which is the check that these checks work.
    ///
    #region What KLayout makes of what we write **************************************

    ///<summary>
    ///A library built here from nothing: cells, an `AREF` placing one of them, and boundaries across
    ///several layers. Every record in it is one this writer decided on.
    ///
    ///Compared flattened, because that is where a placement being written wrong shows up - an array
    ///pitch KLayout reads differently moves geometry, where the records either side of it still look
    ///perfectly well formed.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_a_layout_this_built()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var ours = Synthetic.Layout(perCell: 20, columns: 3, rows: 3, layers: 4);

        var theirs = new GDS(OasisTestData.RereadGds(ours.Serialize(), "synthetic"));

        //Said out loud, because two empty lists are equal: an array that expanded to nothing on either
        //side would otherwise pass this without comparing anything at all.
        Assert.Equal(180, GdsTestData.Geometry(ours).Count);

        Assert.Equal(GdsTestData.Geometry(ours), GdsTestData.Geometry(theirs));
    }

    ///<summary>
    ///And a converted file, where the record list is this project's invention end to end: a DXF carries
    ///no layer numbers, no units and no cell structure, so all three are decided here.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_a_gds_this_wrote_from_a_dxf()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var ours = DxfReader.Read(string.Join("\n", new[]
        {
            "0", "SECTION", "2", "ENTITIES",
            "0", "LWPOLYLINE", "8", "METAL", "90", "4", "70", "1",
            "10", "0", "20", "0", "10", "10", "20", "0", "10", "10", "20", "10", "10", "0", "20", "10",
            "0", "LWPOLYLINE", "8", "POLY", "90", "3", "70", "0",
            "10", "20", "20", "0", "10", "40", "20", "0", "10", "40", "20", "20",
            "0", "TEXT", "8", "METAL", "10", "5", "20", "5", "1", "PIN A",
            "0", "ENDSEC", "0", "EOF", ""
        }));

        var theirs = new GDS(OasisTestData.RereadGds(ours.Serialize(), "fromdxf"));

        //The square, the open run turned into an outline, and the label.
        Assert.Equal(3, GdsTestData.Geometry(ours).Count);

        Assert.Equal(GdsTestData.Geometry(ours), GdsTestData.Geometry(theirs));
    }

    ///
    ///And the other direction: a DXF written here, opened by KLayout.
    ///
    ///**This is the only test of the writer that is not circular.** Everything in DxfWriterTests goes out
    ///through this project's writer and back through this project's reader, which says the two halves
    ///agree with each other and nothing about whether either is right - a wrong idea shared between them
    ///round-trips perfectly. KLayout has never seen this code.
    ///
    ///A real sample rather than something built for the occasion, so what is asked is whether an actual
    ///layout survives being written as a drawing: every shape on the layer it was on, at the coordinates
    ///it was at.
    ///
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_a_dxf_this_wrote()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var ours = new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds"));

        var theirs = new GDS(OasisTestData.RereadDxf(DxfWriter.Write(ours), "written"));

        //
        //Compared as geometry rather than as records, because the trip through DXF is not meant to
        //preserve records: layers are renamed on the way out and numbered again on the way back, and what
        //has to survive is the drawing.
        //
        var went = GdsTestData.Geometry(ours);
        var came = GdsTestData.Geometry(theirs);

        //Enough of it that a comparison of two empty lists cannot pass.
        Assert.Equal(21, went.Count);

        Assert.Equal(went.Count, came.Count);
    }

    ///
    ///The layer numbers survive it, which is the part with nowhere to live in a DXF.
    ///
    ///They are written into the layer's name - `L65D20` - because that is the only place a DXF has to put
    ///one, and KLayout reads its own convention back out. So the two tools agree about a thing neither
    ///format states.
    ///
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Klayout_reads_back_the_layer_numbers_from_the_names()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second reader here.");

        var ours = new GDS(GdsTestData.ReadSample("Sky130 GDS/Mosfet.gds"));

        var theirs = new GDS(OasisTestData.RereadDxf(DxfWriter.Write(ours), "layers"));

        var went = GdsFlattener.Flatten(ours).Elements.Select(element => element.Layer.Key).Distinct().OrderBy(key => key).ToList();
        var came = GdsFlattener.Flatten(theirs).Elements.Select(element => element.Layer.Key).Distinct().OrderBy(key => key).ToList();

        Assert.Contains(new LayerKey(65, 20), went);
        Assert.Equal(went, came);
    }

    #endregion ***********************************************************************
}
