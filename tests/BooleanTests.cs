using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The four boolean operations and the offset.
///
///**Area is what most of this asserts.** A boolean result is a set of rings whose corners depend on how
///the library happened to walk the input, so comparing corner lists would pin an implementation detail;
///the area it encloses is the thing the operation is actually for and is the same whichever way the rings
///came out. Where the shape itself matters - a hole, a result in two pieces - that is asserted directly.
///
///The keyhole is the part worth the most care. GDSII cannot hold a hole, so one is folded into the outline
///as a channel that reaches in and comes back along the same line, and the two tests that matter are that
///the area is right afterwards and that a second pass over the result finds the same shape again.
///</summary>
public class BooleanTests
{
    #region Shapes to work with ******************************************************

    private static List<Element.Point> Box(int left, int bottom, int right, int top)
    {
        return new List<Element.Point>
        {
            new Element.Point { X = left, Y = bottom },
            new Element.Point { X = right, Y = bottom },
            new Element.Point { X = right, Y = top },
            new Element.Point { X = left, Y = top }
        };
    }

    private static List<List<Element.Point>> One(List<Element.Point> shape)
    {
        return new List<List<Element.Point>> { shape };
    }

    ///<summary>
    ///The area a set of rings encloses, by the shoelace formula.
    ///
    ///Signed and then taken absolute per ring, so a keyholed outline comes out as the outer area less the
    ///hole - the channel contributes nothing, which is the whole idea of it.
    ///</summary>
    private static double Area(IEnumerable<IReadOnlyList<Element.Point>> shapes)
    {
        double total = 0;

        foreach (var shape in shapes)
        {
            double sum = 0;

            for (int i = 0; i < shape.Count; i++)
            {
                var a = shape[i];
                var b = shape[(i + 1) % shape.Count];

                sum += ((double)a.X * b.Y) - ((double)b.X * a.Y);
            }

            total += Math.Abs(sum) / 2;
        }

        return total;
    }

    #endregion ***********************************************************************



    #region The four operations ******************************************************

    ///<summary>Two 100 by 100 boxes overlapping by 50 in each direction.</summary>
    private static readonly List<Element.Point> A = Box(0, 0, 100, 100);
    private static readonly List<Element.Point> B = Box(50, 50, 150, 150);

    [Fact]
    public void And_is_where_both_cover()
    {
        var result = Booleans.Combine(One(A), One(B), BooleanOperation.And);

        Assert.Single(result);
        Assert.Equal(50 * 50, Area(result));
    }

    [Fact]
    public void Or_is_the_two_merged_into_one()
    {
        var result = Booleans.Combine(One(A), One(B), BooleanOperation.Or);

        Assert.Single(result);
        Assert.Equal((100 * 100 * 2) - (50 * 50), Area(result));
    }

    [Fact]
    public void Not_is_the_first_with_the_second_taken_out()
    {
        var result = Booleans.Combine(One(A), One(B), BooleanOperation.Not);

        Assert.Equal((100 * 100) - (50 * 50), Area(result));
    }

    ///<summary>
    ///Exactly one, and not both - so the overlap is gone from a shape that is otherwise the union, and it
    ///comes out in two pieces that touch only at a corner.
    ///</summary>
    [Fact]
    public void Xor_is_either_but_not_both()
    {
        var result = Booleans.Combine(One(A), One(B), BooleanOperation.Xor);

        Assert.Equal((100 * 100 * 2) - (2 * 50 * 50), Area(result));
    }

    [Fact]
    public void Shapes_that_do_not_touch_come_back_as_two()
    {
        var result = Booleans.Combine(One(A), One(Box(500, 500, 600, 600)), BooleanOperation.Or);

        Assert.Equal(2, result.Count);
        Assert.Equal((100 * 100) + (100 * 100), Area(result));
    }

    [Fact]
    public void And_of_shapes_that_do_not_touch_is_nothing()
    {
        Assert.Empty(Booleans.Combine(One(A), One(Box(500, 500, 600, 600)), BooleanOperation.And));
    }

    ///<summary>
    ///Merging is what a flattened hierarchy needs: overlapping copies of the same geometry are one shape,
    ///and a layer's area is wrong until they are.
    ///</summary>
    [Fact]
    public void Merging_overlapping_shapes_leaves_one_outline()
    {
        var shapes = new List<List<Element.Point>> { A, B, Box(25, 25, 75, 75) };

        var result = Booleans.Merge(shapes);

        Assert.Single(result);
        Assert.Equal((100 * 100 * 2) - (50 * 50), Area(result));
    }

    ///<summary>
    ///A GDSII file says nothing about which way a boundary runs, and real files carry both.
    ///
    ///**Merged, not combined.** The first version of this passed the two shapes as the two sides of a
    ///union, which proves nothing: Clipper sorts the orientation of a subject against a clip out by
    ///itself. Where it cannot is when both are in the *same* set - the non-zero rule adds their windings,
    ///and two opposite ones cancel to nothing, so the overlap comes out as a hole in the middle of solid
    ///metal. That is what merging a layer does, and it is the case this has to cover.
    ///</summary>
    [Fact]
    public void Two_shapes_wound_opposite_ways_still_merge()
    {
        var backwards = new List<Element.Point>(B);
        backwards.Reverse();

        var result = Booleans.Merge(new List<List<Element.Point>> { A, backwards });

        Assert.Single(result);
        Assert.Equal((100 * 100 * 2) - (50 * 50), Area(result));
    }

    ///<summary>And the same the other way round, so neither order is the one that happens to work.</summary>
    [Fact]
    public void The_order_they_are_wound_in_does_not_matter()
    {
        var backwards = new List<Element.Point>(A);
        backwards.Reverse();

        var result = Booleans.Merge(new List<List<Element.Point>> { backwards, B });

        Assert.Single(result);
        Assert.Equal((100 * 100 * 2) - (50 * 50), Area(result));
    }

    ///<summary>A ring closed by repeating its first corner is how GDSII writes one, and is not a triangle.</summary>
    [Fact]
    public void A_ring_closed_by_a_repeated_corner_reads_as_the_square_it_is()
    {
        var closed = new List<Element.Point>(A) { A[0] };

        Assert.Equal(100 * 100, Area(Booleans.Merge(One(closed))));
    }

    #endregion ***********************************************************************



    #region More than two at once ****************************************************

    ///
    ///Three boxes in a row, each overlapping the next and only the next.
    ///
    ///The middle one shares ground with both of the others and the outer two share none with each other,
    ///which is what makes "all of them" and "the first with any of them" different answers rather than the
    ///same one written twice.
    ///
    private static readonly List<Element.Point> Left = Box(0, 0, 100, 100);
    private static readonly List<Element.Point> Middle = Box(50, 0, 150, 100);
    private static readonly List<Element.Point> Right = Box(120, 0, 220, 100);

    //Left with Middle, and Middle with Right. Left never reaches Right, which stops at 100 where it starts
    //at 120.
    private const int LeftOnMiddle = 50 * 100;
    private const int MiddleOnRight = 30 * 100;
    private const int EachBox = 100 * 100;

    private static List<IReadOnlyList<Element.Point>> Three()
    {
        return new List<IReadOnlyList<Element.Point>> { Left, Middle, Right };
    }

    [Fact]
    public void Union_of_three_covers_what_all_three_cover()
    {
        var made = Booleans.CombineAll(Three(), BooleanOperation.Or);

        //All three are joined in a chain, so one outline - and the area of the three less the ground each
        //overlap was counted twice for.
        Assert.Single(made);
        Assert.Equal((3 * EachBox) - LeftOnMiddle - MiddleOnRight, Area(made), 3);
    }

    ///
    ///**Where all three cover, not where the first covers with any of them.**
    ///
    ///The decision this whole method exists to make. Folding through every shape asks the first question;
    ///taking the first against the merge of the others asks the second, and here the second answers "the
    ///area the left box shares with the middle one" while the true answer is nothing at all - the right box
    ///does not reach the left one.
    ///
    [Fact]
    public void Intersect_of_three_is_where_every_one_of_them_covers()
    {
        Assert.Empty(Booleans.CombineAll(Three(), BooleanOperation.And));
    }

    [Fact]
    public void Intersect_of_three_that_do_all_meet_is_that_overlap()
    {
        var stacked = new List<IReadOnlyList<Element.Point>>
        {
            Box(0, 0, 100, 100),
            Box(20, 0, 100, 100),
            Box(40, 0, 100, 100)
        };

        Assert.Equal(60 * 100, Area(Booleans.CombineAll(stacked, BooleanOperation.And)), 3);
    }

    ///<summary>Subtraction has a side: the first chosen, with all the others taken out of it.</summary>
    [Fact]
    public void Subtract_takes_every_other_shape_out_of_the_first()
    {
        var made = Booleans.CombineAll(Three(), BooleanOperation.Not);

        //The left box less the part the middle one covers. The right box never reaches it.
        Assert.Equal(50 * 100, Area(made), 3);
    }

    ///<summary>And it is the first that survives, not whichever happens to be leftmost.</summary>
    [Fact]
    public void Subtract_starts_from_the_one_given_first()
    {
        var reversed = new List<IReadOnlyList<Element.Point>> { Right, Middle, Left };

        var made = Booleans.CombineAll(reversed, BooleanOperation.Not);

        //The right box less the part the middle one covers.
        Assert.Equal(70 * 100, Area(made), 3);
    }

    ///<summary>Exclude is where an odd number cover, so the ground two of them share drops out.</summary>
    [Fact]
    public void Exclude_of_three_drops_what_an_even_number_cover()
    {
        var made = Booleans.CombineAll(Three(), BooleanOperation.Xor);

        //The union, less both overlaps again: each of those is covered twice, so it goes from being in the
        //answer to being out of it.
        int union = (3 * EachBox) - LeftOnMiddle - MiddleOnRight;

        Assert.Equal(union - LeftOnMiddle - MiddleOnRight, Area(made), 3);
    }

    ///<summary>One shape is its own answer to any of them, rather than an error or an empty result.</summary>
    [Theory]
    [InlineData(BooleanOperation.Or)]
    [InlineData(BooleanOperation.And)]
    [InlineData(BooleanOperation.Not)]
    [InlineData(BooleanOperation.Xor)]
    public void One_shape_comes_back_as_itself(BooleanOperation operation)
    {
        var only = new List<IReadOnlyList<Element.Point>> { Left };

        Assert.Equal(100 * 100, Area(Booleans.CombineAll(only, operation)), 3);
    }

    [Theory]
    [InlineData(BooleanOperation.Or)]
    [InlineData(BooleanOperation.And)]
    [InlineData(BooleanOperation.Not)]
    [InlineData(BooleanOperation.Xor)]
    public void Nothing_at_all_comes_back_as_nothing(BooleanOperation operation)
    {
        Assert.Empty(Booleans.CombineAll(new List<IReadOnlyList<Element.Point>>(), operation));
    }

    ///<summary>
    ///Two shapes go the same way through this as they do through Combine, or the button would mean one
    ///thing for a pair and another for three.
    ///</summary>
    [Theory]
    [InlineData(BooleanOperation.Or)]
    [InlineData(BooleanOperation.And)]
    [InlineData(BooleanOperation.Not)]
    [InlineData(BooleanOperation.Xor)]
    public void Two_shapes_agree_with_combining_them_directly(BooleanOperation operation)
    {
        var pair = new List<IReadOnlyList<Element.Point>> { A, B };

        double all = Area(Booleans.CombineAll(pair, operation));
        double directly = Area(Booleans.Combine(One(A), One(B), operation));

        Assert.Equal(directly, all, 3);
    }

    #endregion ***********************************************************************



    #region Holes ********************************************************************

    ///<summary>A 100 box with a 20 box taken out of the middle of it.</summary>
    private static List<List<Element.Point>> Donut()
    {
        return Booleans.Combine(One(Box(0, 0, 100, 100)), One(Box(40, 40, 60, 60)), BooleanOperation.Not);
    }

    ///<summary>
    ///One outline, not two. The hole is folded in as a channel rather than handed back separately, because
    ///a second boundary on the same layer would be drawn as solid.
    ///</summary>
    [Fact]
    public void A_hole_comes_back_folded_into_its_outline()
    {
        var donut = Donut();

        Assert.Single(donut);
        Assert.Equal((100 * 100) - (20 * 20), Area(donut));
    }

    ///<summary>
    ///And the channel really is a channel: reading the result back finds the same shape, so the outline
    ///that was written is one a boolean engine agrees describes a square with a square hole.
    ///</summary>
    [Fact]
    public void A_keyholed_outline_reads_back_as_the_same_shape()
    {
        var again = Booleans.Merge(Donut());

        Assert.Single(again);
        Assert.Equal((100 * 100) - (20 * 20), Area(again));
    }

    ///<summary>Two holes, so the second is bridged into a ring the first has already been cut into.</summary>
    [Fact]
    public void Two_holes_are_both_folded_in()
    {
        var holes = new List<List<Element.Point>> { Box(10, 40, 30, 60), Box(70, 40, 90, 60) };

        var result = Booleans.Combine(One(Box(0, 0, 100, 100)), holes, BooleanOperation.Not);

        Assert.Single(result);
        Assert.Equal((100 * 100) - (2 * 20 * 20), Area(result));
        Assert.Equal((100 * 100) - (2 * 20 * 20), Area(Booleans.Merge(result)));
    }

    ///<summary>
    ///An island in a lake. The thing inside a hole is an outline again, and comes back as its own shape
    ///rather than being folded into the ring around it.
    ///</summary>
    [Fact]
    public void A_shape_inside_a_hole_comes_back_as_its_own()
    {
        var donut = Donut();

        var withIsland = Booleans.Combine(donut, One(Box(45, 45, 55, 55)), BooleanOperation.Or);

        Assert.Equal(2, withIsland.Count);
        Assert.Equal((100 * 100) - (20 * 20) + (10 * 10), Area(withIsland));
    }

    #endregion ***********************************************************************



    #region Growing and shrinking ****************************************************

    [Fact]
    public void Growing_a_box_moves_every_edge_out()
    {
        var grown = Booleans.Grow(One(Box(0, 0, 100, 100)), 10);

        Assert.Single(grown);
        Assert.Equal(120 * 120, Area(grown));
    }

    [Fact]
    public void Shrinking_a_box_moves_every_edge_in()
    {
        var shrunk = Booleans.Grow(One(Box(0, 0, 100, 100)), -10);

        Assert.Single(shrunk);
        Assert.Equal(80 * 80, Area(shrunk));
    }

    ///<summary>
    ///Shrinking something away entirely is not an error - it is how a minimum-width rule is written. A
    ///shape narrower than twice the distance has nothing left of it.
    ///</summary>
    [Fact]
    public void Shrinking_past_the_middle_leaves_nothing()
    {
        Assert.Empty(Booleans.Grow(One(Box(0, 0, 100, 10)), -20));
    }

    ///<summary>
    ///The pair that makes a spacing check. Two shapes 40 apart, each grown by 25, now touch - and the
    ///intersection is what says so.
    ///</summary>
    [Fact]
    public void Growing_then_intersecting_answers_how_close_two_shapes_are()
    {
        var left = One(Box(0, 0, 100, 100));
        var right = One(Box(140, 0, 240, 100));

        Assert.Empty(Booleans.Combine(Booleans.Grow(left, 15), Booleans.Grow(right, 15), BooleanOperation.And));
        Assert.NotEmpty(Booleans.Combine(Booleans.Grow(left, 25), Booleans.Grow(right, 25), BooleanOperation.And));
    }

    [Fact]
    public void Growing_by_nothing_merges_and_changes_no_area()
    {
        Assert.Equal(100 * 100, Area(Booleans.Grow(One(Box(0, 0, 100, 100)), 0)));
    }

    #endregion ***********************************************************************



    #region Against a real file ******************************************************

    ///<summary>
    ///Merging a real layer cannot make it bigger, and for a layer whose shapes overlap it makes it
    ///smaller. Mosfet.gds is drawn with overlapping geometry on several layers, which is what a flattened
    ///hierarchy looks like everywhere.
    ///</summary>
    [Fact]
    public void Merging_a_real_layer_never_grows_it()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var layout = GdsFlattener.Flatten(gds);

        foreach (var layer in layout.Elements.Where(each => each.Text is null).GroupBy(each => each.Layer.Key))
        {
            var shapes = layer.Select(element => (IReadOnlyList<Element.Point>)element.Points).ToList();

            double before = Area(shapes);
            double after = Area(Booleans.Merge(shapes));

            Assert.True(after <= before + 1, $"{layer.Key} grew from {before} to {after} when merged");
        }
    }

    ///<summary>
    ///And a layer merged twice is the same as merged once, which is the property that says the output is
    ///something the engine can read back - keyholes included.
    ///</summary>
    [Fact]
    public void Merging_a_real_layer_twice_changes_nothing()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var layout = GdsFlattener.Flatten(gds);

        foreach (var layer in layout.Elements.Where(each => each.Text is null).GroupBy(each => each.Layer.Key))
        {
            var once = Booleans.Merge(layer.Select(element => (IReadOnlyList<Element.Point>)element.Points));
            var twice = Booleans.Merge(once);

            Assert.Equal(Area(once), Area(twice), 6);
        }
    }

    #endregion ***********************************************************************



    #region Merging a layer for the renderers ****************************************

    private static Element OnLayer(short number, List<Element.Point> points)
    {
        return new Element { Layer = new Layer(new LayerKey(number, 0), "#ffffff"), Points = points };
    }

    ///<summary>
    ///Each layer separately, and nothing crossing between them. Two shapes on different layers overlap in
    ///plan and sit at different heights, so merging them would weld a via to the metal above it.
    ///</summary>
    [Fact]
    public void Layers_are_merged_one_at_a_time()
    {
        var elements = new List<Element> { OnLayer(65, A), OnLayer(65, B), OnLayer(66, A) };

        var merged = Booleans.MergeByLayer(elements);

        Assert.Equal(2, merged.Count);
        Assert.Single(merged, outline => outline.Layer.Key.Number == 65);
        Assert.Single(merged, outline => outline.Layer.Key.Number == 66);
    }

    ///<summary>
    ///The layer object itself comes through, not a copy of it. The renderers read the color, the height
    ///and the thickness off it, and all three are changed from the settings popup while this geometry
    ///stays as it is - so a copy would freeze a layer's appearance at the moment it was merged.
    ///</summary>
    [Fact]
    public void The_merged_shape_still_points_at_the_layer_it_came_from()
    {
        var element = OnLayer(65, A);

        var merged = Booleans.MergeByLayer(new List<Element> { element });

        Assert.Same(element.Layer, merged.Single().Layer);
    }

    ///<summary>Labels are an anchor and a string. There is nothing about one to merge.</summary>
    [Fact]
    public void Labels_are_left_out_of_the_merge()
    {
        var label = OnLayer(65, new List<Element.Point> { new Element.Point { X = 5, Y = 5 } });
        label.Text = "A";

        Assert.Empty(Booleans.MergeByLayer(new List<Element> { label }));
    }

    ///<summary>
    ///A hole stays a hole here rather than becoming a keyhole.
    ///
    ///Four bars in a ring, which is how a hole turns up in a real layer - nobody draws one, it falls out
    ///of shapes that surround something. What the renderers get is an outline and the hole in it, because
    ///a channel whose two edges lie on top of each other is the case a triangulator handles worst.
    ///</summary>
    [Fact]
    public void A_hole_stays_a_hole_for_the_renderers()
    {
        var ring = new List<Element>
        {
            OnLayer(65, Box(0, 0, 100, 20)),
            OnLayer(65, Box(0, 80, 100, 100)),
            OnLayer(65, Box(0, 0, 20, 100)),
            OnLayer(65, Box(80, 0, 100, 100))
        };

        var merged = Booleans.MergeByLayer(ring);

        var outline = Assert.Single(merged);

        Assert.Single(outline.Holes);
        Assert.Equal(100 * 100, Area(new[] { outline.Boundary }));
        Assert.Equal(60 * 60, Area(outline.Holes));

        //And no corner is visited twice, which is what a keyhole would look like.
        Assert.Equal(outline.Boundary.Count, outline.Boundary.Select(p => (p.X, p.Y)).Distinct().Count());
    }

    ///<summary>
    ///And the bundled corpus really does produce them, so the path is exercised by something other than
    ///a test that was written to exercise it.
    ///</summary>
    [Fact]
    public void Some_bundled_file_merges_into_a_shape_with_a_hole()
    {
        foreach (string path in GdsTestData.AllSampleFiles())
        {
            var layout = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(path)));

            if (Booleans.MergeByLayer(layout.Elements).Any(outline => outline.Holes.Count > 0))
                return;
        }

        Assert.Fail("No bundled file merges into a shape with a hole, so nothing here covers that path.");
    }

    #endregion ***********************************************************************



    #region Against a second engine **************************************************

    ///<summary>
    ///The gate of a transistor, computed twice.
    ///
    ///`poly AND diff` is not an example chosen to be convenient - it is how a PDK defines where a
    ///transistor is, and it is the operation this whole thing exists for. KLayout has its own boolean
    ///engine and no shared code with Clipper, so agreeing with it says something that a test written
    ///against our own output cannot.
    ///
    ///Areas rather than corners: the two engines are free to walk a ring from a different corner or to
    ///split a result differently, and neither is a difference in what is covered.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void The_gate_comes_out_the_same_area_as_klayout_makes_it()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var layout = GdsFlattener.Flatten(gds);

        var poly = shapesOn(layout, new LayerKey(66, 20));
        var diff = shapesOn(layout, new LayerKey(65, 20));

        double mine = Area(Booleans.Combine(poly, diff, BooleanOperation.And));
        double theirs = OasisTestData.RegionArea(GdsTestData.MosfetSample, 66, 20, "&", 65, 20);

        Assert.True(mine > 0, "the gate came out empty, so this is not comparing anything");

        //Within a database unit squared per corner, which is all the rounding either side can introduce.
        Assert.Equal(theirs, mine, 0);
    }

    ///<summary>The other three, against the same engine.</summary>
    [Theory]
    [InlineData(BooleanOperation.Or, "|")]
    [InlineData(BooleanOperation.Not, "-")]
    [InlineData(BooleanOperation.Xor, "^")]
    [Trait("Needs", "KLayout")]
    public void Each_operation_comes_out_the_same_area_as_klayout_makes_it(BooleanOperation operation, string theirOperator)
    {
        Assert.True(OasisTestData.Available, "KLayout is needed as the second engine here.");

        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var layout = GdsFlattener.Flatten(gds);

        double mine = Area(Booleans.Combine(
            shapesOn(layout, new LayerKey(66, 20)),
            shapesOn(layout, new LayerKey(65, 20)),
            operation));

        double theirs = OasisTestData.RegionArea(GdsTestData.MosfetSample, 66, 20, theirOperator, 65, 20);

        Assert.Equal(theirs, mine, 0);
    }

    private static List<IReadOnlyList<Element.Point>> shapesOn(FlattenedLayout layout, LayerKey key)
    {
        return layout.Elements
            .Where(element => element.Text is null && element.Layer.Key.Equals(key))
            .Select(element => (IReadOnlyList<Element.Point>)element.Points)
            .ToList();
    }

    #endregion ***********************************************************************
}
