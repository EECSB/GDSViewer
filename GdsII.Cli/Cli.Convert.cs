namespace GdsII.Cli
{
    ///<summary>
    ///Converting between the two formats.
    ///
    ///One verb rather than two, because neither side of it is a choice worth making twice: what comes in is
    ///decided by what the file starts with, and what goes out by what the output is called. Naming the
    ///direction as well would be a third thing to keep in agreement with the other two.
    ///
    ///**The hierarchy survives both ways.** This is not the flatten-and-rewrite that <c>boolean</c> and
    ///<c>size</c> do - those flatten because the operation needs it. A conversion has no reason to, so a
    ///library of cells stays a library of cells.
    ///</summary>
    public static partial class Cli
    {
        #region convert *********************************************************************

        private static int convert(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "convert", error, out string path))
                return UsageError;

            string? destination = outputPath(args);

            if (destination is null || destination == "-")
            {
                error.WriteLine("This writes binary, so it needs -o <file>. The name decides the format: .oas for OASIS, .dxf for DXF, anything else for GDSII.");

                return UsageError;
            }

            if (!tryChooseFormat(args, destination, error, out Written written))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            if (written == Written.Oasis)
                return writeOasis(gds!, path, destination, output);

            if (written == Written.Dxf)
                return writeDxf(gds!, path, destination, output);

            byte[] asGds = gds!.Serialize();

            File.WriteAllBytes(destination, asGds);

            output.WriteLine($"Wrote {destination}: GDSII, {gds.StreamFormat.Structures.Count} structure(s).");
            output.WriteLine(sizeLine(path, asGds.Length));

            return Ok;
        }

        ///
        ///What the conversion cost or saved, in bytes and as a fraction of what went in.
        ///
        ///**Said on every conversion, because otherwise nobody can tell.** Whether a writer got smaller or
        ///larger is the kind of thing that changes by accident and is noticed by nobody: there is no size
        ///in the output, so the only way to compare two builds was to convert twice and run `ls`. A line
        ///here makes `gds convert` the measuring stick for anything done to a writer, and it costs one
        ///`FileInfo`.
        ///
        ///Invariant, so a number in a bug report reads the same wherever it was produced.
        ///
        private static string sizeLine(string source, int written)
        {
            long read = new FileInfo(source).Length;

            if (read <= 0)
                return FormattableString.Invariant($"  {written:N0} bytes.");

            return FormattableString.Invariant($"  {written:N0} bytes, {written * 100.0 / read:N1}% of the {read:N0} read.");
        }

        ///<summary>Which of the three is being written, which used to be a bool because there were two.</summary>
        private enum Written
        {
            Gds,
            Oasis,
            Dxf
        }

        private static int writeDxf(GDS gds, string source, string destination, TextWriter output)
        {
            byte[] bytes = DxfWriter.Write(gds);

            File.WriteAllBytes(destination, bytes);

            output.WriteLine($"Wrote {destination}: DXF, {gds.StreamFormat.Structures.Count} cell(s).");
            output.WriteLine(sizeLine(source, bytes.Length));

            //
            //**A DXF layer is a name, so the numbers go into one.** Said out loud because it is the one
            //thing about the conversion somebody has to know before sending the file on: a mask shop
            //expecting layer 68/20 gets a layer called L68D20, and every reader worth the name - this one
            //included - takes the numbers back out of it.
            //
            output.WriteLine("  Layers are named L<layer>D<datatype>, since DXF has no layer numbers.");

            return Ok;
        }

        private static int writeOasis(GDS gds, string source, string destination, TextWriter output)
        {
            byte[] bytes = OasisWriter.Write(gds, out int skipped);

            File.WriteAllBytes(destination, bytes);

            output.WriteLine($"Wrote {destination}: OASIS, {gds.StreamFormat.Structures.Count} cell(s).");
            output.WriteLine(sizeLine(source, bytes.Length));

            //A node has no OASIS spelling - it marks an electrical connection rather than an area. Said out
            //loud, because a shape quietly missing from a converted file is found much later by whoever
            //opens it, and by then the original may be gone.
            if (skipped > 0)
                output.WriteLine($"  {skipped} element(s) had no OASIS equivalent and were left out. NODE elements are the usual reason.");

            return Ok;
        }

        ///<summary>
        ///Which format to write: from --to when it is given, and from the output's own name when it is not.
        ///
        ///The name is the better default. Somebody writing cell.oas has already said what they want, and
        ///having to say it twice is the kind of thing that produces an OASIS file called .gds.
        ///</summary>
        private static bool tryChooseFormat(string[] args, string destination, TextWriter error, out Written written)
        {
            written = Written.Gds;

            string? given = valueOf(args, "--to");

            if (given is null)
            {
                string extension = Path.GetExtension(destination);

                if (extension.Equals(".oas", StringComparison.OrdinalIgnoreCase))
                    written = Written.Oasis;
                else if (extension.Equals(".dxf", StringComparison.OrdinalIgnoreCase))
                    written = Written.Dxf;

                return true;
            }

            switch (given.ToLowerInvariant())
            {
                case "oas":
                case "oasis":
                    written = Written.Oasis;

                    return true;

                case "dxf":
                    written = Written.Dxf;

                    return true;

                case "gds":
                case "gdsii":
                    written = Written.Gds;

                    return true;
            }

            error.WriteLine($"\"{given}\" is not a format. It is gds, oas or dxf.");

            return false;
        }

        #endregion **************************************************************************
    }
}
