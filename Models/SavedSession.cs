using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using GdsII;

namespace GDSViewer.Models
{
    ///<summary>
    ///Everything the app puts back when you return: which file was open, what had been edited into it, how
    ///the layers were named and which were showing, and where the controls were left.
    ///
    ///**The file's bytes are part of a session on purpose.** An uploaded file exists only in the tab it was
    ///dropped into, and a text-view edit exists only until the page goes away - so a session that remembered
    ///the name of a bundled example would still lose the one thing that cannot be got back. Storing the bytes
    ///is what makes closing the browser mid-edit safe. It is also why this goes to IndexedDB rather than
    ///localStorage; see <see cref="AppStorage"/>. They are stored beside the rest rather than inside it, and
    ///written only when the file changes - see <see cref="BytesApart"/>.
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
        ///
        ///**Empty in what is written now, too, with the bytes kept apart** - see <see cref="BytesApart"/>. A
        ///session written before that carries them here, and is read the way it always was.
        ///</summary>
        [JsonPropertyName("b")]
        public string FileBytes { get; set; } = "";

        ///
        ///The file itself as bytes, when it was read that way - see <see cref="BytesFromAsync"/> - with
        ///<see cref="FileBytes"/> left empty. Never written: what goes beside a session is written from the file on
        ///screen, never from a session read back.
        ///
        [JsonIgnore]
        public byte[]? FileData { get; set; }

        ///<summary>Whether the session carries its file, as bytes or as base64, rather than naming a bundled example to fetch.</summary>
        [JsonIgnore]
        public bool CarriesFile => FileData is not null || FileBytes.Length > 0;

        ///<summary>The file's bytes: <see cref="FileData"/>, or <see cref="FileBytes"/> decoded at a pace.</summary>
        public async ValueTask<byte[]> FileAsync(Func<ValueTask> pause)
        {
            if (FileData is not null)
                return FileData;

            return await AppStorage.FromBase64Async(FileBytes, 0, pause);
        }

        ///<summary>
        ///Whether the file's bytes are stored apart from the rest, under a key of their own beside this one.
        ///
        ///**A session is written far more often than the file changes.** Every pan that comes to rest saves
        ///one, and so does every switch and slider - and with the bytes inside, each of those serialized and
        ///compressed the whole file again: a third of a second after every pan of an eight megabyte upload,
        ///on a published build, and seconds on the dev server. Apart, they are written when the file changes,
        ///which is an edit or a file opened, and a save is the settings alone.
        ///
        ///Whoever reads the session fills <see cref="FileBytes"/> back in from that key before using it, with
        ///<see cref="BytesFrom"/> - the shell for its own session and <see cref="HistoryStore"/> for a history
        ///row - so everything after the read sees one shape either way. Left out when false, which is what every session written before
        ///this says by not saying it.
        ///</summary>
        [JsonPropertyName("ba")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool BytesApart { get; set; }

        ///
        ///The steps taken since the bytes beside this were written, to be taken again on them - see
        ///<see cref="EditHistory.Journal"/>.
        ///
        ///**What an edit saves now, instead of the file.** Writing the whole file after every edit was most of
        ///what an edit cost on a real chip: 100 - 140 ms of every corner dropped on the RGB mixer, on a published
        ///build. The bytes are written whole when the file is opened, downloaded or put away, when the page is
        ///hidden, and when this grows long; in between, an edit writes the steps since.
        ///
        ///No version bump: a session without one has nothing to take again, which is what every session said
        ///before there were any. Left out when there are none.
        ///
        [JsonPropertyName("j")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SavedStep>? Journal { get; set; }

        ///
        ///Which writing of the file the bytes beside this are - the name they were stored under, which the
        ///<see cref="Journal"/> is only ever taken again on.
        ///
        ///**Two keys, read one at a time, and either can come from either store.** A session and its bytes are
        ///written together, but read separately - and a value missing from IndexedDB is read from the copy the
        ///unload handler left in localStorage, which can be an older writing. Steps taken again on bytes they were
        ///not taken on would put them onto the wrong shapes and hand that back as somebody's file. So the bytes
        ///are stored under a name, the session says which name its steps belong to, and the steps are taken
        ///again only on bytes of that name. See <see cref="Stored"/>.
        ///
        [JsonPropertyName("bs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Base { get; set; }

        ///<summary>The name the bytes filled in from beside this were stored under, or empty for bytes stored before there were names. Never written.</summary>
        [JsonIgnore]
        public string BytesBase { get; set; } = "";

        ///<summary>
        ///What goes beside a session: the file's bytes, under the name of this writing of them. See <see cref="Base"/>.
        ///</summary>
        public static string Stored(string name, string bytes)
        {
            return name + NameEnds + bytes;
        }

        ///
        ///Fills <see cref="FileBytes"/> in from what is stored beside the session, and <see cref="BytesBase"/>
        ///from the name in front of them.
        ///
        ///Bytes stored before there were names are base64 and nothing else, and base64 has no colon in it - so
        ///they come back as they are, under no name.
        ///
        public void BytesFrom(string stored)
        {
            int ends = stored.IndexOf(NameEnds);

            if (ends < 0 || ends > LongestName)
            {
                BytesBase = "";
                FileBytes = stored;

                return;
            }

            BytesBase = stored[..ends];
            FileBytes = stored[(ends + 1)..];
        }

        ///
        ///<see cref="BytesFrom"/> at a pace, for what is stored beside the session read as the bytes of its text - see
        ///AppStorage.ReadAtAPaceAsync: the name in front, then the base64 behind it decoded into <see cref="FileData"/> a
        ///run at a time, with <paramref name="pause"/> awaited between runs.
        ///
        ///**So the file is never text on the way.** As a string it was eleven million characters for gpiov2, copied again
        ///behind its name and then decoded - garbage at several times the file's size, all of it made while a picture of
        ///it was being drawn. False for nothing at all, or what will not inflate or decode, where BytesFrom's caller found
        ///an empty string; only a pause that throws gets out of it, which is how a read is given up.
        ///
        public async ValueTask<bool> BytesFromAsync(Stream stored, Func<ValueTask> pause)
        {
            try
            {
                byte[] run = new byte[RunBytes];
                int held = stored.ReadAtLeast(run, run.Length, throwOnEndOfStream: false);

                if (held == 0)
                    return false;

                //The name, found where BytesFrom finds it: up to the first colon, if that is within LongestName characters.
                //A character is three bytes at most, and no byte of one but a colon's own is a colon.
                int ends = run.AsSpan(0, Math.Min(held, LongestName * 3 + 1)).IndexOf((byte)NameEnds);
                int from = 0;

                BytesBase = "";

                if (ends >= 0)
                {
                    string name = Encoding.UTF8.GetString(run, 0, ends);

                    if (name.Length <= LongestName)
                    {
                        BytesBase = name;
                        from = ends + 1;
                    }
                }

                var pieces = new List<byte[]>();
                int total = 0;

                while (true)
                {
                    //A run that did not fill is the last: there is nothing more to read.
                    bool last = held < run.Length;
                    int length = held - from;

                    //Short of the end, whole groups of four and never the last of them: the end's may be padded, which
                    //only a final block may be.
                    if (!last)
                        length = (length - 1) / 4 * 4;

                    byte[] piece = new byte[length / 4 * 3];

                    if (Base64.DecodeFromUtf8(run.AsSpan(from, length), piece, out _, out int wrote, last) != OperationStatus.Done)
                        return false;

                    if (wrote < piece.Length)
                        Array.Resize(ref piece, wrote);

                    pieces.Add(piece);
                    total += wrote;

                    if (last)
                        break;

                    //What is left over, a group at most, to the front, and the run filled again behind it.
                    int left = held - from - length;

                    Buffer.BlockCopy(run, from + length, run, 0, left);
                    from = 0;

                    await pause();

                    held = left + stored.ReadAtLeast(run.AsSpan(left), run.Length - left, throwOnEndOfStream: false);
                }

                byte[] data = new byte[total];
                int at = 0;

                foreach (byte[] piece in pieces)
                {
                    Buffer.BlockCopy(piece, 0, data, at, piece.Length);
                    at += piece.Length;
                }

                FileData = data;
                FileBytes = "";

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return false;
            }
        }

        ///<summary>
        ///Whether the journal belongs to the bytes filled in beside it, and so may be taken again on them.
        ///</summary>
        public bool JournalFits
        {
            get { return !string.IsNullOrEmpty(Base) && Base == BytesBase; }
        }

        private const char NameEnds = ':';

        private const int LongestName = 64;

        ///<summary>How much of what is stored beside a session BytesFromAsync inflates and decodes between pauses.</summary>
        private const int RunBytes = 1 << 16;

        ///<summary>Which view was on screen, by the slug the address uses.</summary>
        [JsonPropertyName("w")]
        public string View { get; set; } = "";

        ///<summary>The layer names and colors, as the same CSV a layermap file holds.</summary>
        [JsonPropertyName("l")]
        public string LayerNames { get; set; } = "";

        ///
        ///The names that carry onto the next file opened: the ones the visitor chose - typed, imported, or made theirs
        ///by touching a layer - where <see cref="LayerNames"/> is everything on this file, whatever the app or the
        ///address laid over it. See Viewer.razor's carryLayerNamesOver.
        ///
        ///**Kept apart since 2026-10-14**, because restoring the one as the other made assumed names the visitor's
        ///own: after a reload, the bundled sky130 names and a layermap an address named landed on their next upload.
        ///Null in a session written before then, which carries its file's names the way it always did.
        ///
        [JsonPropertyName("lc")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CarriedLayerNames { get; set; }

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

        //
        //**Two flags went from here on 2026-10-19**: "ln", that Clear in the layer panel had declined the PDK's names for
        //this file, and NoBundledDeck, the same for its rules. Clear went with the rows it was in, so nothing sets them;
        //a session that still carries either is read without it, and the file is named and checked by the PDK in use.
        //"ln" is not to be used for anything else, so an old session cannot be read as saying something new.
        //

        ///
        ///Whether the file was put under the PDK in use by hand - a layout started from the PDK's layers, or before
        ///2026-10-19 Load in either panel - which keeps it there however its layers fit the PDK. See Viewer.razor's
        ///pdkTaken.
        ///
        [JsonPropertyName("pt")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool PdkTaken { get; set; }

        ///
        ///Whether the PDK in use was laid over the file when it was put away, so the names on it were that PDK's - which
        ///a restore lays the PDK over again, as it is by then. A file named by a page's own layermap was not, and comes
        ///back as it was left; and nor was a session from before PDKs. See Viewer.razor's applyRestoredLayers.
        ///
        [JsonPropertyName("po")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool PdkLaidOver { get; set; }

        ///<summary>
        ///The deck's rules that were switched off, by id. The off set, like the hidden layers, since a rule
        ///starts on. Belongs with the deck rather than with a file, the way the deck itself does.
        ///</summary>
        [JsonPropertyName("ro")]
        public List<string> RulesOff { get; set; } = new List<string>();

        ///<summary>
        ///The faults somebody waived - looked at and let stand - by <see cref="DrcViolation.Key"/>.
        ///
        ///**With the file rather than with the deck**, unlike <see cref="RulesOff"/>: a waiver is about a fault in
        ///this layout, at this place, and the next file has faults of its own. Kept by what the fault is, so a
        ///check run again after a reload finds the same ones waived.
        ///</summary>
        [JsonPropertyName("wv")]
        public List<string> Waived { get; set; } = new List<string>();

        ///<summary>The pairs that were switched **off**, written "65/20". The off set is the short one.</summary>
        [JsonPropertyName("h")]
        public List<string> HiddenLayers { get; set; } = new List<string>();

        ///
        ///The pairs that were locked, written "65/20".
        ///
        ///The on set this time, and for the same reason the hidden one is the off set: a layer opens
        ///unlocked, so the locked ones are the exceptions. The lock handler always said it saved, and there
        ///was nowhere for it to go - a reload handed every layer back unlocked.
        ///
        [JsonPropertyName("lk")]
        public List<string> LockedLayers { get; set; } = new List<string>();

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
        ///**On unless somebody turns it off, since 2026-11-03** (asked for): the app opens with the whole window for
        ///the editor, the title bar still over it. The key changed with it, from "fs" to "full": every session from
        ///before held "fs" false for anybody who had simply never pressed the button, which is not a choice worth
        ///keeping over the new default - so an older session reads as having said nothing, which is on.
        ///</summary>
        [JsonPropertyName("full")]
        public bool FullScreen { get; set; } = true;

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
        ///How far the 3D view's slider has pulled the layers apart, where zero is the process stack itself.
        ///
        ///**The same default the view opens on**, because a session with no key for this is read by the same
        ///code that reads one that has it - so a number written only here decides what a first visit gets. It
        ///was 50, from when the slider's minimum was 50, and it went on winning after that minimum became
        ///zero: the control opened at 50 on a cleared browser and the stack came up pre-spread, with
        ///nothing in the view or the library still saying 50 anywhere.
        ///</summary>
        [JsonPropertyName("s")]
        public int LayerSpacing { get; set; } = AdditionalGDSInformation.DefaultLayerSpread;

        ///<summary>
        ///How far that slider reaches, which the box beside it sets.
        ///
        ///700 when there is no key for it, which is the reach the slider had before the box existed - so a session
        ///written then opens on the slider it was left with.
        ///</summary>
        [JsonPropertyName("sr")]
        public int SpacingRange { get; set; } = 700;

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
