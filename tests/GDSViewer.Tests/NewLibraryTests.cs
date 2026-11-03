using GdsII;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Starting a layout from nothing.
///
///**Because every other way in needs something to read.** Bytes, a stream, a text dump, a list of records -
///all of them start from a file somebody else made, so building a layout from scratch meant hand-writing an
///eight-line text skeleton and parsing it. That is not a workaround anybody should have to find, and the
///readme was carrying it as an example.
///
///What is worth pinning here is the part a skeleton got wrong quietly: the two halves of `UNITS`, which say
///the same thing in different units and have to agree, and the fact that what comes out is a real file rather
///than an object that only agrees with itself.
///
public class NewLibraryTests
{
    private static double[] UnitsOf(GDS gds)
    {
        Assert.NotNull(gds.StreamFormat.UNITS);
        Assert.IsType<Real8Data>(gds.StreamFormat.UNITS!.Data);

        return ((Real8Data)gds.StreamFormat.UNITS!.Data!).Values;
    }

    ///<summary>One empty cell, no layers, and nothing drawn - which is what "new" means.</summary>
    [Fact]
    public void A_new_library_has_one_empty_cell_and_nothing_else()
    {
        var gds = GDS.NewLibrary();

        Assert.Equal(new[] { "TOP" }, Hierarchy.Names(gds));
        Assert.Empty(gds.AdditionalInformation.Layers);
        Assert.Empty(GdsFlattener.Flatten(gds).Elements);
    }

    ///<summary>And it is named what it was asked to be named, cell included.</summary>
    [Fact]
    public void The_library_and_its_cell_take_the_names_they_were_given()
    {
        var gds = GDS.NewLibrary("MYLIB", "MAIN");

        Assert.Equal(new[] { "MAIN" }, Hierarchy.Names(gds));
        Assert.Contains("LIBNAME: MYLIB", gds.AsText());
    }

    ///
    ///**A database unit is a nanometer unless something says otherwise.**
    ///
    ///Nearly every real file uses one, and it is what makes a process table in nanometers - the heights and
    ///thicknesses a layermap carries - read as it stands rather than needing a conversion nobody would think
    ///to do.
    ///
    [Fact]
    public void A_database_unit_is_a_nanometer_by_default()
    {
        double[] units = UnitsOf(GDS.NewLibrary());

        Assert.Equal(1e-9, units[1], 15);
    }

    ///
    ///**And the two halves of UNITS cannot disagree**, because only one of them is a parameter.
    ///
    ///The record says the same size twice: once in user units, once in meters. A skeleton typed by hand can
    ///say a database unit is a nanometer in one field and something else in the other, and nothing complains -
    ///the file simply measures differently depending on which half a reader believes.
    ///
    [Theory]
    [InlineData(1e-9)]
    [InlineData(1e-6)]
    [InlineData(2.5e-10)]
    public void The_two_halves_of_units_say_the_same_size(double meters)
    {
        double[] units = UnitsOf(GDS.NewLibrary("LIB", "TOP", meters));

        Assert.Equal(meters, units[1], 15);

        //User units are microns, so the first half is the second scaled by a million.
        Assert.Equal(meters * 1e6, units[0], 15);
    }

    ///<summary>A size of nothing is refused rather than written, since every coordinate would measure zero.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1e-9)]
    public void A_database_unit_of_no_size_is_refused(double meters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GDS.NewLibrary("LIB", "TOP", meters));
    }

    ///<summary>And so is one that is no size at all: an infinity or a NaN would write a UNITS no reader can use.</summary>
    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void A_database_unit_that_is_not_a_number_is_refused(double meters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GDS.NewLibrary("LIB", "TOP", meters));
    }

    ///<summary>The size reads back as microns, which is what every ruler, writer and tool asks for.</summary>
    [Theory]
    [InlineData(1e-9, 0.001)]
    [InlineData(1e-6, 1)]
    [InlineData(2.5e-10, 0.00025)]
    public void The_size_of_a_database_unit_reads_back_in_microns(double meters, double microns)
    {
        Assert.Equal(microns, GDS.NewLibrary("LIB", "TOP", meters).MicronsPerDatabaseUnit!.Value, 12);
    }

    ///
    ///A file whose UNITS does not say a size - a zero, a negative, an infinity, a NaN, or too few values to hold one -
    ///reads as nothing rather than as a guess, and each caller falls back to its own default from there: the DXF
    ///writer and the DRC report to a nanometer, the views and the assistant to a micron. Put in behind the
    ///encoder's back, because <see cref="GDS.NewLibrary"/> refuses every one of these, the encoder refuses the
    ///infinity and the NaN, and no file can carry the last three - so this is the one way such a value arrives.
    ///
    [Theory]
    [InlineData(new double[] { 0.001, 0 })]
    [InlineData(new double[] { 0.001, -1e-9 })]
    [InlineData(new double[] { 0.001, double.PositiveInfinity })]
    [InlineData(new double[] { 0.001, double.NaN })]
    [InlineData(new double[] { 0.001 })]
    public void A_unit_that_is_not_a_size_reads_as_nothing(double[] units)
    {
        var gds = GDS.NewLibrary();

        gds.StreamFormat.UNITS.Data = new Real8Data(units);

        Assert.Null(gds.MicronsPerDatabaseUnit);
    }

    ///<summary>A name left blank falls back, rather than writing a LIBNAME no reader is happy with.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_falls_back_rather_than_being_written_empty(string blank)
    {
        var gds = GDS.NewLibrary(blank, blank);

        Assert.Equal(new[] { "TOP" }, Hierarchy.Names(gds));
        Assert.Contains("LIBNAME: LIBRARY", gds.AsText());
    }

    ///
    ///**What comes out is a file**, which is the whole point of it going through FromRecords.
    ///
    ///Serialized, read back as bytes, and serialized again to the same bytes - the same round trip the corpus
    ///holds every real file to, applied to one this library made itself.
    ///
    [Fact]
    public void A_new_library_writes_and_reads_back_byte_for_byte()
    {
        var gds = GDS.NewLibrary("ROUND", "TRIP", stamp: new DateTime(2026, 1, 2, 3, 4, 5));

        byte[] written = gds.Serialize();
        var reopened = new GDS(written);

        Assert.Equal(new[] { "TRIP" }, Hierarchy.Names(reopened));
        Assert.Equal(written, reopened.Serialize());
    }

    ///<summary>The stamp goes where a reader looks for it, rather than the zero year a skeleton leaves.</summary>
    [Fact]
    public void The_time_it_was_made_is_written_into_the_library_and_the_cell()
    {
        var when = new DateTime(2026, 1, 2, 3, 4, 5);

        string text = GDS.NewLibrary("STAMPED", "CELL", stamp: when).AsText();

        Assert.Contains("BGNLIB: 2026 1 2 3 4 5 2026 1 2 3 4 5", text);
        Assert.Contains("BGNSTR: 2026 1 2 3 4 5 2026 1 2 3 4 5", text);
    }

    ///
    ///And it is somewhere to draw: the edits that add shapes take it as they take any other library.
    ///
    ///The check that matters for the readme's example, one step smaller - if a new library were structurally
    ///different from a read one in any way that mattered, this is where it would show.
    ///
    [Fact]
    public void A_new_library_takes_the_edits_that_draw_into_it()
    {
        var gds = GDS.NewLibrary();
        var top = Hierarchy.Named(gds, "TOP")!;

        var square = new[]
        {
            new Element.Point(0, 0),
            new Element.Point(100, 0),
            new Element.Point(100, 100),
            new Element.Point(0, 100)
        };

        new AddElement(gds, top, new LayerKey(68, 20), square).Apply();

        Assert.Single(GdsFlattener.Flatten(gds).Elements);
        Assert.True(gds.AdditionalInformation.Layers.ContainsKey(new LayerKey(68, 20)));

        //And it survives being written out, which a shape added to a library with no UNITS would not.
        Assert.Single(GdsFlattener.Flatten(new GDS(gds.Serialize())).Elements);
    }

    ///
    ///**A new library can start from a set of layers**, as the app's Set PDK does - and each comes
    ///in a color of its own, where one added alone comes in gray. Twenty rows of one gray would be a list nobody could
    ///tell apart, so the palette is divided again over the table.
    ///
    [Fact]
    public void Layers_added_as_a_set_each_take_a_color_of_their_own()
    {
        var table = GDS.NewLibrary().AdditionalInformation;
        var pairs = new[] { new LayerKey(68, 20), new LayerKey(66, 20), new LayerKey(67, 20) };

        Assert.Equal(3, table.AddLayers(pairs));

        var colors = pairs.Select(pair => table.Layers[pair].Color).ToList();

        Assert.Equal(3, colors.Distinct().Count());
        Assert.DoesNotContain(AdditionalGDSInformation.NewLayerColor, colors);

        //And a pair already listed is not added twice, or counted.
        Assert.Equal(1, table.AddLayers(new[] { new LayerKey(68, 20), new LayerKey(69, 20) }));
        Assert.Equal(4, table.Layers.Count);
    }

    ///<summary>A color somebody chose stays when a set arrives - a new layer is no reason to take one back.</summary>
    [Fact]
    public void A_chosen_color_is_kept_when_a_set_of_layers_arrives()
    {
        var table = GDS.NewLibrary().AdditionalInformation;

        table.AddLayer(new LayerKey(1, 0));
        table.Layers[new LayerKey(1, 0)].Color = "#123456";
        table.Layers[new LayerKey(1, 0)].ColorIsCustom = true;

        table.AddLayers(new[] { new LayerKey(2, 0), new LayerKey(3, 0) });

        Assert.Equal("#123456", table.Layers[new LayerKey(1, 0)].Color);
        Assert.NotEqual(AdditionalGDSInformation.NewLayerColor, table.Layers[new LayerKey(2, 0)].Color);
    }
}
