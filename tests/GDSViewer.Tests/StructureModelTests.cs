using GdsII;
using GDSViewer.Models;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers the model tree the parser builds over the flat record list: the library preamble, its
///optional records, structures, and each element type with its optional sub-records.
///</summary>
public class StructureModelTests
{
    #region Library preamble ***********************************************************

    [Fact]
    public void Minimal_library_fills_the_required_stream_records()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var library = gds.StreamFormat;

        Assert.Equal(RecordType.HEADER, library.HEADER.Type);
        Assert.Equal(RecordType.BGNLIB, library.BGNLIB.Type);
        Assert.Equal("TESTLIB", ((AsciiData)library.LIBNAME.Data!).Value);
        Assert.Equal(RecordType.UNITS, library.UNITS.Type);
        Assert.Equal(RecordType.ENDLIB, library.ENDLIB.Type);
    }

    [Fact]
    public void Optional_library_records_are_absent_when_not_in_the_stream()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var library = gds.StreamFormat;

        Assert.Null(library.REFLIBS);
        Assert.Null(library.FONTS);
        Assert.Null(library.ATTRTABLE);
        Assert.Null(library.GENERATIONS);
        Assert.Null(library.FormatType);
    }

    [Fact]
    public void Optional_library_records_are_read_in_their_declared_order()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.REFLIBS, GdsTestData.Ascii("REF")),
            GdsTestData.Record(RecordType.FONTS, GdsTestData.Ascii("FONT")),
            GdsTestData.Record(RecordType.ATTRTABLE, GdsTestData.Ascii("attr.tbl")),
            GdsTestData.Record(RecordType.GENERATIONS, GdsTestData.Int2(3)),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.ENDLIB));

        var library = new GDS(stream).StreamFormat;

        //These four records are optional, so the model types them as nullable. This stream carries all of
        //them, which is the point of the test.
        Assert.Equal("REF", ((AsciiData)library.REFLIBS!.Data!).Value);
        Assert.Equal("FONT", ((AsciiData)library.FONTS!.Data!).Value);
        //ATTRTABLE is 0x2306, so its low byte declares ASCII - it names an attribute table file.
        Assert.Equal("attr.tbl", ((AsciiData)library.ATTRTABLE!.Data!).Value);
        Assert.Equal((short)3, ((Int2Data)library.GENERATIONS!.Data!).Value);
    }

    [Fact]
    public void Format_record_builds_a_format_type_model_with_its_masks()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.FORMAT, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.MASK, GdsTestData.Ascii("1 2 3")),
            GdsTestData.Record(RecordType.MASK, GdsTestData.Ascii("4 5")),
            GdsTestData.Record(RecordType.ENDMASKS),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.ENDLIB));

        var format = new GDS(stream).StreamFormat.FormatType;

        Assert.NotNull(format);
        Assert.Equal((short)1, ((Int2Data)format.FORMAT.Data!).Value);
        Assert.Equal(2, format.MASKS.Count);
        Assert.Equal("1 2 3", ((AsciiData)format.MASKS[0].Data!).Value);
        Assert.Equal(RecordType.ENDMASKS, format.ENDMASKS!.Type);
    }

    [Fact]
    public void A_library_with_no_structures_parses_to_an_empty_structure_list()
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("EMPTY")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.ENDLIB));

        var gds = new GDS(stream);

        Assert.Empty(gds.StreamFormat.Structures);
        Assert.Empty(gds.AdditionalInformation.Layers);
    }

    #endregion ************************************************************************



    #region Structures ****************************************************************

    [Fact]
    public void Structure_exposes_its_name_and_elements()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());
        var structure = Assert.Single(gds.StreamFormat.Structures);

        Assert.Equal("TESTCELL", ((AsciiData)structure.STRNAME.Data!).Value);
        Assert.Equal(RecordType.ENDSTR, structure.ENDSTR.Type);
        Assert.Single(structure.Elements);
    }

    [Fact]
    public void Several_structures_are_parsed_in_stream_order()
    {
        byte[] cell = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("A")),
            GdsTestData.Record(RecordType.ENDSTR));

        byte[] otherCell = GdsTestData.Concat(
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("B")),
            GdsTestData.Record(RecordType.ENDSTR));

        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            cell,
            otherCell,
            GdsTestData.Record(RecordType.ENDLIB));

        var structures = new GDS(stream).StreamFormat.Structures;

        Assert.Equal(2, structures.Count);
        Assert.Equal("A", ((AsciiData)structures[0].STRNAME.Data!).Value);
        Assert.Equal("B", ((AsciiData)structures[1].STRNAME.Data!).Value);
    }

    #endregion ************************************************************************



    #region Element types *************************************************************

    ///<summary>Wraps element records in a one-structure library and returns the parsed elements.</summary>
    private static List<ElementModel> ParseElements(params byte[][] elementRecords)
    {
        byte[] stream = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),
            GdsTestData.Concat(elementRecords),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));

        return new GDS(stream).StreamFormat.Structures[0].Elements;
    }

    [Fact]
    public void Boundary_carries_layer_datatype_and_coordinates()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(64)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 10, 0, 10, 10, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var boundary = Assert.IsType<BoundaryModel>(element.Element);

        Assert.Equal((short)64, ((Int2Data)boundary.LAYER.Data!).Value);
        Assert.Equal((short)20, ((Int2Data)boundary.DATATYPE.Data!).Value);
        Assert.Equal(new[] { 0, 0, 10, 0, 10, 10, 0, 0 }, ((Int4Data)boundary.XY.Data!).Values);
        Assert.Equal(RecordType.ENDEL, element.ENDEL.Type);
    }

    [Fact]
    public void Boundary_reads_the_optional_elflags_and_plex_prefixes()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.ELFLAGS, new byte[] { 0x00, 0x01 }),
            GdsTestData.Record(RecordType.PLEX, GdsTestData.Int2(0, 7)),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var boundary = Assert.IsType<BoundaryModel>(element.Element);

        Assert.NotNull(boundary.ELFLAGS);
        Assert.NotNull(boundary.PLEX);
        Assert.Equal((short)1, ((Int2Data)boundary.LAYER.Data!).Value);
    }

    [Fact]
    public void Path_reads_its_optional_pathtype_and_width()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.PATHTYPE, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.WIDTH, GdsTestData.Int4(140)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 500, 0)),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var path = Assert.IsType<PathModel>(element.Element);

        Assert.Equal((short)2, ((Int2Data)path.PATHTYPE!.Data!).Value);
        //WIDTH is 0x0F03, so its low byte declares INT4 - a path can be wider than a short holds.
        Assert.Equal(140, ((Int4Data)path.WIDTH!.Data!).Value);
        Assert.Equal(new[] { 0, 0, 500, 0 }, ((Int4Data)path.XY.Data!).Values);
    }

    [Fact]
    public void Path_without_pathtype_or_width_leaves_them_null()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.PATH),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 500, 0)),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var path = Assert.IsType<PathModel>(element.Element);

        Assert.Null(path.PATHTYPE);
        Assert.Null(path.WIDTH);
    }

    [Fact]
    public void Box_carries_its_boxtype()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.BOX),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(3)),
            GdsTestData.Record(RecordType.BOXTYPE, GdsTestData.Int2(9)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var box = Assert.IsType<BoxModel>(element.Element);

        Assert.Equal((short)9, ((Int2Data)box.BOXTYPE.Data!).Value);
    }

    [Fact]
    public void Node_carries_its_nodetype()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.NODE),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(4)),
            GdsTestData.Record(RecordType.NODETYPE, GdsTestData.Int2(11)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(1, 2)),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var node = Assert.IsType<NodeModel>(element.Element);

        Assert.Equal((short)11, ((Int2Data)node.NODETYPE.Data!).Value);
    }

    [Fact]
    public void Text_delegates_its_coordinates_to_the_text_body()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(66)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.PRESENTATION, new byte[] { 0x00, 0x05 }),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(700, 800)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii("VDD")),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var text = Assert.IsType<TextModel>(element.Element);

        Assert.Equal((short)66, ((Int2Data)text.LAYER.Data!).Value);
        Assert.Equal("VDD", ((AsciiData)text.TextBody.STRING.Data!).Value);
        //XY is an override that reads through to TextBody, so both must agree.
        Assert.Equal(new[] { 700, 800 }, ((Int4Data)text.XY.Data!).Values);
        Assert.Same(text.TextBody.XY, text.XY);
    }

    [Fact]
    public void Text_body_reads_an_optional_strans_block()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(66)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.STRANS, new byte[] { 0x80, 0x00 }),
            GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(0.5)),
            GdsTestData.Record(RecordType.ANGLE, GdsTestData.Real8(90.0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii("A")),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var text = Assert.IsType<TextModel>(element.Element);
        var strans = text.TextBody.Strans;

        Assert.NotNull(strans);
        Assert.Equal(0.5, ((Real8Data)strans.MAG!.Data!).Value, 1e-12);
        Assert.Equal(90.0, ((Real8Data)strans.ANGLE!.Data!).Value, 1e-9);
    }

    [Fact]
    public void Sref_carries_its_structure_name_and_transform()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("SUBCELL")),
            GdsTestData.Record(RecordType.STRANS, new byte[] { 0x00, 0x00 }),
            GdsTestData.Record(RecordType.MAG, GdsTestData.Real8(2.0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(100, 200)),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var sref = Assert.IsType<SrefModel>(element.Element);

        Assert.Equal("SUBCELL", ((AsciiData)sref.SNAME.Data!).Value);
        Assert.Equal(2.0, ((Real8Data)sref.Strans!.MAG!.Data!).Value, 1e-12);
        Assert.Equal(new[] { 100, 200 }, ((Int4Data)sref.XY.Data!).Values);
    }

    [Fact]
    public void Aref_carries_its_column_and_row_counts()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.AREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("TILE")),
            GdsTestData.Record(RecordType.COLROW, GdsTestData.Int2(4, 3)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 400, 0, 0, 300)),
            GdsTestData.Record(RecordType.ENDEL))[0];

        var aref = Assert.IsType<ArefModel>(element.Element);

        Assert.Equal("TILE", ((AsciiData)aref.SNAME.Data!).Value);
        Assert.Equal(new short[] { 4, 3 }, ((Int2Data)aref.COLROW.Data!).Values);
    }

    ///<summary>
    ///SREF and AREF deliberately do not implement IHasLayer - a reference has no layer of its own.
    ///Both the renderers and layer discovery filter on that interface, so this is load-bearing.
    ///</summary>
    [Fact]
    public void References_do_not_implement_IHasLayer_while_drawable_elements_do()
    {
        Assert.False(typeof(SrefModel).IsAssignableTo(typeof(IHasLayer)));
        Assert.False(typeof(ArefModel).IsAssignableTo(typeof(IHasLayer)));

        Assert.True(typeof(BoundaryModel).IsAssignableTo(typeof(IHasLayer)));
        Assert.True(typeof(PathModel).IsAssignableTo(typeof(IHasLayer)));
        Assert.True(typeof(TextModel).IsAssignableTo(typeof(IHasLayer)));
        Assert.True(typeof(NodeModel).IsAssignableTo(typeof(IHasLayer)));
        Assert.True(typeof(BoxModel).IsAssignableTo(typeof(IHasLayer)));
    }

    #endregion ************************************************************************



    #region Element properties ********************************************************

    [Fact]
    public void Element_properties_are_collected_as_attribute_value_pairs()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("net1")),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(3)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("net2")),
            GdsTestData.Record(RecordType.ENDEL))[0];

        Assert.Equal(2, element.Properties.Count);
        Assert.Equal((short)2, ((Int2Data)element.Properties[0].PROPATTR.Data!).Value);
        Assert.Equal("net1", ((AsciiData)element.Properties[0].PROPVALUE.Data!).Value);
        Assert.Equal("net2", ((AsciiData)element.Properties[1].PROPVALUE.Data!).Value);
    }

    [Fact]
    public void An_element_without_properties_has_an_empty_property_list()
    {
        var gds = new GDS(GdsTestData.MinimalLibrary());

        Assert.Empty(gds.StreamFormat.Structures[0].Elements[0].Properties);
    }

    ///<summary>
    ///An attribute number names a property within its element, so carrying it twice is two values for one
    ///name with nothing to say which is meant. KLayout resolves it by keeping the last silently, which
    ///loses the first without telling anyone.
    ///</summary>
    [Fact]
    public void An_element_repeating_an_attribute_number_is_refused()
    {
        var thrown = Assert.Throws<InvalidDataException>(() => ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("net1")),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("net2")),
            GdsTestData.Record(RecordType.ENDEL)));

        Assert.Contains("a second PROPATTR 2 on the same element", thrown.Message);
    }

    ///<summary>
    ///The number has to repeat, not the value. Two properties sharing a value is ordinary - two nets
    ///named the same thing under different attributes - and the check must not reach for it.
    ///</summary>
    [Fact]
    public void Two_attributes_sharing_a_value_are_accepted()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("same")),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(3)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("same")),
            GdsTestData.Record(RecordType.ENDEL))[0];

        Assert.Equal(2, element.Properties.Count);
    }

    ///<summary>
    ///The rule is per element, so the next element starts over. Repeating a number across elements is how
    ///a property is normally used at all - attribute 2 meaning "net name" on every one of them.
    ///</summary>
    [Fact]
    public void The_same_attribute_number_on_two_elements_is_accepted()
    {
        var elements = ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("net1")),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.PROPVALUE, GdsTestData.Ascii("net2")),
            GdsTestData.Record(RecordType.ENDEL));

        Assert.Equal(2, elements.Count);
        Assert.Single(elements[0].Properties);
        Assert.Single(elements[1].Properties);
    }

    ///<summary>
    ///A PROPATTR with no PROPVALUE after it. Already covered by PropertyModel taking the pair, and pinned
    ///here because it is half of what "properties are validated" has to mean.
    ///</summary>
    [Fact]
    public void An_attribute_without_a_value_is_refused()
    {
        var thrown = Assert.Throws<InvalidDataException>(() => ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.PROPATTR, GdsTestData.Int2(2)),
            GdsTestData.Record(RecordType.ENDEL)));

        Assert.Contains("is ENDEL where PROPVALUE was expected", thrown.Message);
    }

    #endregion ************************************************************************



    #region IsElementRecord ***********************************************************

    [Theory]
    [InlineData(RecordType.BOUNDARY)]
    [InlineData(RecordType.PATH)]
    [InlineData(RecordType.SREF)]
    [InlineData(RecordType.AREF)]
    [InlineData(RecordType.TEXT)]
    [InlineData(RecordType.NODE)]
    [InlineData(RecordType.BOX)]
    public void IsElementRecord_accepts_every_element_start_record(RecordType type)
    {
        Assert.True(ElementModel.IsElementRecord(type));
    }

    [Theory]
    [InlineData(RecordType.HEADER)]
    [InlineData(RecordType.BGNSTR)]
    [InlineData(RecordType.STRNAME)]
    [InlineData(RecordType.ENDSTR)]
    [InlineData(RecordType.ENDLIB)]
    [InlineData(RecordType.LAYER)]
    [InlineData(RecordType.XY)]
    [InlineData(RecordType.ENDEL)]
    public void IsElementRecord_rejects_everything_else(RecordType type)
    {
        Assert.False(ElementModel.IsElementRecord(type));
    }

    #endregion ************************************************************************



    #region ELFLAGS and PLEX on the base class *****************************************

    ///<summary>Wraps an element in the two optional prefix records the format allows on all seven.</summary>
    private static ElementModel ParseWithFlagPrefixes(byte[] elementRecord, params byte[][] tail)
    {
        return ParseElements(GdsTestData.Concat(
            elementRecord,
            GdsTestData.Record(RecordType.ELFLAGS, new byte[] { 0x00, 0x01 }),
            GdsTestData.Record(RecordType.PLEX, GdsTestData.Int2(0, 7)),
            GdsTestData.Concat(tail),
            GdsTestData.Record(RecordType.ENDEL)))[0];
    }

    private static List<(string Name, ElementModel Parsed)> AllElementTypesWithFlagPrefixes()
    {
        byte[] layer = GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1));
        byte[] xy = GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare()));
        byte[] sname = GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("CELL"));

        return new List<(string, ElementModel)>
        {
            ("BOUNDARY", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.BOUNDARY), layer, GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)), xy)),
            ("PATH", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.PATH), layer, GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)), xy)),
            ("SREF", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.SREF), sname, xy)),
            ("AREF", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.AREF), sname, GdsTestData.Record(RecordType.COLROW, GdsTestData.Int2(2, 2)), xy)),
            ("TEXT", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.TEXT), layer, GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(0)), xy, GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii("A")))),
            ("NODE", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.NODE), layer, GdsTestData.Record(RecordType.NODETYPE, GdsTestData.Int2(0)), xy)),
            ("BOX", ParseWithFlagPrefixes(GdsTestData.Record(RecordType.BOX), layer, GdsTestData.Record(RecordType.BOXTYPE, GdsTestData.Int2(0)), xy)),
        };
    }

    ///<summary>
    ///The flattener and the views hold an ElementType, never a concrete model, so these two records have
    ///to arrive on the base class. Six of the seven models used to redeclare them, which hid the base
    ///properties and left them null on every element ever parsed.
    ///</summary>
    [Fact]
    public void Every_element_type_carries_its_elflags_and_plex_on_the_base_class()
    {
        var missing = new List<string>();

        foreach (var (name, parsed) in AllElementTypesWithFlagPrefixes())
        {
            ElementType element = parsed.Element;

            if (element.ELFLAGS is null)
                missing.Add($"{name}.ELFLAGS");

            if (element.PLEX is null)
                missing.Add($"{name}.PLEX");
        }

        Assert.Equal(Array.Empty<string>(), missing.ToArray());
    }

    ///<summary>Which is what makes ElementFlags reachable at all - it reads through the base property.</summary>
    [Fact]
    public void Element_flags_decode_from_an_element_held_as_its_base_type()
    {
        foreach (var (name, parsed) in AllElementTypesWithFlagPrefixes())
        {
            ElementType element = parsed.Element;

            var flags = ElementFlags.From(element.ELFLAGS?.Data);

            Assert.True(flags.TemplateData, $"{name} lost its template-data flag");
            Assert.False(flags.ExternalData);
        }
    }

    ///<summary>An element with no prefix records leaves both null, and the flags fall back to the default.</summary>
    [Fact]
    public void An_element_without_the_prefixes_has_neither_and_reads_as_default()
    {
        var element = ParseElements(
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare())),
            GdsTestData.Record(RecordType.ENDEL))[0].Element;

        Assert.Null(element.ELFLAGS);
        Assert.Null(element.PLEX);
        Assert.False(ElementFlags.From(element.ELFLAGS?.Data).TemplateData);
    }

    #endregion ************************************************************************
}
