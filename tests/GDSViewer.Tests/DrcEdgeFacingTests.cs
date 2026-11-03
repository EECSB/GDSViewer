using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Whether two edges face each other within the limit, decided by the part of each that lies within it - the way
///KLayout decides it - where slanted edges made the old way of deciding it, at each edge's midpoint, answer otherwise.
///
///**Every fixture is two overlapping octagons**, what `gds generate --shapes 4 --columns 1 --rows 1 --corners 8` draws
///on 64/20: edges at 22.7 and 67.3 degrees, and where the two overlap, a neck closing in from above and below to two
///points 95 nm apart - a V on each side. Measured Euclidean at 150, 200, 300 and 399, KLayout counts 2, 10, 24 and
///36 pairs. Until 2026-11-02 this engine counted 1, 5, 19 and 31: it decided facing at midpoints, and kept one report
///per place where KLayout reports a pair from each side of it.
///</summary>
public class DrcEdgeFacingTests
{
    #region Shapes to work with ******************************************************

    private static Element.Point At(int x, int y)
    {
        return new Element.Point { X = x, Y = y };
    }

    ///<summary>A closed outline from x, y pairs, the first corner repeated at the end.</summary>
    private static List<Element.Point> Outline(params int[] xy)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < xy.Length; i += 2)
            points.Add(At(xy[i], xy[i + 1]));

        points.Add(points[0]);

        return points;
    }

    ///<summary>The two octagons as the generator draws them, which the merge makes one outline of.</summary>
    private static List<List<Element.Point>> Octagons()
    {
        return new List<List<Element.Point>>
        {
            Outline(200, 0, 59, 59, 0, 200, 59, 341, 200, 400, 341, 341, 400, 200, 341, 59),
            Outline(560, 0, 419, 59, 360, 200, 419, 341, 560, 400, 701, 341, 760, 200, 701, 59)
        };
    }

    ///<summary>
    ///A pair as text with each part's ends in order and the two parts in order, so a test can name the pairs it
    ///expects however the walk happened to meet them.
    ///</summary>
    private static string Parts(DrcEdgePair pair)
    {
        string a = Part(pair.AFrom, pair.ATo);
        string b = Part(pair.BFrom, pair.BTo);

        if (string.CompareOrdinal(a, b) > 0)
            return $"{b} | {a}";

        return $"{a} | {b}";
    }

    private static string Part(Element.Point from, Element.Point to)
    {
        string one = $"{from.X},{from.Y}";
        string other = $"{to.X},{to.Y}";

        if (string.CompareOrdinal(one, other) > 0)
            return $"{other} {one}";

        return $"{one} {other}";
    }

    #endregion **************************************************************************



    #region Counted as KLayout counts them **********************************************

    ///<summary>
    ///KLayout's counts at the four limits the octagons were measured at, with each engine merging the two for
    ///itself - see <see cref="The_merge_puts_a_crossing_a_unit_lower_than_klayout_does"/> for the one place the two
    ///outlines differ, which the counts come through.
    ///</summary>
    [Fact]
    public void Two_octagons_are_counted_as_klayout_counts_them()
    {
        int[] limits = { 150, 200, 300, 399 };
        int[] klayout = { 2, 10, 24, 36 };

        for (int i = 0; i < limits.Length; i++)
            Assert.Equal(klayout[i], DrcEdges.Width(Octagons(), limits[i], DrcMetric.Euclidean).Count);
    }

    ///<summary>
    ///Where the neck closes from above and below to two points, there is a pair from each side of it: the V's left
    ///side above against its right side below, and its right side above against its left side below.
    ///
    ///**Two reports, not one, which was the choice to make.** Both are 95 nm across, corner to corner, and this
    ///engine used to keep one report per place - one fault, it reasoned, written out twice. KLayout reports both,
    ///because they are two pairs of edges and what it reports is the part of each that is too close; so does this
    ///now, which is what lets the two be compared pair for pair. The sides of the V that point the same way are
    ///more than ninety degrees from facing each other, and are no pair in either.
    ///</summary>
    [Fact]
    public void A_neck_closing_from_both_sides_is_a_pair_from_each_side()
    {
        var found = DrcEdges.Width(Octagons(), 150, DrcMetric.Euclidean);

        Assert.Equal(2, found.Count);
        Assert.All(found, pair => Assert.Equal(95, pair.Distance, 6));

        var expected = new[]
        {
            "358,99 380,152 | 380,247 402,300",
            "358,300 380,247 | 380,152 402,99"
        };

        Assert.Equal(expected.OrderBy(text => text, StringComparer.Ordinal), found.Select(Parts).OrderBy(text => text, StringComparer.Ordinal));
    }

    ///<summary>
    ///The pair the midpoint missed: the V's right side above against the first octagon's bottom right side, 192 nm
    ///from the V's corner to that side's end, inside the shape all the way.
    ///
    ///**The middle of that side is beyond the V side's line**, outside it, so asked at midpoints the two did not
    ///face each other. KLayout asks of the part of each within the limit, and the 12 nm of the side next to its end
    ///is inside the line and within 200 - so it is a pair, and what is reported of the side is those 12 nm, not the
    ///whole 153.
    ///</summary>
    [Fact]
    public void A_side_faces_another_by_its_part_within_the_limit()
    {
        var found = DrcEdges.Width(Octagons(), 200, DrcMetric.Euclidean);

        var pair = Assert.Single(found, pair => Parts(pair) == "329,54 341,59 | 380,247 383,255");

        //From the V's corner at 380,247 to the side's end at 341,59.
        Assert.Equal(Math.Sqrt((39 * 39) + (188 * 188)), pair.Distance, 6);
    }

    #endregion **************************************************************************



    #region What still differs ***********************************************************

    ///<summary>
    ///The merge puts the V's upper corner at 380,247, where the two octagons' sides cross at 380,247.8 and KLayout
    ///puts it at 380,248: Clipper cuts the fraction off, KLayout rounds to the nearest. Clipper is vendored as it ships,
    ///so the unit stays.
    ///
    ///**It tips nearly square pairs from one side of ninety degrees to the other.** On this outline the first octagon's
    ///upper left side and the V's left side above are 89.8 degrees from facing each other, nearest along the side
    ///between them - 152.85 nm - and are a pair, here and in KLayout given this outline; on KLayout's own they are just
    ///over ninety and are not, and the V's sides against the octagons' lower sides come in instead. The counts come
    ///out the same, since the octagons are symmetric and each swap is one for one, but the pairs at that corner differ
    ///by its unit.
    ///</summary>
    [Fact]
    public void The_merge_puts_a_crossing_a_unit_lower_than_klayout_does()
    {
        var merged = Assert.Single(Booleans.Merge(Booleans.AsRings(Octagons())));

        Assert.Contains(At(380, 247), merged.Boundary);

        var here = DrcEdges.Width(Octagons(), 200, DrcMetric.Euclidean);

        Assert.Equal(2, here.Count(pair => Math.Abs(pair.Distance - 152.85) < 0.01));

        var klayoutOutline = new List<List<Element.Point>>
        {
            Outline(200, 0, 59, 59, 0, 200, 59, 341, 200, 400, 341, 341, 380, 248, 419, 341, 560, 400, 701, 341, 760, 200,
                701, 59, 560, 0, 419, 59, 380, 152, 341, 59)
        };

        var there = DrcEdges.Width(klayoutOutline, 200, DrcMetric.Euclidean);

        Assert.Equal(10, there.Count);
        Assert.DoesNotContain(there, pair => Math.Abs(pair.Distance - 152.85) < 0.01);
    }

    ///<summary>
    ///Past the octagons' own size, KLayout pairs each one's far side with the other's near side across the notch above
    ///or below the neck - 405 nm, four such pairs at 450, which it counts 40 and this 36.
    ///
    ///**The ground between them is the difference, not the facing.** The line across runs out of the shape into the
    ///notch and back, and this engine wants it to stay in the material (<c>DrcEdges.Sees</c>). KLayout drops a pair
    ///only when one other edge cuts across both sides of the ground between, and neither side of the V does that on
    ///its own. Only at limits past a shape's own size: on the transistor, IHP's cells and sky130's at their rules'
    ///limits the two agree pair for pair.
    ///</summary>
    [Fact]
    public void Past_its_own_size_a_pair_reaching_across_the_notch_is_not_a_width()
    {
        var found = DrcEdges.Width(Octagons(), 450, DrcMetric.Euclidean);

        Assert.Equal(36, found.Count);
        Assert.DoesNotContain(found, pair => pair.Distance > 400);
    }

    #endregion **************************************************************************



    #region Against KLayout **************************************************************

    ///<summary>
    ///The evidence itself: the generator's octagons handed to KLayout as written, and its counts at the four limits
    ///against these.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void The_generated_octagons_count_as_klayout_counts_them()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        int[] limits = { 150, 200, 300, 399 };

        var generated = Synthetic.Layout(4, corners: 8);
        var shapes = Width64(GdsFlattener.Flatten(generated));

        var theirs = OasisTestData.RuleViolations(generated.Serialize(), "octagons", 64, 20, "width", limits, "Euclidian");
        var mine = limits.Select(limit => DrcEdges.Width(shapes, limit, DrcMetric.Euclidean).Count).ToList();

        Assert.Equal(theirs, mine);
    }

    ///<summary>
    ///And with both handed the one outline - the octagons merged here, written as a single polygon - every metric
    ///counts the same at every limit up to the octagons' size, so what is left between the two engines is the merge's
    ///unit and the ground past the shape's own size, and not the facing.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void Given_one_outline_every_metric_counts_as_klayout_counts()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        int[] limits = { 100, 150, 200, 250, 283, 300, 350, 399 };

        var generated = Synthetic.Layout(4, corners: 8);
        var merged = Assert.Single(Booleans.Merge(Booleans.AsRings(Width64(GdsFlattener.Flatten(generated)))));

        var outline = new FlattenedLayout
        {
            Elements = new List<Element>
            {
                new Element { Layer = new Layer(new LayerKey(64, 20), "#000000"), Points = new List<Element.Point>(merged.Boundary) }
            }
        };

        byte[] written = LayoutWriter.ToGds(generated, outline).Serialize();

        var shapes = new List<IReadOnlyList<Element.Point>> { merged.Boundary };

        var metrics = new (string Theirs, DrcMetric Mine)[]
        {
            ("Euclidian", DrcMetric.Euclidean),
            ("Projection", DrcMetric.Projection),
            ("Square", DrcMetric.Square)
        };

        foreach (var (theirName, mine) in metrics)
        {
            var theirs = OasisTestData.RuleViolations(written, "octagon-outline", 64, 20, "width", limits, theirName);
            var ours = limits.Select(limit => DrcEdges.Width(shapes, limit, mine).Count).ToList();

            Assert.True(theirs.SequenceEqual(ours), $"{theirName}: KLayout {string.Join(" ", theirs)}, here {string.Join(" ", ours)}");
        }
    }

    private static List<IReadOnlyList<Element.Point>> Width64(FlattenedLayout layout)
    {
        var key = new LayerKey(64, 20);

        return layout.Elements
            .Where(element => element.Text is null && !element.IsOpen && element.Layer.Key.Equals(key))
            .Select(element => (IReadOnlyList<Element.Point>)element.Points)
            .ToList();
    }

    #endregion **************************************************************************
}
