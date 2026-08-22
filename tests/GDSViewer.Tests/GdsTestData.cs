using System.Globalization;
using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>Helpers for building GDSII byte streams by hand and for locating the bundled sample files.</summary>
public static class GdsTestData
{
    #region Record building *************************************************************

    ///<summary>
    ///Builds one GDSII record: a big-endian 2-byte total length (header included), the packed
    ///2-byte type/data-type word, then the payload.
    ///</summary>
    public static byte[] Record(RecordType type, params byte[] data)
    {
        int length = 4 + data.Length;

        var record = new byte[length];

        record[0] = (byte)(length >> 8);
        record[1] = (byte)(length & 0xFF);
        record[2] = (byte)((short)type >> 8);
        record[3] = (byte)((short)type & 0xFF);

        data.CopyTo(record, 4);

        return record;
    }

    public static byte[] Concat(params byte[][] records)
    {
        var stream = new List<byte>();

        foreach (var record in records)
            stream.AddRange(record);

        return stream.ToArray();
    }

    #endregion **************************************************************************



    #region Payload encoding ************************************************************

    ///<summary>Big-endian INT2.</summary>
    public static byte[] Int2(params short[] values)
    {
        var bytes = new List<byte>();

        foreach (short value in values)
        {
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value & 0xFF));
        }

        return bytes.ToArray();
    }

    ///<summary>Big-endian INT4.</summary>
    public static byte[] Int4(params int[] values)
    {
        var bytes = new List<byte>();

        foreach (int value in values)
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }

        return bytes.ToArray();
    }

    ///<summary>
    ///GDSII REAL8: bit 7 of the first byte is the sign, bits 6-0 the excess-64 exponent, and the
    ///remaining 7 bytes a 56-bit mantissa read as a fraction. The value is fraction * 16^exponent.
    ///</summary>
    public static byte[] Real8(double value)
    {
        if (value == 0)
            return new byte[8];

        bool negative = value < 0;
        double magnitude = Math.Abs(value);

        //Normalize so the fraction lands in [1/16, 1), which is what the format expects.
        int exponent = 0;
        while (magnitude >= 1)
        {
            magnitude /= 16;
            exponent++;
        }

        while (magnitude < 1.0 / 16)
        {
            magnitude *= 16;
            exponent--;
        }

        ulong mantissa = (ulong)Math.Round(magnitude * Math.Pow(2, 56));

        var bytes = new byte[8];
        bytes[0] = (byte)(exponent + 64);

        if (negative)
            bytes[0] |= 0x80;

        for (int i = 1; i < 8; i++)
            bytes[i] = (byte)(mantissa >> (8 * (7 - i)));

        return bytes;
    }

    ///<summary>ASCII, null-padded to an even length the way the format requires.</summary>
    public static byte[] Ascii(string text)
    {
        var bytes = new List<byte>();

        foreach (char c in text)
            bytes.Add((byte)c);

        if (bytes.Count % 2 != 0)
            bytes.Add(0);

        return bytes.ToArray();
    }

    #endregion **************************************************************************



    #region Whole streams ***************************************************************

    ///<summary>A timestamp payload for BGNLIB/BGNSTR: two of the format's six-INT2 date triples.</summary>
    public static byte[] Timestamps()
    {
        return Int2(122, 12, 13, 16, 59, 44, 123, 4, 22, 14, 56, 21);
    }

    ///<summary>
    ///A closed square, which is the smallest boundary outline the parser accepts: the format wants at
    ///least four coordinate pairs and the last to repeat the first.
    ///
    ///Fixtures use this rather than two or three loose points because a boundary built that way is not a
    ///boundary - it was only ever accepted because nothing checked.
    ///</summary>
    public static int[] ClosedSquare(int size = 10)
    {
        return new[] { 0, 0, size, 0, size, size, 0, size, 0, 0 };
    }

    ///<summary>
    ///The smallest library the parser accepts: header, library preamble, one structure holding one
    ///boundary, and the closing records.
    ///
    ///stamps replaces the twelve INT2 values of both BGNLIB and BGNSTR, for the timestamp cases - a whole
    ///library is needed even to test one record, because the constructor builds the model tree too.
    ///</summary>
    public static byte[] MinimalLibrary(short layer = 5, int[]? xy = null, short[]? stamps = null)
    {
        xy ??= new[] { 0, 0, 100, 0, 100, 100, 0, 100, 0, 0 };

        byte[] timestamps = Timestamps();

        if (stamps is not null)
            timestamps = Int2(stamps);

        return Concat(
            Record(RecordType.HEADER, Int2(600)),
            Record(RecordType.BGNLIB, timestamps),
            Record(RecordType.LIBNAME, Ascii("TESTLIB")),
            Record(RecordType.UNITS, Concat(Real8(0.001), Real8(1e-9))),
            Record(RecordType.BGNSTR, timestamps),
            Record(RecordType.STRNAME, Ascii("TESTCELL")),
            Record(RecordType.BOUNDARY),
            Record(RecordType.LAYER, Int2(layer)),
            Record(RecordType.DATATYPE, Int2(0)),
            Record(RecordType.XY, Int4(xy)),
            Record(RecordType.ENDEL),
            Record(RecordType.ENDSTR),
            Record(RecordType.ENDLIB));
    }

    ///<summary>
    ///Whether any layer of that number was discovered, whatever its data type.
    ///
    ///Layers are keyed by the layer/datatype pair, so a test that only cares "is layer 7 in this file"
    ///would otherwise have to name a data type it has no interest in - and would then be asserting
    ///something narrower than it means. Tests that *are* about the pair name it explicitly instead.
    ///</summary>
    public static bool HasLayerNumber(GDS gds, short number)
    {
        return gds.AdditionalInformation.Layers.Keys.Any(key => key.Number == number);
    }

    #endregion **************************************************************************



    #region Sample files ****************************************************************

    ///<summary>
    ///Walks up from the test binaries to the repository root, identified by GDSViewer.csproj. The
    ///sample GDS files are served from wwwroot at runtime, so the tests read them in place rather
    ///than copying ~9 MB into the test output.
    ///</summary>
    public static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GDSViewer.csproj")))
                    return directory.FullName;

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException($"Could not find GDSViewer.csproj above {AppContext.BaseDirectory}.");
        }
    }

    public static string SampleDirectory
    {
        get { return Path.Combine(RepositoryRoot, "wwwroot", "resources", "GDS Files"); }
    }

    ///<summary>
    ///The hand-made MOSFET example. It sits in the Sky130 folder alongside the standard cells - the
    ///app loads every example from that one directory - despite not being a sky130 cell.
    ///</summary>
    public static string MosfetSample
    {
        get { return Path.Combine("Sky130 GDS", "Mosfet.gds"); }
    }

    public static string Sky130Sample(string fileName)
    {
        return Path.Combine("Sky130 GDS", fileName);
    }

    ///<summary>
    ///Test input, kept away from wwwroot on purpose: the build globs that folder into the app's example
    ///picker, so a fixture placed there would turn up in the dropdown.
    ///</summary>
    public static byte[] ReadFixture(string fileName)
    {
        return File.ReadAllBytes(Path.Combine(RepositoryRoot, "tests", "GDSViewer.Tests", "fixtures", fileName));
    }

    public static byte[] ReadSample(string relativePath)
    {
        return File.ReadAllBytes(Path.Combine(SampleDirectory, relativePath));
    }

    ///<summary>Every bundled .gds file, sorted so failures are reported in a stable order.</summary>
    public static IEnumerable<string> AllSampleFiles()
    {
        return Directory
            .EnumerateFiles(SampleDirectory, "*.gds", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    ///<summary>
    ///Every shape a file draws, as layer/datatype and a sorted list of its corners.
    ///
    ///What "the same layout" means when the two sides went through different code to get here. Flattened
    ///first, so a hierarchy and the same thing expanded compare equal - which is the point when one side
    ///has been through a format that says a placement differently.
    ///
    ///Corners are sorted and de-duplicated. Nothing in either format says which corner a ring starts at,
    ///which way round it runs, or whether it closes explicitly, and none of those is a difference in what
    ///is drawn.
    ///
    ///Shared by the reader's tests and the writer's on purpose: the two have to agree on what a match is,
    ///and two copies of this would eventually not.
    ///</summary>
    public static List<string> Geometry(GDS gds)
    {
        var layout = GdsFlattener.Flatten(gds);
        var shapes = new List<string>();

        foreach (var element in layout.Elements)
        {
            var corners = element.Points
                .Select(point => $"{point.X},{point.Y}")
                .ToList();

            shapes.Add($"{element.Layer.Key} [{string.Join(' ', corners.Distinct().OrderBy(each => each, StringComparer.Ordinal))}] {element.Text}");
        }

        shapes.Sort(StringComparer.Ordinal);

        return shapes;
    }

    #endregion **************************************************************************



    #region Culture *********************************************************************

    ///<summary>
    ///A culture built to break every numeric assumption at once: a comma for the decimal point, a point
    ///for the group separator, and a negative sign that is not a minus.
    ///
    ///Made up rather than picked from the real list on purpose. A real locale only exercises whichever of
    ///these its ICU data happens to carry on the machine running the test, which is how a culture bug
    ///stays hidden until somebody else runs it.
    ///</summary>
    public static CultureInfo HostileCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();

        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        culture.NumberFormat.NegativeSign = "!";

        return culture;
    }

    ///<summary>Runs body with that culture in force, then puts the real one back.</summary>
    public static T UnderHostileCulture<T>(Func<T> body)
    {
        var previous = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = HostileCulture();

        try
        {
            return body();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    #endregion **************************************************************************
}
