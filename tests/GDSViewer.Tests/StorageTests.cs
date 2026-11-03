using System.Text;
using System.Text.Json;
using GdsII;
using GDSViewer.Models;
using Microsoft.JSInterop;

namespace GDSViewer.Tests;

///<summary>
///Covers the two halves of persistence that are pure C#: how a value is encoded for storage, and how a
///session round-trips through JSON - and a big value read at a pace, against a stand-in for the browser's half.
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



    #region Reading at a pace ********************************************************

    ///<summary>A pause that suspends every time and counts, the way the browser's does once a slice is used up.</summary>
    private sealed class Counting
    {
        public int Pauses;

        public async ValueTask Pause()
        {
            Pauses++;

            await Task.Yield();
        }
    }

    ///
    ///The browser's half of AppStorage, answered the way StorageInterop.js answers it, over a dictionary for IndexedDB and
    ///one for localStorage: a value held for reading in parts, each part handed over as the bytes of its characters, and
    ///let go of. Counts the parts, so a test can tell a value read in parts from one read in one go.
    ///
    private sealed class Browser : IJSRuntime
    {
        public readonly Dictionary<string, string> Stored = new Dictionary<string, string>();
        public readonly Dictionary<string, string> Local = new Dictionary<string, string>();
        public int Parts;

        private readonly Dictionary<string, string> held = new Dictionary<string, string>();

        ///<summary>How many values are held for reading in parts and not yet let go of.</summary>
        public int Held => held.Count;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            object? answered = answer(identifier, args ?? Array.Empty<object?>());

            if (answered is null)
                return ValueTask.FromResult(default(TValue)!);

            return ValueTask.FromResult((TValue)answered);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            return InvokeAsync<TValue>(identifier, args);
        }

        private object? answer(string identifier, object?[] args)
        {
            string key = (string)args[0]!;

            switch (identifier)
            {
                case "gdsStorage.get":
                    return Stored.GetValueOrDefault(key);

                case "gdsStorage.set":
                    Stored[key] = (string)args[1]!;

                    return true;

                case "gdsStorage.setTogether":
                    Stored[key] = (string)args[1]!;
                    Stored[(string)args[2]!] = (string)args[3]!;

                    return true;

                case "gdsStorage.remove":
                    Stored.Remove(key);

                    return null;

                case "gdsStorage.getInParts":
                    if (!Stored.TryGetValue(key, out string? value))
                        return -1;

                    held[key] = value;

                    return value.Length;

                case "gdsStorage.partBytes":
                    if (!held.TryGetValue(key, out string? whole))
                        return null;

                    int from = (int)args[1]!;
                    int count = Math.Min((int)args[2]!, whole.Length - from);

                    Parts++;

                    return Encoding.UTF8.GetBytes(whole.Substring(from, count));

                case "gdsStorage.doneWithParts":
                    held.Remove(key);

                    return null;

                case "gdsLocalStorage.get":
                    return Local.GetValueOrDefault(key);

                default:
                    return null;
            }
        }
    }

    ///<summary>What a History row keeps beside it, as it is stored: a name, then a file's bytes as base64.</summary>
    private static string storedBeside(string name, byte[] file)
    {
        string bytes = Convert.ToBase64String(file);

        //Bytes stored before there were names are base64 and nothing else.
        if (name.Length == 0)
            return AppStorage.Encode(bytes);

        return AppStorage.Encode(SavedSession.Stored(name, bytes));
    }

    private static byte[] design(string name)
    {
        return File.ReadAllBytes(Path.Combine(GdsTestData.SampleDirectory, "Sky130 Designs", name));
    }

    ///<summary>
    ///Lengths about a run of 65,536 characters of base64, which is 49,152 bytes, and past one and several parts of
    ///262,144 - random, so they hardly compress. Under no name, under a name as one is written now, and under one with
    ///characters of two and three bytes, which the name is looked for among.
    ///</summary>
    public static IEnumerable<object[]> Files()
    {
        foreach (string name in new[] { "", "3f9c2a7b81d0", "Mösfet — 1" })
        {
            foreach (int length in new[] { 1, 2, 3, 49151, 49152, 49153, 200000, 600000 })
                yield return new object[] { name, length };
        }
    }

    ///<summary>
    ///A History row's file is read this way - see AppStorage.ReadAtAPaceAsync and SavedSession.BytesFromAsync - so it
    ///has to be the file read in one go: the same bytes under the same name, wherever the parts and runs fall.
    ///</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public async Task A_file_read_at_a_pace_is_the_file_read_whole(string name, int length)
    {
        byte[] file = new byte[length];

        new Random(length).NextBytes(file);

        var browser = new Browser();

        browser.Stored["row#bytes"] = storedBeside(name, file);

        var whole = new SavedSession();

        whole.BytesFrom(AppStorage.Decode(browser.Stored["row#bytes"]));

        var counting = new Counting();
        var paced = new SavedSession();

        using (var text = await new AppStorage(browser).ReadAtAPaceAsync("row#bytes", counting.Pause))
            Assert.True(await paced.BytesFromAsync(text!, counting.Pause));

        Assert.Equal(file, Convert.FromBase64String(whole.FileBytes));
        Assert.Equal(file, paced.FileData);
        Assert.Equal(name, whole.BytesBase);
        Assert.Equal(name, paced.BytesBase);
        Assert.Equal("", paced.FileBytes);
        Assert.Equal(0, browser.Held);
    }

    ///<summary>And in parts and a run at a time, which is the point, rather than in one go by another way round.</summary>
    [Fact]
    public async Task A_big_file_is_read_in_parts_and_a_run_at_a_time()
    {
        byte[] file = design("rgb_mixer.gds");
        var browser = new Browser();

        browser.Stored["row#bytes"] = storedBeside("3f9c2a7b81d0", file);

        var counting = new Counting();
        var session = new SavedSession();

        using (var text = await new AppStorage(browser).ReadAtAPaceAsync("row#bytes", counting.Pause))
            Assert.True(await session.BytesFromAsync(text!, counting.Pause));

        Assert.Equal(file, session.FileData);
        Assert.True(browser.Parts > 1, $"handed over in {browser.Parts} part(s)");
        Assert.True(counting.Pauses > browser.Parts + 2, $"read in {counting.Pauses} run(s) for {browser.Parts} part(s)");
    }

    [Fact]
    public async Task Nothing_stored_reads_at_a_pace_as_nothing()
    {
        Assert.Null(await new AppStorage(new Browser()).ReadAtAPaceAsync("row#bytes", new Counting().Pause));
    }

    ///<summary>What only the unload handler wrote, to localStorage, is read the ordinary way - and read all the same.</summary>
    [Fact]
    public async Task A_file_only_in_local_storage_is_read_the_ordinary_way()
    {
        byte[] file = design("tt_um_oscillating_bones.gds");
        var browser = new Browser();

        browser.Local["row#bytes"] = storedBeside("3f9c2a7b81d0", file);

        var session = new SavedSession();

        using (var text = await new AppStorage(browser).ReadAtAPaceAsync("row#bytes", new Counting().Pause))
            Assert.True(await session.BytesFromAsync(text!, new Counting().Pause));

        Assert.Equal(file, session.FileData);
        Assert.Equal("3f9c2a7b81d0", session.BytesBase);
        Assert.Equal(0, browser.Parts);
    }

    ///<summary>
    ///Truncated off a group of four, the way a full quota can leave a value, and whole base64 of what will not inflate:
    ///nothing, rather than a throw or half a file - what Decode answers both with.
    ///</summary>
    [Fact]
    public async Task A_corrupted_file_read_at_a_pace_is_nothing()
    {
        string stored = storedBeside("3f9c2a7b81d0", design("tt_um_oscillating_bones.gds"));
        string notDeflate = "z" + Convert.ToBase64String(Enumerable.Repeat((byte)0xFF, 300).ToArray());

        Assert.Equal("", AppStorage.Decode(stored[..(stored.Length / 2 / 4 * 4 + 3)]));
        Assert.Equal("", AppStorage.Decode(notDeflate));

        var browser = new Browser();

        browser.Stored["cut"] = stored[..(stored.Length / 2 / 4 * 4 + 3)];
        browser.Stored["notDeflate"] = notDeflate;

        var storage = new AppStorage(browser);

        Assert.Null(await storage.ReadAtAPaceAsync("cut", new Counting().Pause));

        var session = new SavedSession();

        using (var text = await storage.ReadAtAPaceAsync("notDeflate", new Counting().Pause))
            Assert.False(await session.BytesFromAsync(text!, new Counting().Pause));

        Assert.Null(session.FileData);
        Assert.Equal(0, browser.Held);
    }

    ///<summary>Except a pause that throws, which is how a read is given up: that has to get out, not read as nothing.</summary>
    [Fact]
    public async Task A_pause_that_throws_gives_a_paced_read_up()
    {
        var browser = new Browser();

        browser.Stored["row#bytes"] = storedBeside("3f9c2a7b81d0", design("tt_um_oscillating_bones.gds"));

        var storage = new AppStorage(browser);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await storage.ReadAtAPaceAsync("row#bytes", () => throw new OperationCanceledException()));

        //Let go of all the same, so the browser does not go on holding a file for nobody.
        Assert.Equal(0, browser.Held);

        using var text = await storage.ReadAtAPaceAsync("row#bytes", new Counting().Pause);

        await Assert.ThrowsAsync<OperationCanceledException>(async () => await new SavedSession().BytesFromAsync(text!, () => throw new OperationCanceledException()));
    }

    ///<summary>
    ///And through the history, which is where it is read: a row read at a pace is the row read in one go - its file, and
    ///the name of the writing it is, which says whether the steps saved since may be taken again.
    ///</summary>
    [Fact]
    public async Task A_history_row_read_at_a_pace_is_the_row_read_whole()
    {
        byte[] file = design("tt_um_oscillating_bones.gds");
        var history = new HistoryStore(new AppStorage(new Browser()));
        var session = new SavedSession { FileName = "tt_um_oscillating_bones", BytesApart = true, Base = "3f9c2a7b81d0" };

        await history.RememberEncodedAsync(new HistoryEntry { Name = "tt_um_oscillating_bones.gds" }, AppStorage.Encode(SavedSession.Serialize(session)), storedBeside("3f9c2a7b81d0", file));

        var whole = await history.ReadAsync("tt_um_oscillating_bones.gds");
        var paced = await history.ReadAsync("tt_um_oscillating_bones.gds", new Counting().Pause);

        Assert.NotNull(whole);
        Assert.NotNull(paced);
        Assert.True(whole!.CarriesFile);
        Assert.True(paced!.CarriesFile);
        Assert.Equal(file, await whole.FileAsync(new Counting().Pause));
        Assert.Equal(file, await paced.FileAsync(new Counting().Pause));
        Assert.True(whole.JournalFits);
        Assert.True(paced.JournalFits);
    }

    ///<summary>A row whose bytes are gone opens nothing, read either way.</summary>
    [Fact]
    public async Task A_history_row_without_its_bytes_reads_as_nothing_at_a_pace_too()
    {
        var browser = new Browser();
        var history = new HistoryStore(new AppStorage(browser));
        var session = new SavedSession { FileName = "tt_um_oscillating_bones", BytesApart = true, Base = "3f9c2a7b81d0" };

        await history.RememberEncodedAsync(new HistoryEntry { Name = "tt_um_oscillating_bones.gds" }, AppStorage.Encode(SavedSession.Serialize(session)), storedBeside("3f9c2a7b81d0", design("tt_um_oscillating_bones.gds")));

        browser.Stored.Remove(HistoryStore.bytesKey("tt_um_oscillating_bones.gds"));

        Assert.Null(await history.ReadAsync("tt_um_oscillating_bones.gds"));
        Assert.Null(await history.ReadAsync("tt_um_oscillating_bones.gds", new Counting().Pause));
    }

    ///<summary>Lengths about the end of a run: 65,536 characters of base64 are 49,152 bytes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(49151)]
    [InlineData(49152)]
    [InlineData(49153)]
    [InlineData(200000)]
    public async Task Base64_read_at_a_pace_is_the_bytes_read_whole(int length)
    {
        byte[] bytes = new byte[length];

        new Random(length).NextBytes(bytes);

        string text = Convert.ToBase64String(bytes);

        Assert.Equal(bytes, await AppStorage.FromBase64Async(text, 0, new Counting().Pause));

        //And from partway along, the way a stored value is read past its marker.
        Assert.Equal(bytes, await AppStorage.FromBase64Async("z" + text, 1, new Counting().Pause));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ab!d")]
    public async Task What_is_not_base64_throws_at_a_pace_as_it_does_whole(string text)
    {
        Assert.Throws<FormatException>(() => Convert.FromBase64String(text));

        await Assert.ThrowsAsync<FormatException>(async () => await AppStorage.FromBase64Async(text, 0, new Counting().Pause));
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
            SpacingRange = 5000,
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
        Assert.Equal(5000, read.SpacingRange);
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
        Assert.Equal(new[] { "67/20" }, read.LockedLayers.ToArray());
        Assert.Equal(new[] { "met1.1" }, read.RulesOff.ToArray());
        Assert.Equal(new[] { "li1.1@-750,100,100,400" }, read.Waived.ToArray());
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
            BytesApart = true,
            View = "2d",
            LayerNames = "65,20,diff.drawing,#00ff00\n",
            CarriedLayerNames = "65,20,diff.drawing\n",
            HiddenLayers = new List<string> { "66/44" },
            LockedLayers = new List<string> { "67/20" },
            RulesOff = new List<string> { "met1.1" },
            Waived = new List<string> { "li1.1@-750,100,100,400" },
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
            SpacingRange = 5000,
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

    ///<summary>
    ///**A session written before the distance slider's reach could be set opens on the reach it had then.** The
    ///key is simply missing from those, and 700 is what the slider stopped at - so the slider comes back as it was
    ///left rather than on some other reach nobody chose.
    ///</summary>
    [Fact]
    public void A_session_from_before_the_reach_box_opens_on_the_old_reach()
    {
        string written = SavedSession.Serialize(new SavedSession { FileName = "Mosfet", LayerSpacing = 350 });
        string older = written.Replace(",\"sr\":700", "").Replace("\"sr\":700,", "");

        Assert.DoesNotContain("\"sr\"", older);

        var read = SavedSession.Deserialize(older);

        Assert.NotNull(read);
        Assert.Equal(350, read!.LayerSpacing);
        Assert.Equal(700, read.SpacingRange);
    }

    ///<summary>
    ///**The names that carry to the next file are kept apart from the file's own**, and an empty set of them is
    ///still said - "nothing of the visitor's own" - rather than left out, which is how a session from before reads.
    ///The file's names here are the bundled ones an example arrives with; none of them is the visitor's.
    ///</summary>
    [Fact]
    public void The_names_that_carry_are_kept_apart_from_the_files_own()
    {
        var session = new SavedSession { FileName = "Mosfet", LayerNames = "65,20,diff,#e69ac5\n", CarriedLayerNames = "" };

        string json = SavedSession.Serialize(session);

        Assert.Contains("\"lc\":\"\"", json);

        var read = SavedSession.Deserialize(json);

        Assert.Equal("65,20,diff,#e69ac5\n", read!.LayerNames);
        Assert.Equal("", read.CarriedLayerNames);
    }

    ///<summary>A session written before then has no key for them at all, which reads as not knowing - so its file's names carry, as they did.</summary>
    [Fact]
    public void A_session_from_before_the_names_were_kept_apart_has_no_set_of_them()
    {
        string json = SavedSession.Serialize(new SavedSession { FileName = "Mosfet", LayerNames = "65,20,diff\n" });

        Assert.DoesNotContain("\"lc\"", json);
        Assert.Null(SavedSession.Deserialize(json)!.CarriedLayerNames);
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
    ///**A session whose file is kept beside it says so, and carries none of it.** The bytes are written under
    ///their own key when the file changes, so that a save - every pan that comes to rest - is the settings
    ///alone; see SavedSession.BytesApart. What is pinned here is the shape: the flag goes in and comes back,
    ///and the bytes field is empty.
    ///</summary>
    [Fact]
    public void A_session_keeping_its_file_apart_says_so_and_carries_none_of_it()
    {
        string json = SavedSession.Serialize(new SavedSession { FileName = "Mosfet", BytesApart = true });

        Assert.Contains("\"ba\":true", json);
        Assert.Contains("\"b\":\"\"", json);
        Assert.True(SavedSession.Deserialize(json)!.BytesApart);
    }

    ///<summary>
    ///And one that keeps nothing apart is written exactly as before there was such a thing - with no key for it
    ///at all - which is also how every session already stored reads as keeping its bytes inside.
    ///</summary>
    [Fact]
    public void A_session_keeping_nothing_apart_is_written_as_it_was()
    {
        string json = SavedSession.Serialize(new SavedSession { FileName = "Mosfet", FileBytes = "AAEC" });

        Assert.DoesNotContain("\"ba\"", json);
        Assert.False(SavedSession.Deserialize(json)!.BytesApart);
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
