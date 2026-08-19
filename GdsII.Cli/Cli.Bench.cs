using System.Diagnostics;
using System.Globalization;

namespace GdsII.Cli
{
    ///
    ///Making a layout to measure against, and measuring against it.
    ///
    ///**Two verbs rather than one**, because the two answer different questions. `generate` hands you a file,
    ///which is what the browser needs - the app opens files and cannot call a library. `bench` times the
    ///stages here, where a profiler works and a run takes seconds rather than a page load.
    ///
    ///What this measures is the *library*: parse, flatten, merge, SVG. It cannot measure the browser, which
    ///is where the wall actually is - a hundred thousand SVG nodes is the browser's problem, not ours. So
    ///these numbers bound the work we control and say nothing about the work we hand over; see the
    ///large-layout end-to-end spec for the other half.
    ///
    public static partial class Cli
    {
        #region generate ********************************************************************

        private static int generate(string[] args, TextWriter output, TextWriter error)
        {
            if (!tryReadShape(args, error, out Shape shape))
                return UsageError;

            string? destination = outputPath(args);

            if (destination is null || destination == "-")
            {
                error.WriteLine("This writes binary, so it needs -o <file>.");

                return UsageError;
            }

            var gds = Synthetic.Layout(shape.PerCell, shape.Columns, shape.Rows, shape.Layers, shape.Corners);

            byte[] bytes = gds.Serialize();

            File.WriteAllBytes(destination, bytes);

            output.WriteLine(FormattableString.Invariant(
                $"Wrote {destination}: {bytes.Length:N0} bytes, {Synthetic.Drawn(shape.PerCell, shape.Columns, shape.Rows):N0} element(s) when flattened."));

            return Ok;
        }

        #endregion **************************************************************************



        #region bench ***********************************************************************

        ///
        ///Times each stage over a generated layout, or over a file if one is named.
        ///
        ///**Each stage on its own, and each one twice.** A single total says which build was faster and
        ///nothing about why; and the first run of anything on .NET pays for the JIT, so a figure taken once
        ///is a figure about compilation. The second pass is the one reported, with the first shown beside it
        ///so a wild gap is visible rather than averaged away.
        ///
        ///
        ///A rule check, timed twice on the same layer: once finding nothing, once finding a great deal.
        ///
        ///**Both, because they measure different halves and only one of them was ever slow.** Finding a
        ///violation is only the beginning of the work: each one is then attributed to the cell it came from,
        ///and on a generated layout of 320,000 elements that step took 455 of the run's 478 seconds - every
        ///violation looking at every shape on its layer, which at 188,742 by 96,000 is eighteen billion box
        ///comparisons. Indexed, the same run is 26 seconds. A single timing over a clean layout would have
        ///reported none of that, because a run that finds nothing never attributes anything.
        ///
        ///The limits are worked out from the layout rather than written down, so this says something on any
        ///file rather than only on the generated one.
        ///
        private static void benchDrc(TextWriter output, GDS gds, FlattenedLayout layout)
        {
            var busiest = layout.Elements
                .Where(element => element.Text is null && !element.IsOpen)
                .GroupBy(element => element.Layer.Key)
                .OrderByDescending(group => group.Count())
                .FirstOrDefault();

            if (busiest is null)
                return;

            //The narrowest shape on the layer sets the scale: half of it finds nothing, ten times it finds
            //nearly everything.
            long narrowest = busiest.Min(element => Math.Min(Bounds.Of(element.Points).Width, Bounds.Of(element.Points).Height));

            if (narrowest <= 0)
                narrowest = 1;

            string layer = FormattableString.Invariant($"{busiest.Key.Number}/{busiest.Key.DataType}");

            output.WriteLine(FormattableString.Invariant(
                $"       drc on {layer}, {busiest.Count():N0} shape(s), narrowest box {narrowest:N0}"));

            //
            //Three units, which nothing real is narrower than, against an eighth of the narrowest box.
            //
            //**Not half of it, which was the first guess and measured the wrong thing.** `narrowest` is the
            //smallest *bounding box* on the layer, and a shape's actual neck is thinner than its box - so a
            //limit near it grows every shape until they all merge, and what gets timed is a vast boolean
            //rather than the attribution this pair exists to expose. An eighth lands under the boxes and
            //over the necks, which is where a great many small violations come from.
            //
            var few = DrcDeck.Parse(FormattableString.Invariant(
                $"layer a {layer}\nrule a.1 width a 3 \"below anything real\""));

            var many = DrcDeck.Parse(FormattableString.Invariant(
                $"layer a {layer}\nrule a.1 width a {Math.Max(4, narrowest / 8)} \"over the necks\""));

            var quiet = time(output, "drc few", () => Drc.Check(few, layout));
            var loud = time(output, "drc many", () => Drc.Check(many, layout));

            output.WriteLine(FormattableString.Invariant(
                $"       {quiet.Violations.Count:N0} found, then {loud.Violations.Count:N0} - the gap between them is what attribution costs"));
        }

        private static int bench(string[] args, TextWriter output, TextWriter error)
        {
            string? path = fileAmong(args);

            GDS gds;
            string what;

            if (path is not null)
            {
                if (!read(path, error, out GDS? opened))
                    return FileError;

                gds = opened!;
                what = path;
            }
            else
            {
                if (!tryReadShape(args, error, out Shape shape))
                    return UsageError;

                gds = Synthetic.Layout(shape.PerCell, shape.Columns, shape.Rows, shape.Layers, shape.Corners);
                what = FormattableString.Invariant(
                    $"generated {shape.PerCell:N0} x {shape.Columns}x{shape.Rows}, {shape.Layers} layer(s), {shape.Corners} corner(s)");
            }

            byte[] bytes = gds.Serialize();

            output.WriteLine($"bench  {what}");
            output.WriteLine(FormattableString.Invariant($"       {bytes.Length:N0} bytes on disk"));
            output.WriteLine();

            //Parsed from the bytes rather than reusing what is in hand, because opening a file is the thing
            //being measured and a library already in memory has had its parse paid for.
            var parsed = time(output, "parse", () => new GDS(bytes));

            var layout = time(output, "flatten", () => GdsFlattener.Flatten(parsed));

            output.WriteLine(FormattableString.Invariant($"       {layout.Elements.Count:N0} element(s) drawn"));

            var everyLayer = layout.Elements.Select(element => element.Layer.Key).Distinct().ToHashSet();
            var noLabels = new HashSet<LayerKey>();

            var svg = time(output, "svg", () => SvgWriter.Build(layout, everyLayer, 0.5f, noLabels, null));

            output.WriteLine(FormattableString.Invariant($"       {svg.Length:N0} characters of SVG"));

            time(output, "merge", () => Booleans.MergeByLayer(layout.Elements));

            benchDrc(output, parsed, layout);

            //
            //And writing the layout back out as OASIS, which is the other end of opening one.
            //
            //**Here because compaction costs time as well as saving bytes**, and the same download runs in
            //a browser tab. Compressing the cell bodies is roughly half again on top of writing them, and
            //that is a trade nobody can weigh without both numbers beside each other - which is also why
            //the size is printed rather than only the duration.
            //
            var oasis = time(output, "oasis", () => OasisWriter.Write(parsed));

            output.WriteLine(FormattableString.Invariant(
                $"       {oasis.Length:N0} bytes of OASIS, {oasis.Length * 100.0 / bytes.Length:N1}% of the GDSII"));

            output.WriteLine();
            output.WriteLine(FormattableString.Invariant(
                $"       peak managed heap {GC.GetTotalMemory(forceFullCollection: false) / (1024.0 * 1024.0):N1} MB"));

            return Ok;
        }

        ///<summary>Runs it twice and prints both, reporting the second - the first pays for the JIT.</summary>
        private static T time<T>(TextWriter output, string stage, Func<T> work)
        {
            var first = Stopwatch.StartNew();
            work();
            first.Stop();

            var second = Stopwatch.StartNew();
            T result = work();
            second.Stop();

            output.WriteLine(FormattableString.Invariant(
                $"  {stage,-8} {second.ElapsedMilliseconds,8:N0} ms   (first run {first.ElapsedMilliseconds:N0} ms)"));

            return result;
        }

        #endregion **************************************************************************



        #region The shape asked for *********************************************************

        private readonly record struct Shape(int PerCell, int Columns, int Rows, int Layers, int Corners);

        ///
        ///The file named among the arguments, if one is.
        ///
        ///**An option's value is not a file.** Taking the first thing that does not start with a dash reads
        ///`--shapes 20000` as a request to open a file called 20000 - which is what it did, and which reports
        ///as "there is no file at 20000" rather than as anything to do with the mistake.
        ///
        private static string? fileAmong(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i].StartsWith('-'))
                {
                    //Every option here takes one value, so the next argument belongs to it.
                    i++;

                    continue;
                }

                return args[i];
            }

            return null;
        }

        ///<summary>
        ///The arguments both verbs share. Defaults chosen to land near the measured wall - twenty thousand
        ///elements - so that running either with no arguments at all says something useful.
        ///</summary>
        private static bool tryReadShape(string[] args, TextWriter error, out Shape shape)
        {
            shape = new Shape(20000, 1, 1, 8, 4);

            if (!tryReadCount(args, "--shapes", error, out int perCell, shape.PerCell))
                return false;

            if (!tryReadCount(args, "--columns", error, out int columns, shape.Columns))
                return false;

            if (!tryReadCount(args, "--rows", error, out int rows, shape.Rows))
                return false;

            if (!tryReadCount(args, "--layers", error, out int layers, shape.Layers))
                return false;

            if (!tryReadCount(args, "--corners", error, out int corners, shape.Corners))
                return false;

            shape = new Shape(perCell, columns, rows, layers, corners);

            //Refused rather than attempted. Past this the machine runs out of memory rather than answering,
            //which is a slow way to find out you typed an extra zero.
            const long Most = 5_000_000;

            if (Synthetic.Drawn(perCell, columns, rows) > Most)
            {
                error.WriteLine(FormattableString.Invariant(
                    $"That is {Synthetic.Drawn(perCell, columns, rows):N0} elements, past the {Most:N0} this will attempt."));

                return false;
            }

            return true;
        }

        private static bool tryReadCount(string[] args, string name, TextWriter error, out int value, int fallback)
        {
            value = fallback;

            int at = Array.IndexOf(args, name);

            if (at < 0)
                return true;

            if (at + 1 >= args.Length || !int.TryParse(args[at + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 1)
            {
                error.WriteLine($"{name} needs a whole number of one or more.");

                return false;
            }

            return true;
        }

        #endregion **************************************************************************
    }
}
