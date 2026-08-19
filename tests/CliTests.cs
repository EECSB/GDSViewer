using System.Globalization;
using GdsII;
using GdsII.Cli;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///<summary>
///Covers the command-line tool.
///
///Cli.Run takes its writers and returns an exit code rather than touching Console or Environment, so
///every command runs here without starting a process - which is what makes these worth having rather
///than a smoke test that shells out.
///
///The exit codes are part of the tool's contract: something scripting it branches on them, so they are
///asserted as deliberately as the output is.
///</summary>
public class CliTests : IDisposable
{
    private readonly StringWriter output = new StringWriter();
    private readonly StringWriter error = new StringWriter();
    private readonly List<string> temporaryFiles = new List<string>();

    private string Output
    {
        get { return output.ToString(); }
    }

    private string Error
    {
        get { return error.ToString(); }
    }

    private int Run(params string[] args)
    {
        return Cli.Run(args, output, error);
    }

    ///<summary>A path in the system's temporary directory, removed when the test finishes.</summary>
    private string TemporaryPath(string extension)
    {
        string path = Path.Combine(Path.GetTempPath(), $"gdscli-{Guid.NewGuid():N}{extension}");

        temporaryFiles.Add(path);

        return path;
    }

    public void Dispose()
    {
        foreach (string path in temporaryFiles)
        {
            try
            {
                File.Delete(path);
            }
            catch { }
        }
    }

    private static string Sample
    {
        get { return Path.Combine(GdsTestData.SampleDirectory, GdsTestData.MosfetSample); }
    }

    #region The command line itself ****************************************************

    [Fact]
    public void No_arguments_prints_the_usage_and_fails()
    {
        Assert.Equal(Cli.UsageError, Run());

        Assert.Contains("Usage:", Output);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    public void Help_is_a_success_rather_than_a_failure(string flag)
    {
        Assert.Equal(Cli.Ok, Run(flag));

        Assert.Contains("gds info", Output);
    }

    [Fact]
    public void The_version_is_reported()
    {
        Assert.Equal(Cli.Ok, Run("--version"));

        Assert.Equal(Cli.Version, Output.Trim());
    }

    ///<summary>Says what was not understood, rather than only that something was not.</summary>
    [Fact]
    public void An_unknown_command_names_it()
    {
        Assert.Equal(Cli.UsageError, Run("frobnicate", "x.gds"));

        Assert.Contains("frobnicate", Error);
    }

    [Fact]
    public void A_command_with_no_file_says_so()
    {
        Assert.Equal(Cli.UsageError, Run("info"));

        Assert.Contains("needs a file", Error);
    }

    [Fact]
    public void A_command_given_two_files_says_so_rather_than_using_the_first()
    {
        Assert.Equal(Cli.UsageError, Run("info", "one.gds", "two.gds"));

        Assert.Contains("takes one file", Error);
    }

    #region boolean and size *********************************************************

    ///<summary>
    ///The transistor gate, derived the way a PDK defines one: where polysilicon crosses diffusion.
    ///</summary>
    [Fact]
    public void Boolean_writes_the_derived_layer_alongside_the_rest()
    {
        string path = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("boolean", Sample, "--op", "and", "--a", "66/20", "--b", "65/20", "--into", "100/0", "-o", path));

        var written = new GDS(File.ReadAllBytes(path));
        var layers = written.AdditionalInformation.Layers.Keys.Select(key => key.ToString()).ToList();

        Assert.Contains("100/0", layers);

        //And what it was derived from is still there, which is what makes the result worth looking at.
        Assert.Contains("65/20", layers);
        Assert.Contains("66/20", layers);
    }

    [Fact]
    public void Boolean_can_write_the_result_on_its_own()
    {
        string path = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("boolean", Sample, "--op", "and", "--a", "66/20", "--b", "65/20", "--into", "100/0", "--only", "-o", path));

        var written = new GDS(File.ReadAllBytes(path));

        Assert.Equal(new[] { "100/0" }, written.AdditionalInformation.Layers.Keys.Select(key => key.ToString()).ToArray());
    }

    ///<summary>
    ///Sizing goes back onto the layer it came from unless told otherwise, which is what sizing means -
    ///deriving a second layer is what boolean is for.
    ///</summary>
    [Fact]
    public void Size_replaces_the_layer_it_read()
    {
        string path = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("size", Sample, "--a", "67/20", "--by", "-50", "-o", path));

        var written = new GDS(File.ReadAllBytes(path));

        Assert.Contains("67/20", written.AdditionalInformation.Layers.Keys.Select(key => key.ToString()));
    }

    [Theory]
    [InlineData("nand")]
    [InlineData("")]
    public void An_operation_that_is_not_one_of_the_four_is_named(string given)
    {
        Assert.Equal(Cli.UsageError, Run("boolean", Sample, "--op", given, "--a", "66/20", "--b", "65/20", "--into", "100/0", "-o", "x.gds"));

        Assert.Contains("and, or, not, xor", Error);
    }

    ///<summary>
    ///A bare layer number is fine for narrowing a drawing and not for this: an operation reads from
    ///exactly one pair and writes onto exactly one, so "65" would be a question rather than an answer.
    ///</summary>
    [Fact]
    public void A_bare_layer_number_is_refused_here()
    {
        Assert.Equal(Cli.UsageError, Run("boolean", Sample, "--op", "and", "--a", "66", "--b", "65/20", "--into", "100/0", "-o", "x.gds"));

        Assert.Contains("like 65/20", Error);
    }

    [Fact]
    public void Writing_binary_to_standard_output_is_refused()
    {
        Assert.Equal(Cli.UsageError, Run("boolean", Sample, "--op", "and", "--a", "66/20", "--b", "65/20", "--into", "100/0"));

        Assert.Contains("needs -o", Error);
    }

    ///<summary>
    ///The options that take a value are not counted as file names.
    ///
    ///They were: the list of which options take one was written where it was needed rather than in a
    ///place a new option would be added, so the first run of this reported "takes one file, but was
    ///given 5" about a command line with one file in it.
    ///</summary>
    [Fact]
    public void The_options_values_are_not_mistaken_for_files()
    {
        string path = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("boolean", Sample, "--op", "or", "--a", "66/20", "--b", "65/20", "--into", "100/0", "-o", path));

        Assert.DoesNotContain("takes one file", Error);
    }

    #endregion ***********************************************************************



    #region convert ******************************************************************

    ///<summary>
    ///Out to OASIS and back, with the same layout at both ends.
    ///
    ///Both directions in one test on purpose: a converter that is wrong in the same way twice is the one
    ///failure this cannot catch, and it is caught instead by the writer's own tests measuring against
    ///KLayout. What this covers is the wiring - that the verb reads either format, picks the right one to
    ///write, and puts the bytes where it was told.
    ///</summary>
    [Fact]
    public void Convert_goes_out_to_oasis_and_back()
    {
        string oasis = TemporaryPath(".oas");
        string back = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "-o", oasis));
        Assert.Equal(Cli.Ok, Run("convert", oasis, "-o", back));

        Assert.True(OasisReader.LooksLikeOasis(File.ReadAllBytes(oasis)));

        Assert.Equal(
            GdsTestData.Geometry(new GDS(File.ReadAllBytes(Sample))),
            GdsTestData.Geometry(new GDS(File.ReadAllBytes(back))));
    }

    ///<summary>
    ///The output's name decides the format, so nobody has to say it twice.
    ///
    ///Which also means the wrong name writes the wrong format silently - hence --to, and hence this
    ///asserting on what the bytes are rather than on what the file is called.
    ///</summary>
    [Fact]
    public void The_outputs_name_decides_the_format()
    {
        string named = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "-o", named));

        Assert.False(OasisReader.LooksLikeOasis(File.ReadAllBytes(named)));
        Assert.Contains("GDSII", Output);
    }

    [Fact]
    public void The_format_can_be_named_outright_whatever_the_file_is_called()
    {
        string misnamed = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "--to", "oas", "-o", misnamed));

        Assert.True(OasisReader.LooksLikeOasis(File.ReadAllBytes(misnamed)));
    }

    [Fact]
    public void A_format_that_is_not_one_of_the_three_is_named()
    {
        Assert.Equal(Cli.UsageError, Run("convert", Sample, "--to", "svg", "-o", TemporaryPath(".x")));

        Assert.Contains("gds, oas or dxf", Error);
    }

    ///<summary>DXF is one of the three now, so the same call that used to be refused writes a drawing.</summary>
    [Fact]
    public void Converting_to_dxf_writes_a_drawing()
    {
        string drawing = TemporaryPath(".dxf");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "--to", "dxf", "-o", drawing));

        Assert.True(DxfReader.LooksLikeDxf(File.ReadAllBytes(drawing)));

        //And says the one thing about the conversion somebody has to know before sending the file on.
        Assert.Contains("L<layer>D<datatype>", Output);
    }

    ///<summary>And the output's own name says it, the way it does for OASIS.</summary>
    [Fact]
    public void A_dxf_name_decides_the_format_without_being_told()
    {
        string drawing = TemporaryPath(".dxf");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "-o", drawing));

        Assert.True(DxfReader.LooksLikeDxf(File.ReadAllBytes(drawing)));
    }

    ///<summary>The drawing it writes opens again, which is the whole point of writing one.</summary>
    [Fact]
    public void A_drawing_it_wrote_converts_back()
    {
        string drawing = TemporaryPath(".dxf");
        string back = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "-o", drawing));
        Assert.Equal(Cli.Ok, Run("convert", drawing, "-o", back));

        var came = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(back)));
        var went = GdsFlattener.Flatten(new GDS(File.ReadAllBytes(Sample)));

        Assert.Equal(went.Elements.Count, came.Elements.Count);
    }

    [Fact]
    public void Converting_to_standard_output_is_refused()
    {
        Assert.Equal(Cli.UsageError, Run("convert", Sample));

        Assert.Contains("needs -o", Error);
    }

    ///<summary>
    ///The whole point of writing OASIS rather than GDSII. Half is a conservative floor - the bundled
    ///transistor comes out at 40% - and a number rather than "smaller" so a writer that quietly stopped
    ///packing anything would still fail this.
    ///</summary>
    [Fact]
    public void The_oasis_is_a_good_deal_smaller_than_the_gds()
    {
        string oasis = TemporaryPath(".oas");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "-o", oasis));

        long before = new FileInfo(Sample).Length;
        long after = new FileInfo(oasis).Length;

        Assert.True(after * 2 < before, $"{before} bytes of GDSII came to {after} bytes of OASIS");
    }

    ///<summary>
    ///Converting reads OASIS as happily as it writes it, which is the half that was already there - every
    ///command tells the two apart by what the file starts with rather than by what it is called.
    ///</summary>
    [Fact]
    public void An_oasis_file_can_be_read_by_any_command()
    {
        string oasis = TemporaryPath(".oas");

        Assert.Equal(Cli.Ok, Run("convert", Sample, "-o", oasis));
        Assert.Equal(Cli.Ok, Run("layers", oasis));

        Assert.Contains("65/20", Output);
    }

    #endregion ***********************************************************************

    ///<summary>A missing file is the file's problem, not the command line's, and the code says which.</summary>
    [Fact]
    public void A_missing_file_is_a_file_error()
    {
        Assert.Equal(Cli.FileError, Run("info", Path.Combine(Path.GetTempPath(), "no-such-file.gds")));

        Assert.Contains("no file at", Error);
    }

    [Fact]
    public void Something_that_is_not_gdsii_is_reported_rather_than_thrown()
    {
        string path = TemporaryPath(".gds");

        File.WriteAllText(path, "this is a sentence, not a layout");

        Assert.Equal(Cli.FileError, Run("info", path));

        Assert.Contains("fail", Error);
    }

    #endregion ***********************************************************************



    #region info **********************************************************************

    [Fact]
    public void Info_reports_what_the_file_holds()
    {
        Assert.Equal(Cli.Ok, Run("info", Sample));

        Assert.Contains("structures  1", Output);
        Assert.Contains("layers      9 layer/datatype pair(s)", Output);
        Assert.Contains("drawn       21 shape(s)", Output);
        Assert.Contains("labels      3", Output);
    }

    ///<summary>
    ///Invariant, like everything else this project writes as data: on a comma-decimal machine a database
    ///unit would otherwise print as 0,001, and somebody parsing this output would get a different number.
    ///</summary>
    [Fact]
    public void Info_writes_units_the_same_in_any_culture()
    {
        Run("info", Sample);

        string invariant = Output;

        var hostile = new StringWriter();
        GdsTestData.UnderHostileCulture(() => Cli.Run(new[] { "info", Sample }, hostile, error));

        Assert.Contains("0.001 user units", invariant);
        Assert.Equal(invariant, hostile.ToString());
    }

    #endregion ***********************************************************************



    #region dump and build ************************************************************

    [Fact]
    public void Dump_writes_one_record_per_line()
    {
        Assert.Equal(Cli.Ok, Run("dump", Sample));

        Assert.StartsWith("HEADER:", Output);
        Assert.Contains("\nENDLIB:", Output);
    }

    [Fact]
    public void Dump_writes_to_a_file_when_asked()
    {
        string path = TemporaryPath(".txt");

        Assert.Equal(Cli.Ok, Run("dump", Sample, "-o", path));

        Assert.Contains("HEADER:", File.ReadAllText(path));
        Assert.Equal("", Output);
    }

    ///<summary>
    ///The pair that makes the text format worth having: a file dumped and rebuilt is the same file, byte
    ///for byte. If the dump lost anything, this is where it would show.
    ///</summary>
    [Fact]
    public void A_file_dumped_and_rebuilt_is_the_same_file()
    {
        string text = TemporaryPath(".txt");
        string rebuilt = TemporaryPath(".gds");

        Assert.Equal(Cli.Ok, Run("dump", Sample, "-o", text));
        Assert.Equal(Cli.Ok, Run("build", text, "-o", rebuilt));

        Assert.Equal(File.ReadAllBytes(Sample), File.ReadAllBytes(rebuilt));
    }

    [Fact]
    public void Build_refuses_to_write_binary_to_standard_output()
    {
        string text = TemporaryPath(".txt");

        Run("dump", Sample, "-o", text);

        Assert.Equal(Cli.UsageError, Run("build", text));

        Assert.Contains("needs -o", Error);
    }

    [Fact]
    public void Build_reports_a_text_it_cannot_read_by_line()
    {
        string text = TemporaryPath(".txt");

        File.WriteAllText(text, "HEADER: 600 \nNONSENSE: 1 \n");

        Assert.Equal(Cli.FileError, Run("build", text, "-o", TemporaryPath(".gds")));

        Assert.Contains("not a readable record dump", Error);
    }

    #endregion ***********************************************************************



    #region validate ******************************************************************

    [Fact]
    public void Validate_passes_a_good_file()
    {
        Assert.Equal(Cli.Ok, Run("validate", Sample));

        Assert.Contains("ok", Output);
    }

    [Fact]
    public void Validate_fails_a_bad_one_and_says_which()
    {
        string bad = TemporaryPath(".gds");

        File.WriteAllBytes(bad, new byte[] { 0x00, 0x05, 0x0D, 0x02, 0x00 });

        Assert.Equal(Cli.FileError, Run("validate", bad));

        Assert.Contains("odd", Error);
    }

    ///<summary>
    ///A directory rather than a file, which is what makes this usable over a whole PDK. Reported as a
    ///total as well as a line each, so a run over hundreds does not have to be counted by hand.
    ///</summary>
    [Fact]
    public void Validate_searches_a_directory()
    {
        string directory = Path.Combine(GdsTestData.SampleDirectory, "Sky130 GDS");

        Assert.Equal(Cli.Ok, Run("validate", directory));

        Assert.Contains("897 of 897 read.", Output);
    }

    [Fact]
    public void Validate_with_no_path_says_so()
    {
        Assert.Equal(Cli.UsageError, Run("validate"));

        Assert.Contains("at least one", Error);
    }

    #endregion ***********************************************************************



    #region layers ********************************************************************

    [Fact]
    public void Layers_lists_the_pairs_with_what_is_on_them()
    {
        Assert.Equal(Cli.Ok, Run("layers", Sample));

        Assert.Contains("layer/datatype", Output);

        //Mosfet.gds draws on 65/20 and carries its labels on 68/5.
        Assert.Contains("65/20", Output);
        Assert.Contains("68/5", Output);
    }

    ///<summary>
    ///Area is behind a flag, because the covered figure is a clipping pass over every layer - which a
    ///quick look at what a file holds should not have to wait for.
    ///</summary>
    [Fact]
    public void Layers_leaves_area_out_unless_it_is_asked_for()
    {
        Assert.Equal(Cli.Ok, Run("layers", Sample));

        Assert.DoesNotContain("density", Output);
        Assert.DoesNotContain("covered", Output);
    }

    [Fact]
    public void Layers_reports_area_and_density_when_asked()
    {
        Assert.Equal(Cli.Ok, Run("layers", Sample, "--area"));

        Assert.Contains("drawn", Output);
        Assert.Contains("covered", Output);
        Assert.Contains("density", Output);

        //65/20 is one 575,000 square unit shape, which is the whole of its own bounding box.
        Assert.Matches(@"65/20\s+1\s+0\s+575,000\s+575,000\s+100\.0", Output);
    }

    #endregion ***********************************************************************



    #region The extent ***************************************************************

    ///<summary>
    ///How big the layout is and where, which the file does not record anywhere - it has to be measured
    ///off the geometry once the hierarchy is resolved.
    ///</summary>
    [Fact]
    public void Info_reports_the_extent_in_units_and_in_microns()
    {
        Assert.Equal(Cli.Ok, Run("info", Sample));

        Assert.Contains("extent      (-1350, 0) to (1450, 1500)", Output);

        //The unit anybody in this field thinks in. 2.8 by 1.5 um is the right order for a transistor.
        Assert.Contains("2800 x 1500 database units, 2.8 x 1.5 um", Output);
    }

    ///<summary>
    ///A file whose UNITS cannot be believed gets the size in database units and no invented microns. A
    ///made-up scale is worse than none, because a number with a unit on it gets quoted.
    ///</summary>
    [Fact]
    public void An_unusable_unit_costs_the_microns_and_not_the_extent()
    {
        string path = TemporaryPath(".gds");

        File.WriteAllBytes(path, GdsTestData.Concat(
            GdsTestData.Record(GDS.Record.RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(GDS.Record.RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(GDS.Record.RecordType.LIBNAME, GdsTestData.Ascii("NOUNITS")),
            GdsTestData.Record(GDS.Record.RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0), GdsTestData.Real8(0))),
            GdsTestData.Record(GDS.Record.RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(GDS.Record.RecordType.STRNAME, GdsTestData.Ascii("C")),
            GdsTestData.Record(GDS.Record.RecordType.BOUNDARY),
            GdsTestData.Record(GDS.Record.RecordType.LAYER, GdsTestData.Int2(1)),
            GdsTestData.Record(GDS.Record.RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(GDS.Record.RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(400))),
            GdsTestData.Record(GDS.Record.RecordType.ENDEL),
            GdsTestData.Record(GDS.Record.RecordType.ENDSTR),
            GdsTestData.Record(GDS.Record.RecordType.ENDLIB)));

        Assert.Equal(Cli.Ok, Run("info", path));

        Assert.Contains("400 x 400 database units", Output);

        //
        //And nothing after it, which is where the microns would be - see the test above, where the same
        //line reads "2800 x 1500 database units, 2.8 x 1.5 um".
        //
        //**Not DoesNotContain("um"), which is what this was.** Two letters match any word that happens to
        //contain them, so it failed the moment `info` gained a line with "number" in it - on the prose,
        //rather than on anything to do with units.
        //
        Assert.DoesNotContain("400 x 400 database units,", Output);
    }

    #endregion ***********************************************************************



    #region svg ***********************************************************************

    [Fact]
    public void Svg_wraps_the_markup_in_a_document()
    {
        Assert.Equal(Cli.Ok, Run("svg", Sample));

        Assert.StartsWith("<svg xmlns=", Output);
        Assert.Contains("viewBox=", Output);
        Assert.Contains("<path", Output);
        Assert.EndsWith("</svg>\n", Output);
    }

    ///<summary>
    ///GDSII counts Y upward and SVG counts it down, so the markup is flipped about the middle of the
    ///viewBox. Checked by arithmetic rather than by eye: the transform has to map the top of the box to
    ///the bottom of it, and getting the sign or the center wrong renders the layout upside down - which
    ///looks plausible enough in a thumbnail to go unnoticed.
    ///</summary>
    [Fact]
    public void Svg_flips_y_about_the_middle_of_the_view_box()
    {
        Run("svg", Sample);

        var box = viewBoxOf(Output);
        double translate = translateYOf(Output);

        //y' = translate - y. The top of the box has to land on the bottom, and the bottom on the top.
        Assert.Equal(box.Bottom, translate - box.Top, 3);
        Assert.Equal(box.Top, translate - box.Bottom, 3);
    }

    [Fact]
    public void Svg_can_leave_the_labels_out()
    {
        Run("svg", Sample);
        Assert.Contains("<text", Output);

        var without = new StringWriter();
        Cli.Run(new[] { "svg", Sample, "--no-labels" }, without, error);

        Assert.DoesNotContain("<text", without.ToString());
        Assert.Contains("<path", without.ToString());
    }

    [Fact]
    public void Svg_takes_an_opacity()
    {
        Assert.Equal(Cli.Ok, Run("svg", Sample, "--opacity", "0.25"));

        //In the stylesheet the writer emits rather than on each shape; see SvgWriter.appendStyle.
        Assert.Contains("opacity:0.25", Output);
    }

    [Fact]
    public void Svg_refuses_an_opacity_that_is_not_a_number()
    {
        Assert.Equal(Cli.UsageError, Run("svg", Sample, "--opacity", "quite-a-lot"));

        Assert.Contains("not an opacity", Error);
    }

    private static (double Top, double Bottom) viewBoxOf(string svg)
    {
        string[] numbers = between(svg, "viewBox=\"", "\"").Split(' ');

        double y = double.Parse(numbers[1], CultureInfo.InvariantCulture);
        double height = double.Parse(numbers[3], CultureInfo.InvariantCulture);

        return (y, y + height);
    }

    private static double translateYOf(string svg)
    {
        string translate = between(svg, "transform=\"translate(0,", ")");

        return double.Parse(translate, CultureInfo.InvariantCulture);
    }

    private static string between(string text, string start, string end)
    {
        int from = text.IndexOf(start, StringComparison.Ordinal) + start.Length;
        int to = text.IndexOf(end, from, StringComparison.Ordinal);

        return text[from..to];
    }

    #endregion ***********************************************************************



    #region model *********************************************************************

    [Theory]
    [InlineData(".stl")]
    [InlineData(".obj")]
    [InlineData(".gltf")]
    [InlineData(".glb")]
    public void Model_writes_each_format_it_offers(string extension)
    {
        string path = TemporaryPath(extension);

        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", path));

        Assert.True(File.Exists(path));
        Assert.Contains("triangles across", Output);
    }

    ///<summary>The format comes from the extension, so an unknown one has to be refused rather than guessed.</summary>
    [Fact]
    public void Model_refuses_a_format_it_does_not_write()
    {
        Assert.Equal(Cli.UsageError, Run("model", Sample, "-o", TemporaryPath(".dwg")));

        Assert.Contains(".dwg", Error);
    }

    [Fact]
    public void Model_needs_an_output_file_since_that_is_where_the_format_comes_from()
    {
        Assert.Equal(Cli.UsageError, Run("model", Sample));

        Assert.Contains("needs -o", Error);
    }

    ///<summary>
    ///An OBJ carries no colors of its own, so the layer colors go in a .mtl beside it. That is a second
    ///file the caller did not name, which is worth saying out loud rather than leaving to be discovered.
    ///</summary>
    [Fact]
    public void Model_writes_a_material_file_beside_an_obj_and_says_so()
    {
        string path = TemporaryPath(".obj");
        string materials = Path.ChangeExtension(path, ".mtl");

        temporaryFiles.Add(materials);

        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", path));

        Assert.True(File.Exists(materials));
        Assert.Contains(materials, Output);
        Assert.Contains("mtllib", File.ReadAllText(path));
        Assert.Contains("newmtl", File.ReadAllText(materials));
    }

    [Fact]
    public void Model_can_leave_the_material_file_out()
    {
        string path = TemporaryPath(".obj");

        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", path, "--no-mtl"));

        Assert.False(File.Exists(Path.ChangeExtension(path, ".mtl")));
        Assert.DoesNotContain("mtllib", File.ReadAllText(path));
    }

    ///<summary>
    ///Binary by default because a layout runs to a lot of triangles, with the text form on request - which
    ///is what the 3D view's own download writes. The same geometry either way, which is the part worth
    ///pinning: it would be easy for one encoder to drop or double a facet without the other noticing.
    ///</summary>
    [Fact]
    public void An_ascii_stl_holds_the_same_facets_as_the_binary_one()
    {
        string binary = TemporaryPath(".stl");
        string text = TemporaryPath(".stl");

        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", binary));
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", text, "--ascii"));

        int inBinary = BitConverter.ToInt32(File.ReadAllBytes(binary), 80);
        int inText = File.ReadAllText(text).Split("facet normal").Length - 1;

        Assert.Equal(inBinary, inText);
        Assert.True(inBinary > 0);

        //And the text form really is text, rather than the binary writer having been called by mistake.
        Assert.StartsWith("solid ", File.ReadAllText(text));
    }

    ///<summary>
    ///Every index an OBJ face names has to exist. Indices are one-based and count across the whole file
    ///rather than restarting at each object, which is the single easiest thing to get wrong in this format
    ///- and it fails by drawing the later layers out of the earlier layers' vertices, which still opens.
    ///</summary>
    [Fact]
    public void Obj_face_indices_stay_inside_the_file()
    {
        string path = TemporaryPath(".obj");

        Run("model", Sample, "-o", path, "--no-mtl");

        int vertices = 0;
        int normals = 0;
        int faces = 0;

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.StartsWith("v ", StringComparison.Ordinal))
                vertices++;

            if (line.StartsWith("vn ", StringComparison.Ordinal))
                normals++;

            if (!line.StartsWith("f ", StringComparison.Ordinal))
                continue;

            faces++;

            foreach (string corner in line[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = corner.Split("//");

                Assert.InRange(int.Parse(parts[0], CultureInfo.InvariantCulture), 1, vertices);
                Assert.InRange(int.Parse(parts[1], CultureInfo.InvariantCulture), 1, normals);
            }
        }

        Assert.True(faces > 0);
    }

    ///<summary>
    ///Reads a written GLB back with the same library that wrote it, which parses and validates it the way
    ///anything opening the file would. Writing a header something refuses is otherwise invisible from this
    ///side: the command reports a triangle count either way and the file looks the right size.
    ///</summary>
    [Fact]
    public void A_written_glb_reads_back_as_the_same_model()
    {
        string path = TemporaryPath(".glb");

        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", path));

        var model = SharpGLTF.Schema2.ModelRoot.Load(path);

        int triangles = model.LogicalMeshes
            .SelectMany(mesh => mesh.Primitives)
            .Sum(primitive => primitive.GetTriangleIndices().Count());

        //One mesh per layer, and Mosfet.gds has geometry on all nine of its layer/datatype pairs - 68/5
        //carries three labels as well as three shapes, so it is a mesh like the rest.
        Assert.Equal(9, model.LogicalMeshes.Count);

        Assert.Contains($"{triangles} triangles across 9 layer(s)", Output);
    }

    #endregion ***********************************************************************



    #region Choosing layers ***********************************************************

    ///<summary>
    ///Mosfet.gds draws on nine layer/datatype pairs, which is what the counts below are measured against.
    ///</summary>
    [Fact]
    public void Everything_is_drawn_when_no_layer_is_named()
    {
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", TemporaryPath(".glb")));

        Assert.Contains("across 9 layer(s)", Output);
    }

    [Fact]
    public void Only_the_named_layers_are_drawn()
    {
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", TemporaryPath(".glb"), "--layers", "66/44"));

        Assert.Contains("across 1 layer(s)", Output);
    }

    ///<summary>
    ///A bare number is the whole layer. Mosfet.gds puts geometry on both 66/20 and 66/44, so "66" is two
    ///of them where "66/44" is one - which is the distinction the app's sidebar makes with a checkbox per
    ///pair, and the reason the numbers alone were not enough to key a layer on.
    ///</summary>
    [Fact]
    public void A_bare_layer_number_takes_every_data_type_on_it()
    {
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", TemporaryPath(".glb"), "--layers", "66"));

        Assert.Contains("across 2 layer(s)", Output);
    }

    [Fact]
    public void Hiding_a_layer_leaves_the_rest()
    {
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", TemporaryPath(".glb"), "--hide", "66"));

        Assert.Contains("across 7 layer(s)", Output);
    }

    ///<summary>Named first, then taken away, so the two can narrow together.</summary>
    [Fact]
    public void Naming_and_hiding_apply_in_that_order()
    {
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", TemporaryPath(".glb"), "--layers", "66,67", "--hide", "66/20"));

        //66/44, 67/20 and 67/44 - four pairs named, one of them taken back out.
        Assert.Contains("across 3 layer(s)", Output);
    }

    ///<summary>
    ///A layer the file does not have is reported and carried on from rather than refused. Refusing would
    ///stop a run over a directory at the first cell that happens not to use one, which is the case this
    ///option is most useful in.
    ///</summary>
    [Fact]
    public void A_layer_the_file_does_not_have_is_named_but_not_fatal()
    {
        Assert.Equal(Cli.Ok, Run("model", Sample, "-o", TemporaryPath(".glb"), "--layers", "66/44,999/1"));

        Assert.Contains("nothing on 999/1", Error);
        Assert.Contains("across 1 layer(s)", Output);
    }

    ///<summary>Something that is not a layer at all is the command line being wrong, which is different.</summary>
    [Fact]
    public void Something_that_is_not_a_layer_is_a_usage_error()
    {
        Assert.Equal(Cli.UsageError, Run("model", Sample, "-o", TemporaryPath(".glb"), "--layers", "metal1"));

        Assert.Contains("is not a layer", Error);
    }

    ///<summary>
    ///The list is an option's value, not a second file. Without that the parser reads "66/44" as a path
    ///and the command refuses two files - which is a confusing way to be told about a missing option.
    ///</summary>
    [Fact]
    public void A_layer_list_is_not_mistaken_for_a_second_file()
    {
        Assert.Equal(Cli.Ok, Run("svg", Sample, "--layers", "66/44"));

        Assert.DoesNotContain("takes one file", Error);
        Assert.Contains("<path", Output);
    }

    [Fact]
    public void Svg_draws_only_the_named_layers()
    {
        Run("svg", Sample);

        //One path per layer drawn, which is what the picture is made of - see SvgWriter.
        int all = Output.Split("<path").Length - 1;

        var some = new StringWriter();
        Cli.Run(new[] { "svg", Sample, "--layers", "66/44" }, some, error);

        Assert.Equal(1, some.ToString().Split("<path").Length - 1);
        Assert.True(all > 1);
    }

    ///<summary>A label is on a layer like anything else, so hiding that layer takes the labels with it.</summary>
    [Fact]
    public void Hiding_a_layer_hides_its_labels_too()
    {
        Run("svg", Sample);
        Assert.Contains("<text", Output);

        var without = new StringWriter();
        Cli.Run(new[] { "svg", Sample, "--hide", "68/5" }, without, error);

        Assert.DoesNotContain("<text", without.ToString());
    }

    ///<summary>
    ///The bounds follow what is left rather than the whole file, so a single layer fills the picture
    ///instead of sitting in a frame sized for layers that were not drawn.
    ///</summary>
    [Fact]
    public void The_view_box_follows_the_layers_that_are_left()
    {
        Run("svg", Sample);
        string all = between(Output, "viewBox=\"", "\"");

        var some = new StringWriter();
        Cli.Run(new[] { "svg", Sample, "--layers", "66/44" }, some, error);

        Assert.NotEqual(all, between(some.ToString(), "viewBox=\"", "\""));
    }

    #endregion ***********************************************************************



    #region Layermaps *****************************************************************

    ///<summary>
    ///Writes a mapping to a temporary file and hands back its path, so a test can say what it wants a layer
    ///to mean rather than depending on a file in the repository.
    ///</summary>
    private string LayerMap(string rows)
    {
        string path = TemporaryPath(".csv");

        File.WriteAllText(path, rows);

        return path;
    }

    ///
    ///**The one thing the app could do that this could not.**
    ///
    ///Nothing in a GDSII file says what 65/20 means, so the names, the real colors and the real process stack
    ///all come from a file the user supplies. The library has carried LayerNames the whole time - anything
    ///referencing it could load one - and only the command line had no way to hand one over, which made the
    ///tool a worse citizen of its own library than the web app was.
    ///
    [Fact]
    public void Layers_names_the_pairs_a_mapping_names()
    {
        string map = LayerMap("65,20,diff\n66,20,poly\n");

        Assert.Equal(Cli.Ok, Run("layers", Sample, "--layermap", map));

        Assert.Contains("diff (65/20)", Output);
        Assert.Contains("poly (66/20)", Output);

        //The numbers stay visible beside the name, the way the app's own layer list keeps them: a name is
        //somebody's mapping where the numbers are what the file says, so a wrong mapping shows as a
        //disagreement rather than as a plausible word.
        Assert.Contains("65/20", Output);
    }

    ///<summary>And without one, the pairs are all there is - which is what the tool did before.</summary>
    [Fact]
    public void Layers_without_a_mapping_prints_the_pairs_alone()
    {
        Assert.Equal(Cli.Ok, Run("layers", Sample));

        Assert.Contains("65/20", Output);
        Assert.DoesNotContain("diff", Output);
    }

    ///<summary>A mapping's colors reach the SVG, which is the whole reason to hand one to `svg`.</summary>
    [Fact]
    public void A_mapping_colors_the_svg()
    {
        string map = LayerMap("65,20,diff,#0a0b0c\n");

        var plain = new StringWriter();
        Cli.Run(new[] { "svg", Sample }, plain, new StringWriter());

        Assert.Equal(Cli.Ok, Run("svg", Sample, "--layermap", map));

        Assert.Contains("#0a0b0c", Output);
        Assert.DoesNotContain("#0a0b0c", plain.ToString());
    }

    ///
    ///**The report goes to standard error for `svg`, because standard output is the file.**
    ///
    ///`gds svg cell.gds --layermap m.csv > cell.svg` is the obvious way to use this, and a line of prose ahead
    ///of the markup would be a line of prose inside the SVG.
    ///
    [Fact]
    public void The_svg_itself_carries_no_word_about_the_mapping()
    {
        string map = LayerMap("65,20,diff\n");

        Assert.Equal(Cli.Ok, Run("svg", Sample, "--layermap", map));

        Assert.StartsWith("<svg", Output.TrimStart());
        Assert.DoesNotContain("named from", Output);
        Assert.Contains("named from", Error);
    }

    ///
    ///**A height in the mapping is a height in the model, and --spacing does not overwrite it.**
    ///
    ///This is what turns `gds model` from evenly spaced planes into the shape of an actual wafer. A mapping's
    ///stack columns set StackIsCustom and SetStackingOffsets steps past a layer that carries it, so the order
    ///matters: place what was placed, then space out the rest.
    ///
    [Fact]
    public void A_mapping_places_a_layer_where_the_model_puts_it()
    {
        string map = LayerMap("65,20,diff,,4000,120\n");
        string path = TemporaryPath(".stl");

        Assert.Equal(Cli.Ok, Run("model", Sample, "--layermap", map, "--spacing", "50", "-o", path));

        var gds = new GDS(File.ReadAllBytes(Sample));

        LayerNames.Parse(File.ReadAllText(map)).ApplyTo(gds.AdditionalInformation.Layers);
        gds.AdditionalInformation.SetStackingOffsets(50);

        //The placed layer kept the height it was given rather than being spaced over.
        Assert.Equal(4000, gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Offset);
        Assert.Equal(120, gds.AdditionalInformation.Layers[new LayerKey(65, 20)].Depth);

        //And every other layer was still spaced out, so the option places rather than freezes.
        Assert.Contains(
            gds.AdditionalInformation.Layers.Where(entry => entry.Key != new LayerKey(65, 20)),
            entry => entry.Value.Offset != 4000);
    }

    ///<summary>A row that cannot be read is named by line, and the readable rows still apply.</summary>
    [Fact]
    public void A_bad_row_is_reported_and_the_rest_still_applies()
    {
        string map = LayerMap("65,20,diff\nnonsense\n66,20,poly\n");

        Assert.Equal(Cli.Ok, Run("layers", Sample, "--layermap", map));

        Assert.Contains("Line 2", Error);
        Assert.Contains("diff (65/20)", Output);
        Assert.Contains("poly (66/20)", Output);
    }

    ///
    ///A mapping matching nothing is said out loud rather than passing quietly.
    ///
    ///Rows matching nothing is normal - a mapping covers a whole PDK where a file uses a handful of layers -
    ///but *zero* matching means the wrong technology or the columns the wrong way round, and silence there
    ///reads as the option having worked.
    ///
    [Fact]
    public void A_mapping_for_another_technology_says_so()
    {
        string map = LayerMap("900,20,nothing.here\n");

        Assert.Equal(Cli.Ok, Run("layers", Sample, "--layermap", map));

        Assert.Contains("says nothing about any layer this file uses", Output);
    }

    [Fact]
    public void A_layermap_that_is_not_there_is_a_file_error_rather_than_a_crash()
    {
        Assert.Equal(Cli.UsageError, Run("layers", Sample, "--layermap", "no-such-map.csv"));

        Assert.Contains("Could not read the layermap", Error);
    }

    ///<summary>
    ///And the other direction: this file's own layers as a mapping to edit, which is the app's Export.
    ///
    ///Every column filled in, because the point is a file to type names into rather than a blank page - the
    ///same reason the app's export stopped writing only the placed heights.
    ///</summary>
    [Fact]
    public void Layers_can_write_the_file_its_own_mapping_would_start_from()
    {
        string path = TemporaryPath(".csv");

        Assert.Equal(Cli.Ok, Run("layers", Sample, "--write-layermap", path));

        string written = File.ReadAllText(path);

        Assert.StartsWith("#layer,datatype,name,color", written);

        var read = LayerNames.Parse(written);

        Assert.Empty(read.Problems);

        //Every pair the file has, and each with the color and the stack it is currently drawn at.
        var gds = new GDS(File.ReadAllBytes(Sample));

        foreach (var layer in gds.AdditionalInformation.OrderedLayers())
        {
            Assert.True(read.Colors.ContainsKey(layer.Key), $"{layer.Key} has no color");
            Assert.True(read.Stack.ContainsKey(layer.Key), $"{layer.Key} has no stack");
        }
    }

    ///<summary>Written round trip: what it hands out, it reads back and names the layers with.</summary>
    [Fact]
    public void A_written_mapping_reads_back_into_the_same_tool()
    {
        string path = TemporaryPath(".csv");

        Assert.Equal(Cli.Ok, Run("layers", Sample, "--write-layermap", path));

        //A name typed into it, the way somebody would.
        File.WriteAllText(path, File.ReadAllText(path).Replace("65,20,,", "65,20,diff,"));

        var second = new StringWriter();

        Assert.Equal(Cli.Ok, Cli.Run(new[] { "layers", Sample, "--layermap", path }, second, error));

        Assert.Contains("diff (65/20)", second.ToString());
    }

    #endregion ***********************************************************************



    #region cells *********************************************************************

    [Fact]
    public void Cells_lists_the_library_and_marks_what_nothing_places()
    {
        Assert.Equal(Cli.Ok, Run("cells", Sample));

        Assert.Contains("mosfet", Output);

        //A cell nothing places is what the flattener draws on its own, which is the answer to "which of
        //these is the layout" and the one thing a bare list of names does not say.
        Assert.Contains("top", Output);
        Assert.Contains("1 cell(s), 1 of them placed by nothing.", Output);
    }

    ///
    ///A three-deep library where one cell is shared by two *different* parents.
    ///
    ///**That distinction is the whole fixture.** The first version of this had ROW place LEAF twice and
    ///expected two LEAF rows, which is not what the library does: `Hierarchy.Tree` deduplicates a parent's
    ///children by name deliberately - "a cell placed four times by one parent is one child of it", and the
    ///count of placements is already on the row. So a repeat is a cell reached from somewhere *else*, which
    ///is what TOP placing LEAF directly as well as through ROW gives.
    ///
    private string NestedFile()
    {
        byte[] stamps = GdsTestData.Timestamps();

        var records = new List<byte[]>
        {
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("NESTED")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(65)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(100))),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("ROW")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(new[] { 0, 0 })),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),

            //TOP places ROW *and* LEAF, so LEAF is reached down two different paths - which is what a repeat
            //is. Twice from one parent would be one row, deliberately.
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("TOP")),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("ROW")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(new[] { 0, 0 })),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii("LEAF")),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(new[] { 0, 800 })),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.ENDLIB)
        };

        string path = TemporaryPath(".gds");

        File.WriteAllBytes(path, GdsTestData.Concat(records.ToArray()));

        return path;
    }

    ///
    ///**A cell placed twice is listed twice, and marked the second time.**
    ///
    ///Which is where this parts company with a folder tree: a directory is in one place and a GDS cell is
    ///genuinely shared, so showing it once would mean picking a parent to call the real one. The alternative
    ///reads as "there are two of these" rather than "this is the same cell again".
    ///
    [Fact]
    public void The_tree_indents_each_cell_under_what_places_it()
    {
        Assert.Equal(Cli.Ok, Run("cells", NestedFile(), "--tree"));

        string[] rows = Output
            .Split('\n')
            .Where(row => row.Contains("element(s)"))
            .ToArray();

        Assert.Equal(4, rows.Length);

        //TOP at the left, ROW one in, LEAF two in under ROW - then LEAF again one in, where TOP places it
        //directly. The indent is what says which path each was reached by.
        Assert.StartsWith("TOP", rows[0]);
        Assert.StartsWith("  ROW", rows[1]);
        Assert.StartsWith("    LEAF", rows[2]);
        Assert.StartsWith("  LEAF", rows[3]);

        //Only the second one is marked, so a reader can tell "the same cell again" from "two of these".
        Assert.DoesNotContain("(again)", rows[2]);
        Assert.Contains("(again)", rows[3]);
    }

    ///<summary>And the flat list counts placements without indenting anything.</summary>
    [Fact]
    public void The_flat_list_says_what_places_each_cell()
    {
        Assert.Equal(Cli.Ok, Run("cells", NestedFile()));

        //LEAF is placed by two, ROW by one, TOP by nothing.
        Assert.Contains("3 cell(s), 1 of them placed by nothing.", Output);
        Assert.DoesNotContain("(again)", Output);
    }

    [Fact]
    public void Cells_needs_a_file()
    {
        Assert.Equal(Cli.UsageError, Run("cells"));

        Assert.Contains("cells needs a file.", Error);
    }

    #endregion ***********************************************************************



    #region nets **********************************************************************

    ///<summary>The roles the bundled sky130 mapping gives, which is what makes a trace possible at all.</summary>
    private string Sky130Roles
    {
        get { return Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", "resources", "GDS Files", "sky130-roles.csv"); }
    }

    ///
    ///**A real net, walked out of a real file.**
    ///
    ///`-700,850` is inside one of Mosfet.gds's li1 wires. Tracing it reaches the mcon above it and the met1
    ///above that - three layers, which is the whole point: a net that climbs is a different answer from one
    ///that stops, and a shape count alone would not say which happened.
    ///
    [Fact]
    public void Nets_walks_a_net_up_through_its_vias()
    {
        Assert.Equal(Cli.Ok, Run("nets", Sample, "--layermap", Sky130Roles, "--at", "-700,850"));

        Assert.Contains("Traced from li1 (67/20) at -700,850.", Output);

        //Up through the contact into the metal above it.
        Assert.Contains("li1 (67/20)", Output);
        Assert.Contains("mcon (67/44)", Output);
        Assert.Contains("met1 (68/20)", Output);

        Assert.Contains("3 shape(s) across 3 layer(s).", Output);
    }

    ///<summary>And the label on it, which is what somebody checking a net is the net they think it is wants.</summary>
    [Fact]
    public void Nets_reports_the_name_sitting_on_it()
    {
        Assert.Equal(Cli.Ok, Run("nets", Sample, "--layermap", Sky130Roles, "--at", "-700,850"));

        Assert.Contains("Named source.", Output);
    }

    ///
    ///**Without a layermap the answer is that the question cannot be asked**, not that the net is empty.
    ///
    ///This is the failure the command will hit most often. Nothing in a GDSII file records which of its
    ///numbers carry a net, so a bare file has no roles and the walk has nothing to follow - and an empty net
    ///reported as a result would read as "this wire connects to nothing", which is a different and wrong
    ///answer. The app greys its own button out for the same reason.
    ///
    [Fact]
    public void Without_roles_nets_says_the_question_cannot_be_asked()
    {
        Assert.Equal(Cli.UsageError, Run("nets", Sample, "--at", "-700,850"));

        Assert.Contains("No layer in this file has a role", Error);
        Assert.Contains("--layermap", Error);

        //And it does not report a net of its own.
        Assert.DoesNotContain("Traced from", Output);
    }

    [Fact]
    public void Nets_needs_a_point()
    {
        Assert.Equal(Cli.UsageError, Run("nets", Sample, "--layermap", Sky130Roles));

        Assert.Contains("--at", Error);
    }

    ///<summary>
    ///A point is two whole numbers of database units. A fractional one is somebody thinking in microns, and
    ///rounding it silently would trace from a neighbouring shape and answer a different question.
    ///</summary>
    [Theory]
    [InlineData("1200")]
    [InlineData("1200,800,4")]
    [InlineData("1.5,800")]
    [InlineData("here,there")]
    [InlineData(",")]
    public void A_point_that_is_not_two_whole_numbers_is_refused(string given)
    {
        Assert.Equal(Cli.UsageError, Run("nets", Sample, "--layermap", Sky130Roles, "--at", given));

        Assert.Contains("is not a point", Error);
    }

    ///<summary>A negative coordinate is ordinary - the sample's own wires are at negative X.</summary>
    [Fact]
    public void A_negative_coordinate_is_a_coordinate()
    {
        Assert.Equal(Cli.Ok, Run("nets", Sample, "--layermap", Sky130Roles, "--at", "-700,850"));

        Assert.Contains("Traced from", Output);
    }

    [Fact]
    public void A_point_on_nothing_is_reported_as_such()
    {
        Assert.Equal(Cli.FileError, Run("nets", Sample, "--layermap", Sky130Roles, "--at", "999999,999999"));

        Assert.Contains("Nothing is drawn at 999999,999999.", Error);
    }

    ///
    ///A shape on a layer with no role carries no net, which is not the same as the file having no roles.
    ///
    ///nsdm is an implant layer: it is drawn, it is in the mapping, and it is deliberately given no role. The
    ///two cases have to read differently or "this layer takes no part" and "nothing is attached" are one
    ///message.
    ///
    [Fact]
    public void A_shape_on_a_roleless_layer_carries_no_net()
    {
        //Inside nsdm (93/44) and outside every conductor - which took finding: nsdm spans -725..675 by
        //475..1225 and most of that has a wire over it, and Picking answers with whatever is drawn last.
        Assert.Equal(Cli.Ok, Run("nets", Sample, "--layermap", Sky130Roles, "--at", "-720,480"));

        Assert.Contains("is on nsdm (93/44), which has no role, so it carries no net", Output);
        Assert.DoesNotContain("shape(s) across", Output);
    }

    ///<summary>--shapes lists the net by index, for feeding into something else.</summary>
    [Fact]
    public void The_shapes_of_a_net_can_be_listed()
    {
        Assert.Equal(Cli.Ok, Run("nets", Sample, "--layermap", Sky130Roles, "--at", "-700,850", "--shapes"));

        Assert.Contains("index   layer            points", Output);

        //Three rows for the three shapes, ordered by index.
        var listed = Output
            .Split('\n')
            .SkipWhile(row => !row.Contains("index   layer"))
            .Skip(1)
            .Where(row => row.Trim().Length > 0)
            .ToArray();

        Assert.Equal(3, listed.Length);
    }

    #endregion ***********************************************************************



    #region measure *******************************************************************

    ///
    ///**The same numbers the 2D view's ruler puts on screen.**
    ///
    ///300 by 400 is 500, which is the case `jstests/viewGeometry.test.js` pins for the ruler itself - chosen
    ///the same way in both places so the two are held to one contract rather than to whatever each happens
    ///to compute. A measurement here that disagreed with the one in the app would be worse than none.
    ///
    [Fact]
    public void Measure_is_the_distance_between_two_points()
    {
        Assert.Equal(Cli.Ok, Run("measure", Sample, "--from", "0,0", "--to", "300,400"));

        Assert.Contains("dx 300, dy 400", Output);
        Assert.Contains("500.00 units", Output);
    }

    ///<summary>
    ///A database unit is a nanometer in every bundled file, so 500 of them is half a micron - and the figure
    ///comes off the file's own UNITS rather than from an assumption about what a unit usually is.
    ///</summary>
    [Fact]
    public void Measure_converts_through_the_files_own_units()
    {
        Assert.Equal(Cli.Ok, Run("measure", Sample, "--from", "0,0", "--to", "300,400"));

        Assert.Contains("(0.5000 µm)", Output);
    }

    ///<summary>
    ///The other way round is the same distance and the opposite deltas, which is the ruler's own rule.
    ///</summary>
    [Fact]
    public void Measuring_back_gives_the_same_distance_and_opposite_deltas()
    {
        Assert.Equal(Cli.Ok, Run("measure", Sample, "--from", "400,600", "--to", "100,200"));

        Assert.Contains("dx -300, dy -400", Output);
        Assert.Contains("500.00 units", Output);
    }

    ///<summary>A point against itself is zero rather than a division by anything.</summary>
    [Fact]
    public void Measuring_a_point_against_itself_is_zero()
    {
        Assert.Equal(Cli.Ok, Run("measure", Sample, "--from", "50,50", "--to", "50,50"));

        Assert.Contains("dx 0, dy 0", Output);
        Assert.Contains("0.00 units", Output);
    }

    ///
    ///**dy follows the file rather than the screen**, the same as the ruler.
    ///
    ///The 2D view maps GDSII's upward Y straight onto SVG's downward Y, so the drawing is flipped and a point
    ///that looks higher has the smaller number. A measurement agreeing with the picture would disagree with
    ///every coordinate in the text view and in the download, which is the worse of the two.
    ///
    [Fact]
    public void The_deltas_follow_the_file_rather_than_a_picture()
    {
        Assert.Equal(Cli.Ok, Run("measure", Sample, "--from", "0,1000", "--to", "0,400"));

        Assert.Contains("dy -600", Output);
    }

    [Theory]
    [InlineData("--from", "0,0")]
    [InlineData("--to", "0,0")]
    public void Measure_needs_both_ends(string option, string value)
    {
        Assert.Equal(Cli.UsageError, Run("measure", Sample, option, value));

        Assert.Contains("needs --from", Error);
    }

    [Fact]
    public void Measure_refuses_a_point_that_is_not_one()
    {
        Assert.Equal(Cli.UsageError, Run("measure", Sample, "--from", "0,0", "--to", "over,there"));

        Assert.Contains("is not a point", Error);
    }

    ///
    ///A file that does not say what a unit is gets the units alone rather than a number invented for it.
    ///
    ///Built here rather than found, since every bundled file carries a usable UNITS - which is exactly why
    ///the branch would otherwise never run.
    ///
    [Fact]
    public void Without_usable_units_the_micron_figure_is_left_out_and_said_so()
    {
        byte[] stamps = GdsTestData.Timestamps();

        //A UNITS record whose meters-per-unit is zero, which is not a scale anything can be converted by.
        string path = TemporaryPath(".gds");

        File.WriteAllBytes(path, GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, stamps),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("NOUNITS")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(0))),
            GdsTestData.Record(RecordType.BGNSTR, stamps),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii("CELL")),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(65)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(20)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(GdsTestData.ClosedSquare(100))),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB)));

        Assert.Equal(Cli.Ok, Run("measure", path, "--from", "0,0", "--to", "300,400"));

        Assert.Contains("500.00 units", Output);
        Assert.DoesNotContain("µm", Output);
        Assert.Contains("does not say what a database unit is", Output);
    }

    ///
    ///Two coordinates at opposite ends of the signed range are further apart than an int holds.
    ///
    ///The subtraction is done in long for that reason: in int it overflows and comes back *negative*, which
    ///reads as a distance rather than as a failure and is the worst way for a measurement to be wrong.
    ///
    [Fact]
    public void A_span_wider_than_an_int_is_measured_rather_than_overflowed()
    {
        Assert.Equal(Cli.Ok, Run("measure", Sample, "--from", "-2000000000,0", "--to", "2000000000,0"));

        Assert.Contains("dx 4000000000", Output);
        Assert.DoesNotContain("dx -", Output);
    }

    #endregion ***********************************************************************



    #region The geometry itself *******************************************************

    ///<summary>
    ///A rectangle becomes a box: two triangles for each cap and two for each of the four walls.
    ///</summary>
    [Fact]
    public void A_rectangle_extrudes_to_twelve_triangles()
    {
        var parts = LayoutMesh.Build(OneShape(Square), 1);

        Assert.Single(parts);
        Assert.Equal(12, parts[0].TriangleCount);
    }

    ///<summary>
    ///The measurement that says the solid is closed and the right way out.
    ///
    ///The divergence theorem gives a closed mesh's volume from its triangles alone, and returns it
    ///**negative** when the faces are wound inward - so one number catches both a gap left in the surface
    ///and a solid built inside out. The 3D view hides both, since it lights back faces as readily as front
    ///ones; a mesh file handed to anything else does not.
    ///</summary>
    [Fact]
    public void A_rectangle_encloses_its_own_volume_the_right_way_out()
    {
        var parts = LayoutMesh.Build(OneShape(Square), 1);

        //100 x 100 across, 50 deep.
        Assert.Equal(100 * 100 * 50, volumeOf(parts), 0);
    }

    ///<summary>
    ///The reason a triangulator is worth a dependency, measured on the cap rather than on the volume.
    ///
    ///Volume cannot see this. Fanning a concave outline from one corner throws triangles out across the
    ///notch, but the ones that land outside come out wound backwards and cancel the ones that overlap
    ///them, so the total is right to the last digit while the surface is wrong. Adding the cap triangles
    ///up **unsigned** is what refuses to cancel: a fan of this plus covers 60000 where the plus covers
    ///50000, which is the notch counted twice over rather than left out.
    ///</summary>
    [Fact]
    public void A_concave_outline_is_triangulated_rather_than_fanned()
    {
        var part = LayoutMesh.Build(OneShape(Plus), 1)[0];

        Assert.Equal(PlusArea, cappedAreaOf(part), 0);

        //Ten triangles for each cap - two fewer than the twelve corners - and a wall on every edge.
        Assert.Equal((10 * 2) + (12 * 2), part.TriangleCount);

        //And it is still a closed solid, which the cap area on its own would not say.
        Assert.Equal(PlusArea * 50, volumeOf(new[] { part }), 0);
    }

    ///<summary>
    ///Nothing in GDSII says which way round a boundary is written, and files use both. The caps come back
    ///counter-clockwise from the tessellator whichever way the outline ran, but the walls are built from
    ///the outline's own order - so unless the two are put in agreement first, a clockwise boundary gets
    ///walls facing inward. Half the surface inverted is what the volume catches, and this is the same
    ///shape written backwards.
    ///</summary>
    [Fact]
    public void An_outline_written_clockwise_comes_out_the_same_way_up()
    {
        var forwards = LayoutMesh.Build(OneShape(Plus), 1);
        var backwards = LayoutMesh.Build(OneShape(Plus.Reverse().ToArray()), 1);

        Assert.Equal(volumeOf(forwards), volumeOf(backwards), 0);
        Assert.Equal(PlusArea * 50, volumeOf(backwards), 0);
    }

    ///<summary>
    ///The closing point a boundary repeats is a zero-length edge, which would be an extra wall of no width
    ///and a duplicate vertex for the tessellator to reconcile.
    ///</summary>
    [Fact]
    public void A_repeated_closing_point_is_not_extruded_twice()
    {
        var closed = Square.Append(Square[0]).ToArray();

        Assert.Equal(volumeOf(LayoutMesh.Build(OneShape(Square), 1)), volumeOf(LayoutMesh.Build(OneShape(closed), 1)), 0);
        Assert.Equal(12, LayoutMesh.Build(OneShape(closed), 1)[0].TriangleCount);
    }

    ///<summary>
    ///A layer sits at its own offset up the stack, which is what keeps the layers apart rather than all of
    ///them lying in one plane.
    ///</summary>
    [Fact]
    public void A_layer_is_extruded_from_its_own_offset()
    {
        var layout = OneShape(Square);
        layout.Elements[0].Layer.Offset = 300;

        var part = LayoutMesh.Build(layout, 1)[0];

        var heights = new SortedSet<float>();

        for (int i = 2; i < part.Vertices.Count; i += 3)
            heights.Add(part.Vertices[i]);

        Assert.Equal(new float[] { 300, 350 }, heights);
    }

    ///<summary>
    ///Scale multiplies the stacking as well as the shape, so a model asked for in different units is the
    ///same model rather than a layout with its layers pulled apart.
    ///</summary>
    [Fact]
    public void Scale_applies_to_the_stack_as_well_as_the_shapes()
    {
        var layout = OneShape(Square);
        layout.Elements[0].Layer.Offset = 300;

        var scaled = LayoutMesh.Build(layout, 0.001)[0];

        //A thousandth of the length in each of three dimensions, so a thousand-millionth of the volume.
        Assert.Equal(100 * 100 * 50 / 1e9, volumeOf(new List<LayoutMesh.Part> { scaled }), 6);

        for (int i = 2; i < scaled.Vertices.Count; i += 3)
            Assert.InRange(scaled.Vertices[i], 0.3f, 0.35f);
    }

    ///<summary>
    ///An outline enclosing nothing - three points on one line, or two - has no solid in it. Left out, and
    ///the ones worth reporting are counted on the way past: a shape silently missing from an export is
    ///found by whoever opens the file rather than by whoever wrote it. Nothing in the bundled corpus trips
    ///this, which is the reason to build the case by hand.
    ///
    ///**Only the two-point one counts now.** Each layer is merged before it is extruded, and three points
    ///on one line enclose no area, so that shape contributes nothing to the union and there is nothing
    ///left to skip - it was never geometry. Two points cannot go into a merge at all, which is the case
    ///still worth telling somebody about.
    ///</summary>
    [Fact]
    public void An_outline_enclosing_nothing_is_counted_rather_than_passed_over()
    {
        var layout = OneShape(Square);

        layout.Elements.Add(new Element
        {
            Layer = layout.Elements[0].Layer,
            Points = { new Element.Point(0, 0), new Element.Point(50, 50), new Element.Point(100, 100) }
        });

        layout.Elements.Add(new Element
        {
            Layer = layout.Elements[0].Layer,
            Points = { new Element.Point(0, 0), new Element.Point(100, 0) }
        });

        var parts = LayoutMesh.Build(layout, 1, out int skipped);

        Assert.Equal(1, skipped);

        //And the square that was fine is still there, whole - neither of the others left a mark on it.
        Assert.Equal(12, parts[0].TriangleCount);
        Assert.Equal(100 * 100 * 50, volumeOf(parts), 0);
    }

    ///<summary>Labels have no outline, and no mesh format can hold one.</summary>
    [Fact]
    public void Labels_are_left_out()
    {
        var layout = OneShape(Square);

        layout.Elements.Add(new Element
        {
            Layer = layout.Elements[0].Layer,
            Text = "VPWR",
            Points = { new Element.Point(10, 10) }
        });

        Assert.Equal(12, LayoutMesh.Build(layout, 1)[0].TriangleCount);
    }

    ///<summary>
    ///One part per layer, ordered the way the sidebar lists them, so the objects in an OBJ or a glTF come
    ///out in the order somebody reading a layermap expects.
    ///</summary>
    [Fact]
    public void Layers_become_separate_parts_in_reading_order()
    {
        var layout = OneShape(Square);

        foreach (var key in new[] { new LayerKey(68, 20), new LayerKey(65, 16) })
        {
            layout.Elements.Add(new Element
            {
                Layer = new Layer(key, "#00ff00"),
                Points = Square.ToList()
            });
        }

        var parts = LayoutMesh.Build(layout, 1);

        Assert.Equal(new[] { "65/16", "65/20", "68/20" }, parts.Select(part => part.Layer.ToString()));
    }

    ///<summary>
    ///The same measurement over a real cell rather than a shape written for the purpose.
    ///
    ///Every outline in it is extruded and the total volume compared against what those outlines enclose,
    ///worked out from the flattened layout by shoelace and multiplied by each layer's depth. The two agree
    ///only if every shape came through: one dropped by the tessellator, one left open, or one built inside
    ///out all move the total. This is the file whose 3D view was wrong once already, and it carries the
    ///concave orthogonal outlines and the path outlines that a hand-written cap gets wrong.
    ///</summary>
    [Fact]
    public void Every_outline_in_a_real_cell_is_extruded_and_closed()
    {
        var gds = new GDS(File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, "Sky130 GDS", "sky130_fd_sc_hd__a211oi_1.gds")));
        var layout = GdsFlattener.Flatten(gds);

        double expected = 0;

        foreach (var element in layout.Elements)
        {
            if (element.Text is not null)
                continue;

            expected += Math.Abs(shoelaceOf(element.Points)) * element.Layer.Depth;
        }

        Assert.True(expected > 0);

        //Relative, since the volumes here run to billions of cubic database units and the mesh holds its
        //coordinates as the float the formats are written in.
        double actual = volumeOf(LayoutMesh.Build(layout, 1));

        Assert.Equal(1, actual / expected, 6);
    }

    ///<summary>Twice the signed area, which is all the comparison above needs before taking its size.</summary>
    private static double shoelaceOf(List<Element.Point> points)
    {
        double sum = 0;

        for (int i = 0; i < points.Count; i++)
        {
            var here = points[i];
            var next = points[(i + 1) % points.Count];

            sum += ((double)here.X * next.Y) - ((double)next.X * here.Y);
        }

        return sum / 2;
    }

    #region The shapes these use ******************************************************

    ///<summary>100 by 100, counter-clockwise, with no repeated closing point.</summary>
    private static readonly Element.Point[] Square = new[]
    {
        new Element.Point(0, 0),
        new Element.Point(100, 0),
        new Element.Point(100, 100),
        new Element.Point(0, 100)
    };

    ///<summary>
    ///A plus, counter-clockwise from the foot of the lower arm. Twelve corners, four of them reflex, and
    ///no corner from which the whole of it is visible - so nothing that fans from a single vertex can
    ///triangulate it correctly, whichever vertex a file happens to have started the boundary at.
    ///</summary>
    private static readonly Element.Point[] Plus = new[]
    {
        new Element.Point(100, 0),
        new Element.Point(200, 0),
        new Element.Point(200, 100),
        new Element.Point(300, 100),
        new Element.Point(300, 200),
        new Element.Point(200, 200),
        new Element.Point(200, 300),
        new Element.Point(100, 300),
        new Element.Point(100, 200),
        new Element.Point(0, 200),
        new Element.Point(0, 100),
        new Element.Point(100, 100)
    };

    ///<summary>Two 300 by 100 bars crossing, so the middle is not counted twice.</summary>
    private const double PlusArea = (300 * 100) + (300 * 100) - (100 * 100);

    private static FlattenedLayout OneShape(IEnumerable<Element.Point> points)
    {
        var layout = new FlattenedLayout();

        layout.Elements.Add(new Element
        {
            Layer = new Layer(new LayerKey(65, 20), "#ff0000", layerOffset: 0, layerDepth: 50),
            Points = points.ToList()
        });

        return layout;
    }

    #region Merging before extruding *************************************************

    ///<summary>
    ///Two shapes overlapping on one layer come out as one solid, not two sharing a face.
    ///
    ///**A mesh with two faces in the same place is not a solid.** It is non-manifold, which is what a
    ///slicer refuses and a mesh checker reports as a defect - and the volume is wrong on top of that,
    ///because the overlap is counted twice. On screen the same thing is only a flicker; in a file somebody
    ///prints or simulates, it is worse.
    ///</summary>
    [Fact]
    public void Overlapping_shapes_on_one_layer_become_one_solid()
    {
        var layout = OneShape(Square);

        //Half over the first, so the two share a quarter of their area.
        layout.Elements.Add(new Element
        {
            Layer = layout.Elements[0].Layer,
            Points =
            {
                new Element.Point(50, 50),
                new Element.Point(150, 50),
                new Element.Point(150, 150),
                new Element.Point(50, 150)
            }
        });

        var parts = LayoutMesh.Build(layout, 1);

        var part = Assert.Single(parts);

        //The union's area is the two squares less the quarter they share, and the volume follows it.
        Assert.Equal(((100 * 100 * 2) - (50 * 50)) * 50, volumeOf(parts), 0);
        Assert.Equal((100 * 100 * 2) - (50 * 50), cappedAreaOf(part), 0);
    }

    ///<summary>
    ///A hole comes out as a hole: walls around it, and no cap across it.
    ///
    ///Four bars in a ring, which is how a hole turns up in a real layer - nobody draws one, it falls out
    ///of shapes that surround something. The tessellator takes the hole as a contour of its own, which is
    ///why the merge hands it over that way rather than as the keyhole a GDSII file has to write.
    ///</summary>
    [Fact]
    public void A_hole_is_extruded_as_a_hole()
    {
        var layout = OneShape(Square);

        layout.Elements.Clear();

        var layer = new Layer(new LayerKey(65, 20), "#ff0000");

        foreach (var bar in new[]
        {
            new[] { (0, 0), (300, 0), (300, 100), (0, 100) },
            new[] { (0, 200), (300, 200), (300, 300), (0, 300) },
            new[] { (0, 0), (100, 0), (100, 300), (0, 300) },
            new[] { (200, 0), (300, 0), (300, 300), (200, 300) }
        })
        {
            layout.Elements.Add(new Element
            {
                Layer = layer,
                Points = bar.Select(corner => new Element.Point(corner.Item1, corner.Item2)).ToList()
            });
        }

        var part = Assert.Single(LayoutMesh.Build(layout, 1));

        //The ring's area is the outer square less the 100 by 100 hole in the middle of it.
        Assert.Equal((300 * 300) - (100 * 100), cappedAreaOf(part), 0);
        Assert.Equal(((300 * 300) - (100 * 100)) * layer.Depth, volumeOf(new[] { part }), 0);

        //And the hole is walled all the way round rather than left open at its edge.
        Assert.True(isClosed(part), "the ring is not a closed surface, so the hole has no walls");
    }

    ///<summary>
    ///Whether a part is a closed surface: every edge used once in each direction.
    ///
    ///Not a count of triangles, which was the first version of this and pinned something that is not ours
    ///to decide - whether the clipper keeps the collinear corner where two shapes meet along an edge. How
    ///many triangles a ring takes is its business; that the surface closes is not.
    ///</summary>
    private static bool isClosed(LayoutMesh.Part part)
    {
        var edges = new Dictionary<(int From, int To), int>();

        for (int i = 0; i < part.Triangles.Count; i += 3)
        {
            for (int corner = 0; corner < 3; corner++)
            {
                var edge = (part.Triangles[i + corner], part.Triangles[i + ((corner + 1) % 3)]);

                edges.TryGetValue(edge, out int seen);
                edges[edge] = seen + 1;
            }
        }

        foreach (var edge in edges)
        {
            if (edge.Value != 1)
                return false;

            if (!edges.TryGetValue((edge.Key.To, edge.Key.From), out int back) || back != 1)
                return false;
        }

        return true;
    }

    #endregion ***********************************************************************

    ///<summary>
    ///The volume a closed mesh encloses, by the divergence theorem: a sixth of the sum of each triangle's
    ///scalar triple product. Positive for outward-facing triangles, negative for inward.
    ///</summary>
    private static double volumeOf(IReadOnlyList<LayoutMesh.Part> parts)
    {
        double total = 0;

        foreach (var part in parts)
        {
            for (int i = 0; i < part.Triangles.Count; i += 3)
            {
                var a = cornerOf(part, part.Triangles[i]);
                var b = cornerOf(part, part.Triangles[i + 1]);
                var c = cornerOf(part, part.Triangles[i + 2]);

                total += (a.X * ((b.Y * c.Z) - (b.Z * c.Y)))
                    - (a.Y * ((b.X * c.Z) - (b.Z * c.X)))
                    + (a.Z * ((b.X * c.Y) - (b.Y * c.X)));
            }
        }

        return total / 6;
    }

    ///<summary>
    ///The area of the top cap, adding each triangle's own area rather than their signed contributions - so
    ///a triangle thrown outside the outline adds to the total instead of cancelling one that overlaps it.
    ///
    ///The cap is picked out by height: it is the triangles lying flat at the top of the extrusion, which
    ///is every triangle except the walls.
    ///</summary>
    private static double cappedAreaOf(LayoutMesh.Part part)
    {
        double top = double.MinValue;

        for (int i = 2; i < part.Vertices.Count; i += 3)
            top = Math.Max(top, part.Vertices[i]);

        double area = 0;

        for (int i = 0; i < part.Triangles.Count; i += 3)
        {
            var a = cornerOf(part, part.Triangles[i]);
            var b = cornerOf(part, part.Triangles[i + 1]);
            var c = cornerOf(part, part.Triangles[i + 2]);

            if (a.Z != top || b.Z != top || c.Z != top)
                continue;

            area += Math.Abs(((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y))) / 2;
        }

        return area;
    }

    private static (double X, double Y, double Z) cornerOf(LayoutMesh.Part part, int index)
    {
        int at = index * 3;

        return (part.Vertices[at], part.Vertices[at + 1], part.Vertices[at + 2]);
    }

    #endregion ***********************************************************************

    #endregion ***********************************************************************
}
