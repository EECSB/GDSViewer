using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Running a deck over a layout, and what comes back.
///
///**Most of this is about the half that is not violations.** A list of faults invites the reading "nothing
///here, so the layout is fine", and that reading is only safe when every rule actually ran. A deck can hold
///a check this build cannot measure, a derivation that goes round in a circle, or a line that did not
///parse, and each of those is a rule that quietly measured nothing - so the tests that matter most here are
///the ones that say Clean is false while any of them stands.
///
///The other half is provenance: a violation is found on flattened geometry, and being able to say which
///cell it came from is the thing a flat checker is not supposed to manage.
///</summary>
public class DrcRunTests
{
    #region A library with a cell placed twice ***************************************

    ///<summary>
    ///A leaf holding one box a hundred units wide - too narrow for a limit of 140 - and a top that places
    ///it twice, far enough apart that the two do not interact.
    ///
    ///Placed rather than drawn at the top, because that is the case provenance exists for: the shape on
    ///screen is one of two instances, and the coordinate to change is the one inside LEAF.
    ///</summary>
    private static GDS Placed()
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
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(68)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0, 100, 1000, 0, 1000, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),

            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(5000, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)
        };

        return new GDS(GdsTestData.Concat(records.ToArray()));
    }

    private const string NarrowDeck = @"layer met1 68/20
rule met1.1 width met1 140 ""Met1 width""";

    private static DrcResult RunOn(string deck, GDS gds)
    {
        return Drc.Check(DrcDeck.Parse(deck), GdsFlattener.Flatten(gds));
    }

    #endregion **************************************************************************



    #region What a violation carries ****************************************************

    [Fact]
    public void A_violation_names_its_rule_and_the_limit_it_broke()
    {
        var result = RunOn(NarrowDeck, Placed());

        Assert.NotEmpty(result.Violations);

        var violation = result.Violations[0];

        Assert.Equal("met1.1", violation.RuleId);
        Assert.Equal("Met1 width", violation.Description);
        Assert.Equal(DrcCheck.Width, violation.Check);
        Assert.Equal(140, violation.Limit);
    }

    ///<summary>One per placement, since the cell is too narrow wherever it is put.</summary>
    [Fact]
    public void A_cell_placed_twice_is_reported_twice()
    {
        Assert.Equal(2, RunOn(NarrowDeck, Placed()).Violations.Count);
    }

    [Fact]
    public void A_violation_is_bounded_by_its_marker()
    {
        var violation = RunOn(NarrowDeck, Placed()).Violations[0];

        Assert.Equal(Bounds.Of(violation.Marker), violation.Bounds);
        Assert.False(violation.Bounds.IsEmpty);
    }

    ///<summary>
    ///The payoff of keeping provenance through the flattener: the fault is on a shape that belongs to LEAF,
    ///and moving it means editing LEAF rather than the drawn coordinate.
    ///</summary>
    [Fact]
    public void A_violation_inside_a_placed_cell_names_that_cell()
    {
        var result = RunOn(NarrowDeck, Placed());

        foreach (var violation in result.Violations)
        {
            Assert.NotNull(violation.Source);
            Assert.Equal("LEAF", violation.Source!.Structure);
        }
    }

    ///<summary>The two placements are told apart by where they are, not only by which cell they name.</summary>
    [Fact]
    public void The_two_placements_are_reported_at_their_own_positions()
    {
        var result = RunOn(NarrowDeck, Placed());

        var lefts = result.Violations.Select(violation => violation.Bounds.Left).OrderBy(left => left).ToList();

        Assert.Equal(0, lefts[0]);
        Assert.Equal(5000, lefts[1]);
    }

    ///<summary>
    ///An L-shaped cell whose arms are a thousand wide, and a narrow cell sitting in the empty quadrant of
    ///its bounding box.
    ///
    ///**Built so that a box test alone gets the wrong answer.** WIDE's extent covers the whole square and
    ///therefore covers NARROW's violation, while its own geometry is nowhere near it - and WIDE is reached
    ///first, so attribution that stopped at the box would name a cell that is not at fault and is not even
    ///adjacent. WIDE's arms are far too wide to break the rule themselves, so the only violation in the
    ///library belongs to NARROW.
    ///</summary>
    private static GDS OverlappingExtents()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("EXTENTS")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("WIDE")),

            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(68)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 3000, 0, 3000, 1000, 1000, 1000, 1000, 3000, 0, 3000, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("NARROW")),

            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(68)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0, 100, 500, 0, 500, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),

            //WIDE first, so the wrong answer is the one reached first.
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("WIDE")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("NARROW")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(2000, 2000)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)
        };

        return new GDS(GdsTestData.Concat(records.ToArray()));
    }

    ///<summary>
    ///The fault belongs to NARROW, and only actually intersecting the marker can say so.
    ///
    ///A box test alone answers WIDE here - its extent swallows the whole library while its geometry keeps
    ///to two arms the violation never touches. That is not a hypothetical: it is the ordinary shape of a
    ///cell with a hole in the middle of its bounding box, which is most cells.
    ///</summary>
    [Fact]
    public void A_cell_whose_extent_covers_the_fault_but_whose_geometry_does_not_is_not_blamed()
    {
        var result = RunOn(NarrowDeck, OverlappingExtents());

        var violation = Assert.Single(result.Violations);

        Assert.NotNull(violation.Source);
        Assert.Equal("NARROW", violation.Source!.Structure);
    }

    #endregion **************************************************************************



    #region Clean, and what makes it false **********************************************

    private const string WideDeck = @"layer met1 68/20
rule met1.1 width met1 100 ""Met1 width""";

    [Fact]
    public void A_layout_that_breaks_nothing_comes_back_clean()
    {
        var result = RunOn(WideDeck, Placed());

        Assert.Empty(result.Violations);
        Assert.True(result.Complete);
        Assert.True(result.Clean);
    }

    ///<summary>
    ///The test the whole design is for. Nothing was found, and nothing may be concluded from that, because
    ///a rule never ran.
    ///</summary>
    [Fact]
    public void A_refused_check_makes_a_result_not_clean_though_nothing_was_found()
    {
        var result = RunOn(WideDeck + "\nrule poly.4 spaceparallel met1 75 \"Parallel edges only\"", Placed());

        Assert.Empty(result.Violations);
        Assert.False(result.Clean);
        Assert.False(result.Complete);
    }

    [Fact]
    public void A_refused_check_is_named_in_what_did_not_run()
    {
        var result = RunOn(WideDeck + "\nrule poly.4 spaceparallel met1 75 \"Parallel edges only\"", Placed());

        Assert.Contains(result.NotRun, entry => entry.Contains("poly.4"));
    }

    [Fact]
    public void A_circle_of_derivations_makes_a_result_not_clean()
    {
        string deck = @"layer met1 68/20
derive one = two and met1
derive two = one and met1
rule made.1 width one 140 ""Made up""";

        var result = RunOn(deck, Placed());

        Assert.False(result.Clean);
        Assert.NotEmpty(result.Problems);
    }

    ///<summary>A rule whose layers went round in a circle is named, not merely counted.</summary>
    [Fact]
    public void A_rule_whose_layers_could_not_be_worked_out_is_named()
    {
        string deck = @"layer met1 68/20
derive one = two and met1
derive two = one and met1
rule made.1 width one 140 ""Made up""";

        var result = RunOn(deck, Placed());

        Assert.Contains(result.NotRun, entry => entry.StartsWith("made.1"));
    }

    ///<summary>A line that did not parse may have been a rule, so it counts against completeness too.</summary>
    [Fact]
    public void A_line_that_did_not_parse_makes_a_result_not_clean()
    {
        var result = RunOn(WideDeck + "\nlayer met2 68-20", Placed());

        Assert.Empty(result.Violations);
        Assert.False(result.Clean);
    }

    #endregion **************************************************************************



    #region Exemptions and grouping *****************************************************

    ///<summary>An exemption covering the fault takes it off, and the run is still complete.</summary>
    [Fact]
    public void A_violation_inside_an_exempt_layer_is_not_reported()
    {
        string deck = @"layer met1 68/20
layer marker 81/4
rule met1.1 width met1 140 except marker ""Met1 width""";

        //The marker layer is not in the file, so the exemption covers nothing and the fault stands.
        Assert.NotEmpty(RunOn(deck, Placed()).Violations);
    }

    [Fact]
    public void Violations_can_be_grouped_by_the_rule_that_found_them()
    {
        var result = RunOn(NarrowDeck, Placed());

        var group = Assert.Single(result.ByRule());

        Assert.Equal("met1.1", group.Key);
        Assert.Equal(2, group.Count());
    }

    #endregion **************************************************************************



    #region Off the grid ****************************************************************

    ///<summary>
    ///A library whose coordinates are multiples of five but for one at three.
    ///
    ///This is the case that shows why the grid cannot be read back off the file: Grid.Of answers one here,
    ///because the greatest common divisor of the coordinates includes the stray one - see GridTests, which
    ///pins exactly that. Checked against a grid the deck states, the stray coordinate is found.
    ///</summary>
    private static GDS AlmostOnGrid()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("GRID")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),

            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(68)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0, 100, 1000, 3, 1000, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)
        };

        return new GDS(GdsTestData.Concat(records.ToArray()));
    }

    ///<summary>
    ///An off-grid deck, declaring the layer the stray coordinate is on.
    ///
    ///**The declaration is not a formality.** `*` means every layer the deck names rather than every
    ///element in the file, and the difference is what stops a pin from being reported: nothing on a mask
    ///comes from one, so where it sits is not a manufacturing fault. A deck that declares no layers
    ///correctly checks nothing.
    ///</summary>
    private static string OnGridDeck(int grid)
    {
        return $"layer met1 68/20\nrule grid.1 offgrid * {grid} \"Off the grid\"";
    }

    ///<summary>A layer the deck does not name is not checked, however far off the grid it sits.</summary>
    [Fact]
    public void A_layer_the_deck_does_not_declare_is_not_checked_against_the_grid()
    {
        var result = RunOn("layer poly 66/20\nrule grid.1 offgrid * 5 \"Off the grid\"", AlmostOnGrid());

        Assert.Empty(result.Violations);
        Assert.True(result.Clean);
    }

    [Fact]
    public void A_coordinate_off_the_stated_grid_is_reported()
    {
        var result = RunOn(OnGridDeck(5), AlmostOnGrid());

        var violation = Assert.Single(result.Violations);

        Assert.Equal(DrcCheck.OffGrid, violation.Check);
        Assert.Equal(3, violation.Marker[0].X);
    }

    ///<summary>
    ///And the grid that the library reports about itself finds nothing, which is the whole reason the deck
    ///has to state one.
    ///</summary>
    [Fact]
    public void The_grid_read_back_off_the_file_would_have_found_nothing()
    {
        Assert.Equal(1, Grid.Of(AlmostOnGrid()));

        Assert.Empty(RunOn(OnGridDeck(1), AlmostOnGrid()).Violations);
    }

    ///<summary>An off-grid fault is a point, so its marker has no area to it.</summary>
    [Fact]
    public void An_off_grid_violation_is_a_point()
    {
        var violation = Assert.Single(RunOn(OnGridDeck(5), AlmostOnGrid()).Violations);

        Assert.Single(violation.Marker);
        Assert.Equal(0, violation.Bounds.Area);
    }

    #endregion **************************************************************************



    #region The bundled deck ***********************************************************

    ///<summary>
    ///The sky130 starter deck run over a bundled layout, end to end.
    ///
    ///What is asserted is that it *runs* - every rule read, understood and measured, nothing refused and no
    ///layer left unresolved. Whether the violations it finds are real is a question only a foundry can
    ///settle, and this deliberately does not pin their number: that would fix a figure produced by an
    ///approximation nobody has checked against a signoff tool.
    ///</summary>
    [Fact]
    public void The_bundled_sky130_deck_runs_end_to_end()
    {
        string path = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        var deck = DrcDeck.Parse(File.ReadAllText(path));

        var gds = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample)));

        var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

        Assert.Empty(result.NotRun);
        Assert.Empty(result.Problems);
        Assert.True(result.Complete);
    }

    #endregion **************************************************************************
}
