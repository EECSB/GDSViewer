using System.Globalization;

namespace GdsII.Cli
{
    ///<summary>
    ///The three commands that report on a layout's *structure* rather than on its bytes or its geometry:
    ///which cells place which, what is electrically joined to what, and how far apart two points are.
    ///
    ///All three existed in the app and in the library long before they existed here, and all three were named
    ///in docs/CLI.md as gaps rather than left to be rediscovered. `nets` in particular could not have worked
    ///before `--layermap` did: tracing needs to know which numbers are metal, and nothing in a GDSII file
    ///says.
    ///</summary>
    public static partial class Cli
    {
        #region cells ***********************************************************************

        ///
        ///The library's cells: what places what, and how much is in each.
        ///
        ///Flat by default and indented under `--tree`, which is the same pair of shapes the app's Cells
        ///sidebar offers - <see cref="Hierarchy.Summarize"/> and <see cref="Hierarchy.Tree"/>, so the two
        ///cannot come to disagree about what places what.
        ///
        ///In the file's own order rather than sorted. That is the order the cells were written, which in a
        ///library built by a tool usually means leaves first and the thing you actually want last - and
        ///sorting it would hide that.
        ///
        private static int cells(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "cells", error, out string path))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            var summaries = Hierarchy.Summarize(gds!);

            if (summaries.Count == 0)
            {
                error.WriteLine($"{describe(path)} holds no cells.");

                return FileError;
            }

            if (args.Contains("--tree"))
                return cellTree(gds!, summaries, output);

            //Wide enough for the longest name, because sky130's run past thirty characters and a column cut
            //to fit the short ones puts every count out of line by a different amount.
            int column = Math.Max(4, summaries.Max(cell => cell.Name.Length) + 2);

            output.WriteLine($"{"cell".PadRight(column)} elements   places   placed by");

            foreach (var cell in summaries)
            {
                //A top is a cell nothing places, which the flattener draws on its own - worth marking, since
                //it is the answer to "which of these is the layout".
                string top = "";

                if (cell.IsTop)
                    top = "   top";

                output.WriteLine($"{cell.Name.PadRight(column)} {cell.Elements,8}   {cell.Places,6}   {cell.PlacedBy,9}{top}");
            }

            int tops = summaries.Count(cell => cell.IsTop);

            output.WriteLine();
            output.WriteLine($"{summaries.Count} cell(s), {tops} of them placed by nothing.");

            return Ok;
        }

        ///
        ///The same cells indented under whatever places them.
        ///
        ///**A cell placed in two parents appears under both**, which is where this parts company with a
        ///folder tree: a directory is in one place, and a GDS cell is genuinely shared. The second and later
        ///times one is reached its children are left out and the row is marked, because the shape below is
        ///identical to the first and the first is the one worth walking. All of that is
        ///<see cref="Hierarchy.Tree"/>'s doing rather than this method's - what is here is the drawing.
        ///
        private static int cellTree(GDS gds, List<Hierarchy.CellSummary> summaries, TextWriter output)
        {
            var rows = Hierarchy.Tree(gds);

            foreach (var row in rows)
            {
                string indent = new string(' ', row.Depth * 2);

                string again = "";

                if (row.Repeats)
                    again = "  (again)";

                output.WriteLine($"{indent}{row.Cell.Name}   {row.Cell.Elements} element(s), {row.Cell.Places} placement(s){again}");
            }

            output.WriteLine();
            output.WriteLine($"{summaries.Count} cell(s) in {rows.Count} row(s). A cell placed twice is listed twice, and marked the second time.");

            return Ok;
        }

        #endregion **************************************************************************



        #region nets ************************************************************************

        ///
        ///Everything electrically joined to the shape at a point.
        ///
        ///**Needs a layermap, and says so plainly when it has none.** Nothing in a GDSII file records which
        ///of its numbers carry a net and which join what they overlap - that is PDK data, so without
        ///`--layermap` no layer takes part and the honest answer is that the question cannot be asked yet.
        ///That is the failure this command will hit most often, so it is the one worded most carefully; the
        ///app grays its own button out for the same reason.
        ///
        ///One net, from one point, rather than every net in the file. That is what
        ///<see cref="Nets.Reaching"/> does and it is deliberate on the library's side: a full extraction over
        ///a large layout is the expensive thing it does not do, and one net is what somebody asking about a
        ///wire wants.
        ///
        private static int nets(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "nets", error, out string path))
                return UsageError;

            string? at = valueOf(args, "--at");

            if (at is null)
            {
                error.WriteLine("nets needs --at <x,y>, a point in database units on the shape to trace from. `gds layers <file> --area` prints each layer's bounds to aim inside.");

                return UsageError;
            }

            if (!tryReadPoint(at, error, out Element.Point point))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            //Before flattening, since a role lives on the Layer each flattened element points at.
            if (!applyLayerMap(args, gds!, output, error))
                return UsageError;

            if (!Nets.AnyRolesSet(gds!.AdditionalInformation.Layers.Values))
            {
                error.WriteLine("No layer in this file has a role, so nothing can be traced.");
                error.WriteLine("A GDSII file does not say which of its numbers are metal. Pass --layermap <file> with a `role` column of conductor or via - see the seventh column in `gds layers <file> --write-layermap`.");

                return UsageError;
            }

            var layout = GdsFlattener.Flatten(gds);

            int from = Picking.At(layout, point);

            if (from < 0)
            {
                error.WriteLine($"Nothing is drawn at {point.X},{point.Y}.");

                return FileError;
            }

            var found = Nets.Reaching(layout, from);

            var started = layout.Elements[from];

            //Empty rather than one is how Reaching says "this layer takes no part", which is a different
            //answer from "nothing else is attached" and reads identically if it is not separated here.
            if (found.Count == 0)
            {
                //DisplayName rather than the bare pair, the same as the traced message below: if a mapping
                //named the layer, saying `nsdm (93/44)` answers "which layer did I hit" in one line.
                output.WriteLine($"The shape at {point.X},{point.Y} is on {started.Layer.DisplayName}, which has no role, so it carries no net.");

                return Ok;
            }

            output.WriteLine($"Traced from {started.Layer.DisplayName} at {point.X},{point.Y}.");
            output.WriteLine();

            //Per layer, because "forty shapes" says less than which layers they are on - a net that reaches
            //met3 and one that stops at li1 are different answers to the same click.
            var perLayer = new SortedDictionary<LayerKey, int>();

            foreach (int index in found)
            {
                var key = layout.Elements[index].Layer.Key;

                perLayer.TryGetValue(key, out int count);
                perLayer[key] = count + 1;
            }

            int column = Math.Max(16, gds.AdditionalInformation.Layers.Values.Max(layer => layer.DisplayName.Length) + 2);

            output.WriteLine($"{"layer".PadRight(column)} shapes");

            foreach (var layer in perLayer)
            {
                string says = layer.Key.ToString();

                if (gds.AdditionalInformation.Layers.TryGetValue(layer.Key, out var known))
                    says = known.DisplayName;

                output.WriteLine($"{says.PadRight(column)} {layer.Value,6}");
            }

            output.WriteLine();
            output.WriteLine($"{found.Count} shape(s) across {perLayer.Count} layer(s).");

            //The labels sitting on it, which is the whole point for anybody checking a net is the net they
            //think it is. More than one distinct name is worth seeing rather than hiding: it is either two
            //spellings of one thing or two nets shorted together, and both are worth knowing.
            var names = Nets.NamesOn(layout, found.ToList());

            if (names.Count == 0)
                output.WriteLine("No label sits on it.");
            else if (names.Count == 1)
                output.WriteLine($"Named {names[0]}.");
            else
                output.WriteLine($"Carries {names.Count} distinct names, which is either two spellings or a short: {string.Join(", ", names)}");

            if (args.Contains("--shapes"))
            {
                output.WriteLine();
                output.WriteLine("index   layer            points");

                foreach (int index in found.OrderBy(one => one))
                {
                    var element = layout.Elements[index];

                    output.WriteLine($"{index,5}   {element.Layer.Key.ToString(),-16} {element.Points.Count,6}");
                }
            }

            return Ok;
        }

        #endregion **************************************************************************



        #region measure *********************************************************************

        ///
        ///The distance between two points, which is the 2D view's ruler without the view.
        ///
        ///The same three numbers it puts on screen and worked out the same way - dx, dy, and the straight
        ///line between them - because a measurement that disagreed with the one in the app would be worse
        ///than no measurement at all. `jstests/viewGeometry.test.js` pins the ruler's own arithmetic on the
        ///same 300-by-400 case this is tested on, so the two are held to one contract rather than to whatever
        ///each happens to compute.
        ///
        ///**In microns as well, when the file says what a unit is.** A database unit is a nanometer on most
        ///files, but only the UNITS record knows, and a file that does not carry a usable one gets units alone
        ///rather than a number invented for it.
        ///
        private static int measure(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "measure", error, out string path))
                return UsageError;

            string? from = valueOf(args, "--from");
            string? to = valueOf(args, "--to");

            if (from is null || to is null)
            {
                error.WriteLine("measure needs --from <x,y> and --to <x,y>, both in database units.");

                return UsageError;
            }

            if (!tryReadPoint(from, error, out Element.Point start))
                return UsageError;

            if (!tryReadPoint(to, error, out Element.Point end))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            //
            //**Long rather than int**, and the cast is on the first operand so the subtraction is done wide.
            //
            //Two coordinates at opposite ends of the signed range are further apart than an int holds, and in
            //int the difference comes back *negative* - which reads as a distance rather than as a failure,
            //and is the worst way for a measurement to be wrong.
            //
            long dx = (long)end.X - start.X;
            long dy = (long)end.Y - start.Y;

            double distance = Math.Sqrt(((double)dx * dx) + ((double)dy * dy));

            output.WriteLine(FormattableString.Invariant($"dx {dx}, dy {dy}"));

            //Two decimals on the units because the endpoints are whole and the diagonal between them is not;
            //four on the microns because a unit is usually a nanometer, and three would round a single-unit
            //measurement away to nothing. Both are the 2D view's own, so the two agree.
            if (micronsPerUnit(gds!) is not double microns)
            {
                output.WriteLine(FormattableString.Invariant($"{distance:0.00} units"));
                output.WriteLine($"{describe(path)} does not say what a database unit is, so there is no micron figure. Its UNITS record is missing or unusable.");

                return Ok;
            }

            output.WriteLine(FormattableString.Invariant($"{distance:0.00} units  ({distance * microns:0.0000} µm)"));

            return Ok;
        }

        ///
        ///How many microns one database unit is, or null when the file does not say.
        ///
        ///The second half of UNITS is meters per database unit, so this is that times a million. Null rather
        ///than a guess for a missing, zero or nonsense value: a file that does not say is a real thing to be
        ///handed, and inventing a nanometer for it would put a wrong number where a missing one belongs.
        ///
        private static double? micronsPerUnit(GDS gds)
        {
            if (gds.StreamFormat.UNITS?.Data is not Real8Data units || units.Values.Length < 2)
                return null;

            double metersPerUnit = units.Values[1];

            if (metersPerUnit <= 0 || double.IsNaN(metersPerUnit) || double.IsInfinity(metersPerUnit))
                return null;

            return metersPerUnit * 1e6;
        }

        #endregion **************************************************************************



        #region Shared **********************************************************************

        ///
        ///An `x,y` in database units.
        ///
        ///Whole numbers, because that is what a coordinate in this format is - a fractional one would be
        ///somebody thinking in microns, and rounding it silently would put the trace on a neighboring shape
        ///and answer the wrong question. Invariant, so a comma-decimal machine does not read `1,5` as one and
        ///a half and then find one field where it wanted two.
        ///
        private static bool tryReadPoint(string given, TextWriter error, out Element.Point point)
        {
            point = new Element.Point(0, 0);

            string[] halves = given.Split(',');

            if (halves.Length != 2)
            {
                error.WriteLine($"\"{given}\" is not a point. Write it as x,y in database units, e.g. --at 1200,800.");

                return false;
            }

            if (!int.TryParse(halves[0].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int x)
                || !int.TryParse(halves[1].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int y))
            {
                error.WriteLine($"\"{given}\" is not a point. Both halves have to be whole numbers of database units, e.g. --at 1200,800.");

                return false;
            }

            point = new Element.Point(x, y);

            return true;
        }

        #endregion **************************************************************************
    }
}
