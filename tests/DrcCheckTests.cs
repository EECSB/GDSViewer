using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The geometric checks, and mostly the boundary between passing and failing.
///
///**Exactly at the limit is the case that matters.** Drawing to minimum is what layout is, so a checker
///that reports every minimum-width wire is worse than none - and the whole of the radius arithmetic in
///DrcChecks exists to guarantee that a shape exactly at the limit comes back clean. Every check here is
///tested at the limit, one manufacturing grid step under it, and where the documented gap is, at the one
///database unit that falls through.
///
///Areas rather than corner lists, for the reason BooleanTests gives: a boolean's rings depend on how the
///clipper walked the input, where the area is the thing being asked about.
///</summary>
public class DrcCheckTests
{
    #region Shapes to work with ******************************************************

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

    private static double Area(IEnumerable<IReadOnlyList<Element.Point>> shapes)
    {
        double total = 0;

        foreach (var shape in shapes)
            total += Measure.AreaOf(shape);

        return total;
    }

    ///<summary>A sky130 metal minimum, and the grid its layouts are snapped to.</summary>
    private const long Limit = 140;
    private const int GridStep = 5;

    #endregion **************************************************************************



    #region Width ***********************************************************************

    [Fact]
    public void A_shape_exactly_at_the_minimum_width_passes()
    {
        var box = One(Box(0, 0, (int)Limit, 1000));

        Assert.Empty(DrcChecks.Width(box, Limit));
    }

    [Fact]
    public void A_shape_one_grid_step_under_the_minimum_is_reported()
    {
        var box = One(Box(0, 0, (int)Limit - GridStep, 1000));

        Assert.NotEmpty(DrcChecks.Width(box, Limit));
    }

    ///<summary>Wide everywhere but one neck, and the neck is what comes back.</summary>
    [Fact]
    public void Only_the_narrow_part_of_a_shape_is_reported()
    {
        var wide = Box(0, 0, 1000, 1000);
        var neck = Box(1000, 400, 1400, 400 + 100);

        var violations = DrcChecks.Width(new List<List<Element.Point>> { wide, neck }, Limit);

        Assert.NotEmpty(violations);

        //The neck is 400 by 100; the body is far wider than the limit and takes no part.
        Assert.True(Area(violations) < 400 * 100 * 1.5, $"Expected about the neck, got {Area(violations)}.");
    }

    ///<summary>
    ///The documented gap, pinned rather than left to be discovered.
    ///
    ///An even limit cannot be halved onto an integer grid in a way that both passes the limit and catches
    ///one below it, so a width of exactly limit - 1 falls through. It is unreachable on a layout snapped to
    ///a manufacturing grid - 139 is not a multiple of 5 - which is what makes the choice acceptable, and
    ///this is here so that the day it stops being true, something says so.
    ///</summary>
    [Fact]
    public void One_database_unit_under_an_even_limit_is_the_known_miss()
    {
        var box = One(Box(0, 0, (int)Limit - 1, 1000));

        Assert.Empty(DrcChecks.Width(box, Limit));
    }

    ///<summary>
    ///A regular octagon 400 across, which is the shape that exposed the worst bug in this engine.
    ///
    ///What the layout generator draws when asked for eight corners, and therefore what a generated layout
    ///is made of.
    ///</summary>
    private static List<Element.Point> Octagon()
    {
        return new List<Element.Point>
        {
            At(400, 200),
            At(341, 341),
            At(200, 400),
            At(59, 341),
            At(0, 200),
            At(59, 59),
            At(200, 0),
            At(341, 59)
        };
    }

    ///<summary>
    ///A convex octagon is not too narrow, and for a long time this engine said it was.
    ///
    ///**Shrinking and growing by the same amount does not return the same shape.** Clipper works in
    ///integers and a mitered offset of a 45-degree edge lands between them, so the corner rounds inward on
    ///the way in and inward again on the way out. This octagon, shrunk by 24 and grown back by 24, came
    ///back 359 square units smaller - a ring about a third of a unit thick all the way round - and
    ///`merged NOT opened` reported that ring as a width violation.
    ///
    ///It is invisible on rectilinear geometry, which is why sky130 layouts never showed it. On a generated
    ///layout of 320,000 octagons it was **188,742 violations, every one of them false**. KLayout says there
    ///is no violation here, and so does this project's own edge engine; the sizing engine was the one that
    ///was wrong.
    ///</summary>
    [Fact]
    public void A_convex_octagon_is_not_too_narrow()
    {
        //Its narrowest crossing is about 370, so nothing about it is near 50.
        Assert.Empty(DrcChecks.Width(One(Octagon()), 50));
    }

    ///<summary>And the same shape by the other engine, which always said so.</summary>
    [Fact]
    public void Both_engines_leave_the_octagon_alone()
    {
        Assert.Empty(DrcChecks.Width(One(Octagon()), 50));
        Assert.Empty(DrcEdges.Width(One(Octagon()), 50));
    }

    ///<summary>
    ///The control that says why it went unnoticed: a square loses nothing at all to the same round trip.
    ///
    ///Every offset of a right angle lands on an integer, so there is no rounding to accumulate - and real
    ///layout is very nearly all right angles.
    ///</summary>
    [Fact]
    public void A_square_survives_the_round_trip_exactly()
    {
        var square = Booleans.Merge(One(Box(0, 0, 400, 400)));

        var opened = Booleans.Grow(Booleans.Grow(square, -24), 24);

        Assert.Equal(Area(square), Area(opened), 3);
    }

    ///<summary>
    ///And the octagon does not, which is the fault itself rather than its symptom.
    ///
    ///Pinned because the fix is a compensation rather than a cure: the round trip still loses the ring, and
    ///what changed is that the check grows back one unit further and clips to the shape. If Clipper ever
    ///offsets exactly, this test says so.
    ///</summary>
    [Fact]
    public void The_octagon_does_not_survive_it_and_that_is_the_fault()
    {
        var octagon = Booleans.Merge(One(Octagon()));

        var opened = Booleans.Grow(Booleans.Grow(octagon, -24), 24);

        Assert.True(
            Area(opened) < Area(octagon),
            "the round trip is exact now, so the compensation in Width and Space is no longer needed");
    }

    ///<summary>An odd limit halves exactly, so there is no gap at all.</summary>
    [Fact]
    public void An_odd_limit_catches_everything_below_it()
    {
        var box = One(Box(0, 0, 74, 1000));

        Assert.NotEmpty(DrcChecks.Width(box, 75));
    }

    [Fact]
    public void An_odd_limit_still_passes_at_the_limit()
    {
        var box = One(Box(0, 0, 75, 1000));

        Assert.Empty(DrcChecks.Width(box, 75));
    }

    #endregion **************************************************************************



    #region Spacing on one layer ********************************************************

    [Fact]
    public void Two_shapes_exactly_the_minimum_apart_pass()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1000 + (int)Limit, 0, 2000, 1000)
        };

        Assert.Empty(DrcChecks.Space(shapes, Limit));
    }

    [Fact]
    public void Two_shapes_one_grid_step_closer_are_reported()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 1000),
            Box(1000 + (int)Limit - GridStep, 0, 2000, 1000)
        };

        Assert.NotEmpty(DrcChecks.Space(shapes, Limit));
    }

    #endregion **************************************************************************



    #region Spacing between two layers **************************************************

    [Fact]
    public void Two_layers_exactly_the_minimum_apart_pass()
    {
        var one = One(Box(0, 0, 1000, 1000));
        var other = One(Box(1000 + (int)Limit, 0, 2000, 1000));

        Assert.Empty(DrcChecks.Space(one, other, Limit));
    }

    [Fact]
    public void Two_layers_one_grid_step_closer_are_reported()
    {
        var one = One(Box(0, 0, 1000, 1000));
        var other = One(Box(1000 + (int)Limit - GridStep, 0, 2000, 1000));

        Assert.NotEmpty(DrcChecks.Space(one, other, Limit));
    }

    ///<summary>
    ///The reason the two-layer check measures the ground outside both rather than growing one into the
    ///other.
    ///
    ///An implant covering a diffusion is the ordinary state of a layout, not a spacing fault. Growing the
    ///first layer and intersecting the second reports the whole ring inside the implant around the
    ///diffusion it deliberately covers - which is a rule about how close two things come, answered with the
    ///place they are on top of each other.
    ///</summary>
    [Fact]
    public void A_layer_lying_inside_another_is_not_a_spacing_violation()
    {
        var implant = One(Box(0, 0, 1000, 1000));
        var inside = One(Box(200, 200, 800, 800));

        Assert.Empty(DrcChecks.Space(implant, inside, Limit));
    }

    ///<summary>And one reaching almost to the edge of the other still is one.</summary>
    [Fact]
    public void A_layer_ending_just_short_of_another_is_still_reported()
    {
        var one = One(Box(0, 0, 1000, 1000));
        var other = One(Box(1000 + GridStep, 0, 2000, 1000));

        Assert.NotEmpty(DrcChecks.Space(one, other, Limit));
    }

    #endregion **************************************************************************



    #region Notches *********************************************************************

    ///<summary>A U, with a hundred units between its arms.</summary>
    private static List<Element.Point> UShape()
    {
        return new List<Element.Point>
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
    }

    [Fact]
    public void The_gap_inside_one_shape_is_a_notch()
    {
        Assert.NotEmpty(DrcChecks.Notch(One(UShape()), Limit));
    }

    ///<summary>
    ///Two separate shapes too close together are a spacing fault and not a notch, and the two are told
    ///apart by how many pieces the gap between them touches.
    ///</summary>
    [Fact]
    public void A_gap_between_two_shapes_is_not_a_notch()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 100, 300),
            Box(200, 0, 300, 300)
        };

        Assert.Empty(DrcChecks.Notch(shapes, Limit));
        Assert.NotEmpty(DrcChecks.Space(shapes, Limit));
    }

    ///<summary>
    ///A spacing check finds a notch as well, which is what lets a deck leave the notch rule out.
    ///
    ///**sky130 states this as a rule of its own**: x.6, "all intra-layer separation checks will include a
    ///notch check". It carries no value because it is not a separate measurement - it says that every
    ///spacing limit already written applies inside a shape as well as between two. A closing fills both
    ///kinds of gap, so `space` satisfies it without the deck saying anything.
    ///
    ///<see cref="DrcChecks.Notch(IEnumerable{IReadOnlyList{Element.Point}}, long)"/> exists for a deck that
    ///wants the two apart - to give an internal gap a different limit from an external one, which some
    ///processes do. No rule in the bundled deck does, and that is why none uses it.
    ///</summary>
    [Fact]
    public void A_spacing_check_finds_a_notch_too()
    {
        var gaps = DrcChecks.Space(One(UShape()), Limit);

        Assert.NotEmpty(gaps);

        //The same gap the notch check finds, since there is only one on this shape.
        Assert.Equal(Area(DrcChecks.Notch(One(UShape()), Limit)), Area(gaps));
    }

    ///<summary>
    ///The notch check over a real sky130 standard cell, rather than only over a shape built to have one.
    ///
    ///**Because a hand-made U proves the arithmetic and nothing about the cost.** Telling a notch from a
    ///gap means growing each gap and intersecting it against every merged piece, which is a boolean per gap
    ///per piece - fine on one shape with one notch, and the kind of thing that only shows its shape on
    ///geometry somebody else drew. This runs it on a metal layer of a real cell and asserts the answer
    ///agrees with the spacing check it is a subset of.
    ///</summary>
    [Fact]
    public void The_notch_check_runs_over_a_real_cell()
    {
        string path = Path.Combine(
            GdsTestData.RepositoryRoot,
            "OtherResources",
            "Sky130",
            "GDS",
            "Sky130 GDS",
            "sky130_fd_sc_hd__a2111o_1.gds");

        Assert.True(File.Exists(path), $"The cell this runs over is missing: {path}");

        var layout = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(path)));

        var met1 = layout.Elements
            .Where(element => element.Text is null && !element.IsOpen && element.Layer.Key.Equals(new LayerKey(68, 20)))
            .Select(element => (IReadOnlyList<Element.Point>)element.Points)
            .ToList();

        Assert.NotEmpty(met1);

        //A limit far above anything the cell satisfies, so both checks have real work to do.
        var notches = DrcChecks.Notch(met1, 400);
        var gaps = DrcChecks.Space(met1, 400);

        //Every notch is a gap, so the notch check can never find more than the spacing one.
        Assert.True(
            notches.Count <= gaps.Count,
            $"the notch check found {notches.Count} where spacing found {gaps.Count}, and a notch is a kind of gap");
    }

    #endregion **************************************************************************



    #region Enclosure and extension *****************************************************

    ///<summary>Only one layer is grown, so there is no half distance and no rounding: this one is exact.</summary>
    [Fact]
    public void An_enclosure_exactly_at_the_limit_passes()
    {
        var inner = One(Box(100, 100, 200, 200));
        var outer = One(Box(0, 0, 300, 300));

        Assert.Empty(DrcChecks.Enclosure(inner, outer, 100));
    }

    [Fact]
    public void An_enclosure_one_unit_short_is_reported()
    {
        var inner = One(Box(100, 100, 200, 200));
        var outer = One(Box(1, 1, 299, 299));

        Assert.NotEmpty(DrcChecks.Enclosure(inner, outer, 100));
    }

    ///<summary>
    ///Enclosure is omnidirectional, which is the whole of what a region-based check can be - and the reason
    ///there is no extension check beside it.
    ///
    ///A gate sits inside poly and is enclosed on its two sides and not at its two ends, which is exactly
    ///what a correct transistor looks like. Measured in every direction that is a violation, and it is not
    ///one. An endcap rule means the ends; this cannot tell an end from a side, so `extension` is refused at
    ///the door rather than answered wrongly.
    ///</summary>
    [Fact]
    public void Enclosure_cannot_tell_an_end_from_a_side()
    {
        //A gate: the full width of the poly stripe, stopping short of its two ends.
        var gate = One(Box(0, 100, 100, 400));
        var poly = One(Box(0, 0, 100, 500));

        //Enclosed by 100 at top and bottom, and by nothing at all on the left and right.
        Assert.NotEmpty(DrcChecks.Enclosure(gate, poly, 100));
    }

    #endregion **************************************************************************



    #region Area ************************************************************************

    [Fact]
    public void A_shape_exactly_at_the_minimum_area_passes()
    {
        Assert.Empty(DrcChecks.Area(One(Box(0, 0, 100, 100)), 10000));
    }

    [Fact]
    public void A_shape_under_the_minimum_area_is_reported()
    {
        Assert.NotEmpty(DrcChecks.Area(One(Box(0, 0, 100, 100)), 10001));
    }

    ///<summary>Merged first, or two overlapping shapes are each measured as though the other were not there.</summary>
    [Fact]
    public void Overlapping_shapes_are_measured_as_the_one_they_cover()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 100, 100),
            Box(50, 0, 150, 100)
        };

        //15000 covered between them, so a limit of 15000 passes where either alone would fail it.
        Assert.Empty(DrcChecks.Area(shapes, 15000));
    }

    ///<summary>Four bars round a square hole of a hundred by a hundred.</summary>
    private static List<List<Element.Point>> RingWithHole()
    {
        return new List<List<Element.Point>>
        {
            Box(0, 0, 1000, 400),
            Box(0, 500, 1000, 1000),
            Box(0, 0, 400, 1000),
            Box(500, 0, 1000, 1000)
        };
    }

    [Fact]
    public void A_hole_under_the_minimum_area_is_reported()
    {
        var small = DrcChecks.HoleArea(RingWithHole(), 10001);

        Assert.Single(small);
        Assert.Equal(10000, Area(small));
    }

    [Fact]
    public void A_hole_exactly_at_the_minimum_area_passes()
    {
        Assert.Empty(DrcChecks.HoleArea(RingWithHole(), 10000));
    }

    ///<summary>A hole is not a shape: the area check and the hole check answer about different things.</summary>
    [Fact]
    public void The_area_check_does_not_report_a_hole()
    {
        Assert.Empty(DrcChecks.Area(RingWithHole(), 10001));
    }

    #endregion **************************************************************************



    #region Off the grid ****************************************************************

    private static Element Shape(List<Element.Point> points)
    {
        return new Element { Layer = new Layer(new LayerKey(68, 20), "#000000"), Points = points };
    }

    [Fact]
    public void Coordinates_on_the_grid_are_not_reported()
    {
        var elements = new List<Element> { Shape(Box(0, 0, 100, 200)) };

        Assert.Empty(DrcChecks.OffGrid(elements, GridStep));
    }

    [Fact]
    public void A_coordinate_off_the_grid_is_reported()
    {
        var elements = new List<Element> { Shape(Box(0, 0, 103, 200)) };

        Assert.NotEmpty(DrcChecks.OffGrid(elements, GridStep));
    }

    ///<summary>A grid of one is every whole coordinate there can be, so nothing is off it.</summary>
    [Fact]
    public void Nothing_is_off_a_grid_of_one()
    {
        var elements = new List<Element> { Shape(Box(0, 0, 103, 207)) };

        Assert.Empty(DrcChecks.OffGrid(elements, 1));
    }

    #endregion **************************************************************************



    #region Exemptions ******************************************************************

    [Fact]
    public void A_violation_inside_the_exempt_region_is_taken_off()
    {
        var violations = One(Box(0, 0, 100, 100));
        var exempt = One(Box(0, 0, 200, 200));

        Assert.Empty(DrcChecks.Outside(violations, exempt));
    }

    [Fact]
    public void A_violation_outside_the_exempt_region_stays()
    {
        var violations = One(Box(0, 0, 100, 100));
        var exempt = One(Box(500, 500, 600, 600));

        Assert.Equal(10000, Area(DrcChecks.Outside(violations, exempt)));
    }

    #endregion **************************************************************************
}
