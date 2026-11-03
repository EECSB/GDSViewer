using GdsII;

namespace GDSViewer.Tests;

///
///The generated marks: a die frame, alignment marks, test structures, the passives of an analog and an RF circuit,
///antennas, a clock tree, pads and fill, and the first shapes of a photonic and a microfluidic chip.
///
///**Measured against the numbers they were made from.** A mark on a mask is something nobody looks at closely
///until a wafer comes back wrong, so each is held to what its settings say: a frame's area is the die less
///what is inside it, a cross's is two arms less the square they share, a window leaves the asked-for gap round
///the cross it is for. Every one is also made from its own starting settings, so the picker can never offer a
///mark that will not draw.
///
public class MarksTests
{
    ///<summary>A file whose database unit is a nanometer, which is what every bundled example uses.</summary>
    private static int Nanometers(double microns)
    {
        return (int)Math.Round(microns * 1000);
    }

    private static double Area(IEnumerable<List<Element.Point>> outlines)
    {
        return outlines.Sum(outline => Math.Abs(Measure.AreaOf(outline)));
    }

    private static Bounds BoxOf(IEnumerable<List<Element.Point>> outlines)
    {
        return Bounds.Of(outlines.SelectMany(outline => outline));
    }

    #region Every mark ****************************************************************

    public static IEnumerable<object[]> EveryMark()
    {
        return Marks.All.Select(maker => new object[] { maker.Key });
    }

    [Theory]
    [MemberData(nameof(EveryMark))]
    public void Every_mark_draws_from_its_starting_settings(string key)
    {
        var made = Marks.Named(key)!.Make(new Dictionary<string, double>(), Nanometers);

        Assert.Null(made.Problem);
        Assert.NotEmpty(made.Outlines);
        Assert.All(made.Outlines, outline => Assert.True(outline.Count >= 3));
    }

    ///<summary>Centered on where the pointer holds it, which is what carrying one puts down - or, for one drawn round another's middle, inside that one's box.</summary>
    [Theory]
    [MemberData(nameof(EveryMark))]
    public void Every_mark_is_centered_on_the_origin(string key)
    {
        var maker = Marks.Named(key)!;
        var box = BoxOf(maker.Make(new Dictionary<string, double>(), Nanometers).Outlines);

        //An inductor's underpass and vias sit where the spiral's middle puts them, inside the spiral's own box.
        if (maker.PlacedOn is string on)
        {
            var under = BoxOf(Marks.Named(on)!.Make(new Dictionary<string, double>(), Nanometers).Outlines);

            Assert.True(box.Left >= under.Left && box.Right <= under.Right && box.Bottom >= under.Bottom && box.Top <= under.Top);

            return;
        }

        //Within a unit or two of rounding, or within the ring - the one mark whose middle is the ring's.
        if (key == "ring")
        {
            Assert.InRange(box.Left + box.Right, -2, 2);

            return;
        }

        Assert.InRange((long)box.Left + box.Right, -2, 2);
        Assert.InRange((long)box.Bottom + box.Top, -2, 2);
    }

    [Fact]
    public void Every_key_is_its_own()
    {
        Assert.Equal(Marks.All.Count, Marks.All.Select(maker => maker.Key).Distinct().Count());
    }

    ///<summary>A count that is not a whole number, or nothing, is a problem said rather than a strange shape.</summary>
    [Fact]
    public void A_setting_that_cannot_be_drawn_is_said_rather_than_drawn()
    {
        var vernier = Marks.Named("vernier")!;

        Assert.NotNull(vernier.Make(new Dictionary<string, double> { ["bars"] = 2.5 }, Nanometers).Problem);
        Assert.NotNull(vernier.Make(new Dictionary<string, double> { ["pitch"] = 0 }, Nanometers).Problem);
        Assert.NotNull(vernier.Make(new Dictionary<string, double> { ["width"] = 10, ["pitch"] = 4 }, Nanometers).Problem);

        var frame = Marks.Named("frame")!;

        Assert.NotNull(frame.Make(new Dictionary<string, double> { ["line"] = 3000 }, Nanometers).Problem);
        Assert.Empty(frame.Make(new Dictionary<string, double> { ["line"] = 3000 }, Nanometers).Outlines);
    }

    #endregion ***********************************************************************



    #region Alignment *****************************************************************

    [Fact]
    public void A_die_frame_is_the_die_less_what_is_inside_it()
    {
        var frame = Marks.DieFrame(5000, 3000, 100);

        Assert.Equal(4, frame.Count);
        Assert.Equal((5000.0 * 3000) - (4800.0 * 2800), Area(frame));
        Assert.Equal(new Bounds(-2500, -1500, 2500, 1500), BoxOf(frame));

        //Four bars that meet without overlapping: merged, they are the same area.
        Assert.Equal(Area(frame), Area(Booleans.Merge(frame)));
    }

    [Fact]
    public void A_cross_is_two_arms_less_the_square_they_share()
    {
        var cross = Marks.Cross(100, 10);

        Assert.Equal(12, cross.Count);
        Assert.Equal((2 * 100.0 * 10) - (10.0 * 10), Math.Abs(Measure.AreaOf(cross)));
        Assert.Equal(new Bounds(-50, -50, 50, 50), Bounds.Of(cross));
    }

    ///<summary>The window and the cross it is for, laid over each other, leave the gap and do not touch.</summary>
    [Fact]
    public void A_window_leaves_the_gap_round_its_cross()
    {
        var cross = Marks.Cross(100, 10);
        var window = Marks.Window(100, 10, 2);

        Assert.Equal(4, window.Count);
        Assert.Empty(Booleans.Combine(new[] { cross }, window, BooleanOperation.And));

        //Grown by just under the gap the two still miss each other; grown by the gap, they touch.
        Assert.Empty(Booleans.Combine(Booleans.Grow(new[] { cross }, 1), window, BooleanOperation.And));
        Assert.NotEmpty(Booleans.Combine(Booleans.Grow(new[] { cross }, 3), window, BooleanOperation.And));
    }

    [Fact]
    public void A_vernier_is_its_bars_at_its_pitch_with_the_middle_one_longer()
    {
        var vernier = Marks.Vernier(5, 2, 20, 4);

        Assert.Equal(5, vernier.Count);

        var lefts = vernier.Select(bar => Bounds.Of(bar).Left).ToList();

        Assert.Equal(new[] { 4, 4, 4, 4 }, lefts.Zip(lefts.Skip(1), (one, next) => next - one));
        Assert.Equal(30, Bounds.Of(vernier[2]).Height);
        Assert.Equal(20, Bounds.Of(vernier[0]).Height);
    }

    #endregion ***********************************************************************



    #region Test structures ***********************************************************

    [Fact]
    public void Lines_and_spaces_come_in_fives_each_group_narrower()
    {
        var lines = Marks.LinesAndSpaces(10, 1, 4, 50);

        //Four widths between ten and one at a constant ratio: 10, 4.64, 2.15 and 1, rounded.
        Assert.Equal(20, lines.Count);

        var widths = lines.Select(line => Bounds.Of(line).Width).Distinct().ToList();

        Assert.Equal(new long[] { 10, 5, 2, 1 }, widths);

        //Within a group a line and the space after it are the same width.
        Assert.Equal(20, Bounds.Of(lines[1]).Left - Bounds.Of(lines[0]).Left);
    }

    [Fact]
    public void A_greek_cross_is_one_shape_with_a_pad_on_every_arm()
    {
        var greek = Marks.GreekCross(10, 100, 80);
        var box = Bounds.Of(greek);

        //Arm to the pad's middle and out to its far edge, either side.
        Assert.Equal(2 * (100 + 80), box.Width);
        Assert.Equal(2 * (100 + 80), box.Height);

        //The pads meet the ends of the arms rather than overlapping them, so the area is the two together.
        double arms = (2 * 200.0 * 10) - (10.0 * 10);
        double pads = 4 * 80.0 * 80;

        Assert.Equal(arms + pads, Math.Abs(Measure.AreaOf(greek)));
    }

    #endregion ***********************************************************************



    #region Photonics *****************************************************************

    [Fact]
    public void A_ring_resonator_is_its_ring_and_its_waveguide_a_gap_apart()
    {
        var made = Marks.RingResonator(10_000, 500, 200, 30_000);

        //The ring as one keyhole outline, and the straight waveguide.
        Assert.Equal(2, made.Count);

        double ring = Math.PI * ((10_250.0 * 10_250) - (9_750.0 * 9_750));

        //Within what 256 straight sides take off a circle.
        Assert.InRange(Math.Abs(Measure.AreaOf(made[0])), ring * 0.999, ring * 1.001);

        var bus = Bounds.Of(made[1]);

        Assert.Equal(-10_250 - 200, bus.Top);
        Assert.Equal(500, bus.Height);
        Assert.Equal(30_000, bus.Width);
    }

    [Fact]
    public void A_grating_is_its_teeth_at_its_period()
    {
        var grating = Marks.Grating(20, 630, 315, 10_000);

        Assert.Equal(20, grating.Count);
        Assert.Equal(630, Bounds.Of(grating[1]).Left - Bounds.Of(grating[0]).Left);
        Assert.All(grating, tooth => Assert.Equal(315, Bounds.Of(tooth).Width));
    }

    #endregion ***********************************************************************



    #region Microfluidics *************************************************************

    [Fact]
    public void A_serpentine_is_one_channel_with_no_square_corner()
    {
        var serpentine = Marks.Serpentine(100, 2000, 6, 300);
        var box = Bounds.Of(serpentine);

        Assert.Equal(2000 + 100, box.Width);
        Assert.Equal((5 * 300) + 100, box.Height);

        //A square corner would be two corners at a right angle with nothing between them; every turn is an arc.
        Assert.True(serpentine.Count > 40);
    }

    [Fact]
    public void A_chamber_is_one_shape_from_port_to_port()
    {
        var chamber = Marks.Chamber(1000, 100, 1500, 600);

        Assert.Single(chamber);

        var box = Bounds.Of(chamber[0]);

        Assert.Equal(2 * (500 + 1500 + 300), box.Width);
        Assert.Equal(1000, box.Height);
    }

    #endregion ***********************************************************************



    #region Families, units and limits ************************************************

    ///
    ///**The layers of one structure share one set of numbers** (2026-11-01): a kind in a family names a first kind that
    ///is in the catalog, and every number it has by the same name as that kind's starts the same and is counted the
    ///same - or the panel, which keeps one set for the family, would draw one layer from a number meant for another.
    ///Each says it in its own words: the window's arm is "the cross's arm".
    ///
    [Fact]
    public void A_family_shares_its_numbers_with_the_kind_it_is_named_for()
    {
        foreach (var maker in Marks.All.Where(maker => maker.Family != maker.Key))
        {
            var first = Marks.Named(maker.Family);

            Assert.NotNull(first);

            foreach (var setting in maker.Settings)
            {
                if (first!.Settings.FirstOrDefault(other => other.Name == setting.Name) is MarkSetting same)
                    Assert.Equal((same.Start, same.Whole, same.Angle), (setting.Start, setting.Whole, setting.Angle));
            }
        }

        Assert.Equal("cross", Marks.Named("window")!.Family);
        Assert.Equal("spiral", Marks.Named("inductorvias")!.Family);
        Assert.Equal("spiral", Marks.Named("underpass")!.PlacedOn);
    }

    [Fact]
    public void A_setting_says_what_it_is_counted_in()
    {
        Assert.Equal("µm", Marks.Named("spiral")!.Settings.First(setting => setting.Name == "width").Unit);
        Assert.Equal("", Marks.Named("spiral")!.Settings.First(setting => setting.Name == "turns").Unit);
        Assert.Equal("°", Marks.Named("bowtie")!.Settings.First(setting => setting.Name == "angle").Unit);
    }

    ///<summary>Past what one placement puts down, a mark is said rather than drawn - a fill grows as the square of its area.</summary>
    [Fact]
    public void More_outlines_than_one_placement_puts_down_is_said()
    {
        var fill = Marks.Named("fill")!.Make(new Dictionary<string, double> { ["width"] = 1000, ["height"] = 1000 }, Nanometers);

        Assert.Empty(fill.Outlines);
        Assert.Contains("62500 squares", fill.Problem);
    }

    #endregion ***********************************************************************



    #region Analog ********************************************************************

    ///
    ///A square spiral's outside is inner + 2nw + 2(n - 1)s, the outer diameter the inductance formulas are written for.
    ///Its outline is its middle's length times its width, ends squared off half a width past - 1,715 along, so 17,250 -
    ///and merged it loses none of that, so no turn runs into another.
    ///
    [Fact]
    public void A_spiral_inductor_is_as_big_as_its_turns_say_and_no_turn_meets_another()
    {
        var spiral = Marks.SpiralInductor(3, 10, 5, 100, 50);

        Assert.Equal(2, spiral.Count);
        Assert.Equal(100 + (2 * 3 * 10) + (2 * 2 * 5), Bounds.Of(spiral[0]).Height);
        Assert.Equal(17_250, Math.Abs(Measure.AreaOf(spiral[0])));
        Assert.Equal(17_250, Area(Booleans.Merge(new[] { spiral[0] })));

        //The landing the underpass comes up to, apart from the spiral.
        Assert.Empty(Booleans.Combine(new[] { spiral[0] }, new[] { spiral[1] }, BooleanOperation.And));
    }

    ///<summary>The underpass runs from under one end to under the other, beneath the turns; every via is inside both it and the spiral, so it joins them.</summary>
    [Fact]
    public void An_inductors_underpass_and_vias_land_on_its_two_ends()
    {
        var spiral = Marks.SpiralInductor(3, 10, 5, 100, 50);
        var underpass = Marks.InductorUnderpass(3, 10, 5, 100, 50);
        var vias = Marks.InductorVias(3, 10, 5, 100, 50, 2, 1);

        Assert.Single(underpass);

        //Three to a row across a trace ten wide at two and one apart: a block of nine at each end.
        Assert.Equal(18, vias.Count);

        Assert.Empty(Booleans.Combine(vias, underpass, BooleanOperation.Not));
        Assert.Empty(Booleans.Combine(vias, spiral, BooleanOperation.Not));
        Assert.NotEmpty(Booleans.Combine(underpass, new[] { spiral[0] }, BooleanOperation.And));
    }

    ///<summary>Three turns of ten at five round a hole of a hundred: out to 180, a mean of 140 and a fill of 80 / 280.</summary>
    [Fact]
    public void A_spiral_inductance_is_mohans()
    {
        Assert.InRange(Marks.SpiralInductance(3, 10, 5, 100), 2.074e-9, 2.076e-9);
        Assert.Equal("About 2.07 nH, by Mohan's formula for a square spiral", Marks.Named("spiral")!.NoteFor(new Dictionary<string, double>()));
    }

    [Fact]
    public void A_loop_inductor_is_one_turn_with_its_leads_out_of_the_bottom()
    {
        var loop = Marks.LoopInductor(200, 10, 20, 100);
        var box = Bounds.Of(loop[0]);

        Assert.Single(loop);

        //Flat to flat across, and from the top flat's outside down to the ends of the leads.
        Assert.InRange(box.Width, 199, 201);
        Assert.InRange(box.Height, 294, 296);

        Assert.Equal("About 473 pH, as a round loop of the same area", Marks.Named("loop")!.NoteFor(new Dictionary<string, double>()));
    }

    ///<summary>Four fingers at a pitch of ten: each plate a spine 35 long and two fingers, and the plates never touch.</summary>
    [Fact]
    public void An_interdigitated_capacitor_is_two_combs_whose_fingers_alternate()
    {
        var capacitor = Marks.InterdigitatedCapacitor(4, 100, 5, 5, 5, 20);

        Assert.Equal(2, capacitor.Count);
        Assert.Equal((35.0 * 20) + (2 * 5.0 * 100), Math.Abs(Measure.AreaOf(capacitor[0])));
        Assert.Empty(Booleans.Combine(new[] { capacitor[0] }, new[] { capacitor[1] }, BooleanOperation.And));
        Assert.Equal(20 + 100 + 5 + 20, BoxOf(capacitor).Height);
    }

    ///<summary>One strip: 326 along its middle at a width of two, and a head of six at each end - and 158.6 squares, its ten corners counted at 0.56.</summary>
    [Fact]
    public void A_meander_resistor_is_one_strip_between_its_heads()
    {
        var resistor = Marks.MeanderResistor(2, 50, 6, 2, 6);

        Assert.Single(resistor);
        Assert.Equal((326.0 * 2) + (2 * 36), Math.Abs(Measure.AreaOf(resistor[0])));
        Assert.Equal(158.6, Marks.ResistorSquares(2, 50, 6, 2), 6);
    }

    ///<summary>Sides 26 and 16 long between the ring's corners at its middle: five and three contacts at a pitch of at least five, twice over.</summary>
    [Fact]
    public void A_guard_ring_has_its_contacts_down_its_middle_all_the_way_round()
    {
        var ring = Marks.GuardRing(20, 10, 6);
        var contacts = Marks.GuardRingContacts(20, 10, 6, 2, 5);

        Assert.Equal((32.0 * 22) - (20.0 * 10), Area(ring));
        Assert.Equal(16, contacts.Count);
        Assert.Empty(Booleans.Combine(contacts, ring, BooleanOperation.Not));
    }

    ///<summary>Four quarters, none touching another and none round a hole - nothing under the inductor that a current could circle in.</summary>
    [Fact]
    public void A_patterned_ground_shield_never_closes_a_loop()
    {
        var shield = Marks.PatternedGroundShield(200_000, 2_000, 1_000, 6_000);
        var merged = Booleans.MergeToRings(shield);

        Assert.Equal(4, shield.Count);
        Assert.Equal(4, merged.Count);
        Assert.All(merged, quarter => Assert.Empty(quarter.Holes));
        Assert.Equal(new Bounds(-100_000, -100_000, 100_000, 100_000), BoxOf(shield));
    }

    #endregion ***********************************************************************



    #region RF and microwave **********************************************************

    [Fact]
    public void A_coplanar_waveguide_is_its_line_between_two_grounds_a_gap_away()
    {
        var line = Marks.CoplanarWaveguide(500, 20, 10, 60);

        Assert.Equal(3, line.Count);
        Assert.Equal(20, Bounds.Of(line[1]).Height);
        Assert.Equal(10, Bounds.Of(line[1]).Bottom - Bounds.Of(line[0]).Top);
        Assert.Equal(10, Bounds.Of(line[2]).Bottom - Bounds.Of(line[1]).Top);
    }

    ///
    ///Drawn as its gaps: twice a gap along its whole length, and round its open start - 4,000 along at gaps of 6 either
    ///side of a line of 10 is 48,132 square microns, within what the turns' straight steps take off their curves.
    ///
    [Fact]
    public void A_cpw_resonator_is_its_gaps_round_a_line_as_long_as_asked()
    {
        var gaps = Marks.CpwResonator(4_000_000, 10_000, 6_000, 400_000, 100_000);

        Assert.Single(gaps);
        Assert.InRange(Area(gaps), 4.8132e10 * 0.999, 4.8132e10 * 1.001);
        Assert.InRange(Marks.QuarterWaveGigahertz(4000, 6.35), 7.43, 7.44);
    }

    ///<summary>One outline: the four arms round their hole, and a port out of each corner - 94,400 in all.</summary>
    [Fact]
    public void A_branch_line_coupler_is_one_square_with_a_port_at_each_corner()
    {
        var coupler = Marks.BranchLineCoupler(700, 36, 20, 20, 200);
        var box = Bounds.Of(coupler[0]);

        Assert.Single(coupler);
        Assert.Equal(700 + 20 + (2 * 200), box.Width);
        Assert.Equal(700 + 36, box.Height);
        Assert.Equal(94_400, Area(coupler));
    }

    ///<summary>Ports at 0° and 180° set its width; the two at 60° and 120° its top, and the ring alone its bottom.</summary>
    [Fact]
    public void A_rat_race_is_a_ring_with_its_ports_a_sixth_of_a_turn_apart()
    {
        var coupler = Marks.RatRaceCoupler(700_000, 11_000, 21_000, 200_000);
        var box = Bounds.Of(coupler[0]);

        Assert.Single(coupler);
        Assert.InRange(box.Width, 1_810_998, 1_811_002);
        Assert.InRange(box.Height, 1_494_933, 1_494_939);

        //The ring's own area and four ports out of it, the hole left a hole.
        Assert.InRange(Area(coupler), 6.50e10, 6.53e10);
    }

    [Fact]
    public void A_coupled_line_filter_runs_each_line_beside_the_next_for_a_quarter()
    {
        var filter = Marks.CoupledLineFilter(3, 720, 20, 10, 200);

        Assert.Equal(5, filter.Count);
        Assert.Equal(1440, Bounds.Of(filter[2]).Width);

        for (int each = 0; each < 4; each++)
        {
            var one = Bounds.Of(filter[each]);
            var next = Bounds.Of(filter[each + 1]);

            Assert.Equal(10, next.Bottom - one.Top);
            Assert.Equal(720, Math.Min(one.Right, next.Right) - Math.Max(one.Left, next.Left));
        }
    }

    [Fact]
    public void A_stepped_impedance_filter_is_one_line_of_its_sections()
    {
        var filter = Marks.SteppedImpedanceFilter(3, 100, 6, 120, 200, 20, 200);

        Assert.Single(filter);
        Assert.Equal((2 * 200) + (4 * 200) + (3 * 120), Bounds.Of(filter[0]).Width);
        Assert.Equal(100, Bounds.Of(filter[0]).Height);
        Assert.Equal((2 * 200.0 * 20) + (4 * 200.0 * 6) + (3 * 120.0 * 100), Area(filter));
    }

    [Fact]
    public void A_radial_stub_is_one_fan_on_the_end_of_its_line()
    {
        var stub = Marks.RadialStub(300_000, 60, 21_000, 200_000);
        var box = Bounds.Of(stub[0]);

        Assert.Single(stub);
        Assert.InRange(box.Width, 499_970, 500_001);
        Assert.InRange(box.Height, 299_998, 300_002);
    }

    ///<summary>Each ring open where its cut is - the outer at the top, the inner at the bottom - and closed on the other side.</summary>
    [Fact]
    public void A_split_ring_resonator_is_two_rings_cut_on_opposite_sides()
    {
        var rings = Marks.SplitRingResonator(100_000, 10_000, 10_000, 10_000);
        var outer = Bounds.Of(rings[0]);
        var inner = Bounds.Of(rings[1]);
        int middle = (outer.Left + outer.Right) / 2;

        Assert.Equal(2, rings.Count);
        Assert.All(Booleans.MergeToRings(rings), ring => Assert.Empty(ring.Holes));

        bool solid(List<Element.Point> ring, int y)
        {
            return Booleans.Combine(new[] { ring }, new[] { Shapes.Rectangle(middle, y, 2_000, 2_000) }, BooleanOperation.And).Count > 0;
        }

        Assert.False(solid(rings[0], outer.Top - 5_000));
        Assert.True(solid(rings[0], outer.Bottom + 5_000));
        Assert.True(solid(rings[1], inner.Top - 5_000));
        Assert.False(solid(rings[1], inner.Bottom + 5_000));
    }

    #endregion ***********************************************************************



    #region Antennas ******************************************************************

    ///<summary>The patch less the notch either side of its feed, and the feed from the bottom of the notch down past the patch.</summary>
    [Fact]
    public void A_patch_antenna_is_one_shape_fed_up_its_notch()
    {
        var patch = Marks.PatchAntenna(1600, 1260, 380, 20, 20, 500);

        Assert.Single(patch);
        Assert.Equal(1600, Bounds.Of(patch[0]).Width);
        Assert.Equal(1260 + 500, Bounds.Of(patch[0]).Height);
        Assert.Equal((1600.0 * 1260) - (2 * 20.0 * 380) + (20.0 * 500), Area(patch));
    }

    [Fact]
    public void A_dipole_is_two_arms_the_gap_apart()
    {
        var dipole = Marks.DipoleAntenna(1250, 20, 20, 300);

        Assert.Equal(2, dipole.Count);
        Assert.Equal(1250, BoxOf(dipole).Width);
        Assert.Equal(20, Bounds.Of(dipole[1]).Left - Bounds.Of(dipole[0]).Right);
        Assert.Equal(20 + 300, BoxOf(dipole).Height);
    }

    ///<summary>Points the gap apart, each flaring 30° either side: 615 out and 355 up from a point ten wide.</summary>
    [Fact]
    public void A_bowtie_is_two_triangles_point_to_point()
    {
        var bowtie = Marks.BowtieAntenna(1250, 60, 20, 20);

        Assert.Equal(2, bowtie.Count);
        Assert.Equal(1250, BoxOf(bowtie).Width);
        Assert.InRange(BoxOf(bowtie).Height, 729, 731);
        Assert.Equal(20, Bounds.Of(bowtie[0]).Left - Bounds.Of(bowtie[1]).Right);
    }

    ///<summary>Two arms that never meet, about a spacing apart all the way out - grown by half of one they are still clear, by a little more than one they touch.</summary>
    [Fact]
    public void A_spiral_antennas_arms_wind_a_spacing_apart()
    {
        var spiral = Marks.SpiralAntenna(3, 10_000, 10_000, 30_000);

        Assert.Equal(2, spiral.Count);
        Assert.Empty(Booleans.Combine(Booleans.Grow(new[] { spiral[0] }, 5_000), new[] { spiral[1] }, BooleanOperation.And));
        Assert.NotEmpty(Booleans.Combine(Booleans.Grow(new[] { spiral[0] }, 11_000), new[] { spiral[1] }, BooleanOperation.And));
    }

    #endregion ***********************************************************************



    #region Digital and chip **********************************************************

    ///<summary>One net: the first H 1,000 across and two levels inside it reach 875 out from the middle, the wire half of 4 past that.</summary>
    [Fact]
    public void A_clock_h_tree_is_one_net_out_to_its_leaves()
    {
        var tree = Marks.ClockHTree(3, 1000, 4);

        Assert.Single(tree);
        Assert.Equal(new Bounds(-877, -877, 877, 877), Bounds.Of(tree[0]));
        Assert.StartsWith("64 leaves", Marks.Named("htree")!.NoteFor(new Dictionary<string, double>()));
    }

    [Fact]
    public void Power_rails_are_a_row_apart()
    {
        var rails = Marks.PowerRails(6, 2720, 480, 100_000);

        Assert.Equal(6, rails.Count);
        Assert.Equal(2720, Bounds.Of(rails[1]).Bottom - Bounds.Of(rails[0]).Bottom);
        Assert.All(rails, rail => Assert.Equal(480, Bounds.Of(rail).Height));
    }

    ///
    ///Fourteen a side on a die of 2,000 at a pitch of 120, and no two pads nearer than two in a row are - grown by just
    ///under half the 40 between them every pad is still on its own, grown by just over the rows run together.
    ///
    [Fact]
    public void A_pad_ring_keeps_its_pads_a_pitch_apart_and_its_openings_inside_them()
    {
        var pads = Marks.PadRing(2000, 80, 120, 50);
        var openings = Marks.PadOpenings(2000, 80, 120, 50, 5);

        Assert.Equal(56, pads.Count);
        Assert.Equal(56, openings.Count);
        Assert.Empty(Booleans.Combine(openings, pads, BooleanOperation.Not));
        Assert.Equal(56, Booleans.Grow(pads, 19).Count);
        Assert.True(Booleans.Grow(pads, 21).Count < 56);
        Assert.Equal("56 pads, 14 a side", Marks.Named("pads")!.NoteFor(new Dictionary<string, double>()));
    }

    [Fact]
    public void A_dummy_fill_says_how_much_of_its_area_it_covers()
    {
        Assert.Equal(625, Marks.DummyFill(100, 100, 2, 2).Count);
        Assert.Equal("625 squares, 25% of the area", Marks.Named("fill")!.NoteFor(new Dictionary<string, double>()));
    }

    ///<summary>The wire a spacing from both combs everywhere: grown by a unit less it is clear of them, by a unit more it touches.</summary>
    [Fact]
    public void A_comb_and_serpentine_keeps_the_wire_a_spacing_from_both_combs()
    {
        var structure = Marks.CombAndSerpentine(5, 1_000, 1_000, 50_000, 10_000);
        var combs = new[] { structure[1], structure[2] };

        Assert.Equal(3, structure.Count);
        Assert.Empty(Booleans.Combine(Booleans.Grow(new[] { structure[0] }, 999), combs, BooleanOperation.And));
        Assert.NotEmpty(Booleans.Combine(Booleans.Grow(new[] { structure[0] }, 1_001), combs, BooleanOperation.And));
    }

    #endregion ***********************************************************************
}
