using GdsII;

namespace GDSViewer.Tests;

///
///Making geometry a size somebody asked for.
///
///**The thing worth testing is that it rounds, and where.** Every other edit here is exact and says so; this
///one is not, so what has to hold is that the *box* lands on the size asked for even though the corners
///inside it each moved by their own fraction of a unit. A test that only checked one corner would pass on an
///implementation that scaled about the wrong point.
///
public class ScalingTests
{
    #region The factor **************************************************************

    [Fact]
    public void The_factor_is_the_size_wanted_over_the_size_there()
    {
        Assert.Equal(2, Scaling.Factor(500, 1000));
        Assert.Equal(0.5, Scaling.Factor(1000, 500));
        Assert.Equal(1, Scaling.Factor(700, 700));
    }

    ///
    ///**A shape with no extent cannot be given one.**
    ///
    ///A flat run or a path drawn straight has nothing to multiply, and any answer here would be invented.
    ///Refusing leaves the shape alone, which is the only honest option of the three.
    ///
    [Fact]
    public void Something_with_no_extent_is_refused()
    {
        Assert.Null(Scaling.Factor(0, 500));
    }

    [Fact]
    public void A_size_of_nothing_or_less_is_refused()
    {
        Assert.Null(Scaling.Factor(500, 0));
        Assert.Null(Scaling.Factor(500, -100));
        Assert.Null(Scaling.Factor(500, double.NaN));
        Assert.Null(Scaling.Factor(500, double.PositiveInfinity));
    }

    #endregion **********************************************************************



    #region The point ***************************************************************

    ///<summary>The point it is scaled about does not move, which is what makes it the anchor.</summary>
    [Fact]
    public void The_anchor_stays_where_it_is()
    {
        Assert.Equal((100.0, 200.0), Scaling.Point(100, 200, 5, 5, 100, 200));
    }

    [Fact]
    public void Each_axis_is_scaled_on_its_own()
    {
        (double x, double y) = Scaling.Point(110, 220, 2, 3, 100, 200);

        Assert.Equal(120, x);
        Assert.Equal(260, y);
    }

    ///<summary>A factor of one along an axis is that axis left alone, which is what a single box typed means.</summary>
    [Fact]
    public void Scaling_across_leaves_the_other_axis_alone()
    {
        (double x, double y) = Scaling.Point(110, 220, 4, 1, 100, 200);

        Assert.Equal(140, x);
        Assert.Equal(220, y);
    }

    #endregion **********************************************************************



    #region A shape in a cell *******************************************************

    private static GDS Placed()
    {
        return new GDS(GdsTestData.ReadFixture("placed.gds"));
    }

    ///<summary>The first shape of a cell, and the context that cell is looked at through.</summary>
    private static (CellContext Context, GDS.ElementModel Model) AShape(GDS gds, string cell)
    {
        var flat = GdsFlattener.Flatten(gds);

        var element = flat.Elements.First(each => each.Source!.Structure == cell);

        return (CellContext.At(element.Source!), element.Source!.Model);
    }

    private static Bounds BoxOf(CellContext context, int[] coordinates)
    {
        var points = new List<Element.Point>();

        for (int i = 0; i + 1 < coordinates.Length; i += 2)
        {
            (double x, double y) = context.ToLayout(coordinates[i], coordinates[i + 1]);

            points.Add(new Element.Point((int)Math.Round(x), (int)Math.Round(y)));
        }

        return Bounds.Of(points);
    }

    ///
    ///**The box comes out the size that was asked for.**
    ///
    ///Measured in the layout's coordinates, which is where the number was typed - the corners themselves are
    ///in the cell's, and on a cell placed square the two agree only up to where the cell was put.
    ///
    [Fact]
    public void A_shape_scaled_across_is_that_wide()
    {
        var gds = Placed();

        (CellContext context, GDS.ElementModel model) = AShape(gds, "LEAF");

        var was = BoxOf(context, ((Int4Data)model.Element.XY!.Data!).Values);

        double factor = Scaling.Factor(was.Width, was.Width * 3)!.Value;

        int[] after = Scaling.Coordinates(context, model, factor, 1, was.Left, was.Bottom)!;

        var box = BoxOf(context, after);

        Assert.Equal(was.Width * 3, box.Width);

        //And nothing happened to the other axis.
        Assert.Equal(was.Height, box.Height);
    }

    ///<summary>The anchor is the corner the position boxes name, so growing it leaves that corner alone.</summary>
    [Fact]
    public void The_corner_it_is_anchored_on_does_not_move()
    {
        var gds = Placed();

        (CellContext context, GDS.ElementModel model) = AShape(gds, "LEAF");

        var was = BoxOf(context, ((Int4Data)model.Element.XY!.Data!).Values);

        int[] after = Scaling.Coordinates(context, model, 4, 4, was.Left, was.Bottom)!;

        var box = BoxOf(context, after);

        Assert.Equal(was.Left, box.Left);
        Assert.Equal(was.Bottom, box.Bottom);
    }

    ///<summary>A factor of one is the shape it already was, corner for corner.</summary>
    [Fact]
    public void Scaling_by_one_changes_nothing()
    {
        var gds = Placed();

        (CellContext context, GDS.ElementModel model) = AShape(gds, "LEAF");

        int[] was = ((Int4Data)model.Element.XY!.Data!).Values;

        Assert.Equal(was, Scaling.Coordinates(context, model, 1, 1, 0, 0));
    }

    [Fact]
    public void An_element_with_no_coordinates_is_refused()
    {
        var gds = Placed();

        (CellContext context, GDS.ElementModel model) = AShape(gds, "LEAF");

        //A record with nothing in it, which is what an element with no geometry reads as.
        model.Element.XY!.Data = new Int4Data(Array.Empty<int>());

        Assert.Null(Scaling.Coordinates(context, model, 2, 2, 0, 0));
    }

    ///
    ///**Scaled twice by halves is not always back where it started.**
    ///
    ///Recorded rather than fixed, because it cannot be: each corner rounds on its own, and a rounding undone
    ///is a second rounding rather than the first one reversed. It is why undo stores both ends of a reshape
    ///instead of the operation between them, and why the box that uses this says on screen that it rounds.
    ///
    [Fact]
    public void Scaling_is_not_exactly_reversible()
    {
        var gds = Placed();

        (CellContext context, GDS.ElementModel model) = AShape(gds, "LEAF");

        int[] was = ((Int4Data)model.Element.XY!.Data!).Values;

        int[] bigger = Scaling.Coordinates(context, model, 3, 3, 1, 1)!;

        model.Element.XY!.Data = new Int4Data(bigger);

        int[] back = Scaling.Coordinates(context, model, 1.0 / 3.0, 1.0 / 3.0, 1, 1)!;

        //Near enough to be the same shape, and not guaranteed to be the same numbers.
        for (int i = 0; i < was.Length; i++)
            Assert.True(Math.Abs(was[i] - back[i]) <= 1);
    }

    #endregion **********************************************************************
}
