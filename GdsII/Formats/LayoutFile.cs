namespace GdsII
{
    ///<summary>The four kinds of file this library reads, told apart by what a file starts with - see <see cref="LayoutFile.FormatOf"/>.</summary>
    public enum LayoutFormat
    {
        Gdsii,
        Oasis,
        Dxf,
        Svg
    }

    ///
    ///Which of the four formats a file is, and reading it as that: the one place the question is asked, for the
    ///CLI, `gds mcp` and the app's ways of opening a file - which had the same four tests, in the same order, each.
    ///
    ///**By what the file says it is rather than by what it is called.** The four are told apart at the front -
    ///"%SEMI-OASIS", a DXF's first group code, an SVG's opening tag, and a GDSII HEADER for whatever is left - and an
    ///extension is a guess about a file that the file itself has already answered: a .oas mailed as .gds, or the
    ///other way round, which is what a rename produces, opens correctly either way.
    ///
    public static class LayoutFile
    {
        ///
        ///Enough of the front of a file to tell the four apart: "%SEMI-OASIS\r\n", and enough of a DXF to see past a
        ///comment line to the SECTION after it - which is the longer of the two, so it is the one that sets this.
        ///
        public const int HeadLength = 256;

        public static LayoutFormat FormatOf(ReadOnlySpan<byte> start)
        {
            if (OasisReader.LooksLikeOasis(start))
                return LayoutFormat.Oasis;

            if (DxfReader.LooksLikeAnyDxf(start))
                return LayoutFormat.Dxf;

            if (SvgReader.LooksLikeSvg(start))
                return LayoutFormat.Svg;

            return LayoutFormat.Gdsii;
        }

        ///<summary>The format's name as the CLI and the app say it: GDSII, OASIS, DXF or SVG.</summary>
        public static string NameOf(LayoutFormat format)
        {
            if (format == LayoutFormat.Oasis)
                return "OASIS";

            if (format == LayoutFormat.Dxf)
                return "DXF";

            if (format == LayoutFormat.Svg)
                return "SVG";

            return "GDSII";
        }

        ///<summary>A whole file as a library, whichever of the four it is.</summary>
        public static GDS Read(byte[] bytes)
        {
            return Read(bytes, out _, out _);
        }

        ///
        ///The same, saying which format it was and what the file got wrong that was read past - which only an OASIS
        ///file reports, see <see cref="OasisReader.Read(byte[], out IReadOnlyList{string})"/>.
        ///
        public static GDS Read(byte[] bytes, out LayoutFormat format, out IReadOnlyList<string> problems)
        {
            format = FormatOf(bytes);
            problems = Array.Empty<string>();

            if (format == LayoutFormat.Oasis)
                return OasisReader.Read(bytes, out problems);

            if (format == LayoutFormat.Dxf)
                return DxfReader.Read(bytes);

            if (format == LayoutFormat.Svg)
                return SvgReader.Read(bytes);

            return new GDS(bytes);
        }

        ///
        ///The same off a stream that only goes forward - a file handle, or standard input - whose front is read to
        ///tell the format and then put back in front of the rest. GDSII is read a record at a time off the stream;
        ///the other three have readers that take the whole file, and do.
        ///
        public static GDS Read(Stream source, out LayoutFormat format, out IReadOnlyList<string> problems)
        {
            var head = new byte[HeadLength];
            int filled = 0;

            while (filled < head.Length)
            {
                int read = source.Read(head, filled, head.Length - filled);

                if (read == 0)
                    break;

                filled += read;
            }

            using var whole = new ConcatenatedStream(head[..filled], source);

            format = FormatOf(head.AsSpan(0, filled));
            problems = Array.Empty<string>();

            if (format == LayoutFormat.Oasis)
            {
                using var buffer = new MemoryStream();

                whole.CopyTo(buffer);

                return OasisReader.Read(buffer.ToArray(), out problems);
            }

            if (format == LayoutFormat.Dxf)
                return DxfReader.Read(whole);

            if (format == LayoutFormat.Svg)
                return SvgReader.Read(whole);

            return GDS.FromStream(whole);
        }
    }
}
