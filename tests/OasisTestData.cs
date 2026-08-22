using System.Diagnostics;

namespace GDSViewer.Tests;

///<summary>
///Turns a bundled GDSII sample into an OASIS one, using KLayout.
///
///**Converted rather than committed.** These are KLayout's bytes and not ours, and keeping a second copy
///of the 897-file corpus in the repository to hold them would double it for data that can be regenerated
///in a second. They are written to a cache beside the build output and reused.
///
///**Why KLayout rather than a file written here.** The point of the corpus test is to read something this
///project did not write. A fixture produced by our own writer would only prove the two agree with each
///other, which is the failure mode a format reader has - it is self-consistent and wrong. KLayout is the
///same second implementation the GDSII interoperability checks are measured against.
///
///Skipped rather than failed when KLayout is not installed. This is a machine-specific dependency and a
///developer without it should still be able to run everything else.
///</summary>
public static class OasisTestData
{
    private static readonly object Gate = new object();

    private static string? klayout;
    private static bool searched;

    ///<summary>Where KLayout puts itself on Windows, and what it is called elsewhere.</summary>
    private static readonly string[] Candidates =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KLayout", "klayout_app.exe"),
        @"C:\Program Files\KLayout\klayout_app.exe",
        @"C:\Program Files (x86)\KLayout\klayout_app.exe",
        "/usr/bin/klayout",
        "/usr/local/bin/klayout",
        "/opt/homebrew/bin/klayout"
    };

    public static bool Available
    {
        get { return Find() is not null; }
    }

    ///<summary>
    ///The OASIS form of one sample.
    ///
    ///The whole corpus is converted on the first call and cached after. **One KLayout for all 897 rather
    ///than one each**: launching it is most of the cost, so the per-file version took a quarter of an hour
    ///on a cold cache where this takes seconds. Nothing about the conversion changed - only how many
    ///processes it is spread over.
    ///</summary>
    public static byte[] Convert(string relativePath)
    {
        string target = Path.Combine(CacheDirectory, Path.ChangeExtension(Path.GetFileName(relativePath), ".oas"));

        lock (Gate)
        {
            if (!converted)
            {
                converted = true;

                ConvertEverything();
            }

            if (!File.Exists(target))
                throw new InvalidOperationException($"KLayout did not produce an OASIS form of {relativePath}.");

            return File.ReadAllBytes(target);
        }
    }

    private static bool converted;

    ///<summary>
    ///Hands KLayout an OASIS file made here and takes back the GDSII it reads out of it.
    ///
    ///The other direction from <see cref="Convert"/>, and for the records the bundled corpus never
    ///produces - it is what lets a hand-built trapezoid be checked against a second reader rather than
    ///against the table it was written from.
    ///</summary>
    public static byte[] ConvertBytesToGds(byte[] oasis, string name)
    {
        return Reread(oasis, $"{name}.built.oas", $"{name}.built.gds", $"the OASIS built for {name}");
    }

    ///<summary>
    ///The same, for GDSII this project wrote: KLayout reads it and writes it back out.
    ///
    ///The direction nothing covered. What KLayout writes is read here from committed fixtures, and what it
    ///makes of the OASIS this writes has its own tests - but whether a second implementation accepts the
    ///**GDSII** this writes was only ever checked by opening a file and looking at it.
    ///</summary>
    public static byte[] RereadGds(byte[] gds, string name)
    {
        return Reread(gds, $"{name}.ours.gds", $"{name}.theirs.gds", $"the GDSII written for {name}");
    }

    ///<summary>
    ///The same for a DXF: KLayout reads it and writes GDSII out, which is what says the drawing is one.
    ///
    ///The GDSII coming back is the point rather than an accident of the helper - it is the format both
    ///sides can be compared in, and a DXF read wrongly comes back as geometry in the wrong places rather
    ///than as an error.
    ///</summary>
    public static byte[] RereadDxf(byte[] dxf, string name)
    {
        return Reread(dxf, $"{name}.ours.dxf", $"{name}.theirs.gds", $"the DXF written for {name}");
    }

    ///<summary>Hands a file to KLayout and gives back whatever it writes out again.</summary>
    private static byte[] Reread(byte[] bytes, string sourceName, string targetName, string describe)
    {
        string source = Path.Combine(CacheDirectory, sourceName);
        string target = Path.Combine(CacheDirectory, targetName);

        lock (Gate)
        {
            File.WriteAllBytes(source, bytes);

            if (File.Exists(target))
                File.Delete(target);

            RunScript(
                "layout = RBA::Layout.new\nlayout.read($input)\nlayout.write($output)\n",
                source,
                target);

            if (!File.Exists(target))
                throw new InvalidOperationException($"KLayout would not read {describe}.");

            return File.ReadAllBytes(target);
        }
    }

    private static string CacheDirectory
    {
        get
        {
            string directory = Path.Combine(Path.GetTempPath(), "GDSViewer.OasisFixtures");

            Directory.CreateDirectory(directory);

            return directory;
        }
    }

    ///<summary>
    ///Converts every sample that is not already cached and current, in one run.
    ///
    ///The script skips a file whose conversion is newer than it, so editing a sample regenerates only that
    ///one - and carries on past a file it cannot read rather than abandoning the rest, since a single
    ///awkward sample should cost its own row in the corpus test and not the whole run.
    ///</summary>
    private static void ConvertEverything()
    {
        string? tool = Find();

        if (tool is null)
            throw new InvalidOperationException("KLayout is needed to make the OASIS fixtures and was not found.");

        string script = Path.Combine(CacheDirectory, "convert.rb");

        File.WriteAllText(script, string.Join('\n',
            "Dir.glob(File.join($input, \"**\", \"*.gds\")).sort.each do |path|",
            "  target = File.join($output, File.basename(path, \".gds\") + \".oas\")",
            "  next if File.exist?(target) && File.mtime(target) >= File.mtime(path)",
            "  begin",
            "    layout = RBA::Layout.new",
            "    layout.read(path)",
            "    layout.write(target)",
            "  rescue => problem",
            "    $stderr.puts(\"#{path}: #{problem}\")",
            "  end",
            "end",
            ""));

        //Forward slashes, because the script hands these to Ruby's own path handling and a Windows
        //backslash in a double-quoted Ruby string is an escape.
        string errors = RunScript(script, GdsTestData.SampleDirectory, CacheDirectory, alreadyWritten: true);

        if (Directory.GetFiles(CacheDirectory, "*.oas").Length == 0)
            throw new InvalidOperationException($"KLayout produced no OASIS fixtures. {errors}");
    }

    ///<summary>
    ///The area KLayout's own boolean engine gets for one operation between two layers, in square database
    ///units.
    ///
    ///A second engine with no shared code, which is the only way to check a boolean result that is not
    ///checking it against the thing that produced it. The operator is KLayout's own - `&amp;`, `|`, `-`, `^`
    ///on a Region.
    ///</summary>
    public static double RegionArea(string relativePath, int aLayer, int aType, string theirOperator, int bLayer, int bType)
    {
        string source = Path.Combine(GdsTestData.SampleDirectory, relativePath);
        string target = Path.Combine(CacheDirectory, "region.txt");

        lock (Gate)
        {
            if (File.Exists(target))
                File.Delete(target);

            //Written to a file rather than read off standard output, which carries KLayout's own banner.
            string script = string.Join('\n',
                "layout = RBA::Layout.new",
                "layout.read($input)",
                "top = layout.top_cell",
                $"a = RBA::Region.new(top.begin_shapes_rec(layout.layer({aLayer}, {aType})))",
                $"b = RBA::Region.new(top.begin_shapes_rec(layout.layer({bLayer}, {bType})))",
                $"r = a {theirOperator} b",
                "File.write($output, r.area.to_s)",
                "");

            string errors = RunScript(script, source, target);

            if (!File.Exists(target))
                throw new InvalidOperationException($"KLayout would not work out that region. {errors}");

            return double.Parse(File.ReadAllText(target), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    ///<summary>
    ///How many violations KLayout's own rule engine finds on one layer of one file.
    ///
    ///**The only outside opinion this feature can get.** Every other test of the checker measures it
    ///against itself: the boundary cases say the arithmetic does what it was designed to, and none of them
    ///can say the design agrees with anybody. KLayout has its own engine, its own metrics and no shared
    ///code, so a rule both are pointed at is the one question worth asking.
    ///
    ///**The number is edge pairs and ours is regions**, so it is not comparable as a count - one region too
    ///narrow can be two edges facing each other, or twenty. What is comparable, and what the tests assert,
    ///is whether either engine found anything at all: that is the question a rule check exists to answer.
    ///
    ///<paramref name="check"/> is `width` or `space`, and <paramref name="limit"/> is in database units,
    ///which is what a deck here is written in.
    ///</summary>
    public static int RuleViolations(string absolutePath, int layer, int type, string check, int limit, bool square = false)
    {
        string metric = "Euclidian";

        if (square)
            metric = "Square";

        return RuleViolations(absolutePath, layer, type, check, limit, metric);
    }

    ///<summary>
    ///The same, naming the metric KLayout is to use.
    ///
    ///**Worth having once this side answers in edge pairs too**, because then the counts are comparable -
    ///which they are not against the region checks, where one region too narrow is any number of edges
    ///facing each other. `Euclidian` is KLayout's own spelling of it.
    ///</summary>
    public static int RuleViolations(string absolutePath, int layer, int type, string check, int limit, string metricName)
    {
        string target = Path.Combine(CacheDirectory, "rule.txt");

        lock (Gate)
        {
            if (File.Exists(target))
                File.Delete(target);

            string metric = $"RBA::Region::{metricName}";

            string script = string.Join('\n',
                "layout = RBA::Layout.new",
                "layout.read($input)",
                "top = layout.top_cell",
                $"r = RBA::Region.new(top.begin_shapes_rec(layout.layer({layer}, {type})))",
                $"found = r.{check}_check({limit}, false, {metric}, nil, nil, nil)",
                "File.write($output, found.size.to_s)",
                "");

            string errors = RunScript(script, absolutePath, target);

            if (!File.Exists(target))
                throw new InvalidOperationException($"KLayout would not run that check. {errors}");

            return int.Parse(File.ReadAllText(target), System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    ///<summary>
    ///What KLayout makes of a report database this project wrote: its categories, and how many items.
    ///
    ///The interop standard the rest of this repository is held to, applied to the one format here that
    ///somebody else defined. A file KLayout cannot open is a file nobody can act on, and nothing on this
    ///side of it would notice.
    ///</summary>
    public static (List<string> Categories, int Items, string FirstValue) ReadReport(string reportPath)
    {
        string target = Path.Combine(CacheDirectory, "readback.txt");

        lock (Gate)
        {
            if (File.Exists(target))
                File.Delete(target);

            string script = string.Join('\n',
                "rdb = RBA::ReportDatabase.new(\"\")",
                "rdb.load($input)",
                "names = []",
                "rdb.each_category { |c| names << c.name }",
                "count = 0",
                "values = []",
                "rdb.each_item { |i| count += 1; i.each_value { |v| values << v.to_s } }",
                "File.write($output, names.join(',') + \"\\n\" + count.to_s + \"\\n\" + (values[0] || ''))",
                "");

            string errors = RunScript(script, reportPath, target);

            if (!File.Exists(target))
                throw new InvalidOperationException($"KLayout would not read that report. {errors}");

            string[] lines = File.ReadAllText(target).Replace("\r\n", "\n").Split('\n');

            var categories = new List<string>();

            if (lines[0].Length > 0)
                categories = lines[0].Split(',').ToList();

            //A report with no items has no third line, which is not a fault - there is simply no first value.
            string firstValue = "";

            if (lines.Length > 2)
                firstValue = lines[2];

            return (categories, int.Parse(lines[1], System.Globalization.CultureInfo.InvariantCulture), firstValue);
        }
    }

    ///<summary>Runs one KLayout batch, and hands back whatever it complained about.</summary>
    private static string RunScript(string script, string input, string output, bool alreadyWritten = false)
    {
        string? tool = Find();

        if (tool is null)
            throw new InvalidOperationException("KLayout is needed here and was not found.");

        string path = script;

        if (!alreadyWritten)
        {
            path = Path.Combine(CacheDirectory, "one.rb");

            File.WriteAllText(path, script);
        }

        var start = new ProcessStartInfo(tool)
        {
            //-b is batch: no window, no session, just the script.
            ArgumentList =
            {
                "-b",
                "-r", path,
                "-rd", $"input={input.Replace('\\', '/')}",
                "-rd", $"output={output.Replace('\\', '/')}"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(start)!;

        string errors = process.StandardError.ReadToEnd();

        process.WaitForExit();

        return errors;
    }

    private static string? Find()
    {
        lock (Gate)
        {
            if (searched)
                return klayout;

            searched = true;
            klayout = Candidates.FirstOrDefault(File.Exists);

            return klayout;
        }
    }
}
