using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers the two-byte bit fields: STRANS, PRESENTATION and ELFLAGS.
///
///GDSII numbers these bits from the left, so bit 0 is the most significant. Getting that backwards
///produces code that looks right and silently reads the wrong flag, which is what these pin down.
///</summary>
public class BitFieldTests
{
    private static BitArrayData Field(int value)
    {
        return new BitArrayData(new byte[] { (byte)(value >> 8), (byte)value });
    }

    #region STRANS *********************************************************************

    [Fact]
    public void An_absent_strans_reflects_nothing_and_is_relative()
    {
        var strans = Strans.From(null);

        Assert.False(strans.ReflectAboutX);
        Assert.False(strans.AbsoluteMagnification);
        Assert.False(strans.AbsoluteAngle);
    }

    [Fact]
    public void An_empty_strans_word_sets_no_flags()
    {
        var strans = Strans.From(Field(0x0000));

        Assert.False(strans.ReflectAboutX);
        Assert.False(strans.AbsoluteMagnification);
        Assert.False(strans.AbsoluteAngle);
    }

    ///<summary>Bit 0 is the most significant bit of the word, so reflection is 0x8000 and not 0x0001.</summary>
    [Fact]
    public void Reflection_is_the_top_bit()
    {
        Assert.True(Strans.From(Field(0x8000)).ReflectAboutX);
        Assert.False(Strans.From(Field(0x0001)).ReflectAboutX);
    }

    [Fact]
    public void Absolute_magnification_is_bit_thirteen()
    {
        var strans = Strans.From(Field(0x0004));

        Assert.True(strans.AbsoluteMagnification);
        Assert.False(strans.AbsoluteAngle);
        Assert.False(strans.ReflectAboutX);
    }

    [Fact]
    public void Absolute_angle_is_bit_fourteen()
    {
        var strans = Strans.From(Field(0x0002));

        Assert.True(strans.AbsoluteAngle);
        Assert.False(strans.AbsoluteMagnification);
    }

    [Fact]
    public void All_three_strans_flags_can_be_set_together()
    {
        var strans = Strans.From(Field(0x8006));

        Assert.True(strans.ReflectAboutX);
        Assert.True(strans.AbsoluteMagnification);
        Assert.True(strans.AbsoluteAngle);
    }

    ///<summary>The value the sample files use on their reflected labels and references.</summary>
    [Fact]
    public void The_value_the_corpus_uses_means_reflection_only()
    {
        var strans = Strans.From(Field(0x8000));

        Assert.True(strans.ReflectAboutX);
        Assert.False(strans.AbsoluteMagnification);
        Assert.False(strans.AbsoluteAngle);
    }

    #endregion ************************************************************************



    #region PRESENTATION ***************************************************************

    ///<summary>The format's default when the record is omitted.</summary>
    [Fact]
    public void An_absent_presentation_is_left_and_top_in_font_zero()
    {
        var presentation = TextPresentation.From(null);

        Assert.Equal(HorizontalPresentation.Left, presentation.Horizontal);
        Assert.Equal(VerticalPresentation.Top, presentation.Vertical);
        Assert.Equal(0, presentation.Font);
    }

    [Theory]
    [InlineData(0x0000, HorizontalPresentation.Left)]
    [InlineData(0x0001, HorizontalPresentation.Center)]
    [InlineData(0x0002, HorizontalPresentation.Right)]
    public void Horizontal_justification_is_the_lowest_two_bits(int value, HorizontalPresentation expected)
    {
        Assert.Equal(expected, TextPresentation.From(Field(value)).Horizontal);
    }

    [Theory]
    [InlineData(0x0000, VerticalPresentation.Top)]
    [InlineData(0x0004, VerticalPresentation.Middle)]
    [InlineData(0x0008, VerticalPresentation.Bottom)]
    public void Vertical_justification_is_the_next_two_bits(int value, VerticalPresentation expected)
    {
        Assert.Equal(expected, TextPresentation.From(Field(value)).Vertical);
    }

    [Theory]
    [InlineData(0x0000, 0)]
    [InlineData(0x0010, 1)]
    [InlineData(0x0020, 2)]
    [InlineData(0x0030, 3)]
    public void The_font_is_the_two_bits_above_that(int value, int expected)
    {
        Assert.Equal(expected, TextPresentation.From(Field(value)).Font);
    }

    ///<summary>
    ///The two values the sample files actually contain, which is what the 2D view's label placement
    ///turns on: 12630 labels centered and 48 left-aligned, all of them vertically middle.
    ///</summary>
    [Fact]
    public void The_corpus_values_decode_to_center_middle_and_left_middle()
    {
        var center = TextPresentation.From(Field(0x0005));

        Assert.Equal(HorizontalPresentation.Center, center.Horizontal);
        Assert.Equal(VerticalPresentation.Middle, center.Vertical);

        var left = TextPresentation.From(Field(0x0004));

        Assert.Equal(HorizontalPresentation.Left, left.Horizontal);
        Assert.Equal(VerticalPresentation.Middle, left.Vertical);
    }

    ///<summary>Only 0, 1 and 2 are defined, so a 3 must not become a nameless enum value.</summary>
    [Fact]
    public void An_undefined_justification_falls_back_to_the_default()
    {
        var presentation = TextPresentation.From(Field(0x000F));

        Assert.Equal(HorizontalPresentation.Left, presentation.Horizontal);
        Assert.Equal(VerticalPresentation.Top, presentation.Vertical);
    }

    #endregion ************************************************************************



    #region ELFLAGS ********************************************************************

    [Fact]
    public void An_absent_elflags_sets_neither_flag()
    {
        var flags = ElementFlags.From(null);

        Assert.False(flags.TemplateData);
        Assert.False(flags.ExternalData);
    }

    [Fact]
    public void Template_data_is_the_bottom_bit()
    {
        var flags = ElementFlags.From(Field(0x0001));

        Assert.True(flags.TemplateData);
        Assert.False(flags.ExternalData);
    }

    [Fact]
    public void External_data_is_the_bit_above_it()
    {
        var flags = ElementFlags.From(Field(0x0002));

        Assert.True(flags.ExternalData);
        Assert.False(flags.TemplateData);
    }

    #endregion ************************************************************************



    #region Malformed input ************************************************************

    ///<summary>A bit field needs two bytes; anything else is not one, so the default stands.</summary>
    [Fact]
    public void A_one_byte_field_falls_back_to_the_default()
    {
        Assert.False(Strans.From(new BitArrayData(new byte[] { 0x80 })).ReflectAboutX);
    }

    [Fact]
    public void A_payload_of_the_wrong_type_falls_back_to_the_default()
    {
        Assert.False(Strans.From(new Int2Data(-32768)).ReflectAboutX);
        Assert.Equal(HorizontalPresentation.Left, TextPresentation.From(new AsciiData("x")).Horizontal);
    }

    #endregion ************************************************************************



    #region Through the flattener ******************************************************

    private static byte[] TextLibrary(int presentation, short layer = 67)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("LIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),
            GdsTestData.Record(RecordType.TEXT),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(layer)),
            GdsTestData.Record(RecordType.TEXTTYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.PRESENTATION, new byte[] { (byte)(presentation >> 8), (byte)presentation }),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(100, 200)),
            GdsTestData.Record(RecordType.STRING, GdsTestData.Ascii("VPWR")),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));
    }

    [Fact]
    public void A_labels_justification_reaches_the_renderers()
    {
        var element = Assert.Single(GdsFlattener.Flatten(new GDS(TextLibrary(0x0004))).Elements);

        Assert.Equal("VPWR", element.Text);
        Assert.Equal(HorizontalPresentation.Left, element.Presentation.Horizontal);
        Assert.Equal(VerticalPresentation.Middle, element.Presentation.Vertical);
    }

    [Fact]
    public void Geometry_carries_the_default_presentation()
    {
        byte[] stream = GdsTestData.MinimalLibrary();

        var element = Assert.Single(GdsFlattener.Flatten(new GDS(stream)).Elements);

        Assert.Null(element.Text);
        Assert.Equal(HorizontalPresentation.Left, element.Presentation.Horizontal);
    }

    ///<summary>
    ///Every label in the sample files must land on one of the two justifications the survey found, since
    ///that is what the 2D view now positions from.
    ///</summary>
    [Fact]
    public void Every_label_in_the_corpus_decodes_to_a_defined_justification()
    {
        var seen = new SortedSet<string>();

        foreach (string file in GdsTestData.AllSampleFiles())
        {
            foreach (var element in GdsFlattener.Flatten(new GDS(File.ReadAllBytes(file))).Elements)
            {
                if (element.Text is null)
                    continue;

                seen.Add($"{element.Presentation.Horizontal}/{element.Presentation.Vertical}");
            }
        }

        Assert.Equal(new[] { "Center/Middle", "Left/Middle", "Left/Top" }, seen.ToArray());
    }

    #endregion ************************************************************************
}
