using GdsII;

namespace GDSViewer.Tests;

///<summary>
///Reading OASIS, the format that was meant to replace GDSII.
///
///**The corpus test is the one that matters.** A binary format read from a specification is exactly the
///kind of thing that comes out plausible and wrong - a misread info byte puts every later element on the
///wrong layer, a misread delta bends every polygon by the same amount, and either produces a picture that
///looks like a layout. So the same 897 files are converted to OASIS by KLayout and read back, and the
///geometry has to match what this reads out of the GDSII original. Nothing about that can pass by
///accident.
///
///The converted files are made on demand and cached, because they are KLayout's output rather than ours
///and there is no reason to keep a second copy of the corpus in the repository.
///
///**Every test here needs KLayout installed**, since its output is the input. Traited so a machine without
///it - a CI runner, or somebody who has just cloned this - can run everything else with
///`--filter "Needs!=KLayout"` rather than reading a wall of red about a tool they were never told to have.
///</summary>
[Trait("Needs", "KLayout")]
public class OasisTests
{
    ///<summary>
    ///Every shape a file draws, as layer/datatype and a sorted list of its corners. Shared with the
    ///writer's tests, which have to mean the same thing by "the same layout".
    ///</summary>
    private static List<string> Geometry(GDS gds)
    {
        return GdsTestData.Geometry(gds);
    }

    #region The primitives ***********************************************************

    ///<summary>The magic is how the two formats are told apart, so it is the one thing read by name.</summary>
    [Fact]
    public void An_oasis_file_is_recognized_by_what_it_starts_with()
    {
        Assert.True(OasisReader.LooksLikeOasis(OasisTestData.Convert(GdsTestData.MosfetSample)));
        Assert.False(OasisReader.LooksLikeOasis(GdsTestData.ReadSample(GdsTestData.MosfetSample)));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x25 })]
    [InlineData(new byte[] { 0x00, 0x06, 0x00, 0x02 })]
    public void Something_too_short_to_be_oasis_is_not_mistaken_for_it(byte[] start)
    {
        Assert.False(OasisReader.LooksLikeOasis(start));
    }

    [Fact]
    public void A_gds_file_is_refused_with_something_to_read()
    {
        var problem = Assert.Throws<InvalidDataException>(
            () => OasisReader.Read(GdsTestData.ReadSample(GdsTestData.MosfetSample)));

        Assert.Contains("%SEMI-OASIS", problem.Message);
    }

    #endregion ***********************************************************************



    #region Against the GDSII the file came from *************************************

    ///<summary>
    ///The hand-made example, which is the one file whose contents are asserted everywhere else here.
    ///</summary>
    [Fact]
    public void The_mosfet_reads_the_same_from_oasis_as_from_gds()
    {
        var fromGds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var fromOasis = OasisReader.Read(OasisTestData.Convert(GdsTestData.MosfetSample));

        Assert.Equal(Geometry(fromGds), Geometry(fromOasis));
    }

    ///<summary>And its layers come out as the same pairs, since that is what the sidebar lists.</summary>
    [Fact]
    public void The_layers_come_out_the_same()
    {
        var fromGds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var fromOasis = OasisReader.Read(OasisTestData.Convert(GdsTestData.MosfetSample));

        Assert.Equal(
            fromGds.AdditionalInformation.OrderedLayers().Select(entry => entry.Key.ToString()),
            fromOasis.AdditionalInformation.OrderedLayers().Select(entry => entry.Key.ToString()));
    }

    ///<summary>
    ///The units survive. OASIS states how many database units go in a micron and GDSII states the two
    ///sides of the same thing, so getting the conversion backwards would scale every drawing by a million.
    ///
    ///Compared as numbers rather than as bytes. A GDSII real is lossy, and the two values here differ in
    ///the last bit of the mantissa - 1e-9 against 1.0000000000000002e-9 - because one was written by a
    ///tool that had the number and the other is computed from the micron count OASIS gives instead. That
    ///is not a difference in what the file means, and a test that demanded the bytes match would be
    ///pinning which of two equally right answers we happen to produce.
    ///</summary>
    [Fact]
    public void The_units_come_out_the_same()
    {
        var fromGds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var fromOasis = OasisReader.Read(OasisTestData.Convert(GdsTestData.MosfetSample));

        double[] expected = ((Real8Data)fromGds.StreamFormat.UNITS.Data!).Values;
        double[] actual = ((Real8Data)fromOasis.StreamFormat.UNITS.Data!).Values;

        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], actual[i], 12);
    }

    ///<summary>
    ///What comes out is a GDSII library, not something that merely looks like one - so it serializes, and
    ///what it serializes to parses back to the same thing.
    ///</summary>
    [Fact]
    public void What_is_read_is_a_gds_file_that_can_be_written_and_read_again()
    {
        var fromOasis = OasisReader.Read(OasisTestData.Convert(GdsTestData.MosfetSample));

        var round = new GDS(fromOasis.Serialize());

        Assert.Equal(Geometry(fromOasis), Geometry(round));
    }

    ///<summary>
    ///Labels come through with their text and their anchor.
    ///
    ///The anchor is the half that is easy to lose: OASIS hangs a label from its bottom-left corner and
    ///GDSII from its top-left, so a converter that says nothing moves every label up by its own height.
    ///</summary>
    [Fact]
    public void Labels_come_through_with_their_text()
    {
        var fromGds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));
        var fromOasis = OasisReader.Read(OasisTestData.Convert(GdsTestData.MosfetSample));

        var expected = GdsFlattener.Flatten(fromGds).Elements
            .Where(element => element.Text is not null)
            .Select(element => element.Text)
            .OrderBy(each => each, StringComparer.Ordinal)
            .ToList();

        var actual = GdsFlattener.Flatten(fromOasis).Elements
            .Where(element => element.Text is not null)
            .Select(element => element.Text)
            .OrderBy(each => each, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    #endregion ***********************************************************************



    #region The corpus ***************************************************************

    ///<summary>
    ///Every bundled file, converted and read back.
    ///
    ///897 layouts is what makes this worth more than any hand-written case: between them they exercise the
    ///point-list forms, the modal carry-over across records and cells, and whatever KLayout's writer
    ///decides is the shortest way to say each shape. A reader that is wrong about any of it fails here and
    ///names the file.
    ///</summary>
    [Fact]
    public void Every_sample_file_reads_the_same_through_oasis()
    {
        var disagreed = new List<string>();

        foreach (string path in GdsTestData.AllSampleFiles())
        {
            string relative = Path.GetRelativePath(GdsTestData.SampleDirectory, path);

            var fromGds = new GDS(File.ReadAllBytes(path));

            GDS fromOasis;

            try
            {
                fromOasis = OasisReader.Read(OasisTestData.Convert(relative));
            }
            catch (Exception problem)
            {
                disagreed.Add($"{relative}: {problem.Message}");

                continue;
            }

            if (!Geometry(fromGds).SequenceEqual(Geometry(fromOasis)))
                disagreed.Add($"{relative}: geometry differs");
        }

        Assert.Empty(disagreed);
    }

    #endregion ***********************************************************************
}
