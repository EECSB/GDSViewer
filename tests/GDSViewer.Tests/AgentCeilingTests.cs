using System.Text.Json;
using GdsII;
using GdsII.Cli;

namespace GDSViewer.Tests;

///<summary>
///How far the AI layout tools flatten a layout: the app's ceiling where they run in a tab, the command line's under
///`gds mcp`, and said in what they answer when a flatten stops all the same. See AgentSession.MostElements.
///
///**With a ceiling given to one session**, so nothing here lowers the one every other test flattens under.
///</summary>
public class AgentCeilingTests : IDisposable
{
    #region Layouts, a deck and a folder ******************************************************

    ///<summary>A hundred-shape cell arrayed ten by ten: ten thousand elements, so a ceiling of five hundred stops it early.</summary>
    private static GDS Layout()
    {
        return Synthetic.Layout(100, 10, 10);
    }

    ///<summary>One rule nothing in <see cref="Layout"/> breaks, so an answer says something only about what it did not see.</summary>
    private const string Deck = """
        layer M 64/20
        rule M.1 width M 1 "Metal width"

        """;

    ///<summary>A cell placed once in the top that arrays a square thirty by thirty - nine hundred elements under one placement.</summary>
    private const string Nested = """
        HEADER: 600
        BGNLIB: 122 12 13 16 59 44 123 4 22 14 56 21
        LIBNAME: LIB
        UNITS: 0.001 1E-09
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: LEAF
        BOUNDARY:
        LAYER: 64
        DATATYPE: 20
        XY: 0 0 1000 0 1000 1000 0 1000 0 0
        ENDEL:
        ENDSTR:
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: MID
        AREF:
        SNAME: LEAF
        COLROW: 30 30
        XY: 0 0 60000 0 0 60000
        ENDEL:
        ENDSTR:
        BGNSTR: 122 12 13 16 59 44 123 4 22 14 56 21
        STRNAME: TOP
        SREF:
        SNAME: MID
        XY: 0 0
        ENDEL:
        ENDSTR:
        ENDLIB:

        """;

    private readonly string folder = Path.Combine(Path.GetTempPath(), $"gdsceiling-{Guid.NewGuid():N}");

    public AgentCeilingTests()
    {
        Directory.CreateDirectory(folder);

        File.WriteAllBytes(Path.Combine(folder, "layout.gds"), Layout().Serialize());
        File.WriteAllText(Path.Combine(folder, "rules.drc"), Deck);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch { }
    }

    private static AgentSession Session(GDS gds, int most)
    {
        return new AgentSession(gds, new EditHistory())
        {
            Deck = DrcDeck.Parse(Deck),
            MostElements = most
        };
    }

    ///<summary>A call that has to work.</summary>
    private static string Done(AgentSession session, string tool, string arguments = "{}")
    {
        var answer = AgentTools.Call(session, tool, arguments);

        Assert.False(answer.IsError, answer.Text);

        return answer.Text;
    }

    ///<summary>`gds mcp` started on the folder with these options, its live link off.</summary>
    private McpSession Mcp(params string[] options)
    {
        var error = new StringWriter();
        var args = new[] { folder, "--link-port", "0" }.Concat(options).ToArray();

        Assert.Equal(Cli.Ok, Cli.McpSessionFrom(args, error, out McpSession? session));

        return session!;
    }

    private static string Done(McpSession session, string tool, string arguments = "{}")
    {
        using var document = JsonDocument.Parse(arguments);

        var answer = session.Call(tool, document.RootElement.Clone());

        Assert.False(answer.IsError, answer.Text);

        return answer.Text;
    }

    #endregion ****************************************************************************



    #region Where the ceiling comes from ******************************************************

    ///<summary>The app's assistant is where the app's ceiling was measured, so it keeps it.</summary>
    [Fact]
    public void The_apps_assistant_flattens_as_far_as_the_app_does()
    {
        Assert.Equal(GdsFlattener.MostElements, new AgentSession(GDS.NewLibrary(), new EditHistory()).MostElements);
    }

    ///<summary>`gds mcp` runs where the command line does, so its tools flatten as far as every other command.</summary>
    [Fact]
    public void Gds_mcp_flattens_as_far_as_the_command_line()
    {
        var session = Mcp("--deck", Path.Combine(folder, "rules.drc"));

        Done(session, "open_file", """{"path": "layout.gds"}""");

        Assert.Equal(Cli.MostElements, session.MostElements);
        Assert.Equal(Cli.MostElements, session.Layout!.MostElements);
        Assert.True(Cli.MostElements > GdsFlattener.MostElements);
    }

    [Fact]
    public void Gds_mcp_takes_most_elements_as_every_command_does()
    {
        var session = Mcp("--most-elements", "500");

        Done(session, "open_file", """{"path": "layout.gds"}""");

        Assert.Equal(500, session.Layout!.MostElements);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("many")]
    public void A_ceiling_that_is_not_a_count_stops_gds_mcp_starting(string most)
    {
        var error = new StringWriter();

        Assert.Equal(Cli.UsageError, Cli.McpSessionFrom(new[] { folder, "--most-elements", most }, error, out _));

        Assert.Contains("--most-elements needs a whole number of one or more.", error.ToString());
    }

    ///
    ///**The point of it, through the protocol's own door**: the same check over the same file says what it did not see
    ///under a ceiling of five hundred, and nothing of the kind under the command line's.
    ///
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Check_rules_under_gds_mcp_goes_as_far_as_its_ceiling(bool lowered)
    {
        var options = new List<string> { "--deck", Path.Combine(folder, "rules.drc") };

        if (lowered)
            options.AddRange(new[] { "--most-elements", "500" });

        var session = Mcp(options.ToArray());

        Done(session, "open_file", """{"path": "layout.gds"}""");

        string said = Done(session, "check_rules", """{"everything": true}""");

        Assert.Equal(lowered, said.Contains("\nNot checked: Only the first ", StringComparison.Ordinal));
    }

    #endregion ****************************************************************************



    #region Said where it stops ****************************************************************

    ///
    ///A check over an area is a window cut out of the flatten, and says where the flatten stopped - not how many shapes
    ///the window holds, which is what it said before the window took its sentence from the flatten it was cut from.
    ///
    [Fact]
    public void A_window_says_where_the_flatten_stopped_not_how_many_shapes_it_holds()
    {
        int stopped = GdsFlattener.Flatten(Layout(), 500).Elements.Count;

        string said = Done(Session(Layout(), 500), "check_rules", """{"everything": true, "area": [0, 0, 1, 1]}""");

        Assert.Contains(FormattableString.Invariant($"\nNot checked: Only the first {stopped:N0} elements of the layout were checked"), said);
    }

    ///<summary>open_cell gives the cell's extent, and says it is the part the flatten got to when it stopped.</summary>
    [Theory]
    [InlineData(500, true)]
    [InlineData(1000, false)]
    public void Open_cell_says_its_extent_is_as_far_as_the_flatten_got(int most, bool cut)
    {
        string said = Done(Session(GDS.FromText(Nested), most), "open_cell", """{"name": "TOP"}""");

        Assert.Contains("covering ", said);
        Assert.Equal(cut, said.Contains(" elements go, where flattening it stopped", StringComparison.Ordinal));
    }

    ///<summary>And so does a placement, whose extent is everything the cell it places draws.</summary>
    [Theory]
    [InlineData(500, true)]
    [InlineData(1000, false)]
    public void A_placement_says_its_extent_is_as_far_as_the_flatten_got(int most, bool cut)
    {
        var session = Session(GDS.FromText(Nested), most);

        Done(session, "open_cell", """{"name": "TOP"}""");

        string said = Done(session, "find_shapes", """{"kind": "placement"}""");

        Assert.Contains("placement of MID at (0, 0), covering ", said);
        Assert.Equal(cut, said.Contains(" elements go, where flattening it stopped", StringComparison.Ordinal));
    }

    ///
    ///The check after a change flattens the cell it is in: a change past where that stopped is not in what was checked,
    ///and the answer's "nothing broken" says so.
    ///
    [Theory]
    [InlineData(500, true)]
    [InlineData(20_000, false)]
    public void The_check_after_a_change_says_what_it_could_not_see(int most, bool cut)
    {
        string said = Done(Session(Layout(), most), "draw_rectangle", """{"layer": "64/20", "from": [100, 100], "to": [101, 101]}""");

        Assert.Contains("Rules: nothing broken around this change.", said);
        Assert.Equal(cut, said.Contains(" Not checked: Only the first ", StringComparison.Ordinal));
    }

    #endregion ****************************************************************************
}
