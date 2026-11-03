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



    #region Holes kept as holes ******************************************************

    ///<summary>The ground some rings cover: each boundary, less the holes in it.</summary>
    private static double Area(IEnumerable<Booleans.Ring> rings)
    {
        double total = 0;

        foreach (var ring in rings)
        {
            total += Area(One(ring.Boundary));

            foreach (var hole in ring.Holes)
                total -= Area(One(hole));
        }

        return total;
    }

    ///<summary>The donut again, as a boundary and its hole rather than one keyholed outline.</summary>
    private static List<Booleans.Ring> DonutRings()
    {
        return Booleans.Combine(
            Booleans.AsRings(One(Box(0, 0, 100, 100))),
            Booleans.AsRings(One(Box(40, 40, 60, 60))),
            BooleanOperation.Not);
    }

    [Fact]
    public void Rings_hand_a_hole_back_as_a_hole()
    {
        var ring = Assert.Single(DonutRings());

        Assert.Single(ring.Holes);
        Assert.Equal((100 * 100) - (20 * 20), Area(new[] { ring }));
    }

    ///<summary>Shapes handed over as rings are neither merged nor changed: one ring each, as given.</summary>
    [Fact]
    public void Shapes_handed_over_as_rings_are_one_ring_each()
    {
        var rings = Booleans.AsRings(new[] { A, B });

        Assert.Equal(2, rings.Count);
        Assert.Equal(A, rings[0].Boundary);
        Assert.Equal(B, rings[1].Boundary);
        Assert.All(rings, ring => Assert.Empty(ring.Holes));
    }

    ///<summary>The same offset moves a hole's edges the other way, so growing the ground closes the hole in.</summary>
    [Fact]
    public void Growing_rings_closes_a_hole_in_from_every_side()
    {
        var ring = Assert.Single(Booleans.Grow(DonutRings(), 5));

        Assert.Equal(10 * 10, Area(One(Assert.Single(ring.Holes))));
        Assert.Equal((110 * 110) - (10 * 10), Area(new[] { ring }));
    }

    [Fact]
    public void Shrinking_rings_opens_a_hole_out_on_every_side()
    {
        var ring = Assert.Single(Booleans.Grow(DonutRings(), -5));

        Assert.Equal(30 * 30, Area(One(Assert.Single(ring.Holes))));
        Assert.Equal((90 * 90) - (30 * 30), Area(new[] { ring }));
    }

    ///<summary>
    ///Why the ring operations exist, beside the speed: shrunk, a keyhole's two sides move apart. The channel
    ///runs from the hole out to the right-hand edge, so the same donut shrunk as one keyholed outline comes back
    ///with a gap ten wide cut from the hole to the outside - its hole no longer a hole, and 300 short of the
    ///ground the rings shrink into.
    ///</summary>
    [Fact]
    public void Shrinking_a_keyholed_outline_opens_its_channel_too()
    {
        var shrunk = Booleans.Grow(Donut(), -5);

        Assert.Equal((90 * 90) - (30 * 30) - (30 * 10), Area(shrunk));
        Assert.Empty(Assert.Single(Booleans.MergeToRings(shrunk)).Holes);
    }

    ///<summary>
    ///Wound by what it is rather than as it came: a hole running the same way as its boundary is still a
    ///hole, rather than ground counted twice.
    ///</summary>
    [Fact]
    public void A_hole_wound_the_same_way_as_its_boundary_is_still_a_hole()
    {
        var ring = new Booleans.Ring
        {
            Boundary = Box(0, 0, 100, 100),
            Holes = new List<List<Element.Point>> { Box(40, 40, 60, 60) }
        };

        Assert.Equal((100 * 100) - (20 * 20), Area(Booleans.Merge(new[] { ring })));
    }

    ///<summary>
    ///Rings keyholed are exactly what a merge would have handed back, corner for corner - which is what lets
    ///a layer be kept as rings and still read back as the same shapes it always did.
    ///</summary>
    [Fact]
    public void Keyholing_merged_rings_gives_exactly_what_merging_gives()
    {
        var shapes = new List<List<Element.Point>>
        {
            Box(0, 0, 100, 100),
            Box(110, 0, 210, 100)
        };

        var holes = new List<List<Element.Point>> { Box(10, 40, 30, 60), Box(70, 40, 90, 60), Box(150, 40, 170, 60) };
        var island = Box(15, 45, 25, 55);

        var drawn = Booleans.Combine(shapes, holes, BooleanOperation.Not).Concat(One(island)).ToList();

        Assert.Equal(Booleans.Merge(drawn), Booleans.Keyholed(Booleans.MergeToRings(drawn)));

        var layout = GdsFlattener.Flatten(new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample)));

        foreach (var layer in layout.Elements.Where(each => each.Text is null).GroupBy(each => each.Layer.Key))
        {
            var points = layer.Select(element => (IReadOnlyList<Element.Point>)element.Points).ToList();

            Assert.Equal(Booleans.Merge(points), Booleans.Keyholed(Booleans.MergeToRings(points)));
        }
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

    ///
    ///**A lone rectangle comes out exactly as a union of it does**, corner for corner and from the same corner.
    ///
    ///The merge writes those out itself rather than sweeping each one - see unitedRectangle - and what makes
    ///that safe is that it is not a second opinion about what a union makes. So this asks Clipper, for a
    ///rectangle started at each of its corners and run both ways, the way the merge hands it a ring: turned
    ///counter-clockwise first.
    ///
    [Fact]
    public void A_lone_rectangle_comes_out_exactly_as_a_union_of_it_does()
    {
        foreach (var box in new[] { Box(0, 0, 100, 40), Box(-500, -70, -20, 900) })
        {
            for (int start = 0; start < 4; start++)
            {
                foreach (bool backwards in new[] { false, true })
                {
                    var corners = new List<Element.Point>();

                    for (int k = 0; k < 4; k++)
                    {
                        int at = (start + k) % 4;

                        if (backwards)
                            at = (start - k + 4) % 4;

                        corners.Add(box[at]);
                    }

                    var ring = new Clipper2Lib.Path64(corners.Select(point => new Clipper2Lib.Point64(point.X, point.Y)));

                    if (!Clipper2Lib.Clipper.IsPositive(ring))
                        ring.Reverse();

                    var tree = new Clipper2Lib.PolyTree64();

                    Clipper2Lib.Clipper.BooleanOp(Clipper2Lib.ClipType.Union, new Clipper2Lib.Paths64 { ring }, null, tree, Clipper2Lib.FillRule.NonZero);

                    var expected = tree[0].Polygon!.Select(point => (point.X, point.Y)).ToList();

                    var merged = Assert.Single(Booleans.MergeByLayer(new List<Element> { OnLayer(65, corners) }));

                    Assert.Equal(expected, merged.Boundary.Select(point => ((long)point.X, (long)point.Y)).ToList());
                    Assert.Empty(merged.Holes);
                }
            }
        }
    }

    ///
    ///**A contact drawn twice where two cells meet comes out once**, as the rectangle a union of it alone makes.
    ///
    ///A placed design is full of these - the same contact from each of two abutting cells, on top of itself - and
    ///the merge writes out a group one of its own rectangles covers rather than sweeping it; see
    ///coveringRectangle. Drawn the second time from another corner and the other way round, the way a reflected
    ///placement would.
    ///
    [Fact]
    public void A_contact_drawn_twice_on_top_of_itself_comes_out_once()
    {
        var once = Box(10, 20, 40, 50);
        var again = new List<Element.Point> { once[2], once[1], once[0], once[3] };

        var merged = Assert.Single(Booleans.MergeByLayer(new List<Element> { OnLayer(67, once), OnLayer(67, again) }));
        var alone = Assert.Single(Booleans.MergeByLayer(new List<Element> { OnLayer(67, once) }));

        Assert.Equal(alone.Boundary.Select(point => (point.X, point.Y)), merged.Boundary.Select(point => (point.X, point.Y)));
        Assert.Empty(merged.Holes);
    }

    ///
    ///And a rectangle with shapes inside it comes out as the rectangle - one touching its side from within, one
    ///that is not a rectangle at all - while one that only spans it one way does not cover the group, and the
    ///group is swept: the same ground, the strip that sticks out included.
    ///
    [Fact]
    public void A_rectangle_with_shapes_inside_it_comes_out_as_the_rectangle()
    {
        var outer = Box(0, 0, 100, 60);
        var touching = Box(0, 10, 30, 20);

        var triangle = new List<Element.Point>
        {
            new Element.Point { X = 50, Y = 10 },
            new Element.Point { X = 90, Y = 10 },
            new Element.Point { X = 70, Y = 50 }
        };

        var covered = Assert.Single(Booleans.MergeByLayer(new List<Element> { OnLayer(68, touching), OnLayer(68, outer), OnLayer(68, triangle) }));

        Assert.Equal(4, covered.Boundary.Count);
        Assert.Equal(100 * 60, Area(new[] { covered.Boundary }));

        //As wide as the group and not as tall, so it covers it across and not up.
        var across = Assert.Single(Booleans.MergeByLayer(new List<Element> { OnLayer(68, outer), OnLayer(68, Box(40, 50, 60, 90)) }));

        Assert.Equal((100 * 60) + (20 * 30), Area(new[] { across.Boundary }));
    }

    ///
    ///Shapes are merged in groups that touch, so a chain that only touches end to end has to come out as one
    ///outline however its links are ordered - the last link joins the first only through every one between.
    ///
    [Fact]
    public void A_chain_of_shapes_touching_end_to_end_is_one_outline()
    {
        var links = new List<Element>();

        foreach (int at in new[] { 3, 0, 4, 1, 2 })
            links.Add(OnLayer(65, Box(at * 100, 0, (at + 1) * 100, 30)));

        var outline = Assert.Single(Booleans.MergeByLayer(links));

        Assert.Equal(500 * 30, Area(new[] { outline.Boundary }));
    }

    ///
    ///An island in the hole of a ring of four bars touches none of them, so it is merged on its own - and has
    ///to come out the way a merge of everything together leaves it: the ring with its hole, and the island.
    ///
    [Fact]
    public void An_island_in_a_ring_is_merged_on_its_own_and_the_ring_keeps_its_hole()
    {
        var shapes = new List<Element>
        {
            OnLayer(65, Box(0, 0, 100, 20)),
            OnLayer(65, Box(40, 40, 60, 60)),
            OnLayer(65, Box(0, 80, 100, 100)),
            OnLayer(65, Box(0, 0, 20, 100)),
            OnLayer(65, Box(80, 0, 100, 100))
        };

        var merged = Booleans.MergeByLayer(shapes);

        Assert.Equal(2, merged.Count);

        var ring = Assert.Single(merged, outline => outline.Holes.Count == 1);
        var island = Assert.Single(merged, outline => outline.Holes.Count == 0);

        Assert.Equal((100 * 100) - (60 * 60), Area(new[] { ring.Boundary }) - Area(ring.Holes));
        Assert.Equal(20 * 20, Area(new[] { island.Boundary }));
    }

    ///
    ///**Merged in groups, a layer comes out as merging it whole does**, on every bundled cell: the same
    ///outlines, each with the same box, area and holes.
    ///
    ///What could go wrong is a group split where it should not be - two shapes that meet put in different
    ///groups come out as two overlapping outlines, which is the flicker the merge is for - or a hole lost on
    ///the way. The whole-layer side is MergeToRings, which is still one union, and the outlines are compared
    ///by what they cover rather than corner by corner: which corners in the middle of a straight side a union
    ///keeps depends on what else is in the sweep with it.
    ///
    [Fact]
    public void Merging_a_layer_in_groups_covers_what_merging_it_whole_does()
    {
        int compared = 0;

        foreach (string path in GdsTestData.CellSampleFiles())
        {
            var layout = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(path)));
            var grouped = Booleans.MergeByLayer(layout.Elements);

            foreach (var layer in layout.Elements.Where(element => element.Text is null && !element.IsOpen).GroupBy(element => element.Layer.Key))
            {
                var whole = Booleans.MergeToRings(layer.Select(element => (IReadOnlyList<Element.Point>)element.Points))
                    .Select(ring => SignatureOf(ring.Boundary, ring.Holes))
                    .OrderBy(one => one)
                    .ToList();

                var apart = grouped
                    .Where(outline => outline.Layer.Key.Equals(layer.Key))
                    .Select(outline => SignatureOf(outline.Boundary, outline.Holes))
                    .OrderBy(one => one)
                    .ToList();

                Assert.True(whole.SequenceEqual(apart), $"{Path.GetFileName(path)} {layer.Key}: merged in groups, the layer does not cover what it does merged whole");

                compared++;
            }
        }

        Assert.True(compared > 1000, $"only {compared} layers were compared, so the corpus did not load");
    }

    ///
    ///The same against shapes made to be awkward for the grouping, from a fixed seed: four boxes over so much
    ///of the layer that they are set aside and compared with everything, four hundred rails running most of
    ///the way across it, and a scatter of small boxes - see apart in Booleans.
    ///
    ///Four thousand of them, which makes the grid thirty-two buckets a side, and a box sixteen thousand units
    ///across in forty thousand covers more than the hundred and twenty-eight buckets that sets one aside.
    ///
    [Fact]
    public void Merging_a_scatter_of_boxes_in_groups_covers_what_merging_it_whole_does()
    {
        var random = new Random(20261008);
        var shapes = new List<Element.Point>[4000];

        for (int i = 0; i < shapes.Length; i++)
        {
            int x = random.Next(0, 40000);
            int y = random.Next(0, 40000);

            if (i < 4)
                shapes[i] = Box(x / 2, y / 2, (x / 2) + random.Next(16000, 20000), (y / 2) + random.Next(16000, 20000));
            else if (i < 404)
                shapes[i] = Box(x / 4, y, (x / 4) + random.Next(20000, 30000), y + random.Next(20, 120));
            else
                shapes[i] = Box(x, y, x + random.Next(30, 400), y + random.Next(30, 400));
        }

        var grouped = Booleans.MergeByLayer(shapes.Select(shape => OnLayer(65, shape)).ToList())
            .Select(outline => SignatureOf(outline.Boundary, outline.Holes))
            .OrderBy(one => one)
            .ToList();

        var whole = Booleans.MergeToRings(shapes)
            .Select(ring => SignatureOf(ring.Boundary, ring.Holes))
            .OrderBy(one => one)
            .ToList();

        Assert.True(whole.Count > 20, $"the scatter merged into {whole.Count} outline(s), which is not much of a test of grouping");
        Assert.Equal(whole, grouped);
    }

    ///<summary>What an outline covers, for comparing two merges of it: its box, its area and its holes'.</summary>
    private static string SignatureOf(IReadOnlyList<Element.Point> boundary, IEnumerable<IReadOnlyList<Element.Point>> holes)
    {
        var box = Bounds.Of(boundary);
        var gaps = holes.ToList();

        return FormattableString.Invariant($"{box.Left},{box.Bottom},{box.Right},{box.Top} {Area(new[] { boundary })} {gaps.Count} {Area(gaps)}");
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
