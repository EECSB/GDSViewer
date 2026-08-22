using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///The way back from a drawn shape to the element that put it there.
///
///**Flattening is lossy in exactly the direction an editor needs.** It answers "what is drawn and where",
///which is all a viewer wants and nothing an editor can use: a shape on screen may be one of a thousand
///instances of a cell, and moving it means changing a coordinate inside that cell rather than the one on
///screen. These are the tests that say the way back is real.
///
///The one that matters is the round trip: a point taken from what is drawn, brought back through the
///placement, has to be the coordinate the file actually holds. Everything else here is bookkeeping.
///</summary>
public class ProvenanceTests
{
    #region A library with a cell placed several ways ********************************

    ///<summary>
    ///A leaf holding one square and one label, and a top that places it four ways: plain, rotated a
    ///quarter turn, mirrored, and at twice the size on a diagonal.
    ///
    ///The four are the cases the inverse has to survive. A plain placement is a translation, which almost
    ///anything gets right; a rotation mixes the axes, a mirror flips one of them, and a magnification is
    ///the one that makes a naive "subtract the offset" wrong rather than merely imprecise.
    ///</summary>
    private static byte[] Placed()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("PLACED")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF")),

            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(65)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(10, 20, 110, 20, 110, 80, 10, 80, 10, 20)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(66)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(50, 50)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii("A")),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP"))
        };

        //A shape belonging to the top itself, which is the one whose drawn coordinates are the file's.
        records.AddRange(new[]
        {
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(67)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 40, 0, 40, 40, 0, 40, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL)
        });

        records.AddRange(Sref(1000, 0, null, null, false));
        records.AddRange(Sref(2000, 0, 90, null, false));
        records.AddRange(Sref(3000, 0, null, null, true));
        records.AddRange(Sref(4000, 500, 30, 2, false));

        records.Add(GdsTestData.Record(RecordType.ENDSTR));
        records.Add(GdsTestData.Record(RecordType.ENDLIB));

        return GdsTestData.Concat(records.ToArray());
    }

    private static byte[][] Sref(int x, int y, double? angle, double? magnification, bool mirrored)
    {
        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF"))
        };

        if (angle is not null || magnification is not null || mirrored)
        {
            byte[] flags = { 0x00, 0x00 };

            if (mirrored)
                flags = new byte[] { 0x80, 0x00 };

            records.Add(GdsTestData.Record(RecordType.STRANS, flags));

            if (magnification is double scale)
                records.Add(GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(scale)));

            if (angle is double turn)
                records.Add(GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(turn)));
        }

        records.Add(GdsTestData.Record(RecordType.XY, GdsTestData.Int4(x, y)));
        records.Add(GdsTestData.Record(RecordType.ENDEL));

        return records.ToArray();
    }

    private static FlattenedLayout Flat()
    {
        return GdsFlattener.Flatten(new GDS(Placed()));
    }

    #endregion ***********************************************************************



    #region The transform itself *****************************************************

    [Fact]
    public void The_identity_inverts_to_itself()
    {
        var back = Transform.Identity.Inverse();

        Assert.NotNull(back);
        Assert.Equal((12.0, -34.0), back!.Value.ApplyTo(12, -34));
    }

    ///<summary>
    ///Every kind of placement GDSII can describe, inverted and applied back. A tolerance rather than
    ///exact equality, because a rotation goes through sine and cosine and thirty degrees is not a number
    ///a double holds exactly.
    ///</summary>
    [Theory]
    [InlineData(false, 1, 0, 100, 200)]
    [InlineData(false, 1, 90, -50, 0)]
    [InlineData(false, 1, 180, 0, 0)]
    [InlineData(true, 1, 0, 300, -300)]
    [InlineData(true, 1, 270, 10, 10)]
    [InlineData(false, 2, 30, 4000, 500)]
    [InlineData(false, 0.25, 45, -1000, -1000)]
    public void A_placement_undoes_itself(bool mirrored, double magnification, double angle, double dx, double dy)
    {
        var forward = Transform.ForPlacement(mirrored, magnification, angle, dx, dy);
        var back = forward.Inverse();

        Assert.NotNull(back);

        foreach ((double x, double y) in new[] { (0.0, 0.0), (137.0, -42.0), (-9999.0, 12345.0) })
        {
            (double outX, double outY) = forward.ApplyTo(x, y);
            (double backX, double backY) = back!.Value.ApplyTo(outX, outY);

            Assert.Equal(x, backX, 6);
            Assert.Equal(y, backY, 6);
        }
    }

    ///<summary>
    ///A placement magnified by zero collapses everything inside it onto its reference point, so there is
    ///no way back out to say where in the cell a click was. Real files do carry one occasionally, so it
    ///is a case to answer rather than a thing to assert against.
    ///</summary>
    [Fact]
    public void A_placement_that_collapses_to_a_point_has_no_way_back()
    {
        Assert.Null(Transform.ForPlacement(false, 0, 0, 500, 500).Inverse());
    }

    #endregion ***********************************************************************



    #region What a drawn shape knows *************************************************

    [Fact]
    public void Every_drawn_shape_says_where_it_came_from()
    {
        var layout = Flat();

        Assert.NotEmpty(layout.Elements);
        Assert.All(layout.Elements, element => Assert.NotNull(element.Source));
    }

    [Fact]
    public void A_shape_of_the_top_structure_is_at_the_top_of_the_chain()
    {
        var source = Flat().Elements.Single(element => element.Layer.Key.Number == 67).Source!;

        Assert.Equal("TOP", source.Structure);
        Assert.Equal(new[] { "TOP" }, source.Path);
        Assert.Equal(0, source.Depth);

        //Which is what makes its drawn coordinates the ones the file holds.
        Assert.True(source.IsDirectlyEditable);
    }

    [Fact]
    public void A_placed_shape_names_the_cell_it_belongs_to_and_the_way_in()
    {
        var placed = Flat().Elements.Where(element => element.Layer.Key.Number == 65).ToList();

        //Four placements of the leaf, so four copies of its one square.
        Assert.Equal(4, placed.Count);

        Assert.All(placed, element =>
        {
            Assert.Equal("LEAF", element.Source!.Structure);
            Assert.Equal(new[] { "TOP", "LEAF" }, element.Source.Path);
            Assert.Equal(1, element.Source.Depth);

            //Its drawn coordinates are not the file's, which is the whole reason this exists.
            Assert.False(element.Source.IsDirectlyEditable);
        });
    }

    ///<summary>
    ///All four copies point at the *same* element of the library.
    ///
    ///Which is what "edit in place" means: change that one model and every instance moves. A provenance
    ///that handed out a copy per instance would look identical and edit nothing.
    ///</summary>
    [Fact]
    public void Every_instance_of_a_cell_points_at_the_one_element_in_the_library()
    {
        var placed = Flat().Elements.Where(element => element.Layer.Key.Number == 65).ToList();

        var models = placed.Select(element => element.Source!.Model).Distinct().ToList();

        Assert.Single(models);
    }

    ///<summary>And each instance is seen through its own placement, or they would all be drawn on top of each other.</summary>
    [Fact]
    public void Each_instance_carries_its_own_placement()
    {
        var placed = Flat().Elements.Where(element => element.Layer.Key.Number == 65).ToList();

        var offsets = placed.Select(element => (element.Source!.Placement.Dx, element.Source.Placement.Dy)).ToList();

        Assert.Equal(4, offsets.Distinct().Count());
    }

    [Fact]
    public void A_label_carries_its_source_too()
    {
        var labels = Flat().Elements.Where(element => element.Text == "A").ToList();

        Assert.Equal(4, labels.Count);
        Assert.All(labels, label => Assert.Equal("LEAF", label.Source!.Structure));
    }

    ///<summary>The chain reads outermost first, which is the order a breadcrumb wants it in.</summary>
    [Fact]
    public void The_chain_prints_from_the_outside_in()
    {
        var source = Flat().Elements.First(element => element.Layer.Key.Number == 65).Source!;

        Assert.Equal("TOP > LEAF", source.ToString());
    }

    #endregion ***********************************************************************



    #region The round trip ***********************************************************

    ///<summary>
    ///**The one that matters.** A point taken off what is drawn, brought back through the placement, is
    ///the coordinate the file holds.
    ///
    ///Run against all four placements including the mirrored one and the one magnified and turned thirty
    ///degrees - which is where a way back that merely subtracts an offset stops working, and where it
    ///stops working *plausibly*, by a few units rather than by being obviously wrong.
    ///</summary>
    [Fact]
    public void A_drawn_corner_maps_back_to_the_coordinate_in_the_file()
    {
        var layout = Flat();

        foreach (var element in layout.Elements.Where(element => element.Layer.Key.Number == 65))
        {
            var source = element.Source!;

            //What the file says this shape's corners are, before anything placed it.
            var xy = (Int4Data)source.Model.Element.XY!.Data!;

            for (int i = 0; i < element.Points.Count; i++)
            {
                var drawn = element.Points[i];

                (double x, double y) = source.ToLocal(drawn.X, drawn.Y)!.Value;

                Assert.Equal(xy.Values[i * 2], Math.Round(x), 6);
                Assert.Equal(xy.Values[(i * 2) + 1], Math.Round(y), 6);
            }
        }
    }

    ///<summary>And forwards again, which is what redrawing after an edit does.</summary>
    [Fact]
    public void A_coordinate_in_the_file_maps_out_to_where_it_is_drawn()
    {
        foreach (var element in Flat().Elements.Where(element => element.Layer.Key.Number == 65))
        {
            var source = element.Source!;
            var xy = (Int4Data)source.Model.Element.XY!.Data!;

            for (int i = 0; i < element.Points.Count; i++)
            {
                (double x, double y) = source.ToLayout(xy.Values[i * 2], xy.Values[(i * 2) + 1]);

                Assert.Equal(element.Points[i].X, Math.Round(x), 6);
                Assert.Equal(element.Points[i].Y, Math.Round(y), 6);
            }
        }
    }

    ///<summary>
    ///An array is many placements of one element, the same as many SREFs are - so every copy has to point
    ///back at the same model and map back to the same coordinates.
    ///</summary>
    [Fact]
    public void Every_copy_of_an_array_maps_back_to_the_same_coordinates()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var gds = new GDS(GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("A")),
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
            GdsTestData.Record(RecordType.AREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.COLROW, GdsTestData.Int2(3, 4)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 900, 0, 0, 1600)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));

        var drawn = GdsFlattener.Flatten(gds).Elements;

        Assert.Equal(12, drawn.Count);

        //One model behind all twelve.
        Assert.Single(drawn.Select(element => element.Source!.Model).Distinct());

        //And twelve places it is seen from.
        Assert.Equal(12, drawn.Select(element => (element.Source!.Placement.Dx, element.Source.Placement.Dy)).Distinct().Count());

        foreach (var element in drawn)
        {
            (double x, double y) = element.Source!.ToLocal(element.Points[0].X, element.Points[0].Y)!.Value;

            Assert.Equal(0, Math.Round(x), 6);
            Assert.Equal(0, Math.Round(y), 6);
        }
    }

    #endregion ***********************************************************************



    #region The corpus ***************************************************************

    ///<summary>
    ///Every bundled file: every drawn shape has a source, and every corner of it maps back to a
    ///coordinate that is actually in the element it names.
    ///
    ///Paths are left out. A path's drawn outline is built from its centerline before being placed, so its
    ///corners are not the coordinates in the file and were never meant to be - the source still names the
    ///right element, which is what an editor needs to find it.
    ///</summary>
    [Fact]
    public void Every_shape_in_the_corpus_maps_back_to_its_own_element()
    {
        var wrong = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            string name = Path.GetFileName(path);

            foreach (var element in GdsFlattener.Flatten(new GDS(File.ReadAllBytes(path))).Elements)
            {
                if (element.Source is not ElementSource source)
                {
                    wrong.Add($"{name}: a shape with no source");

                    continue;
                }

                if (source.Model.Element is GDS.PathModel)
                    continue;

                if (source.Model.Element.XY?.Data is not Int4Data xy)
                    continue;

                if (xy.Values.Length != element.Points.Count * 2)
                {
                    wrong.Add($"{name}: {source} has {element.Points.Count} drawn corners against {xy.Values.Length / 2} in the file");

                    continue;
                }

                for (int i = 0; i < element.Points.Count; i++)
                {
                    if (source.ToLocal(element.Points[i].X, element.Points[i].Y) is not (double x, double y))
                        continue;

                    //A unit of slack, which is all the rounding onto the integer grid can introduce.
                    if (Math.Abs(x - xy.Values[i * 2]) > 1 || Math.Abs(y - xy.Values[(i * 2) + 1]) > 1)
                    {
                        wrong.Add($"{name}: {source} corner {i} came back to ({x}, {y}) rather than ({xy.Values[i * 2]}, {xy.Values[(i * 2) + 1]})");

                        break;
                    }
                }
            }
        }

        Assert.Empty(wrong);
    }

    #endregion ***********************************************************************
}
