namespace GdsII
{
    ///
    ///A few bytes already read, and then the rest of the stream they came from. For telling a file's format off its
    ///front before reading the whole of it - see <see cref="LayoutFile"/> - over a stream that cannot seek back:
    ///standard input, or a browser's file stream. Public for the app, whose file upload reads the front of the
    ///browser's stream asynchronously and then reads on through this; it kept a copy of its own until 2026-11-03.
    ///
    public sealed class ConcatenatedStream : Stream
    {
        private readonly byte[] head;
        private readonly Stream rest;
        private int at;

        public ConcatenatedStream(byte[] head, Stream rest)
        {
            this.head = head;
            this.rest = rest;
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
            if (at < head.Length)
            {
                int taking = Math.Min(count, head.Length - at);

                Array.Copy(head, at, buffer, offset, taking);

                at += taking;

                return taking;
            }

            return rest.Read(buffer, offset, count);
        }

        ///
        ///Asynchronously as well, and not left to the base class: Stream's own ReadAsync falls back to Read, and the
        ///browser's file stream - what the app's upload reads through this - refuses a synchronous read outright.
        ///The app's own copy of this class had the override and this one did not, and the day the app's copy was
        ///dropped for this one (2026-11-03) every file chosen off a disk failed with "Synchronous reads are not
        ///supported" - 152 browser specs, which is what pins it now.
        ///
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (at < head.Length)
            {
                int taking = Math.Min(buffer.Length, head.Length - at);

                head.AsMemory(at, taking).CopyTo(buffer);

                at += taking;

                return taking;
            }

            return await rest.ReadAsync(buffer, cancellationToken);
        }

        ///<summary>The older overload goes the same way, rather than through the base class and so through Read.</summary>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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
}
