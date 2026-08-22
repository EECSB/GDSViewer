using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Bringing boxes into line, and spacing them out.
///
///Arithmetic over rectangles, with nothing in it that knows what a shape is or which cell it sits in - which
///is what lets the awkward cases be written down directly: boxes of different sizes, boxes already in line,
///boxes given in an order that is not the order they sit in, and a middle that falls between two units.
///
///What is not here is which button means which edge. That depends on which way the view draws Y and belongs
///with the buttons; see Viewer2DSvg.
///</summary>
public class AligningTests
{
    private static Bounds Box(int left, int bottom, int width, int height)
    {
        return new Bounds(left, bottom, left + width, bottom + height);
    }

    ///<summary>Where each box ends up once its offset is applied, which is what the assertions are about.</summary>
    private static List<Bounds> Moved(IReadOnlyList<Bounds> boxes, IReadOnlyList<(int Dx, int Dy)> offsets)
    {
        var moved = new List<Bounds>();

        for (int i = 0; i < boxes.Count; i++)
        {
            moved.Add(new Bounds(
                boxes[i].Left + offsets[i].Dx,
                boxes[i].Bottom + offsets[i].Dy,
                boxes[i].Right + offsets[i].Dx,
                boxes[i].Top + offsets[i].Dy));
        }

        return moved;
    }

    #region Lining up ***************************************************************

    ///<summary>Every box ends up with the same edge, and that edge is the one the set already reached to.</summary>
    [Theory]
    [InlineData(Edge.LeastX)]
    [InlineData(Edge.MostX)]
    [InlineData(Edge.LeastY)]
    [InlineData(Edge.MostY)]
    public void Lining_up_puts_every_box_on_the_same_edge(Edge edge)
    {
        var boxes = new List<Bounds>
        {
            Box(0, 0, 100, 50),
            Box(300, 120, 40, 200),
            Box(-70, -400, 500, 20)
        };

        var whole = Bounds.Empty;

        foreach (var box in boxes)
            whole = whole.Union(box);

        var moved = Moved(boxes, Aligning.Aligned(boxes, edge));

        foreach (var box in moved)
        {
            if (edge == Edge.LeastX)
                Assert.Equal(whole.Left, box.Left);
            else if (edge == Edge.MostX)
                Assert.Equal(whole.Right, box.Right);
            else if (edge == Edge.LeastY)
                Assert.Equal(whole.Bottom, box.Bottom);
            else
                Assert.Equal(whole.Top, box.Top);
        }
    }

    ///<summary>And nothing moves along the other axis while it does.</summary>
    [Theory]
    [InlineData(Edge.LeastX, true)]
    [InlineData(Edge.MiddleX, true)]
    [InlineData(Edge.MostX, true)]
    [InlineData(Edge.LeastY, false)]
    [InlineData(Edge.MiddleY, false)]
    [InlineData(Edge.MostY, false)]
    public void Lining_up_moves_along_one_axis_only(Edge edge, bool acrossX)
    {
        var boxes = new List<Bounds> { Box(0, 0, 100, 50), Box(300, 120, 40, 200) };

        foreach ((int dx, int dy) in Aligning.Aligned(boxes, edge))
        {
            if (acrossX)
                Assert.Equal(0, dy);
            else
                Assert.Equal(0, dx);
        }
    }

    ///<summary>The middles all land on one line, and it is the middle of the whole set.</summary>
    [Fact]
    public void Lining_up_the_middles_puts_them_all_on_one_line()
    {
        var boxes = new List<Bounds>
        {
            Box(0, 0, 100, 10),
            Box(500, 0, 40, 10),
            Box(200, 0, 300, 10)
        };

        var moved = Moved(boxes, Aligning.Aligned(boxes, Edge.MiddleX));

        var middles = moved.Select(box => box.Left + box.Right).Distinct().ToList();

        Assert.Single(middles);

        //And it is where the set already was, so the group does not drift sideways.
        Assert.Equal((0 + 540) / 2, (int)(middles[0] / 2));
    }

    ///
    ///**A middle that falls between two units is rounded once, not twice.**
    ///
    ///Rounding each middle and subtracting them leaves boxes a unit apart from each other for no reason
    ///anybody could point at: two boxes of odd width, both meant to land on one line, ending up on two.
    ///
    [Fact]
    public void An_odd_width_still_lines_up_exactly_with_another()
    {
        var boxes = new List<Bounds>
        {
            Box(0, 0, 101, 10),
            Box(40, 0, 103, 10),
            Box(7, 0, 99, 10)
        };

        var moved = Moved(boxes, Aligning.Aligned(boxes, Edge.MiddleX));

        //Widths differ by one, so no two can share a middle exactly - but the doubled middles must, which is
        //the same thing said without halving.
        Assert.Single(moved.Select(box => box.Left + box.Right).Distinct());
    }

    ///<summary>Boxes already in line have nothing to do, so nothing is offered to do.</summary>
    [Fact]
    public void Boxes_already_in_line_come_back_with_no_offsets()
    {
        var boxes = new List<Bounds> { Box(10, 0, 100, 50), Box(10, 200, 40, 20) };

        Assert.All(Aligning.Aligned(boxes, Edge.LeastX), offset => Assert.Equal((0, 0), offset));
    }

    ///<summary>Lining up twice is the same as lining up once, which is what makes the button safe to press.</summary>
    [Theory]
    [InlineData(Edge.LeastX)]
    [InlineData(Edge.MiddleX)]
    [InlineData(Edge.MostY)]
    public void Lining_up_again_changes_nothing(Edge edge)
    {
        var boxes = new List<Bounds> { Box(0, 0, 100, 50), Box(300, 120, 40, 200), Box(-70, -40, 500, 20) };

        var once = Moved(boxes, Aligning.Aligned(boxes, edge));

        Assert.All(Aligning.Aligned(once, edge), offset => Assert.Equal((0, 0), offset));
    }

    [Fact]
    public void One_box_on_its_own_is_already_in_line()
    {
        var boxes = new List<Bounds> { Box(37, 42, 100, 50) };

        Assert.All(Aligning.Aligned(boxes, Edge.MostX), offset => Assert.Equal((0, 0), offset));
    }

    #endregion **********************************************************************



    #region Spacing out *************************************************************

    ///
    ///**The middles come out evenly spaced, and the two on the ends do not move.**
    ///
    ///Written with four different widths, because evenly spaced middles and equal gaps are the same answer
    ///for boxes of one size - which is most of what gets spaced out on a chip, and would hide the difference
    ///between the two entirely.
    ///
    [Fact]
    public void Spacing_out_evens_the_middles_and_leaves_the_ends_alone()
    {
        var boxes = new List<Bounds>
        {
            Box(0, 0, 100, 10),
            Box(150, 0, 20, 10),
            Box(400, 0, 60, 10),
            Box(900, 0, 40, 10)
        };

        var moved = Moved(boxes, Aligning.SpacedOut(boxes, Along.X));

        Assert.Equal(boxes[0], moved[0]);
        Assert.Equal(boxes[3], moved[3]);

        var middles = moved
            .Select(box => box.Left + box.Right)
            .OrderBy(middle => middle)
            .ToList();

        var steps = new List<long>();

        for (int i = 1; i < middles.Count; i++)
            steps.Add(middles[i] - middles[i - 1]);

        //Doubled middles, so the tolerance is two rather than one.
        Assert.True(steps.Max() - steps.Min() <= 2, $"steps were {string.Join(", ", steps)}");
    }

    ///
    ///**Nothing ends up outside the two on the ends.**
    ///
    ///The property that decided which of the two conventions this is. Chip geometry overlaps by design -
    ///every contact sits inside the metal it connects - and for boxes that overlap there is no free space
    ///between them to divide. Spacing the *edges* works out a negative gap and marches the middle ones
    ///outward past the ends, which is a button labeled "space out" flinging a stack of layers across the
    ///cell. Spacing the middles cannot do that.
    ///
    [Fact]
    public void Nothing_is_pushed_outside_the_two_on_the_ends()
    {
        //Four boxes that all overlap each other, which is what a via inside a pad inside a metal looks like.
        var boxes = new List<Bounds>
        {
            Box(0, 0, 400, 10),
            Box(20, 0, 380, 10),
            Box(40, 0, 360, 10),
            Box(60, 0, 340, 10)
        };

        var moved = Moved(boxes, Aligning.SpacedOut(boxes, Along.X));

        long leastMiddle = boxes.Min(box => box.Left + box.Right);
        long mostMiddle = boxes.Max(box => box.Left + box.Right);

        foreach (var box in moved)
        {
            Assert.True(box.Left + box.Right >= leastMiddle, "a box was pushed out past the leftmost");
            Assert.True(box.Left + box.Right <= mostMiddle, "a box was pushed out past the rightmost");
        }
    }

    ///
    ///**The order they were chosen in is not the order they sit in, and the answer is about the second.**
    ///
    ///The one that sits in the middle is given *first*, deliberately. A list whose ends happen to be the
    ///boxes at the ends comes out right whether or not anything sorted it, so it says nothing - which is how
    ///the first version of this test passed with the sort taken out.
    ///
    [Fact]
    public void Spacing_out_goes_by_where_they_sit_not_by_the_order_given()
    {
        var jumbled = new List<Bounds> { Box(150, 0, 20, 10), Box(0, 0, 100, 10), Box(900, 0, 40, 10) };

        var moved = Moved(jumbled, Aligning.SpacedOut(jumbled, Along.X));

        //The two that sit outermost stay, whichever place in the list they came in - and the middle one is
        //the one that moves, though it was given first.
        Assert.Equal(jumbled[1], moved[1]);
        Assert.Equal(jumbled[2], moved[2]);
        Assert.NotEqual(jumbled[0], moved[0]);
    }

    [Fact]
    public void Spacing_out_moves_along_one_axis_only()
    {
        var boxes = new List<Bounds> { Box(0, 0, 100, 10), Box(150, 40, 20, 10), Box(900, 90, 40, 10) };

        Assert.All(Aligning.SpacedOut(boxes, Along.X), offset => Assert.Equal(0, offset.Dy));
        Assert.All(Aligning.SpacedOut(boxes, Along.Y), offset => Assert.Equal(0, offset.Dx));
    }

    ///<summary>Two boxes have no gap between them to divide, so there is nothing to even out.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Fewer_than_three_boxes_have_nothing_to_space_out(int count)
    {
        var boxes = new List<Bounds>();

        for (int i = 0; i < count; i++)
            boxes.Add(Box(i * 500, 0, 100, 10));

        var offsets = Aligning.SpacedOut(boxes, Along.X);

        Assert.Equal(count, offsets.Count);
        Assert.All(offsets, offset => Assert.Equal((0, 0), offset));
    }

    ///<summary>And spacing out again changes nothing, so the button is safe to press twice.</summary>
    [Fact]
    public void Spacing_out_again_changes_nothing()
    {
        var boxes = new List<Bounds>
        {
            Box(0, 0, 100, 10),
            Box(150, 0, 20, 10),
            Box(400, 0, 60, 10),
            Box(900, 0, 40, 10)
        };

        var once = Moved(boxes, Aligning.SpacedOut(boxes, Along.X));

        Assert.All(Aligning.SpacedOut(once, Along.X), offset =>
            Assert.True(Math.Abs(offset.Dx) <= 1, $"a second pass moved something by {offset.Dx}"));
    }

    ///<summary>Boxes that overlap are still evened out, rather than left where they were.</summary>
    [Fact]
    public void Boxes_that_overlap_are_still_evened_out()
    {
        var boxes = new List<Bounds>
        {
            Box(0, 0, 100, 10),
            Box(10, 0, 100, 10),
            Box(20, 0, 100, 10),
            Box(150, 0, 100, 10)
        };

        var moved = Moved(boxes, Aligning.SpacedOut(boxes, Along.X));

        var middles = moved.Select(box => box.Left + box.Right).OrderBy(middle => middle).ToList();

        var steps = new List<long>();

        for (int i = 1; i < middles.Count; i++)
            steps.Add(middles[i] - middles[i - 1]);

        Assert.True(steps.Max() - steps.Min() <= 2, $"steps were {string.Join(", ", steps)}");

        //And it really did move something, or evening out is not what happened.
        Assert.NotEqual(boxes[1], moved[1]);
    }

    #endregion **********************************************************************
}
