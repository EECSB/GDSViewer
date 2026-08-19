using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Which cell is being edited, and through which of its placements.
///
///**Two things, not one.** The structure is what an edit changes - every instance of it moves, because
///there is one cell and the instances are references to it. The placement is only the instance being
///looked through, which is what reads a click. Conflating them is how an editor changes the right cell by
///the wrong amount, so most of what is here is about keeping them apart.
///</summary>
public class CellContextTests
{
    #region A three-deep library ****************************************************

    ///<summary>
    ///TOP places ROW twice; ROW places LEAF twice. So a leaf square is drawn four times, three levels
    ///down, and no two of the four are reached the same way.
    ///
    ///Three levels rather than two on purpose: with only two, going up one level and going to the top are
    ///the same operation and a context that confused them would pass.
    ///</summary>
    private static byte[] Nested()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("NESTED")),
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
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("ROW"))
        };

        records.AddRange(Sref("LEAF", 0, 0));
        records.AddRange(Sref("LEAF", 500, 0));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));

        records.Add(GdsTestData.Record(RecordType.BGNSTR, stamps));
        records.Add(GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")));

        //A shape of the top's own, so there is something at depth zero to compare against.
        records.AddRange(new[]
        {
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(67)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(100))),
            GdsTestData.Record(RecordType.ENDEL)
        });

        records.AddRange(Sref("ROW", 0, 2000));
        records.AddRange(Sref("ROW", 0, 4000));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return GdsTestData.Concat(records.ToArray());
    }

    private static byte[][] Sref(string name, int x, int y)
    {
        return new[]
        {
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y)),
            GdsTestData.Record(RecordType.ENDEL)
        };
    }

    private static FlattenedLayout Flat()
    {
        return GdsFlattener.Flatten(new GDS(Nested()));
    }

    ///<summary>The leaf squares, of which there are four.</summary>
    private static List<Element> Leaves(FlattenedLayout layout)
    {
        return layout.Elements.Where(element => element.Layer.Key.Number == 65).ToList();
    }

    private static Element TopShape(FlattenedLayout layout)
    {
        return layout.Elements.Single(element => element.Layer.Key.Number == 67);
    }

    #endregion **********************************************************************



    #region The chain ***************************************************************

    [Fact]
    public void The_library_nests_the_way_the_tests_below_assume()
    {
        var layout = Flat();

        //Two rows of two leaves, plus the top's own square.
        Assert.Equal(4, Leaves(layout).Count);
        Assert.Equal(5, layout.Elements.Count);
    }

    [Fact]
    public void A_shape_knows_every_level_it_was_reached_through()
    {
        var source = Leaves(Flat())[0].Source!;

        var ancestry = source.Ancestry;

        Assert.Equal(new[] { "TOP", "ROW", "LEAF" }, ancestry.Select(level => level.Structure));

        //The outermost is the identity - nothing placed the top - and the innermost is what the shape was
        //actually drawn through.
        Assert.Equal(Transform.Identity, ancestry[0].Placement);
        Assert.Equal(source.Placement, ancestry[^1].Placement);
    }

    [Fact]
    public void A_top_level_shape_has_one_level_and_it_is_the_identity()
    {
        var ancestry = TopShape(Flat()).Source!.Ancestry;

        Assert.Single(ancestry);
        Assert.Equal("TOP", ancestry[0].Structure);
        Assert.Equal(Transform.Identity, ancestry[0].Placement);
    }

    ///<summary>
    ///Each of the four leaves was reached differently, so each carries its own middle level - the row it
    ///is in - as well as its own final placement.
    ///</summary>
    [Fact]
    public void The_levels_differ_between_instances()
    {
        var ancestries = Leaves(Flat()).Select(element => element.Source!.Ancestry).ToList();

        //Four distinct innermost placements: two leaves in each of two rows.
        Assert.Equal(4, ancestries.Select(a => (a[^1].Placement.Dx, a[^1].Placement.Dy)).Distinct().Count());

        //And two distinct middle ones, because there are two rows.
        Assert.Equal(2, ancestries.Select(a => (a[1].Placement.Dx, a[1].Placement.Dy)).Distinct().Count());
    }

    #endregion **********************************************************************



    #region Going in and coming out *************************************************

    [Fact]
    public void Descending_lands_in_the_cell_the_shape_belongs_to()
    {
        var context = CellContext.At(Leaves(Flat())[0].Source!);

        Assert.Equal("LEAF", context.Structure);
        Assert.Equal(2, context.Depth);
        Assert.False(context.IsTop);
        Assert.Equal("TOP > ROW > LEAF", context.ToString());
    }

    [Fact]
    public void Climbing_out_goes_one_level_at_a_time_and_stops_at_the_top()
    {
        var context = CellContext.At(Leaves(Flat())[0].Source!);

        var row = context.Up();

        Assert.NotNull(row);
        Assert.Equal("ROW", row!.Structure);
        Assert.Equal(1, row.Depth);

        var top = row.Up();

        Assert.NotNull(top);
        Assert.Equal("TOP", top!.Structure);
        Assert.True(top.IsTop);

        //And out of the top there is nowhere further up: the way out is to stop editing.
        Assert.Null(top.Up());
    }

    ///<summary>
    ///Climbing out keeps the transform of the level climbed to, which is the whole reason a chain of names
    ///is not enough - a breadcrumb can be drawn from names, but it cannot be followed with them.
    ///</summary>
    [Fact]
    public void Climbing_out_arrives_with_that_level_own_placement()
    {
        //The *second* leaf of a row on purpose. The first sits at (0,0) inside its row, so its composed
        //transform is its row's - and against that one, a climb that forgot to change the transform at all
        //would pass. Nothing was wrong with the code; the fixture was wrong with the test.
        var leaf = CellContext.At(Leaves(Flat())[1].Source!);
        var row = leaf.Up()!;

        Assert.Equal(leaf.Levels[1].Placement, row.Placement);
        Assert.NotEqual(leaf.Placement, row.Placement);

        //And the top's is the identity, since nothing placed it.
        Assert.Equal(Transform.Identity, row.Up()!.Placement);
    }

    [Fact]
    public void A_breadcrumb_entry_can_be_jumped_to_directly()
    {
        var context = CellContext.At(Leaves(Flat())[0].Source!);

        Assert.Equal("TOP", context.To(0).Structure);
        Assert.Equal("ROW", context.To(1).Structure);
        Assert.Equal("LEAF", context.To(2).Structure);

        //Clamped rather than throwing, since the chain a breadcrumb was drawn from may have gone away.
        Assert.Equal("TOP", context.To(-5).Structure);
        Assert.Equal("LEAF", context.To(99).Structure);
    }

    #endregion **********************************************************************



    #region What an edit would touch ************************************************

    ///<summary>
    ///**All four, not one.** There is one LEAF and the four squares are instances of it, so an edit in
    ///that context moves all of them. A view that said otherwise would be telling a comfortable lie.
    ///</summary>
    [Fact]
    public void Editing_a_cell_holds_every_instance_of_it()
    {
        var layout = Flat();
        var context = CellContext.At(Leaves(layout)[0].Source!);

        Assert.Equal(4, layout.Elements.Count(context.Holds));
        Assert.All(Leaves(layout), leaf => Assert.True(context.Holds(leaf)));

        //And not the top's own square, which is in a different cell.
        Assert.False(context.Holds(TopShape(layout)));
    }

    ///<summary>
    ///One of the four is the instance being looked through. The difference matters: those are the shapes a
    ///click lands on, and the other three are the ones that move with them.
    ///</summary>
    [Fact]
    public void Exactly_one_instance_is_the_one_being_looked_through()
    {
        var layout = Flat();
        var chosen = Leaves(layout)[2];

        var context = CellContext.At(chosen.Source!);

        Assert.Equal(1, layout.Elements.Count(context.IsLookingThrough));
        Assert.True(context.IsLookingThrough(chosen));
    }

    [Fact]
    public void Editing_the_top_holds_its_own_shapes_and_not_a_placed_cell_shapes()
    {
        var layout = Flat();
        var context = CellContext.At(TopShape(layout).Source!);

        Assert.Equal("TOP", context.Structure);
        Assert.True(context.IsTop);

        Assert.True(context.Holds(TopShape(layout)));
        Assert.All(Leaves(layout), leaf => Assert.False(context.Holds(leaf)));
    }

    ///<summary>
    ///Climbing out of LEAF into ROW changes what an edit would touch: ROW's own elements are its two
    ///placements, which draw nothing themselves, so nothing drawn is held there. Worth pinning, because it
    ///is the honest answer and it looks like a bug until it is thought about.
    ///</summary>
    [Fact]
    public void A_cell_that_only_places_others_holds_nothing_that_is_drawn()
    {
        var layout = Flat();
        var row = CellContext.At(Leaves(layout)[0].Source!).Up()!;

        Assert.Equal("ROW", row.Structure);
        Assert.Equal(0, layout.Elements.Count(row.Holds));
    }

    #endregion **********************************************************************



    #region Reading a click *********************************************************

    ///<summary>
    ///A click in the layout's coordinates, brought into the cell being edited, and back out again. The
    ///same round trip provenance makes for one shape, now for the context as a whole.
    ///</summary>
    [Fact]
    public void A_point_goes_into_the_cell_and_back_out_unchanged()
    {
        var context = CellContext.At(Leaves(Flat())[3].Source!);

        foreach ((double x, double y) in new[] { (0.0, 0.0), (1234.0, -567.0), (500.0, 4000.0) })
        {
            (double localX, double localY) = context.ToLocal(x, y)!.Value;
            (double backX, double backY) = context.ToLayout(localX, localY);

            Assert.Equal(x, backX, 6);
            Assert.Equal(y, backY, 6);
        }
    }

    ///<summary>
    ///And the point it arrives at is the one the file holds. The fourth leaf is drawn at a known offset -
    ///the second row, the second column - so its corner in local coordinates has to be the origin.
    ///</summary>
    [Fact]
    public void A_click_on_a_corner_reads_as_that_corner_in_the_cell()
    {
        var layout = Flat();

        foreach (var leaf in Leaves(layout))
        {
            var context = CellContext.At(leaf.Source!);

            var corner = leaf.Points[0];

            (double x, double y) = context.ToLocal(corner.X, corner.Y)!.Value;

            //Every instance is the same square, so every one of them reads back to its own origin.
            Assert.Equal(0, Math.Round(x), 6);
            Assert.Equal(0, Math.Round(y), 6);
        }
    }

    #endregion **********************************************************************
}
