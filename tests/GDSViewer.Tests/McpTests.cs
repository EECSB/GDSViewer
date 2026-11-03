using System.Diagnostics;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using GdsII;
using GdsII.Cli;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using static GdsII.GDS;

namespace GDSViewer.Tests;

///
///`gds mcp`: the folder it keeps to, the six file tools it adds to the library's, and the protocol around them.
///
///Most of it calls <see cref="McpSession"/> directly - the tools as calls and answers, with no protocol in the
///way - and checks what each leaves on disk. The rest goes through the protocol: in process over a pair of
///pipes with the SDK's own client, and once as a real `gds mcp` process, which is the only way to see that
///nothing but protocol reaches its standard output.
///
///Each test has a folder of its own under the system's temporary directory, with a layout beside it - outside
///it - that nothing may read or write, and removes both when it is done.
///
public class McpTests : IDisposable
{
    #region Helpers *******************************************************************

    private static readonly LayerKey Met1 = new LayerKey(68, 20);

    private readonly string scratch;
    private readonly string folder;
    private readonly string outside;

    public McpTests()
    {
        scratch = Path.Combine(Path.GetTempPath(), $"gdsmcp-{Guid.NewGuid():N}");
        folder = Path.Combine(scratch, "layouts");

        Directory.CreateDirectory(folder);

        outside = Path.Combine(scratch, "outside.gds");

        File.WriteAllBytes(outside, Mosfet);
        File.WriteAllBytes(In("Mosfet.gds"), Mosfet);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(scratch, true);
        }
        catch { }
    }

    private static byte[] Mosfet
    {
        get { return GdsTestData.ReadSample(GdsTestData.MosfetSample); }
    }

    ///<summary>A path in the served folder.</summary>
    private string In(string relative)
    {
        return Path.Combine(folder, relative);
    }

    private McpSession Session()
    {
        return new McpSession(new ServedFolder(folder));
    }

    private static AgentResult Call(McpSession session, string tool, string arguments = "{}")
    {
        using var document = JsonDocument.Parse(arguments);

        return session.Call(tool, document.RootElement.Clone());
    }

    ///<summary>A call that has to work.</summary>
    private static string Done(McpSession session, string tool, string arguments = "{}")
    {
        var result = Call(session, tool, arguments);

        Assert.False(result.IsError, result.Text);

        return result.Text;
    }

    ///<summary>A call that has to be refused.</summary>
    private static string Refused(McpSession session, string tool, string arguments = "{}")
    {
        var result = Call(session, tool, arguments);

        Assert.True(result.IsError, result.Text);

        return result.Text;
    }

    ///<summary>Arguments naming one path, written as JSON - which a Windows path's backslashes have to be.</summary>
    private static string PathArgument(string path, string more = "")
    {
        return $"{{\"path\": {JsonSerializer.Serialize(path)}{more}}}";
    }

    ///<summary>Every file under the scratch folder - inside and out - with its bytes, to show nothing moved.</summary>
    private Dictionary<string, byte[]> Everything()
    {
        var found = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories))
            found[file] = File.ReadAllBytes(file);

        return found;
    }

    private static void Unchanged(Dictionary<string, byte[]> before, Dictionary<string, byte[]> after)
    {
        Assert.Equal(before.Keys.OrderBy(name => name), after.Keys.OrderBy(name => name));

        foreach (var (name, bytes) in before)
            Assert.Equal(bytes, after[name]);
    }

    ///<summary>How many shapes a layout file on disk has on met1, whichever of the formats it is in.</summary>
    private static int OnMet1(string path)
    {
        using var stream = File.OpenRead(path);

        var gds = Cli.ReadLayout(stream, out _);

        return GdsFlattener.Flatten(gds).Elements.Count(element => element.Layer.Key == Met1);
    }

    private const string Rectangle = """{"layer": "68/20", "from": [100, 100], "to": [102, 101]}""";

    #endregion **************************************************************************



    #region The tools *******************************************************************

    [Fact]
    public void The_library_tools_are_all_served_after_the_six_file_tools_and_the_four_of_the_live_link()
    {
        var names = Session().Tools.Select(tool => tool.Name).ToList();

        var expected = new List<string>
        {
            "list_files", "open_file", "new_file", "save_file", "load_layermap", "load_rules",
            "link_tab", "unlink_tab", "look", "describe_tab"
        };

        expected.AddRange(AgentTools.All.Select(tool => tool.Name));

        Assert.Equal(expected, names);
        Assert.Equal(41, names.Count);
    }

    [Fact]
    public void Only_readers_are_read_only_and_only_what_can_lose_something_is_destructive()
    {
        var tools = Session().Tools.ToDictionary(tool => tool.Name);

        foreach (var tool in AgentTools.All)
        {
            Assert.Equal(!tool.Edits, tools[tool.Name].ReadsOnly);

            //An edit is in memory and undoable; nothing on disk moves until save_file.
            Assert.False(tools[tool.Name].Destroys, tool.Name);
        }

        Assert.True(tools["list_files"].ReadsOnly);

        //Linking changes what the tools work on, not anything they work on; looking and describing read.
        Assert.False(tools["link_tab"].ReadsOnly);
        Assert.False(tools["unlink_tab"].ReadsOnly);
        Assert.True(tools["look"].ReadsOnly);
        Assert.True(tools["describe_tab"].ReadsOnly);

        Assert.Equal(
            new[] { "new_file", "open_file", "save_file" },
            tools.Values.Where(tool => tool.Destroys).Select(tool => tool.Name).OrderBy(name => name));
    }

    [Fact]
    public void Every_schema_is_an_object_whose_required_arguments_it_describes()
    {
        foreach (var tool in Session().Tools)
        {
            Assert.Equal("object", (string?)tool.InputSchema["type"]);

            var properties = tool.InputSchema["properties"]!.AsObject();

            if (tool.InputSchema["required"] is JsonArray required)
            {
                foreach (var name in required)
                    Assert.True(properties.ContainsKey((string)name!), $"{tool.Name} requires {name}, which it does not describe.");
            }
        }
    }

    [Fact]
    public void Before_anything_is_open_the_library_tools_say_how_to_open_a_layout()
    {
        string said = Refused(Session(), "list_layers");

        Assert.Contains("No layout is open", said);
        Assert.Contains("open_file", said);
        Assert.Contains("new_file", said);
    }

    [Fact]
    public void A_tool_that_does_not_exist_is_refused_with_every_tool_named()
    {
        string said = Refused(Session(), "draw_unicorn");

        Assert.Contains("There is no tool called draw_unicorn", said);
        Assert.Contains("save_file", said);
        Assert.Contains("draw_rectangle", said);
    }

    [Fact]
    public void Arguments_are_strict_about_names_and_forgiving_about_how_a_value_is_written()
    {
        var session = Session();

        string said = Refused(session, "open_file", """{"file": "Mosfet.gds"}""");

        Assert.Contains("open_file does not take \"file\"", said);
        Assert.Contains("It takes path, discard_changes.", said);

        Assert.Contains("needs \"path\"", Refused(session, "open_file", "{}"));
        Assert.Contains("have to be a JSON object", Refused(session, "open_file", "[\"Mosfet.gds\"]"));

        //A flag and a number sent as text are what was meant.
        Done(session, "new_file", """{"path": "a.gds", "grid": "0.0005", "discard_changes": "true"}""");

        Assert.Equal(0.0005, session.Layout!.MicronsPerUnit, 12);
    }

    #endregion **************************************************************************



    #region The folder ******************************************************************

    [Theory]
    [InlineData("../outside.gds")]
    [InlineData("cells/../../outside.gds")]
    [InlineData("..")]
    public void A_path_out_of_the_folder_is_refused(string path)
    {
        var session = Session();
        var before = Everything();

        Assert.Contains("outside the folder", Refused(session, "open_file", PathArgument(path)));
        Assert.Contains("outside the folder", Refused(session, "load_rules", PathArgument(path)));
        Assert.Contains("outside the folder", Refused(session, "list_files", $"{{\"folder\": {JsonSerializer.Serialize(path)}}}"));
        Assert.Null(session.Layout);

        Unchanged(before, Everything());
    }

    [Fact]
    public void An_absolute_path_is_taken_only_inside_the_folder()
    {
        var session = Session();

        Assert.Contains("outside the folder", Refused(session, "open_file", PathArgument(outside)));
        Assert.Null(session.Layout);

        Assert.StartsWith("Opened Mosfet.gds", Done(session, "open_file", PathArgument(In("Mosfet.gds"))));
    }

    [Fact]
    public void Nothing_is_written_out_of_the_folder()
    {
        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);

        var before = Everything();

        Assert.Contains("outside the folder", Refused(session, "save_file", PathArgument("../outside.gds", ", \"replace\": true")));
        Assert.Contains("outside the folder", Refused(session, "save_file", PathArgument("../new.gds")));
        Assert.Contains("outside the folder", Refused(session, "save_file", PathArgument(Path.Combine(scratch, "new.gds"))));
        Assert.Contains("outside the folder", Refused(session, "new_file", PathArgument("../new.gds")));

        Unchanged(before, Everything());
    }

    ///
    ///A link inside the folder is followed only as far as the folder: one that leads out is refused, one that
    ///stays in is a path like any other, and a listing goes through neither.
    ///
    ///A junction on Windows, which needs no privilege to make, and a symbolic link elsewhere.
    ///
    [Fact]
    public void A_link_that_leads_out_of_the_folder_is_refused_and_one_that_stays_in_is_followed()
    {
        Directory.CreateDirectory(In("kept"));
        File.WriteAllBytes(In(Path.Combine("kept", "inv.gds")), Mosfet);

        Link(In("escape"), scratch);
        Link(In("shortcut"), In("kept"));

        var session = Session();

        Assert.Contains("leads out of the folder", Refused(session, "open_file", PathArgument("escape/outside.gds")));
        Assert.Null(session.Layout);

        Assert.StartsWith("Opened shortcut/inv.gds", Done(session, "open_file", PathArgument("shortcut/inv.gds")));

        Assert.Contains("leads out of the folder", Refused(session, "save_file", PathArgument("escape/new.gds")));
        Assert.False(File.Exists(Path.Combine(scratch, "new.gds")));

        string listed = Done(session, "list_files");

        Assert.Contains("kept/inv.gds", listed);
        Assert.DoesNotContain("shortcut/", listed);
        Assert.DoesNotContain("outside.gds", listed);
    }

    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);

            return;
        }

        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);

        using var making = Process.Start(start)!;

        making.WaitForExit();

        Assert.True(making.ExitCode == 0, making.StandardError.ReadToEnd());
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
    }

    ///
    ///Names Windows reads as something other than a file - a device, a stream inside a file - are refused
    ///there, for reading and for writing. Everywhere else they are ordinary names that are not there.
    ///
    [Theory]
    [InlineData("CON")]
    [InlineData("cells/NUL.gds")]
    [InlineData("Mosfet.gds:hidden.gds")]
    public void A_name_windows_reads_as_something_else_is_refused_there(string path)
    {
        var session = Session();
        var before = Everything();

        if (!OperatingSystem.IsWindows())
        {
            Assert.Contains("There is no file at", Refused(session, "open_file", PathArgument(path)));

            return;
        }

        Refused(session, "open_file", PathArgument(path));

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);

        Refused(session, "save_file", PathArgument(path, ", \"replace\": true"));

        Unchanged(before, Everything());
    }

    #endregion **************************************************************************



    #region Opening *********************************************************************

    [Fact]
    public void Opening_says_what_the_layout_holds_and_settles_the_cell_to_work_in()
    {
        var session = Session();

        string said = Done(session, "open_file", PathArgument("Mosfet.gds"));

        Assert.Equal("Opened Mosfet.gds, GDSII: 1 cell, with mosfet on top, which is the one being worked in; 9 layers. "
            + "A database unit is 0.001 µm. Shape numbers start again with this file.", said);

        Assert.Equal("mosfet", session.Layout!.Cell);
        Assert.Equal(In("Mosfet.gds"), session.FilePath);
        Assert.False(session.Unsaved);
    }

    [Fact]
    public void A_file_that_is_not_a_layout_is_refused_as_one()
    {
        File.WriteAllText(In("notes.drc"), "rule met1.1 width met1 140\n");

        var session = Session();

        Assert.Contains("notes.drc is not a layout this reads", Refused(session, "open_file", PathArgument("notes.drc")));
        Assert.Contains("There is no file at missing.gds", Refused(session, "open_file", PathArgument("missing.gds")));

        Directory.CreateDirectory(In("cells"));

        Assert.Contains("cells is a folder", Refused(session, "open_file", PathArgument("cells")));
        Assert.Null(session.Layout);
    }

    [Fact]
    public void Unsaved_changes_are_not_dropped_unless_that_is_asked_for()
    {
        File.WriteAllBytes(In("other.gds"), Mosfet);

        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));

        //Nothing changed yet, so another file may simply replace it.
        Done(session, "open_file", PathArgument("other.gds"));
        Done(session, "draw_rectangle", Rectangle);

        Assert.True(session.Unsaved);

        string said = Refused(session, "open_file", PathArgument("Mosfet.gds"));

        Assert.Contains("other.gds has changes that are not saved", said);
        Assert.Contains("discard_changes", said);

        Assert.Contains("has changes that are not saved", Refused(session, "new_file", PathArgument("new.gds")));
        Assert.Equal(In("other.gds"), session.FilePath);

        Done(session, "open_file", PathArgument("Mosfet.gds", ", \"discard_changes\": true"));

        Assert.Equal(In("Mosfet.gds"), session.FilePath);
        Assert.False(session.Unsaved);
        Assert.Equal(Mosfet, File.ReadAllBytes(In("other.gds")));
    }

    [Fact]
    public void An_undone_change_still_counts_as_unsaved()
    {
        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);
        Done(session, "undo_last_change");

        Assert.True(session.Unsaved);
    }

    #endregion **************************************************************************



    #region Saving **********************************************************************

    [Fact]
    public void A_change_reaches_the_disk_only_when_saved()
    {
        var session = Session();
        int before = OnMet1(In("Mosfet.gds"));

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);

        Assert.Equal(Mosfet, File.ReadAllBytes(In("Mosfet.gds")));

        string said = Done(session, "save_file");

        Assert.StartsWith("Saved Mosfet.gds: GDSII, 1 cell, ", said);
        Assert.Equal(before + 1, OnMet1(In("Mosfet.gds")));
        Assert.False(session.Unsaved);
    }

    [Theory]
    [InlineData("copy.gds", "GDSII")]
    [InlineData("copy.oas", "OASIS")]
    [InlineData("copy.dxf", "DXF")]
    public void A_save_writes_the_format_its_name_says(string name, string format)
    {
        var session = Session();
        int before = OnMet1(In("Mosfet.gds"));

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);

        Assert.StartsWith($"Saved {name}: {format}, ", Done(session, "save_file", PathArgument(name)));

        using (var stream = File.OpenRead(In(name)))
        {
            Cli.ReadLayout(stream, out string read);

            Assert.Equal(format, read);
        }

        Assert.Equal(before + 1, OnMet1(In(name)));

        //The original is untouched, and the copy is the file from now on.
        Assert.Equal(Mosfet, File.ReadAllBytes(In("Mosfet.gds")));
        Assert.Equal(In(name), session.FilePath);
    }

    [Fact]
    public void A_save_under_a_new_name_makes_that_the_file()
    {
        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);

        Assert.EndsWith("save_file with no path writes here from now on.", Done(session, "save_file", PathArgument("out/edited.gds")));

        Done(session, "draw_rectangle", """{"layer": "68/20", "from": [200, 200], "to": [201, 201]}""");
        Done(session, "save_file");

        Assert.Equal(OnMet1(outside) + 2, OnMet1(In("out/edited.gds")));
        Assert.Equal(Mosfet, File.ReadAllBytes(In("Mosfet.gds")));
    }

    [Fact]
    public void A_save_will_not_write_over_another_file_unless_told_to()
    {
        File.WriteAllBytes(In("other.gds"), Mosfet);

        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));
        Done(session, "draw_rectangle", Rectangle);

        string said = Refused(session, "save_file", PathArgument("other.gds"));

        Assert.Contains("There is already a file at other.gds", said);
        Assert.Contains("replace: true", said);
        Assert.Equal(Mosfet, File.ReadAllBytes(In("other.gds")));

        Done(session, "save_file", PathArgument("other.gds", ", \"replace\": true"));

        Assert.Equal(OnMet1(outside) + 1, OnMet1(In("other.gds")));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("run.bat")]
    [InlineData("picture.svg")]
    [InlineData("noextension")]
    public void A_save_refuses_a_name_no_layout_format_has(string name)
    {
        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));

        Assert.Contains("save_file writes GDSII, OASIS or DXF", Refused(session, "save_file", PathArgument(name)));
        Assert.False(File.Exists(In(name)));
    }

    [Fact]
    public void A_layout_read_from_svg_is_saved_only_under_a_layout_name()
    {
        File.WriteAllText(In("drawing.svg"), """<svg xmlns="http://www.w3.org/2000/svg" width="20" height="10"><rect x="0" y="0" width="20" height="10" /></svg>""");

        var session = Session();

        Assert.StartsWith("Opened drawing.svg, SVG: ", Done(session, "open_file", PathArgument("drawing.svg")));

        string said = Refused(session, "save_file");

        Assert.Contains("drawing.svg was read from SVG, which save_file does not write", said);

        Done(session, "save_file", PathArgument("drawing.gds"));

        using var stream = File.OpenRead(In("drawing.gds"));

        Cli.ReadLayout(stream, out string format);

        Assert.Equal("GDSII", format);
    }

    ///
    ///A save that fails leaves the file it was writing over as it was, and nothing beside it.
    ///
    ///The failure is made the way each system allows: on Windows by holding the file open, which stops it
    ///being replaced; elsewhere by taking away the right to write in its folder - which root keeps, so the
    ///check has nothing to fail with there and says so.
    ///
    [Fact]
    public void A_save_that_fails_leaves_the_file_as_it_was_and_nothing_beside_it()
    {
        Directory.CreateDirectory(In("locked"));
        File.WriteAllBytes(In("locked/chip.gds"), Mosfet);

        var session = Session();

        Done(session, "open_file", PathArgument("locked/chip.gds"));
        Done(session, "draw_rectangle", Rectangle);

        AgentResult result;

        if (OperatingSystem.IsWindows())
        {
            using (new FileStream(In("locked/chip.gds"), FileMode.Open, FileAccess.Read, FileShare.None))
                result = Call(session, "save_file");
        }
        else
        {
            var mode = File.GetUnixFileMode(In("locked"));

            File.SetUnixFileMode(In("locked"), UnixFileMode.UserRead | UnixFileMode.UserExecute);

            try
            {
                result = Call(session, "save_file");
            }
            finally
            {
                File.SetUnixFileMode(In("locked"), mode);
            }

            if (Environment.UserName == "root")
                return;
        }

        Assert.True(result.IsError, result.Text);
        Assert.Contains("save_file could not read or write the file", result.Text);

        Assert.Equal(Mosfet, File.ReadAllBytes(In("locked/chip.gds")));
        Assert.Equal(new[] { "chip.gds" }, Directory.GetFiles(In("locked")).Select(Path.GetFileName));
        Assert.True(session.Unsaved);
    }

    #endregion **************************************************************************



    #region Starting ********************************************************************

    [Fact]
    public void A_new_file_is_on_disk_only_once_saved()
    {
        var session = Session();

        string said = Done(session, "new_file", """{"path": "chip.gds", "cell": "CHIP", "grid": 0.0005}""");

        Assert.Equal("Started chip.gds: an empty layout with one cell, CHIP, which is the one being worked in. "
            + "A database unit is 0.0005 µm. Nothing is written until save_file.", said);
        Assert.False(File.Exists(In("chip.gds")));

        Done(session, "draw_rectangle", """{"layer": "68/20", "from": [0, 0], "to": [1, 1]}""");
        Done(session, "save_file");

        using var stream = File.OpenRead(In("chip.gds"));

        var gds = GDS.FromStream(stream);

        Assert.Equal("CHIP", Hierarchy.NameOf(gds.StreamFormat.Structures.Single()));
        Assert.Equal(5e-10, ((Real8Data)gds.StreamFormat.UNITS!.Data!).Values[1], 1e-18);
        Assert.Equal(1, GdsFlattener.Flatten(gds).Elements.Count(element => element.Layer.Key == Met1));
    }

    [Fact]
    public void A_new_file_is_refused_where_a_file_is_or_under_a_name_save_file_cannot_write()
    {
        var session = Session();

        Assert.Contains("There is already a file at Mosfet.gds", Refused(session, "new_file", PathArgument("Mosfet.gds")));
        Assert.Contains("needs a .gds, .oas or .dxf name", Refused(session, "new_file", PathArgument("chip.svg")));
        Assert.Contains("\"grid\" is how big one database unit is", Refused(session, "new_file", """{"path": "chip.gds", "grid": 0}"""));
        Assert.Null(session.Layout);
    }

    #endregion **************************************************************************



    #region Layermaps and decks *********************************************************

    [Fact]
    public void A_layermap_and_a_deck_loaded_from_the_folder_stay_for_every_file_opened_after()
    {
        File.Copy(Path.Combine(GdsTestData.SampleDirectory, "sky130-roles.csv"), In("sky130.csv"));
        File.Copy(Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc"), In("sky130A.drc"));
        File.WriteAllBytes(In("other.gds"), Mosfet);

        var session = Session();

        Assert.Contains("No layout is open", Refused(session, "check_rules"));

        Done(session, "open_file", PathArgument("Mosfet.gds"));

        Assert.Equal(
            "No rule deck is loaded, so there is nothing to check against. Load one from the folder with load_rules.",
            Refused(session, "check_rules"));

        Assert.Contains("It names 9 of the open layout's 9 layers", Done(session, "load_layermap", PathArgument("sky130.csv")));
        Assert.StartsWith("Loaded sky130A.drc: ", Done(session, "load_rules", PathArgument("sky130A.drc")));

        Done(session, "open_file", PathArgument("other.gds"));

        Assert.Contains("met1 (68/20), conductor", Done(session, "list_layers"));
        Assert.Contains("Rules: nothing broken around this change.", Done(session, "draw_rectangle", """{"layer": "met1", "from": [100, 100], "to": [102, 101]}"""));
        Done(session, "check_rules");
    }

    ///
    ///**A KLayout .lyp is a layermap too**, as load_layermap tells the model it is: the listing offers it beside the
    ///CSVs, and its names land on the open layout's layers. It used to say so with only a CSV reader behind it, so a
    ///.lyp was read as rows and matched nothing.
    ///
    [Fact]
    public void A_klayout_lyp_is_listed_and_loaded_as_a_layermap()
    {
        File.WriteAllText(In("sky130.lyp"), """
            <?xml version="1.0" encoding="utf-8"?>
            <layer-properties>
             <properties>
              <fill-color>#9999ff</fill-color>
              <name>met1drawing_m 68/20</name>
              <source>68/20@1</source>
             </properties>
            </layer-properties>
            """);

        var session = Session();

        Assert.Contains("Layermaps, for load_layermap:\n  sky130.lyp", Done(session, "list_files"));

        Done(session, "open_file", PathArgument("Mosfet.gds"));

        Assert.Contains("It names 1 of the open layout's 9 layers", Done(session, "load_layermap", PathArgument("sky130.lyp")));
        Assert.Contains("met1drawing_m (68/20)", Done(session, "list_layers"));
    }

    [Fact]
    public void A_layermap_or_a_deck_that_says_nothing_is_refused()
    {
        File.WriteAllText(In("empty.csv"), "#nothing here\n");
        File.WriteAllText(In("empty.drc"), "#nothing here either\n");

        var session = Session();

        Assert.Contains("empty.csv says nothing about any layer", Refused(session, "load_layermap", PathArgument("empty.csv")));
        Assert.Contains("empty.drc holds no rules", Refused(session, "load_rules", PathArgument("empty.drc")));
    }

    #endregion **************************************************************************



    #region Listing *********************************************************************

    [Fact]
    public void The_listing_finds_layouts_layermaps_and_decks_and_leaves_hidden_and_build_folders_alone()
    {
        foreach (string hidden in new[] { ".git", "bin", "obj", "node_modules" })
        {
            Directory.CreateDirectory(In(hidden));
            File.WriteAllBytes(In(Path.Combine(hidden, "skipped.gds")), Mosfet);
        }

        Directory.CreateDirectory(In("cells/deeper"));
        File.WriteAllBytes(In("cells/deeper/inv.oas"), Mosfet);
        File.WriteAllText(In("sky130.csv"), "68,20,met1\n");
        File.WriteAllText(In("rules.drc"), "");
        File.WriteAllText(In("readme.txt"), "");

        var session = Session();

        string said = Done(session, "list_files");

        Assert.Contains("Layouts:\n  Mosfet.gds, 1 KB\n  cells/deeper/inv.oas, 1 KB", said);
        Assert.Contains("Layermaps, for load_layermap:\n  sky130.csv, 11 bytes", said);
        Assert.Contains("Rule decks, for load_rules:\n  rules.drc, 0 bytes", said);
        Assert.DoesNotContain("skipped", said);
        Assert.DoesNotContain("readme", said);

        Assert.Equal("In cells:\nLayouts:\n  cells/deeper/inv.oas, 1 KB", Done(session, "list_files", """{"folder": "cells"}"""));
        Assert.Contains("There is no folder at gone", Refused(session, "list_files", """{"folder": "gone"}"""));
    }

    [Fact]
    public void The_listing_says_which_layout_is_open_and_whether_it_is_saved()
    {
        var session = Session();

        Done(session, "open_file", PathArgument("Mosfet.gds"));

        Assert.Contains("Mosfet.gds, 1 KB - open", Done(session, "list_files"));

        Done(session, "draw_rectangle", Rectangle);

        Assert.Contains("Mosfet.gds, 1 KB - open, with changes not saved", Done(session, "list_files"));

        Done(session, "new_file", PathArgument("chip.gds", ", \"discard_changes\": true"));

        Assert.Contains("Open: chip.gds, which is not on disk yet.", Done(session, "list_files"));
    }

    #endregion **************************************************************************



    #region The command line ************************************************************

    [Fact]
    public void The_command_takes_one_folder_that_is_there()
    {
        var error = new StringWriter();

        Assert.Equal(Cli.UsageError, Cli.Run(new[] { "mcp", folder, scratch }, new StringWriter(), error));
        Assert.Contains("mcp works in one folder, but was given 2", error.ToString());

        error = new StringWriter();

        Assert.Equal(Cli.FileError, Cli.Run(new[] { "mcp", Path.Combine(scratch, "gone") }, new StringWriter(), error));
        Assert.Contains("There is no folder at", error.ToString());
    }

    [Fact]
    public void The_command_loads_its_layermap_and_deck_for_every_file()
    {
        string layermap = Path.Combine(GdsTestData.SampleDirectory, "sky130-roles.csv");
        string deck = Path.Combine(GdsTestData.SampleDirectory, "sky130A.drc");

        Assert.Equal(Cli.Ok, Cli.McpSessionFrom(new[] { folder, "--layermap", layermap, "--deck", deck }, new StringWriter(), out McpSession? session));

        Assert.Contains("The layermap sky130-roles.csv names the layers of every file opened.", session!.Instructions);
        Assert.Contains("Every change is checked against the rules in sky130A.drc.", session.Instructions);
        Assert.Contains("Changes are checked against sky130A.drc.", Done(session, "open_file", PathArgument("Mosfet.gds")));
        Assert.Contains("met1 (68/20)", Done(session, "list_layers"));
    }

    [Fact]
    public void The_command_refuses_a_deck_with_no_rules_before_serving_anything()
    {
        File.WriteAllText(In("empty.drc"), "");

        var error = new StringWriter();

        Assert.Equal(Cli.FileError, Cli.McpSessionFrom(new[] { folder, "--deck", In("empty.drc") }, error, out McpSession? session));
        Assert.Contains("holds no rules", error.ToString());
        Assert.Null(session);
    }

    #endregion **************************************************************************



    #region The protocol ****************************************************************

    ///<summary>A server on one end of two pipes and the SDK's client on the other, in this process.</summary>
    private static async Task<(McpClient Client, Func<Task> Stop)> Connect(McpSession session)
    {
        var toServer = new Pipe();
        var toClient = new Pipe();
        var stopping = new CancellationTokenSource();

        var serving = McpServing.RunAsync(
            session,
            new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream(), "gds"),
            stopping.Token);

        var client = await McpClient.CreateAsync(new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()));

        async Task stop()
        {
            await client.DisposeAsync();
            stopping.Cancel();

            try
            {
                await serving;
            }
            catch (OperationCanceledException) { }
        }

        return (client, stop);
    }

    [Fact]
    public async Task Over_the_protocol_the_tools_arrive_with_their_schemas_annotations_and_instructions()
    {
        var session = Session();
        var (client, stop) = await Connect(session);

        try
        {
            Assert.Equal("gds", client.ServerInfo.Name);
            Assert.Equal(Cli.Version, client.ServerInfo.Version);
            Assert.Contains(folder, client.ServerInstructions);
            Assert.Contains(AgentTools.Conventions, client.ServerInstructions);

            var tools = await client.ListToolsAsync();

            Assert.Equal(session.Tools.Select(tool => tool.Name), tools.Select(tool => tool.Name));

            var find = tools.Single(tool => tool.Name == "find_shapes").ProtocolTool;
            var draw = tools.Single(tool => tool.Name == "draw_rectangle").ProtocolTool;
            var save = tools.Single(tool => tool.Name == "save_file").ProtocolTool;

            Assert.True(find.Annotations!.ReadOnlyHint);
            Assert.False(draw.Annotations!.ReadOnlyHint);
            Assert.False(draw.Annotations.DestructiveHint);
            Assert.True(save.Annotations!.DestructiveHint);
            Assert.False(save.Annotations.OpenWorldHint);

            Assert.Equal(
                AgentTools.Named("draw_rectangle")!.InputSchema().ToJsonString(),
                JsonNode.Parse(draw.InputSchema.GetRawText())!.ToJsonString());
        }
        finally
        {
            await stop();
        }
    }

    ///
    ///Over the protocol, a linked tab's picture is a picture: an image block after the text, for the model to be shown
    ///rather than told about - and the tab hears which client it is linked to.
    ///
    [Fact]
    public async Task Over_the_protocol_look_answers_with_the_linked_tabs_picture()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7 };

        var session = new McpSession(new ServedFolder(folder), linkPort: LiveLinkTests.FreePort());

        await using var tab = new GDSViewer.Ai.LiveLink((name, arguments, cancel) =>
            Task.FromResult(new GDSViewer.Ai.LlmToolOutput { Text = $"{name} answered", Png = Convert.ToBase64String(png) }));

        var (client, stop) = await Connect(session);

        try
        {
            Assert.True(session.Link!.TryListen(out _));

            tab.Start(session.Link.Port);

            for (int tries = 0; session.Link.Waiting == 0 && tries < 500; tries++)
                await Task.Delay(20);

            var linked = await client.CallToolAsync("link_tab", new Dictionary<string, object?> { ["code"] = tab.Code });

            Assert.NotEqual(true, linked.IsError);
            Assert.False(string.IsNullOrEmpty(tab.Client));

            var looked = await client.CallToolAsync("look", new Dictionary<string, object?>());

            Assert.Equal("look answered", ((TextContentBlock)looked.Content[0]).Text);

            var picture = Assert.IsType<ImageContentBlock>(looked.Content[1]);

            Assert.Equal("image/png", picture.MimeType);
            Assert.Equal(png, picture.DecodedData.ToArray());
        }
        finally
        {
            await stop();
            await session.Link!.DisposeAsync();
        }
    }

    [Fact]
    public async Task Over_the_protocol_a_refusal_is_an_error_result_rather_than_a_protocol_error()
    {
        var session = Session();
        var (client, stop) = await Connect(session);

        try
        {
            var refused = await client.CallToolAsync("open_file", new Dictionary<string, object?> { ["path"] = "../outside.gds" });

            Assert.True(refused.IsError);
            Assert.Contains("outside the folder", ((TextContentBlock)refused.Content.Single()).Text);

            var opened = await client.CallToolAsync("open_file", new Dictionary<string, object?> { ["path"] = "Mosfet.gds" });

            Assert.NotEqual(true, opened.IsError);
            Assert.StartsWith("Opened Mosfet.gds", ((TextContentBlock)opened.Content.Single()).Text);

            var drawn = await client.CallToolAsync("draw_rectangle", new Dictionary<string, object?>
            {
                ["layer"] = "68/20",
                ["from"] = new[] { 100.0, 100.0 },
                ["to"] = new[] { 102.0, 101.0 }
            });

            Assert.StartsWith("Drew a 2 by 1 µm rectangle", ((TextContentBlock)drawn.Content.Single()).Text);
            Assert.True(session.Unsaved);
        }
        finally
        {
            await stop();
        }
    }

    ///
    ///The real thing: `gds mcp` started as its own process, the way a client starts it, and spoken to in
    ///newline-delimited JSON-RPC.
    ///
    ///**Every line it writes to standard output has to be a protocol message.** A stray line of prose there is
    ///a message the client cannot parse, and nothing in process can see one - this is the test that would.
    ///
    [Fact]
    public async Task As_a_process_it_writes_nothing_but_protocol_to_standard_output()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = new System.Text.UTF8Encoding(false)
        };

        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "gds.dll"));
        start.ArgumentList.Add("mcp");
        start.ArgumentList.Add(folder);

        using var server = Process.Start(start)!;

        var errorLines = new List<string>();

        server.ErrorDataReceived += (sender, line) =>
        {
            if (line.Data is not null)
            {
                lock (errorLines)
                    errorLines.Add(line.Data);
            }
        };

        server.BeginErrorReadLine();

        try
        {
            await speak(server, errorLines);
        }
        finally
        {
            if (!server.HasExited)
                server.Kill();
        }
    }

    private async Task speak(Process server, List<string> errorLines)
    {
        async Task<JsonElement> ask(int id, string method, string parameters)
        {
            await server.StandardInput.WriteLineAsync($"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{parameters}}}");
            await server.StandardInput.FlushAsync();

            string? line = await server.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));

            Assert.NotNull(line);

            using var message = JsonDocument.Parse(line!);

            Assert.Equal("2.0", message.RootElement.GetProperty("jsonrpc").GetString());
            Assert.Equal(id, message.RootElement.GetProperty("id").GetInt32());

            return message.RootElement.GetProperty("result").Clone();
        }

        var initialized = await ask(1, "initialize", """{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"0"}}""");

        Assert.Equal("gds", initialized.GetProperty("serverInfo").GetProperty("name").GetString());

        await server.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");

        var listed = await ask(2, "tools/list", "{}");

        //The six file tools, the four of the live link and the library's thirty-one.
        Assert.Equal(41, listed.GetProperty("tools").GetArrayLength());

        var opened = await ask(3, "tools/call", """{"name":"open_file","arguments":{"path":"Mosfet.gds"}}""");

        Assert.StartsWith("Opened Mosfet.gds", opened.GetProperty("content")[0].GetProperty("text").GetString());

        //Closing its input is how a client says it is done; it ends, and says nothing more.
        server.StandardInput.Close();

        string rest = await server.StandardOutput.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(30));

        await server.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("", rest);
        Assert.Equal(0, server.ExitCode);

        lock (errorLines)
            Assert.Contains(errorLines, line => line.StartsWith("gds mcp ") && line.Contains(folder));
    }

    #endregion **************************************************************************
}
