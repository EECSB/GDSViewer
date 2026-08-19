using GdsII;
using GDSViewer.Models;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Parses the real GDSII files bundled under wwwroot. These are the app's own example files - one
///hand-made MOSFET plus the SkyWater sky130 standard-cell libraries - so they double as a regression
///corpus: any change to the parser that breaks a real file shows up here.
///</summary>
public class SampleFileTests
{
    #region A known file **************************************************************

    [Fact]
    public void Mosfet_sample_reports_its_library_name()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        Assert.Equal("mosfet", ((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value);
    }

    ///<summary>
    ///Independent confirmation that the REAL8 decoder is right: layout tools write these units as
    ///1 micron user units over 1 nanometer database units, and nothing in the test suite produced
    ///these bytes.
    ///</summary>
    [Fact]
    public void Mosfet_sample_decodes_the_standard_micron_over_nanometer_units()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        double[] units = ((Real8Data)gds.StreamFormat.UNITS.Data!).Values;

        Assert.Equal(0.001, units[0], 1e-12);
        Assert.Equal(1e-9, units[1], 1e-18);
    }

    [Fact]
    public void Mosfet_sample_contains_structures_with_drawable_elements()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        Assert.NotEmpty(gds.StreamFormat.Structures);

        int drawable = gds.StreamFormat.Structures
            .SelectMany(structure => structure.Elements)
            .Count(element => element.Element is GDS.IHasLayer);

        Assert.True(drawable > 0, "Expected at least one element with a layer.");
    }

    [Fact]
    public void Mosfet_sample_discovers_layers_with_distinct_colors_and_stacked_offsets()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        var layers = gds.AdditionalInformation.Layers;

        Assert.NotEmpty(layers);
        Assert.Equal(layers.Count, layers.Values.Select(layer => layer.Color).Distinct().Count());

        //
        //By the same key the stacking walks - number *and* data type - which is what OrderedLayers does.
        //
        //This sorted on the number alone, and got away with it while every data type of one layer shared a
        //height: a stable sort left 68/20 ahead of 68/5 in discovery order and both had the same offset, so
        //nothing was out of sequence. One step per layer gives them 300 and 250, and reading them in that
        //order is descending. The test was under-specifying its sort rather than finding a real disorder.
        //
        var offsets = gds.AdditionalInformation.OrderedLayers().Select(entry => entry.Value.Offset).ToList();

        Assert.Equal(0, offsets[0]);
        Assert.Equal(offsets.OrderBy(offset => offset).ToList(), offsets);
    }

    [Fact]
    public void Sky130_standard_cell_parses_into_a_named_library()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.Sky130Sample("sky130_fd_sc_hd__nand2_1.gds")));

        Assert.False(string.IsNullOrWhiteSpace(((AsciiData)gds.StreamFormat.LIBNAME.Data!).Value));
        Assert.NotEmpty(gds.StreamFormat.Structures);
        Assert.NotEmpty(gds.AdditionalInformation.Layers);
    }

    #endregion ***********************************************************************



    #region The whole corpus **********************************************************

    [Fact]
    public void The_sample_corpus_is_present()
    {
        Assert.True(Directory.Exists(GdsTestData.SampleDirectory), $"Missing {GdsTestData.SampleDirectory}");
        Assert.NotEmpty(GdsTestData.AllSampleFiles());
    }

    ///<summary>
    ///Parses every bundled file and reports all failures at once rather than stopping at the first,
    ///so a parser regression shows its full blast radius.
    ///</summary>
    [Fact]
    public void Every_bundled_sample_file_parses()
    {
        var failures = new List<string>();
        int total = 0;

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            total++;

            try
            {
                var gds = new GDS(File.ReadAllBytes(path));

                Assert.NotNull(gds.StreamFormat);
            }
            catch (Exception ex)
            {
                failures.Add($"{Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        string report = $"{failures.Count} of {total} sample files failed to parse:\n{string.Join("\n", failures)}";

        Assert.True(failures.Count == 0, report);
    }

    ///<summary>
    ///Every real cell library has geometry, so a file that parses but yields nothing drawable means the
    ///structural walk silently dropped its elements.
    ///</summary>
    [Fact]
    public void Every_bundled_sample_file_yields_structures_and_layers()
    {
        var failures = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            var gds = new GDS(File.ReadAllBytes(path));

            if (gds.StreamFormat.Structures.Count == 0)
                failures.Add($"{Path.GetFileName(path)}: no structures");

            if (gds.AdditionalInformation.Layers.Count == 0)
                failures.Add($"{Path.GetFileName(path)}: no layers");
        }

        Assert.True(failures.Count == 0, $"Files with nothing to draw:\n{string.Join("\n", failures)}");
    }

    ///<summary>
    ///Every record the corpus contains must be a value the RecordType enum knows about; an unmapped
    ///type would silently fall through Record.setData and leave Data null.
    ///</summary>
    [Fact]
    public void Every_record_type_in_the_corpus_is_a_known_enum_value()
    {
        var unknown = new SortedSet<int>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            var gds = new GDS(File.ReadAllBytes(path));

            foreach (var record in gds.Records)
            {
                if (!Enum.IsDefined(typeof(RecordType), record.Type))
                    unknown.Add((short)record.Type);
            }
        }

        Assert.True(unknown.Count == 0, $"Unmapped record types: {string.Join(", ", unknown.Select(value => "0x" + value.ToString("X4")))}");
    }

    ///<summary>Records the record types the corpus actually exercises, as living documentation.</summary>
    [Fact]
    public void The_corpus_exercises_the_core_record_types()
    {
        var seen = new HashSet<RecordType>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            foreach (var record in new GDS(File.ReadAllBytes(path)).Records)
                seen.Add(record.Type);
        }

        Assert.Contains(RecordType.HEADER, seen);
        Assert.Contains(RecordType.BGNLIB, seen);
        Assert.Contains(RecordType.LIBNAME, seen);
        Assert.Contains(RecordType.UNITS, seen);
        Assert.Contains(RecordType.BGNSTR, seen);
        Assert.Contains(RecordType.STRNAME, seen);
        Assert.Contains(RecordType.BOUNDARY, seen);
        Assert.Contains(RecordType.LAYER, seen);
        Assert.Contains(RecordType.DATATYPE, seen);
        Assert.Contains(RecordType.XY, seen);
        Assert.Contains(RecordType.ENDEL, seen);
        Assert.Contains(RecordType.ENDSTR, seen);
        Assert.Contains(RecordType.ENDLIB, seen);
    }

    #endregion ***********************************************************************
}

