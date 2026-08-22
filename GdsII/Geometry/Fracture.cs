using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///
    ///Cutting a shape too large for one GDSII record into several that add up to it.
    ///
    ///**Because the format runs out before geometry does.** A record carries its own total length in a
    ///two-byte field, so 65535 bytes is the most one can be - which for an `XY`, at two four-byte
    ///coordinates a point, is 8,191 points. A polygon with more corners than that has no spelling in GDSII
    ///as one element, and until this existed the writer refused the file rather than write a length that
    ///wrapped. See <see cref="Record.MostBytes"/> for what the refusal is still there to catch.
    ///
    ///It is not a rare shape. A comb - an interdigitated capacitor, a set of fingers, a guard ring - merges
    ///to one outline with about four corners a tooth, so a couple of thousand teeth reaches it, and merging
    ///them is one press of Combine. A DXF spline flattened to a tolerance gets there too, and an OASIS file
    ///can simply *contain* one: that format counts its points with a variable-length integer and has no
    ///ceiling of this kind at all.
    ///
    ///**Several boundaries, not several XY records.** A writer that meets this limit can do either, and this
    ///reader now reads both - see takeXy, which joins a run of them into one shape.
    ///
    ///Boundaries anyway, because reading a thing is not the same as every reader reading it. Splitting a
    ///point list across consecutive `XY` records is behind an option in KLayout rather than being what it
    ///does by default, and a file whose shapes only appear when somebody has turned the right switch on is
    ///a poor thing to hand somebody. Separate elements are what a mask writer produces from a polygon this
    ///large, and they need no switch anywhere.
    ///
    ///The pieces are exact. The cut runs along an integer coordinate and Clipper works in integers, so what
    ///comes out covers the same ground as what went in - not nearly, exactly. What is lost is that the
    ///shape was one object, which is a thing GDSII has no way to say about a polygon this size anyway.
    ///
    public static class Fracture
    {
        ///
        ///The most corners one boundary can carry.
        ///
        ///GDSII repeats a boundary's first corner at the end, so the record holds one more point than the
        ///shape has corners - see <see cref="LayoutWriter"/> and the closing pair every writer here adds.
        ///That is 8,190 rather than 8,191, and getting it wrong by one produces a file that is refused at
        ///the very last step.
        ///
        public const int MostCorners = ((MostBytes - 4) / 8) - 1;

        ///
        ///A ring cut into pieces that each fit, or the ring itself when it already does.
        ///
        ///**Cut at the median corner rather than down the middle of the box.** Halving the bounding box
        ///parts a shape only where its corners are spread across its extent. Where they are crowded into
        ///one part of it, the middle of the box is empty: every corner lands on one side, that side is as
        ///large as what went in, and the next round asks the same question of the same points. Cutting where
        ///half the corners lie on each side halves them by construction, whatever the shape is doing.
        ///
        ///**A comb does not show this**, which is worth knowing since a comb is the case this exists for.
        ///Its teeth are spread evenly, so both rules part it equally well - measured, by making the cut the
        ///midpoint and watching every comb test still pass. What shows it is a strip a million units wide
        ///with a fine zigzag in its first two thousand: on the midpoint rule that one gives up and says it
        ///cannot be parted, which is the test that earns this line.
        ///
        ///The longer side of the box first, so pieces stay roughly square rather than becoming slivers, and
        ///the other axis as a fallback for the shape that will not split along the first - a row of vertical
        ///teeth all standing at the same handful of x, say.
        ///
        public static List<List<Element.Point>> Into(IReadOnlyList<Element.Point> ring)
        {
            return Into(ring, MostCorners);
        }

        ///<summary>The same, to a limit of the caller's choosing - which is how the tests reach small cases.</summary>
        public static List<List<Element.Point>> Into(IReadOnlyList<Element.Point> ring, int mostCorners)
        {
            var pieces = new List<List<Element.Point>>();

            cut(ring, mostCorners, pieces);

            return pieces;
        }

        ///<summary>True when this ring has to be cut before it can be written.</summary>
        public static bool IsTooLarge(IReadOnlyList<Element.Point> ring)
        {
            return ring.Count > MostCorners;
        }

        private static void cut(IReadOnlyList<Element.Point> ring, int mostCorners, List<List<Element.Point>> into)
        {
            if (ring.Count <= mostCorners)
            {
                into.Add(new List<Element.Point>(ring));

                return;
            }

            //Widest axis first; if the shape will not part along it, the other one.
            var halves = split(ring, wider(ring));

            if (halves is null)
                halves = split(ring, !wider(ring));

            //
            //Neither axis parted it, which a shape with this many corners and any extent at all cannot do.
            //
            //Said out loud rather than returned as the ring that does not fit, because handing that back
            //puts a record over the limit into a file at the very last step - and the guard there reports
            //a length, which says nothing about the shape that caused it.
            //
            if (halves is null)
                throw new InvalidDataException($"A shape of {ring.Count} corners could not be cut into pieces that fit a GDSII record: it does not part along either axis.");

            foreach (var half in halves)
                cut(half, mostCorners, into);
        }

        ///<summary>Which way the shape is longer, true for x.</summary>
        private static bool wider(IReadOnlyList<Element.Point> ring)
        {
            long left = ring[0].X, right = ring[0].X, top = ring[0].Y, bottom = ring[0].Y;

            foreach (var point in ring)
            {
                if (point.X < left)
                    left = point.X;

                if (point.X > right)
                    right = point.X;

                if (point.Y < top)
                    top = point.Y;

                if (point.Y > bottom)
                    bottom = point.Y;
            }

            return (right - left) >= (bottom - top);
        }

        ///
        ///The ring intersected with the half on each side of its median corner, or null if that got nowhere.
        ///
        ///Null rather than the ring back, so the caller can tell "this axis did not work" from "here is the
        ///answer" and go and try the other one.
        ///
        private static List<List<Element.Point>>? split(IReadOnlyList<Element.Point> ring, bool alongX)
        {
            long at = median(ring, alongX);

            var box = bounds(ring);

            //A cut outside the shape parts nothing. Which happens when every corner shares a coordinate.
            if (alongX && (at <= box.Left || at >= box.Right))
                return null;

            if (!alongX && (at <= box.Top || at >= box.Bottom))
                return null;

            var subject = new List<IReadOnlyList<Element.Point>> { ring };

            var pieces = new List<List<Element.Point>>();

            foreach (var half in halvesOf(box, at, alongX))
            {
                var clip = new List<IReadOnlyList<Element.Point>> { half };

                pieces.AddRange(Booleans.Combine(subject, clip, BooleanOperation.And));
            }

            //
            //Nothing was made smaller, so recursing would ask the same question again.
            //
            //It can happen even with the cut inside the box: keyholing a piece that came out with a hole in
            //it adds corners back, and on an awkward shape that can hand back a piece as large as the one
            //that was cut. The other axis usually does part it; if neither does, cut() says so.
            //
            foreach (var piece in pieces)
            {
                if (piece.Count >= ring.Count)
                    return null;
            }

            if (pieces.Count == 0)
                return null;

            return pieces;
        }

        ///<summary>The coordinate half the corners fall on either side of, which is what makes the cut bite.</summary>
        private static long median(IReadOnlyList<Element.Point> ring, bool alongX)
        {
            var along = new int[ring.Count];

            for (int i = 0; i < ring.Count; i++)
            {
                if (alongX)
                    along[i] = ring[i].X;
                else
                    along[i] = ring[i].Y;
            }

            Array.Sort(along);

            return along[along.Length / 2];
        }

        ///<summary>The two rectangles either side of the cut, grown past the shape so the ends are covered.</summary>
        private static List<List<Element.Point>> halvesOf(Extent box, long at, bool alongX)
        {
            //One unit past each edge, so a corner sitting exactly on the boundary is inside the clip rather
            //than a question about how Clipper treats a point on an edge.
            long left = box.Left - 1;
            long right = box.Right + 1;
            long top = box.Top - 1;
            long bottom = box.Bottom + 1;

            var halves = new List<List<Element.Point>>();

            if (alongX)
            {
                halves.Add(rectangle(left, top, at, bottom));
                halves.Add(rectangle(at, top, right, bottom));
            }
            else
            {
                halves.Add(rectangle(left, top, right, at));
                halves.Add(rectangle(left, at, right, bottom));
            }

            return halves;
        }

        private static List<Element.Point> rectangle(long left, long top, long right, long bottom)
        {
            return new List<Element.Point>
            {
                new Element.Point((int)left, (int)top),
                new Element.Point((int)right, (int)top),
                new Element.Point((int)right, (int)bottom),
                new Element.Point((int)left, (int)bottom)
            };
        }

        private readonly record struct Extent(long Left, long Top, long Right, long Bottom);

        private static Extent bounds(IReadOnlyList<Element.Point> ring)
        {
            long left = ring[0].X, right = ring[0].X, top = ring[0].Y, bottom = ring[0].Y;

            foreach (var point in ring)
            {
                if (point.X < left)
                    left = point.X;

                if (point.X > right)
                    right = point.X;

                if (point.Y < top)
                    top = point.Y;

                if (point.Y > bottom)
                    bottom = point.Y;
            }

            return new Extent(left, top, right, bottom);
        }



        #region The record list *************************************************************

        ///
        ///A record list with every boundary that will not fit replaced by the several that do.
        ///
        ///**One place, because there are four that make boundaries.** The DXF reader, the OASIS reader, the
        ///editor and <see cref="LayoutWriter"/> each build them, and three of those can produce one past the
        ///limit - a flattened spline, an OASIS polygon, a merge. Fracturing at each of them would be four
        ///chances to miss one, and the fifth producer written later would miss it by default. This runs
        ///where the limit actually applies, which is the moment GDSII bytes are asked for.
        ///
        ///**The same list back when nothing needs cutting**, which is every ordinary file. The check is one
        ///pass over the records comparing payload lengths, beside a pass <see cref="GDS.Serialize"/> already
        ///makes to size its buffer.
        ///
        ///The element's other records go onto every piece: its layer, its datatype, and anything else
        ///between the BOUNDARY and the ENDEL - `ELFLAGS`, `PLEX`, properties. Each piece is the same shape's
        ///worth of the same thing, so it carries the same everything.
        ///
        public static List<Record> ForGdsii(List<Record> records)
        {
            if (!anyTooLarge(records))
                return records;

            var fractured = new List<Record>(records.Count);

            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Type != RecordType.BOUNDARY)
                {
                    fractured.Add(records[i]);

                    continue;
                }

                int end = endOfElement(records, i);

                //No ENDEL, which the reader would have refused - left alone rather than guessed at.
                if (end < 0)
                {
                    fractured.Add(records[i]);

                    continue;
                }

                writeElement(records, i, end, fractured);

                i = end;
            }

            return fractured;
        }

        ///<summary>Whether anything here is over the limit, so an ordinary file pays one comparison a record.</summary>
        private static bool anyTooLarge(List<Record> records)
        {
            foreach (var record in records)
            {
                if (record.Type == RecordType.XY && record.SerializedLength > MostBytes)
                    return true;
            }

            return false;
        }

        ///<summary>Where this element's ENDEL is, or -1 if the list ends without one.</summary>
        private static int endOfElement(List<Record> records, int from)
        {
            for (int i = from + 1; i < records.Count; i++)
            {
                if (records[i].Type == RecordType.ENDEL)
                    return i;
            }

            return -1;
        }

        private static void writeElement(List<Record> records, int start, int end, List<Record> into)
        {
            int xy = -1;

            for (int i = start + 1; i < end; i++)
            {
                if (records[i].Type == RecordType.XY)
                {
                    xy = i;

                    break;
                }
            }

            //Nothing to cut, or nothing that needs it.
            if (xy < 0 || records[xy].SerializedLength <= MostBytes)
            {
                for (int i = start; i <= end; i++)
                    into.Add(records[i]);

                return;
            }

            var pieces = Into(ringOf(records[xy]));

            foreach (var piece in pieces)
            {
                for (int i = start; i < end; i++)
                {
                    if (i == xy)
                        into.Add(new Record((short)RecordType.XY, new Int4Data(closedRing(piece)).Encode()));
                    else
                        into.Add(records[i]);
                }

                into.Add(records[end]);
            }
        }

        ///<summary>An XY payload as corners, with the repeated closing one dropped.</summary>
        private static List<Element.Point> ringOf(Record xy)
        {
            var values = ((Int4Data)xy.Data!).Values;

            var ring = new List<Element.Point>(values.Length / 2);

            for (int i = 0; i + 1 < values.Length; i += 2)
                ring.Add(new Element.Point(values[i], values[i + 1]));

            if (ring.Count > 1 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y)
                ring.RemoveAt(ring.Count - 1);

            return ring;
        }

        ///<summary>Corners as GDSII wants them written: the first one again at the end.</summary>
        private static int[] closedRing(List<Element.Point> ring)
        {
            var values = new int[(ring.Count + 1) * 2];

            for (int i = 0; i < ring.Count; i++)
            {
                values[i * 2] = ring[i].X;
                values[(i * 2) + 1] = ring[i].Y;
            }

            values[ring.Count * 2] = ring[0].X;
            values[(ring.Count * 2) + 1] = ring[0].Y;

            return values;
        }

        #endregion **************************************************************************
    }
}
