using System.Globalization;
using System.Text;
using static GdsII.GDS;

namespace GdsII.Cli
{
    ///<summary>
    ///What the tool does, separated from how it is launched.
    ///
    ///Output goes to writers that are passed in and the result is an exit code, so every command can be
    ///run from a test without starting a process. Nothing here calls Console or Environment.
    ///
    ///Partial because the commands that do more than report live in their own files - conversion, the two
    ///that change geometry, and the benchmark. Each is longer than the rest put together and shares nothing
    ///with them but the option helpers.
    ///</summary>
    public static partial class Cli
    {
        #region Exit codes ******************************************************************

        ///<summary>The command did what was asked.</summary>
        public const int Ok = 0;

        ///<summary>The command line itself was wrong: an unknown command, a missing argument.</summary>
        public const int UsageError = 1;

        ///<summary>The command was understood but the file was not - unreadable, or not GDSII.</summary>
        public const int FileError = 2;

        ///<summary>
        ///`drc` ran every rule and the layout broke some of them.
        ///
        ///Its own code rather than <see cref="FileError"/>, because the file is not wrong: it parsed, it
        ///drew, and it has design rule violations in it. A script gating a cell library wants to tell a
        ///layout that needs fixing from one it could not read.
        ///</summary>
        public const int ViolationsFound = 3;

        ///<summary>
        ///`drc` could not run every rule over the whole layout, so nothing may be concluded from what it did or did
        ///not find.
        ///
        ///**Separate from <see cref="ViolationsFound"/> and worse than it.** A run that reports faults knows
        ///what it found; a run that skipped a rule does not know what it did not look at, and the fix is to
        ///the deck rather than to the layout. Returned whether or not violations were also found, because it
        ///is the more important of the two things to say. The same for a layout that stopped flattening at
        ///<see cref="MostElements"/>, where the fix is `--most-elements`.
        ///</summary>
        public const int IncompleteCheck = 4;

        ///
        ///The most elements a command flattens a layout into before it stops, unless `--most-elements` says otherwise.
        ///
        ///**More than the app's ceiling, because the app's is a browser's.** <see cref="GdsFlattener.MostElements"/> is a
        ///million and a half because a tab has two gigabytes of WebAssembly memory and an element drawn there costs about
        ///a kilobyte of it. This runs on a desktop, where a flattened element is about 250 bytes of managed heap and a
        ///rule check over one peaks near a kilobyte: IHP's 1024x16 SRAM macro, 2,639,671 elements, took 2.5 GB to check
        ///whole. Stopped at the app's ceiling, 1,139,139 elements short, its check found 141 faults that are not there.
        ///
        ///**Five million is where `gds generate` stops as well**, for the same reason: past it a machine runs out of
        ///memory rather than answering, about five gigabytes at the peak of a check. A layout larger still is flattened
        ///that far and every command says so - see <see cref="flatten"/>.
        ///
        public const int MostElements = 5_000_000;

        #endregion **************************************************************************



        #region Dispatch ********************************************************************

        public static int Run(string[] args, TextWriter output, TextWriter error)
        {
            if (args.Length == 0)
            {
                writeUsage(output);

                return UsageError;
            }

            string command = args[0];
            string[] rest = args[1..];

            if (command is "-h" or "--help" or "help")
            {
                writeUsage(output);

                return Ok;
            }

            if (command is "-v" or "--version" or "version")
            {
                output.WriteLine(Version);

                return Ok;
            }

            //Every command reads or writes a file, so the same two failures - it is not there, or it is not
            //GDSII - are caught in one place rather than in each of them.
            try
            {
                switch (command)
                {
                    case "info":
                        return info(rest, output, error);
                    case "dump":
                        return dump(rest, output, error);
                    case "build":
                        return build(rest, output, error);
                    case "validate":
                        return validate(rest, output, error);
                    case "cells":
                        return cells(rest, output, error);
                    case "nets":
                        return nets(rest, output, error);
                    case "measure":
                        return measure(rest, output, error);
                    case "drc":
                        return drc(rest, output, error);
                    case "layers":
                        return layers(rest, output, error);
                    case "svg":
                        return svg(rest, output, error);
                    case "model":
                        return model(rest, output, error);
                    case "boolean":
                        return boolean(rest, output, error);
                    case "size":
                        return size(rest, output, error);
                    case "convert":
                        return convert(rest, output, error);
                    case "generate":
                        return generate(rest, output, error);
                    case "bench":
                        return bench(rest, output, error);
                    case "mcp":
                        return mcp(rest, output, error);
                    default:
                        error.WriteLine($"Unknown command \"{command}\".");
                        writeUsage(error);

                        return UsageError;
                }
            }
            catch (FileNotFoundException problem)
            {
                error.WriteLine(problem.Message);

                return FileError;
            }
            catch (DirectoryNotFoundException problem)
            {
                error.WriteLine(problem.Message);

                return FileError;
            }
            catch (IOException problem)
            {
                error.WriteLine($"Could not read or write the file: {problem.Message}");

                return FileError;
            }
            catch (UnauthorizedAccessException problem)
            {
                error.WriteLine($"Not allowed to read or write that path: {problem.Message}");

                return FileError;
            }
            catch (InvalidDataException problem)
            {
                //
                //A file that could not be written - a record over the format's ceiling, say - rather than one that
                //could not be read, which read catches where it reads. This is a SystemException rather than an
                //IOException, so until 2026-11-03 it was nothing here caught, and `gds convert` fell over with a
                //stack trace on a path of 8,192 points.
                //
                error.WriteLine($"Could not write the file: {problem.Message}");

                return FileError;
            }
        }

        ///<summary>
        ///What `gds --version` prints, read off this assembly rather than written down here.
        ///
        ///It was a constant, which made it the third copy of a number that has to agree with the two in
        ///the build - and the one nothing would notice had gone stale, since a tool reporting the wrong
        ///version still does everything else correctly.
        ///
        ///The informational version is the one the build stamps from &lt;Version&gt;. It carries a build
        ///metadata suffix in some configurations, so anything after a '+' is cut: that part is about which
        ///commit produced the binary, not which release it is.
        ///</summary>
        public static string Version
        {
            get
            {
                var assembly = typeof(Cli).Assembly;

                string? stamped = assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?
                    .InformationalVersion;

                if (stamped is null || stamped.Length == 0)
                    return assembly.GetName().Version?.ToString(3) ?? "0.0.0";

                int metadata = stamped.IndexOf('+');

                if (metadata < 0)
                    return stamped;

                return stamped[..metadata];
            }
        }

        private static void writeUsage(TextWriter output)
        {
            output.WriteLine(@"gds - read, check and convert GDSII layout files.

Usage:
  gds info <file.gds>                  What the file holds: units, structures, elements, layers
  gds dump <file.gds> [-o <file>]      Every record as text, one per line
  gds build <file.txt> [-o <file>]     Read that text back into a GDSII file
  gds validate <path...>               Parse and report; a directory is searched for .gds files
  gds cells <file.gds> [--tree]        The library's cells: what places what, and what is in each
  gds nets <file.gds> --at <x,y>       Everything joined to the shape at that point. Needs --layermap
  gds measure <file.gds> --from --to   The distance between two points, the way the ruler reads it
  gds drc <file.gds> --deck <file>     Check it against design rules. Exit 3 if any are broken,
                                       4 if some rule could not be run
  gds layers <file.gds> [--area]       Layer/datatype pairs, with a count of what is on each, named
                                       if a layermap says so
  gds svg <file.gds> [-o <file>]       The layout as an SVG
  gds model <file.gds> -o <file>       The layout as a 3D model: .stl, .obj, .gltf or .glb
  gds boolean <file.gds> -o <file>     Combine two layers into a third: --op, --a, --b, --into
  gds size <file.gds> -o <file>        Move every edge of a layer out or in: --a, --by
  gds convert <file> -o <file>         Between GDSII, OASIS and DXF
  gds generate -o <file>               A layout of a chosen size, to measure against
  gds bench [<file.gds>]               Time parse, flatten, svg, merge, a rule check and the OASIS
                                       write over one - and a real deck with --deck, every net with
                                       --layermap
  gds mcp [<folder>]                   Let an AI model open, draw on, check and save the layouts
                                       in a folder, over MCP - see below

Options:
  -o, --output <file>   Write here rather than to standard output. ""-"" means standard output.
      --layermap <file> layers, svg, model, nets, mcp, bench: a CSV of layer,datatype,name and more - see below
      --write-layermap <file>
                        layers only: this file's own layers as a mapping to edit
      --layers <list>   svg, model: draw only these, e.g. 65/20,66/44,68
      --hide <list>     svg, model: draw everything except these
      --opacity <n>     svg only: 0 to 1, default 0.5
      --no-labels       svg only: leave the TEXT elements out
      --spacing <n>     model only: extra gap opened between stacked layers, default 0
      --scale <n>       model only: multiply every coordinate by this, default 1
      --ascii           model only: write STL as text rather than binary
      --no-mtl          model only: no companion .mtl file beside an .obj
      --op <name>       boolean only: and, or, not or xor
      --a <layer>       boolean, size: the layer to read, as one pair like 65/20
      --b <layer>       boolean only: the layer to combine it with
      --into <layer>    boolean: where the result goes. size: a copy, rather than in place
      --only            boolean, size: write the result on its own, without the rest of the file
      --by <n>          size only: database units to move every edge, negative to shrink
      --to <format>     convert only: gds, oas or dxf, when the output's name does not say
      --area            layers only: also the area each layer draws, covers, and its density
      --tree            cells only: indent each cell under whatever places it
      --at <x,y>        nets only: a point in database units, on the shape to trace from
      --shapes          nets only: also list every shape on the net, by index
      --deck <file>     drc, mcp, bench: the design rules to check against
      -o <file>         drc: also write the violations as a KLayout .lyrdb marker database
      --rule <id>       drc only: report only this rule. The rest of the deck still runs
      --markers         drc only: list every violation with where it is and which cell it is in
      --from <x,y>      measure only: where to measure from, in database units
      --to <x,y>        measure, convert: the point to measure to; the format to write
      --link-port <n>   mcp only: the port a GDS Viewer tab links on, default 43707; 0 for no live link
      --shapes <n>      generate, bench: how many shapes one cell holds, default 20000
      --columns <n>, --rows <n>
                        generate, bench: how many times that cell is arrayed, default 1 each
      --layers <n>      generate, bench: how many layer numbers the shapes spread over, default 8
      --corners <n>     generate, bench: how many corners each shape has, default 4
      --most-elements <n>
                        every command that flattens the layout: stop after this many elements, default
                        5000000 - the app stops at 1500000, where a browser runs out of memory
  -r, --recursive       validate only: search directories all the way down
  -h, --help            This
  -v, --version         Version

A layer is written as numbers, the way `gds layers` prints it without a layermap. A bare number
is every data type on it, so 65 is both 65/16 and 65/20 where 65/20 is only the drawn geometry.

A GDSII file carries only numbers, so what 65/20 *means* comes from a layermap you supply - the
same file the web app's PDK manager imports into a PDK, and the same one --write-layermap hands back:

  layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize,texture,texturesize
  65,20,diff,#e69ac5,0,120,conductor
  66,20,poly,#d80000,180,180,conductor
  66,44,licon1,,300,180,via

Everything past the third column is optional, but the columns are positional, so a gap is a
comma: 66,44,licon1,,300 gives licon1 a height and no color. A name with a comma in it goes in
double quotes, as a spreadsheet writes it: 65,20,""poly, gate"". Names reach `layers`; names,
colors and fills reach `svg`; heights and thicknesses reach `model`, where a placed layer keeps
its own height and --spacing only opens a gap on top of wherever a layer already rests. At its
default of zero a model comes out at the heights the mapping gives, which is the same stack
the 3D view opens on. The last two are the web viewer's 3D textures, a picture beside the CSV
and its size, and are only carried through. Bad rows are reported by line and the good ones
still applied.

A KLayout .lyp is read too, for the two things it carries: each layer's name and the color
KLayout fills it with. The heights and the roles are a CSV's alone.

  gds layers cell.gds --write-layermap sky130.csv
  gds svg cell.gds --layermap sky130.csv -o cell.svg
  gds model cell.gds --layermap sky130.csv -o wafer.glb

The role column is what nets needs, and the only one no PDK table already carries. Without it
no layer takes part and the trace has nothing to walk, which is what nets says rather than
reporting an empty net:

  gds nets cell.gds --layermap sky130.csv --at 1200,800

One net from one point rather than every net in the file - a full extraction over a large
layout is the expensive thing this deliberately does not do. `gds info <file>` prints the extent
of what is drawn, and `gds dump` each shape's own coordinates, which is how to find a point to
aim at.

The model is written the way the layout is stored - X and Y as the file has them, layers stacked
up Z. The 3D view's right-angle tilt is its camera, not the layout, so it is not baked in here.
Labels are left out: a TEXT element is an anchor and a string, which no mesh format can hold.

boolean and size write a **flat** file: a boolean between two layers means nothing until the
references that place them are resolved, and putting the hierarchy back would mean deciding
which cell a derived shape belongs to. The result is added to the rest of the layout unless
--only, so it can be looked at against what it came from. A hole comes out as a keyhole, since
GDSII has no hole of its own.

  gds boolean cell.gds --op and --a 66/20 --b 65/20 --into 100/0 -o gate.gds
  gds size cell.gds --a 67/20 --by -50 -o undersized.gds

convert keeps the hierarchy - cells stay cells and placements stay placements - which is what
makes OASIS worth writing at all. Every command reads either format already, told apart by what
the file starts with rather than by what it is called, so this is only needed to write one:

  gds convert cell.gds -o cell.oas
  gds convert cell.oas -o cell.gds
  gds convert cell.gds -o cell.dxf

A GDSII NODE has no OASIS equivalent and is reported rather than dropped in silence. A round
path end becomes a square one, and a label loses its justification: the format has neither.

mcp is for an MCP client - Claude Desktop, Claude Code or any other - to start, rather than for
typing: it talks the Model Context Protocol over standard input and output. The model gets the
library's layout tools - drawing, editing, reading and checking rules - and files: it lists,
opens, starts and saves layouts in one folder, the one given or the one it was started in, and
no path it names can leave it. Nothing is written until it saves, and then only .gds, .oas or
.dxf. --layermap and --deck are loaded for every file it opens, and --most-elements holds for
its tools as it does for every other command:

  claude mcp add gds -- gds mcp
  gds mcp C:\layouts --layermap sky130.csv --deck sky130A.drc

The README says how to add it to Claude Desktop.

It can also work on a layout open in the GDS Viewer app, as you watch: open the app's AI settings,
under Settings, press Start under MCP server and tell the model the code it shows. The tab links over a WebSocket to this
computer only, on --link-port, and the layout tools then edit what the tab has open.

A file argument of ""-"" reads standard input, so a dump can be piped straight back:
  gds dump cell.gds | gds build - -o roundtripped.gds

Exit codes: 0 fine, 1 the command line was wrong, 2 the file was.");
        }

        #endregion **************************************************************************



        #region Commands ********************************************************************

        private static int info(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "info", error, out string path))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            if (flatten(gds!, args, error, saysItself: true) is not FlattenedLayout layout)
                return UsageError;

            output.WriteLine($"file        {describe(path)}");
            output.WriteLine($"library     {nameOf(gds!.StreamFormat.LIBNAME)}");

            var units = gds.StreamFormat.UNITS.Data as Real8Data;

            if (units is not null && units.Values.Length >= 2)
            {
                output.WriteLine($"units       {units.Values[0].ToString("G17", CultureInfo.InvariantCulture)} user units per database unit");
                output.WriteLine($"            {units.Values[1].ToString("G17", CultureInfo.InvariantCulture)} meters per database unit");
            }

            if (gds.Records[1].Timestamps is { } stamps)
            {
                output.WriteLine($"modified    {stamps.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");
                output.WriteLine($"accessed    {stamps.Accessed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}");

                //
                //Said out loud when the century was not in the file.
                //
                //GDSII writers use three conventions for the year and the record does not say which - so a
                //small year is interpreted, the way every reader interprets it. Right for every file anyone
                //will open, and still a guess; a date reported flat is a date somebody may go on to quote.
                //
                if (gds.Records[1].YearWasInferred)
                    output.WriteLine("            the century is inferred - the file writes its year as a small number, which GDSII does not define");
            }

            output.WriteLine($"records     {gds.Records.Count}");
            output.WriteLine($"structures  {gds.StreamFormat.Structures.Count}");

            foreach (var structure in gds.StreamFormat.Structures)
                output.WriteLine($"              {nameOf(structure.STRNAME)}  ({structure.Elements.Count} element(s))");

            //
            //**Said where the structures are listed**, because the file holds one more than that list: a reader that
            //makes a cell of KLayout's context - this one did until 2026-11-03 - counts it among them, and this is
            //the line that says which it is and why it is not. See StreamFormatModel.Context.
            //
            if (gds.StreamFormat.Context is StructureModel context)
                output.WriteLine($"context     {StreamFormatModel.ContextName}, KLayout's record of what {describedBy(context)} cell(s) were made from - not a cell, and not drawn");

            output.WriteLine($"layers      {gds.AdditionalInformation.Layers.Count} layer/datatype pair(s)");
            output.WriteLine($"drawn       {layout.Elements.Count} shape(s) after resolving the hierarchy");

            //How big it is and where, which is the first thing anybody wants and the file does not say
            //anywhere - it has to be measured from the geometry once the hierarchy is resolved.
            var bounds = Measure.BoundsOf(layout);

            if (!bounds.IsEmpty)
            {
                output.WriteLine($"extent      {bounds}");
                output.WriteLine(FormattableString.Invariant(
                    $"            {bounds.Width} x {bounds.Height} database units{inMicrons(gds, bounds)}"));
            }

            int labels = layout.Elements.Count(element => element.Text is not null);

            if (labels > 0)
                output.WriteLine($"labels      {labels}");

            if (layout.UnresolvedReferences.Count > 0)
            {
                //Normal for a standalone cell, which references the rest of its library without holding it.
                output.WriteLine($"unresolved  {layout.UnresolvedReferences.Count} referenced structure(s) not in this file");

                foreach (string name in layout.UnresolvedReferences)
                    output.WriteLine($"              {name}");
            }

            if (layout.Stopped)
            {
                output.WriteLine(FormattableString.Invariant(
                    $"note        flattening stopped at {layout.Elements.Count:N0} elements, so what is drawn, its extent and its labels are of part of the layout - --most-elements takes it further"));
            }

            if (layout.DepthLimitReached)
                output.WriteLine("note        nesting was cut short, so this library references itself");

            return Ok;
        }

        ///<summary>
        ///The same size in microns, when the file says how big a database unit is.
        ///
        ///Which is the unit anybody in this field actually thinks in - "2800 by 1500" says nothing until it
        ///is "2.8 by 1.5 um". Empty rather than guessed when UNITS is missing or nonsense, since a made-up
        ///scale is worse than none.
        ///</summary>
        private static string inMicrons(GDS gds, Bounds bounds)
        {
            if (gds.MicronsPerDatabaseUnit is not double micronsPerUnit)
                return "";

            return FormattableString.Invariant(
                $", {bounds.Width * micronsPerUnit:0.###} x {bounds.Height * micronsPerUnit:0.###} um");
        }

        ///<summary>How many cells KLayout's context says were made from a PCell or a library: it places each once.</summary>
        private static int describedBy(StructureModel context)
        {
            return context.Elements.Count(element => element.Element is SrefModel);
        }

        ///
        ///How many structures a library holds, counted as `gds info` lists them: its cells, and KLayout's context named
        ///beside them when the file has one, since that is in the file and is no cell. See StreamFormatModel.Context.
        ///
        private static string structuresOf(GDS gds)
        {
            string said = $"{gds.StreamFormat.Structures.Count} structure(s)";

            if (gds.StreamFormat.Context is not null)
                said += " and KLayout's context";

            return said;
        }

        private static int dump(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "dump", error, out string path))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            return writeText(gds!.AsText(), outputPath(args), output);
        }

        private static int build(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "build", error, out string path))
                return UsageError;

            string text;

            if (path == "-")
                text = Console.In.ReadToEnd();
            else
                text = File.ReadAllText(path);

            GDS gds;

            try
            {
                gds = GDS.FromText(text);
            }
            catch (InvalidDataException problem)
            {
                error.WriteLine($"{describe(path)} is not a readable record dump: {problem.Message}");

                return FileError;
            }

            string? destination = outputPath(args);

            if (destination is null || destination == "-")
            {
                error.WriteLine("build writes binary, so it needs -o <file>.");

                return UsageError;
            }

            File.WriteAllBytes(destination, gds.Serialize());

            output.WriteLine($"Wrote {destination}: {gds.Records.Count} records.");

            return Ok;
        }

        ///<summary>
        ///Parses each path and says whether it read. A directory is searched for .gds files, which is what
        ///makes this usable over a whole PDK rather than a file at a time.
        ///</summary>
        private static int validate(string[] args, TextWriter output, TextWriter error)
        {
            var paths = positional(args).ToList();

            if (paths.Count == 0)
            {
                error.WriteLine("validate needs at least one file or directory.");

                return UsageError;
            }

            bool recursive = args.Contains("-r") || args.Contains("--recursive");

            var files = new List<string>();

            foreach (string path in paths)
            {
                if (Directory.Exists(path))
                {
                    var search = SearchOption.TopDirectoryOnly;

                    if (recursive)
                        search = SearchOption.AllDirectories;

                    files.AddRange(Directory.EnumerateFiles(path, "*.gds", search).OrderBy(name => name, StringComparer.Ordinal));
                }
                else
                    files.Add(path);
            }

            if (files.Count == 0)
            {
                error.WriteLine("No .gds files found.");

                return FileError;
            }

            int failed = 0;

            foreach (string file in files)
            {
                if (read(file, error, out GDS? gds))
                    output.WriteLine($"ok    {describe(file)}  ({gds!.Records.Count} records, {structuresOf(gds)})");
                else
                    failed++;
            }

            if (files.Count > 1)
                output.WriteLine($"\n{files.Count - failed} of {files.Count} read.");

            if (failed > 0)
                return FileError;

            return Ok;
        }

        private static int layers(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "layers", error, out string path))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            if (!applyLayerMap(args, gds!, output, error))
                return FileError;

            if (flatten(gds!, args, error) is not FlattenedLayout layout)
                return UsageError;

            var counts = new Dictionary<LayerKey, int>();
            var labels = new Dictionary<LayerKey, int>();

            foreach (var element in layout.Elements)
            {
                var key = element.Layer.Key;

                counts.TryGetValue(key, out int drawn);
                counts[key] = drawn + 1;

                if (element.Text is not null)
                {
                    labels.TryGetValue(key, out int text);
                    labels[key] = text + 1;
                }
            }

            //Area is behind a flag rather than always on, because the covered figure is a clipping pass
            //over every shape on every layer - fine on a cell, and the slowest thing this tool does on
            //anything bigger. What `gds layers` is for is a quick look at what a file holds.
            bool withArea = args.Contains("--area");

            //
            //The first column widens to fit the names when a mapping named any of them, and stays at sixteen
            //otherwise.
            //
            //Measured rather than fixed, because a name is any length: sky130's run to `sky130_fd_...` and a
            //column cut to fit the numbers would put every count out of line by a different amount. The header
            //says "layer" rather than "layer/datatype" once names are in it, since the cell holds both.
            //
            bool anyNamed = gds!.AdditionalInformation.Layers.Values.Any(layer => layer.Name is not null);

            int column = 16;

            if (anyNamed)
                column = Math.Max(16, gds.AdditionalInformation.Layers.Values.Max(layer => layer.DisplayName.Length) + 2);

            string heading = "layer/datatype".PadRight(column);

            if (anyNamed)
                heading = "layer".PadRight(column);

            if (withArea)
                output.WriteLine($"{heading} shapes   labels          drawn         covered   density");
            else
                output.WriteLine($"{heading} shapes   labels");

            foreach (var layer in gds.AdditionalInformation.OrderedLayers())
            {
                counts.TryGetValue(layer.Key, out int drawn);
                labels.TryGetValue(layer.Key, out int text);

                //DisplayName is the pair on its own until something names it, and `name (65/20)` afterwards -
                //the numbers stay visible for the reason the app keeps them, so a wrong mapping is visible as
                //a disagreement rather than hidden behind a plausible word.
                string says = layer.Value.DisplayName.PadRight(column);

                if (!withArea)
                {
                    output.WriteLine($"{says} {drawn,6}   {text,6}");

                    continue;
                }

                double drawnArea = Measure.DrawnAreaOf(layout, layer.Key);
                double coveredArea = Measure.CoveredAreaOf(layout, layer.Key);
                var bounds = Measure.BoundsOf(layout, layer.Key);

                double density = 0;

                if (!bounds.IsEmpty && bounds.Area > 0)
                    density = coveredArea / bounds.Area;

                output.WriteLine(FormattableString.Invariant(
                    $"{says} {drawn,6}   {text,6} {drawnArea,14:N0} {coveredArea,15:N0} {density,9:P1}"));
            }

            if (withArea)
            {
                //Said once rather than left to be worked out from two columns that usually differ.
                output.WriteLine();
                output.WriteLine("drawn adds every shape up and counts an overlap twice; covered merges them first.");
                output.WriteLine("density is covered over the layer's own bounding box. All in square database units.");
            }

            //
            //And the other direction: this file's layers as a mapping to edit, which is the app's Export.
            //
            //The point of it is that a layermap is easiest to start from a real file rather than from a blank
            //page - every pair already listed, every column filled in with what is currently being drawn, so
            //filling in names means typing over rather than typing out. Written after the table so that
            //`gds layers x.gds --write-layermap map.csv` both shows you the layers and hands you the file.
            //
            string? mapDestination = valueOf(args, "--write-layermap");

            if (mapDestination is not null)
            {
                string mapping = LayerNames.Export(gds.AdditionalInformation);

                if (mapDestination == "-")
                {
                    output.WriteLine();
                    output.Write(mapping);

                    return Ok;
                }

                try
                {
                    File.WriteAllText(mapDestination, mapping);
                }
                catch (Exception problem)
                {
                    error.WriteLine($"Could not write the layermap {mapDestination}: {problem.Message}");

                    return FileError;
                }

                output.WriteLine();
                output.WriteLine($"Wrote {mapDestination}: {gds.AdditionalInformation.Layers.Count} row(s), every column filled in.");
            }

            return Ok;
        }

        private static int svg(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "svg", error, out string path))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            float opacity = 0.5f;
            string? given = valueOf(args, "--opacity");

            if (given is not null && !SvgWriter.TryParseOpacity(given, out opacity))
            {
                error.WriteLine($"\"{given}\" is not an opacity between 0 and 1.");

                return UsageError;
            }

            bool showLabels = !args.Contains("--no-labels");

            //Before flattening, since every flattened element carries a reference to its Layer - so the colors
            //and the fills a mapping sets are the ones the markup is built from.
            //
            //Both halves of the report go to standard error here, because standard output is the SVG: a line
            //of prose ahead of it would be a line of prose inside the file, and `gds svg x.gds > x.svg` is the
            //obvious way to use this.
            if (!applyLayerMap(args, gds!, error, error))
                return FileError;

            if (flatten(gds!, args, error) is not FlattenedLayout layout)
                return UsageError;

            if (!LayerFilter.TryApply(layout, valueOf(args, "--layers"), valueOf(args, "--hide"), error, out layout))
                return UsageError;

            var layers = SvgWriter.AllLayers(layout);
            var labeled = layers;

            if (!showLabels)
                labeled = new HashSet<LayerKey>();

            //
            //The document the app's download writes, framed the way its thumbnails are - see Preview.Document -
            //turned the format's way up, since this is a file to read as KLayout draws the layout where the app's
            //view and download draw y downward. This wrapped the markup on its own until 2026-11-03, with a
            //margin of its own; the two framings were two answers to one question.
            //
            return writeText(Preview.Document(layout, layers, labeled, opacity, yUp: true) + "\n", outputPath(args), output);
        }

        ///<summary>
        ///Extrudes the layout into a solid and writes it out, the format taken from the output file's
        ///extension - which is how the 3D view's own download works, and saves a second option that could
        ///then disagree with the name it was written under.
        ///</summary>
        private static int model(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "model", error, out string path))
                return UsageError;

            string? destination = outputPath(args);

            if (destination is null || destination == "-")
            {
                error.WriteLine("model needs -o <file>, since the format comes from the extension. One of .stl, .obj, .gltf, .glb.");

                return UsageError;
            }

            string extension = Path.GetExtension(destination).ToLowerInvariant();

            if (extension is not (".stl" or ".obj" or ".gltf" or ".glb"))
            {
                error.WriteLine($"\"{extension}\" is not a format this writes. One of .stl, .obj, .gltf, .glb.");

                return UsageError;
            }

            if (!numberOption(args, "--spacing", 0, error, out double spacing, zeroAllowed: true))
                return UsageError;

            if (!numberOption(args, "--scale", 1, error, out double scale))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            //
            //Before the spacing, which is what makes a real process stack reachable from here at all.
            //
            //A mapping's height and thickness columns set CustomHeight, which SetStackingOffsets reads back as
            //where the layer rests - so the order is: place what the mapping placed, then spread every layer
            //from where it rests. The other way round, the even spacing would overwrite the wafer.
            //
            if (!applyLayerMap(args, gds!, output, error))
                return FileError;

            //The same call the 3D view's spacing slider makes, so a model exported at a given spacing
            //matches what that slider shows at the same number.
            gds!.AdditionalInformation.SetStackingOffsets((int)spacing);

            if (flatten(gds, args, error) is not FlattenedLayout layout)
                return UsageError;

            if (!LayerFilter.TryApply(layout, valueOf(args, "--layers"), valueOf(args, "--hide"), error, out layout))
                return UsageError;

            var parts = LayoutMesh.Build(layout, scale, out int skipped);

            if (parts.Count == 0)
            {
                error.WriteLine($"{describe(path)} has no geometry to extrude.");

                return FileError;
            }

            string written = writeModel(parts, destination, extension, args);

            int triangles = parts.Sum(part => part.TriangleCount);

            output.WriteLine($"Wrote {written}: {triangles} triangles across {parts.Count} layer(s).");

            //A shape missing from an export is otherwise found by whoever opens the file, long after this.
            if (skipped > 0)
                output.WriteLine($"{skipped} outline(s) enclosed no area and were left out.");

            return Ok;
        }

        ///<summary>Writes the chosen format and reports what ended up on disk, .mtl included.</summary>
        private static string writeModel(List<LayoutMesh.Part> parts, string destination, string extension, string[] args)
        {
            if (extension == ".stl")
            {
                if (args.Contains("--ascii"))
                {
                    using var text = new StreamWriter(destination, false, new UTF8Encoding(false));

                    ModelWriters.WriteAsciiStl(parts, text);
                }
                else
                {
                    using var stream = File.Create(destination);

                    ModelWriters.WriteBinaryStl(parts, stream);
                }

                return destination;
            }

            if (extension == ".obj")
            {
                string? materialLibrary = null;

                if (!args.Contains("--no-mtl"))
                    materialLibrary = Path.GetFileNameWithoutExtension(destination) + ".mtl";

                using (var text = new StreamWriter(destination, false, new UTF8Encoding(false)))
                    ModelWriters.WriteObj(parts, text, materialLibrary);

                if (materialLibrary is null)
                    return destination;

                //Beside the .obj rather than in the working directory, since that is where the mtllib line
                //says to look for it.
                string beside = Path.Combine(Path.GetDirectoryName(destination) ?? "", materialLibrary);

                using (var text = new StreamWriter(beside, false, new UTF8Encoding(false)))
                    ModelWriters.WriteMtl(parts, text);

                return $"{destination} and {beside}";
            }

            ModelWriters.WriteGltf(parts, destination);

            //A .gltf is JSON that points at its geometry rather than holding it, so a .bin lands beside it.
            //That is the format working as intended, but it is a second file the caller did not name, so
            //it gets said out loud. .glb is the same model in one file for anyone who would rather not.
            string buffer = Path.ChangeExtension(destination, ".bin");

            if (extension == ".gltf" && File.Exists(buffer))
                return $"{destination} and {buffer}";

            return destination;
        }

        #endregion **************************************************************************



        #region Reading and writing *********************************************************

        ///<summary>
        ///Parses a file, reporting rather than throwing when it is not GDSII - which is a normal outcome
        ///for this tool, not an exceptional one, and for validate it is the whole point.
        ///</summary>
        private static bool read(string path, TextWriter error, out GDS? gds)
        {
            gds = null;

            Stream source;

            try
            {
                //Opened rather than read whole. ReadAllBytes holds the file and the records it becomes at
                //the same time, and caps out at the largest array the runtime allows - which a real layout
                //can reach. The parser reads records straight off the handle now.
                if (path == "-")
                    source = Console.OpenStandardInput();
                else
                    source = File.OpenRead(path);
            }
            catch (FileNotFoundException)
            {
                error.WriteLine($"There is no file at {describe(path)}.");

                return false;
            }
            catch (DirectoryNotFoundException)
            {
                error.WriteLine($"There is no directory on the way to {describe(path)}.");

                return false;
            }

            using (source)
            {
                try
                {
                    gds = ReadLayout(source, out _, out IReadOnlyList<string> problems);

                    //What the file got wrong that was read past - an OASIS file leaning on the cell before, see
                    //OasisReader - said on standard error, where the layermap's report goes, so `gds validate` says
                    //it and `gds svg`'s markup is not interrupted by it.
                    foreach (string problem in problems)
                        error.WriteLine($"warn  {describe(path)}: {problem}");

                    return true;
                }
                catch (InvalidDataException problem)
                {
                    error.WriteLine($"fail  {describe(path)}: {problem.Message}");

                    return false;
                }
            }
        }

        ///<summary>
        ///Reads GDSII, OASIS, DXF or SVG, told apart by what the file starts with rather than by its name,
        ///and says which it was - "GDSII", "OASIS", "DXF" or "SVG". The telling apart is the library's, see
        ///<see cref="LayoutFile"/>; this is the name of what it found.
        ///
        ///Public for `gds mcp`, whose open_file reads the same four, and for the tests that read back what it saves.
        ///</summary>
        public static GDS ReadLayout(Stream source, out string format)
        {
            return ReadLayout(source, out format, out _);
        }

        ///<summary>The same, with what the file got wrong that was read past - see <see cref="LayoutFile.Read(Stream, out LayoutFormat, out IReadOnlyList{string})"/>.</summary>
        public static GDS ReadLayout(Stream source, out string format, out IReadOnlyList<string> problems)
        {
            var gds = LayoutFile.Read(source, out LayoutFormat kind, out problems);

            format = LayoutFile.NameOf(kind);

            return gds;
        }

        ///<summary>
        ///Writes to a file or to standard output. UTF-8 without a byte order mark, and the text is written
        ///exactly as it was built - the record dump's line endings are part of a format that gets read back.
        ///</summary>
        private static int writeText(string text, string? destination, TextWriter output)
        {
            if (destination is null || destination == "-")
            {
                output.Write(text);

                return Ok;
            }

            File.WriteAllText(destination, text, new UTF8Encoding(false));

            return Ok;
        }

        #endregion **************************************************************************



        #region Arguments *******************************************************************

        ///<summary>
        ///Every option that is followed by a value, so that value is not mistaken for a file name.
        ///
        ///One list rather than a condition written where it is needed. It was the latter, and adding an
        ///option meant remembering to come here too - which the first run of `gds boolean` did not, and
        ///reported "takes one file, but was given 5" about a command line with one file in it.
        ///</summary>
        private static readonly HashSet<string> ValueOptions = new HashSet<string>
        {
            "-o", "--output",
            "--opacity", "--spacing", "--scale", "--layers", "--hide",
            "--op", "--a", "--b", "--into", "--by",
            "--to", "--layermap", "--write-layermap", "--at", "--from",
            "--deck", "--rule", "--link-port", "--most-elements"
        };

        ///
        ///The whole layout flattened for a command, as far as `--most-elements` says or <see cref="MostElements"/> - or
        ///null when `--most-elements` is not a count, which has been said.
        ///
        ///**Said on standard error when that was not all of it**, unless the command says so itself, as `info` and `drc`
        ///do. A picture, a model or a boolean of part of a layout is what the app shows with a banner over it, and
        ///here nothing else would say it; standard error, so a picture written to standard output is still only the
        ///picture.
        ///
        private static FlattenedLayout? flatten(GDS gds, string[] args, TextWriter error, bool saysItself = false)
        {
            if (!tryReadCount(args, "--most-elements", error, out int most, MostElements))
                return null;

            var layout = GdsFlattener.Flatten(gds, most);

            if (layout.Stopped && !saysItself)
            {
                error.WriteLine(FormattableString.Invariant(
                    $"Only the first {layout.Elements.Count:N0} elements of the layout are here: flattening stopped there. --most-elements takes it further, at about a kilobyte of memory an element."));
            }

            return layout;
        }

        ///<summary>Everything that is not an option or an option's value.</summary>
        private static IEnumerable<string> positional(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];

                if (ValueOptions.Contains(argument))
                {
                    //Skips the value that belongs to it.
                    i++;

                    continue;
                }

                if (argument.StartsWith('-') && argument.Length > 1)
                    continue;

                yield return argument;
            }
        }

        ///<summary>
        ///The single file a command works on. "-" is left as it is, meaning standard input.
        ///</summary>
        private static bool oneInput(string[] args, string command, TextWriter error, out string path)
        {
            var paths = positional(args).ToList();

            path = paths.FirstOrDefault() ?? "";

            if (paths.Count == 0)
            {
                error.WriteLine($"{command} needs a file.");

                return false;
            }

            if (paths.Count > 1)
            {
                error.WriteLine($"{command} takes one file, but was given {paths.Count}.");

                return false;
            }

            return true;
        }

        private static string? outputPath(string[] args)
        {
            return valueOf(args, "-o") ?? valueOf(args, "--output");
        }

        ///
        ///Applies a layermap to the file, if one was named. False when it could not be used.
        ///
        ///**The one thing the app could do that this could not.** A GDSII file carries only numbers, so the
        ///names, the real colors and the real process stack all come from a file the user supplies - and
        ///without a way to hand one over, `gds svg` drew a palette this tool invented and `gds model` stacked
        ///the layers evenly whatever the wafer actually looks like. The library has carried
        ///<see cref="LayerNames"/> the whole time and anything referencing it could load one; only the command
        ///line could not, which made the tool a worse citizen of the same library than the app was.
        ///
        ///Reported the way the app reports it: read as far as it can be, every bad row named by line, and the
        ///count said out loud. A mapping matching *nothing* is called out separately, because rows matching
        ///nothing is normal - a mapping covers a PDK where a file uses a handful of layers - while zero
        ///matching means the wrong technology or the columns in the wrong order, and silence there reads as
        ///the option having done its job.
        ///
        private static bool applyLayerMap(string[] args, GDS gds, TextWriter output, TextWriter error)
        {
            string? path = valueOf(args, "--layermap");

            if (path is null)
                return true;

            string text;

            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception problem)
            {
                error.WriteLine($"Could not read the layermap {path}: {problem.Message}");

                return false;
            }

            var mapping = LayerNames.Parse(text);

            foreach (string trouble in mapping.Problems)
                error.WriteLine(trouble);

            int applied = mapping.ApplyTo(gds.AdditionalInformation.Layers);

            if (applied == 0)
            {
                output.WriteLine($"{path} says nothing about any layer this file uses. Check it is the right technology, and that a CSV's columns are layer,datatype,name.");

                return true;
            }

            output.WriteLine($"{path}: {applied} of this file's {gds.AdditionalInformation.Layers.Count} layer(s) named from {mapping.Count} in the mapping.");

            return true;
        }

        ///<summary>
        ///Reads a numeric option, or leaves <paramref name="value"/> at its default when it was not given.
        ///Invariant, so a machine with a comma for a decimal point does not read --scale 0.5 as 5.
        ///</summary>
        ///
        ///**Zero is allowed for some of these and not others**, which is why the floor is asked for rather
        ///than assumed.
        ///
        ///--spacing is the gap opened on top of wherever a layer already rests, so asking for none of it is
        ///the ordinary case and is the default. --scale and --opacity multiply the whole model, where zero
        ///collapses it to a point or to nothing visible, so they keep the floor they always had.
        ///
        private static bool numberOption(
            string[] args,
            string option,
            double fallback,
            TextWriter error,
            out double value,
            bool zeroAllowed = false)
        {
            value = fallback;

            string? given = valueOf(args, option);

            if (given is null)
                return true;

            bool read = double.TryParse(given, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

            bool tooSmall = value < 0;

            if (!zeroAllowed && value <= 0)
                tooSmall = true;

            if (!read || tooSmall)
            {
                if (zeroAllowed)
                    error.WriteLine($"\"{given}\" is not zero or more for {option}.");
                else
                    error.WriteLine($"\"{given}\" is not a positive number for {option}.");

                return false;
            }

            return true;
        }

        private static string? valueOf(string[] args, string option)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == option)
                    return args[i + 1];
            }

            return null;
        }

        ///<summary>Names standard input as such rather than printing a bare dash at the reader.</summary>
        private static string describe(string path)
        {
            if (path == "-")
                return "standard input";

            return path;
        }

        private static string nameOf(Record? record)
        {
            if (record?.Data is AsciiData ascii)
                return ascii.Value;

            return "(unnamed)";
        }

        #endregion **************************************************************************
    }
}
