using GdsII;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///A layout brought up to date after an edit that changed shapes where they stand, rather than flattened again.
///
///**Held to the flatten it stands in for, element by element.** <see cref="GdsFlattener.Reflatten"/> exists so
///that moving a shape costs what that shape costs rather than what the library costs - which on a real chip
///was most of a twenty-second drag - and the one thing that makes it safe is that the answer is the same. So
///every edit here is made to a real design, in a cell placed many times over, and what comes back is compared
///with a flatten of the library as it now stands: the same elements, in the same order, with the same corners.
///
///And its index with one built from nothing, since the index it carries is the old grid with the changed
///elements set aside - see <see cref="LayoutIndex.After"/>.
///
public class ReflattenTests
{
    #region Helpers *******************************************************************

    ///<summary>Twenty-one SkullFET inverters in a ring: a small design whose cells are placed over and over.</summary>
    private static GDS Bones()
    {
        return new GDS(GdsTestData.ReadSample(Path.Combine("Sky130 Designs", "tt_um_oscillating_bones.gds")));
    }

    private static void AssertSameLayout(FlattenedLayout expected, FlattenedLayout actual)
    {
        Assert.Equal(expected.Elements.Count, actual.Elements.Count);

        for (int i = 0; i < expected.Elements.Count; i++)
        {
            var wanted = expected.Elements[i];
            var got = actual.Elements[i];

            Assert.Equal(wanted.Layer.Key, got.Layer.Key);
            Assert.Equal(wanted.Text, got.Text);
            Assert.Equal(wanted.IsOpen, got.IsOpen);
            Assert.Equal(wanted.Points, got.Points);
            Assert.Same(wanted.Source!.Model, got.Source!.Model);
            Assert.Equal(wanted.Source.Placement, got.Source.Placement);
        }
    }

    ///<summary>
    ///A drawn element of the kind asked for, in a cell the layout draws more than once - so an edit to it
    ///lands on every copy, which is the case worth getting right.
    ///</summary>
    private static (StructureModel Structure, ElementModel Model) InAPlacedCell(GDS gds, FlattenedLayout layout, Func<Element, bool> kind)
    {
        var copies = layout.Elements
            .Where(element => element.Source is not null && kind(element))
            .GroupBy(element => element.Source!.Model)
            .OrderByDescending(group => group.Count())
            .First();

        var source = copies.First().Source!;
        var structure = gds.StreamFormat.Structures.First(each => (each.STRNAME?.Data as AsciiData)?.Value == source.Structure);

        Assert.True(copies.Count() > 1, "the design should place the cell more than once");

        return (structure, source.Model);
    }

    private static bool AShape(Element element)
    {
        return element.Text is null && !element.IsOpen && element.Points.Count >= 4;
    }

    private static FlattenedLayout Reflattened(FlattenedLayout before, GDS gds, EditHistory history)
    {
        var after = GdsFlattener.Reflatten(before, gds, history.LastStep!.Reshaped);

        Assert.NotNull(after);

        return after!;
    }

    #endregion ***********************************************************************



    #region The same as a flatten *****************************************************

    [Fact]
    public void A_move_comes_out_as_a_flatten_would_draw_it()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();

        history.Do(new MoveElement(structure, model, 130, -70));

        AssertSameLayout(GdsFlattener.Flatten(gds), Reflattened(before, gds, history));
    }

    [Fact]
    public void Taking_it_back_comes_out_as_it_was()
    {
        var gds = Bones();
        var original = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, original, AShape);
        var history = new EditHistory();

        history.Do(new MoveElement(structure, model, 130, -70));

        var moved = Reflattened(original, gds, history);

        history.Undo();

        AssertSameLayout(original, Reflattened(moved, gds, history));
    }

    [Fact]
    public void A_corner_dragged_comes_out_as_a_flatten_would_draw_it()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();

        history.Do(new MoveVertex(structure, model, 1, 40, 25));

        AssertSameLayout(GdsFlattener.Flatten(gds), Reflattened(before, gds, history));
    }

    [Fact]
    public void A_shape_turned_comes_out_as_a_flatten_would_draw_it()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();

        var xy = ((Int4Data)model.Element.XY!.Data!).Values;
        var turned = new int[xy.Length];

        //A quarter turn about the origin, which is what Turn writes - any new corners will do here.
        for (int i = 0; i + 1 < xy.Length; i += 2)
        {
            turned[i] = -xy[i + 1];
            turned[i + 1] = xy[i];
        }

        history.Do(new ReshapeElement(structure, model, turned, "Turn"));

        AssertSameLayout(GdsFlattener.Flatten(gds), Reflattened(before, gds, history));
    }

    [Fact]
    public void A_shape_put_on_another_layer_comes_out_as_a_flatten_would_draw_it()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();

        //One the file has never had, which the edit registers - the case where the table changes too.
        history.Do(new RelayerElement(gds, structure, model, new LayerKey(250, 7)));

        var after = Reflattened(before, gds, history);

        AssertSameLayout(GdsFlattener.Flatten(gds), after);
        Assert.Contains(after.Elements, element => element.Layer.Key == new LayerKey(250, 7));
    }

    [Fact]
    public void A_label_retyped_comes_out_as_a_flatten_would_draw_it()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var labeled = before.Elements.First(element => element.Text is not null && element.Source is not null).Source!;
        var structure = gds.StreamFormat.Structures.First(each => (each.STRNAME?.Data as AsciiData)?.Value == labeled.Structure);
        var history = new EditHistory();

        history.Do(new RetextElement(structure, labeled.Model, "renamed"));

        var after = Reflattened(before, gds, history);

        AssertSameLayout(GdsFlattener.Flatten(gds), after);
        Assert.Contains(after.Elements, element => element.Text == "renamed");
    }

    [Fact]
    public void Several_shapes_moved_together_come_out_as_a_flatten_would_draw_them()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);

        var others = structure.Elements
            .Where(each => each != model && each.Element is BoundaryModel)
            .Take(3)
            .Select(each => (LayoutEdit)new MoveElement(structure, each, -20, 15));

        var history = new EditHistory();

        history.Do(new CompoundEdit("Move", others.Append(new MoveElement(structure, model, -20, 15))));

        AssertSameLayout(GdsFlattener.Flatten(gds), Reflattened(before, gds, history));
    }

    ///<summary>
    ///**What did not change is the very object it was** - which is the point. Its box, and the text a view
    ///keeps of it, are worked out once and kept on the element; a copy would have to work them out again.
    ///</summary>
    [Fact]
    public void Only_what_changed_is_new()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();

        history.Do(new MoveElement(structure, model, 10, 10));

        var after = Reflattened(before, gds, history);

        for (int i = 0; i < before.Elements.Count; i++)
        {
            if (before.Elements[i].Source?.Model == model)
                Assert.NotSame(before.Elements[i], after.Elements[i]);
            else
                Assert.Same(before.Elements[i], after.Elements[i]);
        }
    }

    #endregion ***********************************************************************



    #region Flattened again when it cannot be sure ************************************

    ///<summary>A placement moved moves everything under it, which is a flatten's question and not this one's.</summary>
    [Fact]
    public void A_placement_moved_is_left_to_a_flatten()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var top = gds.StreamFormat.Structures.First(structure => structure.Elements.Any(each => each.Element is SrefModel));
        var placement = top.Elements.First(each => each.Element is SrefModel);
        var history = new EditHistory();

        history.Do(new MoveElement(top, placement, 100, 0));

        Assert.Null(GdsFlattener.Reflatten(before, gds, history.LastStep!.Reshaped));
    }

    ///<summary>A shape added is one more element, somewhere in the middle - nothing an update in place can say.</summary>
    [Fact]
    public void A_shape_added_says_nothing_was_reshaped()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, _) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();

        var square = new List<Element.Point>
        {
            new Element.Point(0, 0),
            new Element.Point(100, 0),
            new Element.Point(100, 100),
            new Element.Point(0, 100),
            new Element.Point(0, 0)
        };

        history.Do(new AddElement(gds, structure, before.Elements[0].Layer.Key, square));

        Assert.Null(history.LastStep!.Reshaped);
        Assert.Null(GdsFlattener.Reflatten(before, gds, history.LastStep.Reshaped));
    }

    [Fact]
    public void Every_step_moves_the_history_on_and_names_itself()
    {
        var gds = Bones();
        var before = GdsFlattener.Flatten(gds);
        var (structure, model) = InAPlacedCell(gds, before, AShape);
        var history = new EditHistory();
        var move = new MoveElement(structure, model, 10, 10);

        int was = history.Version;

        history.Do(move);

        Assert.Equal(was + 1, history.Version);
        Assert.Same(move, history.LastStep);

        history.Undo();
        history.Redo();

        Assert.Equal(was + 3, history.Version);
        Assert.Same(move, history.LastStep);

        history.Clear();

        Assert.Equal(was + 4, history.Version);
        Assert.Null(history.LastStep);
    }

    #endregion ***********************************************************************



    #region The index it carries ******************************************************

    ///<summary>
    ///After several moves in a row, each set aside from the same grid, the index answers every question the
    ///way one built from nothing over the same elements does.
    ///</summary>
    [Fact]
    public void Its_index_answers_as_a_new_one_would()
    {
        var gds = Bones();
        var layout = GdsFlattener.Flatten(gds);
        var history = new EditHistory();
        var random = new Random(5);

        var moved = layout.Elements
            .Where(element => element.Source is not null && AShape(element))
            .Select(element => element.Source!)
            .DistinctBy(source => source.Model)
            .Take(6)
            .ToList();

        foreach (var source in moved)
        {
            var structure = gds.StreamFormat.Structures.First(each => (each.STRNAME?.Data as AsciiData)?.Value == source.Structure);

            history.Do(new MoveElement(structure, source.Model, random.Next(-3000, 3000), random.Next(-3000, 3000)));

            layout = Reflattened(layout, gds, history);
        }

        var fresh = new LayoutIndex(layout.Elements);
        var extent = fresh.Extent;

        for (int i = 0; i < 200; i++)
        {
            int left = random.Next(extent.Left, extent.Right);
            int bottom = random.Next(extent.Bottom, extent.Top);
            var box = new Bounds(left, bottom, left + random.Next(0, 20_000), bottom + random.Next(0, 20_000));
            long smallest = random.Next(0, 3) * 10_000L;
            bool withLabels = random.Next(2) == 0;

            Assert.Equal(fresh.Within(box, smallest, withLabels), layout.Index.Within(box, smallest, withLabels));

            var point = new Element.Point(left, bottom);

            Assert.Equal(fresh.Under(point), layout.Index.Under(point));
        }

        Assert.Equal(fresh.LargestArea, layout.Index.LargestArea);
        Assert.Equal(fresh.ShapeLayers.Select(layer => layer.Key), layout.Index.ShapeLayers.Select(layer => layer.Key));

        //The extents are carried across rather than walked again, so they hold everything without having to be
        //tight - a shape moved in from the edge leaves them as large as they were. See LayoutIndex.Summary.After.
        Assert.True(layout.Index.Extent.Contains(fresh.Extent));
        Assert.True(layout.Index.ShapeExtent.Contains(fresh.ShapeExtent));
    }

    ///<summary>
    ///A layer whose only shape has gone to another layer is not in use any more, and one that has just had a
    ///shape put on it is - both told by the counts carried across, without a walk.
    ///</summary>
    [Fact]
    public void A_layer_emptied_by_an_edit_is_no_longer_in_use()
    {
        var elements = new List<Element>();
        var metal = new Layer(new LayerKey(68, 20), "#0000ff");
        var poly = new Layer(new LayerKey(66, 20), "#ff0000");
        var via = new Layer(new LayerKey(67, 44), "#00ff00");

        for (int i = 0; i < 50; i++)
            elements.Add(Square(i * 100, 0, metal));

        elements.Add(Square(9000, 9000, poly));

        var layout = new FlattenedLayout { Elements = elements };

        //Asked for, so there is a summary to carry.
        Assert.Equal(new[] { metal.Key, poly.Key }, layout.Index.ShapeLayers.Select(layer => layer.Key));

        var changed = new List<Element>(elements);

        changed[50] = Square(9000, 9000, via);

        var after = LayoutIndex.After(layout.Index, changed, new[] { 50 });

        Assert.Equal(new[] { metal.Key, via.Key }, after.ShapeLayers.Select(layer => layer.Key));
    }

    private static Element Square(int x, int y, Layer layer)
    {
        return new Element
        {
            Layer = layer,
            Points = new List<Element.Point>
            {
                new Element.Point(x, y),
                new Element.Point(x + 50, y),
                new Element.Point(x + 50, y + 50),
                new Element.Point(x, y + 50),
                new Element.Point(x, y)
            }
        };
    }

    #endregion ***********************************************************************
}
