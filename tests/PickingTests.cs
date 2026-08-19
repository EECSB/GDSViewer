using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Which shape a click lands on.
///
///**This was the browser's answer until the picture stopped being one node per shape**, and a DOM hit test
///could not be tested from here at all. <see cref="Picking"/> says as much in its own summary - "it is also
///directly testable, which the browser's answer never was" - and then nothing tested it. Every rule it
///carries was a rule the DOM used to enforce for free, which is exactly the kind of thing that gets quietly
///lost when it moves into code: the picture still looks right, and the wrong shape is chosen.
///
///The end-to-end suite clicks shapes and checks the panel names the right one, so this is not first coverage
///of the behavior - it is the first coverage that can say *why* a pick was wrong, in a suite that runs in
///milliseconds rather than minutes.
///</summary>
public class PickingTests
{
    #region Shapes built by hand ******************************************************

    private static Layer OnLayer(short number, short dataType = 20)
    {
        return new Layer(new LayerKey(number, dataType), "#ff0000");
    }

    ///<summary>A closed square with its lower left at (x, y), so overlaps can be placed exactly.</summary>
    private static Element Square(int x, int y, int side, Layer layer)
    {
        return new Element
        {
            Layer = layer,
            Points = new List<Element.Point>
            {
                new Element.Point(x, y),
                new Element.Point(x + side, y),
                new Element.Point(x + side, y + side),
                new Element.Point(x, y + side),
                new Element.Point(x, y)
            }
        };
    }

    private static FlattenedLayout Holding(params Element[] elements)
    {
        return new FlattenedLayout { Elements = elements.ToList() };
    }

    private static Element.Point At(int x, int y)
    {
        return new Element.Point(x, y);
    }

    #endregion ***********************************************************************



    #region One shape ****************************************************************

    [Fact]
    public void A_point_inside_a_shape_finds_it()
    {
        var layout = Holding(Square(0, 0, 100, OnLayer(65)));

        Assert.Equal(0, Picking.At(layout, At(50, 50)));
    }

    [Fact]
    public void A_point_outside_every_shape_finds_nothing()
    {
        var layout = Holding(Square(0, 0, 100, OnLayer(65)));

        Assert.Equal(-1, Picking.At(layout, At(500, 500)));
    }

    ///<summary>
    ///Inside the box and outside the outline is outside the shape, which is the whole reason the box test is
    ///not the answer on its own. An L is the cheapest shape whose box holds a point it does not.
    ///</summary>
    [Fact]
    public void The_box_is_not_the_shape()
    {
        var elbow = new Element
        {
            Layer = OnLayer(65),
            Points = new List<Element.Point>
            {
                At(0, 0), At(100, 0), At(100, 40), At(40, 40), At(40, 100), At(0, 100), At(0, 0)
            }
        };

        var layout = Holding(elbow);

        //In the arm.
        Assert.Equal(0, Picking.At(layout, At(20, 20)));

        //In the notch, which the box contains and the outline does not.
        Assert.Equal(-1, Picking.At(layout, At(80, 80)));
    }

    ///<summary>
    ///On the edge counts, the same as it does for a traced net. A rectangle whose boundary refused would be
    ///unselectable along its whole outline, and a corner is where somebody aiming at a small shape clicks.
    ///</summary>
    [Theory]
    [InlineData(0, 50)]
    [InlineData(100, 50)]
    [InlineData(50, 0)]
    [InlineData(50, 100)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    public void A_click_on_the_boundary_is_a_click_on_the_shape(int x, int y)
    {
        var layout = Holding(Square(0, 0, 100, OnLayer(65)));

        Assert.Equal(0, Picking.At(layout, At(x, y)));
    }

    #endregion ***********************************************************************



    #region What is not geometry ******************************************************

    ///
    ///**A label is never picked here**, however large the name it draws.
    ///
    ///Labels are still their own nodes and the browser still hit-tests those; what moved into C# is the
    ///geometry. A label whose box was allowed to answer would swallow every click meant for a shape drawn
    ///over it, because a name's box is far larger than the anchor it hangs from - which is a bug this
    ///project has already had once, from the other direction.
    ///
    [Fact]
    public void A_label_is_never_picked()
    {
        var label = new Element
        {
            Layer = OnLayer(83, 44),
            Points = new List<Element.Point> { At(50, 50) },
            Text = "VDD"
        };

        Assert.False(Picking.Covers(label, At(50, 50)));
        Assert.Equal(-1, Picking.At(Holding(label), At(50, 50)));
    }

    ///
    ///And it is the *text* that rules a label out, not the single point it happens to be.
    ///
    ///**The test above does not say that**, which was worth finding out: a flattened label carries its anchor
    ///and nothing else, so one point is all there is to test against and a one-point ring is outside itself as
    ///far as Clipper is concerned. Deleting the guard leaves that test passing. So the shape here is one no
    ///flattener builds - text with a real extent - which is the only thing that can tell the guard from the
    ///accident, and is what would arrive the day a label gains a box of its own.
    ///
    [Fact]
    public void It_is_the_text_that_rules_a_label_out_and_not_its_one_point()
    {
        var boxed = Square(0, 0, 100, OnLayer(83, 44));
        boxed.Text = "VDD";

        Assert.False(Picking.Covers(boxed, At(50, 50)));

        //Same points without the text, so the difference is the text and nothing else.
        Assert.True(Picking.Covers(Square(0, 0, 100, OnLayer(83, 44)), At(50, 50)));
    }

    ///<summary>
    ///An open run has no inside, so its box stands in for the line. Generous by a hair, which is the right
    ///way to be wrong about something two pixels wide - and the reason a DXF full of arcs is clickable at all.
    ///</summary>
    [Fact]
    public void An_open_run_is_picked_by_its_box()
    {
        var run = new Element
        {
            Layer = OnLayer(65),
            IsOpen = true,
            Points = new List<Element.Point> { At(0, 0), At(100, 0), At(100, 100) }
        };

        //On the line.
        Assert.True(Picking.Covers(run, At(50, 0)));

        //Off the line but inside the corner the two segments make, which the box holds.
        Assert.True(Picking.Covers(run, At(20, 80)));

        //And outside the box is still outside.
        Assert.False(Picking.Covers(run, At(200, 50)));
    }

    #endregion ***********************************************************************



    #region Two shapes at one point ***************************************************

    ///
    ///**The last one wins, not the first.**
    ///
    ///Later in the layout is later in the drawing, so the last match is the shape on top - which is the
    ///answer the DOM used to give. Returning the first instead loses a shape just drawn to whatever it was
    ///drawn over, which is exactly what an editor is used for.
    ///
    [Fact]
    public void The_shape_on_top_wins()
    {
        var under = Square(0, 0, 100, OnLayer(65));
        var over = Square(0, 0, 100, OnLayer(67));

        Assert.Equal(1, Picking.At(Holding(under, over), At(50, 50)));

        //And the other way round, so this is about order and not about the layer numbers.
        Assert.Equal(1, Picking.At(Holding(over, under), At(50, 50)));
    }

    ///<summary>
    ///A hidden layer takes no clicks. A shape nobody can see is not a shape anybody meant to choose, and the
    ///one drawn under it is what a click there means.
    ///</summary>
    [Fact]
    public void A_hidden_layer_does_not_take_the_click()
    {
        var under = Square(0, 0, 100, OnLayer(65));
        var over = Square(0, 0, 100, OnLayer(67));

        var layout = Holding(under, over);

        var onlyTheLower = new HashSet<LayerKey> { new LayerKey(65, 20) };

        Assert.Equal(0, Picking.At(layout, At(50, 50), onlyTheLower));

        //With none of them visible there is nothing to pick, rather than the topmost anyway.
        Assert.Equal(-1, Picking.At(layout, At(50, 50), new HashSet<LayerKey>()));
    }

    ///<summary>Null is every layer, which is what a view with no layer list passes.</summary>
    [Fact]
    public void No_layer_list_means_every_layer()
    {
        var layout = Holding(Square(0, 0, 100, OnLayer(65)));

        Assert.Equal(0, Picking.At(layout, At(50, 50), null));
    }

    #endregion ***********************************************************************



    #region Choosing between two answers *********************************************

    ///<summary>
    ///<see cref="Picking.Preferred"/> resolves the one case that still has two answers: a label found by the
    ///browser and geometry found here. Nothing to choose means the one that exists.
    ///</summary>
    [Fact]
    public void Nothing_found_on_one_side_leaves_the_other()
    {
        var layout = Holding(Square(0, 0, 100, OnLayer(65)), Square(0, 0, 100, OnLayer(67)));

        Assert.Equal(1, Picking.Preferred(layout, null, -1, 1));
        Assert.Equal(0, Picking.Preferred(layout, null, 0, -1));
        Assert.Equal(-1, Picking.Preferred(layout, null, -1, -1));
    }

    ///<summary>And with two real answers, later in the layout wins - the same rule, by the same reasoning.</summary>
    [Fact]
    public void Between_two_answers_the_later_one_wins()
    {
        var layout = Holding(Square(0, 0, 100, OnLayer(65)), Square(0, 0, 100, OnLayer(67)));

        Assert.Equal(1, Picking.Preferred(layout, null, 0, 1));
        Assert.Equal(1, Picking.Preferred(layout, null, 1, 0));
    }

    #endregion ***********************************************************************



    #region The cell being edited ****************************************************

    ///
    ///A library where a shape of a placed cell sits under a shape of the top level, at the same point.
    ///
    ///LEAF holds a square at the origin; TOP places it there and draws its own square over the top of it.
    ///The top's square is written second, so it is the one drawn later - which means the ordinary rule picks
    ///it and the context rule has to overrule something real.
    ///
    private static byte[] Overlapping()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("OVERLAP")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(65)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(100))),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),

            //The placement first, so the leaf's square is element nought of the flattened list.
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(new[] { 0, 0 })),
            GdsTestData.Record(RecordType.ENDEL),

            //And the top's own square over it, drawn later.
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(67)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(100))),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)
        };

        return GdsTestData.Concat(records.ToArray());
    }

    ///<summary>Confirms the fixture is the case it claims to be: without a context, the top's square wins.</summary>
    [Fact]
    public void With_no_cell_being_edited_the_topmost_shape_wins()
    {
        var layout = GdsFlattener.Flatten(new GDS(Overlapping()));

        int found = Picking.At(layout, At(50, 50));

        Assert.Equal("TOP", layout.Elements[found].Source!.Structure);
    }

    ///
    ///**And inside a cell, that cell's shape wins whatever is drawn over it.**
    ///
    ///The layout around a cell being edited is faded because it is not what the pointer is for, and a shape
    ///of another cell sitting over the one being worked on taking the click is the reason this question left
    ///the DOM. A hit test can only answer "whatever is on top"; asking the layout can answer "the thing you
    ///are editing", which is what clicking through a faded context means.
    ///
    [Fact]
    public void Inside_a_cell_its_own_shape_wins_the_click()
    {
        var layout = GdsFlattener.Flatten(new GDS(Overlapping()));

        int found = Picking.At(layout, At(50, 50), null, CellContext.Of("LEAF"));

        Assert.Equal("LEAF", layout.Elements[found].Source!.Structure);
    }

    ///<summary>The same preference when the choice is between a label and a shape.</summary>
    [Fact]
    public void The_cell_being_edited_outranks_the_drawing_order()
    {
        var layout = GdsFlattener.Flatten(new GDS(Overlapping()));

        int leaf = layout.Elements.FindIndex(element => element.Source!.Structure == "LEAF");
        int top = layout.Elements.FindIndex(element => element.Source!.Structure == "TOP");

        Assert.True(leaf < top, "The fixture should draw the leaf's square first.");

        var context = CellContext.Of("LEAF");

        Assert.Equal(leaf, Picking.Preferred(layout, context, leaf, top));
        Assert.Equal(leaf, Picking.Preferred(layout, context, top, leaf));

        //And with neither in the context, the order decides again.
        Assert.Equal(top, Picking.Preferred(layout, CellContext.Of("NEITHER"), leaf, top));
    }

    #endregion ***********************************************************************
}
