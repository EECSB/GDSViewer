using GDSViewer.Ai;
using GdsII;

namespace GDSViewer.Tests;

///
///What a change the assistant made did to the file's text, which its card shows as a diff - asked for 2026-10-21. The
///lines that changed are found from both ends, with a few either side to say where they are; the runner writes the
///text before and after each change, and only when the window asks.
///
public class AiTextChangeTests
{
    private static readonly LayerKey Met1 = new LayerKey(68, 20);

    ///<summary>Lines ended as the file's text ends them, with a space and a newline.</summary>
    private static string Lines(params string[] lines)
    {
        return string.Join("", lines.Select(line => line + " \n"));
    }

    ///<summary>The lines with a mark after two of them.</summary>
    private static string[] Marked(string[] lines, string one, string other)
    {
        var marked = new List<string>();

        foreach (string line in lines)
        {
            if (line == one || line == other)
                marked.Add(line + "!");
            else
                marked.Add(line);
        }

        return marked.ToArray();
    }

    private static readonly string[] Ten = Enumerable.Range(1, 10).Select(number => number.ToString()).ToArray();

    #region The lines that changed *************************************************

    [Fact]
    public void Nothing_changed_is_no_change()
    {
        Assert.Null(TextChange.Between(Lines(Ten), Lines(Ten)));
    }

    ///<summary>A line put in the middle: it, and three unchanged either side, numbered from where they are.</summary>
    [Fact]
    public void A_line_added_comes_with_three_either_side()
    {
        var after = Ten.Take(6).Append("new").Concat(Ten.Skip(6)).ToArray();

        var change = TextChange.Between(Lines(Ten), Lines(after))!;

        Assert.Equal(4, change.FirstLine);
        Assert.Equal("4 \n5 \n6 \n7 \n8 \n9 ", change.Before);
        Assert.Equal("4 \n5 \n6 \nnew \n7 \n8 \n9 ", change.After);
    }

    ///<summary>A line taken away at the very start: there is nothing before it to show.</summary>
    [Fact]
    public void A_line_taken_from_the_start_starts_at_the_first_line()
    {
        var change = TextChange.Between(Lines(Ten), Lines(Ten.Skip(1).ToArray()))!;

        Assert.Equal(1, change.FirstLine);
        Assert.Equal("1 \n2 \n3 \n4 ", change.Before);
        Assert.Equal("2 \n3 \n4 ", change.After);
    }

    ///<summary>One changed at the end runs to the end - and no further: the newline that ends the last line starts no other.</summary>
    [Fact]
    public void A_line_changed_at_the_end_runs_to_the_end()
    {
        var after = Ten.Take(9).Append("ten").ToArray();

        var change = TextChange.Between(Lines(Ten), Lines(after))!;

        Assert.Equal(7, change.FirstLine);
        Assert.Equal("7 \n8 \n9 \n10 ", change.Before);
        Assert.Equal("7 \n8 \n9 \nten ", change.After);
    }

    ///<summary>Two changes far apart are one part, everything between them included, for the diff to show unchanged.</summary>
    [Fact]
    public void Two_changes_far_apart_are_one_part()
    {
        var change = TextChange.Between(Lines(Ten), Lines(Marked(Ten, "2", "9")))!;

        Assert.Equal(1, change.FirstLine);
        Assert.StartsWith("1 \n2! \n3 ", change.After);
        Assert.EndsWith("8 \n9! \n10 ", change.After);
    }

    ///<summary>Text that starts further into the file - one cell's lines - is numbered from where it starts.</summary>
    [Fact]
    public void A_text_from_further_in_is_numbered_from_there()
    {
        var after = Ten.Take(6).Append("new").Concat(Ten.Skip(6)).ToArray();

        Assert.Equal(103, TextChange.Between(Lines(Ten), Lines(after), 100)!.FirstLine);
    }

    #endregion *********************************************************************



    #region What a change did ******************************************************

    private static AgentSession Session()
    {
        var gds = GDS.NewLibrary(stamp: new DateTime(2026, 10, 9));

        gds.AdditionalInformation.AddLayer(Met1);
        gds.AdditionalInformation.Layers[Met1].Name = "met1";

        var session = new AgentSession(gds, new EditHistory());

        Assert.False(AgentTools.Call(session, "draw_rectangle", """{"layer":"met1","from":[0,0],"to":[2,1]}""").IsError);

        return session;
    }

    ///
    ///**A rectangle drawn is its records put in**: one BOUNDARY more after than before, on met1, at the lines the file's
    ///own text has them on.
    ///
    [Fact]
    public async Task A_change_says_what_it_did_to_the_text()
    {
        var session = Session();
        var runner = new LayoutToolRunner(session) { AllowEdits = true, DiffsText = true };

        var output = await runner.RunToolAsync("draw_rectangle", """{"layer":"met1","from":[5,5],"to":[6,6]}""", CancellationToken.None);

        Assert.False(output.IsError, output.Text);

        var change = output.TextChange!;
        int before = change.Before.Split('\n').Count(line => line.StartsWith("BOUNDARY"));

        Assert.Equal(before + 1, change.After.Split('\n').Count(line => line.StartsWith("BOUNDARY")));
        Assert.Contains("LAYER: 68 ", change.After);

        string[] file = session.Gds.AsText().Split('\n');

        Assert.Equal(change.After.Split('\n')[0], file[change.FirstLine - 1]);
    }

    ///
    ///**Past the most records written whole, the cell worked in alone** - its BGNSTR to its ENDSTR, so nothing of the
    ///library's own ending - still numbered as the file's text numbers it.
    ///
    [Fact]
    public async Task Past_the_most_written_whole_it_is_the_cell_alone()
    {
        var session = Session();
        var runner = new LayoutToolRunner(session) { AllowEdits = true, DiffsText = true, MostRecordsWhole = 0 };

        var output = await runner.RunToolAsync("draw_rectangle", """{"layer":"met1","from":[5,5],"to":[6,6]}""", CancellationToken.None);

        var change = output.TextChange!;
        string[] file = session.Gds.AsText().Split('\n');

        Assert.DoesNotContain("ENDLIB", change.After);
        Assert.Contains("BOUNDARY", change.After);
        Assert.Equal(change.After.Split('\n')[0], file[change.FirstLine - 1]);
    }

    ///<summary>Not unless asked, and never for a look or a refusal: nothing changed, so there is nothing to show.</summary>
    [Fact]
    public async Task Only_a_change_made_says_so_and_only_when_asked()
    {
        var unasked = new LayoutToolRunner(Session()) { AllowEdits = true };
        var asked = new LayoutToolRunner(Session()) { AllowEdits = true, DiffsText = true };

        Assert.Null((await unasked.RunToolAsync("draw_rectangle", """{"layer":"met1","from":[5,5],"to":[6,6]}""", CancellationToken.None)).TextChange);
        Assert.Null((await asked.RunToolAsync("find_shapes", "{}", CancellationToken.None)).TextChange);

        var refused = await asked.RunToolAsync("draw_rectangle", """{"layer":"nowhere","from":[5,5],"to":[6,6]}""", CancellationToken.None);

        Assert.True(refused.IsError);
        Assert.Null(refused.TextChange);
    }

    #endregion *********************************************************************
}
