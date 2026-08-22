using System.Text;
using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The edge-pair engine, which answers in pairs of edges rather than in regions.
///
///**What it is for is the three things the region checks cannot do.** It measures Euclidean rather than
///only square; it is exact at every limit rather than missing one database unit under an even one; and it
///can be told to consider only edges that face each other, which is what a rule like sky130's poly.4 -
///"parallel edges only" - is written against.
///
///So the tests here are mostly about those three, and about the one property both engines must share:
///a shape exactly at the limit is legal, because drawing to minimum is what layout is.
///</summary>
public class DrcEdgeTests
{
    private static Element.Point At(int x, int y)
    {
        return new Element.Point { X = x, Y = y };
    }

    private static List<Element.Point> Box(int left, int bottom, int right, int top)
    {
        return new List<Element.Point>
        {
            At(left, bottom),
            At(right, bottom),
            At(right, top),
            At(left, top),
            At(left, bottom)
        };
    }

    private static List<List<Element.Point>> One(List<Element.Point> shape)
    {
        return new List<List<Element.Point>> { shape };
    }

    #region Width ***********************************************************************

    [Fact]
    public void A_shape_exactly_at_the_minimum_width_passes()
    {
        Assert.Empty(DrcEdges.Width(One(Box(0, 0, 140, 1000)), 140));
    }

    [Fact]
    public void A_shape_under_the_minimum_width_is_reported()
    {
        Assert.NotEmpty(DrcEdges.Width(One(Box(0, 0, 135, 1000)), 140));
    }

    ///<summary>
    ///The database unit the region engine misses, found.
    ///
    ///**This is the gap that justified the whole exercise.** An opening halves the limit onto an integer
    ///grid, and for an even limit there is no radius that both passes the limit and catches one below it -
    ///so a width of exactly limit minus one falls through. DrcCheckTests pins that miss deliberately. An
    ///edge pair measures the distance itself and has no halving to round, so it is exact at every limit.
    ///</summary>
    [Fact]
    public void One_database_unit_under_an_even_limit_is_found_here()
    {
        //The region engine passes this. See DrcCheckTests.One_database_unit_under_an_even_limit_is_the_known_miss.
        Assert.Empty(DrcChecks.Width(One(Box(0, 0, 139, 1000)), 140));

        Assert.NotEmpty(DrcEdges.Width(One(Box(0, 0, 139, 1000)), 140));
    }

    ///<summary>And the distance it reports is the width, not something near it.</summary>
    [Fact]
    public void The_distance_reported_is_the_one_measured()
    {
        var found = DrcEdges.Width(One(Box(0, 0, 139, 1000)), 140);

        Assert.All(found, pair => Assert.Equal(139, pair.Distance, 6));
    }

    #endregion **************************************************************************



    #region Spacing *********************************************************************

    [Fact]
    public void Two_shapes_exactly_the_minimum_apart_pass()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1140, 0, 2000, 1000)
        };

        Assert.Empty(DrcEdges.Space(shapes, 140));
    }

    [Fact]
    public void Two_shapes_a_unit_closer_are_reported()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1139, 0, 2000, 1000)
        };

        var found = DrcEdges.Space(shapes, 140);

        Assert.NotEmpty(found);
        Assert.Equal(139, found[0].Distance, 6);
    }

    ///<summary>
    ///A gap inside one shape is a spacing fault too, the way sky130's x.6 says it is.
    ///
    ///"All intra-layer separation checks will include a notch check" - and two edges facing each other
    ///across the inside of a U face each other across empty ground exactly as two separate shapes do.
    ///</summary>
    [Fact]
    public void A_notch_is_found_by_the_spacing_check()
    {
        //A U with a hundred units between its arms.
        var u = new List<Element.Point>
        {
            At(0, 0),
            At(300, 0),
            At(300, 300),
            At(200, 300),
            At(200, 100),
            At(100, 100),
            At(100, 300),
            At(0, 300),
            At(0, 0)
        };

        Assert.NotEmpty(DrcEdges.Space(One(u), 140));
    }

    #endregion **************************************************************************



    #region Facing **********************************************************************

    ///<summary>
    ///Two edges that are close but do not face each other are not a pair.
    ///
    ///**The half that a naive distance test gets wrong.** Every edge of a shape is near the edges beside
    ///it, and a corner is two edges a fraction apart - so a check that only measured distance would report
    ///every corner in the layout. Each edge has to lie on the correct side of the other, both ways round.
    ///</summary>
    [Fact]
    public void The_two_edges_of_a_corner_are_not_a_spacing_fault()
    {
        //One solid square, well over any limit in every direction. Its own corners must report nothing.
        Assert.Empty(DrcEdges.Space(One(Box(0, 0, 1000, 1000)), 140));
    }

    ///<summary>And a solid square is not too narrow either, however its corners are measured.</summary>
    [Fact]
    public void A_shape_far_wider_than_the_limit_reports_nothing()
    {
        Assert.Empty(DrcEdges.Width(One(Box(0, 0, 1000, 1000)), 140));
    }

    #endregion **************************************************************************



    #region The metrics *****************************************************************

    ///
    ///Two squares set corner to corner, a classic diagonal approach.
    ///
    ///Their nearest corners are 100 apart on each axis, so 141.42 apart Euclidean and 100 apart by the
    ///square metric. A limit of 120 is between the two, which is what makes them disagree.
    ///
    private static List<List<Element.Point>> Diagonal()
    {
        return new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1100, 1100, 2000, 2000)
        };
    }

    [Fact]
    public void Euclidean_measures_a_diagonal_the_long_way()
    {
        //141.42 apart, so a limit of 120 is met.
        Assert.Empty(DrcEdges.Space(Diagonal(), 120, DrcMetric.Euclidean));
    }

    [Fact]
    public void The_square_metric_measures_it_the_short_way()
    {
        //100 apart by the larger axis, so the same limit is broken.
        Assert.NotEmpty(DrcEdges.Space(Diagonal(), 120, DrcMetric.Square));
    }

    #endregion **************************************************************************



    #region Parallel edges only *********************************************************

    ///
    ///A wire running end-on at another, which is what a perpendicular approach looks like.
    ///
    ///The end of the upright is 100 from the side of the flat one. Measured any way that counts corners
    ///that is a spacing fault; measured across the part where the two run alongside each other, there is no
    ///such part and nothing to report.
    ///
    private static List<List<Element.Point>> EndOn()
    {
        return new List<List<Element.Point>>
        {
            //A flat bar along the bottom.
            Box(0, 0, 2000, 200),

            //And an upright stopping 100 short of it.
            Box(900, 300, 1100, 2000)
        };
    }

    ///<summary>
    ///The rule this engine was written for.
    ///
    ///sky130's poly.4 is "spacing of poly on field to diff, **parallel edges only**", and the qualifier is
    ///the whole rule: measured without it the corner approaches in a cell are violations and the real ones
    ///are buried among them.
    ///
    ///**The case it excludes is a corner, not a perpendicular edge.** Writing this test the first time I
    ///built a wire running end-on at a bar and expected projection to reject it - and it should not, since
    ///the wire's end edge runs perfectly parallel to the bar's side and genuinely is a pair. What has no
    ///parallel facing at all is two shapes set corner to corner: their nearest edges are parallel but their
    ///spans do not overlap, so there is no run over which they are alongside each other.
    ///</summary>
    [Fact]
    public void A_corner_approach_is_not_a_parallel_edge_pair()
    {
        //141 apart corner to corner, so a limit of 200 catches it Euclidean.
        Assert.NotEmpty(DrcEdges.Space(Diagonal(), 200, DrcMetric.Euclidean));

        //And nothing runs alongside anything, so there is no parallel pair to report.
        Assert.Empty(DrcEdges.Space(Diagonal(), 200, DrcMetric.Projection));
    }

    ///<summary>
    ///A wire ending near a bar *is* a parallel pair, which is the mirror of the test above.
    ///
    ///Its end edge runs alongside the bar's side for the wire's whole width. Rejecting this would be the
    ///error the other way, and it is the one I made first.
    ///</summary>
    [Fact]
    public void A_wire_ending_near_a_bar_is_a_parallel_pair()
    {
        Assert.NotEmpty(DrcEdges.Space(EndOn(), 140, DrcMetric.Projection));
    }

    ///<summary>And two edges that genuinely do run alongside each other are still found.</summary>
    [Fact]
    public void Edges_running_alongside_each_other_are_still_a_pair()
    {
        var alongside = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1100, 0, 2000, 1000)
        };

        var found = DrcEdges.Space(alongside, 140, DrcMetric.Projection);

        Assert.NotEmpty(found);
        Assert.Equal(100, found[0].Distance, 6);
    }

    ///<summary>
    ///A projected pair reports the run over which the two face, not a single point.
    ///
    ///Which is what makes the marker worth drawing: the fault is a stretch of two edges being too close
    ///along their whole length, and a dot in the middle of it says less than the stretch does.
    ///</summary>
    [Fact]
    public void A_projected_pair_covers_the_run_that_faces()
    {
        var alongside = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1100, 200, 2000, 800)
        };

        var pair = Assert.Single(DrcEdges.Space(alongside, 140, DrcMetric.Projection));

        //The overlap is where the second box is, y 200 to 800.
        var box = Bounds.Of(pair.Marker());

        Assert.Equal(200, box.Bottom);
        Assert.Equal(800, box.Top);
    }

    #endregion **************************************************************************



    #region Against the other engine ****************************************************

    ///<summary>
    ///Where the two engines agree, which is nearly everywhere.
    ///
    ///A gap comfortably under the limit is a fault by any measure, and both find it. The point of having
    ///both is not that they differ on the ordinary case - it is that one can express what the other
    ///cannot.
    ///</summary>
    [Fact]
    public void Both_engines_find_an_ordinary_spacing_fault()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1050, 0, 2000, 1000)
        };

        Assert.NotEmpty(DrcChecks.Space(shapes, 140));
        Assert.NotEmpty(DrcEdges.Space(shapes, 140));
    }

    ///<summary>And both leave a layout exactly at the limit alone.</summary>
    [Fact]
    public void Both_engines_pass_a_layout_exactly_at_the_limit()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1140, 0, 2000, 1000)
        };

        Assert.Empty(DrcChecks.Space(shapes, 140));
        Assert.Empty(DrcEdges.Space(shapes, 140));
    }

    ///<summary>A wedge narrowing to a point, which is the case an angle limit exists to decide.</summary>
    private static List<Element.Point> Spike()
    {
        return new List<Element.Point>
        {
            At(0, 0),
            At(400, 0),
            At(200, 2000),
            At(0, 0)
        };
    }

    ///<summary>
    ///A sharp spike is a width fault, and telling it from a square corner is what the angle limit does.
    ///
    ///**This took two goes.** Excluding every pair of edges that meet stops a plain square reporting four
    ///faults at its own corners - and loses the spike, where the two edges genuinely close to a point and
    ///the material between them genuinely is narrower than any limit. The test is the corner's own interior
    ///angle: a right angle or wider is a corner, anything narrower is a wedge and a wedge is a width.
    ///
    ///Ninety degrees is KLayout's own default for `angle_limit`, and asked the same question KLayout gives
    ///the same two answers - three on this wedge and none on a square.
    ///</summary>
    [Fact]
    public void A_sharp_spike_is_a_width_fault()
    {
        Assert.NotEmpty(DrcChecks.Width(One(Spike()), 140));
        Assert.NotEmpty(DrcEdges.Width(One(Spike()), 140));
    }

    ///<summary>And a square, whose corners are all right angles, is not.</summary>
    [Fact]
    public void A_square_corner_is_not_a_width_fault()
    {
        Assert.Empty(DrcEdges.Width(One(Box(0, 0, 1000, 1000)), 140));
    }

    ///<summary>
    ///The wedge is reported from its point out to where it reaches the limit, not along its whole length.
    ///
    ///Past that distance it is wide enough, and marking the whole of both edges would mark ground that is
    ///not at fault.
    ///</summary>
    [Fact]
    public void A_wedge_is_reported_only_where_it_is_too_narrow()
    {
        var pair = DrcEdges.Width(One(Spike()), 140)[0];

        var box = Bounds.Of(pair.Marker());

        //The wedge is 2000 tall; the part under 140 across is a small piece of it near the tip.
        Assert.True(box.Height < 2000, $"the marker spans {box.Height} of a 2000-tall wedge, so it is not bounded");
    }

    ///<summary>
    ///And reported from the corner the two edges share, rather than from the far end of one of them.
    ///
    ///**The size check above cannot see this.** A marker anchored to the wrong end of an edge is the same
    ///small size, just in the wrong place - so `Height` stays well under 2000 and the test passes while the
    ///fault is marked over ground that is not at fault, which is the exact thing the bound exists to
    ///prevent. Found by inverting the corner in <see cref="DrcEdges"/> and watching the whole suite stay
    ///green.
    ///
    ///**The spike has three wedges, not one.** Its point is 11 degrees and its two base corners are 84,
    ///all under the ninety-degree limit - so the three anchors are the triangle's own three corners, and
    ///each must be marked once. Inverting the corner puts two markers on (0,0) and leaves (400,0) unmarked,
    ///which is what this counts.
    ///
    ///The first two points are asserted to differ for the same reason: taking the wrong end collapses
    ///`AFrom` onto `ATo` at the point, and a marker of no width marks nothing at all.
    ///</summary>
    [Fact]
    public void A_wedge_is_reported_from_the_point_it_closes_to()
    {
        var pairs = DrcEdges.Width(One(Spike()), 140);

        Assert.Equal(3, pairs.Count);

        var anchors = new List<Element.Point>();

        foreach (var pair in pairs)
        {
            var marker = pair.Marker();

            Assert.NotEqual(marker[0], marker[1]);

            anchors.Add(marker[0]);
        }

        //The spike's own corners, which are the three points its wedges close to.
        var corners = new List<Element.Point> { At(0, 0), At(200, 2000), At(400, 0) };

        foreach (var corner in corners)
            Assert.True(anchors.Contains(corner), $"nothing was reported at ({corner.X}, {corner.Y}); the anchors were {Listed(anchors)}");

        Assert.Equal(3, anchors.Distinct().Count());
    }

    ///<summary>Points as "(x, y) (x, y)", for a failure that has to say where things actually landed.</summary>
    private static string Listed(List<Element.Point> points)
    {
        var builder = new StringBuilder();

        foreach (var point in points)
            builder.Append($"({point.X}, {point.Y}) ");

        return builder.ToString().TrimEnd();
    }

    ///<summary>
    ///Which is why a rectilinear layout is where the two engines agree.
    ///
    ///Every corner in it is a right angle, so nothing is lost to the exclusion - and that is what the
    ///KLayout comparisons below are measured on.
    ///</summary>
    [Fact]
    public void Both_engines_agree_on_a_rectilinear_neck()
    {
        //A bar with a narrow waist, all right angles.
        var waisted = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1000, 450, 1200, 550),
            Box(1200, 0, 2200, 1000)
        };

        Assert.NotEmpty(DrcChecks.Width(waisted, 140));
        Assert.NotEmpty(DrcEdges.Width(waisted, 140));
    }

    #endregion **************************************************************************



    #region Against KLayout ************************************************************

    private static List<IReadOnlyList<Element.Point>> ShapesOn(FlattenedLayout layout, LayerKey key)
    {
        return layout.Elements
            .Where(element => element.Text is null && !element.IsOpen && element.Layer.Key.Equals(key))
            .Select(element => (IReadOnlyList<Element.Point>)element.Points)
            .ToList();
    }

    private static string MosfetPath
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample); }
    }

    private static FlattenedLayout Mosfet()
    {
        return GdsFlattener.Flatten(new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample)));
    }

    ///<summary>
    ///**Counts, not merely pass-or-fail** - which is what edge pairs bought.
    ///
    ///The region checks could only ever be compared with KLayout on whether either found anything, because
    ///one region too narrow is any number of edges facing each other. Both engines answer in edge pairs
    ///now, so the numbers themselves can be held against each other, and this is the first test in the
    ///whole feature that does.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void The_count_matches_klayout_at_a_realistic_limit()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        int mine = DrcEdges.Width(ShapesOn(Mosfet(), new LayerKey(66, 20)), 200, DrcMetric.Euclidean).Count;
        int theirs = OasisTestData.RuleViolations(MosfetPath, 66, 20, "width", 200, "Euclidian");

        Assert.Equal(theirs, mine);
    }

    ///<summary>Metal, at a limit a real deck might carry.</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void The_count_matches_klayout_on_another_layer()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        int mine = DrcEdges.Width(ShapesOn(Mosfet(), new LayerKey(68, 20)), 400, DrcMetric.Euclidean).Count;
        int theirs = OasisTestData.RuleViolations(MosfetPath, 68, 20, "width", 400, "Euclidian");

        Assert.Equal(theirs, mine);
    }

    ///<summary>
    ///And the projection metric agrees exactly, which is the one that matters most.
    ///
    ///It is what a rule qualified "parallel edges only" is measured in, and the reason this engine was
    ///written - so agreeing with KLayout on it is the single most valuable number in these tests.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void The_projection_count_matches_klayout()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        int mine = DrcEdges.Width(ShapesOn(Mosfet(), new LayerKey(66, 20)), 2000, DrcMetric.Projection).Count;
        int theirs = OasisTestData.RuleViolations(MosfetPath, 66, 20, "width", 2000, "Projection");

        Assert.Equal(theirs, mine);
    }

    ///<summary>
    ///Where the two still do **not** agree, pinned so it is a known limit rather than a surprise.
    ///
    ///**This engine still reports more pairs than KLayout once the limit is large next to the shape**,
    ///though far fewer than it did. Measured on the bundled transistor's poly:
    ///
    ///<code>
    ///limit       200   300   500   2000
    ///KLayout       1     2     6      7
    ///at first      1     3     9     12
    ///+ occlusion   1     3     8     10
    ///+ dedup       1     2     7      9
    ///</code>
    ///
    ///Two causes were found and fixed. Occlusion - a pair whose ground between it is not what the check is
    ///about. And duplication: two edges meet at every corner and both face whatever is across from it, so a
    ///nearest approach landing on a corner was reported twice.
    ///
    ///**What is left is not a measurement difference but a counting one, and it was read off both engines'
    ///output rather than guessed.** At a limit of 500 KLayout reports the base's bottom edge against one
    ///span covering a step and the stripe edge above it; this reports it against each of those edges
    ///separately. Same ground, same distances, coalesced there and not here. Closing it means merging pairs
    ///that continue each other along a boundary, which is a change to the shape of the answer.
    ///
    ///It does not touch the projection metric, which agrees exactly, and it does not touch a limit anywhere
    ///near a real one: a rule saying poly must be 2 microns wide on a transistor 1.5 microns across is not
    ///a rule.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void The_known_divergence_at_an_absurd_limit()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        int mine = DrcEdges.Width(ShapesOn(Mosfet(), new LayerKey(66, 20)), 2000, DrcMetric.Euclidean).Count;
        int theirs = OasisTestData.RuleViolations(MosfetPath, 66, 20, "width", 2000, "Euclidian");

        //Both find the layer too narrow; this one counts more pairs on the way.
        Assert.True(theirs > 0 && mine > 0, "neither engine found anything, so this pins nothing");
        Assert.True(mine > theirs, $"this engine reported {mine} against KLayout's {theirs}, so the known over-report has changed");
    }

    #endregion **************************************************************************
}
