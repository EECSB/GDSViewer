using System.Text;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    public class GDS
    {
        #region Constructor *****************************************************************

        public GDS(byte[] gdsData)
        {
            Deserialize(gdsData);
        }

        ///<summary>
        ///Reads a library from the text dump <see cref="AsText"/> writes.
        ///
        ///Deserialize(string) replaces the contents of a library that is already open, which is what the
        ///editor's save needs. This is the other case - there is nothing open yet - and without it the only
        ///route from text to a library was to parse some unrelated file first and then overwrite it.
        ///</summary>
        public static GDS FromText(string gdsAsText)
        {
            var gds = new GDS();

            gds.Deserialize(gdsAsText);

            return gds;
        }

        ///<summary>
        ///Builds a library from records that were made rather than read.
        ///
        ///The way in for <see cref="OasisReader"/>, which converts a different format into this one. The
        ///records go through the same structural pass a parsed file does, so a conversion that produced
        ///something malformed is caught here rather than three views later.
        ///</summary>
        public static GDS FromRecords(List<Record> records)
        {
            var gds = new GDS();

            gds.Records = records;

            gds.constructGDS();

            gds.AdditionalInformation = new AdditionalGDSInformation(gds);

            return gds;
        }

        ///<summary>
        ///An empty library, for <see cref="FromText"/> to fill. Private because a GDS with no records has
        ///no StreamFormat, and every public way in leaves it with one or throws.
        ///</summary>
        private GDS()
        {
        }

        ///<summary>
        ///Reads a library off a stream, a record at a time, without holding the file as bytes as well.
        ///
        ///The array overloads below want the whole file in memory before they start, which for a real
        ///layout means the bytes and the records alive at once - and in the browser a third copy again,
        ///since an uploaded file arrives as a stream that has to be drained into an array first. This
        ///reads the header, sizes the payload from it, and keeps only the record.
        ///
        ///Synchronous, for a caller reading a file off a disk. The browser needs
        ///<see cref="FromStreamAsync"/>: its file streams refuse a synchronous read outright.
        ///</summary>
        public static GDS FromStream(Stream stream)
        {
            var gds = new GDS();

            gds.Deserialize(stream);

            return gds;
        }

        ///<summary>The same, for a stream that can only be read asynchronously. See <see cref="FromStream"/>.</summary>
        public static async Task<GDS> FromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            var gds = new GDS();

            await gds.DeserializeAsync(stream, cancellationToken);

            return gds;
        }

        ///<summary>
        ///Splits the stream into records. Every length here comes out of the file itself, so each one is
        ///checked before it is trusted - otherwise a malformed file walks the cursor off the end of the
        ///array, allocates a negative-sized buffer, or stalls on a zero-length record.
        ///</summary>
        private void parseRecords(byte[] gdsData)
        {
            for (int i = 0; i < gdsData.Length; )
            {
                if (gdsData.Length - i < 4)
                    throw new InvalidDataException($"Truncated GDSII stream: {gdsData.Length - i} byte(s) left at offset {i}, too few for a record header.");

                //Read as unsigned. A record length is two bytes and legitimately exceeds a signed short
                //- the sample files reach 1548 bytes, but nothing in the format caps it there.
                int recordLength = (gdsData[i] << 8) | gdsData[i + 1];
                short recordTypeInt = (short)((gdsData[i + 2] << 8) | gdsData[i + 3]);

                checkRecordLength(recordLength, i, gdsData.Length - i);

                byte[] data = new byte[recordLength - 4];
                Array.Copy(gdsData, i + 4, data, 0, data.Length);

                Records.Add(new Record(recordTypeInt, data));

                i += recordLength;
            }
        }

        ///<summary>
        ///The same framing, off a stream. Kept beside the array version rather than folded into it: one of
        ///them has the whole file to hand and the other has a cursor it cannot rewind, and the checks they
        ///can make differ because of it - which is why what they *do* agree on is in
        ///<see cref="checkRecordLength"/> rather than written out twice.
        ///</summary>
        private void parseRecords(Stream stream)
        {
            byte[] header = new byte[RecordHeaderLength];
            long offset = 0;

            while (true)
            {
                int read = fill(stream, header, RecordHeaderLength);

                //Nothing at all, on a record boundary: the file is over. This is the only clean way out.
                if (read == 0)
                    return;

                if (read < RecordHeaderLength)
                    throw new InvalidDataException($"Truncated GDSII stream: {read} byte(s) left at offset {offset}, too few for a record header.");

                int recordLength = (header[0] << 8) | header[1];
                short recordTypeInt = (short)((header[2] << 8) | header[3]);

                checkRecordLength(recordLength, offset, null);

                byte[] data = new byte[recordLength - RecordHeaderLength];

                if (fill(stream, data, data.Length) < data.Length)
                    throw new InvalidDataException($"Truncated GDSII record at offset {offset}: it declares {recordLength} bytes and the stream ends inside it.");

                Records.Add(new Record(recordTypeInt, data));

                offset += recordLength;
            }
        }

        ///<summary>The asynchronous twin of the loop above. See <see cref="FromStreamAsync"/> for why both exist.</summary>
        private async Task parseRecordsAsync(Stream stream, CancellationToken cancellationToken)
        {
            byte[] header = new byte[RecordHeaderLength];
            long offset = 0;

            while (true)
            {
                int read = await fillAsync(stream, header, RecordHeaderLength, cancellationToken);

                if (read == 0)
                    return;

                if (read < RecordHeaderLength)
                    throw new InvalidDataException($"Truncated GDSII stream: {read} byte(s) left at offset {offset}, too few for a record header.");

                int recordLength = (header[0] << 8) | header[1];
                short recordTypeInt = (short)((header[2] << 8) | header[3]);

                checkRecordLength(recordLength, offset, null);

                byte[] data = new byte[recordLength - RecordHeaderLength];

                if (await fillAsync(stream, data, data.Length, cancellationToken) < data.Length)
                    throw new InvalidDataException($"Truncated GDSII record at offset {offset}: it declares {recordLength} bytes and the stream ends inside it.");

                Records.Add(new Record(recordTypeInt, data));

                offset += recordLength;
            }
        }

        ///<summary>
        ///What both readers agree a length has to be, in one place so their messages cannot drift apart.
        ///
        ///<paramref name="remaining"/> is null for a stream, which cannot know what is left without
        ///reading it - so that check is the one thing only the array reader can make up front.
        ///</summary>
        private static void checkRecordLength(int recordLength, long offset, long? remaining)
        {
            //The length covers the four header bytes, so anything below that is nonsense. Zero is the
            //dangerous one: the cursor would never advance and the loop would spin forever.
            if (recordLength < RecordHeaderLength)
                throw new InvalidDataException($"Invalid GDSII record at offset {offset}: its length of {recordLength} is less than the four-byte header.");

            if (remaining is long left && recordLength > left)
                throw new InvalidDataException($"Truncated GDSII record at offset {offset}: it declares {recordLength} bytes but only {left} remain.");

            //The format requires every record to be an even number of bytes - it is why an odd-length
            //string is padded with a null. Catching it here rejects a whole family of half-read
            //payloads at the point the length is read, rather than downstream where a stray byte looks
            //like a value that is one short.
            if (recordLength % 2 != 0)
                throw new InvalidDataException($"Invalid GDSII record at offset {offset}: its length of {recordLength} is odd, and every record is an even number of bytes.");
        }

        ///<summary>
        ///Reads until the buffer is full or the stream ends, and says how many bytes it got.
        ///
        ///A single Read is allowed to return fewer bytes than asked for and routinely does - a network
        ///stream hands over whatever has arrived, and the browser's file stream hands over one chunk. Read
        ///once and every record after the first short read is framed from the wrong offset, which surfaces
        ///as a corrupt file rather than as a bug here.
        ///</summary>
        private static int fill(Stream stream, byte[] buffer, int count)
        {
            int filled = 0;

            while (filled < count)
            {
                int read = stream.Read(buffer, filled, count - filled);

                if (read == 0)
                    break;

                filled += read;
            }

            return filled;
        }

        private static async Task<int> fillAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
        {
            int filled = 0;

            while (filled < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(filled, count - filled), cancellationToken);

                if (read == 0)
                    break;

                filled += read;
            }

            return filled;
        }

        ///<summary>Every record is a two-byte length, a two-byte type, and then its payload.</summary>
        private const int RecordHeaderLength = 4;

        private void constructGDS()
        {
            if (Records.Count == 0)
                throw new InvalidDataException("This file contains no GDSII records.");

            if (Records[0].Type != RecordType.HEADER)
                throw new InvalidDataException($"This does not look like a GDSII library: it starts with {Records[0].Type} rather than HEADER.");

            int i = 0;

            try
            {
                StreamFormat = new StreamFormatModel(ref i, Records);
            }
            catch (ArgumentOutOfRangeException)
            {
                //The model constructors walk forward through the record list without looking behind them,
                //so running off the end means the library was cut short - a structure or the library
                //itself is never closed.
                throw new InvalidDataException("Incomplete GDSII library: the stream ends before its structure is closed.");
            }
        }

        #endregion **************************************************************************



        #region Other ***********************************************************************

        ///<summary>
        ///Writes the record list back out as a GDSII stream. Records are emitted in order and each one
        ///recomputes its own length from its payload, so an edited value of a different size does not
        ///leave a stale length behind.
        ///</summary>
        public byte[] Serialize()
        {
            //
            //Any shape too large for one record, cut into several that fit - see Fracture.
            //
            //Here rather than at any of the four places that make boundaries, because this is where the
            //limit applies: it is a property of GDSII bytes, and nothing before this point is those. The
            //same list comes back when nothing needs cutting, which is every ordinary file, so what it
            //costs one of those is a comparison a record beside the pass below.
            //
            //**The library itself is not changed.** What is on screen stays the one shape somebody made,
            //and only the file has several - which is the honest way round, since the format is what cannot
            //hold it.
            //
            var records = Fracture.ForGdsii(Records);

            //Measured first so the whole library goes into one buffer. Growing a stream a record at a
            //time meant every record allocating its own array to be copied in, the stream reallocating as
            //it doubled, and one more full copy to get the array back out - three passes over a file that
            //can be very large, where one will do.
            int total = 0;

            foreach (var record in records)
                total += record.SerializedLength;

            var stream = new byte[total];
            int offset = 0;

            foreach (var record in records)
                offset += record.WriteTo(stream, offset);

            return stream;
        }

        public void Deserialize(byte[] gdsData)
        {
            Records = new List<Record>();

            parseRecords(gdsData);
            constructGDS();

            AdditionalInformation = new AdditionalGDSInformation(this);
        }

        ///<summary>
        ///Replaces this library with what the stream holds. Not all or nothing, matching the array
        ///overload rather than the text one: both are reached with nothing yet open, where the text
        ///overload is reached from a save with a file already on screen to protect.
        ///</summary>
        public void Deserialize(Stream stream)
        {
            Records = new List<Record>();

            parseRecords(buffered(stream));
            constructGDS();

            AdditionalInformation = new AdditionalGDSInformation(this);
        }

        public async Task DeserializeAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            Records = new List<Record>();

            await parseRecordsAsync(buffered(stream), cancellationToken);
            constructGDS();

            AdditionalInformation = new AdditionalGDSInformation(this);
        }

        ///<summary>
        ///Puts a buffer in front of anything that is not already memory.
        ///
        ///This reads a four-byte header and then a payload of a hundred or so, which against a raw file
        ///handle or a network stream is two round trips per record and tens of thousands of them for one
        ///layout. A MemoryStream is already the buffer, so wrapping it would only add a copy.
        ///</summary>
        private static Stream buffered(Stream stream)
        {
            if (stream is MemoryStream)
                return stream;

            return new BufferedStream(stream, ReadBufferSize);
        }

        ///<summary>Enough to hold a run of records, so the reads underneath are not per-record.</summary>
        private const int ReadBufferSize = 64 * 1024;

        ///<summary>
        ///Reads the file back from the dump AsText writes, which is what makes an edit in the text view
        ///stick. See <see cref="TextFormat"/> for the format.
        ///
        ///All or nothing, unlike the byte overload: this one is reached from a save button, so a text that
        ///will not read leaves the loaded file exactly as it was rather than half replaced. Parsing the
        ///records happens before anything is assigned, and the structural pass is rolled back if it fails.
        ///</summary>
        public void Deserialize(string gdsAsText)
        {
            var parsed = TextFormat.ParseRecords(gdsAsText);

            var previousRecords = Records;
            var previousStreamFormat = StreamFormat;
            var previousInformation = AdditionalInformation;

            Records = parsed;

            try
            {
                constructGDS();

                AdditionalInformation = new AdditionalGDSInformation(this);
            }
            catch
            {
                Records = previousRecords;
                StreamFormat = previousStreamFormat;
                AdditionalInformation = previousInformation;

                throw;
            }
        }

        public string AsText()
        {
            var builder = new StringBuilder();

            foreach (var record in Records)
            {
                builder.Append(record.Type.ToString());
                builder.Append(": ");

                //Each payload knows how it prints; a record with none appends nothing.
                record.Data?.AppendText(builder);

                builder.Append(" \n");
            }

            return builder.ToString();
        }

        #endregion **************************************************************************



        #region Properties ******************************************************************

        ///<summary>The flat record list: what the file is, before any structure is read into it.</summary>
        public List<Record> Records { get; set; } = new List<Record>();

        ///<summary>
        ///The tree built over the records, and the layers discovered while walking it.
        ///
        ///Both are assigned by Deserialize, which every constructor path calls. The compiler cannot see
        ///through that, so they are declared non-nullable and initialized null-forgivingly rather than
        ///made nullable - a nullable type here would push a null test onto every caller to describe a
        ///state that a constructed GDS is never in.
        ///</summary>
        public StreamFormatModel StreamFormat { get; set; } = null!;
        public AdditionalGDSInformation AdditionalInformation { get; set; } = null!;


        #endregion **************************************************************************



        #region Reading the record list *****************************************************

        //The models below walk the flat record list with a shared cursor, taking each record by position.
        //These three are how they say what they expect at that position, so that a file whose records are
        //missing or out of order is named as such instead of being quietly read into the wrong fields -
        //which used to leave the cursor sliding and surface as "the stream ends before its structure is
        //closed", true of where it stopped and no use at all for finding the record at fault.
        //
        //Private to GDS, which the nested models can still reach, so this stays out of the public surface.

        ///<summary>Takes the record at the cursor, which has to be of the type named.</summary>
        private static Record take(ref int i, List<Record> records, RecordType expected)
        {
            //No article before the record name on purpose - "a" or "an" depends on the name, and the names
            //come from an enum.
            if (i >= records.Count)
                throw new InvalidDataException($"Incomplete GDSII library: {expected} was expected after record {records.Count}, but the stream ends there.");

            if (records[i].Type != expected)
                throw new InvalidDataException($"Record {i + 1} is {records[i].Type} where {expected} was expected: the records are either missing one or out of order.");

            var record = records[i];
            i++;

            return record;
        }

        ///<summary>
        ///Whether the cursor is on a record of this type, for the optional ones. False at the end of the
        ///list rather than throwing, so a truncated file is reported by whatever required record follows.
        ///</summary>
        private static bool next(int i, List<Record> records, RecordType type)
        {
            return i < records.Count && records[i].Type == type;
        }

        ///<summary>
        ///An XY, which is a list of coordinate pairs - so an odd number of values means one is unpaired.
        ///Checked here because it is the one payload whose shape carries structural meaning: read past, a
        ///stray coordinate leaves a point short of what any outline needs and the element silently stops
        ///being drawn.
        ///
        ///**Consecutive XY records are one list.** A record carries its own length in two bytes, so it holds
        ///at most 8,191 points - and a writer with a larger shape to write either cuts it into several
        ///elements or splits its points across several XY records. This used to refuse the second kind, and
        ///the refusal read as `XY where ENDEL was expected`, which says nothing about the actual problem.
        ///
        ///It was refused for a reason that has since gone. Reading a shape this app could not then *write*
        ///would have meant drawing something it refused to save, and one set of rules for both directions
        ///is the rule here. <see cref="Fracture"/> answered that: a shape too large for one record is cut
        ///into several boundaries on the way out, so a file like this now opens and saves.
        ///
        ///Joined in place, replacing the run in the record list. The models are built over that same list,
        ///so a model holding a record the list does not have would leave the text view and the edit path
        ///describing something other than what is drawn.
        ///</summary>
        private static Record takeXy(ref int i, List<Record> records, RecordType element)
        {
            int position = i + 1;
            var record = take(ref i, records, RecordType.XY);

            record = joinedWithFollowing(ref i, records, record);

            //No payload at all reads as no coordinates, which the minimum below then reports.
            int[] coordinates = Array.Empty<int>();

            if (record.Data is Int4Data xy)
                coordinates = xy.Values;

            if (coordinates.Length % 2 != 0)
                throw new InvalidDataException($"Record {position} is an XY holding {coordinates.Length} coordinates, which cannot be read as (x, y) pairs - one is unpaired.");

            int pairs = coordinates.Length / 2;
            var shape = geometryOf(element);

            if (pairs < shape.MinimumPairs)
                throw new InvalidDataException($"Record {position} is the XY of a {element}, which needs at least {shape.MinimumPairs} coordinate pairs, but it holds {pairs}.");

            if (shape.MustBeClosed && !isClosed(coordinates))
                throw new InvalidDataException($"Record {position} is the XY of a {element}, which has to close on the point it starts from - it runs ({coordinates[0]}, {coordinates[1]}) to ({coordinates[coordinates.Length - 2]}, {coordinates[coordinates.Length - 1]}).");

            return record;
        }

        ///
        ///One XY carrying the coordinates of the run of them starting here, or the one given when it is alone.
        ///
        ///
        ///**The ordinary file is not touched, and that early return is not a micro-optimization.** One XY
        ///followed by anything else comes straight back, which is every element of every file in the corpus.
        ///
        ///Without it the parse goes quadratic. Joining splices the record list - a RemoveRange and an Insert -
        ///and both are O(n) on a List of a million records, so doing it once per element is O(n squared) on
        ///top of re-encoding every payload that never needed it. Measured on a 200,000-shape layout through
        ///`gds bench`: **292 ms with the early return and 37,738 ms without it**, which is not a difference
        ///anything else here would have noticed - every test passes either way.
        ///
        ///
        ///The joined record replaces the whole run in the list rather than being handed back beside it, so
        ///what is written out later is what was read: one XY of every point, which <see cref="Fracture"/>
        ///cuts into elements that fit if it has to. Reading a split shape and writing it back split would
        ///need the writer to reproduce the split, and nothing here remembers where it was.
        ///
        private static Record joinedWithFollowing(ref int i, List<Record> records, Record first)
        {
            int at = i - 1;
            int last = at;

            while (last + 1 < records.Count && records[last + 1].Type == RecordType.XY)
                last++;

            if (last == at)
                return first;

            var coordinates = new List<int>();

            for (int each = at; each <= last; each++)
            {
                if (records[each].Data is Int4Data part)
                    coordinates.AddRange(part.Values);
            }

            var joined = new Record((short)RecordType.XY, new Int4Data(coordinates.ToArray()).Encode());

            records.RemoveRange(at, (last - at) + 1);
            records.Insert(at, joined);

            i = at + 1;

            return joined;
        }

        ///<summary>
        ///The shape an element's coordinate list has to have.
        ///
        ///Minimums and closure only - **no upper bounds**. The format's tables cap a boundary at 200
        ///pairs, but that limit belongs to an era this app does not live in: modern writers go far past
        ///it, the bundled cells already reach 193, and refusing a file for being detailed would be a
        ///worse failure than the one being prevented.
        ///
        ///Checked against the corpus before being written down: of 112544 boundaries not one is unclosed
        ///and the smallest holds 5 pairs, paths run 2 to 4, and SREF and TEXT carry exactly 1. BOX, NODE
        ///and AREF appear in no bundled file, so those three are the format's word alone - which is why
        ///they are given the loosest reading that still means anything.
        ///</summary>
        private static (int MinimumPairs, bool MustBeClosed) geometryOf(RecordType element)
        {
            //A closed polygon: at least three corners plus the repeat of the first.
            if (element == RecordType.BOUNDARY)
                return (4, true);

            //A centerline needs two ends to have a direction.
            if (element == RecordType.PATH)
                return (2, false);

            //Four corners and the repeat. The format says exactly five; a larger one is still readable,
            //so only the floor is enforced.
            if (element == RecordType.BOX)
                return (5, true);

            //The origin and the far end of each of the two runs.
            if (element == RecordType.AREF)
                return (3, false);

            //NODE, TEXT and SREF: one point is the least that locates anything.
            return (1, false);
        }

        ///<summary>Whether a coordinate list ends where it began.</summary>
        private static bool isClosed(int[] coordinates)
        {
            if (coordinates.Length < 4)
                return false;

            return coordinates[0] == coordinates[coordinates.Length - 2]
                && coordinates[1] == coordinates[coordinates.Length - 1];
        }

        #endregion **************************************************************************



        #region Models **********************************************************************

        public class StreamFormatModel
        {
            public StreamFormatModel(ref int i, List<Record> records)
            {
                HEADER = take(ref i, records, RecordType.HEADER);
                BGNLIB = take(ref i, records, RecordType.BGNLIB);
                LIBNAME = take(ref i, records, RecordType.LIBNAME);

                if (next(i, records, RecordType.REFLIBS))
                    REFLIBS = take(ref i, records, RecordType.REFLIBS);

                if (next(i, records, RecordType.FONTS))
                    FONTS = take(ref i, records, RecordType.FONTS);

                if (next(i, records, RecordType.ATTRTABLE))
                    ATTRTABLE = take(ref i, records, RecordType.ATTRTABLE);

                if (next(i, records, RecordType.GENERATIONS))
                    GENERATIONS = take(ref i, records, RecordType.GENERATIONS);

                if (next(i, records, RecordType.FORMAT))
                    FormatType = new FormatTypeModel(ref i, records);

                UNITS = take(ref i, records, RecordType.UNITS);

                while (next(i, records, RecordType.BGNSTR))
                    Structures.Add(new StructureModel(ref i, records));

                ENDLIB = take(ref i, records, RecordType.ENDLIB);
            }

            public Record HEADER { get; set; }
            public Record BGNLIB { get; set; }
            public Record LIBNAME { get; set; }
            //Optional library records, so genuinely null when the file omits them - which the sample files
            //all do.
            public Record? REFLIBS { get; set; }
            public Record? FONTS { get; set; }
            public Record? ATTRTABLE { get; set; }
            public Record? GENERATIONS { get; set; }
            public FormatTypeModel? FormatType { get; set; }
            public Record UNITS { get; set; }
            public List<StructureModel> Structures { get; set; } = new List<StructureModel>();
            public Record ENDLIB { get; set; }
        }

        public class FormatTypeModel
        {
            public FormatTypeModel(ref int i, List<Record> records)
            {
                FORMAT = take(ref i, records, RecordType.FORMAT);

                while (next(i, records, RecordType.MASK))
                    MASKS.Add(take(ref i, records, RecordType.MASK));

                //ENDMASKS only follows MASK records, so a filtered format has neither.
                if (next(i, records, RecordType.ENDMASKS))
                    ENDMASKS = take(ref i, records, RecordType.ENDMASKS);
            }

            public Record FORMAT { get; set; }
            public List<Record> MASKS { get; set; } = new List<Record>();
            //Only present alongside MASK records, which a filtered format does not carry.
            public Record? ENDMASKS { get; set; }
        }

        public class StructureModel
        {
            public StructureModel(ref int i, List<Record> records)
            {
                BGNSTR = take(ref i, records, RecordType.BGNSTR);
                STRNAME = take(ref i, records, RecordType.STRNAME);

                if (next(i, records, RecordType.STRCLASS))
                    STRCLASS = take(ref i, records, RecordType.STRCLASS);

                while (i < records.Count && ElementModel.IsElementRecord(records[i].Type))
                    Elements.Add(new ElementModel(ref i, records));

                ENDSTR = take(ref i, records, RecordType.ENDSTR);
            }

            public Record BGNSTR { get; set; }
            public Record STRNAME { get; set; }
            //Optional, and reserved for a CALMA-internal use nothing here reads.
            public Record? STRCLASS { get; set; }
            public List<ElementModel> Elements { get; set; } = new List<ElementModel>();
            public Record ENDSTR { get; set; }
        }

        public class ElementModel
        {
            public ElementModel(ref int i, List<Record> records)
            {
                if (i >= records.Count)
                    throw new InvalidDataException($"Incomplete GDSII library: an element was expected after record {records.Count}, but the stream ends there.");

                switch (records[i].Type)
                {
                    case RecordType.BOUNDARY:
                        Element = new BoundaryModel(ref i, records);
                        break;
                    case RecordType.PATH:
                        Element = new PathModel(ref i, records);
                        break;
                    case RecordType.SREF:
                        Element = new SrefModel(ref i, records);
                        break;
                    case RecordType.AREF:
                        Element = new ArefModel(ref i, records);
                        break;
                    case RecordType.TEXT:
                        Element = new TextModel(ref i, records);
                        break;
                    case RecordType.NODE:
                        Element = new NodeModel(ref i, records);
                        break;                   
                    case RecordType.BOX:
                        Element = new BoxModel(ref i, records);
                        break;
                    
                    //Unreachable from StructureModel, which only enters here on IsElementRecord - but this
                    //is a public constructor, so it says what it will not read rather than throwing a bare
                    //Exception the upload path cannot tell from a bug.
                    default:
                        throw new InvalidDataException($"Record {i + 1} is {records[i].Type}, which does not start an element.");
                }

                while (next(i, records, RecordType.PROPATTR))
                {
                    int position = i + 1;

                    var property = new PropertyModel(ref i, records);

                    requireUnusedAttribute(property, Properties, position);

                    Properties.Add(property);
                }

                ENDEL = take(ref i, records, RecordType.ENDEL);
            }


            ///<summary>
            ///An attribute number identifies a property within its element, so an element cannot carry the
            ///same one twice - that is two values for one name, and nothing can say which is meant.
            ///
            ///This is the one rule about properties that is worth enforcing. Pairing is already covered,
            ///since PropertyModel takes a PROPVALUE straight after its PROPATTR. The other rule the format
            ///states is an upper bound - 128 bytes of property data per element, 512 for SREF, AREF, NODE
            ///and BOX - and upper bounds are not enforced anywhere here, for the same reason the 200-pair
            ///boundary cap is not.
            ///
            ///No bundled file carries a property at all, so unlike the geometry rules this one cannot be
            ///checked against the corpus. It is written the loosest way that still means something: a
            ///repeat is refused, and nothing is said about which numbers or how many.
            ///</summary>
            private static void requireUnusedAttribute(PropertyModel property, List<PropertyModel> alreadyRead, int position)
            {
                if (property.PROPATTR.Data is not Int2Data attribute)
                    return;

                foreach (var read in alreadyRead)
                {
                    if (read.PROPATTR.Data is Int2Data other && other.Value == attribute.Value)
                        throw new InvalidDataException($"Record {position} is a second PROPATTR {attribute.Value} on the same element, and an attribute number identifies a property within its element.");
                }
            }


            public static bool IsElementRecord(RecordType type)
            {
                switch (type)
                {
                    case RecordType.BOUNDARY:
                    case RecordType.PATH:
                    case RecordType.SREF:
                    case RecordType.AREF:
                    case RecordType.TEXT:
                    case RecordType.NODE:
                    case RecordType.BOX:
                        return true;

                    default:
                        return false;
                }
            }


            public List<PropertyModel> Properties { get; set; } = new List<PropertyModel>();
            public ElementType Element { get; set; } = null!;
            public Record ENDEL { get; set; }
        }

        public interface IHasLayer
        {
            public Record LAYER { get; set; }

            ///<summary>
            ///The record saying what the shape on that layer is *for*, which the format spells differently
            ///for each element: DATATYPE on a BOUNDARY and a PATH, TEXTTYPE on a TEXT, BOXTYPE on a BOX,
            ///NODETYPE on a NODE. All four are the same field as far as anything reading a file cares -
            ///the second half of the layer/datatype pair - and this is how to get at it without knowing
            ///which element is in hand.
            ///
            ///Get-only, and each model returns the record it already holds rather than storing a second
            ///copy. The constructors assign the concrete record, so there is nothing here to leave unset.
            ///</summary>
            public Record? DataTypeRecord { get; }
        }

        ///<summary>
        ///What every element has in common: the two optional records the format allows on all seven of
        ///them, and the coordinates.
        ///
        ///A derived model must NOT redeclare ELFLAGS or PLEX. Doing so hides these, and since the
        ///constructors assign whichever is nearest in scope, the derived property gets the record and
        ///these stay null forever - so anything reading them through an ElementType reference, which is
        ///how the flattener and the views see an element, silently gets nothing.
        ///</summary>
        public class ElementType
        {
            //Optional on all seven element types, and absent from every element in the sample files.
            public Record? ELFLAGS { get; set; }
            public Record? PLEX { get; set; }

            ///<summary>
            ///The format requires XY on all seven element types and every parsing constructor assigns it,
            ///so it is not nullable - but it cannot be assigned here, because TextModel overrides it to
            ///read through to its text body.
            ///</summary>
            public virtual Record XY { get; set; } = null!;

            ///<summary>
            ///The record that starts this element - BOUNDARY, PATH, SREF and so on.
            ///
            ///Each subclass has it under its own name, which is right for reading one and useless for
            ///anything that has to treat all seven alike. Removing an element from a library needs the
            ///span its records occupy, and this is where that span begins; <see cref="ElementModel.ENDEL"/>
            ///is where it ends.
            ///</summary>
            public virtual Record Opening { get; set; } = null!;
        }

        public class BoundaryModel : ElementType, IHasLayer
        {
            public BoundaryModel(ref int i, List<Record> records)
            {
                BOUNDARY = take(ref i, records, RecordType.BOUNDARY);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = BOUNDARY;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                LAYER = take(ref i, records, RecordType.LAYER);
                DATATYPE = take(ref i, records, RecordType.DATATYPE);
                XY = takeXy(ref i, records, RecordType.BOUNDARY);
            }

            public Record BOUNDARY { get; set; }
            public Record LAYER { get; set; }
            public Record DATATYPE { get; set; }

            public Record? DataTypeRecord
            {
                get { return DATATYPE; }
            }
        }

        public class PathModel : ElementType, IHasLayer
        {
            public PathModel(ref int i, List<Record> records)
            {
                PATH = take(ref i, records, RecordType.PATH);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = PATH;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                LAYER = take(ref i, records, RecordType.LAYER);
                DATATYPE = take(ref i, records, RecordType.DATATYPE);

                if (next(i, records, RecordType.PATHTYPE))
                    PATHTYPE = take(ref i, records, RecordType.PATHTYPE);

                if (next(i, records, RecordType.WIDTH))
                    WIDTH = take(ref i, records, RecordType.WIDTH);

                //Only meaningful for PATHTYPE 4, where they replace the half-width a type 2 path extends by.
                if (next(i, records, RecordType.BGNEXTN))
                    BGNEXTN = take(ref i, records, RecordType.BGNEXTN);

                if (next(i, records, RecordType.ENDEXTN))
                    ENDEXTN = take(ref i, records, RecordType.ENDEXTN);

                XY = takeXy(ref i, records, RecordType.PATH);
            }

            public Record PATH { get; set; }
            public Record LAYER { get; set; }
            public Record DATATYPE { get; set; }
            //Optional records, so genuinely null when the file omits them - which is the common case.
            public Record? PATHTYPE { get; set; }
            public Record? WIDTH { get; set; }
            public Record? BGNEXTN { get; set; }
            public Record? ENDEXTN { get; set; }

            public Record? DataTypeRecord
            {
                get { return DATATYPE; }
            }
        }

        public class SrefModel : ElementType
        {
            public SrefModel(ref int i, List<Record> records)
            {
                SREF = take(ref i, records, RecordType.SREF);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = SREF;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                SNAME = take(ref i, records, RecordType.SNAME);

                if (next(i, records, RecordType.STRANS))
                    Strans = new StransModel(ref i, records);

                XY = takeXy(ref i, records, RecordType.SREF);
            }

            public Record SREF { get; set; }
            //Only present when the placement is transformed at all, so an unrotated instance has none.
            public StransModel? Strans { get; set; }
            public Record SNAME { get; set; }
        }

        public class ArefModel : ElementType
        {
            public ArefModel(ref int i, List<Record> records)
            {
                AREF = take(ref i, records, RecordType.AREF);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = AREF;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                SNAME = take(ref i, records, RecordType.SNAME);

                if (next(i, records, RecordType.STRANS))
                    Strans = new StransModel(ref i, records);

                COLROW = take(ref i, records, RecordType.COLROW);
                XY = takeXy(ref i, records, RecordType.AREF);
            }

            public Record AREF { get; set; }
            public Record SNAME { get; set; }
            public StransModel? Strans { get; set; }
            public Record COLROW { get; set; }
        }

        public class TextModel : ElementType, IHasLayer
        {
            public TextModel(ref int i, List<Record> records)
            {
                TEXT = take(ref i, records, RecordType.TEXT);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = TEXT;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                LAYER = take(ref i, records, RecordType.LAYER);

                TextBody = new TextBodyModel(ref i, records);
            }

            public Record TEXT { get; set; }
            public Record LAYER { get; set; }
            public TextBodyModel TextBody { get; set; }

            ///<summary>A TEXT element's coordinates live in its text body, not directly on the element.</summary>
            public override Record XY
            {
                get { return TextBody.XY; }
                set { TextBody.XY = value; }
            }

            ///<summary>And so does its TEXTTYPE, which is this element's half of the layer/datatype pair.</summary>
            public Record? DataTypeRecord
            {
                get { return TextBody.TEXTTYPE; }
            }
        }

        public class NodeModel : ElementType, IHasLayer
        {
            public NodeModel(ref int i, List<Record> records)
            {
                NODE = take(ref i, records, RecordType.NODE);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = NODE;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                LAYER = take(ref i, records, RecordType.LAYER);
                NODETYPE = take(ref i, records, RecordType.NODETYPE);
                XY = takeXy(ref i, records, RecordType.NODE);
            }

            public Record NODE { get; set; }
            public Record LAYER { get; set; }
            public Record NODETYPE { get; set; }

            public Record? DataTypeRecord
            {
                get { return NODETYPE; }
            }
        }

        public class BoxModel : ElementType, IHasLayer
        {
            public BoxModel(ref int i, List<Record> records)
            {
                BOX = take(ref i, records, RecordType.BOX);

                //The one record every element type has under a different name; see ElementType.Opening.
                Opening = BOX;

                if (next(i, records, RecordType.ELFLAGS))
                    ELFLAGS = take(ref i, records, RecordType.ELFLAGS);

                if (next(i, records, RecordType.PLEX))
                    PLEX = take(ref i, records, RecordType.PLEX);

                LAYER = take(ref i, records, RecordType.LAYER);
                BOXTYPE = take(ref i, records, RecordType.BOXTYPE);
                XY = takeXy(ref i, records, RecordType.BOX);
            }

            public Record BOX { get; set; }
            public Record LAYER { get; set; }
            public Record BOXTYPE { get; set; }

            public Record? DataTypeRecord
            {
                get { return BOXTYPE; }
            }
        }

        public class TextBodyModel
        {
            public TextBodyModel(ref int i, List<Record> records)
            {
                TEXTTYPE = take(ref i, records, RecordType.TEXTTYPE);

                if (next(i, records, RecordType.PRESENTATION))
                    PRESENTATION = take(ref i, records, RecordType.PRESENTATION);

                if (next(i, records, RecordType.PATHTYPE))
                    PATHTYPE = take(ref i, records, RecordType.PATHTYPE);

                if (next(i, records, RecordType.WIDTH))
                    WIDTH = take(ref i, records, RecordType.WIDTH);

                if (next(i, records, RecordType.STRANS))
                    Strans = new StransModel(ref i, records);

                XY = takeXy(ref i, records, RecordType.TEXT);
                STRING = take(ref i, records, RecordType.STRING);
            }

            public Record TEXTTYPE { get; set; }
            //All optional. PRESENTATION carries the justification and font; the rest describe how a mask
            //writer should stroke the glyphs, which this app does not do.
            public Record? PRESENTATION { get; set; }
            public Record? PATHTYPE { get; set; }
            public Record? WIDTH { get; set; }
            public StransModel? Strans { get; set; }
            public Record XY { get; set; }
            public Record STRING { get; set; }
        }

        public class StransModel
        {
            public StransModel(ref int i, List<Record> records)
            {
                STRANS = take(ref i, records, RecordType.STRANS);

                if (next(i, records, RecordType.MAG))
                    MAG = take(ref i, records, RecordType.MAG);

                if (next(i, records, RecordType.ANGLE))
                    ANGLE = take(ref i, records, RecordType.ANGLE);
            }

            public Record STRANS { get; set; }
            //Optional, and default to 1 and 0 when the file leaves them out.
            public Record? MAG { get; set; }
            public Record? ANGLE { get; set; }
        }

        public class PropertyModel
        {
            public PropertyModel(ref int i, List<Record> records)
            {
                PROPATTR = take(ref i, records, RecordType.PROPATTR);
                PROPVALUE = take(ref i, records, RecordType.PROPVALUE);
            }

            public Record PROPATTR { get; set; }

            public Record PROPVALUE { get; set; }
        }



        public class Record
        {
            #region Constructor *****************************************************************

            ///<summary>
            ///Builds a record from its type word and payload. The stream's length is not kept: Serialize
            ///recomputes it from the payload, so there is nothing to hold that could go stale.
            ///</summary>
            public Record(short type, byte[] data)
            {
                Type = (RecordType)type;

                setData(data);
            }

            #endregion **************************************************************************



            #region Properties ******************************************************************

            ///<summary>
            ///The decoded payload, or null when the record carries none. One of the RecordData subclasses,
            ///which is what makes the payload's shape checkable rather than something callers have to
            ///know from the record type.
            ///</summary>
            public RecordData? Data { get; set; }

            ///<summary>
            ///Set only by BGNLIB and BGNSTR, whose twelve INT2 values are two timestamps: the last
            ///modification and the last access.
            ///</summary>
            public (DateTime Modified, DateTime Accessed)? Timestamps { get; set; }

            ///
            ///True when a year had to be guessed at to produce those.
            ///
            ///**Because the file is genuinely ambiguous and the answer is presented as though it were not.**
            ///Three conventions are in circulation for the year field and nothing in the record says which
            ///one is in use, so a small year is *interpreted* - see <see cref="toFullYear"/>. That is right
            ///for every file anyone is likely to open and it is still a guess, and a date reported without
            ///saying so is a date somebody may go on to rely on.
            ///
            ///False for a year written in full, which is most files and all 896 sky130 cells: nothing was
            ///decided, so there is nothing to disclose.
            ///
            public bool YearWasInferred { get; set; }

            public RecordType Type { get; set; }

            ///<summary>
            ///The data type of the payload. Derived from the payload itself where there is one, so the two
            ///cannot disagree; declaredDataType covers the case of a record that names a type and then
            ///carries nothing.
            ///</summary>
            public RecordDataType DataType
            {
                get
                {
                    if (Data is not null)
                        return Data.Type;

                    return declaredDataType;
                }
            }

            ///<summary>What the record's type word says it holds, before the payload is looked at.</summary>
            private RecordDataType declaredDataType;

            #endregion **************************************************************************



            #region Serialization ***************************************************************

            ///<summary>The total size of this record on disk: the four-byte header plus its payload.</summary>
            public int SerializedLength
            {
                get { return 4 + (Data?.EncodedLength ?? 0); }
            }

            ///
            ///The most a record can be, header included.
            ///
            ///A record begins with its own total length in a **two-byte** field, so 65535 is the largest
            ///number that field can hold. For an XY, at two four-byte coordinates a point, that is 8191
            ///points - which is the ceiling on how many corners one element can have in a GDSII file.
            ///
            public const int MostBytes = 65535;

            ///
            ///That length, or a refusal - because the alternative is a file that is wrong and says nothing.
            ///
            ///**This is the one place a size can go wrong silently.** The length is written as two bytes
            ///taken off an int, so a record over the ceiling had its length written *modulo 65536*: a
            ///plausible small number, in a file whose bytes are all there. Every record after it is then
            ///framed from the wrong offset, so what the next reader finds is not a large element it cannot
            ///handle but garbage part way through the file - and nothing at any point said so. SerializedLength
            ///is an int and does not wrap, so the buffer is even the right size; only the field lies.
            ///
            ///Thrown rather than split. Splitting an element across consecutive XY records is what a writer
            ///that meets this limit normally does, and it is the one thing this reader refuses to read back
            ///(see ToleranceTests) - so writing one would make a file this app cannot open. One set of rules
            ///for both directions is the rule here, and the honest way to hold it is to say what cannot be
            ///written rather than to write something unreadable.
            ///
            private int lengthOrRefuse(int payloadLength)
            {
                int length = payloadLength + 4;

                if (length > MostBytes)
                    throw new InvalidDataException($"A {Type} record of {length} bytes cannot be written: a GDSII record carries its own length in two bytes, so {MostBytes} is the most one can be - which for an element's coordinates is {(MostBytes - 4) / 8} points. Split the shape into smaller ones.");

                return length;
            }

            ///<summary>
            ///Writes this record into <paramref name="buffer"/> at <paramref name="offset"/> and returns
            ///how many bytes that took, so a caller writing a whole library can fill one buffer in a
            ///single pass rather than concatenating a record at a time.
            ///</summary>
            public int WriteTo(byte[] buffer, int offset)
            {
                byte[] payload = Data?.Encode() ?? Array.Empty<byte>();
                int length = lengthOrRefuse(payload.Length);

                buffer[offset] = (byte)(length >> 8);
                buffer[offset + 1] = (byte)length;
                buffer[offset + 2] = (byte)((short)Type >> 8);
                buffer[offset + 3] = (byte)(short)Type;

                payload.CopyTo(buffer, offset + 4);

                return length;
            }

            ///<summary>
            ///Emits this record as GDSII bytes: a big-endian total length, the packed type/data-type
            ///word, then the payload. The length is derived from the payload rather than remembered
            ///from the file it was read out of.
            ///</summary>
            public byte[] Serialize()
            {
                //Each payload knows how it encodes; a record with none contributes only its header.
                byte[] payload = Data?.Encode() ?? Array.Empty<byte>();

                //The length field counts the four header bytes as well as the payload.
                int length = lengthOrRefuse(payload.Length);

                var record = new byte[length];

                record[0] = (byte)(length >> 8);
                record[1] = (byte)length;
                record[2] = (byte)((short)Type >> 8);
                record[3] = (byte)(short)Type;

                payload.CopyTo(record, 4);

                return record;
            }

            #endregion **************************************************************************



            #region Private Methods *************************************************************

            ///<summary>
            ///Builds the payload object for this record's declared data type. Each RecordData subclass
            ///owns its own decoding, so this only chooses which one applies.
            ///</summary>
            private RecordData? convertData(byte[] data)
            {
                //A record can declare a data type and still carry nothing - the payload size is never
                //checked against the type. Treat that as absent rather than reading off an empty array.
                if (data.Length == 0)
                    return null;

                switch (declaredDataType)
                {
                    case RecordDataType.BITARRAY:
                        return new BitArrayData(data);

                    case RecordDataType.INT2:
                        return Int2Data.Decode(data);

                    case RecordDataType.INT4:
                        return Int4Data.Decode(data);

                    case RecordDataType.REAL8:
                        return Real8Data.Decode(data);

                    case RecordDataType.ASCII:
                        return AsciiData.Decode(data);

                    default:
                        //NODATA carrying data anyway, REAL4 - which no record type declares - or a type
                        //code out of a malformed type word. Keeping the bytes means the record still
                        //writes back out unchanged instead of being silently emptied.
                        return new RawData(declaredDataType, data);
                }
            }

            ///<summary>
            ///Decodes the payload. The data type is not chosen here: a record type word states it in its
            ///low byte - LAYER is 0x0D02, type 0x0D carrying INT2 - so it is derived from the type itself,
            ///which stops the two from drifting apart.
            ///</summary>
            private void setData(byte[] data)
            {
                declaredDataType = (RecordDataType)((short)Type & 0xFF);

                Data = convertData(data);

                //BGNLIB and BGNSTR carry twelve INT2 values that are really two timestamps.
                if (Type == RecordType.BGNLIB || Type == RecordType.BGNSTR)
                {
                    if (Data is Int2Data stamps && stamps.Values.Length >= 12)
                    {
                        Timestamps = toTimestampPair(stamps.Values);

                        //Either stamp having had its year worked out is enough to say the pair was.
                        YearWasInferred = Timestamps is not null && (wasInferred(stamps.Values[0]) || wasInferred(stamps.Values[6]));
                    }
                }
            }

            ///<summary>
            ///Two (year, month, day, hour, minute, second) stamps: the last modification followed by the
            ///last access.
            ///
            ///Null when the twelve values do not describe two real dates. Files carry zeroed and otherwise
            ///impossible stamps - a year of 0, a month of 13, the 30th of February - and every one of them
            ///used to throw out of the constructor and take the whole parse with it, so a layout that was
            ///perfectly readable would not open because of a field nothing here draws from.
            ///
            ///Only this convenience reading is withheld. The raw INT2 values are untouched, so the record
            ///still writes back out exactly as it came in and the text dump still reports what the file
            ///said.
            ///</summary>
            private static (DateTime Modified, DateTime Accessed)? toTimestampPair(short[] values)
            {
                DateTime? modified = toDateTime(values, 0);
                DateTime? accessed = toDateTime(values, 6);

                //The pair is one field of the record, so half of it is not useful on its own.
                if (modified is null || accessed is null)
                    return null;

                return (modified.Value, accessed.Value);
            }

            ///<summary>
            ///Six consecutive values as a date, or null if they are not one. Caught rather than checked:
            ///reproducing which days each month has, leap years included, would be a second implementation
            ///of a rule DateTime already owns.
            ///</summary>
            private static DateTime? toDateTime(short[] values, int offset)
            {
                try
                {
                    return new DateTime(
                        toFullYear(values[offset]),
                        values[offset + 1],
                        values[offset + 2],
                        values[offset + 3],
                        values[offset + 4],
                        values[offset + 5]);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }

            ///<summary>
            ///GDSII writers disagree about the year field, and there are three conventions in circulation:
            ///the full year, which the 896 sky130 cells write as 2019; years since 1900, the C tm_year
            ///convention, which Mosfet.gds writes as 122 and 123 for 2022 and 2023; and a bare two-digit
            ///year. Read literally, the second kind dates a 2022 file to the year 122.
            ///
            ///So a year under 50 is read as a two-digit 2000s year and anything else below 1000 as an
            ///offset from 1900. Both are heuristics, not rules: nothing in the record distinguishes "2022,
            ///written the old way" from "the year 122". They are applied because every file that uses small
            ///years means one of the two, and a viewer reporting 122 AD is wrong in a way nobody wants.
            ///
            ///The 50 is KLayout's cut, taken from its own reader (get_time in dbGDS2Reader.cc) rather than
            ///invented here - it reads a year under 50 as 2000s, under 1900 as an offset from 1900. That
            ///branch is the one this code was missing: a file stamped 24 read as 1924 where every other
            ///tool says 2024. It costs nothing to agree, since GDSII did not exist in the 1920s, so a small
            ///two-digit year cannot honestly mean the century it names.
            ///
            ///1000 rather than 100 as the outer cut: 122 has to be caught, and no convention produces a
            ///year between 200 and 999. Negative is left alone - it is corruption under either reading, and
            ///shifting it would turn nonsense into a plausible 19th-century date.
            ///
            ///Only the derived DateTime is affected. The raw value stays in the payload, so the record
            ///writes back out unchanged and the text dump still shows what the file said.
            ///</summary>
            ///<summary>Whether toFullYear would change this year, which is the whole of what is being guessed.</summary>
            private static bool wasInferred(short year)
            {
                return toFullYear(year) != year;
            }

            private static int toFullYear(short year)
            {
                if (year < 0)
                    return year;

                if (year < 50)
                    return year + 2000;

                if (year < 1000)
                    return year + 1900;

                return year;
            }

            #endregion **************************************************************************



            #region Models **********************************************************************

            public enum RecordDataType
            {
                NODATA = 0,
                BITARRAY = 1,
                INT2 = 2,
                INT4 = 3,
                REAL4 = 4,//Not used
                REAL8 = 5,
                ASCII = 6
            }

            public enum RecordType
            {
                HEADER = 0x0002,
                BGNLIB = 0x0102,
                LIBNAME = 0x0206,
                UNITS = 0x0305,
                ENDLIB = 0x0400,
                BGNSTR = 0x0502,
                STRNAME = 0x0606,
                ENDSTR = 0x0700,
                BOUNDARY = 0x0800,
                PATH = 0x0900,
                SREF = 0x0A00,
                AREF = 0x0B00,
                TEXT = 0x0C00,
                LAYER = 0x0D02,
                DATATYPE = 0x0E02,
                WIDTH = 0x0F03,
                XY = 0x1003,
                ENDEL = 0x1100,
                SNAME = 0x1206,
                COLROW = 0x1302,
                TEXTNODE = 0x1400,
                NODE = 0x1500,
                TEXTTYPE = 0x1602,
                PRESENTATION = 0x1701,
                //SPACING = 0x18??
                STRING = 0x1906,
                STRANS = 0x1A01,
                MAG = 0x1B05,
                ANGLE = 0x1C05,
                //UINTEGER = 0x1D??
                //USTRING = 0x1E??
                REFLIBS = 0x1F06,
                FONTS = 0x2006,
                PATHTYPE = 0x2102,
                GENERATIONS = 0x2202,
                ATTRTABLE = 0x2306,
                //STYPTABLE = 0x2406,//Unreleased feature
                //STRTYPE = 0x2502,//Unreleased feature
                ELFLAGS = 0x2601,
                //ELKEY = 0x2703,//Unreleased feature
                //LINKTYPE = 0x28,//Unreleased feature
                //LINKKEYS = 0x29,//Unreleased feature
                NODETYPE = 0x2A02,
                PROPATTR = 0x2B02,
                PROPVALUE = 0x2C06,
                BOX = 0x2D00,
                BOXTYPE = 0x2E02,
                PLEX = 0x2F03,
                BGNEXTN = 0x3003,
                ENDEXTN = 0x3103,
                TAPENUM = 0x3202,
                TAPECODE = 0x3302,
                STRCLASS = 0x3401,
                RESERVED = 0x3503, 
                FORMAT = 0x3602,
                MASK = 0x3706,
                ENDMASKS = 0x3800,
                LIBDIRSIZE = 0x3902,
                SRFNAME = 0x3A06,
                LIBSECUR = 0x3B02,

                //Types used only with Custom Plus
                BORDER = 0x3C00,
                SOFTFENCE = 0x3D00,
                HARDFENCE = 0x3E00,
                SOFTWIRE = 0x3F00,
                HARDWIRE = 0x4000,
                PATHPORT = 0x4100,
                NODEPORT = 0x4200,
                USERCONSTRAINT = 0x4300,
                SPACERERROR = 0x4400,
                CONTACT = 0x4500
            }

            #endregion **************************************************************************
        }

        #endregion **************************************************************************
    }
}
