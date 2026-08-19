using GdsII;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///Covers Transform directly, which nothing did before - it was only ever exercised through the flattener,
///where a composition order that is backwards shows up as geometry in the wrong place and has to be
///reasoned back to its cause.
///
///The things worth pinning here are the two orders: the order GDSII applies a placement's parts in
///(reflect, magnify, rotate, translate), and the order Then composes in. Both are the kind of thing that
///looks right either way round until a case that distinguishes them is written down.
///</summary>
public class TransformTests
{
    private const double Tolerance = 1e-9;

    private static void AssertPoint(int expectedX, int expectedY, Element.Point actual)
    {
        Assert.Equal(expectedX, actual.X);
        Assert.Equal(expectedY, actual.Y);
    }

    #region The identity and translation ***********************************************

    [Fact]
    public void The_identity_leaves_a_point_where_it_is()
    {
        AssertPoint(37, -42, Transform.Identity.Apply(37, -42));
    }

    [Fact]
    public void A_translation_moves_a_point_without_turning_it()
    {
        var transform = Transform.ForTranslation(100, -50);

        AssertPoint(110, -40, transform.Apply(10, 10));
        Assert.Equal(1, transform.Scale, Tolerance);
        Assert.Equal(0, transform.AngleInDegrees, Tolerance);
    }

    #endregion ************************************************************************



    #region A placement's parts ********************************************************

    [Theory]
    [InlineData(90, 0, 1000)]
    [InlineData(180, -1000, 0)]
    [InlineData(270, 0, -1000)]
    [InlineData(360, 1000, 0)]
    public void Rotation_is_counterclockwise(double angle, int expectedX, int expectedY)
    {
        var transform = Transform.ForPlacement(false, 1, angle, 0, 0);

        AssertPoint(expectedX, expectedY, transform.Apply(1000, 0));
    }

    [Fact]
    public void Reflection_is_about_the_x_axis_so_it_negates_y()
    {
        var transform = Transform.ForPlacement(true, 1, 0, 0, 0);

        AssertPoint(700, -300, transform.Apply(700, 300));
    }

    [Fact]
    public void Magnification_scales_both_axes()
    {
        var transform = Transform.ForPlacement(false, 2.5, 0, 0, 0);

        AssertPoint(250, -500, transform.Apply(100, -200));
    }

    ///<summary>
    ///The order is what this is for. Reflect about X then rotate 90 counterclockwise sends (1000, 0) to
    ///(0, 1000); rotating first and then reflecting would send it to (0, -1000). The two disagree, which
    ///is what makes the case worth writing.
    ///</summary>
    [Fact]
    public void Reflection_happens_before_rotation_not_after()
    {
        var transform = Transform.ForPlacement(true, 1, 90, 0, 0);

        AssertPoint(0, 1000, transform.Apply(1000, 0));
        AssertPoint(1000, 0, transform.Apply(0, 1000));
    }

    ///<summary>And the translation is applied last, so it is not scaled or turned by the rest.</summary>
    [Fact]
    public void The_reference_point_is_added_after_everything_else()
    {
        var transform = Transform.ForPlacement(false, 3, 90, 500, 500);

        //(100, 0) magnified to (300, 0), turned to (0, 300), then moved by (500, 500).
        AssertPoint(500, 800, transform.Apply(100, 0));
    }

    #endregion ************************************************************************



    #region Composition ***************************************************************

    ///<summary>
    ///a.Then(b) means a first, then b. Translating and then rotating is not the same as rotating and then
    ///translating, so this fails if the composition is the wrong way round.
    ///</summary>
    [Fact]
    public void Then_applies_this_transform_first_and_the_argument_second()
    {
        var move = Transform.ForTranslation(1000, 0);
        var quarterTurn = Transform.ForPlacement(false, 1, 90, 0, 0);

        //Moved to (1000, 0) and then turned about the origin, ending up on the y axis.
        AssertPoint(0, 1000, move.Then(quarterTurn).Apply(0, 0));

        //Turned first - which does nothing to the origin - and then moved.
        AssertPoint(1000, 0, quarterTurn.Then(move).Apply(0, 0));
    }

    [Fact]
    public void Composing_with_the_identity_changes_nothing()
    {
        var placement = Transform.ForPlacement(true, 1.5, 30, 40, 50);

        var before = placement.Apply(123, -456);

        AssertPoint(before.X, before.Y, placement.Then(Transform.Identity).Apply(123, -456));
        AssertPoint(before.X, before.Y, Transform.Identity.Then(placement).Apply(123, -456));
    }

    ///<summary>Nesting multiplies the magnifications, which is what an absolute one has to divide out.</summary>
    [Fact]
    public void Composed_magnifications_multiply()
    {
        var outer = Transform.ForPlacement(false, 3, 0, 0, 0);
        var inner = Transform.ForPlacement(false, 2, 0, 0, 0);

        Assert.Equal(6, inner.Then(outer).Scale, Tolerance);
    }

    [Fact]
    public void Composed_rotations_add()
    {
        var outer = Transform.ForPlacement(false, 1, 30, 0, 0);
        var inner = Transform.ForPlacement(false, 1, 45, 0, 0);

        Assert.Equal(75, inner.Then(outer).AngleInDegrees, 1e-9);
    }

    #endregion ************************************************************************



    #region Scale and angle ***********************************************************

    [Theory]
    [InlineData(1)]
    [InlineData(0.1)]
    [InlineData(2.5)]
    [InlineData(1000)]
    public void Scale_reads_back_the_magnification_whatever_the_rotation(double magnification)
    {
        var transform = Transform.ForPlacement(false, magnification, 37, 0, 0);

        Assert.Equal(magnification, transform.Scale, Tolerance);
    }

    ///<summary>Reflection does not change how much a placement magnifies, only which way round it is.</summary>
    [Fact]
    public void Scale_is_unaffected_by_reflection()
    {
        Assert.Equal(2, Transform.ForPlacement(true, 2, 0, 0, 0).Scale, Tolerance);
        Assert.Equal(2, Transform.ForPlacement(true, 2, 90, 0, 0).Scale, Tolerance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(179)]
    [InlineData(-90)]
    public void AngleInDegrees_reads_back_the_rotation(double angle)
    {
        var transform = Transform.ForPlacement(false, 4, angle, 0, 0);

        Assert.Equal(angle, transform.AngleInDegrees, 1e-9);
    }

    ///<summary>
    ///Read off the transformed x axis, which a reflection about X leaves alone - so a reflected placement
    ///still reports the rotation it was given rather than its mirror.
    ///</summary>
    [Fact]
    public void AngleInDegrees_is_unaffected_by_reflection()
    {
        Assert.Equal(30, Transform.ForPlacement(true, 1, 30, 0, 0).AngleInDegrees, 1e-9);
    }

    ///<summary>Translation is not part of either, which is what makes them safe to divide out.</summary>
    [Fact]
    public void Scale_and_angle_ignore_the_translation()
    {
        var transform = Transform.ForPlacement(false, 2, 60, 9999, -9999);

        Assert.Equal(2, transform.Scale, Tolerance);
        Assert.Equal(60, transform.AngleInDegrees, 1e-9);
    }

    #endregion ************************************************************************



    #region Back to the integer grid **************************************************

    ///<summary>
    ///GDSII coordinates are integers, so a transform that lands between them has to round. Math.Round is
    ///banker's rounding - .5 goes to the even neighbor - which is worth pinning because it is not the
    ///rounding most people assume, and it decides a coordinate.
    ///</summary>
    [Fact]
    public void A_coordinate_landing_on_a_half_rounds_to_even()
    {
        var half = Transform.ForPlacement(false, 0.5, 0, 0, 0);

        //0.5 and 1.5 both land on a half: to 0 and to 2.
        AssertPoint(0, 2, half.Apply(1, 3));
        AssertPoint(2, 4, half.Apply(5, 7));
    }

    [Fact]
    public void A_coordinate_is_rounded_rather_than_truncated()
    {
        var transform = Transform.ForPlacement(false, 1, 0, 0, 0).Then(Transform.ForTranslation(0.4, 0.6));

        AssertPoint(100, 101, transform.Apply(100, 100));
    }

    #endregion ************************************************************************
}
