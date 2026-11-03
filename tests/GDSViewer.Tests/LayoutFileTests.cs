using System.Text;
using GdsII;

namespace GDSViewer.Tests;

///
///<see cref="LayoutFile"/>: the one place a file's format is told off its front, for the CLI, `gds mcp` and the app -
///which each asked the same four questions in the same order until 2026-11-03.
///
public class LayoutFileTests
{
    private static byte[] Mosfet
    {
        get { return GdsTestData.ReadSample(GdsTestData.MosfetSample); }
    }

    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\"></svg>");

    [Fact]
    public void Each_format_is_told_by_what_the_file_starts_with()
    {
        var gds = new GDS(Mosfet);

        Assert.Equal(LayoutFormat.Gdsii, LayoutFile.FormatOf(Mosfet));
        Assert.Equal(LayoutFormat.Oasis, LayoutFile.FormatOf(OasisWriter.Write(gds)));
        Assert.Equal(LayoutFormat.Dxf, LayoutFile.FormatOf(DxfWriter.Write(gds)));
        Assert.Equal(LayoutFormat.Svg, LayoutFile.FormatOf(Svg));
    }

    ///<summary>Nothing recognizable is GDSII, whose reader says what is wrong with it; a sniff that threw would say nothing.</summary>
    [Fact]
    public void Anything_else_is_taken_for_gdsii()
    {
        Assert.Equal(LayoutFormat.Gdsii, LayoutFile.FormatOf(Array.Empty<byte>()));
        Assert.Equal(LayoutFormat.Gdsii, LayoutFile.FormatOf(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void Each_format_has_the_name_the_tools_say()
    {
        Assert.Equal("GDSII", LayoutFile.NameOf(LayoutFormat.Gdsii));
        Assert.Equal("OASIS", LayoutFile.NameOf(LayoutFormat.Oasis));
        Assert.Equal("DXF", LayoutFile.NameOf(LayoutFormat.Dxf));
        Assert.Equal("SVG", LayoutFile.NameOf(LayoutFormat.Svg));
    }

    [Fact]
    public void A_file_reads_as_whichever_it_is()
    {
        var gds = new GDS(Mosfet);
        var expected = GdsTestData.Geometry(gds);

        Assert.Equal(expected, GdsTestData.Geometry(LayoutFile.Read(Mosfet)));
        Assert.Equal(expected, GdsTestData.Geometry(LayoutFile.Read(OasisWriter.Write(gds), out LayoutFormat format, out var problems)));
        Assert.Equal(LayoutFormat.Oasis, format);
        Assert.Empty(problems);

        Assert.NotEmpty(GdsFlattener.Flatten(LayoutFile.Read(DxfWriter.Write(gds))).Elements);
    }

    ///<summary>Off a stream that only goes forward, with the front put back - what standard input and a browser's file are.</summary>
    [Fact]
    public void A_stream_reads_the_same_and_says_its_format()
    {
        var gds = new GDS(Mosfet);

        using var oasis = new MemoryStream(OasisWriter.Write(gds));
        using var plain = new MemoryStream(Mosfet);

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(LayoutFile.Read(oasis, out LayoutFormat first, out _)));
        Assert.Equal(LayoutFormat.Oasis, first);

        Assert.Equal(GdsTestData.Geometry(gds), GdsTestData.Geometry(LayoutFile.Read(plain, out LayoutFormat second, out _)));
        Assert.Equal(LayoutFormat.Gdsii, second);
    }

    ///<summary>And what an OASIS file got wrong comes through it, so the CLI can say so.</summary>
    [Fact]
    public void An_oasis_files_problems_come_through()
    {
        var file = new OasisBuilder();

        file.Cell("ONE");
        file.Rectangle(1, 0, 100, 60, 0, 0);
        file.Cell("TWO");
        file.RectangleAt(500, 0);

        LayoutFile.Read(file.Done(), out _, out var problems);

        Assert.NotEmpty(problems);
    }

    ///<summary>
    ///A stream that can only be read asynchronously, and a few bytes at a time - what the browser hands the app
    ///for a file chosen off a disk. Its Read throws the way the browser's does, so a reader that falls back to it
    ///fails here the way it failed there.
    ///</summary>
    private sealed class AsynchronousStream : Stream
    {
        private readonly byte[] data;
        private int position;

        public AsynchronousStream(byte[] data)
        {
            this.data = data;
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
            throw new NotSupportedException("Synchronous reads are not supported.");
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int taking = Math.Min(Math.Min(buffer.Length, 7), data.Length - position);

            data.AsMemory(position, taking).CopyTo(buffer);

            position += taking;

            return ValueTask.FromResult(taking);
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

    ///<summary>
    ///The browser's file stream refuses a synchronous read, so the front put back has to be read past asynchronously
    ///too - Stream's own ReadAsync would call Read. The app's upload reads every file this way, and every one of
    ///them failed the day the library's class took over from the app's own copy, which had the override.
    ///</summary>
    [Fact]
    public async Task A_stream_that_only_reads_asynchronously_reads_through_the_front_put_back()
    {
        byte[] file = Mosfet;

        using var whole = new ConcatenatedStream(file[..LayoutFile.HeadLength], new AsynchronousStream(file[LayoutFile.HeadLength..]));

        var parsed = await GDS.FromStreamAsync(whole);

        Assert.Equal(new GDS(file).Serialize(), parsed.Serialize());
    }

    ///<summary>And through the older overload, which the base class would also have sent through Read.</summary>
    [Fact]
    public async Task The_array_overload_reads_asynchronously_too()
    {
        byte[] file = Mosfet;
        var buffer = new byte[file.Length];
        int filled = 0;

        using var whole = new ConcatenatedStream(file[..LayoutFile.HeadLength], new AsynchronousStream(file[LayoutFile.HeadLength..]));

        while (true)
        {
            int read = await whole.ReadAsync(buffer, filled, buffer.Length - filled, CancellationToken.None);

            if (read == 0)
                break;

            filled += read;
        }

        Assert.Equal(file, buffer[..filled]);
    }
}
