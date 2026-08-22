using System.Globalization;
using System.Text;

namespace GdsII
{
    ///<summary>
    ///A layer/datatype to name mapping, read from text the user supplies.
    ///
    ///**Why this has to come from outside the file.** A GDSII file carries only numbers - nothing in the
    ///format records what 65/20 means. The mapping is PDK data, which is why every tool gets it from a
    ///separate file the user chooses: KLayout from a `.lyp` layer-properties file or a reader layer map,
    ///Magic from its techfile, Cadence from a layermap. This app does the same rather than baking one PDK's
    ///table in, which would also be the one piece of it under somebody else's license.
    ///
    ///**The format.** `layer,datatype,name` per line, which is a Cadence-style layermap with commas -
    ///`65,20,diff.drawing`. That is deliberate: a table exported from any PDK converts to it mechanically,
    ///and it is short enough to type by hand. A fourth column sets the layer's color, for a file exported
    ///from a `.lyp` that carries them.
    ///
    ///**A seventh column says what the layer is for**: `conductor`, `via` or `none`, which is what tracing a
    ///net needs and the one thing here that no PDK table already carries - so it is the column somebody
    ///fills in by hand, knowing which of their numbers are metal. See <see cref="LayerRole"/>.
    ///
    ///**A fifth and sixth column carry the process stack**: the layer's height and its thickness, in
    ///database units - which for a file whose UNITS make a database unit a nanometer, as every bundled
    ///example does, is a real process table typed in as it stands. That is what turns the 3D view from
    ///evenly spaced planes into something with the shape of an actual wafer, and it is the same thing
    ///GDS3D asks for in its process definition file. Every column past the third is optional, so a
    ///three-column mapping written for an older build of this app still reads.
    ///
    ///Blank lines and `#` comments are skipped, whitespace around a field is trimmed, and a header row
    ///naming the columns is recognized and skipped so that a spreadsheet's own export works.
    ///</summary>
    public class LayerNames
    {
        #region Constants *******************************************************************

        ///<summary>
        ///How many bad lines are reported before the rest are counted rather than listed. A file with a
        ///wrong delimiter throws an error per line, and a thousand of them tells the reader nothing that
        ///the first few do not.
        ///</summary>
        private const int MaximumReportedProblems = 5;

        #endregion **************************************************************************



        #region Properties ******************************************************************

        ///<summary>The names, by the pair they belong to.</summary>
        public Dictionary<LayerKey, string> Names { get; } = new Dictionary<LayerKey, string>();

        ///<summary>The colors, for the rows that carried one. Sparse: a row need not set a color.</summary>
        public Dictionary<LayerKey, string> Colors { get; } = new Dictionary<LayerKey, string>();

        ///<summary>
        ///The process stack, for the rows that carried one - height and thickness together, since a height
        ///without a thickness is half a slab. Sparse, like the colors.
        ///</summary>
        public Dictionary<LayerKey, (int Height, int Thickness)> Stack { get; } = new Dictionary<LayerKey, (int, int)>();

        ///<summary>
        ///What each layer is for, for the rows that said - which is what makes tracing a net possible at all.
        ///Sparse: most rows of a real PDK table say nothing about it, and a layer nothing was said about takes
        ///no part. See <see cref="LayerRole"/>.
        ///</summary>
        public Dictionary<LayerKey, LayerRole> Roles { get; } = new Dictionary<LayerKey, LayerRole>();

        ///<summary>
        ///The fill pattern each layer was given, for the rows that said. Sparse, like the rest: every layer
        ///is solid until something says otherwise. See <see cref="LayerFill"/>.
        ///</summary>
        public Dictionary<LayerKey, LayerFill> Fills { get; } = new Dictionary<LayerKey, LayerFill>();

        ///<summary>
        ///What each layer's pattern is drawn in, where a row gave it a color of its own. Sparse, and absent
        ///means the pattern follows the layer's color - see <see cref="Layer.PatternColor"/>.
        ///</summary>
        public Dictionary<LayerKey, string> PatternColors { get; } = new Dictionary<LayerKey, string>();

        ///<summary>
        ///How big each layer's pattern is held on screen, in pixels, where a row said. Sparse; absent means
        ///the usual size. See <see cref="Layer.PatternPixels"/>.
        ///</summary>
        public Dictionary<LayerKey, int> PatternSizes { get; } = new Dictionary<LayerKey, int>();

        ///<summary>What could not be read, by line number. A file is read as far as it can be.</summary>
        public List<string> Problems { get; } = new List<string>();

        ///<summary>
        ///How many layers this mapping says anything about - a name, a color, a stack, or several.
        ///
        ///Not <c>Names.Count</c>, which it was: a row can set a color or a height without naming anything,
        ///and counting only the names would report a file full of heights as having been read as nothing.
        ///</summary>
        public int Count
        {
            get
            {
                return Names.Keys
                    .Concat(Colors.Keys)
                    .Concat(Stack.Keys)
                    .Concat(Roles.Keys)
                    .Concat(Fills.Keys)
                    .Concat(PatternColors.Keys)
                    .Concat(PatternSizes.Keys)
                    .Distinct()
                    .Count();
            }
        }

        #endregion **************************************************************************



        #region Reading *********************************************************************

        ///<summary>
        ///Reads a mapping, keeping every line that parses.
        ///
        ///Deliberately **not** all-or-nothing, unlike saving an edited GDS file. That refuses everything
        ///because a half-applied edit would corrupt a layout; this only labels what is already drawn, so a
        ///file with one bad row is still worth the rows that are good - and refusing the lot over a stray
        ///line would be the more annoying failure.
        ///</summary>
        public static LayerNames Parse(string text)
        {
            var names = new LayerNames();

            if (string.IsNullOrWhiteSpace(text))
                return names;

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i < lines.Length; i++)
                names.readLine(lines[i], i + 1);

            return names;
        }

        private void readLine(string line, int lineNumber)
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                return;

            string[] fields = trimmed.Split(',');

            if (fields.Length < 3)
            {
                report($"Line {lineNumber} has {fields.Length} field(s) where at least 3 are needed: layer,datatype,name.");

                return;
            }

            //A header row is the one line whose numbers are words. Recognized rather than required, so a
            //file with one works and a file without one works too.
            if (isHeader(fields))
                return;

            if (!tryParseNumber(fields[0], out short layer))
            {
                report($"Line {lineNumber}: \"{fields[0].Trim()}\" is not a layer number.");

                return;
            }

            if (!tryParseNumber(fields[1], out short dataType))
            {
                report($"Line {lineNumber}: \"{fields[1].Trim()}\" is not a data type.");

                return;
            }

            var key = new LayerKey(layer, dataType);

            string name = fields[2].Trim();
            string color = field(fields, 3);

            //Last one wins rather than the first, so appending a correction to a file works the way editing
            //the line would.
            if (name.Length > 0)
                Names[key] = name;

            if (color.Length > 0)
                Colors[key] = color;

            bool stacked = readStack(fields, key, lineNumber);
            bool roled = readRole(fields, key, lineNumber);
            bool filled = readFill(fields, key, lineNumber);
            bool patterned = readPattern(fields, key, lineNumber);

            //A row that sets nothing at all.
            //
            //A name used to be the price of entry, and that made a template unable to come back: Template
            //writes a row per layer and leaves the name blank for the ones that have none, so every height
            //it carried was thrown away at the door. A name is one of the things a row can set now, not the
            //condition for setting the others - and a row that says nothing is still worth reporting,
            //because that is what a file with its columns shifted looks like.
            if (name.Length == 0 && color.Length == 0 && !stacked && !roled && !filled && !patterned)
                report($"Line {lineNumber} names no layer.");
        }

        ///
        ///The fill pattern, when a row says: `dots`, `grid`, `diagonal` and the rest.
        ///
        ///The eighth column, and typed by hand like the role rather than exported: a PDK table carries the
        ///colors and the heights and has no opinion about stipples, which are somebody looking at a
        ///particular screen deciding two of their layers are too alike to tell apart.
        ///
        ///Reported rather than ignored when the word is unknown, for the reason the role is: a misspelling
        ///reads as a layer nobody asked to pattern, which looks exactly like the column not working.
        ///
        private bool readFill(string[] fields, LayerKey key, int lineNumber)
        {
            string fill = field(fields, 7);

            if (fill.Length == 0)
                return false;

            //Case-insensitively against the enum's own names, so the list here cannot fall behind the list
            //the popup offers - which is the drift a hand-written switch of eight words would have.
            if (Enum.TryParse(fill, ignoreCase: true, out LayerFill named) && Enum.IsDefined(named))
            {
                Fills[key] = named;

                return true;
            }

            report($"Line {lineNumber}: \"{fill}\" is not a fill pattern. Use {string.Join(", ", Enum.GetNames<LayerFill>()).ToLowerInvariant()}.");

            return false;
        }

        ///
        ///The two columns that say how the pattern is drawn rather than which one it is: what color its
        ///marks are, and how big a repeat of it is on screen.
        ///
        ///Both optional and independent - a layer can be given a hatch color without a size, and a size
        ///without a color - so this reads each on its own and reports each on its own. Either one is enough
        ///to make the row worth keeping.
        ///
        ///The size is in screen pixels rather than in the file's units, which is the same number the popup
        ///offers; see <see cref="Layer.PatternPixels"/> for why that is the useful end of it.
        ///
        private bool readPattern(string[] fields, LayerKey key, int lineNumber)
        {
            bool read = false;

            string color = field(fields, 8);

            if (color.Length > 0)
            {
                PatternColors[key] = color;
                read = true;
            }

            string size = field(fields, 9);

            if (size.Length == 0)
                return read;

            //Bounded rather than merely parsed: a tile under a pixel is a flat tone that reads as a solid
            //fill, and one bigger than the view is a single mark somewhere off screen. Both look like the
            //column doing nothing, so a number outside the range the popup offers is reported.
            if (!tryParseNumber(size, out short pixels) || pixels < LeastPatternPixels || pixels > MostPatternPixels)
            {
                report($"Line {lineNumber}: \"{size}\" is not a pattern size. Use a whole number of pixels from {LeastPatternPixels} to {MostPatternPixels}.");

                return read;
            }

            PatternSizes[key] = pixels;

            return true;
        }

        ///<summary>The smallest pattern worth drawing, in screen pixels. Below this every fill is a flat tone.</summary>
        public const int LeastPatternPixels = 2;

        ///<summary>The largest, in screen pixels. Past this a shape holds less than one repeat of its pattern.</summary>
        public const int MostPatternPixels = 64;

        ///
        ///What the layer is for, when a row says: `conductor`, `via`, or `none`.
        ///
        ///Words rather than numbers, because this column is one somebody types by hand more often than they
        ///export it - a PDK table has the heights in it already and does not have this, so the roles are
        ///filled in afterwards by whoever knows which numbers are metal.
        ///
        ///A word this does not know is reported rather than ignored: a misspelled role reads as a layer that
        ///simply takes no part, which looks exactly like a net that ends there.
        ///
        private bool readRole(string[] fields, LayerKey key, int lineNumber)
        {
            string role = field(fields, 6);

            if (role.Length == 0)
                return false;

            if (role.Equals("conductor", StringComparison.OrdinalIgnoreCase))
            {
                Roles[key] = LayerRole.Conductor;

                return true;
            }

            if (role.Equals("via", StringComparison.OrdinalIgnoreCase))
            {
                Roles[key] = LayerRole.Via;

                return true;
            }

            if (role.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                Roles[key] = LayerRole.None;

                return true;
            }

            report($"Line {lineNumber}: \"{role}\" is not a role. Use conductor, via or none.");

            return false;
        }

        ///<summary>
        ///The height and thickness columns, when a row carries them.
        ///
        ///Both together or neither. A height with no thickness is a plane rather than a slab, and guessing
        ///the missing half would put a layer somewhere nobody asked for - so the row keeps its name and its
        ///color, and the stack column is reported and skipped.
        ///</summary>
        private bool readStack(string[] fields, LayerKey key, int lineNumber)
        {
            string height = field(fields, 4);
            string thickness = field(fields, 5);

            if (height.Length == 0 && thickness.Length == 0)
                return false;

            if (height.Length == 0 || thickness.Length == 0)
            {
                //Named as the one that *was* given, since that is the half the reader can see in their file.
                string given = "height";

                if (height.Length == 0)
                    given = "thickness";

                report($"Line {lineNumber} gives a {given} without the other. Both are needed to place a layer, so neither was used.");

                return false;
            }

            if (!tryParseStackValue(height, out int parsedHeight))
            {
                report($"Line {lineNumber}: \"{height}\" is not a height.");

                return false;
            }

            if (!tryParseStackValue(thickness, out int parsedThickness))
            {
                report($"Line {lineNumber}: \"{thickness}\" is not a thickness.");

                return false;
            }

            //A layer of no thickness draws nothing at all, which reads as the layer having gone missing
            //rather than as a number being wrong. A negative one turns the slab inside out.
            if (parsedThickness <= 0)
            {
                report($"Line {lineNumber}: a thickness of {parsedThickness} would draw nothing, so it was not used.");

                return false;
            }

            Stack[key] = (parsedHeight, parsedThickness);

            return true;
        }

        private static string field(string[] fields, int index)
        {
            if (fields.Length <= index)
                return "";

            return fields[index].Trim();
        }

        ///<summary>
        ///Whether the first two fields are words rather than numbers, which is what a header row looks
        ///like. A row whose numbers parse is data even if its third field says "name".
        ///</summary>
        private static bool isHeader(string[] fields)
        {
            return !tryParseNumber(fields[0], out _) && !tryParseNumber(fields[1], out _);
        }

        ///<summary>
        ///Invariant on purpose. These are field numbers out of a data file, not prose: on a comma-decimal
        ///locale the current culture would read "20" fine but is the wrong thing to have asked, and the
        ///same reasoning applies here as to the record dump.
        ///</summary>
        private static bool tryParseNumber(string field, out short value)
        {
            return short.TryParse(field.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        ///<summary>
        ///A height or a thickness. An int rather than a short like the layer number: these are database
        ///units, and a stack measured in nanometers runs past 32767 before it is out of the metal.
        ///</summary>
        private static bool tryParseStackValue(string field, out int value)
        {
            return int.TryParse(field.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private void report(string problem)
        {
            if (Problems.Count < MaximumReportedProblems)
                Problems.Add(problem);
        }

        #endregion **************************************************************************



        #region Applying ********************************************************************

        ///<summary>
        ///Puts the names onto the layers a file actually has, and returns how many landed.
        ///
        ///A mapping usually covers a whole PDK where a file uses a handful of its layers, so most rows
        ///matching nothing is the normal case rather than a problem. Zero matching, though, is worth
        ///telling the user about: it means the mapping is for a different technology, or the columns are
        ///the wrong way round.
        ///</summary>
        public int ApplyTo(Dictionary<LayerKey, Layer> layers)
        {
            int applied = 0;

            foreach (var layer in layers)
            {
                //Counted per layer touched, whatever the mapping had to say about it. It used to be gated
                //on the name, so a mapping that only carried colors or heights reported having applied
                //nothing - and the caller turns a zero into "none of them name a layer this file uses",
                //which would have been an outright wrong thing to tell somebody whose stack had just
                //landed.
                bool touched = false;

                if (Names.TryGetValue(layer.Key, out string? name))
                {
                    layer.Value.Name = name;
                    touched = true;
                }

                if (Colors.TryGetValue(layer.Key, out string? color))
                {
                    layer.Value.Color = color;
                    layer.Value.ColorIsCustom = true;
                    touched = true;
                }

                if (Stack.TryGetValue(layer.Key, out var stack))
                {
                    layer.Value.Offset = stack.Height;

                    //What the mapping asked for, kept so the spacing slider can spread from it - see CustomHeight.
                    layer.Value.CustomHeight = stack.Height;
                    layer.Value.Depth = stack.Thickness;
                    layer.Value.StackIsCustom = true;
                    touched = true;
                }

                if (Roles.TryGetValue(layer.Key, out var role))
                {
                    layer.Value.Role = role;
                    touched = true;
                }

                if (Fills.TryGetValue(layer.Key, out var fill))
                {
                    layer.Value.Fill = fill;
                    touched = true;
                }

                if (PatternColors.TryGetValue(layer.Key, out var marks))
                {
                    layer.Value.PatternColor = marks;
                    touched = true;
                }

                if (PatternSizes.TryGetValue(layer.Key, out int pixels))
                {
                    layer.Value.PatternPixels = pixels;
                    touched = true;
                }

                if (touched)
                    applied++;
            }

            return applied;
        }

        ///<summary>
        ///Clears every name and restores the palette colors, for going back to bare numbers without
        ///reloading the file.
        ///</summary>
        public static void Clear(AdditionalGDSInformation information)
        {
            foreach (var layer in information.Layers)
            {
                layer.Value.Name = null;

                //The stack goes with the names, the way the colors do. All three came out of the same
                //mapping, and leaving a hand-built wafer standing under a set of bare numbers would be a
                //stranger state than putting the file back as it opened.
                //
                //**All three fields, not two.** The height is the one that decides where a layer rests -
                //SetStackingOffsets reads CustomHeight, not the flag - so clearing the flag and the
                //thickness took the *look* of the stack away and left its heights standing. Nothing showed
                //while the shipped mapping carried no heights; the moment it carried sky130's, Clear stopped
                //putting the file back as it opened. RestoreStacking has always cleared all three.
                layer.Value.StackIsCustom = false;
                layer.Value.CustomHeight = null;
                layer.Value.Depth = AdditionalGDSInformation.DefaultLayerDepth;

                //And what it is for. A role came out of the same mapping as the rest, and leaving a set of
                //bare numbers still traceable as nets would be the odd state to stop in.
                layer.Value.Role = LayerRole.None;

                //And what is drawn over it, which came out of the same mapping and is the most visible of
                //the four - a layout still hatched after the names went would be the odd one to leave.
                //
                //The two that say how the hatch is drawn go with it. They are meaningless without a fill,
                //and a layer that kept them would put its old hatch color back the moment a pattern was
                //chosen again - a setting from a mapping that has been cleared, arriving later.
                layer.Value.Fill = LayerFill.None;
                layer.Value.PatternColor = null;
                layer.Value.PatternPixels = null;
            }

            information.RestorePaletteColors();

            //
            //**The heights still have to be worked out again, and not from here.**
            //
            //A layer's Offset is where it is actually drawn, and nothing recomputes it until something asks
            //for a restack - so dropping the mapping leaves every layer standing at the height that mapping
            //gave it, and the settings popup, which reads Offset, goes on showing sky130's numbers under a
            //list of bare numbers.
            //
            //Doing it here was tried and is wrong: this has no way of knowing how far apart the 3D view's
            //slider is currently holding the stack, so the only spacing it could pass is the default - which
            //collapses a spread stack and leaves the slider reading 700 over a layout stacked at 50. That is
            //the exact failure slider-carry.spec.js exists to catch. The caller knows the spacing; see
            //Viewer.razor's clearLayerNames, which restacks the way RestoreStacking already does.
            //
        }

        #endregion **************************************************************************



        #region Writing *********************************************************************

        ///<summary>
        ///Just the layers something was said about, for storing in a session.
        ///
        ///Separate from <see cref="Template"/>, which lists every layer so there is something to fill in.
        ///Storing that shape would write a row per untouched layer, and reading it back would then report
        ///each one as a row that names nothing - true, but noise made by us rather than by the user.
        ///
        ///Which layers those are is <see cref="Layer.WasSaid"/>, and the test used to be written out here as
        ///a name or a role. That went stale four columns ago; see there.
        ///</summary>
        public static string Named(AdditionalGDSInformation information)
        {
            var builder = new StringBuilder();

            foreach (var layer in information.OrderedLayers())
            {
                if (!layer.Value.WasSaid)
                    continue;

                appendRow(builder, layer.Key, layer.Value, layer.Value.Name ?? "", everyColumn: false);
            }

            return builder.ToString();
        }

        ///<summary>
        ///Every layer the open file has, with every column filled in, as the file the user is handed.
        ///
        ///**Every column, including the heights nobody set.** This wrote the stack only for a layer that
        ///had been placed by hand, which made the export useless for the thing it is for: the header said
        ///`height,thickness` and not one row had them, so building a stack meant knowing to type two
        ///columns that were not there. What is exported now is what the app is currently drawing, which is
        ///a file you can edit and load back.
        ///
        ///The cost is real and worth naming: reading this back marks every layer as placed, so the 3D
        ///view's spacing slider stops moving them. Reset stack on a row, or Clear on the names, puts that
        ///back.
        ///</summary>
        public static string Export(AdditionalGDSInformation information)
        {
            var builder = new StringBuilder();

            builder.Append(HeaderRow);

            foreach (var layer in information.OrderedLayers())
                appendRow(builder, layer.Key, layer.Value, layer.Value.Name ?? "", everyColumn: true);

            return builder.ToString();
        }

        ///<summary>
        ///One row, in the shape <see cref="Parse"/> reads.
        ///
        ///<paramref name="everyColumn"/> is what separates the two writers. The export fills all six, so
        ///what comes out is a file to edit. What is kept in a session writes the stack only for a layer
        ///that was placed, because recording the automatic heights there would pin every layer of the next
        ///file opened to where this one's happened to sit - and nothing on the row would say it was a
        ///guess.
        ///</summary>
        private static void appendRow(StringBuilder builder, LayerKey key, Layer layer, string name, bool everyColumn)
        {
            //
            //**The columns are positional, so a gap is a shift**: anything written means every column in
            //front of it is written too. Written is not the same as filled in, though - a column with nothing
            //to say goes out empty, which the reader takes as an absence, and the ones after it still land
            //where they belong.
            //
            //Worked out here rather than as a longer condition on each column, which is how the fill column
            //came to carry the role's condition inside its own - and how the next column added would have
            //come to carry both.
            //
            bool wantsSize = layer.PatternPixels is not null;
            bool wantsMarks = wantsSize || (layer.PatternColor is string marks && marks.Length > 0);
            bool wantsFill = wantsMarks || layer.Fill != LayerFill.None;
            bool wantsRole = wantsFill || layer.Role != LayerRole.None;

            //
            //**Only what was chosen, in the three columns that have an automatic answer.**
            //
            //A color out of the palette and a height out of the even spacing are *derived* - both are a
            //function of how many layers this file happens to have - and this row is read back as a set of
            //decisions, so a derived value written here becomes one on the next load.
            //
            //The stack is the half that hurt. A placed layer is a layer SetStackingOffsets refuses to move, so
            //a session that recorded the automatic heights came back with every layer that had a role pinned in
            //place - and the shipped sky130 mapping gives most of them one. The 3D view's spacing slider then
            //moved nothing at all the second time an example was opened, which read as a bug in the slider. It
            //survived a test that says exactly this, because that test names a layer and stops: a name alone
            //writes no stack columns, so the row it checked never reached this branch.
            //
            //The color is the quieter half and the same mistake. Storing a palette color is storing something
            //already known - the same file reopened divides the same gradient the same way - and reading it back
            //marks the layer as recolored by hand, which is exactly what ColorIsCustom exists to deny. What it
            //can do is carry one file's palette onto a *different* file's matching pairs, since a mapping is
            //kept per technology and the gradient is divided by a layer count the next file need not share.
            //
            bool wantsColor = everyColumn || layer.ColorIsCustom;
            bool wantsStack = everyColumn || layer.StackIsCustom;

            builder
                .Append(key.Number.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(key.DataType.ToString(CultureInfo.InvariantCulture))
                .Append(',')
                .Append(name);

            if (wantsColor || wantsStack || wantsRole)
            {
                builder.Append(',');

                if (wantsColor)
                    builder.Append(layer.Color);
            }

            if (wantsStack || wantsRole)
            {
                builder.Append(',');

                //
                //**Where the layer rests, not where it is drawn.** These differ by the spread the spacing
                //slider is asking for, and writing the drawn position put that spread into the height column
                //as though it had been measured - so reopening applied it, spread it again, and the stack
                //walked further apart on every open. See Layer.Resting.
                //
                //CustomHeight first so this is right even before a restack has run: ApplyTo writes a height
                //without recomputing the stack, and Resting only catches up when SetStackingOffsets does.
                //
                if (wantsStack)
                    builder.Append((layer.CustomHeight ?? layer.Resting).ToString(CultureInfo.InvariantCulture));

                builder.Append(',');

                if (wantsStack)
                    builder.Append(layer.Depth.ToString(CultureInfo.InvariantCulture));
            }

            //A trailing "none" on every row of an export is a column of noise, so the tail is written only
            //as far as something has said anything.
            if (wantsRole)
                builder.Append(',').Append(layer.Role.ToString().ToLowerInvariant());

            if (wantsFill)
                builder.Append(',').Append(layer.Fill.ToString().ToLowerInvariant());

            if (wantsMarks)
                builder.Append(',').Append(layer.PatternColor ?? "");

            if (wantsSize)
                builder.Append(',').Append(layer.PatternPixels!.Value.ToString(CultureInfo.InvariantCulture));

            builder.Append('\n');
        }

        ///<summary>Names the columns for a spreadsheet, and is skipped on the way back in.</summary>
        public const string HeaderRow = "#layer,datatype,name,color,height,thickness,role,fill,patterncolor,patternsize\n";

        #endregion **************************************************************************
    }
}
