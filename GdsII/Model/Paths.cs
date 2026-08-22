using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///What a path is written with, and how to change it.
    ///
    ///**A path is a centerline and a width, not an outline.** It is most of what a real layout is made of -
    ///every wire between two things is one - and the format stores it as the line down the middle plus how
    ///wide to draw it, which is why one record changes the width of a route with two hundred corners in it.
    ///<see cref="PathOutline"/> turns that back into a polygon for drawing; this is the side that writes it.
    ///</summary>
    public static class Paths
    {
        ///
        ///How the two ends are finished.
        ///
        ///The format's own numbering, and only the three that need no extra records. Type 4 takes its
        ///extensions from a BGNEXTN and an ENDEXTN and can differ at each end, which is a thing files contain
        ///and not a thing worth offering to draw with - a number nobody typed is a number nobody can check.
        ///
        public enum Ends
        {
            ///<summary>Square, stopping exactly on the endpoint. The format's default for a missing record.</summary>
            Flush = 0,

            ///<summary>Round, reaching half the width past the endpoint.</summary>
            Round = 1,

            ///<summary>Square, reaching half the width past the endpoint.</summary>
            Extended = 2
        }

        ///<summary>
        ///The records of a new path down a centerline given in the structure's own coordinates.
        ///
        ///Not closed, unlike a boundary: a path is an open run and joining its ends would draw a wire back to
        ///where it started. Two points is a legitimate path, which is why the minimum here is lower than the
        ///three a polygon needs.
        ///</summary>
        public static List<Record>? Records(LayerKey layer, IReadOnlyList<Element.Point> along, int width, Ends ends)
        {
            var line = new List<Element.Point>();

            foreach (var point in along)
            {
                //A point on top of the one before it is a zero-length segment, which draws nothing and gives
                //the outliner no direction to turn a corner by.
                if (line.Count > 0 && line[^1].X == point.X && line[^1].Y == point.Y)
                    continue;

                line.Add(point);
            }

            if (line.Count < 2 || width < 0)
                return null;

            var coordinates = new int[line.Count * 2];

            for (int i = 0; i < line.Count; i++)
            {
                coordinates[i * 2] = line[i].X;
                coordinates[(i * 2) + 1] = line[i].Y;
            }

            return new List<Record>
            {
                Hierarchy.Make(RecordType.PATH, null),
                Hierarchy.Make(RecordType.LAYER, new Int2Data(layer.Number)),
                Hierarchy.Make(RecordType.DATATYPE, new Int2Data(layer.DataType)),
                Hierarchy.Make(RecordType.PATHTYPE, new Int2Data((short)ends)),
                Hierarchy.Make(RecordType.WIDTH, new Int4Data(width)),
                Hierarchy.Make(RecordType.XY, new Int4Data(coordinates)),
                Hierarchy.Make(RecordType.ENDEL, null)
            };
        }

        ///<summary>
        ///What an element is drawn with, or null for anything that is not a path.
        ///
        ///Both records are optional and both have a default the format states: no WIDTH is a width of zero,
        ///and no PATHTYPE is square ends flush with the endpoint. Reading them here rather than at each call
        ///site is what keeps the editor's answer the same as the flattener's.
        ///</summary>
        public static (int Width, Ends Ends)? Of(ElementModel element)
        {
            if (element.Element is not PathModel path)
                return null;

            int width = 0;

            if (path.WIDTH?.Data is Int4Data measured)
                width = measured.Value;

            var ends = Ends.Flush;

            if (path.PATHTYPE?.Data is Int2Data style && Enum.IsDefined(typeof(Ends), (int)style.Value))
                ends = (Ends)style.Value;

            return (width, ends);
        }

        ///
        ///The same path written with a different width and ends.
        ///
        ///**Rebuilt rather than edited, because both records may be absent.** A path with no WIDTH is a path
        ///a reader draws as a hairline, and giving it one is adding a record rather than changing a number -
        ///which an element cannot be asked to do in place. Everything else it carries, including its
        ///properties and its coordinates, comes across in the order it was already in.
        ///
        ///A BGNEXTN or an ENDEXTN is dropped. Those only mean anything to a type-4 path, and type 4 is not
        ///among the ends that can be chosen - so leaving them would leave two numbers describing an end style
        ///the path no longer has.
        ///
        public static List<Record>? Rewritten(IReadOnlyList<Record> path, int width, Ends ends)
        {
            if (width < 0 || path.Count == 0 || path[0].Type != RecordType.PATH)
                return null;

            var rebuilt = new List<Record>();
            bool written = false;

            foreach (var record in path)
            {
                if (record.Type == RecordType.PATHTYPE
                    || record.Type == RecordType.WIDTH
                    || record.Type == RecordType.BGNEXTN
                    || record.Type == RecordType.ENDEXTN)
                    continue;

                //Through the bytes, which is the one way to copy a record without knowing what it holds.
                rebuilt.Add(new Record((short)record.Type, record.Data?.Encode() ?? Array.Empty<byte>()));

                //Straight after the layer pair, which is where the format puts them - a record in the wrong
                //order is one a strict reader stops at.
                if (record.Type == RecordType.DATATYPE)
                {
                    rebuilt.Add(Hierarchy.Make(RecordType.PATHTYPE, new Int2Data((short)ends)));
                    rebuilt.Add(Hierarchy.Make(RecordType.WIDTH, new Int4Data(width)));

                    written = true;
                }
            }

            if (!written)
                return null;

            return rebuilt;
        }
    }
}
