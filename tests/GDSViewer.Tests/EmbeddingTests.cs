using GDSViewer.Models;

namespace GDSViewer.Tests
{
    ///
    ///Reading an embed's settings out of the address.
    ///
    ///This is the one piece of the embedding work that is pure - a query string in, a set of decisions out -
    ///and it is also the piece a mistake in is hardest to see: a parameter that silently fails to parse
    ///looks exactly like one that was never written, and the app goes on drawing.
    ///
    public class EmbeddingTests
    {
        private static Embedding Read(params string[] pairs)
        {
            var query = new Dictionary<string, string[]>();

            foreach (string pair in pairs)
            {
                int at = pair.IndexOf('=');
                string name = pair.Substring(0, at);
                string value = pair.Substring(at + 1);

                if (query.TryGetValue(name, out var already))
                    query[name] = already.Append(value).ToArray();
                else
                    query[name] = new[] { value };
            }

            return Embedding.Read(query);
        }

        ///An ordinary visit says nothing, and nothing is what should be applied over the session.
        [Fact]
        public void An_address_with_nothing_in_it_names_nothing()
        {
            var settings = Read();

            Assert.True(settings.IsEmpty);
            Assert.Null(settings.ShowGrid);
            Assert.Null(settings.FullScreen);
            Assert.Equal(AppMode.Edit, settings.Mode);
            Assert.False(settings.ModeNamed);
        }

        ///
        ///**Off is a value, not an absence.** This is the distinction the whole model exists for: grid=false
        ///has to beat a session that says the grid is on, and only a nullable can tell it from grid missing.
        ///
        [Fact]
        public void A_flag_set_to_false_is_named_rather_than_absent()
        {
            var settings = Read("grid=false");

            Assert.False(settings.ShowGrid);
            Assert.False(settings.IsEmpty);

            //And the ones beside it are still unsaid.
            Assert.Null(settings.SnapToGrid);
        }

        [Theory]
        [InlineData("true", true)]
        [InlineData("1", true)]
        [InlineData("yes", true)]
        [InlineData("on", true)]
        [InlineData("TRUE", true)]
        [InlineData("false", false)]
        [InlineData("0", false)]
        [InlineData("no", false)]
        [InlineData("off", false)]
        public void A_flag_is_read_the_way_somebody_would_write_one(string written, bool expected)
        {
            Assert.Equal(expected, Embedding.Flag(written));
        }

        ///Anything else is left unsaid rather than guessed at, so a typo costs that setting and nothing else.
        [Fact]
        public void A_flag_that_is_neither_leaves_the_setting_alone()
        {
            Assert.Null(Embedding.Flag("perhaps"));
            Assert.Null(Embedding.Flag(""));
            Assert.Null(Embedding.Flag(null));
        }

        ///
        ///The pitch is a distance, read invariantly.
        ///
        ///Invariant because an embed is written once and read everywhere: a page authored on a machine that
        ///writes decimals with commas is pasted into a browser that does not, and 0.05 has to survive that.
        ///
        [Fact]
        public void The_pitch_is_read_invariantly()
        {
            Assert.Equal(0.05, Read("pitch=0.05").Pitch);
            Assert.Equal(2, Read("pitch=2").Pitch);
        }

        ///A grid cannot be a negative distance or none at all, and a viewer asked for one should ignore it.
        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("fifty")]
        [InlineData("NaN")]
        public void A_pitch_that_is_not_a_distance_is_refused(string written)
        {
            Assert.Null(Read($"pitch={written}").Pitch);
        }

        [Theory]
        [InlineData("viewer", AppMode.Viewer)]
        [InlineData("vieweronly", AppMode.Viewer)]
        [InlineData("noedit", AppMode.NoEdit)]
        [InlineData("readonly", AppMode.NoEdit)]
        [InlineData("NoEdit", AppMode.NoEdit)]
        public void A_mode_is_read_by_name(string written, AppMode expected)
        {
            Assert.Equal(expected, Read($"mode={written}").Mode);
        }

        ///
        ///**An unknown mode is the whole app.**
        ///
        ///Which is the safe way to be wrong in one direction and not the other: a misspelled "noedit" that
        ///fell back to the viewer would take the toolbar away from somebody who only wanted it read-only,
        ///and there would be nothing on screen to say why.
        ///
        [Fact]
        public void A_mode_nobody_recognizes_is_the_whole_app()
        {
            var settings = Read("mode=kiosk");

            Assert.Equal(AppMode.Edit, settings.Mode);

            //Named, though - so it is a choice that was read and not honored, rather than silence.
            Assert.True(settings.ModeNamed);
        }

        ///
        ///A layermap the address names, so a link arrives with the layers already named.
        ///
        ///The one setting here that is not a preference: what a layer is called and what it is for are the
        ///two things a GDSII file does not carry, so without this a page showing one layout has no way to
        ///say "and these numbers are metal" - and Trace net is a button that cannot work.
        ///
        [Fact]
        public void A_layermap_is_an_address_to_fetch_one_from()
        {
            var settings = Read("layermap=https://example.com/sky130.csv");

            Assert.Equal("https://example.com/sky130.csv", settings.LayerMap);
            Assert.False(settings.IsEmpty);
        }

        ///<summary>And an address that names none says nothing about it, so the file keeps its own.</summary>
        [Fact]
        public void No_layermap_named_is_no_layermap()
        {
            Assert.Null(Read("tree=false").LayerMap);
            Assert.True(Read().IsEmpty);
        }

        ///
        ///**Held to the same http-or-https rule as an example, by the same code.**
        ///
        ///This is the second of the two parameters that decide what the app will go and fetch, and it arrives
        ///from an address anybody can write. Two copies of that check would be two chances for one of them to
        ///accept a scheme the other refuses - so both call Embedding.FetchableUrl, and this asks that the
        ///sharing actually happened rather than that somebody remembered to write the guard twice.
        ///
        [Theory]
        [InlineData("file:///C:/secrets/passwords.csv")]
        [InlineData("data:text/csv;base64,AAAA")]
        [InlineData("javascript:alert(1)")]
        [InlineData("ftp://example.com/layers.csv")]
        [InlineData("layers.csv")]
        [InlineData("//example.com/layers.csv")]
        public void A_layermap_that_is_not_the_web_is_refused(string url)
        {
            //Unescaped, because that is the state a value reaches Read in: the query has already been
            //unescaped by then, which is what the helper above models.
            Assert.Null(Read($"layermap={url}").LayerMap);
        }

        ///<summary>And it comes back canonically escaped, which is the form that can be fetched.</summary>
        [Fact]
        public void A_layermap_address_comes_back_escaped()
        {
            var settings = Read("layermap=https://example.com/my layers.csv");

            Assert.Equal("https://example.com/my%20layers.csv", settings.LayerMap);
        }

        [Fact]
        public void An_example_is_a_name_and_an_address()
        {
            var settings = Read("example=My Cell|https://example.com/cell.gds");

            var one = Assert.Single(settings.Examples);

            Assert.Equal("My Cell", one.Name);
            Assert.Equal("https://example.com/cell.gds", one.Url);
        }

        ///Several, in the order they were written, since that is the order they will be read in.
        [Fact]
        public void Several_examples_keep_their_order()
        {
            var settings = Read(
                "example=First|https://example.com/a.gds",
                "example=Second|https://example.com/b.oas",
                "example=Third|https://example.com/c.dxf");

            Assert.Equal(new[] { "First", "Second", "Third" }, settings.Examples.Select(one => one.Name));
        }

        ///
        ///Split on the *first* bar, so an address carrying one survives.
        ///
        ///A query string is not the only thing that has been through a URL encoder by the time it gets here,
        ///and a bar is legal in a query of its own.
        ///
        ///
        ///The address comes back canonical, which is what escapes the bar it was allowed to keep: a bar is
        ///not legal unescaped in a URL, and %7C is the same address written properly. What matters is that
        ///the whole of it survived rather than being cut at the second separator.
        ///
        [Fact]
        public void The_split_is_on_the_first_bar_only()
        {
            var one = InjectedExample.Of("Cell|https://example.com/get?id=1|2");

            Assert.NotNull(one);
            Assert.Equal("Cell", one!.Name);
            Assert.Equal("https://example.com/get?id=1%7C2", one.Url);

            //The part after the second bar is still there, which is the thing being asked.
            Assert.EndsWith("2", one.Url);
        }

        ///
        ///**Only http and https.**
        ///
        ///This is the one parameter that decides what the app goes and fetches, and it arrives from an
        ///address anybody can write. A viewer that will open file:// or a data: URL on somebody's say-so is
        ///a different kind of program, and neither is any use to a page embedding this one.
        ///
        [Theory]
        [InlineData("Cell|file:///C:/secrets/passwords.gds")]
        [InlineData("Cell|data:text/plain;base64,AAAA")]
        [InlineData("Cell|javascript:alert(1)")]
        [InlineData("Cell|ftp://example.com/cell.gds")]
        public void An_address_that_is_not_the_web_is_refused(string entry)
        {
            Assert.Null(InjectedExample.Of(entry));
        }

        [Theory]
        [InlineData("no bar at all")]
        [InlineData("|https://example.com/cell.gds")]
        [InlineData("Cell|")]
        [InlineData("Cell|not a url")]
        [InlineData("Cell|/relative/cell.gds")]
        public void An_entry_that_is_not_a_pair_is_refused(string entry)
        {
            Assert.Null(InjectedExample.Of(entry));
        }

        ///One bad entry costs that entry, not the list - an embed with a typo in it still offers the rest.
        [Fact]
        public void A_bad_entry_does_not_take_the_good_ones_with_it()
        {
            var settings = Read(
                "example=Good|https://example.com/a.gds",
                "example=broken",
                "example=Also good|https://example.com/b.gds");

            Assert.Equal(new[] { "Good", "Also good" }, settings.Examples.Select(one => one.Name));
        }

        ///
        ///The address is split here rather than by Blazor, so the whole path can be tested without a browser.
        ///
        [Fact]
        public void An_address_is_read_from_end_to_end()
        {
            var settings = Embedding.ReadFrom("https://a.site/embed/?grid=false&pitch=0.5&mode=viewer");

            Assert.False(settings.ShowGrid);
            Assert.Equal(0.5, settings.Pitch);
            Assert.Equal(AppMode.Viewer, settings.Mode);
        }

        [Fact]
        public void An_address_with_no_query_says_nothing()
        {
            Assert.True(Embedding.ReadFrom("https://a.site/embed/").IsEmpty);
            Assert.True(Embedding.ReadFrom("https://a.site/embed/?").IsEmpty);
        }

        ///
        ///Repeats are kept, which is the whole reason the query is split by hand.
        ///
        ///A dictionary of single values would keep the last example= and quietly lose the rest of somebody's
        ///library - the kind of wrong that looks like the feature working on a one-file test.
        ///
        [Fact]
        public void A_repeated_name_keeps_every_value()
        {
            var query = Embedding.SplitQuery("https://a.site/?example=A%7Chttps://x/a.gds&example=B%7Chttps://x/b.gds");

            Assert.Equal(2, query["example"].Length);
            Assert.Equal(2, Embedding.Read(query).Examples.Count);
        }

        ///A name is matched however it was typed, since an embed is written by hand into somebody's page.
        [Fact]
        public void A_name_is_read_whatever_its_case()
        {
            Assert.False(Embedding.ReadFrom("https://a.site/?GRID=false").ShowGrid);
        }

        ///
        ///Encoded as a query actually arrives: a space as a plus, and everything else percent-escaped.
        ///
        [Fact]
        public void A_value_is_unescaped_the_way_a_query_is_written()
        {
            var one = Assert.Single(Embedding.ReadFrom(
                "https://a.site/?example=My+Own+Cell%7Chttps%3A%2F%2Fexample.com%2Fa%20b.gds").Examples);

            Assert.Equal("My Own Cell", one.Name);
            Assert.Equal("https://example.com/a%20b.gds", one.Url);
        }

        ///A fragment is not part of the query, and would otherwise ride along on the last value.
        [Fact]
        public void A_fragment_is_not_read_as_a_value()
        {
            Assert.Equal("select", Embedding.ReadFrom("https://a.site/?tool=select#somewhere").Tool);
        }

        ///Everything at once, which is what a real embed looks like.
        [Fact]
        public void A_whole_embed_reads_back_as_it_was_written()
        {
            var settings = Read(
                "file=Mosfet",
                "view=2d",
                "full=true",
                "banner=false",
                "grid=true",
                "snap=false",
                "pitch=0.25",
                "unit=um",
                "tool=select",
                "background=background2.jpg",
                "mode=noedit",
                "example=Our PDK|https://example.com/pdk.gds");

            Assert.True(settings.FullScreen);
            Assert.False(settings.Banner);
            Assert.True(settings.ShowGrid);
            Assert.False(settings.SnapToGrid);
            Assert.Equal(0.25, settings.Pitch);
            Assert.Equal("um", settings.GridUnit);
            Assert.Equal("select", settings.Tool);
            Assert.Equal("background2.jpg", settings.Background);
            Assert.Equal(AppMode.NoEdit, settings.Mode);
            Assert.Single(settings.Examples);
            Assert.False(settings.IsEmpty);
        }

        ///
        ///The framing, which is the one pair of parameters the app also writes back.
        ///
        ///Both spellings read, because the two ends genuinely want different ones: a viewBox is spaces
        ///because that is what the SVG attribute is, and an address carrying spaces shows %20 to anybody
        ///who looks at it. Normalized to the session's spelling on the way in, so nothing downstream has
        ///to know which way it arrived.
        ///
        [Theory]
        [InlineData("500,600,4000,4000")]
        [InlineData("500 600 4000 4000")]
        [InlineData("  500,  600, 4000 ,4000 ")]
        public void A_framing_reads_however_it_was_written(string written)
        {
            Assert.Equal("500 600 4000 4000", Embedding.FramingNamed(written));
        }

        ///
        ///And a framing that is not one costs that setting rather than the page, the way a misspelled tool
        ///does. The zero-size cases are the ones worth having: they are four perfectly good numbers, and a
        ///browser handed a viewBox of no width stops drawing rather than draws something small.
        ///
        [Theory]
        [InlineData("1,2,3")]
        [InlineData("1,2,3,4,5")]
        [InlineData("a,b,c,d")]
        [InlineData("0,0,0,500")]
        [InlineData("0,0,500,0")]
        [InlineData("0,0,-500,500")]
        [InlineData("")]
        [InlineData(null)]
        public void A_framing_that_is_not_one_is_refused(string? written)
        {
            Assert.Null(Embedding.FramingNamed(written));
        }

        ///<summary>Six for a camera - where it is, then what it orbits - and nothing else.</summary>
        [Theory]
        [InlineData("0,0,5000,0,0,0", "0 0 5000 0 0 0")]
        [InlineData("1 2 3 4 5 6", "1 2 3 4 5 6")]
        [InlineData("-1.5,0,2e3,0,0,0", "-1.5 0 2000 0 0 0")]
        public void A_camera_reads_however_it_was_written(string written, string expected)
        {
            Assert.Equal(expected, Embedding.CameraNamed(written));
        }

        ///<summary>A camera is unlike a box in that no value is out of range - only the count can be wrong.</summary>
        [Theory]
        [InlineData("0,0,5000")]
        [InlineData("0,0,5000,0,0,0,0")]
        [InlineData("here,there,everywhere,0,0,0")]
        [InlineData("NaN,0,0,0,0,0")]
        [InlineData("Infinity,0,0,0,0,0")]
        [InlineData(null)]
        public void A_camera_that_is_not_one_is_refused(string? written)
        {
            Assert.Null(Embedding.CameraNamed(written));
        }

        ///
        ///A decimal comma has no reading inside a comma-separated list, which is why the numbers are parsed
        ///invariantly and one at a time rather than counted first.
        ///
        [Fact]
        public void A_framing_is_read_invariantly()
        {
            Assert.Equal("0.5 0.5 10.25 10.25", Embedding.FramingNamed("0.5,0.5,10.25,10.25"));
        }

        ///
        ///And both reach the session, which is the whole of how they are applied: the views already put
        ///themselves back from these two fields on a restore, so an address that names one only has to
        ///write there.
        ///
        [Fact]
        public void The_framing_lands_in_the_session_the_views_restore_from()
        {
            var settings = Read("box=500,600,4000,4000", "camera=0,0,5000,0,0,0");

            Assert.False(settings.IsEmpty);

            var session = settings.Over(new SavedSession());

            Assert.Equal("500 600 4000 4000", session.View2DBox);
            Assert.Equal("0 0 5000 0 0 0", session.View3DCamera);
        }

        ///<summary>And a refused one leaves the session's own framing alone rather than emptying it.</summary>
        [Fact]
        public void A_refused_framing_leaves_what_the_session_had()
        {
            var settings = Read("box=1,2,3", "camera=0,0,5000");

            var session = new SavedSession { View2DBox = "1 2 3 4", View3DCamera = "1 2 3 4 5 6" };

            settings.Over(session);

            Assert.Equal("1 2 3 4", session.View2DBox);
            Assert.Equal("1 2 3 4 5 6", session.View3DCamera);
        }
    }
}
