using System.Text;
using System.Text.Json;
using GdsII;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///Covers the two halves of persistence that are pure C#: how a value is encoded for storage, and how a
///session round-trips through JSON.
///
///The stores themselves are browser APIs and are covered end to end instead. What is worth testing here is
///what would corrupt a saved session silently: an encoder and decoder that disagree, or a reader that
///throws on the path that starts the app rather than giving up on the session.
///</summary>
public class StorageTests
{
    #region Encoding ******************************************************************

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("65,20,diff.drawing")]
    public void A_short_value_round_trips(string value)
    {
        Assert.Equal(value, AppStorage.Decode(AppStorage.Encode(value)));
    }

    ///<summary>Short values are stored as they are: deflate plus base64 would make them bigger.</summary>
    [Fact]
    public void A_short_value_is_not_compressed()
    {
        string encoded = AppStorage.Encode("65,20,diff.drawing");

        Assert.Equal("r65,20,diff.drawing", encoded);
    }

    [Fact]
    public void A_long_value_round_trips()
    {
        string value = string.Join("\n", Enumerable.Range(0, 500).Select(n => $"{n},20,layer{n},#00ff00"));

        Assert.Equal(value, AppStorage.Decode(AppStorage.Encode(value)));
    }

    ///<summary>
    ///The reason for compressing at all. A GDSII file is highly repetitive - record headers and coordinate
    ///runs - so this is the case the threshold exists for.
    ///</summary>
    [Fact]
    public void A_long_repetitive_value_gets_smaller()
    {
        string value = Convert.ToBase64String(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        string encoded = AppStorage.Encode(value);

        Assert.StartsWith("z", encoded);
        Assert.True(encoded.Length < value.Length, $"encoded {encoded.Length} was not smaller than {value.Length}");
    }

    ///<summary>Bytes rather than text, since that is what a session actually carries.</summary>
    [Fact]
    public void A_whole_gds_file_survives_the_trip()
    {
        byte[] original = GdsTestData.ReadSample(GdsTestData.MosfetSample);

        string decoded = AppStorage.Decode(AppStorage.Encode(Convert.ToBase64String(original)));

        Assert.Equal(original, Convert.FromBase64String(decoded));
    }

    [Fact]
    public void A_value_with_no_marker_is_handed_back_untouched()
    {
        //What a version of the app that stored values plainly would have left behind.
        Assert.Equal("65,20,diff.drawing", AppStorage.Decode("65,20,diff.drawing"));
    }

    ///<summary>
    ///Truncation is what a full quota produces, and this runs while the app is starting - so it has to give
    ///up on the value rather than throw.
    ///</summary>
    [Fact]
    public void A_corrupted_compressed_value_decodes_to_nothing_rather_than_throwing()
    {
        string encoded = AppStorage.Encode(new string('a', 1000));

        Assert.Equal("", AppStorage.Decode(encoded[..(encoded.Length / 2)]));
    }

    [Fact]
    public void Unicode_survives_the_trip()
    {
        string value = string.Concat(Enumerable.Repeat("layer name with an em dash — and a µ ", 20));

        Assert.Equal(value, AppStorage.Decode(AppStorage.Encode(value)));
    }

    #endregion ***********************************************************************



    #region Sessions *****************************************************************

    [Fact]
    public void A_session_round_trips()
    {
        var session = new SavedSession
        {
            ExampleName = "Mosfet",
            FileName = "Mosfet",
            FileType = ".gds",
            View = "3d",
            LayerNames = "65,20,diff.drawing,#00ff00\n",
            HiddenLayers = new List<string> { "65/20", "66/44" },
            Opacity = 0.25f,
            LayerSpacing = 700,
            Background = "background2.jpg",
            ModelFileType = ".obj"
        };

        var read = SavedSession.Deserialize(SavedSession.Serialize(session));

        Assert.NotNull(read);
        Assert.Equal("Mosfet", read!.ExampleName);
        Assert.Equal("3d", read.View);
        Assert.Equal("65,20,diff.drawing,#00ff00\n", read.LayerNames);
        Assert.Equal(new[] { "65/20", "66/44" }, read.HiddenLayers.ToArray());
        Assert.Equal(0.25f, read.Opacity);
        Assert.Equal(700, read.LayerSpacing);
        Assert.Equal("background2.jpg", read.Background);
        Assert.Equal(".obj", read.ModelFileType);
    }

    ///<summary>
    ///**The generated serializer writes exactly what the reflecting one wrote.**
    ///
    ///Sessions written by every version before <see cref="SavedJson"/> are already sitting in people's
    ///browsers, and a session that cannot be read is a file and an undo stack lost. Comparing the two
    ///serializers directly is the only assertion that covers the whole shape - a round trip through the
    ///new one alone would pass just as happily if both ends had changed together.
    ///
    ///Every field is set, including the two that are left out when empty, because what would differ is a
    ///property the generator handled differently rather than the ones any example happens to use.
    ///</summary>
    [Fact]
    public void A_session_is_written_the_way_it_always_was()
    {
        var session = fullSession();

        Assert.Equal(JsonSerializer.Serialize(session), SavedSession.Serialize(session));
    }

    ///And reads one, which is the direction that matters to somebody who already has one stored.
    [Fact]
    public void A_session_written_before_the_generated_serializer_still_reads()
    {
        var read = SavedSession.Deserialize(JsonSerializer.Serialize(fullSession()));

        Assert.NotNull(read);
        Assert.Equal("Mosfet", read!.FileName);
        Assert.Equal(0.25f, read.Opacity);
        Assert.Equal(new[] { "65/20" }, read.LabelsOffLayers.ToArray());
        Assert.Equal("Move", read.Edits?.Done[0].Kind);
        Assert.Equal(new[] { 1, 2 }, read.Edits?.Done[0].Before);
    }

    ///<summary>A session with something in every field, including the parts that nest.</summary>
    private static SavedSession fullSession()
    {
        return new SavedSession
        {
            ExampleName = "Mosfet",
            FileName = "Mosfet",
            FileType = ".gds",
            FileBytes = "AAEC",
            View = "2d",
            LayerNames = "65,20,diff.drawing,#00ff00\n",
            HiddenLayers = new List<string> { "66/44" },
            LabelsOffLayers = new List<string> { "65/20" },
            LayerColors = new List<string> { "65/20,#00ff00" },
            LayerStack = new List<string> { "65/20,10,50" },
            RecentColors = new List<string> { "#00ff00" },
            Opacity = 0.25f,
            ShowGrid = true,
            SnapToGrid = true,
            SnapToShapes = true,
            GridMicrons = 0.5,
            GridUnit = "Nanometer",
            Joining = true,
            EllipseSides = 32,
            PathWidthMicrons = 1.5,
            PathEnds = "Round",
            LayerSpacing = 700,
            Background = "background2.jpg",
            ModelFileType = ".obj",
            DownloadFormat = ".oas",
            Edits = new SavedEdits
            {
                Done = new List<EditRecord>
                {
                    new EditRecord
                    {
                        Kind = "Move",
                        Structure = "Mosfet",
                        At = 3,
                        Corner = 1,
                        Dx = 10,
                        Dy = -10,
                        Label = "diff",
                        Before = new[] { 1, 2 },
                        After = new[] { 3, 4 },
                        Said = "one",
                        Says = "two",
                        Records = new List<SavedRecord> { new SavedRecord { Type = 4, Data = "8" } },
                        Parts = new List<EditRecord> { new EditRecord { Kind = "Delete", At = 9 } }
                    }
                },
                Undone = new List<EditRecord> { new EditRecord { Kind = "Add", At = 1 } }
            }
        };
    }

    [Fact]
    public void A_session_carrying_a_file_round_trips_through_storage_encoding()
    {
        byte[] file = GdsTestData.ReadSample(GdsTestData.MosfetSample);

        var session = new SavedSession { FileName = "Mosfet", FileBytes = Convert.ToBase64String(file) };

        var read = SavedSession.Deserialize(AppStorage.Decode(AppStorage.Encode(SavedSession.Serialize(session))));

        Assert.NotNull(read);
        Assert.Equal(file, Convert.FromBase64String(read!.FileBytes));
    }

    ///<summary>
    ///An edited file has to come back edited, or the feature is worse than not having it: you would return
    ///to a file that looks like yours and quietly is not.
    ///</summary>
    [Fact]
    public void An_edited_file_comes_back_edited()
    {
        var gds = new GDS(GdsTestData.ReadSample(GdsTestData.MosfetSample));

        gds.Deserialize(gds.AsText().Replace("LAYER: 65 ", "LAYER: 200 "));

        var session = new SavedSession { FileBytes = Convert.ToBase64String(gds.Serialize()) };

        var read = SavedSession.Deserialize(AppStorage.Decode(AppStorage.Encode(SavedSession.Serialize(session))));

        var restored = new GDS(Convert.FromBase64String(read!.FileBytes));

        Assert.True(GdsTestData.HasLayerNumber(restored, 200));
        Assert.False(GdsTestData.HasLayerNumber(restored, 65));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"v\":1,")]
    [InlineData("[]")]
    public void Anything_that_is_not_a_session_reads_as_none(string? stored)
    {
        Assert.Null(SavedSession.Deserialize(stored));
    }

    ///<summary>
    ///A session written by another version is dropped rather than guessed at. It costs one reopened file,
    ///where reading it wrongly could put the wrong bytes on screen.
    ///</summary>
    [Fact]
    public void A_session_from_another_version_is_dropped()
    {
        //Off whatever the current version is, rather than off a literal 1. Written that way, this passed
        //only while the version happened to be 1: the substitution stopped matching when it was bumped,
        //the session stayed current, and the test failed for having nothing to drop.
        string json = SavedSession.Serialize(new SavedSession())
            .Replace($"\"v\":{SavedSession.CurrentVersion}", "\"v\":99");

        Assert.Null(SavedSession.Deserialize(json));
    }

    #endregion ***********************************************************************
}
