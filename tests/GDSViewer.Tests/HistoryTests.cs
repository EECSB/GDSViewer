using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///The history list's own rules: what makes two rows the same file, where a file lands when it is opened
///again, what falls off the end, and what a stored list that cannot be read costs.
///
///The store around this is IndexedDB and is covered end to end instead. What is worth testing here is the
///part that would silently make the list wrong rather than fail: a duplicate row that still opens and hands
///back a state that has been superseded, or a cap that drops the wrong end.
///</summary>
public class HistoryTests
{
    private const int Capacity = HistoryStore.Capacity;

    private static HistoryEntry Entry(string name, bool edited = false)
    {
        return new HistoryEntry { Name = name, Edited = edited };
    }

    private static string[] Names(HistoryIndex index)
    {
        return index.Entries.Select(entry => entry.Name).ToArray();
    }

    #region Ordering ******************************************************************

    [Fact]
    public void The_newest_file_is_first()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("one.gds"), Capacity);
        index.Remember(Entry("two.gds"), Capacity);
        index.Remember(Entry("three.gds"), Capacity);

        Assert.Equal(new[] { "three.gds", "two.gds", "one.gds" }, Names(index));
    }

    ///<summary>
    ///The rule the user asked for: a file opened again moves up rather than being listed twice.
    ///</summary>
    [Fact]
    public void A_file_seen_again_is_moved_to_the_front()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("one.gds"), Capacity);
        index.Remember(Entry("two.gds"), Capacity);
        index.Remember(Entry("three.gds"), Capacity);

        index.Remember(Entry("one.gds"), Capacity);

        Assert.Equal(new[] { "one.gds", "three.gds", "two.gds" }, Names(index));
    }

    ///<summary>
    ///And there is only one of it. A second row for the same file would still be openable, and would hand
    ///back a state that has since been superseded - which is worse than not having the history at all.
    ///</summary>
    [Fact]
    public void A_file_seen_again_is_not_listed_twice()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("one.gds"), Capacity);
        index.Remember(Entry("one.gds"), Capacity);
        index.Remember(Entry("one.gds"), Capacity);

        Assert.Single(index.Entries);
    }

    ///<summary>The row that replaces one carries the newer state, not the older.</summary>
    [Fact]
    public void The_newer_row_replaces_the_older_one()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("one.gds", edited: false), Capacity);
        index.Remember(Entry("one.gds", edited: true), Capacity);

        Assert.True(index.Entries.Single().Edited);
    }

    ///<summary>
    ///Windows would otherwise let one file be two rows. Nothing in the app lowercases a file's name on the
    ///way in, so the comparison is where this has to be handled.
    ///</summary>
    [Fact]
    public void The_same_name_in_another_case_is_the_same_file()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("Mosfet.gds"), Capacity);
        index.Remember(Entry("mosfet.GDS"), Capacity);

        Assert.Single(index.Entries);
    }

    #endregion ***********************************************************************



    #region The cap ******************************************************************

    [Fact]
    public void Nothing_is_dropped_below_the_cap()
    {
        var index = new HistoryIndex();

        for (int n = 0; n < Capacity; n++)
            Assert.Empty(index.Remember(Entry($"file{n}.gds"), Capacity));

        Assert.Equal(Capacity, index.Entries.Count);
    }

    ///<summary>The oldest goes, and the caller is told which so its payload can go with it.</summary>
    [Fact]
    public void Past_the_cap_the_oldest_is_dropped_and_named()
    {
        var index = new HistoryIndex();

        for (int n = 0; n < Capacity; n++)
            index.Remember(Entry($"file{n}.gds"), Capacity);

        var dropped = index.Remember(Entry("newest.gds"), Capacity);

        Assert.Equal(new[] { "file0.gds" }, dropped.ToArray());
        Assert.Equal(Capacity, index.Entries.Count);
        Assert.DoesNotContain("file0.gds", Names(index));
        Assert.Equal("newest.gds", index.Entries[0].Name);
    }

    ///<summary>
    ///Re-opening a file that is already listed must not push another one off the end: it is a move, and a
    ///move does not change how many rows there are.
    ///</summary>
    [Fact]
    public void Moving_a_file_up_does_not_push_one_off_the_end()
    {
        var index = new HistoryIndex();

        for (int n = 0; n < Capacity; n++)
            index.Remember(Entry($"file{n}.gds"), Capacity);

        var dropped = index.Remember(Entry("file0.gds"), Capacity);

        Assert.Empty(dropped);
        Assert.Equal(Capacity, index.Entries.Count);
    }

    ///<summary>
    ///A list that is already over the cap - written by a build with a larger one - is trimmed back on the
    ///next write rather than kept forever.
    ///</summary>
    [Fact]
    public void An_oversized_list_is_trimmed_back()
    {
        var index = new HistoryIndex();

        for (int n = 0; n < Capacity + 5; n++)
            index.Entries.Add(Entry($"file{n}.gds"));

        var dropped = index.Remember(Entry("newest.gds"), Capacity);

        Assert.Equal(6, dropped.Count);
        Assert.Equal(Capacity, index.Entries.Count);
    }

    #endregion ***********************************************************************



    #region Removing *****************************************************************

    [Fact]
    public void Forgetting_a_file_removes_its_row()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("one.gds"), Capacity);
        index.Remember(Entry("two.gds"), Capacity);

        Assert.True(index.Forget("one.gds"));
        Assert.Equal(new[] { "two.gds" }, Names(index));
    }

    ///<summary>False, so the caller can skip a write that would change nothing.</summary>
    [Fact]
    public void Forgetting_a_file_that_is_not_listed_says_so()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("one.gds"), Capacity);

        Assert.False(index.Forget("other.gds"));
        Assert.Single(index.Entries);
    }

    [Fact]
    public void Contains_answers_for_the_name_in_any_case()
    {
        var index = new HistoryIndex();

        index.Remember(Entry("Mosfet.gds"), Capacity);

        Assert.True(index.Contains("mosfet.gds"));
        Assert.False(index.Contains("other.gds"));
    }

    #endregion ***********************************************************************



    #region Reading it back **********************************************************

    [Fact]
    public void An_index_round_trips()
    {
        var index = new HistoryIndex();

        index.Remember(new HistoryEntry { Name = "one.gds", ExampleName = "Mosfet.gds", Edited = true, When = "2026-08-01T05:00:00.0000000Z" }, Capacity);
        index.Remember(new HistoryEntry { Name = "two.gds" }, Capacity);

        var read = HistoryIndex.Deserialize(HistoryIndex.Serialize(index));

        Assert.Equal(new[] { "two.gds", "one.gds" }, Names(read));

        var entry = read.Entries[1];

        Assert.Equal("Mosfet.gds", entry.ExampleName);
        Assert.True(entry.Edited);
        Assert.Equal("2026-08-01T05:00:00.0000000Z", entry.When);
    }

    ///<summary>
    ///**The generated serializer writes exactly what the reflecting one wrote**, so an index already in
    ///somebody's browser still lists their files. See the same test over a session in
    ///<see cref="StorageTests"/> for why the two serializers are compared rather than round-tripped.
    ///</summary>
    [Fact]
    public void An_index_is_written_the_way_it_always_was()
    {
        var index = new HistoryIndex();

        index.Remember(new HistoryEntry { Name = "one.gds", ExampleName = "Mosfet.gds", Edited = true, When = "2026-08-01T05:00:00.0000000Z" }, Capacity);
        index.Remember(new HistoryEntry { Name = "two.gds" }, Capacity);

        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(index), HistoryIndex.Serialize(index));
    }

    ///<summary>
    ///It survives what a session does not: this is read on the path that starts the app, and there is
    ///nothing to fall back to, so a stored list that cannot be read has to become an empty one.
    ///</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"v\":1,")]
    [InlineData("[]")]
    public void Anything_that_is_not_an_index_reads_as_an_empty_one(string? stored)
    {
        var read = HistoryIndex.Deserialize(stored);

        Assert.NotNull(read);
        Assert.Empty(read.Entries);
    }

    [Fact]
    public void An_index_from_another_version_is_dropped()
    {
        string json = HistoryIndex.Serialize(new HistoryIndex())
            .Replace($"\"v\":{HistoryIndex.CurrentVersion}", "\"v\":99");

        Assert.Empty(HistoryIndex.Deserialize(json).Entries);
    }

    ///<summary>
    ///A row with no name has no payload key, so it can be neither opened nor deleted - it would sit in the
    ///list forever doing nothing.
    ///</summary>
    [Fact]
    public void A_row_with_no_name_is_dropped_on_the_way_in()
    {
        string json = HistoryIndex.Serialize(new HistoryIndex
        {
            Entries = new List<HistoryEntry> { Entry(""), Entry("one.gds") }
        });

        Assert.Equal(new[] { "one.gds" }, Names(HistoryIndex.Deserialize(json)));
    }

    ///<summary>The whole point of the split: the row is small, and the file it points at is not.</summary>
    [Fact]
    public void A_payload_is_kept_under_a_key_of_its_own()
    {
        Assert.Equal("gdsviewer.history.Mosfet.gds", HistoryStore.payloadKey("Mosfet.gds"));
        Assert.NotEqual(HistoryStore.IndexKey, HistoryStore.payloadKey("Mosfet.gds"));
    }

    #endregion ***********************************************************************
}
