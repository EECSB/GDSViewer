using System.Text.Json.Nodes;
using GdsII;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///The assistant's tools, called the way a model calls them: a name and JSON arguments in, a line of text out.
///
///Each tool is checked by what it leaves in the file - the coordinates, the layer, the records - rather than
///by what it says it did, and every refusal is checked to have left the file byte for byte as it was. The rules
///that hold across all of them come first: one step of undo per call, numbers that outlive edits, schemas every
///provider can read.
///
public class AgentToolTests
{
    #region Helpers *******************************************************************

    private static readonly LayerKey Met1 = new LayerKey(68, 20);
    private static readonly LayerKey Via = new LayerKey(68, 44);
    private static readonly LayerKey Met2 = new LayerKey(69, 20);

    ///<summary>A new library on a nanometer grid, with three named layers and an empty TOP.</summary>
    private static AgentSession Session()
    {
        var gds = GDS.NewLibrary(stamp: new DateTime(2026, 10, 9));

        Name(gds, Met1, "met1");
        Name(gds, Via, "via");
        Name(gds, Met2, "met2");

        return new AgentSession(gds, new EditHistory());
    }

    private static void Name(GDS gds, LayerKey key, string name)
    {
        gds.AdditionalInformation.AddLayer(key);
        gds.AdditionalInformation.Layers[key].Name = name;
    }

    ///<summary>A call that has to work.</summary>
    private static AgentResult Done(AgentSession session, string tool, string arguments)
    {
        var result = AgentTools.Call(session, tool, arguments);

        Assert.False(result.IsError, result.Text);

        return result;
    }

    ///<summary>A call that has to be refused - and leave the file and the history exactly as they were.</summary>
    private static string Refused(AgentSession session, string tool, string arguments)
    {
        var before = session.Gds.Serialize();
        int steps = session.History.Count;

        var result = AgentTools.Call(session, tool, arguments);

        Assert.True(result.IsError, result.Text);
        Assert.Null(result.Edit);
        Assert.False(result.Changed);
        Assert.Equal(before, session.Gds.Serialize());
        Assert.Equal(steps, session.History.Count);

        return result.Text;
    }

    ///<summary>The one shape a call made.</summary>
    private static int Made(AgentResult result)
    {
        return Assert.Single(result.Made);
    }

    private static StructureModel Cell(AgentSession session, string name = "TOP")
    {
        return Hierarchy.Named(session.Gds, name)!;
    }

    private static ElementModel Shape(AgentSession session, int id)
    {
        var model = session.ShapeNumbered(id);

        Assert.NotNull(model);

        return model!;
    }

    private static int[] Xy(ElementModel model)
    {
        return ((Int4Data)model.Element.XY.Data!).Values;
    }

    private static Bounds Box(ElementModel model)
    {
        var outline = GdsFlattener.OutlineOf(model);

        Assert.NotNull(outline);

        return Bounds.Of(outline!);
    }

    private static LayerKey? LayerOf(ElementModel model)
    {
        if (model.Element is not IHasLayer onLayer)
            return null;

        return GdsFlattener.KeyOf(onLayer);
    }

    ///<summary>A rectangle drawn by the tool, and its number.</summary>
    private static int Rectangle(AgentSession session, string layer, double left, double bottom, double right, double top)
    {
        string arguments = FormattableString.Invariant($"{{\"layer\": \"{layer}\", \"from\": [{left}, {bottom}], \"to\": [{right}, {top}]}}");

        return Made(Done(session, "draw_rectangle", arguments));
    }

    ///<summary>LEAF, a cell holding one rectangle 4 by 1 µm, made and drawn in - and TOP open again afterwards.</summary>
    private static void Leaf(AgentSession session)
    {
        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");
        Rectangle(session, "met1", 0, 0, 4, 1);
        Done(session, "open_cell", """{"name": "TOP"}""");
    }

    ///
    ///What a cell draws: each shape's layer and corners, the corners sorted and then the shapes - the picture,
    ///in whatever order it was written. A path is outlined from its centerline wherever it is drawn, so through a
    ///mirror the same rectangle starts at another corner.
    ///
    private static List<string> Picture(AgentSession session, string cell = "TOP")
    {
        return GdsFlattener.Flatten(session.Gds, cell).Elements
            .Select(element => $"{element.Layer.Key} " + string.Join(" ", element.Points
                .Select(point => $"{point.X},{point.Y}")
                .OrderBy(text => text, StringComparer.Ordinal)))
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToList();
    }

    #endregion ***********************************************************************



    #region The catalog ***************************************************************

    [Fact]
    public void Every_tool_has_a_name_a_description_and_an_object_schema()
    {
        var names = new HashSet<string>();

        foreach (var tool in AgentTools.All)
        {
            Assert.Matches("^[a-z][a-z_]*$", tool.Name);
            Assert.True(names.Add(tool.Name), $"{tool.Name} twice");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));

            var schema = tool.InputSchema();

            Assert.Equal("object", (string?)schema["type"]);

            var properties = (JsonObject)schema["properties"]!;

            Assert.Equal(tool.Parameters.Count, properties.Count);

            foreach (var required in (JsonArray?)schema["required"] ?? new JsonArray())
                Assert.True(properties.ContainsKey((string)required!), $"{tool.Name} requires {required}, which it does not describe");
        }
    }

    [Fact]
    public void The_catalog_draws_edits_and_reads()
    {
        var names = AgentTools.All.Select(tool => tool.Name).ToHashSet();

        foreach (string expected in new[]
        {
            "list_layers", "list_cells", "open_cell", "find_shapes", "measure", "check_rules",
            "draw_rectangle", "draw_polygon", "draw_circle", "draw_ring", "draw_path", "draw_text", "add_label",
            "move", "copy", "array", "turn", "align", "delete", "change_layer", "boolean", "grow", "corners",
            "make_cell", "place_cell", "flatten", "rename_cell", "delete_cell", "undo_last_change",
            "list_structures", "place_structure"
        })
            Assert.Contains(expected, names);

        //Thirty-one, which is what the readme and the library guide say.
        Assert.Equal(31, names.Count);

        Assert.False(AgentTools.Named(AgentTools.ListStructuresName)!.Edits);
        Assert.True(AgentTools.Named(AgentTools.PlaceStructureName)!.Edits);

        Assert.All(AgentTools.All.Where(tool => tool.Name.StartsWith("draw_") || tool.Name == "move"), tool => Assert.True(tool.Edits));
        Assert.All(AgentTools.All.Where(tool => tool.Name.StartsWith("list_") || tool.Name == "find_shapes"), tool => Assert.False(tool.Edits));
    }

    ///
    ///The keywords Google's function calling takes and nothing past them - additionalProperties, oneOf or
    ///default in a schema is a hard error there, and a tool set that works with one provider has to work with
    ///the others.
    ///
    [Fact]
    public void Schemas_use_only_the_keywords_every_provider_reads()
    {
        var allowed = new HashSet<string> { "type", "description", "properties", "required", "items", "enum", "minItems", "maxItems" };

        void walk(JsonNode? node, string where)
        {
            if (node is JsonObject schema)
            {
                foreach (var (key, value) in schema)
                {
                    Assert.True(allowed.Contains(key), $"{where} uses {key}");

                    if (key == "properties")
                    {
                        foreach (var (name, property) in (JsonObject)value!)
                            walk(property, $"{where}.{name}");
                    }
                    else if (key == "items")
                    {
                        walk(value, $"{where}[]");
                    }
                }
            }
        }

        foreach (var tool in AgentTools.All)
            walk(tool.InputSchema(), tool.Name);
    }

    [Fact]
    public void A_schema_is_a_new_object_each_time()
    {
        var tool = AgentTools.Named("draw_rectangle")!;

        tool.InputSchema()["type"] = "changed";

        Assert.Equal("object", (string?)tool.InputSchema()["type"]);
    }

    [Fact]
    public void The_conventions_name_the_units_and_the_numbers()
    {
        Assert.Contains("microns", AgentTools.Conventions);
        Assert.Contains("#12", AgentTools.Conventions);
        Assert.Contains("undo", AgentTools.Conventions);
        Assert.Contains("manufacturing grid", AgentTools.Conventions);
    }

    #endregion ***********************************************************************



    #region Calls and their arguments *************************************************

    [Fact]
    public void A_tool_that_does_not_exist_is_answered_with_the_ones_that_do()
    {
        string said = Refused(Session(), "draw_square", "{}");

        Assert.Contains("draw_square", said);
        Assert.Contains("draw_rectangle", said);
    }

    [Fact]
    public void An_argument_a_tool_does_not_take_is_refused_with_the_ones_it_does()
    {
        string said = Refused(Session(), "draw_circle", """{"layer": "met1", "center": [0, 0], "diameter": 2}""");

        Assert.Contains("diameter", said);
        Assert.Contains("radius", said);
    }

    [Fact]
    public void A_missing_argument_is_refused_with_what_it_is_for()
    {
        string said = Refused(Session(), "draw_circle", """{"layer": "met1", "center": [0, 0]}""");

        Assert.Contains("radius", said);
        Assert.Contains("microns", said);
    }

    [Fact]
    public void Arguments_that_are_not_json_are_refused()
    {
        string said = Refused(Session(), "draw_circle", "{layer: met1");

        Assert.Contains("not JSON", said);
    }

    [Fact]
    public void Arguments_sent_as_a_string_of_json_are_read_through()
    {
        var session = Session();

        Done(session, "draw_rectangle", """ "{\"layer\": \"met1\", \"from\": [0, 0], \"to\": [1, 1]}" """);

        Assert.Single(Cell(session).Elements);
    }

    [Fact]
    public void A_number_written_as_text_and_a_shape_written_with_a_hash_are_understood()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "move", $$"""{"shapes": ["#{{square}}"], "dx": "2.5", "dy": 0}""");

        Assert.Equal(new Bounds(2500, 0, 3500, 1000), Box(Shape(session, square)));
    }

    [Fact]
    public void A_null_argument_is_one_left_out()
    {
        var session = Session();

        var result = Done(session, "draw_circle", """{"layer": "met1", "center": [0, 0], "radius": 1, "sides": null}""");

        Assert.Equal(Shapes.DefaultVertices + 1, Xy(Shape(session, Made(result))).Length / 2);
    }

    [Fact]
    public void A_choice_outside_the_list_is_refused_with_the_list()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        string said = Refused(session, "turn", $$"""{"shapes": [{{square}}], "turn": "45"}""");

        Assert.Contains("90, 180, 270, flip_x, flip_y", said);
    }

    #endregion ***********************************************************************



    #region One step of undo per call *************************************************

    [Fact]
    public void A_call_that_adds_many_shapes_is_one_step_of_undo()
    {
        var session = Session();

        var result = Done(session, "draw_text", """{"layer": "met1", "text": "HELLO", "at": [0, 0], "height": 7}""");

        Assert.True(result.Made.Count > 1);
        Assert.Equal(1, session.History.Count);
        Assert.Same(result.Edit, session.History.LastStep);
        Assert.True(result.Changed);

        session.History.Undo();

        Assert.Empty(Cell(session).Elements);
    }

    [Fact]
    public void Undoing_every_call_gives_back_the_file_byte_for_byte()
    {
        var session = Session();
        var before = session.Gds.Serialize();

        int one = Rectangle(session, "met1", 0, 0, 2, 1);
        int two = Rectangle(session, "met1", 1, 0, 3, 1);

        Done(session, "boolean", $$"""{"shapes": [{{one}}, {{two}}], "operation": "union"}""");
        Done(session, "draw_path", """{"layer": "met2", "points": [[0, 5], [10, 5], [10, 15]], "width": 1, "bend_radius": 2}""");
        Done(session, "make_cell", """{"name": "EMPTY"}""");

        while (session.History.Undo())
        {
        }

        Assert.Equal(before, session.Gds.Serialize());
    }

    [Fact]
    public void The_undo_button_says_the_step_was_the_assistants()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.StartsWith("AI draw rectangle", session.History.NextUndo);
    }

    #endregion ***********************************************************************



    #region Shape numbers *************************************************************

    [Fact]
    public void A_shape_keeps_its_number_as_others_come_and_go()
    {
        var session = Session();

        int first = Rectangle(session, "met1", 0, 0, 1, 1);
        int second = Rectangle(session, "met1", 2, 0, 3, 1);

        Done(session, "delete", $$"""{"shapes": [{{first}}]}""");
        Done(session, "move", $$"""{"shapes": [{{second}}], "dx": 0, "dy": 1}""");

        Assert.Equal(new Bounds(2000, 1000, 3000, 2000), Box(Shape(session, second)));
    }

    [Fact]
    public void A_deleted_shape_is_said_to_be_gone_and_is_back_after_undo()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "delete", $$"""{"shapes": [{{square}}]}""");

        Assert.Contains("not in the layout any more", Refused(session, "move", $$"""{"shapes": [{{square}}], "dx": 1, "dy": 0}"""));

        session.History.Undo();

        Done(session, "move", $$"""{"shapes": [{{square}}], "dx": 1, "dy": 0}""");
    }

    [Fact]
    public void A_number_nobody_handed_out_is_refused()
    {
        Assert.Contains("There is no shape #99", Refused(Session(), "delete", """{"shapes": [99]}"""));
    }

    [Fact]
    public void A_host_can_number_the_selection_the_same_way()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Equal(square, session.IdOf(Cell(session).Elements[0]));
    }

    [Fact]
    public void Shapes_in_two_cells_are_refused_in_one_call()
    {
        var session = Session();
        int top = Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");

        int leaf = Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Contains("One call works in one cell", Refused(session, "delete", $$"""{"shapes": [{{top}}, {{leaf}}]}"""));
    }

    #endregion ***********************************************************************



    #region Layers and units **********************************************************

    [Fact]
    public void A_layer_goes_by_its_name_its_numbers_or_both()
    {
        var session = Session();

        foreach (string named in new[] { "met2", "MET2", "69/20", "69", "met2 (69/20)" })
        {
            int drawn = Rectangle(session, named, 0, 0, 1, 1);

            Assert.Equal(Met2, LayerOf(Shape(session, drawn)));
        }
    }

    [Fact]
    public void An_unknown_layer_name_is_refused_with_the_names_there_are()
    {
        string said = Refused(Session(), "draw_rectangle", """{"layer": "met7", "from": [0, 0], "to": [1, 1]}""");

        Assert.Contains("no layer called \"met7\"", said);
        Assert.Contains("met1 (68/20)", said);
        Assert.Contains("via (68/44)", said);
    }

    [Fact]
    public void A_near_miss_of_a_name_is_offered_back()
    {
        string said = Refused(Session(), "draw_rectangle", """{"layer": "met", "from": [0, 0], "to": [1, 1]}""");

        Assert.Contains("Did you mean met1 (68/20) or met2 (69/20)?", said);
    }

    [Fact]
    public void A_layer_number_with_several_datatypes_asks_which()
    {
        string said = Refused(Session(), "draw_rectangle", """{"layer": "68", "from": [0, 0], "to": [1, 1]}""");

        Assert.Contains("68/20", said);
        Assert.Contains("68/44", said);
    }

    [Fact]
    public void Numbers_draw_on_a_layer_the_file_does_not_have_yet()
    {
        var session = Session();

        Rectangle(session, "70/20", 0, 0, 1, 1);

        Assert.True(session.Gds.AdditionalInformation.Layers.ContainsKey(new LayerKey(70, 20)));
    }

    ///
    ///**A layer a tool starts takes the PDK's row**, where the host says the file is drawn in one - its name, height and
    ///role - as one added in the layer panel does. It came in bare - a number, with no height for the 3D view and no role
    ///for Trace net - until the 2026-10-21 audit. See GDS.RowsForNewLayers.
    ///
    [Fact]
    public void A_layer_a_tool_starts_takes_the_pdks_row()
    {
        var session = Session();

        session.Gds.RowsForNewLayers = () => LayerNames.Parse("70,20,met3,,2785,845,conductor,\n");

        Rectangle(session, "70/20", 0, 0, 1, 1);

        var layer = session.Gds.AdditionalInformation.Layers[new LayerKey(70, 20)];

        Assert.Equal("met3", layer.Name);
        Assert.Equal(845, layer.Depth);
        Assert.Equal(LayerRole.Conductor, layer.Role);
    }

    [Fact]
    public void Microns_are_database_units_on_the_files_own_grid()
    {
        var session = Session();

        Assert.Equal(0.001, session.MicronsPerUnit, 12);

        int drawn = Rectangle(session, "met1", -1.5, 0.25, 2, 0.75);

        Assert.Equal(new Bounds(-1500, 250, 2000, 750), Box(Shape(session, drawn)));
    }

    [Fact]
    public void A_size_under_one_database_unit_is_refused_with_the_grid()
    {
        string said = Refused(Session(), "draw_rectangle", """{"layer": "met1", "center": [0, 0], "width": 0.0004, "height": 1}""");

        Assert.Contains("0.001 µm", said);
    }

    [Fact]
    public void A_coordinate_past_what_a_layout_reaches_is_refused()
    {
        Assert.Contains("further than a layout reaches", Refused(Session(), "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [1e9, 1]}"""));
    }

    #endregion ***********************************************************************



    #region Drawing *******************************************************************

    [Fact]
    public void A_rectangle_from_two_corners_is_a_boundary_on_its_layer()
    {
        var session = Session();

        var result = Done(session, "draw_rectangle", """{"layer": "met1", "from": [2, 1], "to": [0, 0]}""");
        var drawn = Shape(session, Made(result));

        Assert.IsType<BoundaryModel>(drawn.Element);
        Assert.Equal(Met1, LayerOf(drawn));
        Assert.Equal(new Bounds(0, 0, 2000, 1000), Box(drawn));
        Assert.Contains("2 by 1 µm rectangle on met1 (68/20) in TOP", result.Text);
        Assert.Contains($"#{Made(result)}", result.Text);
    }

    [Fact]
    public void A_rectangle_from_its_center_and_size()
    {
        var session = Session();

        var result = Done(session, "draw_rectangle", """{"layer": "met1", "center": [5, 5], "width": 2, "height": 4}""");

        Assert.Equal(new Bounds(4000, 3000, 6000, 7000), Box(Shape(session, Made(result))));
    }

    [Fact]
    public void A_rectangle_asked_for_both_ways_or_neither_or_flat_is_refused()
    {
        var session = Session();

        Assert.Contains("not both", Refused(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [1, 1], "width": 1}"""));
        Assert.Contains("two opposite corners", Refused(session, "draw_rectangle", """{"layer": "met1"}"""));
        Assert.Contains("no area", Refused(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [0, 1]}"""));
    }

    [Fact]
    public void A_polygon_is_closed_for_the_model()
    {
        var session = Session();

        var result = Done(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [2, 0], [2, 1], [1, 1], [1, 2], [0, 2]]}""");
        var values = Xy(Shape(session, Made(result)));

        Assert.Equal(14, values.Length);
        Assert.Equal(values[0], values[^2]);
        Assert.Equal(values[1], values[^1]);
        Assert.Equal(3_000_000, Math.Abs(Measure.AreaOf(GdsFlattener.OutlineOf(Shape(session, Made(result)))!)));
    }

    [Fact]
    public void A_polygon_that_crosses_itself_is_refused_with_the_crossing()
    {
        string said = Refused(Session(), "draw_polygon", """{"layer": "met1", "points": [[0, 0], [2, 2], [2, 0], [0, 2]]}""");

        Assert.Contains("crosses itself", said);
        Assert.Contains("corner 0", said);
    }

    [Fact]
    public void A_polygon_with_no_area_or_too_few_corners_on_the_grid_is_refused()
    {
        var session = Session();

        Assert.Contains("one line", Refused(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [1, 0], [2, 0]]}"""));
        Assert.Contains("three different corners", Refused(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [0.0001, 0], [1, 1]]}"""));
    }

    [Fact]
    public void A_circles_corners_sit_on_the_radius()
    {
        var session = Session();

        var result = Done(session, "draw_circle", """{"layer": "met1", "center": [10, 10], "radius": 5, "sides": 32}""");
        var values = Xy(Shape(session, Made(result)));

        Assert.Equal(33 * 2, values.Length);

        for (int i = 0; i + 1 < values.Length; i += 2)
        {
            double away = Math.Sqrt(Math.Pow(values[i] - 10_000, 2) + Math.Pow(values[i + 1] - 10_000, 2));

            Assert.InRange(away, 4999, 5001);
        }
    }

    [Fact]
    public void An_ellipse_has_a_radius_each_way()
    {
        var session = Session();

        var result = Done(session, "draw_circle", """{"layer": "met1", "center": [0, 0], "radius": 4, "radius_y": 2}""");

        Assert.Equal(new Bounds(-4000, -2000, 4000, 2000), Box(Shape(session, Made(result))));
        Assert.Contains("ellipse", result.Text);
    }

    [Fact]
    public void A_ring_is_one_outline_with_the_area_between_its_circles()
    {
        var session = Session();

        var result = Done(session, "draw_ring", """{"layer": "met1", "center": [0, 0], "outer_radius": 10, "inner_radius": 5}""");
        double area = Math.Abs(Measure.AreaOf(GdsFlattener.OutlineOf(Shape(session, Made(result)))!));

        int sides = Shapes.DefaultVertices;
        double expected = sides / 2.0 * Math.Sin(2 * Math.PI / sides) * ((10_000.0 * 10_000) - (5_000.0 * 5_000));

        Assert.InRange(area, expected * 0.999, expected * 1.001);
    }

    [Fact]
    public void A_ring_whose_hole_is_no_smaller_than_it_is_refused()
    {
        Assert.Contains("less than the outer", Refused(Session(), "draw_ring", """{"layer": "met1", "center": [0, 0], "outer_radius": 5, "inner_radius": 5}"""));
    }

    [Fact]
    public void A_path_is_one_path_element_with_its_width_and_ends()
    {
        var session = Session();

        var result = Done(session, "draw_path", """{"layer": "met2", "points": [[0, 0], [10, 0], [10, 5]], "width": 0.5, "ends": "round"}""");
        var drawn = Shape(session, Made(result));

        Assert.IsType<PathModel>(drawn.Element);
        Assert.Equal((500, Paths.Ends.Round), Paths.Of(drawn));
        Assert.Equal(new[] { 0, 0, 10_000, 0, 10_000, 5_000 }, Xy(drawn));
    }

    ///
    ///The corner of a route is where it was asked for: the runs aim at it, and the arc joins them - so the
    ///route starts and ends where it was told to and every point of the bend is the radius from its center.
    ///
    [Fact]
    public void A_bent_path_turns_its_corners_into_arcs_of_the_radius()
    {
        var session = Session();

        var result = Done(session, "draw_path", """{"layer": "met2", "points": [[0, 0], [10, 0], [10, 10]], "width": 1, "bend_radius": 2}""");
        var drawn = Shape(session, Made(result));
        var values = Xy(drawn);

        Assert.Equal((0, 0), (values[0], values[1]));
        Assert.Equal((10_000, 10_000), (values[^2], values[^1]));

        int onArc = 0;

        for (int i = 0; i + 1 < values.Length; i += 2)
        {
            Assert.False(values[i] == 10_000 && values[i + 1] == 0, "the square corner is still there");

            if (values[i] > 8_000 && values[i + 1] < 2_000)
            {
                double away = Math.Sqrt(Math.Pow(values[i] - 8_000, 2) + Math.Pow(values[i + 1] - 2_000, 2));

                Assert.InRange(away, 1_999, 2_001);

                onArc++;
            }
        }

        Assert.True(onArc > 0);
    }

    [Fact]
    public void A_bend_with_no_room_on_its_run_is_refused_with_the_numbers()
    {
        string said = Refused(Session(), "draw_path", """{"layer": "met2", "points": [[0, 0], [1, 0], [1, 10]], "width": 0.5, "bend_radius": 2}""");

        Assert.Contains("is 1 µm long", said);
        Assert.Contains("need 2 µm", said);
        Assert.Contains("smaller bend_radius", said);
    }

    [Fact]
    public void A_bend_tighter_than_half_the_width_is_refused()
    {
        Assert.Contains("fold over itself", Refused(Session(), "draw_path", """{"layer": "met2", "points": [[0, 0], [10, 0], [10, 10]], "width": 5, "bend_radius": 2}"""));
    }

    [Fact]
    public void A_path_that_doubles_back_cannot_be_bent()
    {
        Assert.Contains("straight back on itself", Refused(Session(), "draw_path", """{"layer": "met2", "points": [[0, 0], [10, 0], [5, 0]], "width": 1, "bend_radius": 1}"""));
    }

    [Fact]
    public void Text_is_written_as_shapes_from_its_bottom_left_corner()
    {
        var session = Session();

        var result = Done(session, "draw_text", """{"layer": "met1", "text": "HI", "at": [1, 2], "height": 7}""");

        var box = Bounds.Empty;

        foreach (int id in result.Made)
            box = box.Union(Box(Shape(session, id)));

        //H is five columns, then a column of space; I is lit in the middle three of its own five.
        Assert.Equal(new Bounds(1000, 2000, 11_000, 9000), box);
    }

    [Fact]
    public void Text_with_nothing_printable_is_refused()
    {
        Assert.Contains("nothing to write", Refused(Session(), "draw_text", """{"layer": "met1", "text": "   ", "at": [0, 0], "height": 7}"""));
    }

    ///
    ///An L tells which way it lies, where HI reads the same mirrored: a stem five strokes from its foot's far end
    ///and seven tall. Upright at [1, 2] on 1 µm strokes, its foot runs along the bottom to (6, 2) and its stem up
    ///to (1, 9).
    ///
    [Fact]
    public void Text_turned_a_quarter_counterclockwise_lies_on_its_side_from_the_same_corner()
    {
        var session = Session();

        var result = Done(session, "draw_text", """{"layer": "met1", "text": "L", "at": [1, 2], "height": 7, "turn": "90"}""");
        var corners = CornersOf(session, result);

        //The stem along the bottom now, and the foot standing up at its right-hand end.
        Assert.Equal(new Bounds(1000, 2000, 8000, 7000), Box(Shape(session, result.Made[0])));
        Assert.Contains(new Element.Point(7000, 7000), corners);
        Assert.Contains(new Element.Point(7000, 3000), corners);
        Assert.Contains("capitals 7 µm tall, turned 90° counterclockwise, covering", result.Text);
    }

    [Fact]
    public void Mirrored_text_is_flipped_top_to_bottom_in_its_own_box()
    {
        var session = Session();

        var result = Done(session, "draw_text", """{"layer": "met1", "text": "L", "at": [1, 2], "height": 7, "mirror": true}""");
        var corners = CornersOf(session, result);

        //The foot along the top, and the stem coming down from it.
        Assert.Equal(new Bounds(1000, 2000, 6000, 9000), Box(Shape(session, result.Made[0])));
        Assert.Contains(new Element.Point(6000, 8000), corners);
        Assert.DoesNotContain(new Element.Point(6000, 3000), corners);
        Assert.Contains("mirrored top to bottom, covering", result.Text);
    }

    ///<summary>GDSII's own order, and what the tool's description promises: mirrored, then a half turn, reads right to left.</summary>
    [Fact]
    public void Mirrored_and_turned_half_way_text_is_mirror_writing()
    {
        var session = Session();

        var result = Done(session, "draw_text", """{"layer": "met1", "text": "L", "at": [1, 2], "height": 7, "turn": "180", "mirror": true}""");
        var corners = CornersOf(session, result);

        //The stem on the right now, and the foot running left from its bottom.
        Assert.Contains(new Element.Point(5000, 9000), corners);
        Assert.Contains(new Element.Point(5000, 3000), corners);
        Assert.Contains(new Element.Point(1000, 3000), corners);
        Assert.DoesNotContain(new Element.Point(2000, 9000), corners);
        Assert.Contains("mirrored top to bottom and turned 180° counterclockwise", result.Text);
    }

    [Fact]
    public void Text_turns_only_by_quarters()
    {
        Assert.Contains("\"turn\" has to be one of 0, 90, 180, 270", Refused(Session(), "draw_text", """{"layer": "met1", "text": "L", "at": [0, 0], "height": 7, "turn": "45"}"""));
    }

    [Fact]
    public void A_label_is_a_text_element_at_its_point()
    {
        var session = Session();

        var result = Done(session, "add_label", """{"layer": "met1", "text": "VDD", "at": [3, 4]}""");
        var label = Shape(session, Made(result));

        Assert.IsType<TextModel>(label.Element);
        Assert.Equal("VDD", RetextElement.TextOf(label));
        Assert.Equal(new[] { 3000, 4000 }, Xy(label));
    }

    #endregion ***********************************************************************



    #region Structures from the Generator *********************************************

    ///
    ///**What the Draw menu's Generator makes, put down whole** (2026-11-01, asked for): one call and one step of undo,
    ///the structure Marks makes from the same numbers, on the layer asked for and with the middle of the box round it
    ///where it was asked - the way the panel's Place puts one down.
    ///
    [Fact]
    public void A_structure_from_the_generator_is_put_down_whole_round_its_point_as_one_step()
    {
        var session = Session();

        var result = Done(session, "place_structure", """{"kind": "ratrace", "layer": "met1", "at": [100, 50], "numbers": "radius=70, ring=5, port=8, lead=20"}""");
        var expected = Marks.Named("ratrace")!.Make(new Dictionary<string, double> { ["radius"] = 70, ["ring"] = 5, ["port"] = 8, ["lead"] = 20 }, microns => (int)Math.Round(microns * 1000));

        Assert.Equal(expected.Outlines.Count, result.Made.Count);
        Assert.Equal(1, session.History.Count);
        Assert.Equal("AI place rat-race coupler", result.Edit!.Description);
        Assert.All(result.Made, id => Assert.Equal(Met1, LayerOf(Shape(session, id))));

        var box = Bounds.Empty;

        foreach (int id in result.Made)
            box = box.Union(Box(Shape(session, id)));

        Assert.InRange((box.Left + (long)box.Right) / 2.0, 99_999, 100_001);
        Assert.InRange((box.Bottom + (long)box.Top) / 2.0, 49_999, 50_001);
        Assert.Contains("Put down the rat-race coupler on met1 (68/20) in TOP, its middle at (100, 50), from radius 70 µm, ring 5 µm, port 8 µm, lead 20 µm", result.Text);
    }

    ///<summary>Numbers go by the kind's own names, whatever their case or unit; any left out are the kind's own.</summary>
    [Fact]
    public void A_structures_numbers_go_by_name_and_any_left_out_are_its_own()
    {
        var session = Session();

        var spiral = Done(session, "place_structure", """{"kind": "spiral", "layer": "met2", "at": [0, 0], "numbers": "Turns: 2; width = 5 um"}""");

        Assert.Contains("from turns 2, width 5 µm, spacing 5 µm, inner 100 µm, lead 50 µm", spiral.Text);
        Assert.Contains("nH, by Mohan's formula for a square spiral.", spiral.Text);
        Assert.Contains("each on its own layer: underpass (inductor underpass), inductorvias (inductor vias).", spiral.Text);

        //The same numbers as a JSON object, which is how a model may send them whatever the schema says.
        var underpass = Done(session, "place_structure", """{"kind": "underpass", "layer": "met1", "at": [0, 0], "numbers": {"turns": 2, "width": 5}}""");

        Assert.Contains("from turns 2, width 5 µm, spacing 5 µm", underpass.Text);

        var stepped = Done(session, "place_structure", """{"kind": "steppedfilter", "layer": "met1", "at": [0, 300], "numbers": "wide_run=100"}""");

        Assert.Contains("wide run 100 µm", stepped.Text);
    }

    [Fact]
    public void A_structure_with_a_number_it_does_not_have_or_cannot_be_made_from_is_refused_and_nothing_is_put_down()
    {
        Assert.Contains("The rat-race coupler has no number called diameter. Its numbers are radius, ring, port, lead.",
            Refused(Session(), "place_structure", """{"kind": "ratrace", "layer": "met1", "at": [0, 0], "numbers": "diameter=5"}"""));

        Assert.Contains("leaves no hole in the middle. Nothing was put down.",
            Refused(Session(), "place_structure", """{"kind": "ratrace", "layer": "met1", "at": [0, 0], "numbers": "radius=7, ring=8"}"""));

        Assert.Contains("The radius of 0.0001 µm is less than one database unit",
            Refused(Session(), "place_structure", """{"kind": "ratrace", "layer": "met1", "at": [0, 0], "numbers": "radius=0.0001"}"""));

        Assert.Contains("is not a name and a number, like radius=700.",
            Refused(Session(), "place_structure", """{"kind": "ratrace", "layer": "met1", "at": [0, 0], "numbers": "radius"}"""));

        Assert.Contains("\"kind\" has to be one of",
            Refused(Session(), "place_structure", """{"kind": "transistor", "layer": "met1", "at": [0, 0]}"""));
    }

    [Fact]
    public void A_structure_turns_about_its_middle()
    {
        var session = Session();
        string numbers = "width=16, length=10, inset=3, notch=1, feed=2, lead=5";

        var upright = Done(session, "place_structure", $$"""{"kind": "patch", "layer": "met1", "at": [0, 0], "numbers": "{{numbers}}"}""");
        var turned = Done(session, "place_structure", $$"""{"kind": "patch", "layer": "met1", "at": [0, 0], "numbers": "{{numbers}}", "turn": "90"}""");

        var standing = Box(Shape(session, Made(upright)));
        var lying = Box(Shape(session, Made(turned)));

        Assert.Equal((16_000, 15_000), (standing.Width, standing.Height));
        Assert.Equal((standing.Width, standing.Height), (lying.Height, lying.Width));
        Assert.Contains("turned 90° counterclockwise", turned.Text);
    }

    ///<summary>A ring's curve is worked out, so on a deck with a grid its corners go on it - as the panel's do - and the answer says so.</summary>
    [Fact]
    public void A_structures_worked_out_corners_go_on_the_decks_grid()
    {
        var session = OnGrid();

        var result = Done(session, "place_structure", """{"kind": "ring", "layer": "met1", "at": [0.0013, 0], "numbers": "radius=10, width=0.5, gap=0.2, bus=30"}""");

        AllOnTheGrid(CornersOf(session, result));
        Assert.Contains("Its worked-out corners are on the deck's 0.005 µm grid (grid.1).", result.Text);
    }

    [Fact]
    public void The_structures_are_listed_by_key_and_one_kinds_numbers_are_said_with_their_meanings()
    {
        string all = Done(Session(), "list_structures", "{}").Text;

        Assert.All(Marks.All, maker => Assert.Contains($"{maker.Key} - {maker.Name} (", all));
        Assert.Contains("\nRF and microwave: ", all);

        string one = Done(Session(), "list_structures", """{"kind": "ratrace"}""").Text;

        Assert.Contains("ratrace - Rat-race coupler, under RF and microwave.", one);
        Assert.Contains("- radius: The ring's radius, to the middle of its line. In microns, 700 µm when not given.", one);
        Assert.Contains("- turns: How many turns. A count, 3 when not given.", Done(Session(), "list_structures", """{"kind": "spiral"}""").Text);
        Assert.Contains("- angle: How wide the fan opens. In degrees, 60° when not given.", Done(Session(), "list_structures", """{"kind": "radialstub"}""").Text);
    }

    #endregion ***********************************************************************



    #region Reading *******************************************************************

    [Fact]
    public void The_layers_are_listed_with_what_the_cell_has_on_each()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 1, 1);
        Rectangle(session, "met1", 2, 0, 3, 1);

        string said = Done(session, "list_layers", "{}").Text;

        Assert.Contains("met1 (68/20), 2 shapes", said);
        Assert.Contains("met2 (69/20), none", said);
    }

    [Fact]
    public void The_cells_are_listed_as_a_tree()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0]}""");

        string said = Done(session, "list_cells", "{}").Text;

        Assert.Contains("TOP: top cell", said);
        Assert.Contains("  LEAF: placed once", said);
        Assert.Contains("being worked in", said);
    }

    [Fact]
    public void Shapes_are_found_by_layer_kind_and_area()
    {
        var session = Session();

        int left = Rectangle(session, "met1", 0, 0, 1, 1);
        int right = Rectangle(session, "met2", 10, 0, 11, 1);

        Done(session, "add_label", """{"layer": "met1", "text": "A", "at": [0.5, 0.5]}""");

        string onMet2 = Done(session, "find_shapes", """{"layer": "met2"}""").Text;

        Assert.Contains($"#{right} met2", onMet2);
        Assert.DoesNotContain($"#{left} ", onMet2);

        string labels = Done(session, "find_shapes", """{"kind": "label"}""").Text;

        Assert.Contains("label \"A\"", labels);
        Assert.StartsWith("1 of the 3 shapes", labels);

        string nearLeft = Done(session, "find_shapes", """{"area": [-1, -1, 2, 2]}""").Text;

        Assert.Contains($"#{left} ", nearLeft);
        Assert.DoesNotContain($"#{right} ", nearLeft);
    }

    [Fact]
    public void A_limit_says_how_many_more_there_are()
    {
        var session = Session();

        for (int i = 0; i < 5; i++)
            Rectangle(session, "met1", i * 2, 0, (i * 2) + 1, 1);

        string said = Done(session, "find_shapes", """{"limit": 2}""").Text;

        Assert.StartsWith("5 of the 5 shapes in TOP match; the first 2:", said);
    }

    [Fact]
    public void A_limit_outside_what_find_shapes_lists_and_too_many_to_measure_are_refused()
    {
        var session = Session();
        var ids = new List<int>();

        for (int i = 0; i < 11; i++)
            ids.Add(Rectangle(session, "met1", i * 2, 0, (i * 2) + 1, 1));

        Assert.Contains("from 1 to 500", Refused(session, "find_shapes", """{"limit": 0}"""));
        Assert.Contains("up to 10 shapes", Refused(session, "measure", $"{{\"shapes\": [{string.Join(", ", ids)}]}}"));
    }

    [Fact]
    public void A_placement_is_found_by_what_it_draws()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");
        Rectangle(session, "met1", 0, 0, 1, 1);
        Done(session, "open_cell", """{"name": "TOP"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [100, 0]}""");

        string said = Done(session, "find_shapes", """{"area": [100.5, 0.5, 100.6, 0.6]}""").Text;

        Assert.Contains("placement of LEAF at (100, 0), covering (100, 0) to (101, 1)", said);
    }

    [Fact]
    public void Two_shapes_are_measured_edge_to_edge()
    {
        var session = Session();

        int one = Rectangle(session, "met1", 0, 0, 1, 1);
        int apart = Rectangle(session, "met1", 2, 0, 3, 1);
        int touching = Rectangle(session, "met1", 1, 0, 1.5, 1);
        int overlapping = Rectangle(session, "met1", 0.5, 0.5, 2, 2);

        string said = Done(session, "measure", $$"""{"shapes": [{{one}}, {{apart}}, {{touching}}, {{overlapping}}]}""").Text;

        Assert.Contains($"#{one} and #{apart}: 1 µm apart at the closest", said);
        Assert.Contains($"#{one} and #{touching}: they touch", said);
        Assert.Contains($"#{one} and #{overlapping}: they overlap", said);
        Assert.Contains("area 1 µm²", said);
    }

    [Fact]
    public void Measuring_an_L_finds_the_gap_its_box_hides()
    {
        var session = Session();

        int el = Made(Done(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [4, 0], [4, 1], [1, 1], [1, 4], [0, 4]]}"""));
        int square = Rectangle(session, "met1", 2, 2, 3, 3);

        Assert.Contains("1 µm apart", Done(session, "measure", $$"""{"shapes": [{{el}}, {{square}}]}""").Text);
    }

    #endregion ***********************************************************************



    #region Rules *********************************************************************

    private const string Deck = """
        layer met1 68/20
        rule met1.1 width met1 140 "Met1 width"
        rule met1.2 space met1 140 "Met1 spacing"
        """;

    [Fact]
    public void Checking_rules_with_no_deck_says_so()
    {
        Assert.Contains("No rule deck is loaded", Refused(Session(), "check_rules", "{}"));
    }

    ///<summary>Where a deck comes from is the host's to say - the app's rules panel, gds mcp's load_rules - and nothing when it says nothing.</summary>
    [Fact]
    public void Checking_rules_with_no_deck_ends_on_what_the_host_says_to_do()
    {
        var session = Session();

        Assert.Equal("No rule deck is loaded, so there is nothing to check against.", Refused(session, "check_rules", "{}"));

        session.HowToLoadRules = "Load one with load_rules.";

        Assert.Equal("No rule deck is loaded, so there is nothing to check against. Load one with load_rules.", Refused(session, "check_rules", "{}"));
    }

    [Fact]
    public void A_change_that_breaks_a_rule_says_which_and_where()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(Deck);

        string said = Done(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [0.1, 1]}""").Text;

        Assert.Contains("this change broke 1", said);
        Assert.Contains("met1.1 (width under 0.14 µm: Met1 width)", said);
    }

    [Fact]
    public void A_change_that_breaks_nothing_says_that()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(Deck);

        Assert.Contains("nothing broken around this change", Done(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [1, 1]}""").Text);
    }

    [Fact]
    public void Only_what_the_change_broke_is_reported_not_what_was_there()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(Deck);

        Rectangle(session, "met1", 0, 0, 0.1, 1);

        string said = Done(session, "draw_rectangle", """{"layer": "met1", "from": [0.2, 0], "to": [1.2, 1]}""").Text;

        Assert.Contains("met1.2", said);
        Assert.DoesNotContain("met1.1", said);
    }

    [Fact]
    public void Mending_a_violation_is_said_too()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(Deck);

        int narrow = Rectangle(session, "met1", 0, 0, 0.1, 1);

        Assert.Contains("cleared 1", Done(session, "delete", $$"""{"shapes": [{{narrow}}]}""").Text);
    }

    [Fact]
    public void No_check_runs_after_a_change_when_the_host_turns_it_off()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(Deck);
        session.CheckAfterEdits = false;

        Assert.DoesNotContain("Rules:", Done(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [0.1, 1]}""").Text);
    }

    [Fact]
    public void Check_rules_lists_each_broken_rule_with_where()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 0.1, 1);
        Rectangle(session, "met1", 0.2, 0, 1.2, 1);

        session.Deck = DrcDeck.Parse(Deck);

        string said = Done(session, "check_rules", "{}").Text;

        Assert.Contains("2 violations of 2 rules", said);
        Assert.Contains("met1.1 (width under 0.14 µm: Met1 width), once: (0, 0) to (0.1, 1)", said);
    }

    ///
    ///A fault inside a placed cell is found in that cell's own coordinates while working in it, and where the
    ///placement puts it over everything.
    ///
    [Fact]
    public void Check_rules_over_everything_finds_faults_where_placements_put_them()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [20, 0]}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");
        Rectangle(session, "met1", 0, 0, 0.1, 1);

        session.Deck = DrcDeck.Parse(Deck);

        string inCell = Done(session, "check_rules", "{}").Text;

        Assert.Contains("over LEAF: 1 violation of 1 rule", inCell);
        Assert.Contains("(0, 0) to (0.1, 1)", inCell);

        string overAll = Done(session, "check_rules", """{"everything": true}""").Text;

        Assert.Contains("over the whole layout: 1 violation of 1 rule", overAll);
        Assert.Contains("(20, 0) to (20.1, 1)", overAll);
    }

    ///
    ///A fault the person waived in the app is left out of what check_rules lists, and said to be: a model told to
    ///fix every violation would otherwise set about one somebody looked at and let stand.
    ///
    [Fact]
    public void Check_rules_leaves_out_what_the_person_waived_and_says_so()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 0.1, 1);
        Rectangle(session, "met1", 0.2, 0, 1.2, 1);

        session.Deck = DrcDeck.Parse(Deck);
        session.Waived = new HashSet<string> { "met1.1@0,0,100,1000" };

        string said = Done(session, "check_rules", "{}").Text;

        Assert.Contains("1 violation of 1 rule", said);
        Assert.DoesNotContain("met1.1 (width", said);
        Assert.Contains("Left out: 1 violation the person has waived", said);
    }

    ///<summary>And a rule the person switched off is not checked at all, as the app's own check does not check it.</summary>
    [Fact]
    public void Check_rules_leaves_out_a_rule_the_person_switched_off_and_says_so()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 0.1, 1);

        session.Deck = DrcDeck.Parse(Deck);
        session.RulesOff = new HashSet<string> { "met1.1" };

        string said = Done(session, "check_rules", "{}").Text;

        Assert.Contains("Checked 1 rule over TOP: nothing broken.", said);
        Assert.Contains("Left out: 1 rule the person has switched off.", said);
    }

    ///<summary>The check after a change leaves out the waived too, so a change that breaks only what was waived breaks nothing.</summary>
    [Fact]
    public void A_change_that_breaks_only_what_was_waived_breaks_nothing()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(Deck);
        session.Waived = new HashSet<string> { "met1.1@0,0,100,1000" };

        Assert.Contains("nothing broken around this change", Done(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [0.1, 1]}""").Text);
    }

    [Fact]
    public void Check_rules_inside_an_area_leaves_out_what_is_elsewhere()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 0.1, 1);
        Rectangle(session, "met1", 50, 0, 50.1, 1);

        session.Deck = DrcDeck.Parse(Deck);

        string said = Done(session, "check_rules", """{"area": [40, -1, 60, 2]}""").Text;

        Assert.Contains("1 violation of 1 rule", said);
        Assert.Contains("(50, 0) to (50.1, 1)", said);
        Assert.DoesNotContain("(0, 0) to (0.1, 1)", said);
    }

    #endregion ***********************************************************************



    #region The manufacturing grid ****************************************************

    ///<summary>sky130's grid, five units, beside its met1 width and spacing - which a curve put on the grid has to pass too.</summary>
    private const string GridDeck = """
        layer met1 68/20
        rule met1.1 width met1 140 "Met1 width"
        rule met1.2 space met1 140 "Met1 spacing"
        rule grid.1 offgrid * 5 "Manufacturing grid"
        """;

    private static AgentSession OnGrid()
    {
        var session = Session();

        session.Deck = DrcDeck.Parse(GridDeck);

        return session;
    }

    ///<summary>Every corner of the shapes a call made, outlined the way the check sees them.</summary>
    private static List<Element.Point> CornersOf(AgentSession session, AgentResult result)
    {
        var corners = new List<Element.Point>();

        foreach (int id in result.Made)
            corners.AddRange(GdsFlattener.OutlineOf(Shape(session, id))!);

        return corners;
    }

    private static void AllOnTheGrid(IEnumerable<Element.Point> corners)
    {
        foreach (var corner in corners)
            Assert.True(corner.X % 5 == 0 && corner.Y % 5 == 0, $"({corner.X}, {corner.Y}) is off the grid");
    }

    ///
    ///What driving gds mcp against sky130's deck found: a stroke of 1 µm lettering is a seventh of a micron, 143
    ///units on the file's own, and "MCP" broke the grid 99 times. On the deck's grid the stroke is 145 and the
    ///start moves onto it - and the answer says both, since the model asked for neither.
    ///
    [Fact]
    public void Lettering_goes_on_the_decks_grid_stroke_and_start()
    {
        var session = OnGrid();

        var result = Done(session, "draw_text", """{"layer": "met1", "text": "MCP", "at": [0.0013, -3], "height": 1}""");

        AllOnTheGrid(CornersOf(session, result));
        Assert.Contains("capitals 1.015 µm tall", result.Text);
        Assert.Contains("Its strokes are 0.145 µm and it starts at (0, -3), on the deck's 0.005 µm grid (grid.1).", result.Text);
        Assert.Contains("nothing broken around this change", result.Text);
    }

    [Fact]
    public void Lettering_already_on_the_grid_is_not_said_to_have_moved()
    {
        var result = Done(OnGrid(), "draw_text", """{"layer": "met1", "text": "HI", "at": [1, 2], "height": 1.4}""");

        Assert.Contains("capitals 1.4 µm tall", result.Text);
        Assert.DoesNotContain("grid", result.Text);
    }

    [Fact]
    public void Lettering_finer_than_the_grid_is_refused_with_the_smallest_there_can_be()
    {
        Assert.Contains("0.035 µm tall", Refused(OnGrid(), "draw_text", """{"layer": "met1", "text": "A", "at": [0, 0], "height": 0.01}"""));
    }

    ///<summary>`*` is the layers the deck declares, as the check reads it - and met2 is not one of them here.</summary>
    [Fact]
    public void A_layer_the_deck_does_not_check_keeps_the_files_own_grid()
    {
        var result = Done(OnGrid(), "draw_text", """{"layer": "met2", "text": "MCP", "at": [0.0013, -3], "height": 1}""");

        Assert.Contains("capitals 1.001 µm tall", result.Text);
        Assert.DoesNotContain("grid.1", result.Text);
    }

    [Fact]
    public void A_circles_corners_go_on_the_decks_grid()
    {
        var session = OnGrid();

        var result = Done(session, "draw_circle", """{"layer": "met1", "center": [10.001, 0], "radius": 1}""");
        var corners = CornersOf(session, result);

        AllOnTheGrid(corners);

        //Half a step each way off the circle, and under a unit more for the unit it was worked out on.
        foreach (var corner in corners)
            Assert.InRange(Math.Sqrt(Math.Pow(corner.X - 10_001, 2) + Math.Pow(corner.Y, 2)), 1000 - 4.5, 1000 + 4.5);

        Assert.Contains("Its corners are on the deck's 0.005 µm grid (grid.1).", result.Text);
        Assert.Contains("nothing broken around this change", result.Text);
    }

    ///
    ///The keyhole that joins a ring's hole to its outside lands where a line out of the hole meets the outside -
    ///worked out, not a corner of either circle. On this ring it lands at (998, 50) with both circles on the grid, so
    ///it goes on the grid after the hole is cut as well as before.
    ///
    [Fact]
    public void A_rings_corners_and_its_keyhole_go_on_the_decks_grid()
    {
        var session = OnGrid();

        var result = Done(session, "draw_ring", """{"layer": "met1", "center": [0, 0], "outer_radius": 1, "inner_radius": 0.5}""");

        AllOnTheGrid(CornersOf(session, result));
        Assert.Contains("Its corners are on the deck's 0.005 µm grid (grid.1).", result.Text);
        Assert.Contains("nothing broken around this change", result.Text);
    }

    ///
    ///A path is checked by its outline, worked out half the width either side of every corner - so a bend's outline
    ///is off the grid whatever its centerline is on, and the path is drawn as that outline instead.
    ///
    [Fact]
    public void A_bent_path_is_drawn_as_its_outline_on_the_decks_grid()
    {
        var session = OnGrid();

        var result = Done(session, "draw_path", """{"layer": "met1", "points": [[20, 0], [24, 0], [24, 4]], "width": 0.5, "bend_radius": 1}""");

        Assert.IsType<BoundaryModel>(Shape(session, Made(result)).Element);
        AllOnTheGrid(CornersOf(session, result));
        Assert.Contains("as a polygon of", result.Text);
        Assert.Contains("along its bends", result.Text);
        Assert.Contains("nothing broken around this change", result.Text);
    }

    [Fact]
    public void A_slanted_path_and_a_round_ended_one_are_drawn_as_outlines_too()
    {
        var session = OnGrid();

        var slanted = Done(session, "draw_path", """{"layer": "met1", "points": [[40, 10], [43, 13]], "width": 0.5}""");
        var rounded = Done(session, "draw_path", """{"layer": "met1", "points": [[50, 10], [54, 10]], "width": 0.5, "ends": "round"}""");

        Assert.IsType<BoundaryModel>(Shape(session, Made(slanted)).Element);
        Assert.IsType<BoundaryModel>(Shape(session, Made(rounded)).Element);
        AllOnTheGrid(CornersOf(session, slanted).Concat(CornersOf(session, rounded)));
        Assert.Contains("along its slanted runs", slanted.Text);
        Assert.Contains("along its round ends", rounded.Text);
        Assert.Contains("nothing broken around this change", rounded.Text);
    }

    ///
    ///A wire that comes back within its own width of itself has an outline that crosses itself, and no one polygon is
    ///that - so it stays a path, which draws it, and the answer says why its outline is off the grid.
    ///
    [Fact]
    public void A_path_that_comes_back_over_itself_stays_a_path()
    {
        var session = OnGrid();

        var result = Done(session, "draw_path", """{"layer": "met1", "points": [[0, 0], [1, 0.013], [0.003, 0.03]], "width": 0.05}""");

        Assert.IsType<PathModel>(Shape(session, Made(result)).Element);
        Assert.Contains("It comes back over itself, so it stays a path", result.Text);
    }

    ///<summary>Square to the axes, on numbers on the grid, a path's outline is on it too - and it stays a path.</summary>
    [Fact]
    public void A_square_path_on_the_grid_stays_a_path()
    {
        var session = OnGrid();

        var result = Done(session, "draw_path", """{"layer": "met1", "points": [[20, 10], [24, 10], [24, 14]], "width": 0.5}""");

        Assert.IsType<PathModel>(Shape(session, Made(result)).Element);
        Assert.DoesNotContain("polygon", result.Text);
    }

    ///
    ///A width of an odd number of steps puts a square path's sides off the grid - and that is the model's own
    ///number, so the path is drawn as asked and the check says so, as it would of a rectangle's corners.
    ///
    [Fact]
    public void A_square_path_off_the_grid_by_its_own_numbers_is_drawn_as_asked()
    {
        var session = OnGrid();

        var result = Done(session, "draw_path", """{"layer": "met1", "points": [[20, 10], [24, 10]], "width": 0.145}""");

        Assert.IsType<PathModel>(Shape(session, Made(result)).Element);
        Assert.Contains("grid.1 (off the 0.005 µm grid", result.Text);
    }

    ///<summary>And the corners one is told to round are worked out, so they go on the grid too.</summary>
    [Fact]
    public void Rounded_corners_go_on_the_decks_grid()
    {
        var session = OnGrid();
        int box = Rectangle(session, "met1", 30, 0, 32, 1);

        string said = Done(session, "corners", $$"""{"shapes": [{{box}}], "style": "round", "size": 0.3}""").Text;

        AllOnTheGrid(GdsFlattener.OutlineOf(Shape(session, box))!);
        Assert.Contains("The new corners are on the deck's 0.005 µm grid (grid.1).", said);
        Assert.Contains("nothing broken around this change", said);
    }

    ///
    ///Where a boolean crosses edges, the corner is worked out - a circle cut by a rectangle put two off the grid on
    ///Mosfet - and a grow moves every edge, which put 52 of a grown circle's off it. Both go on the grid now.
    ///
    [Fact]
    public void A_booleans_corners_go_on_the_decks_grid()
    {
        var session = OnGrid();

        int circle = Made(Done(session, "draw_circle", """{"layer": "met1", "center": [0, 0], "radius": 1}"""));
        int box = Rectangle(session, "met1", -2, 0.3, 2, 2);

        var result = Done(session, "boolean", $$"""{"shapes": [{{circle}}, {{box}}], "operation": "subtract"}""");

        AllOnTheGrid(CornersOf(session, result));
        Assert.Contains("The corners it worked out are on the deck's 0.005 µm grid (grid.1).", result.Text);
        Assert.Contains("nothing broken around this change", result.Text);
    }

    [Fact]
    public void A_grows_corners_go_on_the_decks_grid()
    {
        var session = OnGrid();

        int circle = Made(Done(session, "draw_circle", """{"layer": "met1", "center": [0, 0], "radius": 1}"""));

        var result = Done(session, "grow", $$"""{"shapes": [{{circle}}], "by": 0.1}""");

        AllOnTheGrid(CornersOf(session, result));
        Assert.Contains("The corners it worked out are on the deck's 0.005 µm grid (grid.1).", result.Text);
        Assert.Contains("nothing broken around this change", result.Text);
    }

    ///<summary>The grid is the deck's: with none loaded the same cut leaves its corners where the edges cross, and says nothing of a grid.</summary>
    [Fact]
    public void Without_a_deck_a_booleans_corners_stay_where_the_edges_cross()
    {
        var session = Session();

        int circle = Made(Done(session, "draw_circle", """{"layer": "met1", "center": [0, 0], "radius": 1}"""));
        int box = Rectangle(session, "met1", -2, 0.3, 2, 2);

        var result = Done(session, "boolean", $$"""{"shapes": [{{circle}}, {{box}}], "operation": "subtract"}""");

        Assert.Contains(CornersOf(session, result), corner => corner.X % 5 != 0 || corner.Y % 5 != 0);
        Assert.DoesNotContain("grid", result.Text);
    }

    ///<summary>Rectangles on the grid cross only on it, so nothing moves and nothing is said.</summary>
    [Fact]
    public void A_union_of_rectangles_on_the_grid_says_nothing_of_it()
    {
        var session = OnGrid();

        int a = Rectangle(session, "met1", 0, 0, 2, 1);
        int b = Rectangle(session, "met1", 1, 0.5, 3, 2);

        var result = Done(session, "boolean", $$"""{"shapes": [{{a}}, {{b}}], "operation": "union"}""");

        Assert.DoesNotContain("grid", result.Text);
    }

    ///<summary>A piece narrower than a step all along is a line on the grid: left out of the answer, and the answer says so.</summary>
    [Fact]
    public void A_piece_narrower_than_a_step_is_left_out_and_said()
    {
        var session = OnGrid();

        int sliver = Made(Done(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [1, 0.002], [1, 0]]}"""));
        int box = Rectangle(session, "met1", 5, 0, 6, 1);

        var result = Done(session, "boolean", $$"""{"shapes": [{{sliver}}, {{box}}], "operation": "union"}""");

        Assert.Equal(new Bounds(5000, 0, 6000, 1000), Box(Shape(session, Made(result))));
        Assert.Contains("One piece narrower than a step of it was left out.", result.Text);
    }

    [Fact]
    public void A_boolean_that_leaves_nothing_wider_than_a_step_is_refused()
    {
        var session = OnGrid();

        int one = Made(Done(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [1, 0.002], [1, 0]]}"""));
        int two = Made(Done(session, "draw_polygon", """{"layer": "met1", "points": [[0, 1], [1, 1.002], [1, 1]]}"""));

        Assert.Contains("nowhere wider than a step of the deck's 0.005 µm grid (grid.1)", Refused(session, "boolean", $$"""{"shapes": [{{one}}, {{two}}], "operation": "union"}"""));
    }

    ///<summary>Shrunk to under a step tall, a strip 10 nm tall is a line on the grid - nothing left, which is a refusal as any shrink to nothing is.</summary>
    [Fact]
    public void A_shrink_that_leaves_nothing_wider_than_a_step_is_refused()
    {
        var session = OnGrid();
        int strip = Rectangle(session, "met1", 0, 0, 1, 0.01);

        Assert.Contains("wider than a step of the deck's 0.005 µm grid (grid.1)", Refused(session, "grow", $$"""{"shapes": [{{strip}}], "by": -0.004}"""));
    }

    ///
    ///A fold - a corner within a step of an edge it is not on, landing across it - spoils a piece that is part of the
    ///answer, so the whole call is refused and says where. The outline is GridTests' neck: (2, -2) lands on (0, 0), and
    ///the edge that makes crosses the one down x = 20. A union hands it back whole, where a clipper takes a spike a few
    ///units long off an outline before the grid ever sees it.
    ///
    [Fact]
    public void A_boolean_the_grid_would_fold_is_refused_and_says_where()
    {
        var session = OnGrid();

        int folding = Made(Done(session, "draw_polygon", """{"layer": "met1", "points": [[0, 0], [0.002, -0.002], [0.1, 0.005], [0.1, 0.1], [0.02, 0.1], [0.02, 0]]}"""));
        int box = Rectangle(session, "met1", 5, 0, 6, 1);

        string said = Refused(session, "boolean", $$"""{"shapes": [{{folding}}, {{box}}], "operation": "union"}""");

        Assert.Contains("Put on the deck's 0.005 µm grid (grid.1), the union would cross itself: the edge from (0.0", said);
        Assert.Contains("Nothing was changed.", said);
    }

    #endregion ***********************************************************************



    #region Editing *******************************************************************

    [Fact]
    public void Moving_shifts_every_coordinate_by_the_distance()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        var result = Done(session, "move", $$"""{"shapes": [{{square}}], "dx": 1, "dy": -0.5}""");

        Assert.Equal(new Bounds(1000, -500, 2000, 500), Box(Shape(session, square)));
        Assert.Contains("by (1, -0.5) µm", result.Text);
    }

    [Fact]
    public void A_move_by_nothing_is_refused()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Contains("nothing would move", Refused(session, "move", $$"""{"shapes": [{{square}}], "dx": 0.0001, "dy": 0}"""));
    }

    [Fact]
    public void A_copy_is_a_new_shape_beside_the_original()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        int copied = Made(Done(session, "copy", $$"""{"shapes": [{{square}}], "dx": 0, "dy": 2}"""));

        Assert.NotEqual(square, copied);
        Assert.Equal(new Bounds(0, 0, 1000, 1000), Box(Shape(session, square)));
        Assert.Equal(new Bounds(0, 2000, 1000, 3000), Box(Shape(session, copied)));
    }

    [Fact]
    public void An_array_of_shapes_copies_them_into_each_place()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        var result = Done(session, "array", $$"""{"shapes": [{{square}}], "columns": 3, "rows": 2, "spacing": [2, 5]}""");

        Assert.Equal(5, result.Made.Count);
        Assert.Contains(new Bounds(4000, 5000, 5000, 6000), result.Made.Select(id => Box(Shape(session, id))));

        //The rectangle, and the array as one step however many copies it made.
        Assert.Equal(2, session.History.Count);
    }

    [Fact]
    public void An_array_of_one_placement_is_one_array_record()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");

        int placed = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [1, 1]}"""));
        int arrayed = Made(Done(session, "array", $$"""{"shapes": [{{placed}}], "columns": 4, "rows": 2, "spacing": [10, 20]}""")) ;

        var aref = Assert.IsType<ArefModel>(Shape(session, arrayed).Element);

        Assert.Equal(new short[] { 4, 2 }, ((Int2Data)aref.COLROW.Data!).Values);
        Assert.Equal(new[] { 1000, 1000, 41_000, 1000, 1000, 41_000 }, Xy(Shape(session, arrayed)));
        Assert.Single(Cell(session).Elements);
    }

    [Fact]
    public void An_array_of_too_many_copies_points_at_a_cell_instead()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Contains("make_cell", Refused(session, "array", $$"""{"shapes": [{{square}}], "columns": 200, "rows": 200, "spacing": [2, 2]}"""));
    }

    [Fact]
    public void A_quarter_turn_about_the_middle_stands_a_rectangle_on_its_end()
    {
        var session = Session();
        int bar = Rectangle(session, "met1", 0, 0, 4, 2);

        Done(session, "turn", $$"""{"shapes": [{{bar}}], "turn": "90"}""");

        Assert.Equal(new Bounds(1000, -1000, 3000, 3000), Box(Shape(session, bar)));
    }

    [Fact]
    public void Turns_and_mirrors_about_a_given_point()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 1, 0, 2, 1);

        Done(session, "turn", $$"""{"shapes": [{{square}}], "turn": "180", "about": [0, 0]}""");

        Assert.Equal(new Bounds(-2000, -1000, -1000, 0), Box(Shape(session, square)));

        Done(session, "turn", $$"""{"shapes": [{{square}}], "turn": "flip_x", "about": [0, 0]}""");

        Assert.Equal(new Bounds(1000, -1000, 2000, 0), Box(Shape(session, square)));
    }

    ///
    ///A placement turns the cell it places - its transform composed with the turn, not its corners moved - so
    ///what it draws comes out exactly where the same turn of the drawn shapes would put them.
    ///
    [Fact]
    public void A_turned_placement_draws_where_its_turned_shapes_would_be()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");
        Rectangle(session, "met1", 0, 0, 4, 1);
        Done(session, "open_cell", """{"name": "TOP"}""");

        int placed = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [10, 0]}"""));

        var result = Done(session, "turn", $$"""{"shapes": [{{placed}}], "turn": "90", "about": [10, 0]}""");

        Assert.Contains($"#{placed} is now #", result.Text);

        var drawn = Measure.BoundsOf(GdsFlattener.Flatten(session.Gds, "TOP"));

        Assert.Equal(new Bounds(9000, 0, 10_000, 4000), drawn);
    }

    [Fact]
    public void A_turned_array_turns_its_steps_with_it()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");
        Rectangle(session, "met1", 0, 0, 1, 1);
        Done(session, "open_cell", """{"name": "TOP"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0], "columns": 3, "rows": 1, "spacing": [2, 0]}""");

        int arrayed = session.IdOf(Cell(session).Elements[0]);

        Done(session, "turn", $$"""{"shapes": [{{arrayed}}], "turn": "90", "about": [0, 0]}""");

        //Three squares along x, turned a quarter about the origin, are three squares up y.
        Assert.Equal(new Bounds(-1000, 0, 0, 5000), Measure.BoundsOf(GdsFlattener.Flatten(session.Gds, "TOP")));
    }

    [Fact]
    public void Shapes_line_up_on_an_edge_and_spread_between_the_outer_two()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int b = Rectangle(session, "met1", 3, 2, 4, 3);
        int c = Rectangle(session, "met1", 10, 5, 11, 6);

        Done(session, "align", $$"""{"shapes": [{{a}}, {{b}}, {{c}}], "to": "bottom"}""");

        Assert.All(new[] { a, b, c }, id => Assert.Equal(0, Box(Shape(session, id)).Bottom));

        Done(session, "align", $$"""{"shapes": [{{a}}, {{b}}, {{c}}], "to": "spread_x"}""");

        Assert.Equal(5000, Box(Shape(session, b)).Left);
    }

    [Fact]
    public void Shapes_already_in_line_are_left_alone_with_no_step()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int b = Rectangle(session, "met1", 0, 5, 2, 6);

        var result = Done(session, "align", $$"""{"shapes": [{{a}}, {{b}}], "to": "left"}""");

        Assert.Contains("already", result.Text);
        Assert.Null(result.Edit);
        Assert.Equal(2, session.History.Count);
    }

    [Fact]
    public void Deleting_takes_the_shapes_out()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int b = Rectangle(session, "met1", 2, 0, 3, 1);

        Done(session, "delete", $$"""{"shapes": [{{a}}, {{b}}]}""");

        Assert.Empty(Cell(session).Elements);
    }

    [Fact]
    public void Changing_layer_keeps_the_shape_where_it_is()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "change_layer", $$"""{"shapes": [{{square}}], "layer": "met2"}""");

        Assert.Equal(Met2, LayerOf(Shape(session, square)));
        Assert.Equal(new Bounds(0, 0, 1000, 1000), Box(Shape(session, square)));
    }

    [Fact]
    public void A_placement_has_no_layer_to_change()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");

        int placed = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0]}"""));

        Assert.Contains("no layer of its own", Refused(session, "change_layer", $$"""{"shapes": [{{placed}}], "layer": "met2"}"""));
    }

    [Fact]
    public void A_union_replaces_the_shapes_with_their_merged_outline()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 2, 1);
        int b = Rectangle(session, "met1", 1, 0, 3, 1);

        int merged = Made(Done(session, "boolean", $$"""{"shapes": [{{a}}, {{b}}], "operation": "union"}"""));

        Assert.Equal(new Bounds(0, 0, 3000, 1000), Box(Shape(session, merged)));
        Assert.Single(Cell(session).Elements);
        Assert.Null(session.ShapeNumbered(a));
    }

    [Fact]
    public void A_subtraction_cuts_the_rest_out_of_the_first_and_can_keep_them()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 3, 1);
        int b = Rectangle(session, "met1", 2, 0, 4, 1);

        var result = Done(session, "boolean", $$"""{"shapes": [{{a}}, {{b}}], "operation": "subtract", "keep": true, "layer": "met2"}""");
        var cut = Shape(session, Made(result));

        Assert.Equal(new Bounds(0, 0, 2000, 1000), Box(cut));
        Assert.Equal(Met2, LayerOf(cut));
        Assert.Equal(3, Cell(session).Elements.Count);
    }

    [Fact]
    public void An_empty_intersection_is_refused_rather_than_deleting_the_shapes()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int b = Rectangle(session, "met1", 5, 0, 6, 1);

        Assert.Contains("no area in common", Refused(session, "boolean", $$"""{"shapes": [{{a}}, {{b}}], "operation": "intersect"}"""));
    }

    [Fact]
    public void A_label_has_no_area_to_combine()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int label = Made(Done(session, "add_label", """{"layer": "met1", "text": "A", "at": [0, 0]}"""));

        Assert.Contains("label with no area", Refused(session, "boolean", $$"""{"shapes": [{{a}}, {{label}}], "operation": "union"}"""));
    }

    [Fact]
    public void Growing_moves_every_edge_and_keeps_each_layer_apart()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int b = Rectangle(session, "met2", 0, 0, 1, 1);

        var result = Done(session, "grow", $$"""{"shapes": [{{a}}, {{b}}], "by": 0.1}""");

        Assert.Equal(2, result.Made.Count);
        Assert.All(result.Made, id => Assert.Equal(new Bounds(-100, -100, 1100, 1100), Box(Shape(session, id))));
        Assert.Equal(new LayerKey?[] { Met1, Met2 }, result.Made.Select(id => LayerOf(Shape(session, id))).ToArray());
    }

    [Fact]
    public void Shrinking_a_shape_away_is_refused()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Contains("leaves nothing", Refused(session, "grow", $$"""{"shapes": [{{square}}], "by": -0.6}"""));
    }

    [Fact]
    public void Rounded_corners_keep_a_rectangle_its_number_and_take_area_off_it()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 10, 10);

        Done(session, "corners", $$"""{"shapes": [{{square}}], "style": "round", "size": 1}""");

        var outline = GdsFlattener.OutlineOf(Shape(session, square))!;
        double area = Math.Abs(Measure.AreaOf(outline));

        //A quarter circle cut into eight straight pieces falls short of the arc by about 5,000 square units.
        Assert.True(outline.Count > 20);
        Assert.InRange(area, (100e6) - (4 * 1e6) + (Math.PI * 1e6) - 40_000, (100e6) - (4 * 1e6) + (Math.PI * 1e6));
    }

    [Fact]
    public void A_cut_takes_each_corner_off_in_a_line()
    {
        var session = Session();
        int square = Rectangle(session, "met1", 0, 0, 10, 10);

        Done(session, "corners", $$"""{"shapes": [{{square}}], "style": "cut", "size": 1}""");

        Assert.Equal(100e6 - (4 * 0.5e6), Math.Abs(Measure.AreaOf(GdsFlattener.OutlineOf(Shape(session, square))!)));
    }

    [Fact]
    public void Rounding_a_path_makes_it_a_polygon_with_a_new_number()
    {
        var session = Session();
        int wire = Made(Done(session, "draw_path", """{"layer": "met1", "points": [[0, 0], [10, 0], [10, 10]], "width": 2}"""));

        var result = Done(session, "corners", $$"""{"shapes": [{{wire}}], "style": "round", "size": 0.5}""");

        Assert.IsType<BoundaryModel>(Shape(session, Made(result)).Element);
        Assert.Null(session.ShapeNumbered(wire));
    }

    #endregion ***********************************************************************



    #region Cells *********************************************************************

    [Fact]
    public void An_empty_cell_is_made_to_be_opened_and_drawn_in()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "PAD"}""");

        var opened = Done(session, "open_cell", """{"name": "pad"}""");

        Assert.Contains("Now working in PAD", opened.Text);

        Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Single(Cell(session, "PAD").Elements);
        Assert.Empty(Cell(session).Elements);
    }

    [Fact]
    public void A_cell_name_already_taken_is_refused()
    {
        Assert.Contains("already a cell called TOP", Refused(Session(), "make_cell", """{"name": "TOP"}"""));
    }

    ///
    ///Shapes moved into a cell of their own and placed where they were: the layout draws exactly what it drew
    ///before, and the cell holds them in their own coordinates.
    ///
    [Fact]
    public void Shapes_made_into_a_cell_draw_where_they_were()
    {
        var session = Session();

        int a = Rectangle(session, "met1", 0, 0, 1, 1);
        int b = Rectangle(session, "met2", 5, 5, 6, 6);
        int stays = Rectangle(session, "met1", 20, 0, 21, 1);

        var before = GdsFlattener.Flatten(session.Gds).Elements.Select(element => Bounds.Of(element.Points)).OrderBy(box => box.Left).ToList();

        var result = Done(session, "make_cell", $$"""{"name": "PAIR", "shapes": [{{a}}, {{b}}]}""");

        var after = GdsFlattener.Flatten(session.Gds).Elements.Select(element => Bounds.Of(element.Points)).OrderBy(box => box.Left).ToList();

        Assert.Equal(before, after);
        Assert.Equal(2, Cell(session, "PAIR").Elements.Count);
        Assert.Equal(2, Cell(session).Elements.Count);
        Assert.IsType<SrefModel>(Shape(session, Made(result)).Element);
        Assert.NotNull(session.ShapeNumbered(stays));
    }

    [Fact]
    public void A_cell_is_placed_turned_mirrored_and_magnified()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "open_cell", """{"name": "LEAF"}""");
        Rectangle(session, "met1", 0, 0, 2, 1);
        Done(session, "open_cell", """{"name": "TOP"}""");

        var result = Done(session, "place_cell", """{"cell": "LEAF", "at": [10, 10], "turn": "90", "mirror": true, "magnification": 2}""");

        (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(Shape(session, Made(result)));

        Assert.True(mirrored);
        Assert.Equal(90, angle);
        Assert.Equal(2, magnification);

        //Mirrored about x, doubled, then turned a quarter: (0..4, -2..0) turned is (0..2, 0..4), about (10, 10).
        Assert.Equal(new Bounds(10_000, 10_000, 12_000, 14_000), Measure.BoundsOf(GdsFlattener.Flatten(session.Gds, "TOP")));
    }

    [Fact]
    public void Placing_a_cell_in_itself_or_in_what_it_holds_is_refused()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0]}""");

        Assert.Contains("inside itself", Refused(session, "place_cell", """{"cell": "TOP", "at": [0, 0]}"""));

        Done(session, "open_cell", """{"name": "LEAF"}""");

        Assert.Contains("TOP already contains LEAF", Refused(session, "place_cell", """{"cell": "TOP", "at": [0, 0]}"""));
    }

    [Fact]
    public void A_grid_of_placements_is_one_array_record()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");

        var result = Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0], "columns": 2, "rows": 3, "spacing": [5, 7]}""");

        var aref = Assert.IsType<ArefModel>(Shape(session, Made(result)).Element);

        Assert.Equal(new short[] { 2, 3 }, ((Int2Data)aref.COLROW.Data!).Values);
        Assert.Contains("grid of 2 columns by 3 rows", result.Text);
    }

    [Fact]
    public void A_cell_that_is_not_there_is_refused_with_the_ones_that_are()
    {
        string said = Refused(Session(), "open_cell", """{"name": "NOPE"}""");

        Assert.Contains("no cell called \"NOPE\"", said);
        Assert.Contains("TOP", said);
    }

    [Fact]
    public void With_two_top_cells_a_drawing_tool_asks_which()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "OTHER"}""");

        session.Cell = null;

        Assert.Contains("2 top cells", Refused(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [1, 1]}"""));
    }

    ///
    ///**A flattened placement draws what it drew** - the case the app's own Flatten got wrong before both went
    ///through the library: a placement inside a turned, mirrored and magnified one came out unturned, and a
    ///path inside it came out at its old width.
    ///
    [Fact]
    public void Flattening_a_turned_placement_draws_what_it_drew_in_one_step()
    {
        var session = Session();

        Leaf(session);

        Done(session, "make_cell", """{"name": "MID"}""");
        Done(session, "open_cell", """{"name": "MID"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [1, 2]}""");
        Done(session, "draw_path", """{"layer": "met2", "width": 0.2, "points": [[0, 5], [3, 5]]}""");
        Done(session, "open_cell", """{"name": "TOP"}""");

        int placed = Made(Done(session, "place_cell", """{"cell": "MID", "at": [10, 0], "turn": "90", "mirror": true, "magnification": 2}"""));

        var before = Picture(session);
        int steps = session.History.Count;

        var result = Done(session, "flatten", $$"""{"shapes": [{{placed}}]}""");

        Assert.Equal(before, Picture(session));
        Assert.Equal(steps + 1, session.History.Count);
        Assert.Equal(2, result.Made.Count);
        Assert.Contains("MID was placed nowhere else", result.Text);
        Assert.Null(Hierarchy.Named(session.Gds, "MID"));

        //The placement inside is still a placement, turned with the one it was in; the path is twice as wide.
        var leaf = Assert.Single(Cell(session).Elements, element => element.Element is SrefModel);

        Assert.Equal((true, 90.0, 2.0), Hierarchy.TransformOf(leaf));

        var path = Assert.Single(Cell(session).Elements, element => element.Element is PathModel);

        Assert.Equal(400, ((Int4Data)((PathModel)path.Element).WIDTH!.Data!).Value);
    }

    [Fact]
    public void Flattening_an_array_writes_every_place_in_it()
    {
        var session = Session();

        Leaf(session);

        int grid = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0], "columns": 3, "rows": 2, "spacing": [5, 3]}"""));
        var before = Picture(session);

        var result = Done(session, "flatten", $$"""{"shapes": [{{grid}}]}""");

        Assert.Equal(before, Picture(session));
        Assert.Equal(6, result.Made.Count);
        Assert.All(Cell(session).Elements, element => Assert.IsType<BoundaryModel>(element.Element));
    }

    [Fact]
    public void A_cell_placed_somewhere_else_as_well_stays_in_the_library()
    {
        var session = Session();

        Leaf(session);

        int first = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0]}"""));

        Done(session, "place_cell", """{"cell": "LEAF", "at": [10, 0]}""");

        var result = Done(session, "flatten", $$"""{"shapes": [{{first}}]}""");

        Assert.NotNull(Hierarchy.Named(session.Gds, "LEAF"));
        Assert.DoesNotContain("gone from the library", result.Text);
    }

    [Fact]
    public void Only_a_placement_is_flattened_and_not_one_too_big_to_take_apart()
    {
        var session = Session();

        int square = Rectangle(session, "met1", 0, 0, 1, 1);

        Assert.Contains("not a placement", Refused(session, "flatten", $$"""{"shapes": [{{square}}]}"""));

        Leaf(session);

        int grid = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0], "columns": 101, "rows": 100, "spacing": [5, 2]}"""));

        Assert.Contains("stops at 10000", Refused(session, "flatten", $$"""{"shapes": [{{grid}}]}"""));
    }

    ///<summary>Undo puts back the very placement, so the number it went by works again.</summary>
    [Fact]
    public void Undoing_a_flatten_puts_the_placement_back_with_its_number()
    {
        var session = Session();

        Leaf(session);

        int placed = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [3, 0], "turn": "90"}"""));
        byte[] before = session.Gds.Serialize();

        Done(session, "flatten", $$"""{"shapes": [{{placed}}]}""");

        Assert.Contains("not in the layout any more", Refused(session, "move", $$"""{"shapes": [{{placed}}], "dx": 1, "dy": 0}"""));

        Done(session, "undo_last_change", "{}");

        Assert.Equal(before, session.Gds.Serialize());
        Assert.NotNull(session.ShapeNumbered(placed));
    }

    ///<summary>A cell's name is written into every placement of it too, and the two change together.</summary>
    [Fact]
    public void A_renamed_cell_takes_its_placements_with_it()
    {
        var session = Session();

        Leaf(session);

        int placed = Made(Done(session, "place_cell", """{"cell": "LEAF", "at": [3, 0]}"""));
        var before = Picture(session);

        var result = Done(session, "rename_cell", """{"cell": "leaf", "name": "PIN"}""");

        Assert.Contains("Renamed LEAF to PIN. Its placement names it by the new name too.", result.Text);
        Assert.Equal("PIN", Hierarchy.PlacedBy(Shape(session, placed)));
        Assert.Null(Hierarchy.Named(session.Gds, "LEAF"));
        Assert.Equal(before, Picture(session));
    }

    [Fact]
    public void A_rename_to_a_name_in_use_or_to_nothing_is_refused_and_to_its_own_does_nothing()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");

        Assert.Contains("already a cell called TOP", Refused(session, "rename_cell", """{"cell": "LEAF", "name": "TOP"}"""));
        Assert.Contains("printable ASCII", Refused(session, "rename_cell", """{"cell": "LEAF", "name": "µ"}"""));
        Assert.Contains("no cell called \"NOPE\"", Refused(session, "rename_cell", """{"cell": "NOPE", "name": "X"}"""));

        int steps = session.History.Count;

        Assert.Contains("called that already", Done(session, "rename_cell", """{"cell": "LEAF", "name": "LEAF"}""").Text);
        Assert.Equal(steps, session.History.Count);
    }

    ///
    ///**The cell being worked in is followed through a rename**, and through the undo of one, so the session
    ///never points at a name nothing has.
    ///
    [Fact]
    public void Renaming_the_cell_being_worked_in_keeps_working_in_it()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "rename_cell", """{"cell": "TOP", "name": "CHIP"}""");

        Assert.Equal("CHIP", session.Cell);

        Rectangle(session, "met1", 2, 0, 3, 1);

        Assert.Equal(2, Cell(session, "CHIP").Elements.Count);

        Done(session, "undo_last_change", "{}");
        Done(session, "undo_last_change", "{}");

        Assert.Equal("TOP", session.Cell);

        Rectangle(session, "met1", 4, 0, 5, 1);

        Assert.Equal(2, Cell(session).Elements.Count);
    }

    [Fact]
    public void A_placed_cell_is_not_deleted_and_the_refusal_says_where_it_is_placed()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "LEAF"}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [0, 0]}""");
        Done(session, "place_cell", """{"cell": "LEAF", "at": [5, 0]}""");

        string said = Refused(session, "delete_cell", """{"cell": "LEAF"}""");

        Assert.Contains("placed in TOP (2 times)", said);
        Assert.Contains("Flatten or delete those placements first", said);
    }

    [Fact]
    public void A_cell_nothing_places_is_deleted_and_comes_back_with_undo()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "SPARE"}""");
        Done(session, "open_cell", """{"name": "SPARE"}""");

        int inside = Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "open_cell", """{"name": "TOP"}""");

        byte[] before = session.Gds.Serialize();

        var result = Done(session, "delete_cell", """{"cell": "SPARE"}""");

        Assert.Contains("Deleted SPARE from the library, with its 1 shape.", result.Text);
        Assert.Null(Hierarchy.Named(session.Gds, "SPARE"));
        Assert.Null(session.ShapeNumbered(inside));

        Done(session, "undo_last_change", "{}");

        Assert.Equal(before, session.Gds.Serialize());
        Assert.NotNull(session.ShapeNumbered(inside));
    }

    [Fact]
    public void Deleting_the_cell_being_worked_in_says_to_choose_another()
    {
        var session = Session();

        Done(session, "make_cell", """{"name": "SPARE"}""");
        Done(session, "open_cell", """{"name": "SPARE"}""");

        Assert.Contains("choose another with open_cell", Done(session, "delete_cell", """{"cell": "SPARE"}""").Text);
        Assert.Contains("SPARE, is not in the library any more", Refused(session, "draw_rectangle", """{"layer": "met1", "from": [0, 0], "to": [1, 1]}"""));
    }

    #endregion ***********************************************************************



    #region Taking back the assistant's own changes ***********************************

    [Fact]
    public void The_assistant_undoes_its_own_changes_last_first()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 1, 1);
        Rectangle(session, "met1", 2, 0, 3, 1);

        var result = Done(session, "undo_last_change", "{}");

        Assert.True(result.Changed);
        Assert.Single(Cell(session).Elements);

        Done(session, "undo_last_change", "{}");

        Assert.Empty(Cell(session).Elements);
        Assert.Contains("no change of mine left", Refused(session, "undo_last_change", "{}"));
    }

    [Fact]
    public void The_assistant_will_not_undo_past_somebody_elses_step()
    {
        var session = Session();

        Rectangle(session, "met1", 0, 0, 1, 1);

        //The person draws something in the app, on the same history.
        var top = Cell(session);

        session.History.Do(new AddElement(session.Gds, top, Met2, Shapes.Between(5000, 0, 6000, 1000)));

        Assert.Contains("changed since my last change", Refused(session, "undo_last_change", "{}"));
    }

    [Fact]
    public void A_change_of_its_own_on_top_of_somebody_elses_is_undone_and_no_further()
    {
        var session = Session();

        session.History.Do(new AddElement(session.Gds, Cell(session), Met2, Shapes.Between(5000, 0, 6000, 1000)));

        Rectangle(session, "met1", 0, 0, 1, 1);

        Done(session, "undo_last_change", "{}");

        Assert.Single(Cell(session).Elements);
        Assert.Contains("no change of mine left", Refused(session, "undo_last_change", "{}"));
    }

    #endregion ***********************************************************************
}
