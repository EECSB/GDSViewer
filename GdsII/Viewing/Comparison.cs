namespace GdsII
{
    ///<summary>Which of the two layouts a difference is drawn in.</summary>
    public enum DifferenceSide
    {
        ///<summary>Drawn in the layout being compared, and not in the one it is compared with.</summary>
        OnlyHere,

        ///<summary>Drawn in the other one, and missing from this.</summary>
        OnlyThere
    }

    ///<summary>
    ///One place two layouts differ: a region of one layer drawn in one of them and not in the other.
    ///</summary>
    public sealed class Difference
    {
        public required LayerKey Layer { get; init; }

        public required DifferenceSide Side { get; init; }

        ///<summary>The region, as one hole-free outline - a keyhole where it has a hole, as GDSII would need.</summary>
        public required List<Element.Point> Region { get; init; }

        ///<summary>The box round the region, which is what stepping to it frames.</summary>
        public Bounds Bounds
        {
            get
            {
                box ??= Bounds.Of(Region);

                return box.Value;
            }
        }

        private Bounds? box;

        ///<summary>How much ground it covers, in square database units.</summary>
        public double Area
        {
            get { return Math.Abs(Measure.AreaOf(Region)); }
        }
    }

    ///<summary>What a comparison found, and what it looked at.</summary>
    public sealed class Comparison
    {
        ///<summary>Every difference, layer by layer in layer order, and down and across each layer.</summary>
        public List<Difference> Differences { get; } = new List<Difference>();

        ///<summary>Every layer either layout draws on, in order - the ones that matched as well as the ones that did not.</summary>
        public List<LayerKey> Compared { get; } = new List<LayerKey>();

        ///<summary>Whether the two draw exactly the same thing on every layer.</summary>
        public bool Same
        {
            get { return Differences.Count == 0; }
        }
    }

    ///<summary>
    ///Two layouts laid over each other, layer by layer, and where they differ: an XOR.
    ///
    ///**What a layout engineer means by "did anything change".** Two files can be the same layout and share
    ///no bytes - cells renamed, shapes cut differently, placements flattened, one of them OASIS - and two
    ///files can differ by one rectangle in a million. Comparing the bytes, the records or the hierarchy
    ///answers the wrong question; comparing what each one *draws* answers the right one. So both are
    ///flattened, every layer of each is merged, and the area one covers that the other does not is the
    ///difference - in both directions, kept apart, since "only in the new one" and "only in the old one" are
    ///different findings.
    ///
    ///Matched by layer and datatype: a layer the other file does not have at all is all difference, which is
    ///what a layer that went missing or arrived is.
    ///</summary>
    public static class Compare
    {
        ///
        ///Compares <paramref name="here"/> with <paramref name="there"/>.
        ///
        ///<paramref name="scaleThere"/> takes the other layout's coordinates onto this one's database unit -
        ///a file drawn in nanometers compared with one in ångströms is ten times bigger otherwise. Exact when
        ///the two units are the same, which is nearly always.
        ///
        ///<paramref name="ignoreUnder"/> leaves out a difference too thin to matter: every region is shrunk
        ///by half of it and grown back, which takes away a sliver narrower than that and nothing else. Zero,
        ///the default, keeps everything - a comparison that quietly drops a difference is the worst kind.
        ///Worth setting where one file was snapped to a grid the other was not, and every edge moved a unit.
        ///
        public static Comparison Layouts(FlattenedLayout here, FlattenedLayout there, double scaleThere = 1, int ignoreUnder = 0)
        {
            var mine = byLayer(here, 1);
            var theirs = byLayer(there, scaleThere);

            var comparison = new Comparison();

            var layers = mine.Keys.Union(theirs.Keys)
                .OrderBy(key => key.Number)
                .ThenBy(key => key.DataType)
                .ToList();

            foreach (var layer in layers)
            {
                comparison.Compared.Add(layer);

                mine.TryGetValue(layer, out var ours);
                theirs.TryGetValue(layer, out var others);

                ours ??= new List<IReadOnlyList<Element.Point>>();
                others ??= new List<IReadOnlyList<Element.Point>>();

                add(comparison, layer, DifferenceSide.OnlyHere, Booleans.Combine(ours, others, BooleanOperation.Not), ignoreUnder);
                add(comparison, layer, DifferenceSide.OnlyThere, Booleans.Combine(others, ours, BooleanOperation.Not), ignoreUnder);
            }

            return comparison;
        }

        private static void add(Comparison comparison, LayerKey layer, DifferenceSide side, List<List<Element.Point>> regions, int ignoreUnder)
        {
            if (ignoreUnder > 0 && regions.Count > 0)
            {
                int half = Math.Max(1, ignoreUnder / 2);

                regions = Booleans.Grow(Booleans.Grow(regions, -half), half);
            }

            //Down and across, so stepping through a layer's differences walks it the way it is read.
            foreach (var region in regions.OrderBy(region => Bounds.Of(region).Bottom).ThenBy(region => Bounds.Of(region).Left))
            {
                if (region.Count < 3)
                    continue;

                comparison.Differences.Add(new Difference { Layer = layer, Side = side, Region = region });
            }
        }

        ///<summary>A layout's areas by layer: no labels, which have none, and no lines of no width, which have none either.</summary>
        private static Dictionary<LayerKey, List<IReadOnlyList<Element.Point>>> byLayer(FlattenedLayout layout, double scale)
        {
            var found = new Dictionary<LayerKey, List<IReadOnlyList<Element.Point>>>();

            foreach (var element in layout.Elements)
            {
                if (element.Text is not null || element.IsOpen || element.Points.Count < 3)
                    continue;

                if (!found.TryGetValue(element.Layer.Key, out var shapes))
                {
                    shapes = new List<IReadOnlyList<Element.Point>>();
                    found[element.Layer.Key] = shapes;
                }

                if (scale == 1)
                {
                    shapes.Add(element.Points);

                    continue;
                }

                var scaled = new List<Element.Point>(element.Points.Count);

                foreach (var point in element.Points)
                    scaled.Add(new Element.Point((int)Math.Round(point.X * scale), (int)Math.Round(point.Y * scale)));

                shapes.Add(scaled);
            }

            return found;
        }
    }
}
