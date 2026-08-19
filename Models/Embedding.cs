using System.Globalization;

namespace GDSViewer.Models
{
    ///<summary>How much of the app an embedder is offering.</summary>
    public enum AppMode
    {
        ///<summary>The whole app, which is what it is without being asked.</summary>
        Edit,

        ///<summary>The whole app, with everything that would change the file turned off.</summary>
        NoEdit,

        ///<summary>The canvas and nothing else.</summary>
        Viewer
    }

    ///
    ///The settings an embedder can put in the address, for a viewer dropped into somebody else's page.
    ///
    ///**Every value is nullable, and that is the whole design.** The question each one answers is not "what
    ///is the grid set to" but "did the address say anything about the grid" - because a parameter that was
    ///named beats the saved session and one that was left out does not. A bool would collapse those two into
    ///one, and the session would be overwritten with a default nobody asked for on every load.
    ///
    ///Nothing here is a preference. It is what the page hosting the viewer wants it to start as; what the
    ///visitor does next is theirs, and goes into the session as it always did.
    ///
    public sealed class Embedding
    {
        ///<summary>Whether the page gives its margins to the view - the toolbar's own full-screen button.</summary>
        public bool? FullScreen { get; init; }

        ///<summary>Whether the title bar over the app is drawn at all.</summary>
        public bool? Banner { get; init; }

        ///<summary>Whether the 2D view draws a grid, and whether the pointer lands on it.</summary>
        public bool? ShowGrid { get; init; }

        ///
        ///Whether the cell tree is docked open down the side of the view.
        ///
        ///On by default, so an address only has to say anything to shut it - which a page embedding the
        ///viewer to show one layout usually wants, since a file explorer beside a single cell is a column of
        ///nothing. It is also how the specs ask for a known page: a test about drawing a rectangle should not
        ///be at the mercy of a panel it never mentions.
        ///
        public bool? CellTree { get; init; }

        ///<summary>Whether the layer list is showing. On by default, like the tree.</summary>
        public bool? Layers { get; init; }

        public bool? SnapToGrid { get; init; }

        ///<summary>How far apart the grid lines are, in <see cref="GridUnit"/>.</summary>
        public double? Pitch { get; init; }

        ///<summary>How the pitch is written - "nm", "um", "mm" or "db", or the long names the app uses.</summary>
        public string? GridUnit { get; init; }

        ///<summary>Which tool is in hand: pan, measure, select, move or draw.</summary>
        public string? Tool { get; init; }

        ///<summary>Which backdrop the 3D scene wears, by file name, or "none".</summary>
        public string? Background { get; init; }

        ///<summary>Where the 2D view looks - a viewBox, "x y width height", or the same four comma separated.</summary>
        public string? Framing { get; init; }

        ///<summary>Where the 3D camera stands and what it orbits - six numbers, position then target.</summary>
        public string? Camera { get; init; }

        public AppMode Mode { get; init; } = AppMode.Edit;

        ///<summary>Whether the address mentioned the mode at all, for telling a default from a choice.</summary>
        public bool ModeNamed { get; init; }

        ///
        ///The embedder's own files, in the order they were given: what to call each one, and where it is.
        ///
        ///Empty for an ordinary visit. These are added to the bundled examples rather than replacing them -
        ///a page that offers its own PDK has no reason to take the sky130 cells away, and one that wants
        ///only its own can say so by the list it publishes rather than by this.
        ///
        public IReadOnlyList<InjectedExample> Examples { get; init; } = new List<InjectedExample>();

        ///
        ///Where to fetch a layermap from, so a link arrives with the layers already named.
        ///
        ///**Not a layermap in the address.** A real one is hundreds of rows, and a query string is not where
        ///a PDK table goes - so this is a URL to one, the same shape as <see cref="InjectedExample"/>'s, and
        ///held to the same http/https rule for the same reason: this is the second of the two places the app
        ///will go and fetch something because an address said so.
        ///
        ///What it is *for*: what a layer is called and what it is for are the two things a GDSII file does
        ///not carry, so a page showing one layout has no way to say "and these numbers are metal" - and
        ///without that, Trace net is a button that will not work and nobody can see why. Every other setting
        ///here is a preference; this one is the difference between a feature working and not.
        ///
        ///Applied over the file rather than into the session, unlike everything else in this class. A
        ///layermap is not state the visitor arrived with - it is a fact about the process the page is
        ///showing - and putting it in the session would have the next file opened inherit it.
        ///
        public string? LayerMap { get; init; }

        ///<summary>Nothing named, nothing to apply - the ordinary case, and worth not walking.</summary>
        public bool IsEmpty
        {
            get
            {
                return FullScreen is null
                    && Banner is null
                    && ShowGrid is null
                    && SnapToGrid is null
                    && Pitch is null
                    && GridUnit is null
                    && Tool is null
                    && CellTree is null
                    && Layers is null
                    && Background is null
                    && Framing is null
                    && Camera is null
                    && !ModeNamed
                    && LayerMap is null
                    && Examples.Count == 0;
            }
        }

        ///
        ///Lays what the address named over a state, and leaves the rest of it alone.
        ///
        ///**Through the session rather than around it.** Every one of these settings already travels in a
        ///SavedSession - that is how they are restored, and how a change to one is written back - so the
        ///shortest correct way to apply an embed is to put its values into that and let the existing path do
        ///the work. Nothing downstream has to learn what an embed is.
        ///
        ///Which is also what makes the precedence fall out rather than being enforced: a named parameter is
        ///written in, an unnamed one is not touched, and what the visitor does next saves over the top in the
        ///ordinary way.
        ///
        public SavedSession Over(SavedSession session)
        {
            if (FullScreen is bool full)
                session.FullScreen = full;

            if (CellTree is bool tree)
                session.CellTree = tree;

            if (Layers is bool layers)
                session.Layers = layers;

            if (ShowGrid is bool grid)
                session.ShowGrid = grid;

            if (SnapToGrid is bool snap)
                session.SnapToGrid = snap;

            if (UnitNamed(GridUnit) is string unit)
                session.GridUnit = unit;

            //
            //The pitch is written in whatever unit was named beside it, and the session holds microns.
            //
            //Database units are refused rather than guessed: what one is worth is a property of the file,
            //and a pitch converted with the wrong file's scale is out by a thousand without saying so. The
            //unit still applies as a way of *writing* the pitch - it is only the conversion that cannot be
            //done here.
            //
            if (Pitch is double pitch && InMicrons(pitch, GridUnit) is double microns)
            {
                session.GridMicrons = microns;

                //Theirs rather than the file's, so opening another file does not quietly replace it.
                session.PitchChosen = true;
            }

            if (ToolNamed(Tool) is string tool)
                session.Tool = tool;

            if (Background is string backdrop)
                session.Background = backdrop;

            //
            //The framing, which needs nothing here beyond being written down.
            //
            //Both views already put themselves back from these two on a restore - that is how a session
            //returns you to where you were looking - so an address that names one only has to reach the
            //same field, and the whole restore path applies it. Which is the point of laying an embed over
            //a session rather than around it, and it is why this is two lines instead of a second
            //apply-the-camera route living beside the first.
            //
            if (Framing is string framing)
                session.View2DBox = framing;

            if (Camera is string camera)
                session.View3DCamera = camera;

            return session;
        }

        ///<summary>
        ///A pitch in microns, or null when the unit cannot be converted here.
        ///
        ///**The British spellings in here are deliberate and must not be "corrected".** Everything this
        ///repository writes is US English, and a sweep of the codebase for `metre` will land on the four
        ///case labels below - which are not prose. They are *accepted input*, from an embedder this app
        ///does not control and cannot recompile: a page that has been passing `unit=nanometre` since it was
        ///written goes on working because the label is here, and stops the day somebody tidies it away.
        ///
        ///What is emitted is US English regardless - see <see cref="UnitNamed"/>, which answers "Nanometer"
        ///whichever spelling it was asked with. Tolerant on the way in, consistent on the way out.
        ///</summary>
        public static double? InMicrons(double pitch, string? unit)
        {
            switch ((unit ?? "um").ToLowerInvariant())
            {
                case "nm":
                case "nanometer":
                case "nanometre":
                    return pitch / 1000;

                case "um":
                case "µm":
                case "micron":
                case "micrometer":
                    return pitch;

                case "mm":
                case "millimeter":
                case "millimetre":
                    return pitch * 1000;
            }

            //Including "db": see Over.
            return null;
        }

        ///<summary>The unit as the session writes it, or null for one this build does not know.</summary>
        public static string? UnitNamed(string? unit)
        {
            switch ((unit ?? "").ToLowerInvariant())
            {
                case "nm":
                case "nanometer":
                case "nanometre":
                    return "Nanometer";

                case "um":
                case "µm":
                case "micron":
                case "micrometer":
                    return "Micron";

                case "mm":
                case "millimeter":
                case "millimetre":
                    return "Millimeter";

                case "db":
                case "dbu":
                case "databaseunit":
                    return "DatabaseUnit";
            }

            return null;
        }

        ///<summary>The tool as the session writes it, or null for a name this build does not know.</summary>
        public static string? ToolNamed(string? tool)
        {
            switch ((tool ?? "").ToLowerInvariant())
            {
                case "pan":
                    return "Pan";

                case "measure":
                case "ruler":
                    return "Measure";

                case "select":
                    return "Select";

                case "move":
                    return "Move";

                case "draw":
                    return "Draw";
            }

            return null;
        }

        ///
        ///Reads the settings straight out of an address.
        ///
        ///The query is split here rather than through Blazor's own [SupplyParameterFromQuery], because that
        ///wants one property per parameter and gives no way to ask whether a name was written at all - which
        ///is the only question this type is interested in. Splitting it once means the whole of it can be
        ///tested without a browser.
        ///
        public static Embedding ReadFrom(string address)
        {
            return Read(SplitQuery(address));
        }

        ///
        ///A query string as a name to its values, keeping repeats.
        ///
        ///Repeats matter: example= is written once per file, and a dictionary of single values would keep
        ///the last and quietly lose the rest of somebody's library.
        ///
        ///Case-insensitive on the name, because an embed is typed by hand into somebody else's page.
        ///
        public static Dictionary<string, string[]> SplitQuery(string address)
        {
            var found = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            int mark = address.IndexOf('?');

            if (mark < 0 || mark == address.Length - 1)
                return found;

            string query = address.Substring(mark + 1);

            //A fragment is not part of the query, and an address that has one would otherwise put it in the
            //last value.
            int hash = query.IndexOf('#');

            if (hash >= 0)
                query = query.Substring(0, hash);

            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = part.IndexOf('=');

                if (equals <= 0)
                    continue;

                string name = Uri.UnescapeDataString(part.Substring(0, equals));

                //Written as a query is: a space arrives as a plus, and unescaping alone leaves it a plus.
                string value = Uri.UnescapeDataString(part.Substring(equals + 1).Replace("+", "%20"));

                if (found.TryGetValue(name, out var already))
                    found[name] = already.Append(value).ToArray();
                else
                    found[name] = new[] { value };
            }

            return found;
        }

        ///
        ///Reads the settings out of a query string.
        ///
        ///**Nothing here throws and nothing here is rejected loudly.** The address is written by hand, by
        ///somebody pasting an embed into a page they are building, and a viewer that refuses to draw because
        ///one parameter was misspelled is a viewer they cannot debug. A value that cannot be read is left
        ///unset, which means the session decides it - the same as not having written it at all.
        ///
        public static Embedding Read(IReadOnlyDictionary<string, string[]> query)
        {
            string? one(string name)
            {
                if (!query.TryGetValue(name, out var values) || values.Length == 0)
                    return null;

                string first = values[0].Trim();

                if (first.Length == 0)
                    return null;

                return first;
            }

            AppMode mode = AppMode.Edit;
            string? named = one("mode");

            if (named is not null)
                mode = ModeOf(named);

            return new Embedding
            {
                FullScreen = Flag(one("full")),
                Banner = Flag(one("banner")),
                ShowGrid = Flag(one("grid")),
                CellTree = Flag(one("tree")),
                Layers = Flag(one("layers")),
                SnapToGrid = Flag(one("snap")),
                Pitch = Distance(one("pitch")),
                GridUnit = one("unit"),
                Tool = one("tool"),
                Background = one("background"),
                Framing = FramingNamed(one("box")),
                Camera = CameraNamed(one("camera")),
                Mode = mode,
                ModeNamed = named is not null,
                LayerMap = FetchableUrl(one("layermap")),
                Examples = InjectedExample.ReadAll(query)
            };
        }

        ///
        ///An address the app is willing to go and fetch, canonically escaped, or null.
        ///
        ///**One place decides this, because it is the whole of the app's exposure to what an address says.**
        ///Two parameters now name something to fetch - an injected example and a layermap - and a second copy
        ///of this check is a second chance for one of them to accept a scheme the other refuses.
        ///
        ///http and https only. A viewer that will fetch file:// or data: on somebody's say-so is a different
        ///kind of program, and neither is any use to a page embedding this one.
        ///
        ///AbsoluteUri rather than ToString: ToString is the form meant for showing somebody, and it unescapes
        ///what it can - so an address that arrived with %20 in it comes back with a space, and a space is not
        ///something that can be fetched.
        ///
        public static string? FetchableUrl(string? value)
        {
            if (value is null)
                return null;

            string url = value.Trim();

            if (url.Length == 0)
                return null;

            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address))
                return null;

            if (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps)
                return null;

            return address.AbsoluteUri;
        }

        ///
        ///A flag as a person would write one.
        ///
        ///"true" and "1" are the obvious pair; "yes" and "on" are what somebody writing an embed by hand
        ///reaches for, and refusing them would be pedantry in a string nobody validates.
        ///
        public static bool? Flag(string? value)
        {
            if (value is null)
                return null;

            switch (value.ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                case "on":
                    return true;

                case "false":
                case "0":
                case "no":
                case "off":
                    return false;
            }

            return null;
        }

        ///<summary>A distance, invariant, and refused when it is not one a grid could use.</summary>
        public static double? Distance(string? value)
        {
            if (value is null)
                return null;

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double read))
                return null;

            if (double.IsNaN(read) || double.IsInfinity(read) || read <= 0)
                return null;

            return read;
        }

        ///
        ///A viewBox as the session writes it, or null for anything that is not four usable numbers.
        ///
        ///**Refused rather than corrected**, the way a misspelled tool is: a box that will not parse costs
        ///that one setting and the view frames the drawing, which is what it does when nothing was said.
        ///
        ///A size of zero is refused here as well as downstream. A browser handed a viewBox of no width does
        ///not draw a very small picture, it stops drawing - and unlike a typo in the address this one would
        ///be written into the session and come back on every visit until something else overwrote it.
        ///
        public static string? FramingNamed(string? value)
        {
            double[]? numbers = NumbersIn(value, 4);

            if (numbers is null)
                return null;

            if (!(numbers[2] > 0) || !(numbers[3] > 0))
                return null;

            return joined(numbers);
        }

        ///<summary>Six numbers - where the camera is, then what it orbits - or null for anything else.</summary>
        public static string? CameraNamed(string? value)
        {
            double[]? numbers = NumbersIn(value, 6);

            if (numbers is null)
                return null;

            return joined(numbers);
        }

        ///
        ///Exactly `howMany` finite numbers, taken from a value written either way, or null.
        ///
        ///**Commas or spaces, because both are right in their own place.** The session holds these as SVG
        ///writes them, which is spaces; an address that carried spaces would show them as %20 to anybody who
        ///looked at it, and a link is a thing people look at. Accepting both costs one character in the
        ///split and means neither end has to be the one that is wrong.
        ///
        ///Invariant, since an address written where decimals are commas has to be readable where they are
        ///not - and a decimal comma in a comma-separated list has no reading at all, which is the other
        ///reason the numbers are checked one at a time rather than counted afterwards.
        ///
        private static double[]? NumbersIn(string? value, int howMany)
        {
            if (value is null)
                return null;

            string[] parts = value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != howMany)
                return null;

            var numbers = new double[howMany];

            for (int i = 0; i < howMany; i++)
            {
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;

                if (!double.IsFinite(numbers[i]))
                    return null;
            }

            return numbers;
        }

        ///<summary>Back into the one spelling the session and the views both read - spaces, invariant.</summary>
        private static string joined(double[] numbers)
        {
            var written = new string[numbers.Length];

            for (int i = 0; i < numbers.Length; i++)
                written[i] = numbers[i].ToString(CultureInfo.InvariantCulture);

            return string.Join(' ', written);
        }

        ///<summary>A mode by name. Anything unrecognized is the whole app, which is the safe way to be wrong.</summary>
        public static AppMode ModeOf(string value)
        {
            switch (value.ToLowerInvariant())
            {
                case "viewer":
                case "vieweronly":
                case "view":
                    return AppMode.Viewer;

                case "noedit":
                case "readonly":
                case "read-only":
                    return AppMode.NoEdit;
            }

            return AppMode.Edit;
        }
    }

    ///<summary>One of the embedder's own files: what to call it, and where to fetch it from.</summary>
    public sealed class InjectedExample
    {
        public InjectedExample(string name, string url)
        {
            Name = name;
            Url = url;
        }

        public string Name { get; }

        public string Url { get; }

        ///
        ///Reads every example= out of a query string, in the order they were written.
        ///
        ///One parameter per file rather than one parameter holding a list, because the value is a URL and a
        ///URL is full of the characters a list would have to be split on. Repeating a name is what a query
        ///string does natively, and it leaves each value to be encoded once and read once.
        ///
        ///The name and the address are split on the first bar. A bar in a file's name is the price of the
        ///simplest separator that is not in a URL's own alphabet - and it is the *first* one, so a bar in
        ///the address itself survives.
        ///
        ///Refused, quietly and one at a time: an entry with no bar, an empty half, or an address that is not
        ///an absolute http or https URL. The last of those matters most - it is the one thing here that
        ///decides what the app will go and fetch.
        ///
        public static IReadOnlyList<InjectedExample> ReadAll(IReadOnlyDictionary<string, string[]> query)
        {
            var found = new List<InjectedExample>();

            if (!query.TryGetValue("example", out var values))
                return found;

            foreach (string entry in values)
            {
                if (Of(entry) is InjectedExample one)
                    found.Add(one);
            }

            return found;
        }

        public static InjectedExample? Of(string entry)
        {
            int bar = entry.IndexOf('|');

            if (bar <= 0 || bar == entry.Length - 1)
                return null;

            string name = entry.Substring(0, bar).Trim();
            string url = entry.Substring(bar + 1).Trim();

            if (name.Length == 0 || url.Length == 0)
                return null;

            //The one check that decides what this app will go and fetch, shared with layermap= so the two
            //cannot come to disagree about it. See Embedding.FetchableUrl.
            if (Embedding.FetchableUrl(url) is not string fetchable)
                return null;

            return new InjectedExample(name, fetchable);
        }
    }
}
