using GdsII;

namespace GDSViewer.Tests;

///<summary>
///The grid a layout's elements are filed in, so that a question about one place reads only that place.
///
///**Everything here is held to the walk it replaced.** <see cref="LayoutIndex"/> exists to give exactly the
///answer a walk of every element gave, from fewer of them - a picture drawn from it, a click answered by it
///and a band caught by it are only right if nothing is missed and nothing is added, and in the same order.
///So most of these build a layout, ask both, and compare: random shapes and random questions, many times
///over, with the awkward cases - a shape over the whole layout, one exactly on a cell's edge, a label, a
///point - made sure of rather than left to chance.
///</summary>
public class LayoutIndexTests
{
    #region Layouts built by hand ****************************************************

    private static readonly Layer Metal = new Layer(new LayerKey(68, 20), "#0000ff");

    private static readonly Layer Poly = new Layer(new LayerKey(66, 20), "#ff0000");

    private static Element Box(int left, int bottom, int right, int top, Layer layer)
    {
        return new Element
        {
            Layer = layer,
            Points = new List<Element.Point>
            {
                new Element.Point(left, bottom),
                new Element.Point(right, bottom),
                new Element.Point(right, top),
                new Element.Point(left, top),
                new Element.Point(left, bottom)
            }
        };
    }

    private static Element Label(int x, int y, Layer layer)
    {
        return new Element
        {
            Layer = layer,
            Text = "A",
            Points = new List<Element.Point> { new Element.Point(x, y) }
        };
    }

    ///
    ///A layout of a few thousand shapes of very different sizes, with labels and a few that span all of it -
    ///the mix a real chip has: rows of small things, long wires, and a ring round the edge.
    ///
    private static List<Element> Scattered(int seed, int count = 3000)
    {
        var random = new Random(seed);
        var elements = new List<Element>();

        for (int i = 0; i < count; i++)
        {
            int left = random.Next(-50_000, 50_000);
            int bottom = random.Next(-50_000, 50_000);
            int kind = random.Next(20);

            if (kind == 0)
            {
                elements.Add(Label(left, bottom, Metal));

                continue;
            }

            int width = random.Next(0, 400);
            int height = random.Next(0, 400);

            //Now and then a long wire, and once in a while something over the whole of it.
            if (kind == 1)
                width = random.Next(5_000, 60_000);

            if (kind == 2 && random.Next(10) == 0)
            {
                left = -60_000;
                bottom = -60_000;
                width = 120_000;
                height = 120_000;
            }

            Layer layer = Metal;

            if (random.Next(2) == 0)
                layer = Poly;

            elements.Add(Box(left, bottom, left + width, bottom + height, layer));
        }

        return elements;
    }

    private static Bounds RandomBox(Random random)
    {
        int left = random.Next(-70_000, 70_000);
        int bottom = random.Next(-70_000, 70_000);

        return new Bounds(left, bottom, left + random.Next(0, 40_000), bottom + random.Next(0, 40_000));
    }

    ///<summary>The walk the index replaced, as the reference every answer is held to.</summary>
    private static List<int> Walked(List<Element> elements, Bounds box, long smallest, bool withLabels)
    {
        var found = new List<int>();

        for (int i = 0; i < elements.Count; i++)
        {
            var element = elements[i];

            if (element.Text is not null && !withLabels)
                continue;

            if (element.Text is null && smallest > 0 && element.Box.Area < smallest)
                continue;

            if (box.Intersects(element.Box))
                found.Add(i);
        }

        return found;
    }

    #endregion ***********************************************************************



    #region The same answer as the walk ***********************************************

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_box_finds_exactly_what_a_walk_finds(int seed)
    {
        var elements = Scattered(seed);
        var index = new LayoutIndex(elements);
        var random = new Random(seed * 31);

        for (int i = 0; i < 300; i++)
        {
            var box = RandomBox(random);

            Assert.Equal(Walked(elements, box, 0, true), index.Within(box));
        }
    }

    ///<summary>
    ///With a smallest size, which is the other of the two ways to an answer - walking the shapes largest
    ///first - and the one taken when the box covers most of the layout. Both have to agree with the walk.
    ///</summary>
    [Theory]
    [InlineData(4, 100)]
    [InlineData(5, 10_000)]
    [InlineData(6, 1_000_000)]
    public void Leaving_out_the_small_finds_exactly_what_a_walk_finds(int seed, long smallest)
    {
        var elements = Scattered(seed);
        var index = new LayoutIndex(elements);
        var random = new Random(seed * 17);

        for (int i = 0; i < 200; i++)
        {
            var box = RandomBox(random);
            bool withLabels = random.Next(2) == 0;

            Assert.Equal(Walked(elements, box, smallest, withLabels), index.Within(box, smallest, withLabels));
        }

        //And the box over everything, which is the fit, and is where the largest-first walk is taken.
        var everything = new Bounds(-100_000, -100_000, 100_000, 100_000);

        Assert.Equal(Walked(elements, everything, smallest, true), index.Within(everything, smallest, true));
        Assert.Equal(Walked(elements, everything, smallest, false), index.Within(everything, smallest, false));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void A_point_finds_exactly_the_boxes_a_walk_finds(int seed)
    {
        var elements = Scattered(seed);
        var index = new LayoutIndex(elements);
        var random = new Random(seed * 13);

        for (int i = 0; i < 500; i++)
        {
            var point = new Element.Point(random.Next(-70_000, 70_000), random.Next(-70_000, 70_000));
            var walked = new List<int>();

            for (int k = 0; k < elements.Count; k++)
            {
                if (elements[k].Box.Contains(point))
                    walked.Add(k);
            }

            Assert.Equal(walked, index.Under(point));
        }
    }

    ///<summary>
    ///A point on the corner of every shape that has one, which is the case most likely to fall between two
    ///cells: an edge is where one cell stops and the next begins.
    ///</summary>
    [Fact]
    public void Every_corner_finds_the_shape_it_belongs_to()
    {
        var elements = Scattered(9, 1500);
        var index = new LayoutIndex(elements);

        for (int i = 0; i < elements.Count; i++)
        {
            foreach (var corner in elements[i].Points)
                Assert.Contains(i, index.Under(corner));
        }
    }

    #endregion ***********************************************************************



    #region What it knows about the layout as a whole **********************************

    [Fact]
    public void The_largest_shape_and_the_extent_are_the_layouts_own()
    {
        var elements = new List<Element>
        {
            Box(0, 0, 10, 10, Metal),
            Label(-500, 900, Metal),
            Box(100, 100, 400, 200, Poly)
        };

        var index = new LayoutIndex(elements);

        Assert.Equal(300L * 100, index.LargestArea);
        Assert.Equal(new Bounds(-500, 0, 400, 900), index.Extent);

        //The label is an anchor with a name on it, and says nothing about how big the drawing is.
        Assert.Equal(new Bounds(0, 0, 400, 200), index.ShapeExtent);
    }

    [Fact]
    public void The_layers_in_use_come_in_the_order_each_first_appears()
    {
        var elements = new List<Element>
        {
            Label(0, 0, Poly),
            Box(0, 0, 10, 10, Metal),
            Box(0, 0, 10, 10, Poly),
            Box(0, 0, 10, 10, Metal)
        };

        var index = new LayoutIndex(elements);

        //Metal first: the label on poly came before it, and a label is not a shape.
        Assert.Equal(new[] { Metal.Key, Poly.Key }, index.ShapeLayers.Select(layer => layer.Key));
    }

    [Fact]
    public void The_extent_of_some_layers_is_theirs_alone()
    {
        var elements = new List<Element>
        {
            Box(0, 0, 10, 10, Metal),
            Box(1000, 1000, 1010, 1010, Poly)
        };

        var index = new LayoutIndex(elements);

        Assert.Equal(new Bounds(0, 0, 10, 10), index.ExtentOf(new HashSet<LayerKey> { Metal.Key }));
        Assert.Equal(new Bounds(0, 0, 1010, 1010), index.ExtentOf(new HashSet<LayerKey> { Metal.Key, Poly.Key }));
        Assert.True(index.ExtentOf(new HashSet<LayerKey>()).IsEmpty);
    }

    [Fact]
    public void An_empty_layout_answers_every_question_with_nothing()
    {
        var index = new LayoutIndex(new List<Element>());

        Assert.Empty(index.Within(new Bounds(-10, -10, 10, 10)));
        Assert.Empty(index.Under(new Element.Point(0, 0)));
        Assert.Equal(0, index.LargestArea);
        Assert.True(index.Extent.IsEmpty);
    }

    ///<summary>A layout that is one long line, which a square grid would give millions of cells to.</summary>
    [Fact]
    public void A_layout_that_is_one_long_line_is_still_indexed()
    {
        var elements = new List<Element>();

        for (int i = 0; i < 5000; i++)
            elements.Add(Box(i * 400_000, 0, (i * 400_000) + 10, 10, Metal));

        var index = new LayoutIndex(elements);
        var box = new Bounds(400_000 * 10, 0, 400_000 * 12, 10);

        Assert.Equal(new List<int> { 10, 11, 12 }, index.Within(box));
    }

    #endregion ***********************************************************************



    #region Held by the layout it indexes ********************************************

    [Fact]
    public void A_layout_keeps_its_index_until_its_list_changes()
    {
        var layout = new FlattenedLayout { Elements = new List<Element> { Box(0, 0, 10, 10, Metal) } };

        var first = layout.Index;

        Assert.Same(first, layout.Index);

        layout.Elements.Add(Box(100, 100, 110, 110, Metal));

        Assert.NotSame(first, layout.Index);
        Assert.Equal(new List<int> { 1 }, layout.Index.Within(new Bounds(90, 90, 120, 120)));

        //And a list swapped for another of the same length is a different layout.
        var second = layout.Index;

        layout.Elements = new List<Element> { Box(0, 0, 10, 10, Metal), Box(500, 500, 510, 510, Metal) };

        Assert.NotSame(second, layout.Index);
        Assert.Equal(new List<int> { 1 }, layout.Index.Within(new Bounds(490, 490, 520, 520)));
    }

    #endregion ***********************************************************************



    #region What it is for ************************************************************

    ///<summary>
    ///The picture a view draws from the index is the picture a walk drew, character for character.
    ///
    ///Against a layout whose elements are dropped by the window and by the smallest size both, so neither
    ///rule can be quietly skipped. The reference is the same call with no window and a layout holding only
    ///what the walk keeps - which would give the subpaths different numbers, so the numbers are compared by
    ///what they point at.
    ///</summary>
    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void A_window_draws_the_same_shapes_a_walk_would(int seed)
    {
        var elements = Scattered(seed, 2000);
        var layout = new FlattenedLayout { Elements = elements };
        var visible = new HashSet<LayerKey> { Metal.Key, Poly.Key };
        var random = new Random(seed);

        for (int i = 0; i < 20; i++)
        {
            var window = RandomBox(random);
            long smallest = random.Next(0, 3) * 5_000L;

            string svg = SvgWriter.Build(layout, visible, 0.5f, visible, null, window, smallest);

            var drawn = System.Text.RegularExpressions.Regex.Matches(svg, "data-elements=\"([0-9 ]*)\"")
                .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Concat(System.Text.RegularExpressions.Regex.Matches(svg, "data-element=\"([0-9]+)\"").Select(match => match.Groups[1].Value))
                .Select(int.Parse)
                .Order()
                .ToList();

            Assert.Equal(Walked(elements, window, smallest, true), drawn);
        }
    }

    ///<summary>The picking rules, asked of the index, against the rules asked of every element.</summary>
    [Fact]
    public void A_click_and_a_band_land_where_a_walk_would()
    {
        var elements = Scattered(12, 2000);
        var layout = new FlattenedLayout { Elements = elements };
        var random = new Random(12);

        for (int i = 0; i < 300; i++)
        {
            var point = new Element.Point(random.Next(-60_000, 60_000), random.Next(-60_000, 60_000));
            int walked = -1;

            for (int k = 0; k < elements.Count; k++)
            {
                if (Picking.Covers(elements[k], point))
                    walked = k;
            }

            Assert.Equal(walked, Picking.At(layout, point));
        }

        for (int i = 0; i < 100; i++)
        {
            var band = RandomBox(random);

            Assert.Equal(Walked(elements, band, 0, true), Picking.Caught(layout, band));
        }
    }

    #endregion ***********************************************************************
}
