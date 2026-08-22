using System.Globalization;
using GdsII;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///Covers SvgWriter, the markup the 2D view draws.
///
///This used to live inside Viewer2DSvg.razor, where the only way to check any of it was to load the app
///and look - which is how three culture bugs in it survived a suite of 340 tests. Nothing here needs a
///browser: it is string building over a flattened layout.
///</summary>
public class SvgWriterTests
{
    private const string Red = "#b30000";

    ///<summary>
    ///A layer on data type 0, which is all most of these tests need - they are about which layers are drawn
    ///rather than about the pair. The ones that *are* about the pair build their keys explicitly.
    ///</summary>
    private static Layer TestLayer(short number = 5)
    {
        return new Layer(Key(number), Red);
    }

    private static LayerKey Key(short number)
    {
        return new LayerKey(number, 0);
    }

    ///<summary>
    ///The same rows as a set of pairs, which is what SvgWriter takes.
    ///
    ///The library deals in layer/datatype pairs rather than in the shell's row model - a package about
    ///GDSII should not have a type called CheckboxItem in its public surface - so the conversion belongs
    ///on the app side, and this is the tests' shortcut through it.
    ///</summary>
    private static IReadOnlySet<LayerKey> Visible(params short[] layerNumbers)
    {
        return CheckboxItem.VisibleLayers(AllVisible(layerNumbers));
    }

    private static List<CheckboxItem> AllVisible(params short[] layerNumbers)
    {
        return layerNumbers
            .Select(number => new CheckboxItem { Id = Key(number), Label = $"{number}/0", IsSelected = true })
            .ToList();
    }

    private static FlattenedLayout LayoutOf(params Element[] elements)
    {
        var layout = new FlattenedLayout();

        layout.Elements.AddRange(elements);

        return layout;
    }

    private static Element Polygon(Layer layer, params int[] coordinates)
    {
        var element = new Element { Layer = layer };

        for (int i = 0; i + 1 < coordinates.Length; i += 2)
            element.Points.Add(new Element.Point(coordinates[i], coordinates[i + 1]));

        return element;
    }

    private static Element Label(Layer layer, string text, int x, int y, TextPresentation presentation)
    {
        var element = new Element { Layer = layer, Text = text, Presentation = presentation };

        element.Points.Add(new Element.Point(x, y));

        return element;
    }

    private static TextPresentation Justified(HorizontalPresentation horizontal, VerticalPresentation vertical)
    {
        return new TextPresentation(horizontal, vertical, 0);
    }

    #region Polygons *******************************************************************

    [Fact]
    public void A_polygon_carries_its_points_color_and_opacity()
    {
        var layer = TestLayer();
        string svg = SvgWriter.Build(LayoutOf(Polygon(layer, 0, 0, 10, 0, 10, 10)), Visible(5), 0.5f);

        //
        //**The color and the opacity are a rule now, not attributes on the shape.**
        //
        //They are the same on every shape in the view, and repeating them per element was about a hundred
        //bytes each - a megabyte and a half of the same words at twenty thousand elements. What is asserted
        //is unchanged in substance: this shape, these corners, that color, that opacity.
        //
        Assert.Contains($"path.{SvgWriter.ClassFor(layer.Key)}{{fill:{Red};", svg);
        Assert.Contains("opacity:0.5", svg);

        //One path for the layer, with a subpath per shape and the elements they came from beside it.
        Assert.Contains(
            $"<path class=\"{SvgWriter.ClassFor(layer.Key)}\" fill-rule=\"nonzero\" data-elements=\"0\" d=\"M0,0L10,0L10,10Z\"/>",
            svg);
    }

    ///
    ///Each shape is tagged with which element of the layout drew it, so a click on it can be traced back.
    ///
    ///**The number is the index into the layout, not a count of what was drawn.** The two differ the
    ///moment a layer is switched off, and a caller looking up the wrong one gets a real element that is
    ///not the one under the cursor - which is the kind of wrong that looks right until something is moved.
    ///
    [Fact]
    public void A_shape_is_tagged_with_its_place_in_the_layout_not_with_how_many_were_drawn()
    {
        var shown = TestLayer();
        var hidden = new Layer(new LayerKey(9, 0), Red);

        //Three elements, of which the middle one is on a layer that is switched off.
        var layout = LayoutOf(
            Polygon(hidden, 0, 0, 10, 0, 10, 10),
            Polygon(shown, 0, 0, 10, 0, 10, 10),
            Polygon(shown, 20, 0, 30, 0, 30, 10));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f);

        //So the two that are drawn are elements 1 and 2, and the one that is not is never named. Both are
        //on the one layer, so both are subpaths of one path and the numbers sit together.
        Assert.Contains($"{SvgWriter.ElementsAttribute}=\"1 2\"", svg);
    }

    ///
    ///With a cell being edited, every shape says whether it is in it.
    ///
    ///**Only the browser caught this the first time.** The class was threaded through every signature and
    ///passed to AppendFormat, but the polygon's format string had no slot for it - and AppendFormat
    ///ignores an argument nothing references, so the build passed, every existing test passed, and the
    ///markup silently carried no classes at all. A test that asks for the attribute is the one that would
    ///have said so.
    ///
    [Fact]
    public void With_a_cell_being_edited_every_shape_says_whether_it_is_in_it()
    {
        var layout = GdsFlattener.Flatten(new GDS(GdsTestData.ReadFixture("placed.gds")));

        var context = CellContext.At(layout.Elements.First(element => element.Source!.Depth == 1).Source!);

        string svg = SvgWriter.Build(layout, SvgWriter.AllLayers(layout), 0.5f, new HashSet<LayerKey>(), context);

        //Three squares of the one cell - the instance looked through and the two that move with it - and
        //the top's own square, which is outside it.
        //
        //**Counted as shapes, not as nodes.** The three states are what the picture is split by, so each
        //is one path holding however many shapes are in that state - the instance looked through is one
        //subpath of one path, and the two that move with it are two subpaths of another.
        Assert.Equal(1, Shapes(svg, SvgWriter.InContextClass));
        Assert.Equal(2, Shapes(svg, SvgWriter.AlsoAffectedClass));
        Assert.Equal(1, Shapes(svg, SvgWriter.OutOfContextClass));
    }

    ///<summary>And with no cell being edited, nothing is marked - the layout is looked at, not edited.</summary>
    [Fact]
    public void With_no_cell_being_edited_nothing_is_marked()
    {
        var layout = GdsFlattener.Flatten(new GDS(GdsTestData.ReadFixture("placed.gds")));

        string svg = SvgWriter.Build(layout, SvgWriter.AllLayers(layout), 0.5f, new HashSet<LayerKey>(), null);

        //Every shape carries its layer's class whatever is being edited - that is where its color comes
        //from. What must be absent is any of the three that say where a shape stands relative to a cell.
        Assert.DoesNotContain(SvgWriter.InContextClass, svg);
        Assert.DoesNotContain(SvgWriter.AlsoAffectedClass, svg);
        Assert.DoesNotContain(SvgWriter.OutOfContextClass, svg);
    }

    ///
    ///**The rules must stand on their own, with no ancestor to scope them.**
    ///
    ///What comes out of here is not only the app's markup: it is also what Download Image saves and what
    ///the `gds svg` command writes, and neither has an element called gdsSVG in it. Scoped that way - the
    ///obvious way to write them - a standalone file came out with no color at all, every shape
    ///black-stroked and unfilled, and the app looked perfectly fine.
    ///
    ///Nor may they be scoped to the bare element, which would have been worse than useless: the drawing
    ///preview, the rubber band and the snap mark are polygons JavaScript puts inside the very same SVG, and
    ///a stylesheet rule beats the attributes those set.
    ///
    [Fact]
    public void The_rules_need_no_ancestor_and_match_no_other_shape()
    {
        var layer = TestLayer();

        string svg = SvgWriter.Build(LayoutOf(Polygon(layer, 0, 0, 10, 0, 10, 10)), Visible(5), 0.5f);

        Assert.DoesNotContain("#gdsSVG", svg);

        //Every selector names the generated class, so nothing without it can be caught.
        Assert.DoesNotContain("<style>path{", svg);
        Assert.DoesNotContain(";path{", svg);
        Assert.DoesNotContain("}path{", svg);
        Assert.DoesNotContain("polyline{", svg);

        Assert.Contains($"path.{SvgWriter.ClassFor(layer.Key)}{{", svg);
    }

    private static int Occurrences(string text, string what)
    {
        int found = 0;

        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + 1, StringComparison.Ordinal))
            found++;

        return found;
    }

    ///
    ///Whether the picture says a given element drew something.
    ///
    ///A shape's number is one of a list on the path holding it rather than an attribute of its own, so
    ///"data-element=2" would match element 2, 20 and 200 alike if it were looked for as text.
    ///
    private static bool Drew(string svg, int index)
    {
        //A label is still tagged on its own.
        if (svg.Contains($"{SvgWriter.ElementAttribute}=\"{index}\"", StringComparison.Ordinal))
            return true;

        foreach (string part in svg.Split($"{SvgWriter.ElementsAttribute}=\"").Skip(1))
        {
            string listed = part.Substring(0, part.IndexOf('"'));

            if (listed.Split(' ').Contains(index.ToString(CultureInfo.InvariantCulture)))
                return true;
        }

        return false;
    }

    ///
    ///How many shapes carry a given mark, across however many paths hold them.
    ///
    ///A shape is a subpath now rather than a node, so counting nodes counts layers. Every path the mark
    ///appears on is asked how many moves its data holds, which is one per shape.
    ///
    private static int Shapes(string svg, string marked)
    {
        int found = 0;

        foreach (string part in svg.Split("<path "))
        {
            if (!part.Contains($" {marked}\"", StringComparison.Ordinal))
                continue;

            found += Occurrences(part.Substring(0, part.IndexOf("/>", StringComparison.Ordinal)), "M");
        }

        return found;
    }

    [Fact]
    public void A_label_is_tagged_the_same_way_a_shape_is()
    {
        var layout = LayoutOf(
            Polygon(TestLayer(), 0, 0, 10, 0, 10, 10),
            Label(TestLayer(), "VPWR", 100, 200, TextPresentation.Default));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f);

        Assert.Contains("<text data-element=\"1\"", svg);
    }

    [Fact]
    public void Every_element_becomes_one_shape()
    {
        var layer = TestLayer();

        var layout = LayoutOf(
            Polygon(layer, 0, 0, 10, 0, 10, 10),
            Polygon(layer, 0, 0, 20, 0, 20, 20));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f);

        Assert.Equal(2, svg.Split("M").Length - 1);
    }

    #endregion ************************************************************************



    #region Layer visibility ***********************************************************

    [Fact]
    public void An_element_on_a_deselected_layer_is_left_out()
    {
        var layout = LayoutOf(Polygon(TestLayer(5), 0, 0, 10, 0, 10, 10));

        var hidden = AllVisible(5);
        hidden[0].IsSelected = false;

        Assert.Equal("", SvgWriter.Build(layout, CheckboxItem.VisibleLayers(hidden), 0.5f));
    }

    ///<summary>A layer the shell never listed is not drawn either, rather than defaulting to visible.</summary>
    [Fact]
    public void An_element_on_an_unlisted_layer_is_left_out()
    {
        var layout = LayoutOf(Polygon(TestLayer(42), 0, 0, 10, 0, 10, 10));

        Assert.Equal("", SvgWriter.Build(layout, Visible(5), 0.5f));
    }

    [Fact]
    public void Only_the_deselected_layer_is_left_out()
    {
        var layout = LayoutOf(
            Polygon(TestLayer(5), 0, 0, 10, 0, 10, 10),
            Polygon(TestLayer(6), 0, 0, 20, 0, 20, 20));

        var layers = AllVisible(5, 6);
        layers[0].IsSelected = false;

        string svg = SvgWriter.Build(layout, CheckboxItem.VisibleLayers(layers), 0.5f);

        Assert.Single(svg.Split("M"), part => part.Contains("20,0"));
        Assert.DoesNotContain("10,0", svg);
    }

    #endregion ************************************************************************



    #region Labels *********************************************************************

    [Fact]
    public void A_label_becomes_a_text_element_at_its_anchor()
    {
        var presentation = Justified(HorizontalPresentation.Center, VerticalPresentation.Middle);
        var layout = LayoutOf(Label(TestLayer(), "VPWR", 100, 200, presentation));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f);

        Assert.StartsWith("<text data-element=\"0\" x=\"100\" y=\"200\"", svg);
        Assert.Contains($"fill=\"{Red}\"", svg);
        Assert.Contains($"font-size=\"{SvgWriter.LabelFontSize}\"", svg);
        Assert.Contains(">VPWR</text>", svg);
        Assert.DoesNotContain("<path", svg);
    }

    [Theory]
    [InlineData(HorizontalPresentation.Left, "start")]
    [InlineData(HorizontalPresentation.Center, "middle")]
    [InlineData(HorizontalPresentation.Right, "end")]
    public void Horizontal_justification_maps_onto_the_text_anchor(HorizontalPresentation horizontal, string expected)
    {
        var presentation = Justified(horizontal, VerticalPresentation.Middle);
        var layout = LayoutOf(Label(TestLayer(), "A", 0, 0, presentation));

        Assert.Contains($"text-anchor=\"{expected}\"", SvgWriter.Build(layout, Visible(5), 0.5f));
    }

    ///<summary>
    ///Inverted on purpose, because this view maps GDSII's upward Y onto SVG's downward Y: a label the
    ///format says hangs below its anchor has to sit above it on screen. Pinned so the inversion cannot be
    ///"corrected" by someone reading only the enum names.
    ///</summary>
    [Theory]
    [InlineData(VerticalPresentation.Top, "auto")]
    [InlineData(VerticalPresentation.Middle, "middle")]
    [InlineData(VerticalPresentation.Bottom, "hanging")]
    public void Vertical_justification_maps_onto_an_inverted_baseline(VerticalPresentation vertical, string expected)
    {
        var presentation = Justified(HorizontalPresentation.Center, vertical);
        var layout = LayoutOf(Label(TestLayer(), "A", 0, 0, presentation));

        Assert.Contains($"dominant-baseline=\"{expected}\"", SvgWriter.Build(layout, Visible(5), 0.5f));
    }

    ///<summary>
    ///A label's string is the only value out of the file that reaches the markup as text, so it is the
    ///only place a stray angle bracket could close an element early.
    ///</summary>
    [Fact]
    public void A_label_containing_markup_characters_is_encoded()
    {
        var presentation = Justified(HorizontalPresentation.Center, VerticalPresentation.Middle);
        var layout = LayoutOf(Label(TestLayer(), "A<B>&C", 0, 0, presentation));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f);

        Assert.Contains(">A&lt;B&gt;&amp;C</text>", svg);
        Assert.DoesNotContain("<B>", svg);
    }

    ///<summary>
    ///Labels have their own switch because they usually share a layer with the shapes they name, so hiding
    ///the pair to be rid of the writing would take that geometry too.
    ///</summary>
    [Fact]
    public void Labels_can_be_left_out_without_taking_the_geometry_with_them()
    {
        var layer = TestLayer();
        var presentation = Justified(HorizontalPresentation.Center, VerticalPresentation.Middle);

        var layout = LayoutOf(
            Polygon(layer, 0, 0, 10, 0, 10, 10),
            Label(layer, "VPWR", 100, 200, presentation));

        string withLabels = SvgWriter.Build(layout, Visible(5), 0.5f, showLabels: true);
        string without = SvgWriter.Build(layout, Visible(5), 0.5f, showLabels: false);

        Assert.Contains("<text", withLabels);
        Assert.Contains("<path", withLabels);

        Assert.DoesNotContain("<text", without);
        Assert.DoesNotContain("VPWR", without);
        Assert.Contains("<path", without);
    }

    ///<summary>Labels are drawn unless something says otherwise, which is how every existing caller reads.</summary>
    [Fact]
    public void Labels_are_drawn_by_default()
    {
        var presentation = Justified(HorizontalPresentation.Center, VerticalPresentation.Middle);
        var layout = LayoutOf(Label(TestLayer(), "A", 0, 0, presentation));

        Assert.Contains("<text", SvgWriter.Build(layout, Visible(5), 0.5f));
    }

    ///<summary>A hidden layer stays hidden whatever the label switch says - the two are separate filters.</summary>
    [Fact]
    public void Showing_labels_does_not_bring_back_a_hidden_layer()
    {
        var presentation = Justified(HorizontalPresentation.Center, VerticalPresentation.Middle);
        var layout = LayoutOf(Label(TestLayer(5), "A", 0, 0, presentation));

        var hidden = AllVisible(5);
        hidden[0].IsSelected = false;

        Assert.Equal("", SvgWriter.Build(layout, CheckboxItem.VisibleLayers(hidden), 0.5f, showLabels: true));
    }

    ///<summary>
    ///Labels are answered per layer, which is the point of taking a set rather than a switch: one layer's
    ///pin names can be the reason the view is open while another's are noise.
    ///</summary>
    [Fact]
    public void One_layers_labels_can_be_dropped_while_anothers_stay()
    {
        var presentation = Justified(HorizontalPresentation.Center, VerticalPresentation.Middle);

        var layout = LayoutOf(
            Label(TestLayer(5), "KEEP", 0, 0, presentation),
            Label(TestLayer(6), "DROP", 10, 10, presentation),
            Polygon(TestLayer(6), 0, 0, 10, 0, 10, 10));

        var rows = AllVisible(5, 6);
        rows[1].ShowLabels = false;

        string svg = SvgWriter.Build(
            layout,
            CheckboxItem.VisibleLayers(rows),
            0.5f,
            CheckboxItem.LabeledLayers(rows));

        Assert.Contains("KEEP", svg);
        Assert.DoesNotContain("DROP", svg);

        //And the layer whose labels went is still drawn.
        Assert.Contains("<path", svg);
    }

    ///<summary>
    ///A layer that is switched off contributes no labels either, however its own label switch is left.
    ///Asserted on the set rather than on the markup, since that is where the two are reconciled.
    ///</summary>
    [Fact]
    public void A_hidden_layer_contributes_no_labels()
    {
        var rows = AllVisible(5, 6);
        rows[0].IsSelected = false;

        var labeled = CheckboxItem.LabeledLayers(rows);

        Assert.DoesNotContain(rows[0].Id, labeled);
        Assert.Contains(rows[1].Id, labeled);
    }

    #endregion ************************************************************************



    #region The visible-layer set ******************************************************

    ///<summary>
    ///Both views filter through this, so what counts as visible has to mean the same thing in each. It is
    ///built once per redraw rather than scanning the list per element - which the bundled cells are far
    ///too small to notice, but this view rebuilds its whole markup on every tick of the opacity slider,
    ///for whatever file the user opened.
    ///</summary>
    [Fact]
    public void Only_the_selected_layers_are_in_the_set()
    {
        var layers = AllVisible(5, 6, 7);
        layers[1].IsSelected = false;

        var visible = CheckboxItem.VisibleLayers(layers);

        Assert.Equal(new[] { Key(5), Key(7) }, visible.OrderBy(key => key.Number).ToArray());
    }

    [Fact]
    public void A_layer_that_is_not_listed_is_not_visible()
    {
        Assert.DoesNotContain(Key(42), CheckboxItem.VisibleLayers(AllVisible(5, 6)));
    }

    [Fact]
    public void An_empty_list_makes_nothing_visible()
    {
        Assert.Empty(CheckboxItem.VisibleLayers(new List<CheckboxItem>()));
    }

    ///<summary>A set, so a layer listed twice does not become two entries or throw.</summary>
    [Fact]
    public void A_repeated_layer_is_held_once()
    {
        var visible = CheckboxItem.VisibleLayers(AllVisible(5, 5, 5));

        Assert.Single(visible);
        Assert.Contains(Key(5), visible);
    }

    ///<summary>The set and the markup have to agree - the same filter, reached two ways.</summary>
    [Fact]
    public void What_the_set_excludes_is_what_the_markup_leaves_out()
    {
        var layout = LayoutOf(
            Polygon(TestLayer(5), 0, 0, 10, 0, 10, 10),
            Polygon(TestLayer(6), 0, 0, 20, 0, 20, 20));

        var layers = AllVisible(5, 6);
        layers[0].IsSelected = false;

        Assert.DoesNotContain(Key(5), CheckboxItem.VisibleLayers(layers));
        Assert.DoesNotContain("10,0", SvgWriter.Build(layout, CheckboxItem.VisibleLayers(layers), 0.5f));
    }

    #endregion ************************************************************************



    #region Culture independence *******************************************************

    ///<summary>
    ///The whole reason this class exists outside the component. A comma decimal separator makes the
    ///opacity an invalid number that the browser discards along with the attribute, and a non-ASCII
    ///negative sign makes a coordinate unparseable - so in a comma-decimal locale, which is where a large
    ///share of users are, the layout would not draw.
    ///</summary>
    [Fact]
    public void A_negative_coordinate_keeps_an_ascii_minus_under_a_hostile_culture()
    {
        var layout = LayoutOf(Polygon(TestLayer(), -600, 600, 550, 600, 550, -1100));

        string svg = GdsTestData.UnderHostileCulture(() => SvgWriter.Build(layout, Visible(5), 0.5f));

        Assert.Contains("d=\"M-600,600L550,600L550,-1100Z\"", svg);
        Assert.DoesNotContain("!", svg);
    }

    [Fact]
    public void The_opacity_keeps_a_decimal_point_under_a_hostile_culture()
    {
        var layout = LayoutOf(Polygon(TestLayer(), 0, 0, 10, 0, 10, 10));

        string svg = GdsTestData.UnderHostileCulture(() => SvgWriter.Build(layout, Visible(5), 0.35f));

        //In the rule rather than on the shape now, and just as fatal if a comma reaches it: a browser
        //discards the whole declaration.
        Assert.Contains("opacity:0.35", svg);
        Assert.DoesNotContain("0,35", svg);
    }

    [Fact]
    public void A_labels_coordinates_survive_a_hostile_culture()
    {
        var presentation = Justified(HorizontalPresentation.Left, VerticalPresentation.Top);
        var layout = LayoutOf(Label(TestLayer(), "gate", -1015, 328, presentation));

        string svg = GdsTestData.UnderHostileCulture(() => SvgWriter.Build(layout, Visible(5), 0.5f));

        Assert.Contains("x=\"-1015\" y=\"328\"", svg);
        Assert.DoesNotContain("!", svg);
    }

    ///<summary>
    ///A real file, so every path is exercised at once rather than only the ones a hand-built element
    ///happens to reach. Mosfet.gds is the useful one here: its coordinates are negative and its three
    ///labels take the default justification.
    ///</summary>
    [Fact]
    public void The_whole_markup_for_a_real_file_is_identical_under_a_hostile_culture()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var layout = GdsFlattener.Flatten(gds);

        var layers = gds.AdditionalInformation.Layers.Keys
            .Select(number => new CheckboxItem { Id = number, Label = $"Layer {number}", IsSelected = true })
            .ToList();

        string invariant = SvgWriter.Build(layout, CheckboxItem.VisibleLayers(layers), 0.5f);
        string hostile = GdsTestData.UnderHostileCulture(() => SvgWriter.Build(layout, CheckboxItem.VisibleLayers(layers), 0.5f));

        Assert.Equal(invariant, hostile);

        //Guards the test itself: a file with no negative coordinate would pass this without proving much.
        Assert.Contains("-", invariant);
    }

    #endregion ************************************************************************



    #region The opacity round trip *****************************************************

    ///<summary>
    ///The slider is rendered with FormatOpacity and its input is read back with TryParseOpacity, so the
    ///two have to agree about the decimal separator whatever the culture - otherwise it moves once and
    ///then sticks.
    ///</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.1f)]
    [InlineData(0.35f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void Formatting_an_opacity_and_reading_it_back_returns_it(float opacity)
    {
        string formatted = GdsTestData.UnderHostileCulture(() => SvgWriter.FormatOpacity(opacity));

        Assert.DoesNotContain(",", formatted);

        bool parsed = GdsTestData.UnderHostileCulture(() =>
        {
            SvgWriter.TryParseOpacity(formatted, out float readBack);

            return readBack == opacity;
        });

        Assert.True(parsed, $"{opacity} did not survive the round trip as \"{formatted}\"");
    }

    ///<summary>
    ///What the bug actually did: read with the current culture, "0.9" in a comma-decimal one treats the
    ///point as a group separator and yields 9, so the view went fully opaque with nothing to show why.
    ///</summary>
    [Fact]
    public void A_decimal_point_is_not_read_as_a_group_separator()
    {
        bool parsed = GdsTestData.UnderHostileCulture(() => SvgWriter.TryParseOpacity("0.9", out float opacity) && opacity == 0.9f);

        Assert.True(parsed);
    }

    [Fact]
    public void A_value_that_is_not_a_number_is_rejected_rather_than_defaulting()
    {
        Assert.False(SvgWriter.TryParseOpacity("", out _));
        Assert.False(SvgWriter.TryParseOpacity(null, out _));
        Assert.False(SvgWriter.TryParseOpacity("half", out _));
    }

    #endregion ************************************************************************

    #region Leaving things out *********************************************************

    ///
    ///**Nothing is left out unless the caller asks.** A viewer that quietly omits shapes is one nobody
    ///should trust with a layout they are checking, so both of these are off by default and the view turns
    ///them on only above a size where the markup is large enough for it to be worth it. That size was
    ///decided when leaving a shape out saved a node; it saves bytes now, and bytes are what a rebuild
    ///pays - see DOCUMENTATION.md for the measurement that says so.
    ///
    [Fact]
    public void By_default_every_shape_is_drawn()
    {
        var layer = TestLayer();

        var layout = LayoutOf(
            Polygon(layer, 0, 0, 10, 0, 10, 10),
            Polygon(layer, 100000, 0, 100010, 0, 100010, 10));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f);

        Assert.True(Drew(svg, 0));
        Assert.True(Drew(svg, 1));
    }

    ///<summary>A shape outside the box asked for is left out; one straddling its edge is not.</summary>
    [Fact]
    public void A_shape_off_the_shown_box_is_left_out()
    {
        var layer = TestLayer();

        var layout = LayoutOf(
            Polygon(layer, 0, 0, 10, 0, 10, 10),
            Polygon(layer, 90, 0, 110, 0, 110, 10),
            Polygon(layer, 100000, 0, 100010, 0, 100010, 10));

        string svg = SvgWriter.Build(
            layout, Visible(5), 0.5f, new HashSet<LayerKey>(), null, new Bounds(0, 0, 100, 100));

        Assert.True(Drew(svg, 0));

        //Half in and half out still draws, or a pan would tear shapes off at the edge of the view.
        Assert.True(Drew(svg, 1));

        Assert.False(Drew(svg, 2));
    }

    ///<summary>And a shape smaller than the area given is left out, which is level of detail.</summary>
    [Fact]
    public void A_shape_below_the_smallest_worth_drawing_is_left_out()
    {
        var layer = TestLayer();

        var layout = LayoutOf(
            Polygon(layer, 0, 0, 2, 0, 2, 2),
            Polygon(layer, 0, 0, 500, 0, 500, 500));

        string svg = SvgWriter.Build(
            layout, Visible(5), 0.5f, new HashSet<LayerKey>(), null, null, 100);

        Assert.False(Drew(svg, 0));
        Assert.True(Drew(svg, 1));
    }

    ///
    ///A label is never dropped for being small. Its size on screen is fixed by the view rather than by the
    ///layout, so the extent of its anchor says nothing at all about whether it can be read.
    ///
    [Fact]
    public void A_label_is_never_too_small_to_draw()
    {
        var layer = TestLayer();

        var layout = LayoutOf(Label(layer, "gate", 0, 0, Justified(HorizontalPresentation.Left, VerticalPresentation.Top)));

        string svg = SvgWriter.Build(
            layout, Visible(5), 0.5f, Visible(5), null, null, 1000000);

        Assert.Contains("gate", svg);
    }

    #endregion **********************************************************************



    #region Fill patterns ***********************************************************

    ///
    ///A layer with nothing said about it is filled with its color, exactly as before.
    ///
    ///The whole feature has to be invisible until somebody asks for it: every file in the corpus, every
    ///downloaded image and every thumbnail is a layer on LayerFill.None.
    ///
    [Fact]
    public void ALayerWithNoPatternIsFilledWithItsColorAndDefinesNothing()
    {
        var layout = LayoutOf(Polygon(TestLayer(), 0, 0, 100, 0, 100, 100, 0, 100));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>());

        Assert.Contains($"fill:{Red}", svg);
        Assert.DoesNotContain("<defs>", svg);
        Assert.DoesNotContain("<pattern", svg);
    }

    ///<summary>And one that asked is filled from a definition rather than with a color.</summary>
    [Fact]
    public void APatternedLayerIsFilledFromItsOwnDefinition()
    {
        var layer = TestLayer();

        layer.Fill = LayerFill.Diagonal;

        var layout = LayoutOf(Polygon(layer, 0, 0, 100, 0, 100, 100, 0, 100));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>());

        Assert.Contains($"fill:url(#{SvgWriter.PatternPrefix}l5_0)", svg);
        Assert.Contains($"<pattern id=\"{SvgWriter.PatternPrefix}l5_0\"", svg);

        //Defined before it is referenced, which is not required by SVG but is required by anybody reading it.
        Assert.True(svg.IndexOf("<pattern") < svg.IndexOf("fill:url("));
    }

    ///
    ///Every pattern is painted in the layer's own color, ground and motif both.
    ///
    ///A pattern is a second axis on top of the color rather than a replacement for it - the point is to
    ///tell two greens apart, and a hatch in some other color would be telling them apart by color again.
    ///
    [Theory]
    [InlineData(LayerFill.Dots)]
    [InlineData(LayerFill.Squares)]
    [InlineData(LayerFill.Grid)]
    [InlineData(LayerFill.Dashes)]
    [InlineData(LayerFill.Diagonal)]
    [InlineData(LayerFill.BackDiagonal)]
    [InlineData(LayerFill.CrossHatch)]
    public void EveryPatternIsDrawnInTheLayersOwnColor(LayerFill fill)
    {
        var layer = TestLayer();

        layer.Fill = fill;

        var layout = LayoutOf(Polygon(layer, 0, 0, 1000, 0, 1000, 1000, 0, 1000));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>());

        string pattern = Between(svg, "<pattern", "</pattern>");

        //The washed ground, so a shape is not empty where the motif is too small to see.
        Assert.Contains($"fill=\"{Red}\" opacity=\"0.35\"", pattern);

        //And a motif over it, in the same color: something beyond the ground rectangle.
        Assert.True(Occurrences(pattern, Red) >= 2, $"{fill} drew no motif in the layer's color");
    }

    ///
    ///A pattern given a color of its own is drawn in it, and the ground stays the layer's.
    ///
    ///Both halves matter. Coloring the whole tile would be recoloring the layer by another route - the
    ///shape would simply become red - and coloring neither would make the control do nothing.
    ///
    [Fact]
    public void APatternColorPaintsTheMarksAndNotTheGround()
    {
        var layer = TestLayer();

        layer.Fill = LayerFill.Squares;
        layer.PatternColor = "#00ff00";

        var layout = LayoutOf(Polygon(layer, 0, 0, 1000, 0, 1000, 1000, 0, 1000));

        string pattern = Between(SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>()), "<pattern", "</pattern>");

        //The ground is still the layer's own color, washed out the way it always was.
        Assert.Contains($"fill=\"{Red}\" opacity=\"0.35\"", pattern);

        //And the motif is the chosen one, which is the only other thing in the tile.
        Assert.Contains("fill=\"#00ff00\"", pattern);
        Assert.Equal(1, Occurrences(pattern, Red));
    }

    ///<summary>And a layer that was given none draws its marks in its own color, which is the old behavior.</summary>
    [Fact]
    public void APatternWithNoColorOfItsOwnFollowsTheLayer()
    {
        var layer = TestLayer();

        layer.Fill = LayerFill.Squares;

        Assert.Equal(layer.Color, SvgWriter.MarksColorOf(layer));

        //An empty string is the same as nothing: a name box cleared to blank must not pin the hatch to "".
        layer.PatternColor = "";

        Assert.Equal(layer.Color, SvgWriter.MarksColorOf(layer));

        layer.PatternColor = "#00ff00";

        Assert.Equal("#00ff00", SvgWriter.MarksColorOf(layer));
    }

    ///
    ///A layer given a pattern size carries it on the tag, and one that was not carries nothing.
    ///
    ///The interop rescales patterns by walking these nodes, and the size is per layer - so it has to travel
    ///on the node rather than in a table handed over beside it. Absent is the usual size, which keeps it out
    ///of every picture nobody changed.
    ///
    [Fact]
    public void OnlyALayerWithItsOwnPatternSizeCarriesOne()
    {
        var layer = TestLayer();

        layer.Fill = LayerFill.Dots;

        var layout = LayoutOf(Polygon(layer, 0, 0, 1000, 0, 1000, 1000, 0, 1000));

        string plain = Between(SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>()), "<pattern", ">");

        Assert.DoesNotContain(SvgWriter.PatternPixelsAttribute, plain);

        layer.PatternPixels = 20;

        string sized = Between(SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>()), "<pattern", ">");

        Assert.Contains($"{SvgWriter.PatternPixelsAttribute}=\"20\"", sized);
    }

    ///
    ///The swatch shows the size as well as the pattern, so the control says what it is doing.
    ///
    ///A picker whose eight tiles looked identical however coarse the layer was set would leave the size box
    ///as a number with no picture attached - and the swatch beside the layer in the list is what that list
    ///is for.
    ///
    [Fact]
    public void ASwatchDrawsACoarserTileForACoarserPattern()
    {
        long fine = TileOf(SvgWriter.SwatchFor(LayerFill.Grid, Red, "fine", 24, null, 6));
        long usual = TileOf(SvgWriter.SwatchFor(LayerFill.Grid, Red, "usual", 24, null, Layer.DefaultPatternPixels));
        long coarse = TileOf(SvgWriter.SwatchFor(LayerFill.Grid, Red, "coarse", 24, null, 18));

        Assert.True(fine < usual, $"a fine pattern drew a {fine} tile against the usual {usual}");
        Assert.True(coarse > usual, $"a coarse pattern drew a {coarse} tile against the usual {usual}");

        //Held inside the swatch either way: one repeat with the rest cropped off is a picture of a corner.
        Assert.True(coarse <= 12, $"a coarse pattern filled {coarse} of a 24 swatch");
    }

    ///<summary>The width of the one pattern a swatch defines, which is its tile.</summary>
    private static long TileOf(string swatch)
    {
        //By pattern, not by Between: the tag holds four quoted attributes before this one, and a scan for
        //the next quote after "width=" finds the closing quote of the first of them instead.
        var found = System.Text.RegularExpressions.Regex.Match(swatch, "<pattern[^>]*width=\"(\\d+)\"");

        Assert.True(found.Success, $"no pattern with a width in {swatch}");

        return long.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    ///<summary>And a swatch takes a pattern color too, or the list would not show one that was chosen.</summary>
    [Fact]
    public void ASwatchDrawsThePatternColorWhenThereIsOne()
    {
        string following = SvgWriter.SwatchFor(LayerFill.Squares, Red, "following", 24);
        string chosen = SvgWriter.SwatchFor(LayerFill.Squares, Red, "chosen", 24, "#00ff00");

        Assert.DoesNotContain("#00ff00", following);
        Assert.Contains("#00ff00", chosen);

        //The ground is still the layer's in both, the same as in the picture.
        Assert.Contains($"fill=\"{Red}\" opacity=\"0.35\"", chosen);
    }

    ///<summary>Each pattern is a different picture, or the choice would be a menu of one thing.</summary>
    [Fact]
    public void EveryPatternDrawsSomethingDifferent()
    {
        var seen = new Dictionary<string, LayerFill>();

        foreach (LayerFill fill in Enum.GetValues<LayerFill>())
        {
            if (fill == LayerFill.None)
                continue;

            var layer = TestLayer();

            layer.Fill = fill;

            string pattern = Between(
                SvgWriter.Build(LayoutOf(Polygon(layer, 0, 0, 1000, 0, 1000, 1000, 0, 1000)), Visible(5), 0.5f, new HashSet<LayerKey>()),
                "<pattern",
                "</pattern>");

            var same = fill;

            if (seen.TryGetValue(pattern, out var other))
                same = other;

            Assert.False(seen.ContainsKey(pattern), $"{fill} draws the same tile as {same}");

            seen.Add(pattern, fill);
        }

        Assert.Equal(Enum.GetValues<LayerFill>().Length - 1, seen.Count);
    }

    ///
    ///The tile is sized from the layout, so the texture is the same density whatever the file is.
    ///
    ///A database unit is not a length: the bundled Mosfet is a couple of thousand units across and a die is
    ///tens of millions, so any fixed number of units is invisible on one and larger than the other.
    ///
    [Fact]
    public void TheTileIsAFractionOfTheLayoutRatherThanAFixedSize()
    {
        var small = TestLayer();
        var large = TestLayer();

        small.Fill = LayerFill.Squares;
        large.Fill = LayerFill.Squares;

        long tileOfSmall = SvgWriter.TileFor(LayoutOf(Polygon(small, 0, 0, 1000, 0, 1000, 1000, 0, 1000)));
        long tileOfLarge = SvgWriter.TileFor(LayoutOf(Polygon(large, 0, 0, 1000000, 0, 1000000, 1000000, 0, 1000000)));

        //
        //The same count of repeats across either, which is the claim - not that one tile is exactly a
        //thousand times the other. A tile is a whole number of database units, so the small layout's is
        //truncated (1000/32 is 31, not 31.25) and multiplying it back up misses by the truncation.
        //
        Assert.Equal(32, (int)Math.Round(1000.0 / tileOfSmall));
        Assert.Equal(32, (int)Math.Round(1000000.0 / tileOfLarge));

        //And bigger for the bigger layout, which the rounding above could hide if both collapsed to one.
        Assert.True(tileOfLarge > tileOfSmall);

        //And never zero, which would be a pattern that fills nothing and a browser that draws no shape.
        Assert.True(SvgWriter.TileFor(LayoutOf(Polygon(small, 5, 5, 5, 5))) > 0);
        Assert.True(SvgWriter.TileFor(new FlattenedLayout()) > 0);
    }

    ///
    ///A pattern is defined for a layer that is drawn, and not for one that is switched off.
    ///
    ///The same rule the color rules follow. A definition nothing references is bytes in every document, and
    ///the whole reason this markup is one path per layer is that bytes were what pan could not afford.
    ///
    [Fact]
    public void OnlyTheDrawnLayersGetAPattern()
    {
        var shown = new Layer(Key(5), Red) { Fill = LayerFill.Dots };
        var hidden = new Layer(Key(9), Red) { Fill = LayerFill.Grid };

        var layout = LayoutOf(
            Polygon(shown, 0, 0, 100, 0, 100, 100, 0, 100),
            Polygon(hidden, 0, 0, 100, 0, 100, 100, 0, 100));

        string svg = SvgWriter.Build(layout, Visible(5), 0.5f, new HashSet<LayerKey>());

        Assert.Contains($"{SvgWriter.PatternPrefix}l5_0", svg);
        Assert.DoesNotContain($"{SvgWriter.PatternPrefix}l9_0", svg);
    }

    ///<summary>Every pattern carries the class the view rescales them by, since it cannot know the layers.</summary>
    [Fact]
    public void EveryPatternIsMarkedForTheViewToFind()
    {
        var one = new Layer(Key(5), Red) { Fill = LayerFill.Dots };
        var two = new Layer(Key(9), "#0000ff") { Fill = LayerFill.CrossHatch };

        var layout = LayoutOf(
            Polygon(one, 0, 0, 100, 0, 100, 100, 0, 100),
            Polygon(two, 0, 0, 100, 0, 100, 100, 0, 100));

        string svg = SvgWriter.Build(layout, Visible(5, 9), 0.5f, new HashSet<LayerKey>());

        Assert.Equal(2, Occurrences(svg, $"class=\"{SvgWriter.PatternClass}\""));
    }

    ///<summary>The text between two markers, for reading one pattern out of a document.</summary>
    private static string Between(string text, string from, string to)
    {
        int start = text.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"no {from} in the markup");

        int end = text.IndexOf(to, start, StringComparison.Ordinal);

        Assert.True(end >= 0, $"no {to} after {from}");

        return text.Substring(start, end - start);
    }

    #endregion **********************************************************************


    #region The action icons ********************************************************

    ///<summary>Every action has a picture, and none of them is empty markup.</summary>
    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void EveryActionDrawsSomething(SvgWriter.ShapeIcon icon)
    {
        string drawn = SvgWriter.IconFor(icon);

        Assert.NotEmpty(drawn);

        //Something with an outline or an area, rather than a comment or a stray attribute.
        Assert.True(drawn.Contains("<path") || drawn.Contains("<rect") || drawn.Contains("<circle"),
            $"{icon} drew no shape");
    }

    ///
    ///Each one is a different picture.
    ///
    ///The set is nineteen glyphs and several are near neighbours - left against right, union against
    ///exclude - so a copied line that was never edited is the likely mistake here, and it is one nobody
    ///would see at fifteen pixels.
    ///
    [Fact]
    public void EveryActionDrawsADifferentPicture()
    {
        var seen = new Dictionary<string, SvgWriter.ShapeIcon>();

        foreach (SvgWriter.ShapeIcon icon in Enum.GetValues<SvgWriter.ShapeIcon>())
        {
            string drawn = SvgWriter.IconFor(icon);

            SvgWriter.ShapeIcon same = icon;

            if (seen.TryGetValue(drawn, out var other))
                same = other;

            Assert.False(seen.ContainsKey(drawn),
                $"{icon} draws the same picture as {same}");

            seen.Add(drawn, icon);
        }

        Assert.Equal(Enum.GetValues<SvgWriter.ShapeIcon>().Length, seen.Count);
    }

    ///
    ///Nothing is drawn in a color of its own.
    ///
    ///A line of the menu is dark on white and lights up white on blue when the pointer is over it, and the
    ///same picture on the panel is white on blue always. One glyph carrying a fixed color would be right in
    ///one of those three and invisible in another.
    ///
    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void NoActionIconCarriesAColorOfItsOwn(SvgWriter.ShapeIcon icon)
    {
        string drawn = SvgWriter.IconFor(icon);

        Assert.DoesNotContain("#", drawn);
        Assert.DoesNotContain("rgb", drawn);

        //It has to say currentColor somewhere, or it is drawing in the browser's default black.
        Assert.Contains("currentColor", drawn);
    }

    ///
    ///Every rectangle fits the sixteen-unit box the markup gives it.
    ///
    ///**The rectangles only, and the name says so.** A path's numbers are a mix of absolute coordinates and
    ///relative deltas - `l0.7-9.1` moves down nine units and is not a point at minus nine - so reading them
    ///all as coordinates fails on three of these icons that are perfectly well drawn. Telling the two apart
    ///needs a path parser, which is more machinery than this is worth.
    ///
    ///A rect's four numbers are unambiguous, and they are most of the geometry here: the six aligns, the two
    ///spaces and three of the four booleans are rectangles, and they are hand-typed numbers where a slip
    ///gives a shape hanging outside its button - a clipped edge rather than an obvious mistake.
    ///
    [Theory]
    [MemberData(nameof(EveryIcon))]
    public void EveryRectangleInAnIconFitsItsBox(SvgWriter.ShapeIcon icon)
    {
        string drawn = SvgWriter.IconFor(icon);

        foreach (System.Text.RegularExpressions.Match found in System.Text.RegularExpressions.Regex.Matches(
            drawn, "<rect x=\"(?<x>[-\\d.]+)\" y=\"(?<y>[-\\d.]+)\" width=\"(?<w>[-\\d.]+)\" height=\"(?<h>[-\\d.]+)\""))
        {
            double At(string named)
            {
                return double.Parse(found.Groups[named].Value, CultureInfo.InvariantCulture);
            }

            Assert.InRange(At("x"), 0.0, 16.0);
            Assert.InRange(At("y"), 0.0, 16.0);

            //Its far edge too, which is where an over-long bar actually shows.
            Assert.InRange(At("x") + At("w"), 0.0, 16.0);
            Assert.InRange(At("y") + At("h"), 0.0, 16.0);
        }
    }

    public static TheoryData<SvgWriter.ShapeIcon> EveryIcon()
    {
        var all = new TheoryData<SvgWriter.ShapeIcon>();

        foreach (SvgWriter.ShapeIcon icon in Enum.GetValues<SvgWriter.ShapeIcon>())
            all.Add(icon);

        return all;
    }

    #endregion **********************************************************************
}
