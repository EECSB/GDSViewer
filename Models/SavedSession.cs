using System.Text.Json;
using System.Text.Json.Serialization;

using GdsII;

namespace GDSViewer.Models
{
    ///<summary>
    ///Everything the app puts back when you return: which file was open, what had been edited into it, how
    ///the layers were named and which were showing, and where the controls were left.
    ///
    ///**The file's bytes are in here on purpose.** An uploaded file exists only in the tab it was dropped
    ///into, and a text-view edit exists only until the page goes away - so a session that remembered the
    ///name of a bundled example would still lose the one thing that cannot be got back. Storing the bytes
    ///is what makes closing the browser mid-edit safe. It is also why this goes to IndexedDB rather than
    ///localStorage; see <see cref="AppStorage"/>.
    ///
    ///Serialized as JSON with short property names, because it is compressed and then base64'd, and every
    ///byte of key text is paid for three times over.
    ///</summary>
    public class SavedSession
    {
        ///<summary>The version this was written by, so a shape change can be recognized rather than misread.</summary>
        [JsonPropertyName("v")]
        public int Version { get; set; } = CurrentVersion;

        ///<summary>The bundled example's name, or empty when the file came off the user's machine.</summary>
        [JsonPropertyName("e")]
        public string ExampleName { get; set; } = "";

        [JsonPropertyName("n")]
        public string FileName { get; set; } = "";

        [JsonPropertyName("t")]
        public string FileType { get; set; } = ".gds";

        ///<summary>
        ///The file itself, base64'd. Empty for a bundled example that was never edited, since that can be
        ///fetched again by name - which keeps the common session small.
        ///</summary>
        [JsonPropertyName("b")]
        public string FileBytes { get; set; } = "";

        ///<summary>Which view was on screen, by the slug the address uses.</summary>
        [JsonPropertyName("w")]
        public string View { get; set; } = "";

        ///<summary>The layer names and colors, as the same CSV a layermap file holds.</summary>
        [JsonPropertyName("l")]
        public string LayerNames { get; set; } = "";

        ///
        ///The design rule deck in force, as the text the file held.
        ///
        ///**Kept for the same reason the layer names are, and it was an oversight that it was not.** Both
        ///are PDK data a GDSII file cannot carry, both arrive from a file the user picks, and the controls
        ///for them sit next to each other in the sidebar on exactly that argument - so a reload that
        ///remembered one and forgot the other sent somebody back to the file picker for no reason they
        ///could see.
        ///
        ///The text rather than anything parsed. A deck is small, re-reading it is free, and storing a
        ///parsed form would mean a stored shape that has to survive every future change to the format.
        ///
        ///What is deliberately not kept is the *result*. A run belongs to the layout it was run against,
        ///and the file can be edited between one visit and the next - so markers restored beside a changed
        ///layout would be pointing at where a fault used to be. The deck comes back and the Check button
        ///with it; pressing it is one gesture and it is honest.
        ///
        [JsonPropertyName("dk")]
        public string Deck { get; set; } = "";

        ///
        ///Whether the visitor has said they do not want the bundled sky130 mapping laid over the examples.
        ///
        ///**A flag rather than an empty <see cref="LayerNames"/>, because those are two different states.**
        ///Empty means "nothing has named anything yet", which is where a first visit starts and is exactly
        ///when the bundled mapping *should* land. This means "Clear was pressed", and it has to outlive a
        ///reload or Clear would be a button whose effect the next page load undoes - which is what happened
        ///when this was a field in the component instead, and what the clear-drops-the-stored-names spec
        ///caught.
        ///
        ///Set only by Clear. Loading a mapping or typing a name fills LayerNames, which the default already
        ///stands aside for, so neither needs to touch this. Getting the mapping back afterwards is Import,
        ///with the file the app ships - which is the same gesture as choosing any other one.
        ///
        [JsonPropertyName("ln")]
        public bool NoBundledLayerNames { get; set; }

        ///<summary>Whether the bundled deck was cleared, so it stops being laid over the examples.</summary>
        public bool NoBundledDeck { get; set; }

        ///<summary>The pairs that were switched **off**, written "65/20". The off set is the short one.</summary>
        [JsonPropertyName("h")]
        public List<string> HiddenLayers { get; set; } = new List<string>();

        ///<summary>
        ///The pairs whose **labels** were switched off, written "65/20".
        ///
        ///The off set, like <see cref="HiddenLayers"/> and for the same reason: labels start on, so the
        ///list of exceptions is the short one. This replaced a single bool for the whole file when the
        ///switch moved into each layer's own settings.
        ///</summary>
        [JsonPropertyName("x")]
        public List<string> LabelsOffLayers { get; set; } = new List<string>();

        ///<summary>
        ///Colors set by hand or by a layermap, as "65/20=#00ff00". Only the ones that differ from the
        ///palette: the palette is derived from how many layers a file has, so recording it would be
        ///recording something already known.
        ///</summary>
        [JsonPropertyName("c")]
        public List<string> LayerColors { get; set; } = new List<string>();

        ///<summary>
        ///Heights and thicknesses given by hand or by a layermap, as "65/20=2000,500".
        ///
        ///Only the layers that have one, for the same reason as the colors: the rest are worked out from
        ///the file, and a session that recorded those would pin every layer to one file's spacing and then
        ///fight the slider that is meant to move them.
        ///</summary>
        [JsonPropertyName("k")]
        public List<string> LayerStack { get; set; } = new List<string>();

        ///<summary>
        ///The fill patterns somebody chose, as "65/20=Dots".
        ///
        ///Only the layers that have one, and for a simpler reason than the colors and the stack: there is no
        ///automatic pattern to record a deviation from. Every layer is solid until it is told otherwise, so
        ///a layer named here is a layer that was told.
        ///</summary>
        [JsonPropertyName("pf")]
        public List<string> LayerFills { get; set; } = new List<string>();

        ///<summary>
        ///Colors chosen recently, newest first, so the picker can offer them again. Kept across files -
        ///a palette someone is working to is theirs, not the file's.
        ///</summary>
        [JsonPropertyName("r")]
        public List<string> RecentColors { get; set; } = new List<string>();

        [JsonPropertyName("o")]
        public float Opacity { get; set; } = 0.5f;

        ///<summary>Whether the 2D view draws a grid, and whether the pointer lands on it.</summary>
        [JsonPropertyName("gs")]
        public bool ShowGrid { get; set; }

        [JsonPropertyName("gn")]
        public bool SnapToGrid { get; set; }

        ///<summary>Whether the pointer lands on the corners and edges of shapes already drawn.</summary>
        [JsonPropertyName("gz")]
        public bool SnapToShapes { get; set; }

        ///<summary>
        ///How far apart the grid lines are, in microns.
        ///
        ///In microns rather than in database units so it survives being carried to a different file: a
        ///micron is a micron, where a database unit is whatever that file's UNITS record says it is - and a
        ///pitch stored in those would silently mean a thousand times more or less on the next one.
        ///
        ///Defaulting this to the grid the file was drawn on was tried and undone; see gridMicrons in the 2D
        ///view for the measurement that settled it.
        ///</summary>
        [JsonPropertyName("gp")]
        public double GridMicrons { get; set; } = 1;

        ///<summary>
        ///Which unit the pitch above is typed and shown in - "Nanometer", "Micron", "Millimeter" or
        ///"DatabaseUnit".
        ///
        ///A way of writing the pitch rather than the pitch, so it is saved beside one held in microns and
        ///changes nothing about it. Worth keeping for the same reason the pitch is: somebody working in
        ///nanometers is working in nanometers tomorrow too.
        ///
        ///No version bump - an older session has no key here and gets microns, which is what the app showed
        ///before there was a choice.
        ///</summary>
        [JsonPropertyName("gu")]
        public string GridUnit { get; set; } = "Micron";

        ///<summary>
        ///Whether the pitch above was typed by somebody, rather than worked out from the file it was opened
        ///on.
        ///
        ///The pitch follows the file until somebody takes it over, and then it stays taken over. The number
        ///alone cannot say which of those it is, and only one of them should survive opening the next file -
        ///hence a second key rather than reading it out of the first.
        ///
        ///No version bump: an older session has no key here and reads false, which means its pitch is
        ///treated as the file's and the next file may replace it. That is the right way round - a pitch
        ///saved before this existed was the fixed micron nobody chose.
        ///</summary>
        [JsonPropertyName("gq")]
        public bool PitchChosen { get; set; }

        ///<summary>
        ///Whether a shape that comes to rest on another one on its layer becomes one with it - whether it
        ///got there by being drawn or by being moved.
        ///
        ///Saved beside the side count and for the same reason: whether you are building shapes out of
        ///pieces or placing them one beside another is a decision about the work, and one worth coming back
        ///to already set.
        ///
        ///No version bump - an older session simply has no key here and gets the default, which is off.
        ///</summary>
        [JsonPropertyName("gj")]
        public bool Joining { get; set; }

        ///<summary>
        ///How many straight sides stand in for an ellipse the Draw tool makes.
        ///
        ///Saved where the shape being drawn is not, which is the same line the tool falls on: how round a
        ///round thing should be is a decision about the work, where which shape your hand is drawing right
        ///now is not something to come back to a week later already set to.
        ///</summary>
        [JsonPropertyName("gc")]
        public int EllipseSides { get; set; } = 64;

        ///<summary>
        ///How wide a path the Draw tool makes is, in microns, and how its ends are finished.
        ///
        ///In microns rather than in the file's units for the same reason the grid pitch is: a width is a real
        ///dimension, and one stored in units would silently mean a thousand times more or less on a file with
        ///a different scale. Saved for the same reason the side count is - it is a decision about the work.
        ///</summary>
        [JsonPropertyName("pw")]
        public double PathWidthMicrons { get; set; } = 0;

        [JsonPropertyName("pe")]
        public string PathEnds { get; set; } = "Flush";

        ///<summary>
        ///Whether the page had given the view its margins.
        ///
        ///**Ours rather than the browser's.** The button does not call requestFullscreen - that needs a
        ///gesture the browser can refuse and could not be put back on a load at all. This is the four rems
        ///of padding the page wraps itself in, which is a setting like any other here, and somebody who
        ///wants the whole window for a layout wants it again next time.
        ///
        ///No version bump: an older session has no key here and reads false, which is the margins being
        ///there - what the app did before there was a button.
        ///</summary>
        [JsonPropertyName("fs")]
        public bool FullScreen { get; set; }

        ///<summary>
        ///Where the 2D view was looking, as the viewBox it was left on - "x y width height".
        ///
        ///One string rather than four numbers because that is what an SVG attribute is, and splitting it
        ///into keys would mean putting it back together at both ends. Written by
        ///<see cref="GDSViewer.Components.Viewer2DSvg"/> in the invariant culture, since a session saved on
        ///a machine that writes decimals with commas has to be readable on one that does not.
        ///
        ///Empty means nothing has been said, and the view frames the drawing the way it always did - which
        ///is also what an older session gets, and what a box that will not parse falls back to.
        ///
        ///No version bump: a missing key reads as empty, which is the old behavior exactly.
        ///</summary>
        [JsonPropertyName("vb")]
        public string View2DBox { get; set; } = "";

        ///<summary>
        ///Where the 3D camera was, as "x y z" of its position followed by "x y z" of what it orbits.
        ///
        ///Six numbers, not three: where a camera is says nothing about which way it points, and the orbit
        ///target is what the controls turn around. Restoring the position alone would leave you looking at
        ///the origin from somewhere you never chose to be.
        ///
        ///Empty when nothing has been said - a view nobody has turned keeps the opening angle, which is
        ///also what an older session and an unreadable value get.
        ///
        ///No version bump: a missing key reads as empty, which is the old behavior exactly.
        ///</summary>
        [JsonPropertyName("cam")]
        public string View3DCamera { get; set; } = "";

        ///<summary>
        ///Which cell the 2D editor was in, by name, or empty for the whole layout.
        ///
        ///The name and not the path it was reached by. Coming back re-enters it the same way opening it
        ///from the library does - through a shape in it, which rebuilds the whole breadcrumb, and directly
        ///when the cell has no shape to go through. So the crumb comes back for any cell that has anything
        ///in it, and an empty one opens at the top, which is the only honest answer for a cell that is
        ///placed several times.
        ///
        ///Refused if the file no longer has a cell by that name - a session outlives a rename.
        ///
        ///No version bump: a missing key reads as empty, which is the whole layout, and is what the app did
        ///before there was anything to remember.
        ///</summary>
        [JsonPropertyName("cell")]
        public string EditingCell { get; set; } = "";

        ///<summary>
        ///Which tool the 2D editor was in - "Pan", "Measure", "Select", "Move" or "Draw".
        ///
        ///This was deliberately left out once, on the grounds that a tool is what you are doing now rather
        ///than how you left the file. That is a reasonable line and it was drawn in the wrong place: opening
        ///a layout to carry on moving things means reaching for Move first, every time.
        ///
        ///A name this build does not know costs the tool and nothing else, and so does Draw with no cell to
        ///draw into - both leave the view in Pan, which is where it opens.
        ///
        ///No version bump: a missing key reads as empty, which is Pan, and is what the app did before.
        ///</summary>
        [JsonPropertyName("tool")]
        public string Tool { get; set; } = "";

        ///<summary>
        ///Whether the cell's own actions were showing - the Rename, Copy to and Delete behind the ellipsis
        ///on the context bar.
        ///
        ///**The only panel in the app worth keeping.** The rest are open for as long as they are pointed at
        ///and go the moment they are not - Examples, History, the library, the grid, the shapes, the
        ///backdrops - so a restored one would vanish on the first movement of the mouse, which is a worse
        ///answer than not restoring it. The layer settings popup is fixed at the point it was opened from,
        ///and putting it back a session later means putting it at a coordinate that no longer means
        ///anything. This one is a disclosure with no position and no timer, and it stays where it is put.
        ///
        ///No version bump: a missing key reads as false, which is shut, and is where the bar opens.
        ///</summary>
        [JsonPropertyName("ca")]
        public bool CellActions { get; set; }

        ///<summary>
        ///Whether the cell tree is docked open down the left of the view.
        ///
        ///Kept for the same reason as CellActions and unlike the rest: it is opened by a press and closed by
        ///one, with nothing timed and no position to put back. It is also the panel most likely to be wanted
        ///open for a whole sitting - somebody reading a hierarchy is reading it while they work, and being
        ///made to re-open it on every visit is the sort of thing that stops it being used.
        ///
        ///True by default, the same as Layers - the two sidebars are a pair, and a file explorer that
        ///opened shut would be one most people never found. No version bump: a missing key now reads as
        ///open, which is where the app starts.
        ///</summary>
        [JsonPropertyName("ct")]
        public bool CellTree { get; set; } = true;

        ///<summary>
        ///Whether the layer list is showing.
        ///
        ///**True by default, where the cell tree is false**, because a missing key has to mean what the app
        ///did before it existed - and the layer list has always been there. Reading it as shut would hide a
        ///panel from everybody who had ever opened the app, once, on the release that added the switch.
        ///
        ///Not the width, which the drag resets on every open. See .layerSidebar.
        ///</summary>
        [JsonPropertyName("ls")]
        public bool Layers { get; set; } = true;

        ///<summary>
        ///How far the 3D view's slider has pulled the layers apart, where nought is the process stack itself.
        ///
        ///**The same default the view opens on**, because a session with no key for this is read by the same
        ///code that reads one that has it - so a number written only here decides what a first visit gets. It
        ///was 50, from when the slider's minimum was 50, and it went on winning after that minimum became
        ///nought: the control opened at 50 on a cleared browser and the stack came up pre-spread, with
        ///nothing in the view or the library still saying 50 anywhere.
        ///</summary>
        [JsonPropertyName("s")]
        public int LayerSpacing { get; set; } = AdditionalGDSInformation.DefaultLayerSpread;

        [JsonPropertyName("g")]
        public string Background { get; set; } = "none";

        [JsonPropertyName("m")]
        public string ModelFileType { get; set; } = ".stl";

        ///<summary>
        ///Which format the download button writes - ".gds" or ".oas".
        ///
        ///No version bump: an older session simply has no "d" key and gets the default, which is the same
        ///thing the app did before there was a choice. Only a shape an older reader would *misread* is
        ///worth dropping a session over.
        ///</summary>
        [JsonPropertyName("d")]
        public string DownloadFormat { get; set; } = ".gds";

        ///<summary>
        ///The 2D editor's undo stack, so a refresh costs the page and not what can be taken back.
        ///
        ///**The edits, not the file before them.** The bytes above already carry what was changed, which is
        ///why an edit survived a refresh before this existed; what did not survive was the ability to undo
        ///it. Storing the file as it was at each step would be trivially correct and would cost a copy of it
        ///per keystroke - so what is here is each change and how to reverse it, addressed by where a shape
        ///sits rather than by the objects it was made against, which do not come back. See
        ///<see cref="GdsII.EditRecord"/>.
        ///
        ///No version bump: an older session has no "u" key and gets no stack, which is exactly what the app
        ///did before there was one. Left out entirely when there is nothing to say, rather than written as a
        ///null - a file nobody has edited is the common case, and it should not pay for this at all.
        ///</summary>
        [JsonPropertyName("u")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SavedEdits? Edits { get; set; }

        ///<summary>
        ///Bumped when the shape changes in a way an older reader would get wrong. A session from a different
        ///version is dropped rather than guessed at - it is a convenience, and the cost of dropping it is
        ///one reopened file.
        ///
        ///2: "x" went from a bool for the whole file to the list of layers whose labels are off. A version 1
        ///session has a bool under that key, which is not a list - so it has to be recognized and dropped
        ///rather than parsed.
        ///</summary>
        public const int CurrentVersion = 2;

        public static string Serialize(SavedSession session)
        {
            return JsonSerializer.Serialize(session, SavedJson.Default.SavedSession);
        }

        ///<summary>
        ///Reads a session back, or null if it cannot be trusted. Never throws: this is called on the path
        ///that starts the app, so a stored value that has been corrupted, truncated by a quota, or written
        ///by another version has to cost the session and not the page.
        ///</summary>
        public static SavedSession? Deserialize(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var session = JsonSerializer.Deserialize(json, SavedJson.Default.SavedSession);

                if (session is null || session.Version != CurrentVersion)
                    return null;

                return session;
            }
            catch
            {
                return null;
            }
        }
    }
}
