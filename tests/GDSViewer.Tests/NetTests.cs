using GdsII;

namespace GDSViewer.Tests;

///
///Tracing what a piece of metal is attached to.
///
///**Built by hand rather than taken from a fixture**, because the thing under test is a rule about layers and
///every case that matters is a specific arrangement of them: two metals crossing with no via, the same two
///with one, a wire that abuts rather than overlaps. A real file has all of those tangled together and none of
///them isolated.
///
///The case that decides whether the whole thing is worth having is <see cref="Two_conductors_crossing_are_not_connected"/>.
///Metal1 and metal2 cross each other all over a real chip without meeting, and a tool that joined them would
///report almost any layout as one enormous net - which is why the naive "everything that touches" model was
///not built.
///
public class NetTests
{
    #region A layout to trace *******************************************************

    private const short Metal1 = 68;
    private const short Metal2 = 69;
    private const short Via1 = 67;

    private static readonly Dictionary<short, LayerRole> Roles = new Dictionary<short, LayerRole>
    {
        { Metal1, LayerRole.Conductor },
        { Metal2, LayerRole.Conductor },
        { Via1, LayerRole.Via }
    };

    ///<summary>A rectangle on a layer, added to a layout with the role that layer is being given.</summary>
    private static int Add(FlattenedLayout layout, short number, int left, int bottom, int right, int top, short dataType = 0)
    {
        var key = new LayerKey(number, dataType);

        var layer = new Layer(key, "#ffffff");

        if (Roles.TryGetValue(number, out var role))
            layer.Role = role;

        var element = new Element { Layer = layer };

        element.Points.Add(new Element.Point(left, bottom));
        element.Points.Add(new Element.Point(right, bottom));
        element.Points.Add(new Element.Point(right, top));
        element.Points.Add(new Element.Point(left, top));
        element.Points.Add(new Element.Point(left, bottom));

        layout.Elements.Add(element);

        return layout.Elements.Count - 1;
    }

    private static FlattenedLayout Empty()
    {
        return new FlattenedLayout();
    }

    #endregion **********************************************************************



    #region The rule ****************************************************************

    [Fact]
    public void One_shape_alone_is_its_own_net()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 100, 20);

        Assert.Equal(new[] { wire }, Nets.Reaching(layout, wire).Order());
    }

    [Fact]
    public void Two_overlapping_shapes_on_one_layer_are_one_net()
    {
        var layout = Empty();

        int left = Add(layout, Metal1, 0, 0, 100, 20);
        int right = Add(layout, Metal1, 80, 0, 200, 20);

        Assert.Equal(new[] { left, right }, Nets.Reaching(layout, left).Order());
    }

    ///
    ///**Shapes that share an edge are one wire.**
    ///
    ///Two rectangles meeting exactly intersect in nothing at all as far as a polygon clipper is concerned -
    ///the overlap has zero area - and yet they are plainly the same piece of metal. It is the commonest way a
    ///router lays a wire down, so a tool that missed it would break a net at every corner.
    ///
    [Fact]
    public void Shapes_that_only_abut_are_one_net()
    {
        var layout = Empty();

        int left = Add(layout, Metal1, 0, 0, 100, 20);
        int right = Add(layout, Metal1, 100, 0, 200, 20);

        Assert.Equal(new[] { left, right }, Nets.Reaching(layout, left).Order());
    }

    [Fact]
    public void Shapes_with_a_gap_between_them_are_two_nets()
    {
        var layout = Empty();

        int left = Add(layout, Metal1, 0, 0, 100, 20);

        Add(layout, Metal1, 150, 0, 250, 20);

        Assert.Equal(new[] { left }, Nets.Reaching(layout, left).Order());
    }

    ///
    ///**The one that decides the design.** Metal1 and metal2 cross each other constantly on a real chip
    ///without meeting. Joining them would make almost every layout one net, which is why the roles exist.
    ///
    [Fact]
    public void Two_conductors_crossing_are_not_connected()
    {
        var layout = Empty();

        int lower = Add(layout, Metal1, 0, 0, 200, 20);

        Add(layout, Metal2, 90, -50, 110, 70);

        Assert.Equal(new[] { lower }, Nets.Reaching(layout, lower).Order());
    }

    ///<summary>And with a via over the crossing, they are one - which is the only way two metals ever meet.</summary>
    [Fact]
    public void A_via_joins_two_conductors()
    {
        var layout = Empty();

        int lower = Add(layout, Metal1, 0, 0, 200, 20);
        int upper = Add(layout, Metal2, 90, -50, 110, 70);
        int via = Add(layout, Via1, 95, 5, 105, 15);

        Assert.Equal(new[] { lower, upper, via }, Nets.Reaching(layout, lower).Order());
    }

    ///<summary>A via reaching only one of them joins only that one, which is a via that has not landed.</summary>
    [Fact]
    public void A_via_that_reaches_only_one_conductor_joins_only_that_one()
    {
        var layout = Empty();

        int lower = Add(layout, Metal1, 0, 0, 200, 20);

        //Well clear of the metal2 above it.
        int via = Add(layout, Via1, 10, 5, 20, 15);

        Add(layout, Metal2, 90, -50, 110, 70);

        Assert.Equal(new[] { lower, via }, Nets.Reaching(layout, lower).Order());
    }

    ///
    ///**A net runs as far as it runs**, through as many hops as it takes - which is the whole reason this is
    ///a walk rather than a lookup. Metal1 to a via to metal2 to another via to metal1 again, none of whose
    ///ends touch each other.
    ///
    [Fact]
    public void A_net_is_followed_through_every_hop()
    {
        var layout = Empty();

        int start = Add(layout, Metal1, 0, 0, 100, 20);
        int up = Add(layout, Via1, 90, 5, 100, 15);
        int across = Add(layout, Metal2, 90, 5, 400, 15);
        int down = Add(layout, Via1, 390, 5, 400, 15);
        int end = Add(layout, Metal1, 390, 0, 500, 20);

        //And something on the same layer as the far end, not touching any of it.
        Add(layout, Metal1, 900, 0, 1000, 20);

        Assert.Equal(new[] { start, up, across, down, end }, Nets.Reaching(layout, start).Order());
    }

    ///<summary>Asked from anywhere on it, a net is the same net.</summary>
    [Fact]
    public void The_answer_does_not_depend_on_where_it_is_asked_from()
    {
        var layout = Empty();

        int lower = Add(layout, Metal1, 0, 0, 200, 20);
        int upper = Add(layout, Metal2, 90, -50, 110, 70);
        int via = Add(layout, Via1, 95, 5, 105, 15);

        var fromBelow = Nets.Reaching(layout, lower).Order().ToList();
        var fromAbove = Nets.Reaching(layout, upper).Order().ToList();
        var fromVia = Nets.Reaching(layout, via).Order().ToList();

        Assert.Equal(fromBelow, fromAbove);
        Assert.Equal(fromBelow, fromVia);
    }

    #endregion **********************************************************************



    #region What takes part *********************************************************

    ///
    ///**A layer nothing has said anything about takes no part**, and the answer is empty rather than just
    ///the shape itself - which is how the caller tells "nothing is attached to this" from "the question
    ///cannot be asked here".
    ///
    [Fact]
    public void A_layer_with_no_role_traces_nothing()
    {
        var layout = Empty();

        int marker = Add(layout, 90, 0, 0, 100, 20);

        Add(layout, 90, 80, 0, 200, 20);

        Assert.Empty(Nets.Reaching(layout, marker));
    }

    ///<summary>And a conductor overlapping one is not joined to it, whatever they share.</summary>
    [Fact]
    public void A_conductor_is_not_joined_to_something_with_no_role()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);

        Add(layout, 90, 0, 0, 200, 20);

        Assert.Equal(new[] { wire }, Nets.Reaching(layout, wire).Order());
    }

    ///
    ///**One physical layer is spelled as several data types**, and they are the same metal. A PDK writes
    ///drawing, pin and label on one layer number, so requiring the pair to match would break a net at every
    ///pin - which is exactly where somebody clicks.
    ///
    [Fact]
    public void Two_data_types_on_one_layer_number_are_one_metal()
    {
        var layout = Empty();

        int drawing = Add(layout, Metal1, 0, 0, 100, 20, dataType: 20);
        int pin = Add(layout, Metal1, 80, 0, 120, 20, dataType: 16);

        Assert.Equal(new[] { drawing, pin }, Nets.Reaching(layout, drawing).Order());
    }

    [Fact]
    public void An_index_outside_the_layout_is_answered_rather_than_thrown()
    {
        var layout = Empty();

        Add(layout, Metal1, 0, 0, 100, 20);

        Assert.Empty(Nets.Reaching(layout, -1));
        Assert.Empty(Nets.Reaching(layout, 99));
    }

    [Fact]
    public void Whether_anything_has_a_role_is_answerable()
    {
        var bare = new Layer(new LayerKey(90, 0), "#ffffff");
        var metal = new Layer(new LayerKey(68, 20), "#ffffff") { Role = LayerRole.Conductor };

        Assert.False(Nets.AnyRolesSet(new[] { bare }));
        Assert.True(Nets.AnyRolesSet(new[] { bare, metal }));
    }

    #endregion **********************************************************************



    #region What it is called *******************************************************

    ///<summary>A label anchored at a point, on a layer number rather than a role - labels conduct nothing.</summary>
    private static int Label(FlattenedLayout layout, short number, string says, int x, int y, short dataType = 16)
    {
        var element = new Element { Layer = new Layer(new LayerKey(number, dataType), "#ffffff"), Text = says };

        element.Points.Add(new Element.Point(x, y));

        layout.Elements.Add(element);

        return layout.Elements.Count - 1;
    }

    ///
    ///**A net has no name of its own anywhere in the file**, so the name is found rather than read: a layout
    ///says which piece of metal is VPWR by putting a label down on top of it.
    ///
    [Fact]
    public void A_label_on_a_net_names_it()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);

        Label(layout, Metal1, "VPWR", 100, 10);

        Assert.Equal(new[] { "VPWR" }, Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///
    ///**Matched by layer number, not by the whole pair.** A PDK writes the label on a different data type
    ///from the metal - 68/16 naming what is drawn on 68/20 - so requiring the pair to match would find no
    ///names at all on the files this is for.
    ///
    [Fact]
    public void A_label_names_metal_of_the_same_number_on_another_data_type()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20, dataType: 20);

        Label(layout, Metal1, "A", 100, 10, dataType: 16);

        Assert.Equal(new[] { "A" }, Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///<summary>A label on some other layer names some other thing, whatever it happens to sit over.</summary>
    [Fact]
    public void A_label_on_a_different_layer_does_not_name_it()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);

        Label(layout, Metal2, "B", 100, 10);

        Assert.Empty(Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///<summary>And one in the gap between two wires names neither of them.</summary>
    [Fact]
    public void A_label_that_lands_on_nothing_names_nothing()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);

        Label(layout, Metal1, "NOWHERE", 400, 400);

        Assert.Empty(Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///
    ///A pin label is routinely placed on the boundary of the shape it names, so on the edge has to count -
    ///refusing those would lose the names on exactly the shapes people label.
    ///
    [Fact]
    public void A_label_on_the_edge_still_names_it()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);

        Label(layout, Metal1, "EDGE", 200, 20);

        Assert.Equal(new[] { "EDGE" }, Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///
    ///**The name is found anywhere on the net**, not only on the shape that was clicked - which is the whole
    ///reason to trace first and ask afterwards. Here the label is on the metal2 the via reaches.
    ///
    [Fact]
    public void A_name_is_found_across_the_whole_net()
    {
        var layout = Empty();

        int lower = Add(layout, Metal1, 0, 0, 200, 20);

        Add(layout, Metal2, 90, -50, 110, 70);
        Add(layout, Via1, 95, 5, 105, 15);

        Label(layout, Metal2, "CLK", 100, 60);

        Assert.Equal(new[] { "CLK" }, Nets.NamesOn(layout, Nets.Reaching(layout, lower)));
    }

    ///<summary>And a name on a net this one is not joined to stays over there.</summary>
    [Fact]
    public void A_name_on_another_net_is_not_borrowed()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 100, 20);

        Add(layout, Metal1, 400, 0, 500, 20);

        Label(layout, Metal1, "MINE", 50, 10);
        Label(layout, Metal1, "THEIRS", 450, 10);

        Assert.Equal(new[] { "MINE" }, Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///
    ///**Two names on one net are both shown.** It is either two spellings of the same thing or two nets that
    ///are shorted, and hiding one of them would hide the second case entirely.
    ///
    [Fact]
    public void Two_names_on_one_net_are_both_reported()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 400, 20);

        Label(layout, Metal1, "VDD", 50, 10);
        Label(layout, Metal1, "VPWR", 350, 10);

        Assert.Equal(new[] { "VDD", "VPWR" }, Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///<summary>The same name written twice is one name, which is what a pin on every shape of a rail looks like.</summary>
    [Fact]
    public void The_same_name_twice_is_reported_once()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 400, 20);

        Label(layout, Metal1, "VDD", 50, 10);
        Label(layout, Metal1, "VDD", 350, 10);

        Assert.Equal(new[] { "VDD" }, Nets.NamesOn(layout, Nets.Reaching(layout, wire)));
    }

    ///<summary>A label takes no part in connectivity - it is an annotation, and conducts nothing.</summary>
    [Fact]
    public void A_label_is_not_part_of_the_net_itself()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);

        int label = Label(layout, Metal1, "VPWR", 100, 10);

        Assert.DoesNotContain(label, Nets.Reaching(layout, wire));
    }

    ///
    ///**A label cannot be traced from, and the caller has to be able to ask.**
    ///
    ///A pin label sits on a conducting layer, so its *role* lets it through - and it has one point, so the
    ///walk refuses it. The two conditions disagreeing is how the button came to be offered on a label and do
    ///nothing at all when pressed, which reads as a net of one shape. There is one condition now, and this is
    ///it: the walk asks it and so does whatever offers the operation.
    ///
    [Fact]
    public void A_label_cannot_be_traced_from_even_on_a_conducting_layer()
    {
        var layout = Empty();

        Add(layout, Metal1, 0, 0, 200, 20);

        int label = Label(layout, Metal1, "VPWR", 100, 10, dataType: 0);

        //On a layer that was given a role, which is the case that caused this: sky130's met1 pin layer is
        //metal, so somebody marking it conductor marks the labels on it too.
        layout.Elements[label].Layer.Role = LayerRole.Conductor;

        Assert.Equal(LayerRole.Conductor, Nets.RoleOf(layout.Elements[label]));

        Assert.False(Nets.TakesPart(layout.Elements[label]));
        Assert.Empty(Nets.Reaching(layout, label));
    }

    ///<summary>And a shape that the walk does follow answers the same question the same way.</summary>
    [Fact]
    public void What_the_walk_follows_is_what_takes_part()
    {
        var layout = Empty();

        int wire = Add(layout, Metal1, 0, 0, 200, 20);
        int marker = Add(layout, 90, 0, 0, 200, 20);

        Assert.True(Nets.TakesPart(layout.Elements[wire]));
        Assert.NotEmpty(Nets.Reaching(layout, wire));

        Assert.False(Nets.TakesPart(layout.Elements[marker]));
        Assert.Empty(Nets.Reaching(layout, marker));
    }

    [Fact]
    public void An_empty_net_has_no_name()
    {
        var layout = Empty();

        Add(layout, Metal1, 0, 0, 200, 20);
        Label(layout, Metal1, "VPWR", 100, 10);

        Assert.Empty(Nets.NamesOn(layout, new HashSet<int>()));
    }

    #endregion **********************************************************************



    #region Roles through the layermap **********************************************

    [Fact]
    public void A_role_column_is_read()
    {
        var mapping = LayerNames.Parse("68,20,met1,#ff0000,100,50,conductor\n67,44,via1,#00ff00,90,10,via\n");

        Assert.Equal(LayerRole.Conductor, mapping.Roles[new LayerKey(68, 20)]);
        Assert.Equal(LayerRole.Via, mapping.Roles[new LayerKey(67, 44)]);
    }

    [Fact]
    public void A_role_is_read_whatever_case_it_is_written_in()
    {
        var mapping = LayerNames.Parse("68,20,met1,,,,CONDUCTOR\n69,20,met2,,,,Via\n");

        Assert.Equal(LayerRole.Conductor, mapping.Roles[new LayerKey(68, 20)]);
        Assert.Equal(LayerRole.Via, mapping.Roles[new LayerKey(69, 20)]);
    }

    ///<summary>A misspelled role reads as a layer that takes no part, which looks like a net ending there.</summary>
    [Fact]
    public void A_role_that_is_not_one_is_reported()
    {
        var mapping = LayerNames.Parse("68,20,met1,,,,metal\n");

        Assert.Empty(mapping.Roles);
        Assert.Contains(mapping.Problems, problem => problem.Contains("metal"));
    }

    ///<summary>A row that says only a role still says something, so it is not reported as empty.</summary>
    [Fact]
    public void A_row_with_only_a_role_is_not_a_row_that_names_nothing()
    {
        var mapping = LayerNames.Parse("68,20,,,,,conductor\n");

        Assert.Empty(mapping.Problems);
        Assert.Equal(LayerRole.Conductor, mapping.Roles[new LayerKey(68, 20)]);
    }

    ///<summary>A mapping written for an older build, with no seventh column, still reads.</summary>
    [Fact]
    public void A_mapping_with_no_role_column_still_reads()
    {
        var mapping = LayerNames.Parse("68,20,met1,#ff0000,100,50\n");

        Assert.Empty(mapping.Problems);
        Assert.Empty(mapping.Roles);
        Assert.Equal("met1", mapping.Names[new LayerKey(68, 20)]);
    }

    private static AdditionalGDSInformation OneLayer(short number, short dataType)
    {
        var gds = new GDS(GdsTestData.ReadFixture("placed.gds"));

        var information = gds.AdditionalInformation;

        information.Layers[new LayerKey(number, dataType)] = new Layer(new LayerKey(number, dataType), "#ffffff");

        return information;
    }

    [Fact]
    public void A_role_lands_on_the_layer_it_names()
    {
        var information = OneLayer(68, 20);

        LayerNames.Parse("68,20,met1,,,,conductor\n").ApplyTo(information.Layers);

        Assert.Equal(LayerRole.Conductor, information.Layers[new LayerKey(68, 20)].Role);
    }

    ///
    ///**The role survives a round trip through the exported file.**
    ///
    ///Which is the point of the column: a role is worked out once by somebody who knows which of their
    ///numbers are metal, and it should be a thing they can hand to the next person.
    ///
    [Fact]
    public void A_role_survives_being_exported_and_read_back()
    {
        var information = OneLayer(68, 20);

        information.Layers[new LayerKey(68, 20)].Role = LayerRole.Via;

        var read = LayerNames.Parse(LayerNames.Export(information));

        Assert.Equal(LayerRole.Via, read.Roles[new LayerKey(68, 20)]);
    }

    ///<summary>A layer with a role but no name is still written into a session, or it is lost on a refresh.</summary>
    [Fact]
    public void A_role_on_an_unnamed_layer_is_kept_in_a_session()
    {
        var information = OneLayer(68, 20);

        information.Layers[new LayerKey(68, 20)].Role = LayerRole.Conductor;

        var read = LayerNames.Parse(LayerNames.Named(information));

        Assert.Equal(LayerRole.Conductor, read.Roles[new LayerKey(68, 20)]);
    }

    ///<summary>Clearing the names clears the roles with them - they came out of the same mapping.</summary>
    [Fact]
    public void Clearing_the_names_clears_the_roles()
    {
        var information = OneLayer(68, 20);

        information.Layers[new LayerKey(68, 20)].Role = LayerRole.Conductor;

        LayerNames.Clear(information);

        Assert.Equal(LayerRole.None, information.Layers[new LayerKey(68, 20)].Role);
    }

    #endregion **********************************************************************
}
