using System.Globalization;

namespace GdsII.Cli
{
    ///<summary>
    ///The two commands that change geometry rather than report on it: combining two layers with a boolean,
    ///and growing or shrinking one.
    ///
    ///Both write a **flat** library. Flattening is what the operations work on - a boolean between two
    ///layers means nothing until the references that place them have been resolved - and putting the
    ///hierarchy back afterwards would mean deciding which cell a derived shape belongs to, which is a
    ///question the operation does not answer.
    ///</summary>
    public static partial class Cli
    {
        #region boolean *********************************************************************

        private static int boolean(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "boolean", error, out string path))
                return UsageError;

            if (!tryParseOperation(valueOf(args, "--op"), error, out var operation))
                return UsageError;

            if (!tryParseLayer(valueOf(args, "--a"), "--a", required: true, error, out var a))
                return UsageError;

            if (!tryParseLayer(valueOf(args, "--b"), "--b", required: true, error, out var b))
                return UsageError;

            if (!tryParseLayer(valueOf(args, "--into"), "--into", required: true, error, out var into))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            var layout = GdsFlattener.Flatten(gds!);

            var result = Booleans.Combine(shapesOn(layout, a!.Value), shapesOn(layout, b!.Value), operation);

            return writeDerived(args, gds!, layout, result, into!.Value, output, error);
        }

        private static bool tryParseOperation(string? given, TextWriter error, out BooleanOperation operation)
        {
            operation = BooleanOperation.And;

            switch (given?.ToLowerInvariant())
            {
                case "and": operation = BooleanOperation.And; return true;
                case "or": operation = BooleanOperation.Or; return true;
                case "not": operation = BooleanOperation.Not; return true;
                case "xor": operation = BooleanOperation.Xor; return true;
            }

            if (given is null)
                error.WriteLine("boolean needs --op: and, or, not or xor.");
            else
                error.WriteLine($"\"{given}\" is not an operation. It is one of and, or, not, xor.");

            return false;
        }

        #endregion **************************************************************************



        #region size ************************************************************************

        private static int size(string[] args, TextWriter output, TextWriter error)
        {
            if (!oneInput(args, "size", error, out string path))
                return UsageError;

            string? given = valueOf(args, "--by");

            if (given is null)
            {
                error.WriteLine("size needs --by: how far to move every edge, in database units. A negative number shrinks.");

                return UsageError;
            }

            if (!int.TryParse(given, NumberStyles.Integer, CultureInfo.InvariantCulture, out int by))
            {
                error.WriteLine($"\"{given}\" is not a whole number of database units.");

                return UsageError;
            }

            if (!tryParseLayer(valueOf(args, "--a"), "--a", required: true, error, out var a))
                return UsageError;

            if (!tryParseLayer(valueOf(args, "--into"), "--into", required: false, error, out var into))
                return UsageError;

            if (!read(path, error, out GDS? gds))
                return FileError;

            var layout = GdsFlattener.Flatten(gds!);

            var result = Booleans.Grow(shapesOn(layout, a!.Value), by);

            //Back onto the layer it came from unless told otherwise, which is what sizing usually means.
            //Deriving a second layer is what boolean is for, and --into is here for the cases where a
            //grown copy is wanted beside the original rather than instead of it.
            return writeDerived(args, gds!, layout, result, into ?? a.Value, output, error);
        }

        #endregion **************************************************************************



        #region Shared **********************************************************************

        ///<summary>
        ///Everything drawn on one layer/datatype pair. Labels are left out: a boolean is about area, and a
        ///TEXT element is an anchor and a string rather than a shape.
        ///</summary>
        private static List<IReadOnlyList<Element.Point>> shapesOn(FlattenedLayout layout, LayerKey key)
        {
            var shapes = new List<IReadOnlyList<Element.Point>>();

            foreach (var element in layout.Elements)
            {
                if (element.Text is null && element.Layer.Key.Equals(key))
                    shapes.Add(element.Points);
            }

            return shapes;
        }

        ///<summary>
        ///Writes the result, either on its own or added to the flattened original.
        ///
        ///Alongside by default, because a derived layer is nearly always looked at against what it came
        ///from - and this app opens one file at a time, so the alternative is not seeing them together.
        ///</summary>
        private static int writeDerived(
            string[] args,
            GDS gds,
            FlattenedLayout layout,
            List<List<Element.Point>> result,
            LayerKey into,
            TextWriter output,
            TextWriter error)
        {
            string? destination = outputPath(args);

            if (destination is null || destination == "-")
            {
                error.WriteLine("This writes binary, so it needs -o <file>.");

                return UsageError;
            }

            var written = new FlattenedLayout();

            if (!args.Contains("--only"))
            {
                foreach (var element in layout.Elements)
                {
                    //Anything already on the target layer would be indistinguishable from what was
                    //derived, so it makes way for it.
                    if (!element.Layer.Key.Equals(into))
                        written.Elements.Add(element);
                }
            }

            var layer = new Layer(into, DerivedColor);

            foreach (var shape in result)
                written.Elements.Add(new Element { Layer = layer, Points = shape });

            File.WriteAllBytes(destination, LayoutWriter.ToGds(gds, written).Serialize());

            output.WriteLine($"Wrote {destination}: {result.Count} shape(s) on {into}.");

            return Ok;
        }

        ///<summary>
        ///What a derived layer is drawn in when the file is opened. Overwritten by the palette as soon as
        ///it is - the app assigns colors from how many layers a file has - so this only has to be valid.
        ///</summary>
        private const string DerivedColor = "#808080";

        ///<summary>
        ///A layer and data type, written the way `gds layers` prints it.
        ///
        ///A bare number is not accepted here, unlike in --layers and --hide. Those narrow a drawing, where
        ///"65" sensibly means every purpose on it; an operation reads from exactly one pair and writes onto
        ///exactly one, so a bare number would be a question rather than an answer.
        ///</summary>
        private static bool tryParseLayer(string? given, string option, bool required, TextWriter error, out LayerKey? key)
        {
            key = null;

            if (given is null)
            {
                if (required)
                    error.WriteLine($"This needs {option}, one layer and data type like 65/20.");

                return !required;
            }

            string[] halves = given.Split('/');

            if (halves.Length == 2
                && short.TryParse(halves[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out short number)
                && short.TryParse(halves[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out short dataType))
            {
                key = new LayerKey(number, dataType);

                return true;
            }

            error.WriteLine($"{option} takes one layer and data type, like 65/20. \"{given}\" is not one.");

            return false;
        }

        #endregion **************************************************************************
    }
}
