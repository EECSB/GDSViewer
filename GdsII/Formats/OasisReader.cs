using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///Reads OASIS (SEMI P39), the format that was meant to replace GDSII.
    ///
    ///**It is turned into a GDSII library rather than modeled separately.** Everything downstream - the
    ///structural model, the flattener, both views, the layer sidebar, the text editor, the exporters -
    ///speaks GDSII records, and a second model beside it would mean a second copy of all of that. So an
    ///OASIS file is read into the same record list a .gds would have produced, which also means opening one
    ///and saving it writes a valid GDSII file: the conversion is free.
    ///
    ///**Reading only.** Writing OASIS is a bigger job than reading it and a different one - a writer has to
    ///choose which of the eleven repetition forms and six point-list forms says a thing most compactly, and
    ///getting that wrong costs file size rather than correctness, so it is the half where the format earns
    ///its keep and the half worth doing carefully rather than quickly.
    ///
    ///**What it costs to convert.** A few OASIS ideas have no GDSII spelling and are expanded on the way
    ///through: a repetition becomes one element per position, a CTRAPEZOID or a CIRCLE becomes an ordinary
    ///boundary. That is lossless for what is drawn and lossy for how it was written down, which is the right
    ///trade for a viewer - and the same one every GDSII exporter makes.
    ///
    ///Coordinates are **not** scaled. OASIS integers are already in database units, the same as GDSII's, so
    ///the only thing the file's unit decides is what goes in the UNITS record.
    ///</summary>
    public static class OasisReader
    {
        #region Constants *******************************************************************

        ///<summary>What every OASIS file starts with, before the START record.</summary>
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("%SEMI-OASIS\r\n");

        ///<summary>
        ///The GDSII version written into the HEADER of what comes out. 600 is release 6, which is what the
        ///sample corpus carries and what every current tool reads.
        ///</summary>
        private const short GdsVersion = 600;

        ///<summary>
        ///The library name given to the converted file. OASIS has no equivalent - a GDSII library is named
        ///and an OASIS file is not - so something has to be invented, and this is what gdstk invents too.
        ///</summary>
        private const string LibraryName = "LIB";

        ///<summary>
        ///How many positions one repetition is allowed to expand to.
        ///
        ///A repetition is one record however many copies it stands for, and the format puts no limit on
        ///that - a fill pattern can be millions. Expanding one of those into millions of GDSII elements
        ///would take the memory of the machine rather than fail, so it is refused with something to read
        ///instead.
        ///</summary>
        private const int MaximumRepetition = 2_000_000;

        ///<summary>
        ///How many segments a CIRCLE is drawn with. GDSII has no circle, so one becomes a polygon, and this
        ///is the number KLayout uses when it converts the same way.
        ///</summary>
        private const int CircleSegments = 64;

        #endregion **************************************************************************



        #region Reading *********************************************************************

        ///<summary>
        ///Whether these opening bytes are an OASIS file.
        ///
        ///By what the file says it is rather than by what it is called: the two formats are told apart at
        ///the front - "%SEMI-OASIS" against a GDSII HEADER - and an extension is a guess about a file that
        ///the file itself has already answered.
        ///</summary>
        public static bool LooksLikeOasis(ReadOnlySpan<byte> start)
        {
            if (start.Length < Magic.Length)
                return false;

            return start[..Magic.Length].SequenceEqual(Magic);
        }

        public static GDS Read(byte[] bytes)
        {
            return GDS.FromRecords(new Reader(bytes).ToRecords());
        }

        ///<summary>
        ///Reads a whole stream and then parses it.
        ///
        ///Unlike the GDSII reader, this one cannot work off a cursor that only goes forwards: a CBLOCK
        ///holds a compressed run of records whose length is known in bytes, and reading it means taking
        ///that many and then carrying on where they ended. So the stream is drained first, and the saving
        ///the GDSII reader makes by not doing that is not available here.
        ///</summary>
        public static GDS Read(Stream stream)
        {
            using var buffer = new MemoryStream();

            stream.CopyTo(buffer);

            return Read(buffer.ToArray());
        }

        ///<summary>
        ///The same, for a stream that can only be read asynchronously.
        ///
        ///Which the browser's is: a Blazor WASM file stream throws on a synchronous read outright. Without
        ///this an uploaded OASIS file was refused by that exception rather than by anything about the
        ///file - and because the shell names a file before it parses it, what was left on screen was the
        ///*previous* layout under the new file's name.
        ///</summary>
        public static async Task<GDS> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();

            await stream.CopyToAsync(buffer, cancellationToken);

            return Read(buffer.ToArray());
        }

        #endregion **************************************************************************



        #region The byte level **************************************************************

        ///<summary>
        ///A cursor over the file, and over a CBLOCK's contents while one is being read.
        ///
        ///A CBLOCK is a deflated run of ordinary records. When one is met the cursor moves into the
        ///inflated bytes and comes back out where the compressed ones ended, so nothing above this has to
        ///know whether a record arrived compressed. The format does not allow one inside another, which is
        ///why one level is enough.
        ///</summary>
        private sealed class Cursor
        {
            private byte[] buffer;
            private int at;

            private byte[]? outer;
            private int outerAt;

            public Cursor(byte[] bytes)
            {
                buffer = bytes;
                at = 0;
            }

            public bool AtEnd
            {
                get { return at >= buffer.Length && outer is null; }
            }

            public byte ReadByte()
            {
                while (at >= buffer.Length)
                {
                    if (outer is null)
                        throw new InvalidDataException("Truncated OASIS file: it ends in the middle of a record.");

                    buffer = outer;
                    at = outerAt;
                    outer = null;
                }

                return buffer[at++];
            }

            public byte Peek()
            {
                byte value = ReadByte();

                at--;

                return value;
            }

            public byte[] ReadBytes(int count)
            {
                var taken = new byte[count];

                for (int i = 0; i < count; i++)
                    taken[i] = ReadByte();

                return taken;
            }

            ///<summary>
            ///Moves into a run of compressed records. The count is of compressed bytes, which is what says
            ///where to carry on afterwards.
            ///</summary>
            public void EnterCompressed(byte[] inflated, int compressedBytes)
            {
                //Taken from the outer buffer directly rather than through ReadByte, since a CBLOCK's
                //payload is never itself inside a CBLOCK.
                if (at + compressedBytes > buffer.Length)
                    throw new InvalidDataException("Truncated OASIS file: a compressed block runs past the end of it.");

                outer = buffer;
                outerAt = at + compressedBytes;

                buffer = inflated;
                at = 0;
            }

            ///<summary>The compressed bytes themselves, without moving on.</summary>
            public byte[] PeekRaw(int count)
            {
                if (at + count > buffer.Length)
                    throw new InvalidDataException("Truncated OASIS file: a compressed block runs past the end of it.");

                return buffer[at..(at + count)];
            }

            ///<summary>
            ///An unsigned integer, seven bits per byte, low group first, the top bit saying more follows.
            ///</summary>
            public ulong ReadUnsigned()
            {
                byte b = ReadByte();
                ulong value = (ulong)(b & 0x7F);
                int shift = 7;

                while ((b & 0x80) != 0)
                {
                    b = ReadByte();

                    if (shift > 63)
                        throw new InvalidDataException("Invalid OASIS file: an integer runs past what 64 bits can hold.");

                    value |= (ulong)(b & 0x7F) << shift;
                    shift += 7;
                }

                return value;
            }

            ///<summary>
            ///The same, with the lowest few bits of the first byte carrying something else - a sign, or a
            ///direction. Hands back those bits, and the number without them.
            ///
            ///Every signed quantity in the format is this shape, which is why it is one function: an
            ///integer skips one bit, a two-delta skips two, a three-delta three, a g-delta four.
            ///</summary>
            public byte ReadPacked(int skipBits, out long value)
            {
                byte b = ReadByte();

                value = (long)((ulong)(b & 0x7F) >> skipBits);

                byte carried = (byte)(b & ((1 << skipBits) - 1));
                int shift = 7 - skipBits;

                while ((b & 0x80) != 0)
                {
                    b = ReadByte();

                    if (shift > 56)
                        throw new InvalidDataException("Invalid OASIS file: an integer runs past what 64 bits can hold.");

                    value |= (long)((ulong)(b & 0x7F) << shift);
                    shift += 7;
                }

                return carried;
            }

            public long ReadSigned()
            {
                if (ReadPacked(1, out long value) > 0)
                    return -value;

                return value;
            }

            public string ReadString()
            {
                int length = checked((int)ReadUnsigned());

                //Latin-1 rather than UTF-8: the format's strings are bytes, and every byte is a character
                //in this encoding - so nothing is lost and nothing throws on a byte no UTF-8 sequence
                //could have produced.
                return Encoding.Latin1.GetString(ReadBytes(length));
            }

            public double ReadReal()
            {
                return ReadRealOfType(ReadByte());
            }

            ///<summary>The eight ways the format writes a number that is not a whole one.</summary>
            public double ReadRealOfType(byte type)
            {
                switch (type)
                {
                    case 0: return ReadUnsigned();
                    case 1: return -(double)ReadUnsigned();
                    case 2: return 1.0 / ReadUnsigned();
                    case 3: return -1.0 / ReadUnsigned();
                    case 4: return (double)ReadUnsigned() / ReadUnsigned();
                    case 5: return -((double)ReadUnsigned() / ReadUnsigned());
                    case 6: return BinaryPrimitives.ReadSingleLittleEndian(ReadBytes(4));
                    case 7: return BinaryPrimitives.ReadDoubleLittleEndian(ReadBytes(8));
                }

                throw new InvalidDataException($"Invalid OASIS file: {type} is not one of the eight kinds of number.");
            }

            ///<summary>A step along one of the four compass directions.</summary>
            public (long X, long Y) ReadDelta2()
            {
                byte direction = ReadPacked(2, out long value);

                return alongDirection(direction, value);
            }

            ///<summary>A step along one of eight - the four, and the four diagonals.</summary>
            public (long X, long Y) ReadDelta3()
            {
                byte direction = ReadPacked(3, out long value);

                return alongDirection(direction, value);
            }

            ///<summary>
            ///A step anywhere. Two shapes: one of the eight directions again when the lowest bit is clear,
            ///and an arbitrary pair when it is set.
            ///</summary>
            public (long X, long Y) ReadGDelta()
            {
                if ((Peek() & 0x01) == 0)
                {
                    byte direction = (byte)(ReadPacked(4, out long value) >> 1);

                    return alongDirection(direction, value);
                }

                long x;
                long y;

                if ((ReadPacked(2, out x) & 0x02) > 0)
                    x = -x;

                if ((ReadPacked(1, out y) & 0x01) > 0)
                    y = -y;

                return (x, y);
            }

            private static (long X, long Y) alongDirection(byte direction, long value)
            {
                switch (direction)
                {
                    case 0: return (value, 0);
                    case 1: return (0, value);
                    case 2: return (-value, 0);
                    case 3: return (0, -value);
                    case 4: return (value, value);
                    case 5: return (-value, value);
                    case 6: return (-value, -value);
                    case 7: return (value, -value);
                }

                throw new InvalidDataException($"Invalid OASIS file: {direction} is not a direction.");
            }
        }

        #endregion **************************************************************************



        #region What a file holds, before it is GDSII ***************************************

        private sealed class Shape
        {
            public int Layer;
            public int DataType;
            public List<Element.Point> Points = new List<Element.Point>();

            ///<summary>Null for a boundary. A path carries a width and how its ends are finished.</summary>
            public PathEnds? Path;

            ///<summary>Set for a label rather than an outline.</summary>
            public string? Text;
            public long? TextReference;
        }

        private sealed class PathEnds
        {
            public int HalfWidth;
            public long StartExtension;
            public long EndExtension;
        }

        private sealed class Placement
        {
            public string? Name;
            public long? NameReference;
            public Element.Point At;
            public double Magnification = 1;
            public double Angle;
            public bool Flipped;
        }

        private sealed class OasisCell
        {
            public string? Name;
            public long? NameReference;
            public List<Shape> Shapes = new List<Shape>();
            public List<Placement> Placements = new List<Placement>();
        }

        #endregion **************************************************************************



        #region The record loop *************************************************************

        ///<summary>
        ///One pass over the file, holding the modal state the format leaves out of each record.
        ///
        ///**Almost every field is optional.** A record's first byte says which of its fields are present,
        ///and the ones that are not are whatever the last record set - so a run of rectangles on one layer
        ///writes the layer once. That is where the format's compactness comes from and where a reader gets
        ///it wrong quietly: miss one and every element after it lands on the wrong layer, at the wrong
        ///place, or both.
        ///</summary>
        private sealed class Reader
        {
            private readonly Cursor cursor;

            private readonly List<OasisCell> cells = new List<OasisCell>();
            private readonly Dictionary<long, string> cellNames = new Dictionary<long, string>();
            private readonly Dictionary<long, string> textStrings = new Dictionary<long, string>();

            private long implicitCellName;
            private long implicitTextString;

            private OasisCell? cell;

            private double unit = 1000;

            //The modal variables, in the order the specification lists them.
            private bool absolutePosition = true;
            private int layer;
            private int dataType;
            private int textLayer;
            private int textType;
            private long geometryX;
            private long geometryY;
            private long textX;
            private long textY;
            private long placementX;
            private long placementY;
            private long width;
            private long height;
            private int pathHalfWidth;
            private long pathStartExtension;
            private long pathEndExtension;
            private List<Element.Point> polygonPoints = new List<Element.Point>();
            private List<Element.Point> pathPoints = new List<Element.Point>();
            private List<(long X, long Y)> repetition = new List<(long, long)>();
            private string? lastText;
            private long? lastTextReference;
            private string? lastPlacementName;
            private long? lastPlacementReference;

            public Reader(byte[] bytes)
            {
                if (!LooksLikeOasis(bytes))
                    throw new InvalidDataException("This is not an OASIS file: it does not start with %SEMI-OASIS.");

                cursor = new Cursor(bytes);

                cursor.ReadBytes(Magic.Length);
            }

            public List<Record> ToRecords()
            {
                readStart();

                while (!cursor.AtEnd)
                {
                    byte record = cursor.ReadByte();

                    if (record == 2)
                        break;

                    readRecord(record);
                }

                return emit();
            }

            private void readStart()
            {
                if (cursor.ReadByte() != 1)
                    throw new InvalidDataException("Invalid OASIS file: it does not begin with a START record.");

                string version = cursor.ReadString();

                if (version != "1.0")
                    throw new InvalidDataException($"Unsupported OASIS version \"{version}\". This reads version 1.0.");

                unit = cursor.ReadReal();

                if (unit <= 0)
                    throw new InvalidDataException($"Invalid OASIS file: its unit of {unit} is not a positive number.");

                //Zero means the table offsets are here rather than in the END record. They are skipped
                //either way: they are an index into a file this reads from front to back.
                if (cursor.ReadUnsigned() == 0)
                {
                    for (int i = 0; i < 12; i++)
                        cursor.ReadUnsigned();
                }
            }

            private void readRecord(byte record)
            {
                switch (record)
                {
                    case 0: return;//PAD

                    case 3: cellNames[implicitCellName++] = cursor.ReadString(); return;
                    case 4: readNamed(cellNames); return;
                    case 5: textStrings[implicitTextString++] = cursor.ReadString(); return;
                    case 6: readNamed(textStrings); return;

                    //The property and extension name tables. Read past rather than kept: nothing here
                    //draws a property, and skipping the payload is what keeps the cursor aligned.
                    case 7: cursor.ReadString(); return;
                    case 8: cursor.ReadString(); cursor.ReadUnsigned(); return;
                    case 9: cursor.ReadString(); return;
                    case 10: cursor.ReadString(); cursor.ReadUnsigned(); return;
                    case 30: cursor.ReadUnsigned(); cursor.ReadString(); return;
                    case 31: cursor.ReadUnsigned(); cursor.ReadString(); cursor.ReadUnsigned(); return;

                    case 11:
                    case 12: readLayerName(); return;

                    case 13: startCell(cursor.ReadUnsigned()); return;
                    case 14: startCell(cursor.ReadString()); return;

                    case 15: absolutePosition = true; return;
                    case 16: absolutePosition = false; return;

                    case 17:
                    case 18: readPlacement(record == 18); return;

                    case 19: readText(); return;
                    case 20: readRectangle(); return;
                    case 21: readPolygon(); return;
                    case 22: readPath(); return;

                    case 23:
                    case 24:
                    case 25: readTrapezoid(record); return;

                    case 26: readCTrapezoid(); return;
                    case 27: readCircle(); return;

                    case 28:
                    case 29: readProperty(record == 29); return;

                    case 32: readExtensionElement(); return;
                    case 33: readExtensionGeometry(); return;

                    case 34: readCompressedBlock(); return;
                }

                throw new InvalidDataException($"Invalid OASIS file: {record} is not a record type.");
            }

            private void readNamed(Dictionary<long, string> table)
            {
                string name = cursor.ReadString();

                table[(long)cursor.ReadUnsigned()] = name;
            }

            ///<summary>
            ///A layer's name, and the layer numbers it covers. Read past: a name here is for a *range* of
            ///layer/datatype pairs rather than for one, and GDSII has nowhere to put either.
            ///</summary>
            private void readLayerName()
            {
                cursor.ReadString();

                skipInterval();
                skipInterval();
            }

            private void skipInterval()
            {
                switch (cursor.ReadUnsigned())
                {
                    case 0: return;
                    case 1:
                    case 2:
                    case 3: cursor.ReadUnsigned(); return;
                    case 4: cursor.ReadUnsigned(); cursor.ReadUnsigned(); return;
                }

                throw new InvalidDataException("Invalid OASIS file: a layer name gives a range this does not recognize.");
            }

            private void startCell(string name)
            {
                cell = new OasisCell { Name = name };

                cells.Add(cell);

                resetCellState();
            }

            private void startCell(ulong reference)
            {
                cell = new OasisCell { NameReference = (long)reference };

                cells.Add(cell);

                resetCellState();
            }

            ///<summary>
            ///What a new cell resets. Deliberately not everything: the format keeps the layer, the sizes
            ///and the point lists across a cell boundary, and only the positions and the addressing mode
            ///go back to where they started.
            ///</summary>
            private void resetCellState()
            {
                absolutePosition = true;

                geometryX = 0;
                geometryY = 0;
                textX = 0;
                textY = 0;
                placementX = 0;
                placementY = 0;
            }

            #endregion **************************************************************************



            #region The elements ****************************************************************

            private void readPlacement(bool transformed)
            {
                byte info = cursor.ReadByte();

                var placement = new Placement();

                if ((info & 0x80) != 0)
                {
                    if ((info & 0x40) != 0)
                        lastPlacementReference = (long)cursor.ReadUnsigned();
                    else
                        lastPlacementName = cursor.ReadString();

                    //Whichever was given replaces the other, so a later placement without a name follows
                    //the one that was actually last rather than the last of its own kind.
                    if ((info & 0x40) != 0)
                        lastPlacementName = null;
                    else
                        lastPlacementReference = null;
                }

                placement.Name = lastPlacementName;
                placement.NameReference = lastPlacementReference;

                if (transformed)
                {
                    if ((info & 0x04) != 0)
                        placement.Magnification = cursor.ReadReal();

                    if ((info & 0x02) != 0)
                        placement.Angle = cursor.ReadReal();
                }
                else
                {
                    placement.Angle = ((info & 0x06) >> 1) * 90.0;
                }

                placement.Flipped = (info & 0x01) != 0;

                if ((info & 0x20) != 0)
                    placementX = moved(placementX, cursor.ReadSigned());

                if ((info & 0x10) != 0)
                    placementY = moved(placementY, cursor.ReadSigned());

                placement.At = point(placementX, placementY);

                foreach (var offset in repetitionFor((info & 0x08) != 0))
                {
                    currentCell().Placements.Add(new Placement
                    {
                        Name = placement.Name,
                        NameReference = placement.NameReference,
                        At = point(placementX + offset.X, placementY + offset.Y),
                        Magnification = placement.Magnification,
                        Angle = placement.Angle,
                        Flipped = placement.Flipped
                    });
                }
            }

            private void readText()
            {
                byte info = cursor.ReadByte();

                if ((info & 0x40) != 0)
                {
                    if ((info & 0x20) != 0)
                    {
                        lastTextReference = (long)cursor.ReadUnsigned();
                        lastText = null;
                    }
                    else
                    {
                        lastText = cursor.ReadString();
                        lastTextReference = null;
                    }
                }

                if ((info & 0x01) != 0)
                    textLayer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    textType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x10) != 0)
                    textX = moved(textX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    textY = moved(textY, cursor.ReadSigned());

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                {
                    currentCell().Shapes.Add(new Shape
                    {
                        Layer = textLayer,
                        DataType = textType,
                        Text = lastText,
                        TextReference = lastTextReference,
                        Points = { point(textX + offset.X, textY + offset.Y) }
                    });
                }
            }

            private void readRectangle()
            {
                byte info = cursor.ReadByte();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x40) != 0)
                    width = (long)cursor.ReadUnsigned();

                if ((info & 0x20) != 0)
                    height = (long)cursor.ReadUnsigned();
                else if ((info & 0x80) != 0)
                    height = width;//A square says its width once

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                {
                    long x = geometryX + offset.X;
                    long y = geometryY + offset.Y;

                    addBoundary(new List<Element.Point>
                    {
                        point(x, y),
                        point(x + width, y),
                        point(x + width, y + height),
                        point(x, y + height)
                    });
                }
            }

            private void readPolygon()
            {
                byte info = cursor.ReadByte();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x20) != 0)
                    polygonPoints = readPointList(closed: true);

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                    addBoundary(shifted(polygonPoints, geometryX + offset.X, geometryY + offset.Y));
            }

            private void readPath()
            {
                byte info = cursor.ReadByte();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x40) != 0)
                    pathHalfWidth = checked((int)cursor.ReadUnsigned());

                if ((info & 0x80) != 0)
                    readPathExtensions();

                if ((info & 0x20) != 0)
                    pathPoints = readPointList(closed: false);

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                {
                    currentCell().Shapes.Add(new Shape
                    {
                        Layer = layer,
                        DataType = dataType,
                        Points = shifted(pathPoints, geometryX + offset.X, geometryY + offset.Y),
                        Path = new PathEnds
                        {
                            HalfWidth = pathHalfWidth,
                            StartExtension = pathStartExtension,
                            EndExtension = pathEndExtension
                        }
                    });
                }
            }

            ///<summary>
            ///How a path's two ends are finished. Two bits each: leave it alone, cut it flush, carry it a
            ///half-width past the last point, or carry it a distance that follows.
            ///</summary>
            private void readPathExtensions()
            {
                byte scheme = cursor.ReadByte();

                switch (scheme & 0x0C)
                {
                    case 0x04: pathStartExtension = 0; break;
                    case 0x08: pathStartExtension = pathHalfWidth; break;
                    case 0x0C: pathStartExtension = cursor.ReadSigned(); break;
                }

                switch (scheme & 0x03)
                {
                    case 0x01: pathEndExtension = 0; break;
                    case 0x02: pathEndExtension = pathHalfWidth; break;
                    case 0x03: pathEndExtension = cursor.ReadSigned(); break;
                }
            }

            ///<summary>
            ///A trapezoid, given as a box and how far its corners are pulled in.
            ///
            ///Whether the slanted sides are the vertical or the horizontal pair is one bit of the info
            ///byte; the two deltas are how far the ends of those sides move.
            ///</summary>
            private void readTrapezoid(byte record)
            {
                byte info = cursor.ReadByte();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x40) != 0)
                    width = (long)cursor.ReadUnsigned();

                if ((info & 0x20) != 0)
                    height = (long)cursor.ReadUnsigned();

                long a = 0;
                long b = 0;

                if (record == 23)
                {
                    a = cursor.ReadSigned();
                    b = cursor.ReadSigned();
                }
                else if (record == 24)
                {
                    a = cursor.ReadSigned();
                }
                else
                {
                    b = cursor.ReadSigned();
                }

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                bool vertical = (info & 0x80) != 0;

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                {
                    long x = geometryX + offset.X;
                    long y = geometryY + offset.Y;

                    addBoundary(trapezoidCorners(x, y, width, height, a, b, vertical));
                }
            }

            ///<summary>
            ///The four corners of a trapezoid.
            ///
            ///**The two deltas move opposite *edges*, not opposite sides.** With the parallel pair vertical,
            ///a moves both ends of the bottom edge and b both ends of the top; laid the other way it is the
            ///left edge and the right. Reading it as "a is the left side and b is the right" gives a shape
            ///of the correct size, in the correct place, sheared the wrong way - which is the kind of wrong
            ///that looks like a trapezoid.
            ///
            ///The sign says which end moves: a positive delta lifts the near corner and a negative one
            ///drops the far one, so the box is never left.
            ///</summary>
            private static List<Element.Point> trapezoidCorners(long x, long y, long w, long h, long a, long b, bool vertical)
            {
                if (vertical)
                {
                    return new List<Element.Point>
                    {
                        point(x, y + Math.Max(a, 0)),
                        point(x, y + h + Math.Min(b, 0)),
                        point(x + w, y + h - Math.Max(b, 0)),
                        point(x + w, y - Math.Min(a, 0))
                    };
                }

                return new List<Element.Point>
                {
                    point(x + Math.Max(a, 0), y + h),
                    point(x + w + Math.Min(b, 0), y + h),
                    point(x + w - Math.Max(b, 0), y),
                    point(x - Math.Min(a, 0), y)
                };
            }

            ///<summary>
            ///One of twenty-six named trapezoid shapes, each a box with one or two corners cut at
            ///forty-five degrees. The type says which; the box says how big.
            ///</summary>
            private void readCTrapezoid()
            {
                byte info = cursor.ReadByte();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x80) != 0)
                    ctrapezoidType = cursor.ReadByte();

                if ((info & 0x40) != 0)
                    width = (long)cursor.ReadUnsigned();

                if ((info & 0x20) != 0)
                    height = (long)cursor.ReadUnsigned();

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                    addBoundary(cTrapezoidCorners(geometryX + offset.X, geometryY + offset.Y));
            }

            private byte ctrapezoidType;

            ///<summary>
            ///The corners of one of the twenty-six named shapes.
            ///
            ///Written as the specification's own table rather than as a general trapezoid, which is what
            ///this was: the sixteen four-sided ones are a box with one or two corners slid along by the
            ///*other* dimension, and eight of the rest are triangles that do not have four corners at all.
            ///Sixteen through twenty-three also **set** the dimension they derive, because a later record
            ///that leaves one out inherits what this left behind.
            ///</summary>
            private List<Element.Point> cTrapezoidCorners(long x, long y)
            {
                long w = width;
                long h = height;

                //Sixteen to twenty-three are triangles, all three corners starting at the box's origin.
                if (ctrapezoidType > 15 && ctrapezoidType < 24)
                {
                    long[] tx = { x, x, x };
                    long[] ty = { y, y, y };

                    switch (ctrapezoidType)
                    {
                        case 16: tx[1] += w; ty[2] += w; height = w; break;
                        case 17: tx[1] += w; ty[1] += w; ty[2] += w; height = w; break;
                        case 18: tx[1] += w; tx[2] += w; ty[2] += w; height = w; break;
                        case 19: tx[0] += w; tx[1] += w; ty[1] += w; ty[2] += w; height = w; break;
                        case 20: tx[1] += 2 * h; tx[2] += h; ty[2] += h; width = 2 * h; break;
                        case 21: tx[0] += h; tx[1] += 2 * h; ty[1] += h; ty[2] += h; width = 2 * h; break;
                        case 22: tx[1] += w; ty[1] += w; ty[2] += 2 * w; height = 2 * w; break;
                        case 23: tx[0] += w; tx[1] += w; ty[1] += 2 * w; ty[2] += w; height = 2 * w; break;
                    }

                    return new List<Element.Point> { point(tx[0], ty[0]), point(tx[1], ty[1]), point(tx[2], ty[2]) };
                }

                //The rest start as the box itself, counter-clockwise from its bottom-left corner.
                long[] cx = { x, x + w, x + w, x };
                long[] cy = { y, y, y + h, y + h };

                switch (ctrapezoidType)
                {
                    case 0: cx[2] -= h; break;
                    case 1: cx[1] -= h; break;
                    case 2: cx[3] += h; break;
                    case 3: cx[0] += h; break;
                    case 4: cx[2] -= h; cx[3] += h; break;
                    case 5: cx[0] += h; cx[1] -= h; break;
                    case 6: cx[1] -= h; cx[3] += h; break;
                    case 7: cx[0] += h; cx[2] -= h; break;

                    case 8: cy[2] -= w; break;
                    case 9: cy[3] -= w; break;
                    case 10: cy[1] += w; break;
                    case 11: cy[0] += w; break;
                    case 12: cy[1] += w; cy[2] -= w; break;
                    case 13: cy[0] += w; cy[3] -= w; break;
                    case 14: cy[1] += w; cy[3] -= w; break;
                    case 15: cy[0] += w; cy[2] -= w; break;

                    //Twenty-four is the box itself. Twenty-five is the square on its width, which is the
                    //one shape that carries a single dimension.
                    case 24: break;
                    case 25: cy[2] = y + w; cy[3] = y + w; break;

                    default:
                        throw new InvalidDataException($"Invalid OASIS file: {ctrapezoidType} is not one of the named trapezoids.");
                }

                return new List<Element.Point>
                {
                    point(cx[0], cy[0]),
                    point(cx[1], cy[1]),
                    point(cx[2], cy[2]),
                    point(cx[3], cy[3])
                };
            }

            ///<summary>
            ///A circle, which GDSII has no word for - so it becomes a polygon, the way every tool that
            ///writes GDSII out of OASIS does it.
            ///</summary>
            private void readCircle()
            {
                byte info = cursor.ReadByte();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                if ((info & 0x20) != 0)
                    circleRadius = (long)cursor.ReadUnsigned();

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                foreach (var offset in repetitionFor((info & 0x04) != 0))
                    addBoundary(circleCorners(geometryX + offset.X, geometryY + offset.Y, circleRadius));
            }

            private long circleRadius;

            private static List<Element.Point> circleCorners(long x, long y, long radius)
            {
                var points = new List<Element.Point>(CircleSegments);

                for (int i = 0; i < CircleSegments; i++)
                {
                    double angle = 2 * Math.PI * i / CircleSegments;

                    points.Add(point(x + (long)Math.Round(radius * Math.Cos(angle)), y + (long)Math.Round(radius * Math.Sin(angle))));
                }

                return points;
            }

            ///<summary>
            ///A property on whatever came last. Read past: nothing downstream carries an OASIS property,
            ///and the point of reading it is to leave the cursor where the next record starts.
            ///</summary>
            private void readProperty(bool repeatLast)
            {
                if (repeatLast)
                    return;

                byte info = cursor.ReadByte();

                if ((info & 0x04) != 0)
                {
                    if ((info & 0x02) != 0)
                        cursor.ReadUnsigned();
                    else
                        cursor.ReadString();
                }

                if ((info & 0x08) != 0)
                    return;

                ulong values = (ulong)(info >> 4);

                if (values == 15)
                    values = cursor.ReadUnsigned();

                for (ulong i = 0; i < values; i++)
                    skipPropertyValue();
            }

            private void skipPropertyValue()
            {
                byte type = cursor.ReadByte();

                switch (type)
                {
                    case 8:
                    case 9:
                    {
                        //Nine is the signed form, and its sign is the low bit of the packed value - so one bit
                        //is skipped where eight skips none. Skipped rather than read, since this is the path
                        //that throws a property away.
                        int signBits = 0;

                        if (type == 9)
                            signBits = 1;

                        cursor.ReadPacked(signBits, out _);

                        return;
                    }
                    case 10:
                    case 11:
                    case 12: cursor.ReadString(); return;
                    case 13:
                    case 14:
                    case 15: cursor.ReadUnsigned(); return;
                }

                cursor.ReadRealOfType(type);
            }

            private void readExtensionElement()
            {
                cursor.ReadUnsigned();
                cursor.ReadString();
            }

            private void readExtensionGeometry()
            {
                byte info = cursor.ReadByte();

                cursor.ReadUnsigned();

                if ((info & 0x01) != 0)
                    layer = checked((int)cursor.ReadUnsigned());

                if ((info & 0x02) != 0)
                    dataType = checked((int)cursor.ReadUnsigned());

                cursor.ReadString();

                if ((info & 0x10) != 0)
                    geometryX = moved(geometryX, cursor.ReadSigned());

                if ((info & 0x08) != 0)
                    geometryY = moved(geometryY, cursor.ReadSigned());

                repetitionFor((info & 0x04) != 0);
            }

            ///<summary>
            ///A run of ordinary records, deflated. The cursor moves into them and comes back out where
            ///they ended, so nothing above here knows the difference.
            ///</summary>
            private void readCompressedBlock()
            {
                ulong method = cursor.ReadUnsigned();

                if (method != 0)
                    throw new InvalidDataException($"Unsupported OASIS file: it compresses a block with method {method}, where only DEFLATE is defined.");

                int uncompressed = checked((int)cursor.ReadUnsigned());
                int compressed = checked((int)cursor.ReadUnsigned());

                byte[] deflated = cursor.PeekRaw(compressed);
                var inflated = new byte[uncompressed];

                //Raw deflate, with no zlib header - which is what a negative window size means to zlib and
                //what DeflateStream reads natively.
                using (var source = new MemoryStream(deflated))
                using (var stream = new DeflateStream(source, CompressionMode.Decompress))
                {
                    int filled = 0;

                    while (filled < uncompressed)
                    {
                        int read = stream.Read(inflated, filled, uncompressed - filled);

                        if (read == 0)
                            throw new InvalidDataException("Truncated OASIS file: a compressed block holds less than it declares.");

                        filled += read;
                    }
                }

                cursor.EnterCompressed(inflated, compressed);
            }

            #endregion **************************************************************************



            #region Shared reading **************************************************************

            private OasisCell currentCell()
            {
                if (cell is null)
                    throw new InvalidDataException("Invalid OASIS file: it places geometry before any cell has been opened.");

                return cell;
            }

            ///<summary>
            ///Where a coordinate lands. The file says either where a thing is or how far it is from the
            ///last one, and which of the two is a mode set by a record of its own.
            ///</summary>
            private long moved(long current, long value)
            {
                if (absolutePosition)
                    return value;

                return current + value;
            }

            private void addBoundary(List<Element.Point> points)
            {
                currentCell().Shapes.Add(new Shape { Layer = layer, DataType = dataType, Points = points });
            }

            private static List<Element.Point> shifted(List<Element.Point> points, long x, long y)
            {
                var moved = new List<Element.Point>(points.Count);

                foreach (var each in points)
                    moved.Add(point(each.X + x, each.Y + y));

                return moved;
            }

            private static Element.Point point(long x, long y)
            {
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                    throw new InvalidDataException($"This OASIS file holds a coordinate ({x}, {y}) too large for GDSII, whose coordinates are 32-bit.");

                return new Element.Point { X = (int)x, Y = (int)y };
            }

            ///<summary>
            ///A list of points, in one of five ways of writing one down. All of them are deltas from the
            ///point before, which is why the list starts at the origin and is moved into place afterwards.
            ///</summary>
            private List<Element.Point> readPointList(bool closed)
            {
                byte type = cursor.ReadByte();
                int count = checked((int)cursor.ReadUnsigned());

                var points = new List<Element.Point> { point(0, 0) };

                long x = 0;
                long y = 0;

                switch (type)
                {
                    case 0:
                    case 1:
                    {
                        //Alternating horizontal and vertical steps, so only the distance is written.
                        bool horizontal = type == 0;

                        for (int i = 0; i < count; i++)
                        {
                            if (horizontal)
                                x += cursor.ReadSigned();
                            else
                                y += cursor.ReadSigned();

                            horizontal = !horizontal;

                            points.Add(point(x, y));
                        }

                        //The closing step is the one that is not written: it is whichever of the two
                        //directions has not just been used, back to where the outline started.
                        if (closed)
                        {
                            if (horizontal)
                                points.Add(point(0, y));
                            else
                                points.Add(point(x, 0));
                        }

                        break;
                    }

                    case 2:
                    case 3:
                    case 4:
                    {
                        for (int i = 0; i < count; i++)
                        {
                            (long dx, long dy) = type switch
                            {
                                2 => cursor.ReadDelta2(),
                                3 => cursor.ReadDelta3(),
                                _ => cursor.ReadGDelta()
                            };

                            x += dx;
                            y += dy;

                            points.Add(point(x, y));
                        }

                        break;
                    }

                    case 5:
                    {
                        //Each delta is a step of the *step*, not of the point - so a run at a constant
                        //slope writes one value and then nothing.
                        long stepX = 0;
                        long stepY = 0;

                        for (int i = 0; i < count; i++)
                        {
                            (long dx, long dy) = cursor.ReadGDelta();

                            stepX += dx;
                            stepY += dy;
                            x += stepX;
                            y += stepY;

                            points.Add(point(x, y));
                        }

                        break;
                    }

                    default:
                        throw new InvalidDataException($"Invalid OASIS file: {type} is not a way of writing a point list.");
                }

                return points;
            }

            ///<summary>
            ///Where the copies of an element go, as offsets from where it would have been on its own.
            ///
            ///Always at least one, so an element with no repetition and an element with one go through the
            ///same path. A repetition is also modal: a record can say "the same again" and mean whatever
            ///the last one was.
            ///</summary>
            private List<(long X, long Y)> repetitionFor(bool present)
            {
                if (!present)
                    return new List<(long, long)> { (0, 0) };

                byte type = cursor.ReadByte();

                if (type == 0)
                    return repetition;

                var offsets = new List<(long X, long Y)>();

                switch (type)
                {
                    case 1:
                    case 2:
                    case 3:
                    {
                        //
                        //Read in the order the record writes them, and only the fields it writes: the
                        //two-dimensional form gives both counts and both spacings, the one-dimensional
                        //forms give only their own.
                        //
                        //**Each read is conditional and the order is the file's**, which is what these four
                        //say and is the whole of why they cannot be rearranged: a ReadUnsigned advances the
                        //cursor, so skipping one that the record does not carry is not an optimization but
                        //the difference between reading the record and reading past it.
                        //
                        long columns = 1;

                        if (type != 3)
                            columns = 2 + (long)cursor.ReadUnsigned();

                        long rows = 1;

                        if (type != 2)
                            rows = 2 + (long)cursor.ReadUnsigned();

                        long spacingX = 0;

                        if (type != 3)
                            spacingX = (long)cursor.ReadUnsigned();

                        long spacingY = 0;

                        if (type != 2)
                            spacingY = (long)cursor.ReadUnsigned();

                        guardSize(columns * rows);

                        for (long i = 0; i < columns; i++)
                        {
                            for (long j = 0; j < rows; j++)
                                offsets.Add((i * spacingX, j * spacingY));
                        }

                        break;
                    }

                    case 4:
                    case 5:
                    case 6:
                    case 7:
                    {
                        bool alongX = type == 4 || type == 5;
                        bool gridded = type == 5 || type == 7;

                        long count = 1 + (long)cursor.ReadUnsigned();

                        //Only the gridded forms carry it, and reading it when they do not would consume the
                        //first spacing - see the note on the two-dimensional forms above.
                        long grid = 1;

                        if (gridded)
                            grid = (long)cursor.ReadUnsigned();

                        guardSize(count + 1);

                        //The element's own position is the first, and what follows are the others.
                        offsets.Add((0, 0));

                        long along = 0;

                        for (long i = 0; i < count; i++)
                        {
                            along += grid * (long)cursor.ReadUnsigned();

                            if (alongX)
                                offsets.Add((along, 0L));
                            else
                                offsets.Add((0L, along));
                        }

                        break;
                    }

                    case 8:
                    case 9:
                    {
                        long columns = 2 + (long)cursor.ReadUnsigned();

                        //Nine is the one-dimensional form: one row, one delta, and neither of the two fields
                        //the other carries is in the record to be read.
                        long rows = 1;

                        if (type != 9)
                            rows = 2 + (long)cursor.ReadUnsigned();

                        (long X, long Y) first = cursor.ReadGDelta();

                        (long X, long Y) second = (0, 0);

                        if (type != 9)
                            second = cursor.ReadGDelta();

                        guardSize(columns * rows);

                        for (long i = 0; i < columns; i++)
                        {
                            for (long j = 0; j < rows; j++)
                                offsets.Add((i * first.X + j * second.X, i * first.Y + j * second.Y));
                        }

                        break;
                    }

                    case 10:
                    case 11:
                    {
                        long count = 1 + (long)cursor.ReadUnsigned();

                        //Eleven is the gridded one; ten carries no grid field at all.
                        long grid = 1;

                        if (type == 11)
                            grid = (long)cursor.ReadUnsigned();

                        guardSize(count + 1);

                        offsets.Add((0, 0));

                        long x = 0;
                        long y = 0;

                        for (long i = 0; i < count; i++)
                        {
                            (long dx, long dy) = cursor.ReadGDelta();

                            x += grid * dx;
                            y += grid * dy;

                            offsets.Add((x, y));
                        }

                        break;
                    }

                    default:
                        throw new InvalidDataException($"Invalid OASIS file: {type} is not a kind of repetition.");
                }

                repetition = offsets;

                return offsets;
            }

            private static void guardSize(long positions)
            {
                if (positions > MaximumRepetition)
                    throw new InvalidDataException($"This OASIS file repeats one element {positions} times, which is more than this can expand into separate GDSII elements.");
            }

            #endregion **************************************************************************



            #region Turning it into GDSII *******************************************************

            private List<Record> emit()
            {
                var records = new List<Record>
                {
                    record(RecordType.HEADER, new Int2Data(GdsVersion)),
                    record(RecordType.BGNLIB, new Int2Data(timestamp())),
                    record(RecordType.LIBNAME, new AsciiData(LibraryName)),
                    record(RecordType.UNITS, new Real8Data(1.0 / unit, 1e-6 / unit))
                };

                foreach (var each in cells)
                {
                    records.Add(record(RecordType.BGNSTR, new Int2Data(timestamp())));
                    records.Add(record(RecordType.STRNAME, new AsciiData(nameOf(each))));

                    foreach (var shape in each.Shapes)
                        emitShape(records, shape);

                    foreach (var placement in each.Placements)
                        emitPlacement(records, placement);

                    records.Add(record(RecordType.ENDSTR, null));
                }

                records.Add(record(RecordType.ENDLIB, null));

                return records;
            }

            ///<summary>
            ///A cell's name, which may only be known once the whole file has been read: the tables that
            ///hold them are allowed to come after the cells that use them.
            ///</summary>
            private string nameOf(OasisCell each)
            {
                if (each.Name is not null)
                    return each.Name;

                if (each.NameReference is long reference && cellNames.TryGetValue(reference, out string? name))
                    return name;

                //A cell whose name the file never gives. Named after its reference rather than refused:
                //the geometry is all there, and a GDSII structure has to be called something.
                return $"CELL{each.NameReference}";
            }

            private void emitShape(List<Record> records, Shape shape)
            {
                if (shape.Text is not null || shape.TextReference is not null)
                {
                    emitText(records, shape);

                    return;
                }

                if (shape.Path is not null)
                {
                    emitPath(records, shape);

                    return;
                }

                records.Add(record(RecordType.BOUNDARY, null));
                records.Add(record(RecordType.LAYER, new Int2Data((short)shape.Layer)));
                records.Add(record(RecordType.DATATYPE, new Int2Data((short)shape.DataType)));
                records.Add(record(RecordType.XY, new Int4Data(closedRing(shape.Points))));
                records.Add(record(RecordType.ENDEL, null));
            }

            private void emitPath(List<Record> records, Shape shape)
            {
                var ends = shape.Path!;

                records.Add(record(RecordType.PATH, null));
                records.Add(record(RecordType.LAYER, new Int2Data((short)shape.Layer)));
                records.Add(record(RecordType.DATATYPE, new Int2Data((short)shape.DataType)));

                //GDSII names three of the endings and leaves the rest to a pair of records: flush, a half
                //width past the end, and anything else said outright.
                short pathType = 4;

                if (ends.StartExtension == 0 && ends.EndExtension == 0)
                    pathType = 0;
                else if (ends.StartExtension == ends.HalfWidth && ends.EndExtension == ends.HalfWidth)
                    pathType = 2;

                records.Add(record(RecordType.PATHTYPE, new Int2Data(pathType)));

                //**WIDTH before the extensions, not after them.** The format fixes the order of an
                //element's records and this project's own parser enforces it, so a path written the other
                //way round is a file nothing here can read back.
                //
                //It went unnoticed because no bundled file reaches it: KLayout writes flush or half-width
                //ends and never the explicit pair, so type 4 was only ever produced by something written
                //here - which nothing was, until there was a writer to write one.
                records.Add(record(RecordType.WIDTH, new Int4Data(ends.HalfWidth * 2)));

                if (pathType == 4)
                {
                    records.Add(record(RecordType.BGNEXTN, new Int4Data((int)ends.StartExtension)));
                    records.Add(record(RecordType.ENDEXTN, new Int4Data((int)ends.EndExtension)));
                }

                records.Add(record(RecordType.XY, new Int4Data(coordinates(shape.Points))));
                records.Add(record(RecordType.ENDEL, null));
            }

            private void emitText(List<Record> records, Shape shape)
            {
                records.Add(record(RecordType.TEXT, null));
                records.Add(record(RecordType.LAYER, new Int2Data((short)shape.Layer)));
                records.Add(record(RecordType.TEXTTYPE, new Int2Data((short)shape.DataType)));

                //OASIS hangs a label from its bottom-left corner and GDSII from its top-left, so the one
                //that is not the default has to be written down or every label would move up by its own
                //height on the way through.
                records.Add(record(RecordType.PRESENTATION, new BitArrayData(new byte[] { 0x00, 0x08 })));

                records.Add(record(RecordType.XY, new Int4Data(coordinates(shape.Points))));
                records.Add(record(RecordType.STRING, new AsciiData(textOf(shape))));
                records.Add(record(RecordType.ENDEL, null));
            }

            private string textOf(Shape shape)
            {
                if (shape.Text is not null)
                    return shape.Text;

                if (shape.TextReference is long reference && textStrings.TryGetValue(reference, out string? text))
                    return text;

                return "";
            }

            private void emitPlacement(List<Record> records, Placement placement)
            {
                records.Add(record(RecordType.SREF, null));
                records.Add(record(RecordType.SNAME, new AsciiData(placementName(placement))));

                if (placement.Flipped || placement.Angle != 0 || placement.Magnification != 1)
                {
                    //Bit 15 is the reflection about the x axis, applied before the rotation - which is the
                    //same order OASIS applies its own flip in.
                    byte[] flags = new byte[] { 0x00, 0x00 };

                    if (placement.Flipped)
                        flags[0] = 0x80;

                    records.Add(record(RecordType.STRANS, new BitArrayData(flags)));

                    if (placement.Magnification != 1)
                        records.Add(record(RecordType.MAG, new Real8Data(placement.Magnification)));

                    if (placement.Angle != 0)
                        records.Add(record(RecordType.ANGLE, new Real8Data(placement.Angle)));
                }

                records.Add(record(RecordType.XY, new Int4Data(placement.At.X, placement.At.Y)));
                records.Add(record(RecordType.ENDEL, null));
            }

            private string placementName(Placement placement)
            {
                if (placement.Name is not null)
                    return placement.Name;

                if (placement.NameReference is long reference && cellNames.TryGetValue(reference, out string? name))
                    return name;

                return $"CELL{placement.NameReference}";
            }

            ///<summary>
            ///A boundary's coordinates, closed. GDSII wants the first point repeated at the end and OASIS
            ///does not write it, so it is added here rather than left for a reader to notice.
            ///</summary>
            private static int[] closedRing(List<Element.Point> points)
            {
                var coordinates = new List<int>((points.Count + 1) * 2);

                foreach (var each in points)
                {
                    coordinates.Add(each.X);
                    coordinates.Add(each.Y);
                }

                if (points.Count > 0 && (points[0].X != points[^1].X || points[0].Y != points[^1].Y))
                {
                    coordinates.Add(points[0].X);
                    coordinates.Add(points[0].Y);
                }

                return coordinates.ToArray();
            }

            private static int[] coordinates(List<Element.Point> points)
            {
                var values = new int[points.Count * 2];

                for (int i = 0; i < points.Count; i++)
                {
                    values[i * 2] = points[i].X;
                    values[(i * 2) + 1] = points[i].Y;
                }

                return values;
            }

            ///<summary>
            ///The twelve numbers a BGNLIB or a BGNSTR carries.
            ///
            ///A fixed date rather than the clock: OASIS records no timestamps, so anything here is
            ///invented, and inventing the same thing every time means converting one file twice produces
            ///the same bytes twice.
            ///</summary>
            private static short[] timestamp()
            {
                return new short[] { 70, 1, 1, 0, 0, 0, 70, 1, 1, 0, 0, 0 };
            }

            private static Record record(RecordType type, RecordData? data)
            {
                return new Record((short)type, data?.Encode() ?? Array.Empty<byte>());
            }

            #endregion **************************************************************************
        }
    }
}
