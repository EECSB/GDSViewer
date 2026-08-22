using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Reading a library off a stream rather than out of an array.
///
///The array reader has the whole file to hand and can look ahead; this one has a cursor it cannot rewind,
///so the same guarantees have to be reached a different way. What is worth testing is that it agrees with
///the array reader record for record, that it refuses the same malformed files, and that it survives a
///stream handing over fewer bytes than it was asked for - which is normal behavior for every stream this
///will actually be given and is invisible against a MemoryStream.
///</summary>
public class StreamReadTests
{
    ///<summary>
    ///A stream that never returns more than a few bytes at a time, and cannot be seeked or measured.
    ///
    ///What the browser's file stream and a network stream both are. A MemoryStream answers every read in
    ///full, so a reader that assumes one Read is enough passes every test written against one and then
    ///frames every record after the first chunk boundary from the wrong offset.
    ///</summary>
    private sealed class DribblingStream : Stream
    {
        private readonly byte[] data;
        private readonly int most;
        private int position;

        public DribblingStream(byte[] data, int most)
        {
            this.data = data;
            this.most = most;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int taking = Math.Min(Math.Min(count, most), data.Length - position);

            Array.Copy(data, position, buffer, offset, taking);

            position += taking;

            return taking;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private static byte[] Mosfet()
    {
        return GdsTestData.ReadSample(GdsTestData.MosfetSample);
    }

    #region Agreeing with the array reader *********************************************

    ///<summary>
    ///The whole point: the two readers produce the same library. Compared through Serialize, which is a
    ///byte-for-byte statement about every record's type and payload rather than a count of them.
    ///</summary>
    [Fact]
    public void A_stream_reads_the_same_library_as_an_array()
    {
        byte[] file = Mosfet();

        var fromArray = new GDS(file);
        var fromStream = GDS.FromStream(new MemoryStream(file));

        Assert.Equal(fromArray.Records.Count, fromStream.Records.Count);
        Assert.Equal(fromArray.Serialize(), fromStream.Serialize());
    }

    [Fact]
    public async Task Reading_asynchronously_gives_the_same_library()
    {
        byte[] file = Mosfet();

        var fromStream = await GDS.FromStreamAsync(new MemoryStream(file));

        Assert.Equal(new GDS(file).Serialize(), fromStream.Serialize());
    }

    ///<summary>
    ///And the structural model comes out too, not only the record list - the stream reader has to leave a
    ///library that can be drawn, not one that has to be reparsed.
    ///</summary>
    [Fact]
    public void A_streamed_library_is_built_out_as_well_as_read()
    {
        var gds = GDS.FromStream(new MemoryStream(Mosfet()));

        Assert.NotNull(gds.StreamFormat);
        Assert.NotNull(gds.AdditionalInformation);
        Assert.NotEmpty(gds.AdditionalInformation.Layers);
    }

    ///<summary>
    ///The case a MemoryStream cannot show. One byte at a time is the worst a stream can do and still be
    ///making progress; two and three cross a record header at every possible point.
    ///</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(4096)]
    public void A_stream_that_answers_a_few_bytes_at_a_time_still_reads_the_same_library(int most)
    {
        byte[] file = Mosfet();

        var fromStream = GDS.FromStream(new DribblingStream(file, most));

        Assert.Equal(new GDS(file).Serialize(), fromStream.Serialize());
    }

    [Fact]
    public async Task A_dribbling_stream_reads_the_same_asynchronously()
    {
        byte[] file = Mosfet();

        var fromStream = await GDS.FromStreamAsync(new DribblingStream(file, 3));

        Assert.Equal(new GDS(file).Serialize(), fromStream.Serialize());
    }

    ///<summary>Nothing is seeked or measured, so a stream that cannot do either is enough.</summary>
    [Fact]
    public void A_stream_that_cannot_seek_or_report_its_length_is_enough()
    {
        var source = new DribblingStream(Mosfet(), 64);

        Assert.False(source.CanSeek);
        Assert.Throws<NotSupportedException>(() => source.Length);

        Assert.NotEmpty(GDS.FromStream(source).Records);
    }

    #endregion ***********************************************************************



    #region Refusing the same files **************************************************

    [Fact]
    public void An_empty_stream_is_refused_the_way_an_empty_file_is()
    {
        var problem = Assert.Throws<InvalidDataException>(() => GDS.FromStream(new MemoryStream(Array.Empty<byte>())));

        Assert.Contains("no GDSII records", problem.Message);
    }

    ///<summary>A header cut in half, which is what a download that stopped early looks like.</summary>
    [Fact]
    public void A_stream_ending_inside_a_record_header_is_refused()
    {
        byte[] file = Mosfet();

        var problem = Assert.Throws<InvalidDataException>(() => GDS.FromStream(new MemoryStream(file[..^2])));

        Assert.Contains("too few for a record header", problem.Message);
    }

    ///<summary>
    ///A record that declares more than the stream has left. The array reader catches this from the length
    ///alone; this one only finds out by running out, so it is worth pinning that it does find out.
    ///
    ///Built rather than chopped off a sample. Cutting a byte off Mosfet.gds was the first version of this
    ///and it tests something else entirely: that file ends on ENDLIB, which is a header and no payload, so
    ///one byte short leaves a partial *header* and the other message. A HEADER record declaring a two-byte
    ///payload and carrying one is the case wanted, and it says so on the page.
    ///</summary>
    [Fact]
    public void A_stream_ending_inside_a_payload_is_refused()
    {
        byte[] file = { 0x00, 0x06, 0x00, 0x02, 0x00 };

        var problem = Assert.Throws<InvalidDataException>(() => GDS.FromStream(new MemoryStream(file)));

        Assert.Contains("Truncated GDSII record", problem.Message);
        Assert.Contains("the stream ends inside it", problem.Message);
    }

    ///<summary>Zero would leave the cursor where it is, and the loop would never end.</summary>
    [Fact]
    public void A_record_shorter_than_its_header_is_refused_rather_than_looped_on()
    {
        byte[] file = { 0x00, 0x00, 0x00, 0x02, 0x00, 0x06 };

        var problem = Assert.Throws<InvalidDataException>(() => GDS.FromStream(new MemoryStream(file)));

        Assert.Contains("less than the four-byte header", problem.Message);
    }

    [Fact]
    public void An_odd_record_length_is_refused()
    {
        byte[] file = { 0x00, 0x05, 0x00, 0x02, 0x00, 0x06, 0x00 };

        var problem = Assert.Throws<InvalidDataException>(() => GDS.FromStream(new MemoryStream(file)));

        Assert.Contains("odd", problem.Message);
    }

    ///<summary>
    ///The one message that has to differ. An array knows how much is left before it reads; a stream finds
    ///out by hitting the end, so it says that rather than quoting a count it never had.
    ///</summary>
    [Fact]
    public void The_two_readers_word_a_truncated_payload_differently_and_both_say_where()
    {
        //A HEADER declaring a two-byte payload with one byte behind it.
        byte[] file = { 0x00, 0x06, 0x00, 0x02, 0x00 };

        var fromArray = Assert.Throws<InvalidDataException>(() => new GDS(file));
        var fromStream = Assert.Throws<InvalidDataException>(() => GDS.FromStream(new MemoryStream(file)));

        Assert.Contains("but only", fromArray.Message);
        Assert.Contains("the stream ends inside it", fromStream.Message);

        //Both name the offset of the record that is wrong, which is what makes either message usable.
        Assert.Contains("at offset", fromArray.Message);
        Assert.Contains("at offset", fromStream.Message);
    }

    #endregion ***********************************************************************



    #region The corpus ***************************************************************

    ///<summary>
    ///Every bundled file, both ways. A disagreement on one cell out of 897 is exactly the kind of thing a
    ///second reader introduces, and exactly what a single hand-picked sample would miss.
    ///</summary>
    [Fact]
    public void Every_sample_file_reads_the_same_from_a_stream_as_from_an_array()
    {
        var disagreed = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            byte[] file = File.ReadAllBytes(path);

            var fromArray = new GDS(file);
            var fromStream = GDS.FromStream(new MemoryStream(file));

            if (!fromArray.Serialize().SequenceEqual(fromStream.Serialize()))
                disagreed.Add(Path.GetFileName(path));
        }

        Assert.Empty(disagreed);
    }

    #endregion ***********************************************************************
}
