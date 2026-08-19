namespace GdsII.Cli
{
    ///<summary>
    ///Narrows a layout to the layers asked for, which is what the sidebar's checkboxes do in the app.
    ///
    ///Written against the flattened layout rather than against either command, so `svg` and `model` take
    ///the same options and mean the same thing by them - and so the bounds each works out follow what is
    ///left rather than framing layers that were dropped.
    ///</summary>
    public static class LayerFilter
    {
        ///<summary>
        ///Applies --layers and --hide. False when a spec could not be read at all, which is the command
        ///line's problem rather than the file's; a spec that reads fine but matches nothing in this
        ///particular file is reported and carried on from, so a run over a directory is not stopped by one
        ///cell that happens not to use a layer.
        ///</summary>
        public static bool TryApply(FlattenedLayout layout, string? show, string? hide, TextWriter error, out FlattenedLayout filtered)
        {
            filtered = layout;

            if (show is null && hide is null)
                return true;

            if (!tryParse(show, error, out var shown) || !tryParse(hide, error, out var hidden))
                return false;

            var present = layout.Elements.Select(element => element.Layer.Key).ToHashSet();

            report(shown, present, "--layers", error);
            report(hidden, present, "--hide", error);

            var keep = new FlattenedLayout
            {
                UnresolvedReferences = layout.UnresolvedReferences,
                DepthLimitReached = layout.DepthLimitReached
            };

            foreach (var element in layout.Elements)
            {
                if (show is not null && !matchesAny(shown, element.Layer.Key))
                    continue;

                if (matchesAny(hidden, element.Layer.Key))
                    continue;

                keep.Elements.Add(element);
            }

            filtered = keep;

            return true;
        }

        ///<summary>
        ///One entry of a list. A data type of null means the whole layer number - "65" is every purpose
        ///drawn on layer 65, where "65/20" is only the drawn geometry. Both are how a layer gets written
        ///in a layermap, in KLayout, and in this tool's own output, so both are accepted.
        ///</summary>
        private readonly record struct Spec(short Number, short? DataType);

        private static bool tryParse(string? list, TextWriter error, out List<Spec> specs)
        {
            specs = new List<Spec>();

            if (list is null)
                return true;

            foreach (string entry in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] halves = entry.Split('/');

                if (halves.Length > 2 || !short.TryParse(halves[0], out short number))
                {
                    error.WriteLine($"\"{entry}\" is not a layer. Write one as 65 or as 65/20, separated by commas.");

                    return false;
                }

                if (halves.Length == 1)
                {
                    specs.Add(new Spec(number, null));

                    continue;
                }

                //The way this tool prints a data type it could not read, so it round-trips.
                if (halves[1] == "?")
                {
                    specs.Add(new Spec(number, LayerKey.UnknownDataType));

                    continue;
                }

                if (!short.TryParse(halves[1], out short dataType))
                {
                    error.WriteLine($"\"{entry}\" is not a layer. Write one as 65 or as 65/20, separated by commas.");

                    return false;
                }

                specs.Add(new Spec(number, dataType));
            }

            return true;
        }

        private static bool matchesAny(List<Spec> specs, LayerKey key)
        {
            foreach (var spec in specs)
            {
                if (spec.Number != key.Number)
                    continue;

                if (spec.DataType is null || spec.DataType == key.DataType)
                    return true;
            }

            return false;
        }

        ///<summary>
        ///Names a layer that was asked for and is not in this file. Worth saying: the usual cause is a
        ///typo or a layer from another technology, and silence there looks exactly like a layer that was
        ///found and happened to be empty.
        ///</summary>
        private static void report(List<Spec> specs, HashSet<LayerKey> present, string option, TextWriter error)
        {
            foreach (var spec in specs)
            {
                bool found = present.Any(key => matchesAny(new List<Spec> { spec }, key));

                if (found)
                    continue;

                string wanted = spec.Number.ToString();

                if (spec.DataType is not null)
                    wanted = new LayerKey(spec.Number, spec.DataType.Value).ToString();

                error.WriteLine($"{option}: this file has nothing on {wanted}.");
            }
        }
    }
}
