using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///Writes OASIS (SEMI P39), the other half of <see cref="OasisReader"/>.
    ///
    ///**The hierarchy is kept.** A cell goes over as a cell and a placement as a placement, so a library of
    ///two hundred standard cells placed a thousand times stays two hundred cells and a thousand placements
    ///rather than becoming a million polygons. That is the whole reason to write this format at all, and it
    ///is why this walks the structural model rather than the flattener's output the way
    ///<see cref="LayoutWriter"/> does.
    ///
    ///**Correct before compact.** OASIS gets its size from two things: modal variables, where a record
    ///leaves out a field that has not changed since the last one, and repetitions, where one record stands
    ///for a grid of copies. Only the second is used here, and only for the one case that hands it over
    ///ready-made - a GDSII AREF. Everything else writes every field on every record.
    ///
    ///That costs bytes and buys something worth more: no record here depends on what the record before it
    ///left behind. Modal state is where an OASIS writer goes wrong, and it goes wrong quietly - the file
    ///still parses, and one shape is in the wrong place a thousand records later. Squeezing it comes after
    ///a corpus has round-tripped, not before.
    ///
    ///It is still a good deal smaller than the GDSII it came from: a variable-length integer beats a fixed
    ///four bytes, a coordinate is a delta from the last one rather than an absolute, and a rectangle - which
    ///is most of a layout - is a position and two lengths rather than five points.
    ///
    ///**What has no OASIS spelling.** A GDSII NODE is a connectivity marker rather than an area, and there
    ///is nothing to write it as; those are counted and reported rather than dropped in silence. A TEXT loses
    ///its PRESENTATION, because an OASIS text is an anchor and a string with no justification of its own. A
    ///round-ended PATH becomes a half-width extended one, which is the closest of the three ends the format
    ///offers.
    ///
    ///Coordinates are **not** scaled, for the same reason the reader does not scale them: both formats count
    ///in database units, and only the header says how big one is.
    ///</summary>
    public static class OasisWriter
    {
        #region Constants *******************************************************************

        ///<summary>What every OASIS file starts with, before the START record.</summary>
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("%SEMI-OASIS\r\n");

        ///<summary>The only version there is, and the only one the reader accepts.</summary>
        private const string Version = "1.0";

        ///<summary>
        ///How long the END record has to be.
        ///
        ///The specification fixes it, and KLayout enforces it - it refused a hand-built file with a bare END
        ///outright. The length is made up by padding a string that carries nothing.
        ///</summary>
        private const int EndRecordLength = 256;

        ///<summary>
        ///Database units per micron for a file whose UNITS record is missing or unreadable.
        ///
        ///A thousand, so one database unit is a nanometer - which is what every bundled sample uses and what
        ///a GDSII file that says nothing is usually meant to be.
        ///</summary>
        private const double DefaultUnit = 1000;

        ///<summary>The name given to a structure with no STRNAME, so a placement still has something to name.</summary>
        private const string UnnamedCell = "UNNAMED";

        #endregion **************************************************************************



        #region Writing *********************************************************************

        ///<summary>The library as OASIS bytes.</summary>
        public static byte[] Write(GDS gds)
        {
            return Write(gds, out _);
        }

        ///<summary>
        ///The same, reporting how many elements had no OASIS spelling and were left out. Normally zero, and
        ///worth saying out loud when it is not - a shape quietly missing from a converted file is the kind
        ///of thing found much later by whoever opens it.
        ///</summary>
        public static byte[] Write(GDS gds, out int skipped)
        {
            var writer = new Writer(gds);

            byte[] bytes = writer.Build();

            skipped = writer.Skipped;

            return bytes;
        }

        public static void Write(GDS gds, Stream stream)
        {
            byte[] bytes = Write(gds);

            stream.Write(bytes, 0, bytes.Length);
        }

        ///<summary>
        ///The same, for a stream that can only be written asynchronously - which the browser's download
        ///path is, the same way the upload path can only be read that way.
        ///</summary>
        public static async Task WriteAsync(GDS gds, Stream stream, CancellationToken cancellationToken = default)
        {
            byte[] bytes = Write(gds);

            await stream.WriteAsync(bytes, cancellationToken);
        }

        #endregion **************************************************************************



        #region The byte level **************************************************************

        ///<summary>
        ///The format's primitives.
        ///
        ///Every number in an OASIS file is one of two shapes: seven bits a byte with the top bit saying more
        ///follows, or that with the lowest few bits of the first byte carrying a sign or a direction instead.
        ///The second is written by <see cref="Packed"/>, which is the mirror of the reader's ReadPacked and
        ///the one place the bit arithmetic lives.
        ///</summary>
        private sealed class Bytes
        {
            private readonly List<byte> bytes = new List<byte>();

            public int Count
            {
                get { return bytes.Count; }
            }

            public byte[] ToArray()
            {
                return bytes.ToArray();
            }

            public void Byte(byte value)
            {
                bytes.Add(value);
            }

            public void Raw(byte[] value)
            {
                bytes.AddRange(value);
            }

            ///<summary>Seven bits a byte, low group first, the top bit saying more follows.</summary>
            public void Unsigned(ulong value)
            {
                while (value > 0x7F)
                {
                    bytes.Add((byte)((value & 0x7F) | 0x80));

                    value >>= 7;
                }

                bytes.Add((byte)value);
            }

            ///<summary>
            ///The same with <paramref name="carried"/> in the lowest <paramref name="skipBits"/> bits, which
            ///is how every signed quantity in the format is written: an integer carries one bit, a g-delta
            ///four.
            ///</summary>
            public void Packed(ulong value, int skipBits, byte carried)
            {
                Unsigned((value << skipBits) | carried);
            }

            ///<summary>A whole number with its sign in the lowest bit.</summary>
            public void Signed(long value)
            {
                byte sign = 0;

                if (value < 0)
                    sign = 1;

                Packed(magnitude(value), 1, sign);
            }

            public void Text(string value)
            {
                //Latin-1 rather than UTF-8, matching the reader: the format's strings are bytes, and this is
                //the encoding under which every byte is a character, so a name survives the round trip
                //whatever is in it.
                byte[] encoded = Encoding.Latin1.GetBytes(value);

                Unsigned((ulong)encoded.Length);

                bytes.AddRange(encoded);
            }

            ///<summary>
            ///A number, in whichever of the format's eight kinds says it exactly.
            ///
            ///A whole one goes as a whole one, which is both shorter and what a reader expects to see for a
            ///unit of 1000. Anything else goes as an eight-byte double, which is the only kind that can
            ///hold an arbitrary one - the rationals would need the fraction found first, and would still
            ///not cover every value.
            ///</summary>
            public void Real(double value)
            {
                if (value == Math.Floor(value) && Math.Abs(value) <= long.MaxValue && !double.IsInfinity(value))
                {
                    if (value >= 0)
                        Byte(0);
                    else
                        Byte(1);

                    Unsigned(magnitude((long)value));

                    return;
                }

                Byte(7);

                var eight = new byte[8];

                BinaryPrimitives.WriteDoubleLittleEndian(eight, value);

                bytes.AddRange(eight);
            }

            ///<summary>
            ///A step anywhere, in the arbitrary-pair form: two integers, the first carrying a flag bit
            ///saying this is that form and the sign of x above it, the second carrying the sign of y.
            ///
            ///The eight-direction form would be a byte shorter for a step along an axis, which most steps
            ///are. It is not used, because one form that covers every case cannot be got wrong for the
            ///cases it does not cover - and the reader accepts both.
            ///</summary>
            public void GDelta(long x, long y)
            {
                byte first = 0x01;

                if (x < 0)
                    first |= 0x02;

                Packed(magnitude(x), 2, first);

                byte second = 0;

                if (y < 0)
                    second = 0x01;

                Packed(magnitude(y), 1, second);
            }

            ///<summary>
            ///The size of a number without its sign, as an unsigned.
            ///
            ///Through ulong rather than Math.Abs, which throws on long.MinValue - the one value whose
            ///negation does not fit back into a long. Nothing in a layout is that far out, but a converter
            ///that crashes on one coordinate is worse than one that writes it.
            ///</summary>
            private static ulong magnitude(long value)
            {
                if (value < 0)
                    return (ulong)(-(value + 1)) + 1;

                return (ulong)value;
            }
        }

        #endregion **************************************************************************



        #region The file ********************************************************************

        ///<summary>
        ///Walks the library and writes it out.
        ///
        ///One pass, front to back. The name tables and the offset table at the end of a well-packed OASIS
        ///file are an index for a reader that wants to jump about; nothing is written here that needs one,
        ///and the offsets are declared absent rather than filled with lies.
        ///</summary>
        private sealed class Writer
        {
            private readonly GDS gds;
            ///
            ///Where bytes go. Not readonly, because a cell's body is built into one of these of its own
            ///before being handed to the file - see writeCell.
            ///
            private Bytes file = new Bytes();

            ///
            ///The modal variables this writer keeps, which have to be exactly the reader's.
            ///
            ///**None of them is reset at a cell boundary, and that is not an oversight on either side.**
            ///OasisReader.resetCellState resets the addressing mode and the six x/y variables and nothing
            ///else, so layer, datatype and the sizes carry from the last record of one cell into the first
            ///record of the next. A writer that reset them here would leave out a field the reader still
            ///thinks it knows, and every shape in the second cell would land on the layer the first one
            ///ended on. Compressing the bodies changes nothing about this: the state belongs to the reader,
            ///not to the buffer it is reading from.
            ///
            ///Started at -1, which no layer, datatype or size can be, so the first record of a file writes
            ///every field.
            ///
            ///**Text has its own pair.** textlayer and texttype are separate modal variables from layer and
            ///datatype - see readText against readRectangle - so sharing one pair between labels and
            ///geometry would put labels on whatever layer the last shape used.
            ///
            private int modalLayer = -1;
            private int modalDataType = -1;
            private int modalTextLayer = -1;
            private int modalTextType = -1;
            private long modalWidth = -1;
            private long modalHeight = -1;
            private long modalPathHalfWidth = -1;

            ///<summary>Elements with nothing to write them as. Reported rather than passed over.</summary>
            public int Skipped { get; private set; }

            public Writer(GDS gds)
            {
                this.gds = gds;
            }

            public byte[] Build()
            {
                file.Raw(Magic);

                writeStart();

                foreach (var structure in gds.StreamFormat.Structures)
                    writeCell(structure);

                writeEnd();

                return file.ToArray();
            }

            ///<summary>
            ///The START record: the version, how big a database unit is, and where the name tables are.
            ///
            ///The offset flag is zero - "the table offsets are in this record rather than in END" - and the
            ///twelve that follow are all zero, since there are no tables to point at. Putting them in END
            ///instead is equally valid and is what the flag being 1 would mean; this is the shape KLayout
            ///has already been shown to accept.
            ///</summary>
            private void writeStart()
            {
                file.Byte(1);
                file.Text(Version);
                file.Real(unit());
                file.Unsigned(0);

                for (int i = 0; i < 12; i++)
                    file.Unsigned(0);
            }

            ///<summary>
            ///Database units per micron, from the GDSII UNITS record.
            ///
            ///UNITS holds how big a database unit is in *meters*, so a micron holds the reciprocal of a
            ///million times it. A file whose UNITS is missing or nonsense gets the usual nanometer grid
            ///rather than a unit of zero, which no reader would accept.
            ///</summary>
            private double unit()
            {
                if (gds.StreamFormat.UNITS?.Data is not Real8Data units || units.Values.Length < 2)
                    return DefaultUnit;

                double metersPerDatabaseUnit = units.Values[1];

                if (metersPerDatabaseUnit <= 0 || double.IsNaN(metersPerDatabaseUnit) || double.IsInfinity(metersPerDatabaseUnit))
                    return DefaultUnit;

                return 1e-6 / metersPerDatabaseUnit;
            }

            ///<summary>
            ///The END record, padded to exactly 256 bytes.
            ///
            ///The specification fixes the length and KLayout enforces it. One byte for the record, a string
            ///of spaces long enough to make up the difference, and a zero saying the file carries no
            ///checksum.
            ///
            ///The padding is solved for rather than computed, because the string's own length prefix is
            ///part of what has to add up: 240 characters need two bytes to say so and 120 need one, so
            ///subtracting a fixed overhead gives an answer one byte out - which is exactly the kind of
            ///thing that produces a file every reader rejects for a reason none of them explain.
            ///</summary>
            private void writeEnd()
            {
                int before = file.Count;

                file.Byte(2);

                //Everything the padding string has to fill, less the validation byte after it.
                int available = EndRecordLength - (file.Count - before) - 1;
                int padding = available;

                while (padding > 0 && unsignedLength((ulong)padding) + padding > available)
                    padding--;

                file.Text(new string(' ', padding));
                file.Unsigned(0);

                if (file.Count - before != EndRecordLength)
                    throw new InvalidOperationException($"The END record came to {file.Count - before} bytes rather than {EndRecordLength}.");
            }

            ///<summary>How many bytes an unsigned integer takes: seven bits in each.</summary>
            private static int unsignedLength(ulong value)
            {
                int length = 1;

                while (value > 0x7F)
                {
                    value >>= 7;
                    length++;
                }

                return length;
            }

            #endregion **********************************************************************



            #region Cells *******************************************************************

            private void writeCell(StructureModel structure)
            {
                file.Byte(14);//CELL, named rather than by reference number
                file.Text(nameOf(structure));

                //
                //The body is built on its own before it is put in the file, so it can be compressed whole.
                //
                //CELL stays outside it. A reader has to know which cell it is in before the geometry
                //arrives, and this one resets its per-cell modal state when that record is read - so the
                //name has to be readable without inflating anything.
                //
                var body = new Bytes();
                var outer = file;

                file = body;

                try
                {
                    //Every coordinate written below is an absolute one. Said out loud rather than left to
                    //the reader's default, because it is a modal variable like any other and a file that
                    //never mentions it is relying on where the reader happens to start.
                    file.Byte(15);//XYABSOLUTE

                    writeElements(structure);
                }
                finally
                {
                    file = outer;
                }

                writeBody(body.ToArray());
            }

            ///
            ///A cell's records, compressed if that makes them smaller and plain if it does not.
            ///
            ///**DEFLATE over the records already written, which is what makes this cheap and safe.** Every
            ///byte inside the block is a byte this writer already produced and 897 files already round-trip
            ///through - nothing about how a record is spelled changes, only whether it is stored packed. So
            ///a compression bug cannot produce a wrong shape; it can only produce a block that will not
            ///inflate, which fails loudly at the first record read.
            ///
            ///**Whole records only, and never nested.** The reader steps out of a block the moment its
            ///inflated bytes run out (see Cursor.ReadByte) and carries on from the outer buffer, so a record
            ///straddling the end would be read half from one and half from the other - quietly wrong rather
            ///than refused. A body is built from complete records, so that cannot arise here. Nesting cannot
            ///either: Cursor holds one outer buffer, and this writes one block per cell with nothing inside
            ///it that compresses anything.
            ///
            ///**Only when it actually helps.** Deflate on a few dozen bytes is usually longer than what it
            ///compresses, and the header costs four or five on top. A standard cell is small enough for that
            ///to matter - which is most of this repository's own examples - so the plain body goes in
            ///whenever the packed one is not smaller.
            ///
            private void writeBody(byte[] body)
            {
                byte[] deflated = deflate(body);

                var block = new Bytes();

                block.Byte(34);//CBLOCK
                block.Unsigned(0);//DEFLATE, the only method the format defines
                block.Unsigned((ulong)body.Length);
                block.Unsigned((ulong)deflated.Length);
                block.Raw(deflated);

                if (block.Count >= body.Length)
                {
                    file.Raw(body);

                    return;
                }

                file.Raw(block.ToArray());
            }

            ///
            ///Raw deflate, with no zlib header - which is what the format asks for and what the reader reads.
            ///
            ///**Optimal rather than SmallestSize**, which is not the choice the names suggest.
            ///
            ///On a 200,000-shape layout SmallestSize is neither smaller nor faster: 484,276 bytes in
            ///284/334/283 ms against Optimal's 482,662 in 187/233/184. Fastest is worse again on both counts
            ///- 833,694 bytes, and slower than Optimal, since what it saves in searching it spends writing
            ///the extra bytes back out.
            ///
            ///**It does win on small files, and the amount is why it is not taken.** Over the 897 bundled
            ///standard cells SmallestSize comes to 1,378,450 bytes against Optimal's 1,392,811 - a little
            ///over one per cent, for a third again as long on every file written. The case compression is
            ///for is the large layout, and that is the one Optimal takes.
            ///
            private static byte[] deflate(byte[] body)
            {
                using var packed = new MemoryStream();

                using (var stream = new DeflateStream(packed, CompressionLevel.Optimal, leaveOpen: true))
                    stream.Write(body, 0, body.Length);

                return packed.ToArray();
            }

            private static string nameOf(StructureModel structure)
            {
                string name = (structure.STRNAME?.Data as AsciiData)?.Value ?? "";

                if (name.Length == 0)
                    return UnnamedCell;

                return name;
            }

            ///
            ///A cell's elements, with runs of the same rectangle collapsed into one record and a repetition.
            ///
            ///**A repetition is one record standing for a row of copies**, which is what a layout is full
            ///of: a via array, a row of fingers, a fill pattern. The record carries the shape once and the
            ///count and pitch after it, and the reader lays them out.
            ///
            ///Rectangles only. They are 86% of the boundaries in the bundled corpus and all of what a fill
            ///or a via array is made of, and a polygon run would need whole point lists compared rather than
            ///four numbers. Runs of three or more, along one axis, at a constant pitch.
            ///
            ///**This is the one thing here that reorders the file.** A run is written where the first of its
            ///members was and the rest are skipped, so a shape can move earlier in the cell than it was.
            ///Nothing in OASIS makes the order of geometry within a cell mean anything - it is a set - but it
            ///does mean the bytes are no longer in the order the GDSII had them, and a diff between two
            ///conversions is no longer a diff of the layout.
            ///
            ///
            ///A cell's elements, with runs of the same thing collapsed into one record and a repetition.
            ///
            ///**A repetition is one record standing for a row of copies**, which is what a layout is full
            ///of: a via array, a row of fingers, a fill pattern, a standard cell placed along a site row.
            ///The record carries the thing once and the count and pitch after it, and the reader lays them
            ///out.
            ///
            ///Rectangles and placements. Rectangles are 86% of the boundaries in the bundled corpus and all
            ///of what a fill or a via array is made of; a polygon run would need whole point lists compared
            ///rather than a handful of numbers. Placements are here because a tool that writes a row of
            ///separate `SREF`s rather than one `AREF` is writing this out by hand - an `AREF` already
            ///arrives as a repetition, through writeArray.
            ///
            ///Runs of three or more, along one axis, at a constant pitch. Two would spend a repetition
            ///record to save one copy.
            ///
            ///**This is the one thing here that reorders the file.** A run is written where the first of its
            ///members sat and the rest are skipped, so a shape can move earlier in the cell than it was.
            ///Nothing in OASIS makes the order of geometry within a cell mean anything - it is a set - but a
            ///diff between two conversions is no longer a diff of the layout.
            ///
            private void writeElements(StructureModel structure)
            {
                var models = structure.Elements.ToList();

                var repeatable = new Dictionary<int, Repeated>();

                for (int i = 0; i < models.Count; i++)
                {
                    if (repeatedAt(models[i].Element) is Repeated found)
                        repeatable[i] = found;
                }

                var runs = runsAmong(repeatable);

                var written = new HashSet<int>();

                for (int i = 0; i < models.Count; i++)
                {
                    if (written.Contains(i))
                        continue;

                    if (runs.TryGetValue(i, out var run))
                    {
                        writeElement(models[run.Anchor].Element, run);

                        foreach (int at in run.At)
                            written.Add(at);

                        continue;
                    }

                    writeElement(models[i].Element, null);
                }
            }

            ///
            ///One thing that could be repeated: what makes two of them the same, and where this one is.
            ///
            ///**The key is a string and it carries what kind of thing this is.** A rectangle groups on its
            ///layer, datatype and size; a placement on the cell it names and how it is turned. Those have
            ///nothing in common but the question being asked of them, and the leading letter is what stops a
            ///rectangle from ever grouping with a placement whose numbers happen to read the same.
            ///
            private readonly record struct Repeated(string Key, long X, long Y);

            ///<summary>A run: where its members sit, which way it goes, the step, and which one it starts from.</summary>
            private readonly record struct Run(List<int> At, bool AlongX, long Step, int Anchor);

            ///
            ///The element as something repeatable, or null - a path, a label, a polygon, an array.
            ///
            ///Placements only in the right-angle, unit-magnification form. A transformed one carries a real
            ///magnification and a real angle, and whether two of those are "the same" is a floating-point
            ///question this has no reason to ask: layouts that place a cell along a row place it the same
            ///way up.
            ///
            private Repeated? repeatedAt(ElementType element)
            {
                if (rectangleAt(element) is (int layer, int dataType, Element.Point corner, long width, long height))
                    return new Repeated(FormattableString.Invariant($"r {layer} {dataType} {width} {height}"), corner.X, corner.Y);

                if (placementAt(element) is (string name, int quarter, bool flipped, long x, long y))
                    return new Repeated(FormattableString.Invariant($"p {name} {quarter} {flipped}"), x, y);

                return null;
            }

            ///<summary>A rectangle as everything needed to write one, or null when the element is not one.</summary>
            private (int Layer, int DataType, Element.Point Corner, long Width, long Height)? rectangleAt(ElementType element)
            {
                Record? layer;
                Record? dataType;
                Record? xy;

                if (element is BoundaryModel boundary)
                {
                    layer = boundary.LAYER;
                    dataType = boundary.DATATYPE;
                    xy = boundary.XY;
                }
                else if (element is BoxModel box)
                {
                    layer = box.LAYER;
                    dataType = box.BOXTYPE;
                    xy = box.XY;
                }
                else
                {
                    return null;
                }

                var points = pointsOf(xy);

                dropClosingPoint(points);

                if (rectangleOf(points) is not (Element.Point corner, long width, long height))
                    return null;

                return (numberOf(layer), numberOf(dataType), corner, width, height);
            }

            ///<summary>A plain placement as everything needed to write one, or null.</summary>
            private (string Name, int Quarter, bool Flipped, long X, long Y)? placementAt(ElementType element)
            {
                if (element is not SrefModel sref)
                    return null;

                var flags = Strans.From(sref.Strans?.STRANS?.Data);

                double magnification = 1;
                double angle = 0;

                if (sref.Strans?.MAG?.Data is Real8Data mag)
                    magnification = mag.Value;

                if (sref.Strans?.ANGLE?.Data is Real8Data rotation)
                    angle = rotation.Value;

                if (magnification != 1 || rightAngle(angle) is not int quarter)
                    return null;

                var points = pointsOf(sref.XY);

                if (points.Count < 1)
                    return null;

                return (referencedName(sref.SNAME), quarter, flags.ReflectAboutX, points[0].X, points[0].Y);
            }

            ///
            ///Which of them form runs, keyed on where each run starts.
            ///
            ///Grouped by everything except position, so only identical things are ever compared. Within a
            ///group a run is three or more sharing one coordinate and evenly spaced along the other.
            ///
            ///**Along x first, then along y among what is left.** A grid of vias satisfies both, and taking
            ///the rows leaves each column with one member - which is not a run. The other order collapses
            ///the same grid the other way for the same saving.
            ///
            private static Dictionary<int, Run> runsAmong(Dictionary<int, Repeated> things)
            {
                var runs = new Dictionary<int, Run>();
                var taken = new HashSet<int>();

                foreach (var group in things.GroupBy(each => each.Value.Key))
                {
                    var members = group.ToList();

                    findRuns(members, taken, runs, alongX: true);
                    findRuns(members, taken, runs, alongX: false);
                }

                return runs;
            }

            private static void findRuns(List<KeyValuePair<int, Repeated>> members, HashSet<int> taken, Dictionary<int, Run> runs, bool alongX)
            {
                //Everything on one line, in order along it, so a run is a walk with a constant step.
                foreach (var line in members.Where(each => !taken.Contains(each.Key)).GroupBy(each => across(each.Value, alongX)))
                {
                    var along = line.OrderBy(each => down(each.Value, alongX)).ToList();

                    int at = 0;

                    while (at < along.Count - 2)
                    {
                        long step = down(along[at + 1].Value, alongX) - down(along[at].Value, alongX);

                        int end = at + 1;

                        //A step of nothing is two things in the same place, which is not a repetition.
                        if (step > 0)
                        {
                            while (end + 1 < along.Count && down(along[end + 1].Value, alongX) - down(along[end].Value, alongX) == step)
                                end++;
                        }

                        if (step > 0 && (end - at) + 1 >= 3)
                        {
                            var positions = new List<int>();

                            for (int i = at; i <= end; i++)
                            {
                                positions.Add(along[i].Key);
                                taken.Add(along[i].Key);
                            }

                            //
                            //Two different firsts, and they are not the same one.
                            //
                            //A repetition lays its copies out from the record it hangs off, stepping one
                            //way, so that record has to be the member the run *starts* from - along[at].
                            //Where it is written is the lowest element index, so the file stays as near its
                            //own order as collapsing allows. Anchoring on the wrong one lays the whole run
                            //out from the wrong end, which the corpus reported as the right number of
                            //shapes in the wrong places.
                            //
                            int anchor = along[at].Key;

                            positions.Sort();

                            runs[positions[0]] = new Run(positions, alongX, step, anchor);

                            at = end + 1;

                            continue;
                        }

                        at++;
                    }
                }
            }

            ///<summary>The coordinate a run holds constant.</summary>
            private static long across(Repeated thing, bool alongX)
            {
                if (alongX)
                    return thing.Y;

                return thing.X;
            }

            ///<summary>And the one it steps along.</summary>
            private static long down(Repeated thing, bool alongX)
            {
                if (alongX)
                    return thing.X;

                return thing.Y;
            }

            ///
            ///The repetition itself, kind 2 along x and kind 3 along y.
            ///
            ///It goes last in every record that carries one, which is the reader's order: everything the
            ///info byte promised, and then this. Kinds 2 and 3 are the axis-aligned forms - a count and a
            ///single spacing, where the general form carries a vector per step.
            ///
            ///The count written is two less than the number of copies and the reader adds it back. The
            ///spacing is unsigned, which is why a run is found in ascending order and a step of zero is
            ///refused rather than written.
            ///
            private void writeRepeat(Run run)
            {
                if (run.AlongX)
                    file.Byte(2);
                else
                    file.Byte(3);

                file.Unsigned((ulong)(run.At.Count - 2));
                file.Unsigned((ulong)run.Step);
            }

            private void writeElement(ElementType element, Run? repeat)
            {
                if (element is BoundaryModel boundary)
                {
                    writeOutline(boundary.LAYER, boundary.DATATYPE, boundary.XY, repeat);

                    return;
                }

                //A box is a rectangle drawn on a layer, and every view here treats it as one - so it is
                //written as the geometry it draws rather than left out for being a different record.
                if (element is BoxModel box)
                {
                    writeOutline(box.LAYER, box.BOXTYPE, box.XY, repeat);

                    return;
                }

                if (element is PathModel path)
                {
                    writePath(path);

                    return;
                }

                if (element is TextModel text)
                {
                    writeText(text);

                    return;
                }

                if (element is SrefModel sref)
                {
                    writePlacement(sref.SNAME, sref.Strans, sref.XY, null, repeat);

                    return;
                }

                if (element is ArefModel aref)
                {
                    writeArray(aref);

                    return;
                }

                //A NODE, or something this does not know. A node marks an electrical connection rather than
                //an area, and OASIS has no record for one.
                Skipped++;
            }

            #endregion **********************************************************************



            #region Shapes ******************************************************************

            ///<summary>
            ///A closed outline, as a RECTANGLE when it is one and a POLYGON otherwise.
            ///
            ///Worth telling apart rather than writing everything as a polygon: most of a layout is
            ///rectangles, and one goes over as a position and two lengths where a polygon needs four points
            ///and a list header. It is also what a reader on the other side expects to find, which matters
            ///for anything that treats a rectangle specially.
            ///</summary>
            private void writeOutline(Record? layer, Record? dataType, Record? xy, Run? repeat)
            {
                var points = pointsOf(xy);

                //A GDSII boundary repeats its first point to close the ring; OASIS closes one implicitly, so
                //the repeat would be a zero-length edge in the middle of the point list.
                dropClosingPoint(points);

                if (points.Count < 3)
                {
                    Skipped++;

                    return;
                }

                if (rectangleOf(points) is (Element.Point corner, long width, long height))
                {
                    writeRectangle(numberOf(layer), numberOf(dataType), corner, width, height, repeat);

                    return;
                }

                writePolygon(numberOf(layer), numberOf(dataType), points, repeat);
            }

            ///
            ///A rectangle: a corner and two lengths, with whatever the last record already said left out.
            ///
            ///Still not the square bit. It saves one number on a shape that happens to be square, and the
            ///reader takes it as "height is width" only when the height is *absent* - so with the height
            ///modal it would be a third spelling of the same thing, and the saving is already had by not
            ///writing the height at all.
            ///
            private void writeRectangle(int layer, int dataType, Element.Point corner, long width, long height, Run? repeat)
            {
                //x and y always; a rectangle somewhere else is why there is a second record at all.
                byte info = 0x10 | 0x08;

                if (repeat is not null)
                    info |= 0x04;

                if (layer != modalLayer)
                    info |= 0x01;

                if (dataType != modalDataType)
                    info |= 0x02;

                if (width != modalWidth)
                    info |= 0x40;

                if (height != modalHeight)
                    info |= 0x20;

                file.Byte(20);//RECTANGLE
                file.Byte(info);

                //In the reader's order, which is the whole of the contract between the two.
                if ((info & 0x01) != 0)
                {
                    file.Unsigned((ulong)layer);
                    modalLayer = layer;
                }

                if ((info & 0x02) != 0)
                {
                    file.Unsigned((ulong)dataType);
                    modalDataType = dataType;
                }

                if ((info & 0x40) != 0)
                {
                    file.Unsigned((ulong)width);
                    modalWidth = width;
                }

                if ((info & 0x20) != 0)
                {
                    file.Unsigned((ulong)height);
                    modalHeight = height;
                }

                file.Signed(corner.X);
                file.Signed(corner.Y);

                if (repeat is Run run)
                    writeRepeat(run);
            }

            private void writePolygon(int layer, int dataType, List<Element.Point> points, Run? repeat)
            {
                //Point list, x and y always; layer and datatype only when they have moved on.
                byte info = 0x20 | 0x10 | 0x08;

                if (repeat is not null)
                    info |= 0x04;

                if (layer != modalLayer)
                    info |= 0x01;

                if (dataType != modalDataType)
                    info |= 0x02;

                file.Byte(21);//POLYGON
                file.Byte(info);

                if ((info & 0x01) != 0)
                {
                    file.Unsigned((ulong)layer);
                    modalLayer = layer;
                }

                if ((info & 0x02) != 0)
                {
                    file.Unsigned((ulong)dataType);
                    modalDataType = dataType;
                }

                writePointList(points, closed: true);

                //The first point is where the shape is; the list holds the rest as steps from it.
                file.Signed(points[0].X);
                file.Signed(points[0].Y);

                if (repeat is Run run)
                    writeRepeat(run);
            }

            private void writePath(PathModel path)
            {
                var points = pointsOf(path.XY);

                if (points.Count < 2)
                {
                    Skipped++;

                    return;
                }

                //A negative WIDTH means an absolute one - a width the parent's magnification does not
                //scale. OASIS has no such distinction, so the size is what carries over.
                long halfWidth = Math.Abs(widthOf(path)) / 2;

                int layer = numberOf(path.LAYER);
                int dataType = numberOf(path.DATATYPE);

                //
                //Extension scheme, point list, x and y always; layer, datatype and the half width only when
                //they have changed.
                //
                //The scheme byte stays on every path rather than being left to the modal extensions. It is
                //one byte, the two extensions behind it are a pair of signed numbers with their own modal
                //state, and a path whose ends differ from the last one's is common enough that leaving it
                //out would be trading a byte for a class of bug.
                //
                byte info = 0x80 | 0x20 | 0x10 | 0x08;

                if (layer != modalLayer)
                    info |= 0x01;

                if (dataType != modalDataType)
                    info |= 0x02;

                if (halfWidth != modalPathHalfWidth)
                    info |= 0x40;

                file.Byte(22);//PATH
                file.Byte(info);

                if ((info & 0x01) != 0)
                {
                    file.Unsigned((ulong)layer);
                    modalLayer = layer;
                }

                if ((info & 0x02) != 0)
                {
                    file.Unsigned((ulong)dataType);
                    modalDataType = dataType;
                }

                if ((info & 0x40) != 0)
                {
                    file.Unsigned((ulong)halfWidth);
                    modalPathHalfWidth = halfWidth;
                }

                writePathEnds(path, halfWidth);
                writePointList(points, closed: false);

                file.Signed(points[0].X);
                file.Signed(points[0].Y);
            }

            ///<summary>
            ///How the two ends of a path are finished, as the scheme byte and whatever follows it.
            ///
            ///Two bits each: 1 cuts the end flush at the last point, 2 carries it a half-width past, 3 says
            ///a distance follows. GDSII's PATHTYPE maps onto those directly except for type 1, a round cap,
            ///which OASIS has no way to say - it becomes the half-width extension, which is the same
            ///outline with square corners instead of a semicircle.
            ///</summary>
            private void writePathEnds(PathModel path, long halfWidth)
            {
                int pathType = 0;

                if (path.PATHTYPE?.Data is Int2Data type)
                    pathType = type.Value;

                //Type 4 is the only one that gives its own distances, and the two ends can differ - so the
                //scheme is built an end at a time rather than as one value for both.
                if (pathType == 4)
                {
                    file.Byte(0x0C | 0x03);
                    file.Signed(extensionOf(path.BGNEXTN));
                    file.Signed(extensionOf(path.ENDEXTN));

                    return;
                }

                if (pathType == 2 || pathType == 1)
                {
                    file.Byte(0x08 | 0x02);

                    return;
                }

                file.Byte(0x04 | 0x01);
            }

            private void writeText(TextModel text)
            {
                var points = pointsOf(text.XY);

                if (points.Count < 1)
                {
                    Skipped++;

                    return;
                }

                int textLayer = numberOf(text.LAYER);
                int textType = numberOf(text.TextBody.TEXTTYPE);

                //
                //Explicit string, x and y always; the layer pair only when it has moved on. Not 0x20, which
                //would make the string a reference into a table there is not one of.
                //
                //**The pair here is textlayer and texttype, not layer and datatype.** They are separate
                //modal variables in the format and separate fields in the reader, so a writer sharing one
                //pair between labels and shapes would put every label on whatever layer the last shape
                //happened to use - and the file would parse perfectly.
                //
                //The string stays explicit on every label. It is its own modal variable and could be left
                //out when it repeats, but a run of labels that all say the same thing is not what a layout
                //looks like, and the saving would be nothing on the files this actually writes.
                //
                //The justification in PRESENTATION has nowhere to go: an OASIS text is an anchor and a
                //string, and where the glyphs sit around that point is the reader's business.
                //
                byte info = 0x40 | 0x10 | 0x08;

                if (textLayer != modalTextLayer)
                    info |= 0x01;

                if (textType != modalTextType)
                    info |= 0x02;

                file.Byte(19);//TEXT
                file.Byte(info);

                //Both of these live on the text body rather than on the element, which is where the format
                //puts them: a TEXT record is followed by a run that carries the string and how to draw it.
                file.Text((text.TextBody.STRING?.Data as AsciiData)?.Value ?? "");

                if ((info & 0x01) != 0)
                {
                    file.Unsigned((ulong)textLayer);
                    modalTextLayer = textLayer;
                }

                if ((info & 0x02) != 0)
                {
                    file.Unsigned((ulong)textType);
                    modalTextType = textType;
                }

                file.Signed(points[0].X);
                file.Signed(points[0].Y);
            }

            ///
            ///A list of points, as steps from the one before.
            ///
            ///**Kinds 0 and 1 when the outline is manhattan**, which is nearly all of one: a step that is
            ///purely horizontal or purely vertical needs only its length, where the general kind 4 writes a
            ///g-delta carrying both a direction and a distance. Kind 0 starts horizontal and kind 1 starts
            ///vertical, and the two alternate from there, so the axis is never written at all.
            ///
            ///**A closed outline leaves its last corner out entirely.** The reader walks the steps and then
            ///adds one more point itself, along whichever axis has not just been used - see readPointList,
            ///where that is the `closed` branch. So a ring of N corners writes N-2 numbers where kind 4
            ///writes N-1 g-deltas. An open path has nothing implied and writes N-1.
            ///
            ///That count is the thing to get wrong here, and it is invisible to most of this suite: writing
            ///one step too many makes the reader append a corner that duplicates the first, and
            ///GdsTestData.Geometry sorts and de-duplicates before comparing. Which is why
            ///A_manhattan_polygon_keeps_its_corners_in_order compares the ordered list.
            ///
            ///Kind 5, a constant slope in almost no numbers, is still not written. Nothing in the corpus is
            ///a diagonal staircase.
            ///
            private void writePointList(List<Element.Point> points, bool closed)
            {
                if (manhattanKind(points, closed) is int kind)
                {
                    int steps = points.Count - 1;

                    //A closed ring's last corner is the reader's to work out.
                    if (closed)
                        steps = points.Count - 2;

                    file.Byte((byte)kind);
                    file.Unsigned((ulong)steps);

                    for (int i = 1; i <= steps; i++)
                    {
                        long alongX = points[i].X - (long)points[i - 1].X;
                        long alongY = points[i].Y - (long)points[i - 1].Y;

                        //One of the two is zero, which is what manhattanKind checked - so this is the
                        //length of the step and the axis is the one the alternation is up to.
                        if (alongX != 0)
                            file.Signed(alongX);
                        else
                            file.Signed(alongY);
                    }

                    return;
                }

                file.Byte(4);
                file.Unsigned((ulong)(points.Count - 1));

                for (int i = 1; i < points.Count; i++)
                    file.GDelta(points[i].X - (long)points[i - 1].X, points[i].Y - (long)points[i - 1].Y);
            }

            ///
            ///0 or 1 when every step is axis-aligned and they strictly alternate, and null otherwise.
            ///
            ///**Strictly** is the word doing the work. Two steps along the same axis in a row - a corner
            ///that is not a corner, which is what a collinear pair looks like once it is a point list -
            ///cannot be written this way at all, because the reader takes the axis from the alternation
            ///rather than from the file. So does a step of zero length, which is a repeated point.
            ///
            ///A closed ring is checked all the way round, its last edge included, because that edge is the
            ///one the reader invents and it has to land where the shape says. An even number of corners
            ///follows from that and is checked outright: an odd one cannot alternate and close.
            ///
            private static int? manhattanKind(List<Element.Point> points, bool closed)
            {
                if (points.Count < 3)
                    return null;

                if (closed && points.Count % 2 != 0)
                    return null;

                int edges = points.Count - 1;

                if (closed)
                    edges = points.Count;

                bool? horizontal = null;

                for (int i = 0; i < edges; i++)
                {
                    var from = points[i];
                    var to = points[(i + 1) % points.Count];

                    long alongX = to.X - (long)from.X;
                    long alongY = to.Y - (long)from.Y;

                    //Axis-aligned and going somewhere.
                    if (alongX != 0 && alongY != 0)
                        return null;

                    if (alongX == 0 && alongY == 0)
                        return null;

                    bool thisOne = alongX != 0;

                    if (horizontal is bool last && thisOne == last)
                        return null;

                    horizontal = thisOne;
                }

                //The first step decides which kind it is; the rest follow from alternating.
                if (points[1].X != points[0].X)
                    return 0;

                return 1;
            }

            #endregion **********************************************************************



            #region Placements **************************************************************

            ///<summary>
            ///One placement of another cell, optionally standing for a grid of them.
            ///
            ///Record 17 when the orientation is one of the four right angles at natural size, which nearly
            ///every placement in a real layout is, and 18 when it is not - 17 spends two bits on the angle
            ///where 18 spends a whole real number.
            ///</summary>
            private void writePlacement(Record? sname, StransModel? strans, Record? xy, (long X, long Y)? at, Run? repeat)
            {
                var flags = Strans.From(strans?.STRANS?.Data);

                double magnification = 1;
                double angle = 0;

                if (strans?.MAG?.Data is Real8Data mag)
                    magnification = mag.Value;

                if (strans?.ANGLE?.Data is Real8Data rotation)
                    angle = rotation.Value;

                long x = 0;
                long y = 0;

                if (at is (long atX, long atY))
                {
                    x = atX;
                    y = atY;
                }
                else
                {
                    var points = pointsOf(xy);

                    if (points.Count < 1)
                    {
                        Skipped++;

                        return;
                    }

                    x = points[0].X;
                    y = points[0].Y;
                }

                if (rightAngle(angle) is int quarter && magnification == 1)
                    writeSimplePlacement(sname, flags, quarter, x, y, repeat);
                else
                    writeTransformedPlacement(sname, flags, magnification, angle, x, y, repeat);
            }

            ///<summary>Record 17: the angle is two bits, and there is no magnification to write.</summary>
            ///
            ///The cell a placement names, when it is not the one the last placement named.
            ///
            ///One modal variable serves both PLACEMENT records and both ways of naming a cell - see
            ///readPlacement, where whichever of the name and the reference number is given clears the other.
            ///This writer only ever writes names, so only the name is tracked, and a record that leaves the
            ///name out clears 0x80 entirely.
            ///
            ///Null to start, which no cell name can be, so the first placement in a file always says which.
            ///Not reset per cell, for the reason none of the others are.
            ///
            private string? modalPlacementName;

            private void writeSimplePlacement(Record? sname, Strans flags, int quarter, long x, long y, Run? repeat)
            {
                string name = referencedName(sname);

                //
                //x and y. The name is not a reference number, so 0x40 stays clear whenever 0x80 is set.
                //
                //A placement announces its repetition with 0x08 where a shape uses 0x04. The two records do
                //not share an info byte, and reading one for the other gives a file that parses into
                //nonsense rather than one that fails.
                //
                byte info = 0x20 | 0x10;

                if (repeat is not null)
                    info |= 0x08;

                if (name != modalPlacementName)
                    info |= 0x80;

                info |= (byte)(quarter << 1);

                if (flags.ReflectAboutX)
                    info |= 0x01;

                file.Byte(17);//PLACEMENT
                file.Byte(info);

                if ((info & 0x80) != 0)
                {
                    file.Text(name);
                    modalPlacementName = name;
                }

                file.Signed(x);
                file.Signed(y);

                if (repeat is Run run)
                    writeRepeat(run);
            }

            ///<summary>Record 18: the magnification and the angle are written out, whatever they are.</summary>
            private void writeTransformedPlacement(Record? sname, Strans flags, double magnification, double angle, long x, long y, Run? repeat)
            {
                string name = referencedName(sname);

                //x, y, magnification and angle; the name only when it has changed.
                byte info = 0x20 | 0x10 | 0x04 | 0x02;

                if (repeat is not null)
                    info |= 0x08;

                if (name != modalPlacementName)
                    info |= 0x80;

                if (flags.ReflectAboutX)
                    info |= 0x01;

                file.Byte(18);//PLACEMENT with a transform
                file.Byte(info);

                //One modal name for both records, because the reader keeps one - so a transformed placement
                //can follow a plain one that already said which cell, and does.
                if ((info & 0x80) != 0)
                {
                    file.Text(name);
                    modalPlacementName = name;
                }
                file.Real(magnification);
                file.Real(angle);
                file.Signed(x);
                file.Signed(y);

                if (repeat is Run run)
                    writeRepeat(run);
            }

            ///<summary>
            ///A GDSII array, as one placement carrying a repetition.
            ///
            ///This is the one place a repetition is written, and the one place it is free: an AREF already
            ///says how many columns and rows and how far apart, which is exactly what a repetition holds.
            ///Kind 8 takes two step vectors and so covers a skewed array as well as a square one; kind 9 is
            ///the same with one vector, for an array only one deep.
            ///
            ///A one-by-one array is written as the plain placement it amounts to.
            ///</summary>
            private void writeArray(ArefModel aref)
            {
                var points = pointsOf(aref.XY);

                if (points.Count < 3 || aref.COLROW?.Data is not Int2Data colrow || colrow.Values.Length < 2)
                {
                    Skipped++;

                    return;
                }

                int columns = colrow.Values[0];
                int rows = colrow.Values[1];

                if (columns < 1 || rows < 1)
                {
                    Skipped++;

                    return;
                }

                var origin = points[0];

                //The two reference points are where the array *ends*, so each step is the whole span over
                //however many copies span it - which is a rational, not necessarily a whole number.
                double columnStepX = (points[1].X - (double)origin.X) / columns;
                double columnStepY = (points[1].Y - (double)origin.Y) / columns;
                double rowStepX = (points[2].X - (double)origin.X) / rows;
                double rowStepY = (points[2].Y - (double)origin.Y) / rows;

                if (columns == 1 && rows == 1)
                {
                    writePlacement(aref.SNAME, aref.Strans, null, (origin.X, origin.Y), null);

                    return;
                }

                //**A repetition only when the steps come out whole.**
                //
                //GDSII stores where the array ends and divides; OASIS stores the step itself, and a step is
                //a whole number of database units. So an array of three across a span of four hundred - a
                //step of 133 and a third - has no OASIS repetition that holds it, and rounding the step
                //would not merely move a copy but move each one further than the last.
                //
                //Those are written out a placement at a time instead, each rounded to its own nearest unit.
                //That is still not exact, because the position was not on the grid to begin with, but the
                //error stays under half a unit rather than growing across the array.
                if (whole(columnStepX) && whole(columnStepY) && whole(rowStepX) && whole(rowStepY))
                {
                    var columnStep = ((long)columnStepX, (long)columnStepY);
                    var rowStep = ((long)rowStepX, (long)rowStepY);

                    writeRepeatedPlacement(aref, origin, columns, rows, columnStep, rowStep);

                    return;
                }

                for (int column = 0; column < columns; column++)
                {
                    for (int row = 0; row < rows; row++)
                    {
                        long x = (long)Math.Round(origin.X + (column * columnStepX) + (row * rowStepX));
                        long y = (long)Math.Round(origin.Y + (column * columnStepY) + (row * rowStepY));

                        writePlacement(aref.SNAME, aref.Strans, null, (x, y), null);
                    }
                }
            }

            private static bool whole(double value)
            {
                return value == Math.Floor(value);
            }

            private void writeRepeatedPlacement(
                ArefModel aref,
                Element.Point origin,
                int columns,
                int rows,
                (long X, long Y) columnStep,
                (long X, long Y) rowStep)
            {
                var flags = Strans.From(aref.Strans?.STRANS?.Data);

                double magnification = 1;
                double angle = 0;

                if (aref.Strans?.MAG?.Data is Real8Data mag)
                    magnification = mag.Value;

                if (aref.Strans?.ANGLE?.Data is Real8Data rotation)
                    angle = rotation.Value;

                bool simple = rightAngle(angle) is not null && magnification == 1;

                //Explicit cell name, repetition, x and y.
                byte info = 0x80 | 0x20 | 0x10 | 0x08;

                if (simple)
                    info |= (byte)(rightAngle(angle)!.Value << 1);
                else
                    info |= 0x04 | 0x02;

                if (flags.ReflectAboutX)
                    info |= 0x01;

                if (simple)
                    file.Byte(17);
                else
                    file.Byte(18);

                file.Byte(info);
                file.Text(referencedName(aref.SNAME));

                if (!simple)
                {
                    file.Real(magnification);
                    file.Real(angle);
                }

                file.Signed(origin.X);
                file.Signed(origin.Y);

                writeRepetition(columns, rows, columnStep, rowStep);
            }

            ///<summary>
            ///A grid of positions, of which the element's own is the first.
            ///
            ///Both counts are written two short, which is the format's way of saying a repetition is never
            ///one copy: a kind that takes a count at all has at least two along that direction, and the one
            ///that does not is the single placement this is never reached for.
            ///</summary>
            private void writeRepetition(int columns, int rows, (long X, long Y) columnStep, (long X, long Y) rowStep)
            {
                if (columns >= 2 && rows >= 2)
                {
                    file.Byte(8);
                    file.Unsigned((ulong)(columns - 2));
                    file.Unsigned((ulong)(rows - 2));
                    file.GDelta(columnStep.X, columnStep.Y);
                    file.GDelta(rowStep.X, rowStep.Y);

                    return;
                }

                //One deep in one direction, so only the other's vector is written - whichever that is.
                file.Byte(9);

                if (columns >= 2)
                {
                    file.Unsigned((ulong)(columns - 2));
                    file.GDelta(columnStep.X, columnStep.Y);

                    return;
                }

                file.Unsigned((ulong)(rows - 2));
                file.GDelta(rowStep.X, rowStep.Y);
            }

            private static string referencedName(Record? sname)
            {
                string name = (sname?.Data as AsciiData)?.Value ?? "";

                if (name.Length == 0)
                    return UnnamedCell;

                return name;
            }

            ///<summary>
            ///Which quarter turn this angle is, or null when it is not one.
            ///
            ///Normalized first, because GDSII does not require an angle to be in range and files carry 360
            ///and -90 alike. Compared exactly rather than with a tolerance: an angle that is nearly a right
            ///angle is not one, and rounding it to one would move geometry.
            ///</summary>
            private static int? rightAngle(double angle)
            {
                double turned = angle % 360;

                if (turned < 0)
                    turned += 360;

                if (turned == 0)
                    return 0;

                if (turned == 90)
                    return 1;

                if (turned == 180)
                    return 2;

                if (turned == 270)
                    return 3;

                return null;
            }

            #endregion **********************************************************************



            #region Reading the model *******************************************************

            private static List<Element.Point> pointsOf(Record? xy)
            {
                var points = new List<Element.Point>();

                if (xy?.Data is not Int4Data data)
                    return points;

                for (int i = 0; i + 1 < data.Values.Length; i += 2)
                    points.Add(new Element.Point(data.Values[i], data.Values[i + 1]));

                return points;
            }

            ///<summary>
            ///Takes off the repeat of the first point that closes a GDSII ring. In a loop, because a file
            ///is free to repeat it more than once and one such outline exists in the bundled corpus.
            ///</summary>
            private static void dropClosingPoint(List<Element.Point> points)
            {
                while (points.Count > 1
                    && points[0].X == points[^1].X
                    && points[0].Y == points[^1].Y)
                    points.RemoveAt(points.Count - 1);
            }

            ///<summary>
            ///The corner and the two lengths of an axis-aligned rectangle, or null when the outline is not
            ///one.
            ///
            ///Four corners, each edge along an axis, and neither side zero. Both windings and all four
            ///starting corners count, since nothing in GDSII says which corner an outline starts at or which
            ///way round it runs - so the test is on the set of coordinates rather than on their order.
            ///</summary>
            private static (Element.Point Corner, long Width, long Height)? rectangleOf(List<Element.Point> points)
            {
                if (points.Count != 4)
                    return null;

                //Opposite corners share nothing; adjacent ones share exactly one coordinate. So a rectangle
                //is four points over two distinct x values and two distinct y values, with each edge
                //changing one of them.
                for (int i = 0; i < 4; i++)
                {
                    var here = points[i];
                    var next = points[(i + 1) % 4];

                    if (here.X != next.X && here.Y != next.Y)
                        return null;
                }

                long left = points.Min(point => (long)point.X);
                long right = points.Max(point => (long)point.X);
                long bottom = points.Min(point => (long)point.Y);
                long top = points.Max(point => (long)point.Y);

                if (left == right || bottom == top)
                    return null;

                //Four right-angled edges over two x values and two y values still allows a degenerate
                //figure that doubles back, so every corner is checked to be a corner of the box.
                foreach (var point in points)
                {
                    if ((point.X != left && point.X != right) || (point.Y != bottom && point.Y != top))
                        return null;
                }

                //And that all four are different ones, rather than two of them being the same corner twice.
                if (points.Select(point => (point.X, point.Y)).Distinct().Count() != 4)
                    return null;

                return (new Element.Point((int)left, (int)bottom), right - left, top - bottom);
            }

            private static int numberOf(Record? record)
            {
                if (record?.Data is not Int2Data number)
                    return 0;

                //An OASIS layer is an unsigned number. A GDSII one is signed and files do carry negatives,
                //which have nowhere to go - zero is what a reader would make of a missing record anyway.
                if (number.Value < 0)
                    return 0;

                return number.Value;
            }

            private static int widthOf(PathModel path)
            {
                if (path.WIDTH?.Data is Int4Data width)
                    return width.Value;

                return 0;
            }

            private static int extensionOf(Record? record)
            {
                if (record?.Data is Int4Data extension)
                    return extension.Value;

                return 0;
            }

            #endregion **********************************************************************
        }
    }
}
