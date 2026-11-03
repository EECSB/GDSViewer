using System.Globalization;
using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///A report database read back and placed on a layout - KLayout's, and this project's own.
///
///**The fixtures are KLayout's**, written by 0.30.9 and checked in, so these run without it: one report built
///to hold every kind of value, tag, cell and category the format has, and a real DRC run over the 555 timer
///twice - in deep mode, where items are filed under the cells they were found in, and flat, where they are
///all in the top. The flat one is the answer the deep one has to come to once it is placed. See the fixtures'
///README for the scripts.
///</summary>
public class DrcReportReadingTests
{
    #region Layouts *********************************************************************

    private const string TimerTop = "tt_um_vaf_555_timer";

    private static readonly Lazy<GDS> timer = new Lazy<GDS>(() => new GDS(GdsTestData.ReadSample(Path.Combine("Sky130 Designs", "tt_um_vaf_555_timer.gds"))));

    private static readonly Lazy<FlattenedLayout> timerLayout = new Lazy<FlattenedLayout>(() => GdsFlattener.Flatten(timer.Value));

    ///<summary>One cell, named as the every-value report's top is, on a nanometer grid.</summary>
    private static GDS Top(string name = "TOP")
    {
        byte[] stamps = GdsTestData.Timestamps();

        return new GDS(GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(name)),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 100, 0, 100, 100, 0, 100, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));
    }

    ///<summary>
    ///A cell too narrow for its rule, placed three times: plainly, moved, and mirrored and turned a quarter.
    ///
    ///The three faults are in the same cell at three different places, which is what a report has to be able
    ///to tell apart - and the last placement is the kind that a reader getting the order of a mirror and a
    ///turn the wrong way round would put somewhere else.
    ///</summary>
    private static GDS PlacedThreeWays()
    {
        byte[] stamps = GdsTestData.Timestamps();

        return new GDS(GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("PLACED")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(68)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(100, 200, 200, 200, 200, 1200, 100, 1200, 100, 200)),
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

            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.STRANS, new byte[] { 0x80, 0x00 }),
            GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(90)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(-3000, 4000)),
            GdsTestData.Record(RecordType.ENDEL),

            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));
    }

    private const string NarrowDeck = "layer met1 68/20\nrule met1.1 width met1 140 \"Met1 width\"";

    private static DrcReportFile Fixture(string name, GDS gds, FlattenedLayout layout)
    {
        return DrcReport.Read(GdsTestData.ReadFixture(name), gds, layout);
    }

    private static DrcReportFile EveryValue()
    {
        var gds = Top();

        return Fixture("klayout-every-value.lyrdb", gds, GdsFlattener.Flatten(gds));
    }

    private static List<(int X, int Y)> Points(IEnumerable<Element.Point> marker)
    {
        return marker.Select(point => (point.X, point.Y)).ToList();
    }

    ///<summary>A marker as a set of corners, which is what two tools that start a ring at different corners agree on.</summary>
    private static string Shape(string category, IEnumerable<Element.Point> marker)
    {
        var corners = marker.Select(point => FormattableString.Invariant($"{point.X},{point.Y}")).Distinct().OrderBy(corner => corner, StringComparer.Ordinal);

        return category + "|" + string.Join(";", corners);
    }

    #endregion **************************************************************************



    #region What a report says about itself *********************************************

    [Fact]
    public void The_report_says_what_wrote_it_and_which_cell_it_is_about()
    {
        var report = EveryValue();

        Assert.Equal("klayout-every-value.rb", report.Generator);
        Assert.Equal("sample.gds", report.OriginalFile);
        Assert.Equal("TOP", report.TopCell);
        Assert.Empty(report.Problems);
    }

    ///<summary>
    ///Every category, a subcategory named after the one it sits in, and the descriptions with their markup
    ///characters given back.
    ///</summary>
    [Fact]
    public void Categories_come_back_in_order_with_what_each_holds()
    {
        var report = EveryValue();

        Assert.Equal(new[] { "m1.width", "m1.width / sub.cat", "plain", "has space" }, report.Categories.Select(category => category.Name));
        Assert.Equal("Metal 1 width < 0.14 & stuff <tag>", report.Categories[0].Description);
        Assert.Equal("A subcategory", report.Categories[1].Description);
        Assert.All(report.Categories, category => Assert.Equal(1, category.Items));
        Assert.Equal(1, report.Categories[0].Waived);
    }

    [Fact]
    public void An_item_keeps_its_tags_comment_and_what_was_done_with_it()
    {
        var report = EveryValue();

        var first = report.Items[0];

        Assert.True(first.Waived);
        Assert.Contains("mine", first.Tags);
        Assert.Equal("a comment", first.Comment);
        Assert.False(first.Visited);
        Assert.True(report.Items[3].Visited);
        Assert.Equal(1, report.Waived);
    }

    #endregion **************************************************************************



    #region Every kind of value *********************************************************

    ///<summary>
    ///Six values with a place and three without: a polygon, an edge pair, an edge, a box, a path and a label
    ///drawn; a number, a piece of text and the label's words kept as notes.
    ///</summary>
    [Fact]
    public void Every_kind_of_value_is_drawn_or_kept()
    {
        var item = EveryValue().Items[0];

        Assert.Equal(6, item.Markers.Count);
        Assert.Equal(new[] { "lbl", "2.5", "some text, with 'quotes'" }, item.Notes);
    }

    [Fact]
    public void A_polygon_box_and_edge_land_in_database_units()
    {
        var markers = EveryValue().Items[0].Markers;

        Assert.Equal(new[] { (0, 0), (0, 2250), (1500, 2250), (1500, 0) }, Points(markers[0]));
        Assert.Equal(new[] { (0, 0), (3000, 4000) }, Points(markers[2]));
        Assert.Equal(new[] { (-1000, -2000), (-1000, 4000), (3000, 4000), (3000, -2000) }, Points(markers[3]));
    }

    ///<summary>An edge pair is the area between its two edges, the four ends taken in turn.</summary>
    [Fact]
    public void An_edge_pair_is_the_area_between_its_edges()
    {
        var pair = EveryValue().Items[0].Markers[1];

        Assert.Equal(new[] { (0, 0), (0, 1000), (1000, 1000), (1000, 0) }, Points(pair));
    }

    ///<summary>A path half a micron wide along ten microns is a band a quarter of a micron either side of it.</summary>
    [Fact]
    public void A_path_is_its_outline_at_its_width()
    {
        var path = EveryValue().Items[0].Markers[4];

        var box = Bounds.Of(path);

        Assert.Equal(new Bounds(0, -250, 10000, 250), box);
    }

    [Fact]
    public void A_label_is_a_point_where_it_is_anchored()
    {
        var label = EveryValue().Items[0].Markers[5];

        Assert.Equal(new[] { (1500, 2500) }, Points(label));
    }

    #endregion **************************************************************************



    #region Placed the way KLayout places it ********************************************

    ///<summary>
    ///A cell placed by its reference: `r90 *1 10.5,-2.25`, so the polygon's corner at 10,0 is turned to 0,10
    ///and moved. The hole inside it is not drawn.
    ///</summary>
    [Fact]
    public void A_cell_is_placed_by_its_reference()
    {
        var item = EveryValue().Items[1];

        Assert.Equal("SUB", item.Cell);
        Assert.True(item.Placed);
        Assert.Equal(new[] { (10500, -2250), (500, -2250), (500, 7750), (10500, 7750) }, Points(item.Markers[0]));
    }

    ///<summary>
    ///`m0 *2 -1,3`: mirrored across the x axis, doubled and moved. The symmetric edge pair it holds comes out
    ///as the rectangle between its edges, the second edge taken the other way round so the ring does not
    ///cross itself.
    ///</summary>
    [Fact]
    public void A_mirrored_and_magnified_reference_places_its_cell()
    {
        var item = EveryValue().Items[2];

        Assert.Equal(new[] { (-1000, 3000), (-1000, 1000), (0, 1000), (0, 3000) }, Points(item.Markers[0]));
    }

    ///<summary>
    ///**The test that says the transforms are KLayout's.** The same check run deep and flat: every item the
    ///deep run filed under a cell - turned a quarter, three quarters, or mirrored into place - lands exactly on
    ///an item the flat run found in the top.
    ///</summary>
    [Fact]
    public void A_deep_report_lands_where_the_flat_one_does()
    {
        var deep = Fixture("klayout-deep.lyrdb", timer.Value, timerLayout.Value);
        var flat = Fixture("klayout-flat.lyrdb", timer.Value, timerLayout.Value);

        Assert.Empty(deep.Problems);
        Assert.Empty(flat.Problems);

        var found = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in flat.Items)
        {
            foreach (var marker in item.Markers)
                found.Add(Shape(item.Category, marker));
        }

        int compared = 0;

        foreach (var item in deep.Items)
        {
            foreach (var marker in item.Markers)
            {
                Assert.Contains(Shape(item.Category, marker), found);

                compared++;
            }
        }

        Assert.Equal(57, deep.Items.Count);
        Assert.Equal(57, compared);
        Assert.Contains(deep.Items, item => item.Cell != TimerTop);
    }

    ///<summary>
    ///A cell the report gives no reference for is found in the layout instead. timer_core sits at
    ///-97.08,-74.05 in the 555 timer, so its origin lands there.
    ///</summary>
    [Fact]
    public void A_cell_the_report_does_not_place_is_found_in_the_layout()
    {
        string xml = Report(TimerTop, "<cell><name>timer_core</name><variant/><references/></cell>", "timer_core", "edge: (0,0;0,0)");

        var report = DrcReport.Read(xml, timer.Value, timerLayout.Value);

        var item = Assert.Single(report.Items);

        Assert.True(item.Placed);
        Assert.Equal(new[] { (-97080, -74050) }, Points(item.Markers[0]));
        Assert.Empty(report.Problems);
    }

    [Fact]
    public void An_item_nothing_can_place_is_listed_and_not_drawn()
    {
        string xml = Report(TimerTop, "", "nowhere", "box: (0,0;1,1)");

        var report = DrcReport.Read(xml, timer.Value, timerLayout.Value);

        var item = Assert.Single(report.Items);

        Assert.False(item.Placed);
        Assert.Empty(item.Markers);
        Assert.Equal(1, report.Unplaced);
        Assert.Contains(report.Problems, problem => problem.Contains("nowhere", StringComparison.Ordinal));
    }

    ///<summary>A report made for another layout still draws, and says it may not be about this one.</summary>
    [Fact]
    public void A_report_about_a_cell_this_layout_does_not_have_says_so()
    {
        string xml = Report("SOMETHING_ELSE", "", "SOMETHING_ELSE", "box: (0,0;1,1)");

        var report = DrcReport.Read(xml, timer.Value, timerLayout.Value);

        Assert.Equal(new[] { (0, 0), (0, 1000), (1000, 1000), (1000, 0) }, Points(report.Items[0].Markers[0]));
        Assert.Contains(report.Problems, problem => problem.Contains("SOMETHING_ELSE", StringComparison.Ordinal));
    }

    ///<summary>A report with one item, in the shape KLayout writes, around whatever a test needs to vary.</summary>
    private static string Report(string topCell, string cells, string itemCell, string value)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<report-database>
 <description>hand made</description>
 <top-cell>{topCell}</top-cell>
 <categories><category><name>only</name><description/><categories/></category></categories>
 <cells>{cells}</cells>
 <items>
  <item>
   <tags/>
   <category>only</category>
   <cell>{itemCell}</cell>
   <visited>false</visited>
   <multiplicity>1</multiplicity>
   <values><value>{value}</value></values>
  </item>
 </items>
</report-database>
";
    }

    #endregion **************************************************************************



    #region This project's own reports **************************************************

    ///<summary>
    ///**The writer and the reader agree.** Every fault this run found, written out and read back, is exactly
    ///where it was found - including the one in a cell mirrored and turned into place, which is the one the
    ///writer used to file with the top's coordinates under the cell's name.
    ///</summary>
    [Fact]
    public void What_this_writes_reads_back_where_it_was_found()
    {
        var gds = PlacedThreeWays();
        var layout = GdsFlattener.Flatten(gds);
        var deck = DrcDeck.Parse(NarrowDeck);
        var result = Drc.Check(deck, layout);

        Assert.Equal(3, result.Violations.Count);

        var back = DrcReport.Read(DrcReport.Write(result, deck, gds, "TOP"), gds, layout);

        Assert.Empty(back.Problems);
        Assert.Equal(3, back.Items.Count);

        for (int i = 0; i < 3; i++)
            Assert.Equal(Points(result.Violations[i].Marker), Points(back.Items[i].Markers[0]));
    }

    ///<summary>
    ///One cell met in three places is three variants, each with the reference that puts it where it is - and
    ///a fault in one is written in the cell's own coordinates, which are the same for all three.
    ///</summary>
    [Fact]
    public void A_cell_met_in_several_places_is_told_apart_by_variant()
    {
        var gds = PlacedThreeWays();
        var deck = DrcDeck.Parse(NarrowDeck);
        var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

        string xml = DrcReport.Write(result, deck, gds, "TOP");

        Assert.Contains("<cell>LEAF:1</cell>", xml);
        Assert.Contains("<cell>LEAF:3</cell>", xml);
        Assert.Contains("<trans>r0 *1 5,0</trans>", xml);
        Assert.Contains("<trans>m45 *1 -3,4</trans>", xml);
    }

    ///<summary>
    ///And KLayout puts them in the same places: it reads the report this wrote, places each item through its
    ///cell's reference, and every one lands on the fault this run found.
    ///</summary>
    [Fact]
    [Trait("Needs", "KLayout")]
    public void KLayout_places_what_this_writes_where_it_was_found()
    {
        Assert.True(OasisTestData.Available, "KLayout is needed to place the report.");

        var gds = PlacedThreeWays();
        var deck = DrcDeck.Parse(NarrowDeck);
        var result = Drc.Check(deck, GdsFlattener.Flatten(gds));

        string path = Path.Combine(Path.GetTempPath(), $"gdsdrc-{Guid.NewGuid():N}.lyrdb");

        try
        {
            File.WriteAllText(path, DrcReport.Write(result, deck, gds, "TOP"));

            var theirs = OasisTestData.PlacedReportValues(path);
            var ours = result.Violations.Select(violation => Shape(violation.RuleId, violation.Marker)).OrderBy(shape => shape, StringComparer.Ordinal);

            Assert.Equal(ours, theirs.OrderBy(shape => shape, StringComparer.Ordinal));
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }

    #endregion **************************************************************************



    #region Drawn over the layout *******************************************************

    ///<summary>An area as a polygon, an edge as a line, and a point as a line of no length - each saying whose it is.</summary>
    [Fact]
    public void Each_marker_is_drawn_as_the_kind_of_thing_it_is()
    {
        var report = EveryValue();

        string markup = SvgWriter.ReportMarkers(report.Items);

        Assert.StartsWith($"<g id=\"{SvgWriter.ReportMarkersId}\">", markup);
        Assert.Contains($"<polygon class=\"{SvgWriter.ReportMarkerClass}", markup);
        Assert.Contains($"<line class=\"{SvgWriter.ReportEdgeClass}", markup);
        Assert.Contains($"<line class=\"{SvgWriter.ReportPointClass}", markup);
        Assert.Contains($"{SvgWriter.RuleAttribute}=\"has space\"", markup);
        Assert.Contains($"{SvgWriter.ReportItemAttribute}=\"3\"", markup);
    }

    [Fact]
    public void A_waived_item_and_the_one_being_looked_at_are_marked_as_such()
    {
        var report = EveryValue();

        string markup = SvgWriter.ReportMarkers(report.Items, current: 3);

        Assert.Contains($"{SvgWriter.ReportMarkerClass} {SvgWriter.ReportWaivedClass}\" {SvgWriter.RuleAttribute}=\"m1.width\"", markup);
        Assert.Contains($"{SvgWriter.ReportMarkerClass} {SvgWriter.ReportCurrentClass}\"", markup);

        //Last, so it is drawn over its neighbors.
        Assert.EndsWith("</g>", markup);
        Assert.True(markup.LastIndexOf(SvgWriter.ReportCurrentClass, StringComparison.Ordinal) > markup.LastIndexOf($"{SvgWriter.ReportItemAttribute}=\"2\"", StringComparison.Ordinal));
    }

    [Fact]
    public void A_hidden_category_is_not_drawn_at_all()
    {
        var report = EveryValue();

        string markup = SvgWriter.ReportMarkers(report.Items, new HashSet<string> { "m1.width" }, current: 0);

        Assert.DoesNotContain("\"m1.width\"", markup);
        Assert.Contains("\"plain\"", markup);
    }

    ///<summary>Past the cap nothing more is drawn - but the item being looked at always is.</summary>
    [Fact]
    public void Past_the_cap_only_the_item_being_looked_at_is_drawn()
    {
        var report = EveryValue();

        string markup = SvgWriter.ReportMarkers(report.Items, current: 3, most: 1);

        Assert.Contains($"{SvgWriter.ReportItemAttribute}=\"0\"", markup);
        Assert.DoesNotContain($"{SvgWriter.ReportItemAttribute}=\"1\"", markup);
        Assert.DoesNotContain($"{SvgWriter.ReportItemAttribute}=\"2\"", markup);
        Assert.Contains($"{SvgWriter.ReportItemAttribute}=\"3\"", markup);
    }

    [Fact]
    public void A_report_that_draws_nothing_adds_nothing_to_the_picture()
    {
        Assert.Equal("", SvgWriter.ReportMarkers(new List<DrcReportItem>()));
    }

    #endregion **************************************************************************



    #region What is refused, and what is not ********************************************

    [Fact]
    public void Text_that_is_not_a_report_is_refused()
    {
        var gds = Top();

        Assert.Throws<InvalidDataException>(() => DrcReport.Read("layer met1 68/20", gds, GdsFlattener.Flatten(gds)));
    }

    [Fact]
    public void A_value_of_a_kind_this_does_not_read_is_counted_and_said()
    {
        var gds = Top();
        string xml = Report("TOP", "", "TOP", "hologram: (1,2,3)");

        var report = DrcReport.Read(xml, gds, GdsFlattener.Flatten(gds));

        var item = Assert.Single(report.Items);

        Assert.Empty(item.Markers);
        Assert.Contains(report.Problems, problem => problem.Contains("hologram (1)", StringComparison.Ordinal));
    }

    ///<summary>
    ///A comma-decimal locale reads the same report the same way. A coordinate is `1.5,2.25` - the comma
    ///between the two numbers - and a reader that let the culture in would read it as one number, or two in
    ///the wrong places.
    ///</summary>
    [Fact]
    public void Numbers_are_read_the_same_in_every_locale()
    {
        var before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            var markers = EveryValue().Items[0].Markers;

            Assert.Equal(new[] { (0, 0), (0, 2250), (1500, 2250), (1500, 0) }, Points(markers[0]));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_report_is_told_from_a_deck_and_a_layout_by_what_is_in_it()
    {
        Assert.True(DrcReport.LooksLikeReport(GdsTestData.ReadFixture("klayout-deep.lyrdb")));
        Assert.False(DrcReport.LooksLikeReport("layer met1 68/20\n"u8));
        Assert.False(DrcReport.LooksLikeReport(GdsTestData.ReadSample(GdsTestData.MosfetSample)));
    }

    #endregion **************************************************************************
}
