using System.Text;
using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Layouts the edge engine reported faults on that are not there, each pinned with the shapes that showed it.
///
///**Every one of them found on IHP's SG13G2, and every one passed by KLayout and by the sizing engine.** For a
///width the sizing engine's opening finds everything two facing edges can and more, so an edge-engine fault on
///ground the sizing engine passes is the engine's own. With `euclidean` on every width and space rule of the
///sg13g2 deck, twenty-nine of IHP's 84 standard cells reported something; KLayout, asked the same 44 rules,
///reports nothing in any of them, and neither does the engine now.
///
///**Three ways it went wrong**, a region each below. A merge leaves a straight side in pieces wherever a corner
///of a shape that went into it lands on the side, and a keyhole as a slit where a hole's channel runs along
///another hole's edge - see DrcEdges.straightened. Two edges that do not meet were paired whatever angle they
///made, so the sides of a corner with a step beside it were measured along the boundary as a width - see
///DrcEdges.faces. And the ground between two edges was tested at one point that was not on the line they were
///measured across - see DrcEdges.Sees.
///
///Each fixture's numbers were taken from the layout it came from, with KLayout's own count beside them.
///</summary>
public class DrcEdgeFalseFaultTests
{
    #region Shapes to work with ******************************************************

    private static Element.Point At(int x, int y)
    {
        return new Element.Point { X = x, Y = y };
    }

    ///<summary>A closed box, the way a GDSII boundary writes one - first point repeated at the end.</summary>
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

    ///<summary>A closed outline from x, y pairs, the first corner repeated at the end.</summary>
    private static List<Element.Point> Outline(params int[] xy)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < xy.Length; i += 2)
            points.Add(At(xy[i], xy[i + 1]));

        points.Add(points[0]);

        return points;
    }

    private static FlattenedLayout LayoutOf(LayerKey key, IEnumerable<List<Element.Point>> shapes)
    {
        var elements = new List<Element>();

        foreach (var shape in shapes)
            elements.Add(new Element { Layer = new Layer(key, "#000000"), Points = shape });

        return new FlattenedLayout { Elements = elements };
    }

    ///<summary>One rule over one layer of shapes, through a deck, the way the app and the CLI run it.</summary>
    private static DrcResult Check(LayerKey key, IEnumerable<List<Element.Point>> shapes, string rule)
    {
        string deck = $"layer L {key.Number}/{key.DataType}\n{rule}";

        return Drc.Check(DrcDeck.Parse(deck), LayoutOf(key, shapes));
    }

    ///<summary>What a run found, as "rule (x, y) (x, y) ..." per violation, for a failure that has to say where.</summary>
    private static string Listed(DrcResult result)
    {
        var builder = new StringBuilder();

        foreach (var violation in result.Violations)
        {
            builder.Append(violation.RuleId);

            foreach (var point in violation.Marker)
                builder.Append($" ({point.X}, {point.Y})");

            builder.Append("; ");
        }

        return builder.ToString();
    }

    private static void AssertClean(DrcResult result)
    {
        Assert.True(result.Complete, "a rule did not run: " + string.Join(" ", result.NotRun) + string.Join(" ", result.Problems));
        Assert.True(result.Violations.Count == 0, $"{result.Violations.Count} violation(s) where there are none: {Listed(result)}");
    }

    #endregion **************************************************************************



    #region A straight side in pieces ***************************************************

    private static readonly LayerKey NWell = new LayerKey(31, 0);

    ///<summary>
    ///sg13g2_and2_2's NWell: three rectangles, in nanometers, whose union is one rectangle 3360 by 2420.
    ///
    ///Merged, it comes back with corners at 2160 and 2640 along its top and 2640 along its bottom, where the
    ///corners of the rectangles that went into it land. The top piece from 2640 to 2160 no longer shares a corner
    ///with the right side, and the two were measured against each other along the top, 480 nm, as a width.
    ///</summary>
    private static List<List<Element.Point>> ThreeWells()
    {
        return new List<List<Element.Point>>
        {
            Box(-240, 1750, 720, 4170),
            Box(2160, 1750, 3120, 4170),
            Box(-240, 1750, 2640, 4170)
        };
    }

    ///<summary>
    ///The reproduction as it was filed: NW.a, 620 nm, reported twice measured Euclidean, at 2640,4170 to
    ///3120,4170 and the same along the bottom. KLayout and the sizing engine report nothing.
    ///</summary>
    [Fact]
    public void Three_rectangles_that_make_one_are_as_wide_as_the_one()
    {
        AssertClean(Check(NWell, ThreeWells(), "rule NW.a width L 620 euclidean \"x\""));
        AssertClean(Check(NWell, ThreeWells(), "rule NW.a width L 620 parallel \"x\""));
        AssertClean(Check(NWell, ThreeWells(), "rule NW.b space L 620 euclidean \"x\""));
    }

    ///<summary>
    ///And the one they make is measured as one: past its own height it is too narrow, once, top against bottom.
    ///
    ///**Once, and that pins the joining.** With its top and bottom in pieces it was too narrow between every piece
    ///of one and every piece of the other.
    ///</summary>
    [Fact]
    public void The_one_they_make_is_measured_as_one()
    {
        foreach (var metric in new[] { DrcMetric.Euclidean, DrcMetric.Projection })
        {
            var pair = Assert.Single(DrcEdges.Width(ThreeWells(), 2500, metric));

            Assert.Equal(2420, pair.Distance, 6);
        }
    }

    #endregion **************************************************************************



    #region A corner with something between its sides ***********************************

    ///<summary>
    ///Two rectangles overlapping with tops 10 nm apart, so a step stands beside the corner: the top and the side
    ///across from it no longer meet, and at right angles they were measured 480 nm apart along the boundary.
    ///
    ///KLayout holds every pair to its angle limit, not only the two edges of a corner, and reports nothing.
    ///</summary>
    [Fact]
    public void A_step_beside_a_corner_is_not_a_width()
    {
        var stepped = new List<List<Element.Point>>
        {
            Box(-240, 1750, 2640, 4170),
            Box(2160, 1750, 3120, 4160)
        };

        AssertClean(Check(NWell, stepped, "rule NW.a width L 620 euclidean \"x\""));
    }

    ///<summary>
    ///The inside of an L, with one of the corner's sides cut by a third rectangle's corner, and then with a step
    ///instead: the spacing check's version of the two above. Both reported 200 nm across the corner; KLayout
    ///reports nothing.
    ///</summary>
    [Fact]
    public void An_inside_corner_cut_or_stepped_is_not_a_spacing()
    {
        var metal1 = new LayerKey(8, 0);

        var cut = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 300),
            Box(0, 0, 300, 1000),
            Box(0, 0, 500, 300)
        };

        var stepped = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 300),
            Box(0, 0, 300, 1000),
            Box(0, 0, 500, 310)
        };

        AssertClean(Check(metal1, cut, "rule M1.b space L 250 euclidean \"x\""));
        AssertClean(Check(metal1, stepped, "rule M1.b space L 250 euclidean \"x\""));
    }

    ///<summary>
    ///sg13g2_tielo's poly, which turns its two pieces at 45 degrees in stairs of 5 nm steps.
    ///
    ///**A stair is a step beside a corner at every tread.** Measured Euclidean, Gat.a's 130 nm found 38 widths in
    ///it and Gat.b's 180 found 33 spacings, every one between two edges at right angles: a tread against a riser
    ///one to seven steps along, or a riser against the side the stair runs into. KLayout finds none, and the
    ///pieces are 182 nm apart, which Gat.b allows.
    ///</summary>
    [Fact]
    public void A_stair_of_five_nanometer_steps_is_neither_a_width_nor_a_spacing()
    {
        var poly = new List<List<Element.Point>>
        {
            Outline(1600, 1740, 1335, 1740, 1335, 1735, 1330, 1735, 1330, 1730, 1325, 1730, 1325, 1725, 1320, 1725,
                1320, 1720, 1315, 1720, 1315, 1715, 1310, 1715, 1310, 1710, 1305, 1710, 1305, 1705, 1300, 1705,
                1300, 1570, 1115, 1570, 1115, 290, 1245, 290, 1245, 1440, 1600, 1440),
            Outline(1245, 3490, 1115, 3490, 1115, 2080, 855, 2080, 855, 1780, 1120, 1780, 1120, 1785, 1125, 1785,
                1125, 1790, 1130, 1790, 1130, 1795, 1135, 1795, 1135, 1800, 1140, 1800, 1140, 1805, 1145, 1805,
                1145, 1810, 1150, 1810, 1150, 1815, 1155, 1815, 1155, 1935, 1245, 1935),
            Outline(765, 1570, 675, 1570, 675, 2260, 765, 2260, 765, 2960, 635, 2960, 635, 2390, 295, 2390,
                295, 2090, 545, 2090, 545, 1440, 635, 1440, 635, 785, 765, 785)
        };

        var gatPoly = new LayerKey(5, 0);

        AssertClean(Check(gatPoly, poly, "rule Gat.a width L 130 euclidean \"x\""));
        AssertClean(Check(gatPoly, poly, "rule Gat.b space L 180 euclidean \"x\""));
    }

    ///<summary>
    ///Two edges at right angles are no pair even where the line between them stays in the metal.
    ///
    ///**The case the line through the ground does not catch.** The bundled transistor's poly is a stripe
    ///standing on a base, and the stripe's side runs down to where the base begins - straight above the base's
    ///bottom, 460 nm below, with nothing but poly between. KLayout reports six pairs at 500 nm and that is not one
    ///of them: the two edges are at ninety degrees. Measured only at the edges that meet, it was a seventh.
    ///</summary>
    [Fact]
    public void A_side_and_the_bottom_below_it_are_not_a_pair()
    {
        var transistor = new List<List<Element.Point>>
        {
            Box(-50, 460, 100, 1500),
            Box(-300, 0, 150, 460)
        };

        var found = DrcEdges.Width(transistor, 500, DrcMetric.Euclidean).Select(pair => pair.Distance).OrderBy(distance => distance).ToList();

        Assert.Equal(new double[] { 150, 200, 400, 450, 460, 460 }, found);
    }

    #endregion **************************************************************************



    #region A keyhole left as a slit ****************************************************

    private static readonly LayerKey Metal3 = new LayerKey(30, 0);

    ///<summary>
    ///The Metal3 of the core of IHP's 60 GHz power amplifier (TO_July2025, 50_ghz_mpa_core.gds), flattened: a
    ///frame of overlapping rectangles around two holes whose tops are both at -29170, and three islands.
    ///
    ///Its layer is folded into keyholes on its way to a check, and the channel cut to the left hole runs along
    ///the right hole's top. Merged again, 14.5 µm of channel stayed in the outline as a slit, metal on both sides,
    ///and its end was a wedge closing to a point - an M3.a width fault inside a bar 1950 nm wide.
    ///</summary>
    private static List<List<Element.Point>> Frame()
    {
        return new List<List<Element.Point>>
        {
            Box(-2305, -17185, 2305, 17185),
            Box(-27885, -59940, -23275, -25570),
            Box(-57655, -63995, -55705, -21465),
            Box(-19205, -52025, -15345, -33425),
            Box(5025, -63995, 6975, -21465),
            Box(-43185, -63995, -7495, -56290),
            Box(-35335, -51980, -31475, -33380),
            Box(-43185, -29170, -7495, -21465),
            Box(-55705, -63995, -43185, -21465),
            Box(-7495, -63995, 5025, -21465)
        };
    }

    ///<summary>The reproduction as it was filed, M3.a at 200 nm, under both metrics and as a spacing too.</summary>
    [Fact]
    public void A_frame_whose_holes_line_up_is_as_wide_as_its_bars()
    {
        AssertClean(Check(Metal3, Frame(), "rule M3.a width L 200 parallel \"x\""));
        AssertClean(Check(Metal3, Frame(), "rule M3.a width L 200 euclidean \"x\""));
        AssertClean(Check(Metal3, Frame(), "rule M3.b space L 200 euclidean \"x\""));
    }

    ///<summary>
    ///And handed the keyholed outline directly, which is how a GDSII file draws a hole, the walk measures the shape
    ///it draws rather than the cut - whatever the layers it is given were merged by.
    ///</summary>
    [Fact]
    public void A_keyholed_outline_is_measured_as_the_shape_it_draws()
    {
        var keyholed = Booleans.Merge(Frame());

        foreach (var metric in new[] { DrcMetric.Euclidean, DrcMetric.Projection })
            Assert.Empty(DrcEdges.Width(keyholed, 200, metric));
    }

    ///<summary>
    ///And the frame is still measured: its narrowest bar is the 4610 nm one between the holes - the 1950 nm
    ///rectangles merge into the metal beside them - and a limit past that finds it there.
    ///</summary>
    [Fact]
    public void The_bar_between_the_holes_is_still_measured()
    {
        var result = Check(Metal3, Frame(), "rule M3.a width L 5000 parallel \"x\"");

        Assert.Contains(result.Violations, violation =>
        {
            var box = Bounds.Of(violation.Marker);

            return box.Left == -27885 && box.Right == -23275;
        });
    }

    #endregion **************************************************************************



    #region The ground between two edges ************************************************

    ///<summary>
    ///One of sg13g2_a22oi_1's poly pieces, which jogs right on its way up: corners at 635,1490 and 700,1630 lie
    ///diagonally across the jog, and the sides running away from them face each other.
    ///
    ///**Measured 154 nm corner to corner, straight through the jog's own poly**, while the point that was tested
    ///- halfway between the two sides' middles - sat in the empty ground beside it. A Gat.b fault where KLayout
    ///reports none; forty-two like it in twenty-two of IHP's cells.
    ///</summary>
    [Fact]
    public void Two_corners_across_a_jog_are_not_a_spacing()
    {
        var jog = new List<List<Element.Point>>
        {
            Outline(1250, 3480, 1120, 3480, 1120, 2090, 950, 2090, 950, 1750, 700, 1750, 700, 1630, 505, 1630,
                505, 300, 635, 300, 635, 1490, 830, 1490, 830, 1610, 1250, 1610)
        };

        AssertClean(Check(new LayerKey(5, 0), jog, "rule Gat.b space L 180 euclidean \"x\""));
    }

    ///<summary>
    ///A thin shape standing between two others: each is a spacing fault against it, and the two outer ones are not
    ///one against each other - the wall is between them. KLayout reports the two, 20 and 60 apart.
    ///
    ///**The case that took the line across the middle of the run.** Two edges alongside each other are nearest all
    ///along it, and the line drawn at one end ran along the wall's foot, touching it and never crossing.
    ///</summary>
    [Fact]
    public void A_shape_between_two_others_stands_between_them()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1020, 0, 1040, 1000),
            Box(1100, 0, 2000, 1000)
        };

        foreach (var metric in new[] { DrcMetric.Euclidean, DrcMetric.Projection })
        {
            var found = DrcEdges.Space(shapes, 140, metric).Select(pair => pair.Distance).OrderBy(distance => distance).ToList();

            Assert.Equal(new double[] { 20, 60 }, found);
        }
    }

    ///<summary>
    ///And a wire at 45 degrees that is too narrow is still too narrow, though the line across it ends a hair
    ///outside it.
    ///
    ///**The nearest point on a slanted edge is rarely on the grid.** Here it is 914.5,1085.5, rounded to
    ///914,1086, which is 0.7 nm beyond the wire's far side - so the line it was measured along pokes out of the
    ///metal at its very end, and a test of every stretch of that line would throw the pair away. A stretch under a
    ///database unit is not ground. KLayout reports three, this pair and the two acute corners of the skewed ends.
    ///</summary>
    [Fact]
    public void A_narrow_wire_at_45_degrees_is_still_too_narrow()
    {
        var wire = new List<List<Element.Point>>
        {
            Outline(0, 0, 1000, 1000, 950, 1121, -50, 121)
        };

        foreach (var metric in new[] { DrcMetric.Euclidean, DrcMetric.Projection })
        {
            var found = DrcEdges.Width(wire, 130, metric);

            Assert.Equal(3, found.Count);
            Assert.Contains(found, pair => Math.Abs(pair.Distance - (171 / Math.Sqrt(2))) < 1);
        }
    }

    #endregion **************************************************************************
}
