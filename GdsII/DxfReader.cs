using System.Globalization;
using System.Text;

using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///
    ///Reading DXF, as a GDSII library.
    ///
    ///**Why bother.** A mask is not always drawn in a layout tool. MEMS, photonics, packaging, test
    ///structures and anything with a mechanical drawing behind it start life in a CAD package, and DXF is
    ///what comes out of one. Getting that into a layout viewer at all normally means a round trip through
    ///something else.
    ///
    ///**Both flavors, and the entities that mean something on a mask.** DXF is a group code and a value
    ///repeated - as two lines of text, or as bytes when a tool wrote the binary form. Neither is hard to
    ///read, and they part company only in <see cref="DxfBinary"/>: both produce the same list of pairs and
    ///everything below is shared. What takes the thought is the mapping, and it is a mapping between two
    ///formats that disagree about nearly everything:
    ///
    ///- **DXF is floating point and GDSII is integers.** Every coordinate is scaled and rounded exactly once,
    ///  here, by <see cref="MicronsPerUnit"/> off the drawing's own `$INSUNITS`. A drawing that says nothing
    ///  is taken as microns, which is stated rather than guessed at silently.
    ///- **DXF layers are names and GDSII layers are numbers.** A name that *is* a number is taken as one -
    ///  `68`, `68/20`, `L68D20` - because a drawing meant for a mask shop names its layers after the numbers
    ///  the mask shop uses, and that is an instruction rather than a label. Anything else is numbered in the
    ///  order the LAYER table declares it, so two runs of a file agree. Either way the name is carried onto
    ///  the layer so nothing is lost - see <see cref="NumberFromName"/> and <see cref="Layer.Name"/>.
    ///- **DXF has curves and GDSII does not.** Every one becomes a run of straight edges, flattened until
    ///  the gap between the edge and the curve is under <see cref="CurveToleranceDatabaseUnits"/> - see
    ///  <see cref="DxfCurves"/>, which is where all of that lives.
    ///- **A closed shape is a boundary and an open one is a path.** That is the only reading that keeps both:
    ///  an open run has no area, and calling it a polygon would fill in a shape nobody drew.
    ///
    ///**A hatch is its area.** The pattern is dropped - a run of parallel lines standing in for concrete has
    ///no reading on a photomask - and the islands inside one are subtracted rather than filled, so a washer
    ///comes out as a washer.
    ///
    ///**What is skipped**, and it is worth saying rather than discovering: DIMENSION, MTEXT, LEADER, the
    ///3D entities, and everything else that is a *drawing* about a shape rather than a shape. SPLINE,
    ///ELLIPSE and HATCH used to be on that list on the reading that they were drawing constructs too, which
    ///was wrong: a spline is a fillet, an ellipse is a pad, and a hatch is a filled region. What was
    ///actually missing was a defensible answer to how finely to flatten one, which
    ///<see cref="CurveToleranceDatabaseUnits"/> is.
    ///
    public static class DxfReader
    {
        #region Constants *******************************************************************

        ///
        ///How far a straight edge standing in for a curve may sit off it, in database units.
        ///
        ///**One database unit, because there is nothing below it.** Every coordinate is rounded to a whole
        ///database unit on the way in, so two points closer together than one are the same point and a
        ///tolerance finer than one buys nothing at all - it only asks for segments the rounding then throws
        ///away. It is the finest the file can express and so the one figure that needs no argument beyond
        ///that.
        ///
        ///It replaced a flat sixty-four sides per circle, which is a different quantity pretending to be
        ///this one: sixty-four is a tenth of a nanometer out on a one-micron circle and a hundred and twenty
        ///nanometers out on a millimeter one, and only the second of those is a shape anybody would notice.
        ///
        public const double CurveToleranceDatabaseUnits = 1;

        ///<summary>The GDSII version written into what comes out - release 6, as the OASIS reader writes.</summary>
        private const short GdsVersion = 600;

        ///<summary>
        ///The library name given to the converted file. DXF has no equivalent, so something is invented, and
        ///this is what the OASIS reader invents too.
        ///</summary>
        private const string LibraryName = "LIB";

        ///<summary>
        ///The cell the ENTITIES section becomes. DXF's top-level drawing is unnamed where a GDSII library is
        ///all named cells, so it is given one.
        ///</summary>
        public const string TopCell = "DRAWING";

        ///<summary>
        ///One database unit, in microns. A nanometer, which is what nearly every real GDSII file uses and
        ///what makes a coordinate typed in microns land on a whole number.
        ///</summary>
        public const double MicronsPerDatabaseUnit = 0.001;

        #endregion **************************************************************************



        #region Opening *********************************************************************

        ///
        ///Whether this looks like a DXF, read off the front of the file rather than off its name.
        ///
        ///A DXF opens with a group code of 0 and a value of SECTION, or with a 999 comment before it. Both
        ///are checked because the comment is what most exporters write - AutoCAD's own does not, which is
        ///why looking only for one of them is a test that passes on half of them.
        ///
        public static bool LooksLikeDxf(ReadOnlySpan<byte> start)
        {
            //Enough for a comment line and the SECTION after it. Compared as text, since the format is.
            int length = Math.Min(start.Length, 256);

            if (length == 0)
                return false;

            string head = Encoding.ASCII.GetString(start.Slice(0, length));

            string[] lines = head.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            //**Read as pairs, not as lines.** A comment's text is the line after its code and may say
            //anything at all, including a bare "0" - so skipping the code and then judging the next line on
            //its own is a test that a comment can defeat.
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                string code = lines[i].Trim();

                //A comment, which is what most exporters write first and AutoCAD's own does not.
                if (code == "999")
                    continue;

                //The first thing a DXF proper says is a group code of 0 naming a section.
                return code == "0" && lines[i + 1].Trim() == "SECTION";
            }

            return false;
        }

        ///<summary>Either flavor, which is what the app asks before deciding what a file is.</summary>
        public static bool LooksLikeAnyDxf(ReadOnlySpan<byte> start)
        {
            return LooksLikeDxf(start) || DxfBinary.LooksLikeBinaryDxf(start);
        }

        ///
        ///A drawing, whichever way round it is written.
        ///
        ///**The two flavors part company here and nowhere else.** Both are group codes and values; only how
        ///those are spelled differs, so both produce the same list of pairs and everything downstream - the
        ///sections, the entities, the mapping to GDSII - is shared. A second reader for the binary form
        ///would be a second copy of all of that, kept in step by hand.
        ///
        public static GDS Read(byte[] bytes)
        {
            if (DxfBinary.LooksLikeBinaryDxf(bytes))
                return FromPairs(DxfBinary.Pairs(bytes));

            return Read(Encoding.UTF8.GetString(bytes));
        }

        public static GDS Read(Stream stream)
        {
            using var buffer = new MemoryStream();

            stream.CopyTo(buffer);

            return Read(buffer.ToArray());
        }

        ///<summary>
        ///The same, for a stream that can only be read asynchronously - which the browser's is: a Blazor
        ///WASM file stream throws on a synchronous read outright.
        ///</summary>
        public static async Task<GDS> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();

            await stream.CopyToAsync(buffer, cancellationToken);

            return Read(buffer.ToArray());
        }

        public static GDS Read(string text)
        {
            return FromPairs(Pairs(text));
        }

        ///<summary>The library, from the pairs either flavor produced.</summary>
        private static GDS FromPairs(List<Pair> pairs)
        {
            var drawing = new Drawing(pairs);

            var gds = GDS.FromRecords(drawing.ToRecords());

            //The layer names, carried onto the layers the flattener built. A DXF names its layers and GDSII
            //numbers them, so without this the one piece of the original nobody could get back is the piece
            //that says what anything is.
            foreach (var named in drawing.LayerNames)
            {
                if (gds.AdditionalInformation.Layers.TryGetValue(named.Key, out var layer))
                    layer.Name = named.Value;
            }

            return gds;
        }

        #endregion **************************************************************************



        #region The pairs *******************************************************************

        ///<summary>One group code and its value, which is the whole of the file's structure.</summary>
        public readonly record struct Pair(int Code, string Value);

        ///
        ///The file as pairs, with anything malformed dropped.
        ///
        ///**Read as far as it can be, rather than all or nothing.** A DXF is written by dozens of tools and
        ///the tail of one is often a proprietary section this has no interest in; refusing the file over a
        ///line in it would cost the geometry, which is the part somebody wanted.
        ///
        public static List<Pair> Pairs(string text)
        {
            var pairs = new List<Pair>();

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                if (!int.TryParse(lines[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                    continue;

                //The value is taken as it stands apart from the line ending: a text entity's string may have
                //leading spaces that are part of what it says.
                pairs.Add(new Pair(code, lines[i + 1].TrimEnd()));
            }

            return pairs;
        }

        ///<summary>
        ///A coordinate or any other real. Invariant, because these are numbers in a data file rather than
        ///prose - the same reasoning as the record dump and the layermap.
        ///</summary>
        public static double Number(string value)
        {
            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double read))
                return read;

            return 0;
        }

        private static int Whole(string value)
        {
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int read))
                return read;

            return 0;
        }

        ///
        ///How many microns one drawing unit is, from the header's `$INSUNITS`.
        ///
        ///**A drawing that says nothing is taken as microns.** Something has to be assumed - a number with no
        ///unit is not a length - and microns is the assumption that makes a layout-sized drawing come out
        ///layout-sized. It is also the one this says out loud rather than burying, because a file read at the
        ///wrong scale opens looking perfectly fine and is a thousand times too big.
        ///
        public static double MicronsPerUnit(int insUnits)
        {
            //The format's own table. Only the lengths are here; the ones it leaves out are not units.
            switch (insUnits)
            {
                case 1: return 25400;      //inches
                case 2: return 304800;     //feet
                case 3: return 1609344000; //miles
                case 4: return 1000;       //millimeters
                case 5: return 10000;      //centimeters
                case 6: return 1000000;    //meters
                case 7: return 1000000000; //kilometers
                case 8: return 0.0254;     //microinches
                case 9: return 25.4;       //mils
                case 10: return 914400;    //yards
                case 11: return 0.0001;    //angstroms
                case 12: return 0.001;     //nanometers
                case 13: return 1;         //microns
                case 14: return 100000;    //decimeters
                default: return 1;         //unitless, and everything this does not know
            }
        }

        ///
        ///The layer and datatype a DXF layer's *name* asks for, or null if it is not asking for one.
        ///
        ///**A drawing meant for a mask shop names its layers after the numbers the mask shop uses.** That is
        ///the whole reason this exists. Somebody drawing a MEMS device for a shuttle run is told to put the
        ///structural layer on 68/20, and what they do is call the AutoCAD layer `68/20` - so numbering it by
        ///the order it happened to be declared in threw away the one instruction in the file.
        ///
        ///The spellings, and only these:
        ///
        ///- `68` - the layer, datatype zero.
        ///- `68/20`, `68.20`, `68-20`, `68:20` - the pair, as a layermap writes it.
        ///- `L68D20` - KLayout's own, and case does not matter.
        ///- `METAL1 (68/20)` - a name with the pair after it in brackets, which is what a person writes when
        ///  they want the layer list to still be readable.
        ///
        ///**The whole name has to be one of those**, which is the part worth being strict about. Picking a
        ///number out of a name that merely contains one reads `METAL1` as layer 1, and `POLY_2024_01` as
        ///layer 2024 datatype 1 - guesses that are right often enough to be trusted and wrong on the file
        ///that mattered. Anything not on the list is a name and gets an index instead.
        ///
        ///A negative number is not a GDSII layer and is not treated as one; nor is a number past what a
        ///short holds, which is the case that would otherwise wrap into a negative silently.
        ///
        public static LayerKey? NumberFromName(string name)
        {
            string trimmed = name.Trim();

            if (trimmed.Length == 0)
                return null;

            //The bracketed annotation, taken off first so the rest of the reading is the same either way.
            var annotated = System.Text.RegularExpressions.Regex.Match(trimmed, @"\(\s*([^()]*?)\s*\)$");

            if (annotated.Success)
                trimmed = annotated.Groups[1].Value;

            //L68D20, where the letters are part of the spelling rather than a name wrapped round it.
            var klayout = System.Text.RegularExpressions.Regex.Match(trimmed, @"^[lL](\d{1,5})[dD](\d{1,5})$");

            if (klayout.Success)
                return Made(klayout.Groups[1].Value, klayout.Groups[2].Value);

            var pair = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(\d{1,5})\s*[/.\-:]\s*(\d{1,5})$");

            if (pair.Success)
                return Made(pair.Groups[1].Value, pair.Groups[2].Value);

            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\d{1,5}$"))
                return Made(trimmed, "0");

            return null;
        }

        ///
        ///A DXF insert's two scale factors, as the reflection and the extra turn GDSII would need.
        ///
        ///**DXF mirrors with a negative scale; GDSII mirrors with a flag.** A GDSII placement is reflected
        ///about the X axis *and then* rotated, and its magnification is a positive number - so there are
        ///only four cases, and each one has an exact answer rather than an approximation:
        ///
        ///- both positive: nothing to do.
        ///- Y negative: reflection about the X axis, which is what the flag means. The angle is unchanged.
        ///- X negative: reflection about the Y axis, which GDSII has no flag for - but reflecting about X
        ///  and then turning half a turn is the same transform, so that is what it becomes.
        ///- both negative: not a reflection at all. Turning something half a turn flips it twice, and twice
        ///  is not at all.
        ///
        ///Worth writing out because the last one is the case a reader gets wrong: two minus signs look like
        ///more mirroring than one, and they are less.
        ///
        public static (bool Mirrored, double ExtraDegrees) MirrorOf(double across, double down)
        {
            if (across < 0 && down < 0)
                return (false, 180);

            if (across < 0)
                return (true, 180);

            if (down < 0)
                return (true, 0);

            return (false, 0);
        }

        ///<summary>A pair of digit runs as a key, or null if either is past what a GDSII layer holds.</summary>
        private static LayerKey? Made(string layer, string dataType)
        {
            if (!int.TryParse(layer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                return null;

            if (!int.TryParse(dataType, NumberStyles.Integer, CultureInfo.InvariantCulture, out int type))
                return null;

            if (number < 0 || number > short.MaxValue || type < 0 || type > short.MaxValue)
                return null;

            return new LayerKey((short)number, (short)type);
        }

        #endregion **************************************************************************



        #region The drawing *****************************************************************

        ///
        ///One DXF, walked into the pieces a GDSII library is made of.
        ///
        ///A single pass, because the format is a single pass: sections in order, and within one, entities
        ///that each run from a group code of 0 to the next. The only thing that reaches backwards is a
        ///POLYLINE, whose points arrive as VERTEX entities after it - so those are folded into it as they
        ///come rather than being left as entities of their own.
        ///
        internal sealed class Drawing
        {
            ///<summary>What each layer was called in the drawing, by the number it was given.</summary>
            public Dictionary<LayerKey, string> LayerNames { get; } = new Dictionary<LayerKey, string>();

            private readonly Dictionary<string, LayerKey> numbers = new Dictionary<string, LayerKey>(StringComparer.OrdinalIgnoreCase);

            ///<summary>Every layer number already spoken for, so the index cannot hand out one a name took.</summary>
            private readonly HashSet<short> taken = new HashSet<short>();

            ///<summary>Where the index has got to. Only ever moves forwards, so two runs of a file agree.</summary>
            private int nextFree = 0;

            ///<summary>Blocks in the order they were declared, so two runs of a file agree.</summary>
            private readonly List<(string Name, List<Entity> Entities)> blocks = new List<(string, List<Entity>)>();

            ///<summary>Each block's base point, by its place in that list. Nearly always the origin.</summary>
            private readonly Dictionary<int, (double X, double Y)> bases = new Dictionary<int, (double, double)>();

            ///<summary>What the cell being written is shifted by, which is the base point of the block it is.</summary>
            private (double X, double Y) shift = (0, 0);

            private readonly List<Entity> top = new List<Entity>();

            private double microns = 1;

            ///
            ///The curve tolerance in the drawing's own units, which is what the flattener works in.
            ///
            ///Stated once in database units and converted here, so a drawing measured in millimeters and one
            ///measured in microns are flattened to the same accuracy on the mask rather than to the same
            ///number of drawing units - which would be a thousand times coarser on one of them.
            ///
            private double Tolerance
            {
                get { return CurveToleranceDatabaseUnits * MicronsPerDatabaseUnit / microns; }
            }

            public Drawing(List<Pair> pairs)
            {
                string section = "";
                string table = "";

                List<Entity>? into = null;
                string blockName = "";
                Entity? entity = null;

                //A $INSUNITS is a 9 naming it followed by a 70 carrying the number, so the name has to be
                //remembered until the value arrives.
                string variable = "";

                for (int i = 0; i < pairs.Count; i++)
                {
                    var pair = pairs[i];

                    if (pair.Code == 0)
                    {
                        //A finished entity goes in wherever entities are going at the moment.
                        entity = close(entity, into);

                        if (pair.Value == "SECTION")
                        {
                            section = nextValue(pairs, i, 2);
                            table = "";
                            into = null;

                            if (section == "ENTITIES")
                                into = top;

                            continue;
                        }

                        if (pair.Value == "ENDSEC" || pair.Value == "EOF")
                        {
                            section = "";
                            into = null;

                            continue;
                        }

                        if (section == "TABLES")
                        {
                            if (pair.Value == "TABLE")
                                table = nextValue(pairs, i, 2);

                            //A row of the LAYER table, which is what fixes the numbering.
                            if (pair.Value == "LAYER" && table == "LAYER")
                                keyFor(nextValue(pairs, i, 2));

                            continue;
                        }

                        if (section == "BLOCKS")
                        {
                            if (pair.Value == "BLOCK")
                            {
                                blockName = nextValue(pairs, i, 2);
                                into = new List<Entity>();

                                //
                                //**The base point, which is where an insert puts the block's origin.**
                                //
                                //A GDSII placement puts the cell's own origin at the point it names, so a
                                //block drawn around (100, 100) with a base point there has to have that
                                //taken off its geometry or every instance of it lands a hundred units out.
                                //Most writers use (0, 0) and the difference never shows, which is exactly
                                //what makes the one that does not a bad afternoon.
                                //
                                blocks.Add((blockName, into));

                                bases[blocks.Count - 1] = (
                                    Number(nextValue(pairs, i, 10)),
                                    Number(nextValue(pairs, i, 20)));

                                continue;
                            }

                            if (pair.Value == "ENDBLK")
                            {
                                into = null;

                                continue;
                            }
                        }

                        //A VERTEX belongs to the POLYLINE before it rather than to the drawing.
                        if (pair.Value == "VERTEX" && into is not null && into.Count > 0 && into[^1].Type == "POLYLINE")
                        {
                            entity = new Entity("VERTEX");

                            continue;
                        }

                        if (pair.Value == "SEQEND")
                            continue;

                        if (into is not null)
                            entity = new Entity(pair.Value);

                        continue;
                    }

                    if (section == "HEADER")
                    {
                        if (pair.Code == 9)
                            variable = pair.Value.Trim();
                        else if (pair.Code == 70 && variable == "$INSUNITS")
                            microns = MicronsPerUnit(Whole(pair.Value));

                        continue;
                    }

                    entity?.Body.Add(pair);
                }

                close(entity, into);
            }

            ///<summary>Puts a finished entity where it belongs, folding a VERTEX into the run it is part of.</summary>
            private static Entity? close(Entity? entity, List<Entity>? into)
            {
                if (entity is null || into is null)
                    return null;

                if (entity.Type == "VERTEX" && into.Count > 0 && into[^1].Type == "POLYLINE")
                    into[^1].Vertices.Add(entity);
                else
                    into.Add(entity);

                return null;
            }

            ///<summary>The value of the next pair with that code, before the entity after this one starts.</summary>
            private static string nextValue(List<Pair> pairs, int from, int code)
            {
                for (int i = from + 1; i < pairs.Count; i++)
                {
                    if (pairs[i].Code == 0)
                        return "";

                    if (pairs[i].Code == code)
                        return pairs[i].Value.Trim();
                }

                return "";
            }

            ///
            ///The GDSII layer and datatype a DXF layer name becomes.
            ///
            ///**The name first, and an index only when the name is not one.** A drawing meant for a mask
            ///shop names its layers after the numbers the mask shop uses - `68`, `68/20`, `M1_68_20` - and
            ///numbering those in the order they happen to be declared threw away the one piece of
            ///information the file was carrying. A layer called 7 is layer 7. See <see cref="LayerKey.Parse"/>
            ///for the spellings.
            ///
            ///**And an index when it is not**, in the order the LAYER table declares them and then in the
            ///order entities first mention any the table left out - so a file read twice gives the same
            ///numbers, which a hash of the name would not.
            ///
            ///The two cannot collide, because the index skips every number a name has already claimed.
            ///That is the whole reason the table is walked before the entities: a drawing with a layer
            ///called `1` and a layer called `POLY` gives the first to the name and hands the other the next
            ///number that is free.
            ///
            private LayerKey keyFor(string name)
            {
                if (name.Length == 0)
                    name = "0";

                if (numbers.TryGetValue(name, out LayerKey already))
                    return already;

                LayerKey given;

                if (NumberFromName(name) is LayerKey named)
                {
                    given = named;
                }
                else
                {
                    //Past whatever the names have taken. GDSII layer numbers are shorts, and a drawing with
                    //more layers than that is not a thing that exists - but wrapping into a negative one
                    //silently is not the way to find out.
                    while (nextFree < short.MaxValue && taken.Contains((short)nextFree))
                        nextFree++;

                    given = new LayerKey((short)nextFree, 0);

                    if (nextFree < short.MaxValue)
                        nextFree++;
                }

                numbers[name] = given;
                taken.Add(given.Number);

                //First name wins. Two DXF layers can land on one GDSII layer - "68" and "68/0" are the same
                //place - and overwriting would mean the sidebar named a layer after whichever of them the
                //file happened to mention second.
                if (!LayerNames.ContainsKey(given))
                    LayerNames[given] = name;

                return given;
            }

            ///<summary>The layer alone, for the places that only need the number.</summary>
            private short numberFor(string name)
            {
                return keyFor(name).Number;
            }

            ///<summary>One drawing unit as a whole number of database units, which is the one rounding here.</summary>
            private int Units(double value)
            {
                double scaled = value * microns / MicronsPerDatabaseUnit;

                if (scaled > int.MaxValue)
                    return int.MaxValue;

                if (scaled < int.MinValue)
                    return int.MinValue;

                return (int)Math.Round(scaled);
            }

            #region Into records ************************************************

            public List<Record> ToRecords()
            {
                var records = new List<Record>
                {
                    Hierarchy.Make(RecordType.HEADER, new Int2Data(GdsVersion)),
                    Hierarchy.Make(RecordType.BGNLIB, new Int2Data(new short[12])),
                    Hierarchy.Make(RecordType.LIBNAME, new AsciiData(LibraryName)),
                    Hierarchy.Make(RecordType.UNITS, new Real8Data(new double[] { MicronsPerDatabaseUnit, MicronsPerDatabaseUnit / 1e6 }))
                };

                //Blocks first, so every cell a placement names is already there when it is read back - which
                //is what a GDSII reader expects and what makes the file open in anything else.
                for (int i = 0; i < blocks.Count; i++)
                {
                    //
                    //The block's geometry is written about its base point rather than about wherever it
                    //happens to be drawn.
                    //
                    //An INSERT puts the base point at the point it names; a GDSII placement puts the cell's
                    //*origin* there. Taking the base point off the geometry is what makes those the same
                    //thing, and it is the whole of the difference between the two formats here.
                    //
                    if (bases.TryGetValue(i, out var origin))
                        shift = origin;
                    else
                        shift = (0, 0);

                    appendStructure(records, blocks[i].Name, blocks[i].Entities);
                }

                //The drawing itself has no base point, so nothing is taken off it.
                shift = (0, 0);

                appendStructure(records, TopCell, top);

                records.Add(Hierarchy.Make(RecordType.ENDLIB, null));

                return records;
            }

            private void appendStructure(List<Record> records, string name, List<Entity> entities)
            {
                records.Add(Hierarchy.Make(RecordType.BGNSTR, new Int2Data(new short[12])));
                records.Add(Hierarchy.Make(RecordType.STRNAME, new AsciiData(AddElement.AsAscii(name))));

                foreach (var entity in entities)
                    append(records, entity);

                records.Add(Hierarchy.Make(RecordType.ENDSTR, null));
            }

            private void append(List<Record> records, Entity entity)
            {
                if (entity.Type == "LWPOLYLINE")
                    appendPolyline(records, entity, DxfCurves.Bulged(entity.Points(10, 20), entity.Bulges(), entity.Closed, Tolerance), entity.Closed, entity.Real(43));
                else if (entity.Type == "POLYLINE")
                    appendPolyline(records, entity, DxfCurves.Bulged(entity.VertexPoints(), entity.VertexBulges(), entity.Closed, Tolerance), entity.Closed, entity.RunWidth());
                else if (entity.Type == "LINE")
                    appendPolyline(records, entity, entity.Ends(), closed: false);
                else if (entity.Type == "CIRCLE")
                    appendPolyline(records, entity, entity.Curve(0, 360, Tolerance), closed: true);
                else if (entity.Type == "ARC")
                    appendPolyline(records, entity, entity.Curve(entity.Real(50), entity.Real(51), Tolerance), closed: false);
                else if (entity.Type == "ELLIPSE")
                    appendPolyline(records, entity, entity.EllipseRun(Tolerance), entity.EllipseIsWhole);
                else if (entity.Type == "SPLINE")
                    appendPolyline(records, entity, entity.SplineRun(Tolerance), entity.SplineIsClosed);
                else if (entity.Type == "HATCH")
                    appendHatch(records, entity);
                else if (entity.Type == "SOLID" || entity.Type == "TRACE")
                    appendPolyline(records, entity, entity.Corners(), closed: true);
                else if (entity.Type == "TEXT")
                    appendText(records, entity);
                else if (entity.Type == "INSERT" || entity.Type == "MINSERT")
                    appendInsert(records, entity);
            }

            ///
            ///A hatch, as the region it fills.
            ///
            ///**What a mask wants from a hatch is the area, not the pattern.** A hatch is a boundary and a
            ///fill style, and every fill style that means anything here is solid - a run of parallel lines
            ///standing in for concrete has no reading on a photomask. So the boundary is what comes across,
            ///filled, and the pattern name is dropped.
            ///
            ///**Islands are cut out rather than filled in.** A hatch describes a hole as another boundary
            ///path inside the first, flagged not-outermost - and emitting each path as its own boundary
            ///turns a washer into a disc. They are subtracted, through the same Clipper the editor's own
            ///booleans use, which also means the result arrives hole-free the way GDSII needs.
            ///
            private void appendHatch(List<Record> records, Entity entity)
            {
                var paths = entity.HatchPaths(Tolerance);

                if (paths.Count == 0)
                    return;

                var outer = new List<IReadOnlyList<Element.Point>>();
                var inner = new List<IReadOnlyList<Element.Point>>();

                foreach (var path in paths)
                {
                    var outline = Rounded(entity, path.Points, closed: true);

                    if (outline.Count < 3)
                        continue;

                    if (path.Outermost)
                        outer.Add(outline);
                    else
                        inner.Add(outline);
                }

                //Nothing said which was which - some writers flag none of them - so they are all outer and
                //the merge below sorts out what overlaps.
                if (outer.Count == 0)
                {
                    outer.AddRange(inner);
                    inner.Clear();
                }

                List<List<Element.Point>> filled;

                if (inner.Count > 0)
                    filled = Booleans.Combine(outer, inner, BooleanOperation.Not);
                else
                    filled = Booleans.Merge(outer);

                LayerKey key = keyFor(entity.Layer);

                foreach (var shape in filled)
                {
                    if (shape.Count < 3)
                        continue;

                    var closed = new List<Element.Point>(shape);

                    if (closed[0].X != closed[^1].X || closed[0].Y != closed[^1].Y)
                        closed.Add(closed[0]);

                    records.Add(Hierarchy.Make(RecordType.BOUNDARY, null));
                    records.Add(Hierarchy.Make(RecordType.LAYER, new Int2Data(key.Number)));
                    records.Add(Hierarchy.Make(RecordType.DATATYPE, new Int2Data(key.DataType)));
                    records.Add(Hierarchy.Make(RecordType.XY, new Int4Data(Flat(closed))));
                    records.Add(Hierarchy.Make(RecordType.ENDEL, null));
                }
            }

            ///
            ///An entity's own coordinates, as the library's.
            ///
            ///Three things happen here and each of them is a place a reader can be quietly wrong: the
            ///entity's plane is turned into the drawing's, the block's base point is taken off, and the
            ///whole thing is scaled and rounded to database units. In that order, because each is expressed
            ///in the space the one before it produced.
            ///
            private Element.Point At(Entity entity, double x, double y)
            {
                (double worldX, double worldY) = entity.ToWorld(x, y);

                return new Element.Point(Units(worldX - shift.X), Units(worldY - shift.Y));
            }

            ///<summary>The same for a run, with the repeats a closing vertex leaves dropped.</summary>
            private List<Element.Point> Rounded(Entity entity, List<(double X, double Y)> points, bool closed)
            {
                var outline = new List<Element.Point>();

                foreach ((double x, double y) in points)
                {
                    var made = At(entity, x, y);

                    //A point on top of the one before it is a zero-length edge, which no reader wants and
                    //which an exporter that repeats its closing vertex produces on every shape.
                    if (outline.Count > 0 && outline[^1].X == made.X && outline[^1].Y == made.Y)
                        continue;

                    outline.Add(made);
                }

                //A ring that comes back to its start says so twice, which is a zero-length edge to Clipper.
                if (closed && outline.Count > 1 && outline[0].X == outline[^1].X && outline[0].Y == outline[^1].Y)
                    outline.RemoveAt(outline.Count - 1);

                return outline;
            }

            ///
            ///A run of points, as a boundary when it closes and a path when it does not.
            ///
            ///**The only reading that keeps both.** An open run has no area, so calling it a polygon fills in
            ///a shape nobody drew; a closed one called a path would be an outline of its own edge. A path
            ///takes the drawing's own constant width where it has one, and nothing where it does not - which
            ///is a hairline, and is what a line in a CAD drawing is.
            ///
            private void appendPolyline(List<Record> records, Entity entity, List<(double X, double Y)> points, bool closed, double width = 0)
            {
                var outline = Rounded(entity, points, closed: false);

                LayerKey key = keyFor(entity.Layer);

                if (closed)
                {
                    if (outline.Count < 3)
                        return;

                    if (outline[0].X != outline[^1].X || outline[0].Y != outline[^1].Y)
                        outline.Add(outline[0]);

                    records.Add(Hierarchy.Make(RecordType.BOUNDARY, null));
                    records.Add(Hierarchy.Make(RecordType.LAYER, new Int2Data(key.Number)));
                    records.Add(Hierarchy.Make(RecordType.DATATYPE, new Int2Data(key.DataType)));
                    records.Add(Hierarchy.Make(RecordType.XY, new Int4Data(Flat(outline))));
                    records.Add(Hierarchy.Make(RecordType.ENDEL, null));

                    return;
                }

                if (outline.Count < 2)
                    return;

                var path = Paths.Records(key, outline, Math.Max(0, Units(width)), Paths.Ends.Flush);

                if (path is not null)
                    records.AddRange(path);
            }

            private void appendText(List<Record> records, Entity entity)
            {
                string says = AddElement.AsAscii(entity.Text(1));

                if (says.Length == 0)
                    return;

                var centered = new TextPresentation(HorizontalPresentation.Left, VerticalPresentation.Bottom, 0);

                records.Add(Hierarchy.Make(RecordType.TEXT, null));
                LayerKey key = keyFor(entity.Layer);

                records.Add(Hierarchy.Make(RecordType.LAYER, new Int2Data(key.Number)));
                records.Add(Hierarchy.Make(RecordType.TEXTTYPE, new Int2Data(key.DataType)));
                records.Add(Hierarchy.Make(RecordType.PRESENTATION, new BitArrayData(centered.Encode())));
                var placed = At(entity, entity.Real(10), entity.Real(20));

                records.Add(Hierarchy.Make(RecordType.XY, new Int4Data(new int[] { placed.X, placed.Y })));
                records.Add(Hierarchy.Make(RecordType.STRING, new AsciiData(says)));
                records.Add(Hierarchy.Make(RecordType.ENDEL, null));
            }

            ///
            ///A block reference, as a placement of the cell it names.
            ///
            ///**A non-uniform scale is not representable and the X one is taken.** GDSII magnifies a
            ///placement by one number where DXF has two, so a block inserted at 2× across and 1× up has no
            ///GDSII spelling - and of the three ways out (refuse it, flatten it, or take one), taking one
            ///keeps the hierarchy and puts the shape somewhere a reader can see is wrong.
            ///
            ///A repeated insert - columns and rows, DXF's MINSERT - becomes an AREF, which is one record
            ///rather than one placement per position.
            ///
            ///**A negative scale is a mirror**, which is how DXF spells one - there is no reflection flag.
            ///GDSII has the flag and no negative magnification, so the two have to be translated rather
            ///than copied across, and getting it wrong is silent: a mirrored block placed unmirrored is a
            ///cell in the right place facing the wrong way. See <see cref="MirrorOf"/>.
            ///
            private void appendInsert(List<Record> records, Entity entity)
            {
                string names = entity.Text(2);

                if (names.Length == 0)
                    return;

                double across = entity.Real(41);
                double down = entity.Real(42);

                if (across == 0)
                    across = 1;

                //A DXF that says nothing about the Y scale means the same as the X one.
                if (down == 0)
                    down = across;

                (bool mirrored, double turn) = MirrorOf(across, down);

                double angle = entity.Real(50) + turn;
                double scale = Math.Abs(across);

                var at = At(entity, entity.Real(10), entity.Real(20));

                var placement = Hierarchy.PlacementRecords(AddElement.AsAscii(names), at, mirrored, angle);

                //The magnification, which PlacementRecords does not take: it is written for the editor, where
                //nothing is ever placed scaled.
                if (scale != 1)
                    placement = Hierarchy.WithTransform(placement, at, mirrored, angle, scale) ?? placement;

                int columns = Math.Max(1, entity.Whole(70));
                int rows = Math.Max(1, entity.Whole(71));

                if (columns > 1 || rows > 1)
                {
                    int columnPitch = Units(entity.Real(44));
                    int rowPitch = Units(entity.Real(45));

                    var array = Hierarchy.AsArray(placement, columns, rows, columnPitch, 0, 0, rowPitch);

                    if (array is not null)
                    {
                        records.AddRange(array);

                        return;
                    }
                }

                records.AddRange(placement);
            }

            private static int[] Flat(List<Element.Point> points)
            {
                var flat = new int[points.Count * 2];

                for (int i = 0; i < points.Count; i++)
                {
                    flat[i * 2] = points[i].X;
                    flat[(i * 2) + 1] = points[i].Y;
                }

                return flat;
            }

            #endregion **********************************************************
        }

        ///<summary>One entity: what kind it is, the pairs that describe it, and a POLYLINE's own vertices.</summary>
        internal sealed class Entity
        {
            public Entity(string type)
            {
                Type = type;
            }

            public string Type { get; }

            public List<Pair> Body { get; } = new List<Pair>();

            public List<Entity> Vertices { get; } = new List<Entity>();

            ///<summary>Which layer it is on. DXF's default layer is literally called zero.</summary>
            public string Layer
            {
                get { return Text(8); }
            }

            ///<summary>Bit one of the flags word, which is what says a run comes back to where it started.</summary>
            public bool Closed
            {
                get { return (Whole(70) & 1) != 0; }
            }

            ///
            ///A point of this entity's, in the drawing's own coordinates rather than in the entity's.
            ///
            ///Almost always the same point: an extrusion of (0, 0, 1) is what nearly every entity carries
            ///and is what changes nothing. The one that matters is (0, 0, -1) - something drawn on a face
            ///pointing the other way - which mirrors the entity's X, and which a reader that ignores the
            ///extrusion gets backwards without any sign of having done so.
            ///
            public (double X, double Y) ToWorld(double x, double y)
            {
                if (!Has(210) && !Has(220) && !Has(230))
                    return (x, y);

                //A missing component is zero, but a missing Z on an extrusion that says anything at all
                //would be a vector of no length - so the default is the one that changes nothing.
                double z = Real(230);

                if (!Has(230))
                    z = 1;

                return DxfCurves.ToWorld(Real(210), Real(220), z, x, y);
            }

            ///<summary>Whether the entity carries that code at all, which is not the same as it being zero.</summary>
            public bool Has(int code)
            {
                foreach (var pair in Body)
                {
                    if (pair.Code == code)
                        return true;
                }

                return false;
            }

            public string Text(int code)
            {
                foreach (var pair in Body)
                {
                    if (pair.Code == code)
                        return pair.Value.Trim();
                }

                return "";
            }

            public double Real(int code)
            {
                foreach (var pair in Body)
                {
                    if (pair.Code == code)
                        return Number(pair.Value);
                }

                return 0;
            }

            public int Whole(int code)
            {
                return (int)Math.Round(Real(code));
            }

            ///<summary>
            ///The coordinates, paired off as they arrive. An LWPOLYLINE writes a 10 and a 20 per point and
            ///nothing separating one from the next, so the pairing is positional and has to stay that way.
            ///</summary>
            public List<(double X, double Y)> Points(int xCode, int yCode)
            {
                var points = new List<(double, double)>();

                double? x = null;

                foreach (var pair in Body)
                {
                    if (pair.Code == xCode)
                    {
                        //Two x values running means a point with no y, which is a malformed entity - the
                        //first is dropped rather than paired with the wrong number.
                        x = Number(pair.Value);
                    }
                    else if (pair.Code == yCode && x is double across)
                    {
                        points.Add((across, Number(pair.Value)));
                        x = null;
                    }
                }

                return points;
            }

            ///<summary>Every value carrying that code, in the order they appear.</summary>
            public List<double> Reals(int code)
            {
                var values = new List<double>();

                foreach (var pair in Body)
                {
                    if (pair.Code == code)
                        values.Add(Number(pair.Value));
                }

                return values;
            }

            ///
            ///An LWPOLYLINE's bulges, lined up with its vertices.
            ///
            ///**Positionally, which is the part that needs care.** The bulge is optional per vertex and is
            ///written between that vertex's 20 and the next vertex's 10 - so a run where only the third
            ///vertex bows writes exactly one 42, and reading them as a list would put that bow on the first
            ///segment. This walks the body in order and holds a place for every vertex that did not have
            ///one.
            ///
            public List<double> Bulges()
            {
                var bulges = new List<double>();

                double pending = 0;
                bool started = false;

                foreach (var pair in Body)
                {
                    //A new vertex begins, so whatever was gathered belongs to the one before it.
                    if (pair.Code == 10)
                    {
                        if (started)
                            bulges.Add(pending);

                        pending = 0;
                        started = true;
                    }
                    else if (pair.Code == 42 && started)
                    {
                        pending = Number(pair.Value);
                    }
                }

                if (started)
                    bulges.Add(pending);

                return bulges;
            }

            ///<summary>A POLYLINE's points, which arrive as VERTEX entities of their own after it.</summary>
            public List<(double X, double Y)> VertexPoints()
            {
                var points = new List<(double, double)>();

                foreach (var vertex in Vertices)
                    points.Add((vertex.Real(10), vertex.Real(20)));

                return points;
            }

            ///
            ///An old-style POLYLINE's width, which is not where an LWPOLYLINE keeps its.
            ///
            ///Code 43 is the newer entity's constant width. The older one has a *default* start and end
            ///width - 40 and 41 - on the run itself, and every VERTEX may override them with its own pair.
            ///What is read here is the constant case, which is the only one GDSII has a spelling for: a
            ///path is one width along its whole length.
            ///
            ///Not read through <c>Real</c> against the whole entity, because a VERTEX's own 40 is inside
            ///this entity's body only after the run's - the first one found is the run's default, and that
            ///is the one that means the width of the whole thing.
            ///
            public double RunWidth()
            {
                double start = Real(40);

                if (start > 0)
                    return start;

                //Nothing on the run, so whatever the first vertex says - which is what a writer that puts
                //the width per vertex rather than on the run produces.
                foreach (var vertex in Vertices)
                {
                    double width = vertex.Real(40);

                    if (width > 0)
                        return width;
                }

                return 0;
            }

            ///<summary>The same run's bulges, one per vertex, since each VERTEX carries its own.</summary>
            public List<double> VertexBulges()
            {
                var bulges = new List<double>();

                foreach (var vertex in Vertices)
                    bulges.Add(vertex.Real(42));

                return bulges;
            }

            ///<summary>A line's two ends.</summary>
            public List<(double X, double Y)> Ends()
            {
                return new List<(double, double)> { (Real(10), Real(20)), (Real(11), Real(21)) };
            }

            ///
            ///A SOLID's corners, in the order that makes a ring rather than a bowtie.
            ///
            ///The format numbers them in a Z: the third and fourth are the far edge *backwards*, so taking
            ///them as written draws a shape crossing itself. The fourth repeats the third on a triangle,
            ///which the repeat check upstream drops.
            ///
            public List<(double X, double Y)> Corners()
            {
                return new List<(double, double)>
                {
                    (Real(10), Real(20)),
                    (Real(11), Real(21)),
                    (Real(13), Real(23)),
                    (Real(12), Real(22))
                };
            }

            #region Ellipses ****************************************************

            ///
            ///Whether the ellipse goes all the way round, which is what makes it an outline rather than a
            ///run. A whole one is written from parameter 0 to 2π; anything short of that is an arc of one.
            ///
            public bool EllipseIsWhole
            {
                get { return Math.Abs(Math.Abs(Real(42) - Real(41)) - (2 * Math.PI)) < 1e-6; }
            }

            ///
            ///An ellipse, as the run of points that stands in for it.
            ///
            ///The format gives it as a center, the vector from there to the end of the major axis, and how
            ///long the minor one is as a fraction of that - so an ellipse at an angle needs no angle: the
            ///major axis vector is already pointing where it points.
            ///
            ///The two parameters are in radians here, unlike an ARC's angles, which are in degrees. That is
            ///the format's own inconsistency and not one worth smoothing over silently.
            ///
            public List<(double X, double Y)> EllipseRun(double tolerance)
            {
                double from = Real(41);
                double sweep = Real(42) - from;

                //Nothing said, which is how a whole ellipse is usually written: 0 to 2π.
                if (sweep == 0)
                    sweep = 2 * Math.PI;

                return DxfCurves.Ellipse(
                    Real(10),
                    Real(20),
                    Real(11),
                    Real(21),
                    Real(40),
                    from,
                    sweep,
                    tolerance);
            }

            #endregion **********************************************************



            #region Splines *****************************************************

            ///<summary>Bit one of a spline's flags word, which is what says the curve comes back on itself.</summary>
            public bool SplineIsClosed
            {
                get { return (Whole(70) & 1) != 0; }
            }

            ///
            ///A spline, flattened.
            ///
            ///**Control points when there are any, fit points when there are not.** A spline is defined by
            ///its control points, its knots and its weights, and that is what gets evaluated. Some writers
            ///give only the points the curve was drawn *through* - reconstructing a curve from those is a
            ///choice of interpolation rather than a reading of the file, so those are joined up as the run
            ///they are, which is the shape somebody drew, drawn coarsely.
            ///
            ///Code 42 is the knot tolerance on this entity, not a bulge. Worth saying because 42 on a
            ///polyline vertex is a bulge, and a reader that shares one routine between them draws nonsense.
            ///
            public List<(double X, double Y)> SplineRun(double tolerance)
            {
                var controlPoints = Points(10, 20);

                if (controlPoints.Count == 0)
                    return Points(11, 21);

                return DxfCurves.Spline(
                    controlPoints,
                    Reals(40),
                    Reals(41),
                    Whole(71),
                    SplineIsClosed,
                    tolerance);
            }

            #endregion **********************************************************



            #region Hatches *****************************************************

            ///<summary>One boundary of a hatch, and whether it is the outside of the region or a hole in it.</summary>
            public sealed record HatchPath(List<(double X, double Y)> Points, bool Outermost);

            ///
            ///A hatch's boundary paths, walked in order.
            ///
            ///**In order, because a hatch cannot be read any other way.** Everywhere else here a value is
            ///found by its group code; in a HATCH the same code means different things depending on where in
            ///the entity it appears - 10 and 20 are the elevation point before the paths start, then a
            ///vertex or an edge end inside one, then a *seed point* after them all. Asking for "the 10" gets
            ///the elevation and draws the hatch at the origin.
            ///
            ///So this walks: 91 says how many paths follow, 92 opens each one and says what kind it is, and
            ///93 says how many vertices or edges are in it. Each path is closed by definition - a boundary
            ///that does not close bounds nothing.
            ///
            public List<HatchPath> HatchPaths(double tolerance)
            {
                var paths = new List<HatchPath>();

                int at = IndexOf(91, 0);

                if (at < 0)
                    return paths;

                int count = (int)Math.Round(Number(Body[at].Value));

                for (int path = 0; path < count; path++)
                {
                    at = IndexOf(92, at + 1);

                    if (at < 0)
                        break;

                    int flags = (int)Math.Round(Number(Body[at].Value));

                    //Bit 0 is the outside of the region and bit 4 is the outermost of a nest; anything with
                    //neither is an island, which is a hole rather than a shape.
                    bool outermost = (flags & 1) != 0 || (flags & 16) != 0;

                    List<(double X, double Y)> points;

                    if ((flags & 2) != 0)
                        points = polylinePath(ref at, tolerance);
                    else
                        points = edgePath(ref at, tolerance);

                    if (points.Count >= 3)
                        paths.Add(new HatchPath(points, outermost));
                }

                return paths;
            }

            ///<summary>A boundary written as a polyline: a vertex count, then the vertices, bulges and all.</summary>
            private List<(double X, double Y)> polylinePath(ref int at, double tolerance)
            {
                bool hasBulges = ReadWhole(IndexOf(72, at + 1)) != 0;

                int countAt = IndexOf(93, at + 1);

                if (countAt < 0)
                    return new List<(double, double)>();

                int vertices = ReadWhole(countAt);

                var points = new List<(double X, double Y)>();
                var bulges = new List<double>();

                int i = countAt + 1;

                while (points.Count < vertices && i < Body.Count)
                {
                    if (Body[i].Code == 10 && i + 1 < Body.Count && Body[i + 1].Code == 20)
                    {
                        points.Add((Number(Body[i].Value), Number(Body[i + 1].Value)));
                        bulges.Add(0);

                        i += 2;

                        //The bulge for that vertex, when the path said it carries them.
                        if (hasBulges && i < Body.Count && Body[i].Code == 42)
                        {
                            bulges[^1] = Number(Body[i].Value);

                            i++;
                        }

                        continue;
                    }

                    i++;
                }

                at = i;

                return DxfCurves.Bulged(points, bulges, closed: true, tolerance);
            }

            ///
            ///A boundary written as edges: a count, then that many, each of which says its own kind first.
            ///
            ///Every edge contributes its own points and the run is the concatenation, which is why each one
            ///drops the point it ends on - the next edge starts there.
            ///
            private List<(double X, double Y)> edgePath(ref int at, double tolerance)
            {
                int countAt = IndexOf(93, at + 1);

                if (countAt < 0)
                    return new List<(double, double)>();

                int edges = ReadWhole(countAt);

                var points = new List<(double X, double Y)>();

                int i = countAt + 1;

                for (int edge = 0; edge < edges; edge++)
                {
                    int kindAt = IndexOf(72, i);

                    if (kindAt < 0)
                        break;

                    //Where this edge's values end: at the next edge's kind, or at the source-object count
                    //that closes the path.
                    int next = IndexOf(72, kindAt + 1);
                    int ends = IndexOf(97, kindAt + 1);

                    if (next < 0 || (ends >= 0 && ends < next))
                        next = ends;

                    if (next < 0)
                        next = Body.Count;

                    points.AddRange(edgeRun(kindAt, next, ReadWhole(kindAt), tolerance));

                    i = next;
                }

                at = i;

                return points;
            }

            ///<summary>One edge of a boundary, as points - the last one left off for the edge after it.</summary>
            private List<(double X, double Y)> edgeRun(int from, int to, int kind, double tolerance)
            {
                var slice = new Entity("EDGE");

                for (int i = from; i < to; i++)
                    slice.Body.Add(Body[i]);

                var points = new List<(double X, double Y)>();

                if (kind == 1)
                {
                    points.Add((slice.Real(10), slice.Real(20)));
                }
                else if (kind == 2)
                {
                    points.AddRange(slice.Curve(slice.Real(50), slice.Real(51), tolerance));
                }
                else if (kind == 3)
                {
                    //An elliptic edge writes its parameters in degrees, where the ELLIPSE entity writes the
                    //same two in radians. The format's own inconsistency, and the one thing about this edge
                    //that is easy to get wrong.
                    double start = slice.Real(50) * Math.PI / 180.0;
                    double end = slice.Real(51) * Math.PI / 180.0;
                    double sweep = end - start;

                    while (sweep <= 0)
                        sweep += 2 * Math.PI;

                    points.AddRange(DxfCurves.Ellipse(
                        slice.Real(10),
                        slice.Real(20),
                        slice.Real(11),
                        slice.Real(21),
                        slice.Real(40),
                        start,
                        sweep,
                        tolerance));
                }
                else if (kind == 4)
                {
                    points.AddRange(DxfCurves.Spline(
                        slice.Points(10, 20),
                        slice.Reals(40),
                        slice.Reals(42),
                        slice.Whole(94),
                        closed: false,
                        tolerance));
                }

                //The point this edge ends on is the one the next begins from.
                if (points.Count > 1)
                    points.RemoveAt(points.Count - 1);

                return points;
            }

            ///<summary>The first pair with that code at or after an index, or -1.</summary>
            private int IndexOf(int code, int from)
            {
                for (int i = Math.Max(0, from); i < Body.Count; i++)
                {
                    if (Body[i].Code == code)
                        return i;
                }

                return -1;
            }

            private int ReadWhole(int index)
            {
                if (index < 0 || index >= Body.Count)
                    return 0;

                return (int)Math.Round(Number(Body[index].Value));
            }

            #endregion **********************************************************

            ///
            ///A circle or an arc, as the run of points that stands in for it.
            ///
            ///Angles in degrees here because that is what the format writes them in, and DXF arcs run
            ///counterclockwise from the start - so an end angle below the start has come the long way round
            ///rather than backwards.
            ///
            public List<(double X, double Y)> Curve(double fromDegrees, double toDegrees, double tolerance)
            {
                double sweep = toDegrees - fromDegrees;

                while (sweep <= 0)
                    sweep += 360;

                return DxfCurves.Arc(
                    Real(10),
                    Real(20),
                    Real(40),
                    fromDegrees * Math.PI / 180.0,
                    sweep * Math.PI / 180.0,
                    tolerance);
            }
        }

        #endregion **************************************************************************
    }
}
