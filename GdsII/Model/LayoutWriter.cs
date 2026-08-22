using System.Globalization;
using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///Writes a flattened layout back out as a GDSII library.
    ///
    ///**The way back from <see cref="GdsFlattener"/>.** Resolving a hierarchy into flat geometry was
    ///one-way: the result could be drawn and measured but not saved, so anything computed from it - a
    ///merged layer, a boolean, a grown shape - had nowhere to go. This closes that.
    ///
    ///What comes out is one structure. That is what flattening means: the references were resolved into
    ///the geometry they stood for, and there is nothing left to point at.
    ///</summary>
    public static class LayoutWriter
    {
        ///<summary>
        ///Builds a library from flat geometry, keeping the source's header and units so the result
        ///measures the same as the file it came from.
        ///</summary>
        public static GDS ToGds(GDS source, FlattenedLayout layout, string structureName = "TOP")
        {
            var records = new List<Record>
            {
                copyOf(source.StreamFormat.HEADER, RecordType.HEADER, new Int2Data(600)),
                copyOf(source.StreamFormat.BGNLIB, RecordType.BGNLIB, new Int2Data(epoch())),
                copyOf(source.StreamFormat.LIBNAME, RecordType.LIBNAME, new AsciiData("LIB")),
                copyOf(source.StreamFormat.UNITS, RecordType.UNITS, new Real8Data(0.001, 1e-9)),

                new Record((short)RecordType.BGNSTR, new Int2Data(epoch()).Encode()),
                new Record((short)RecordType.STRNAME, new AsciiData(structureName).Encode())
            };

            foreach (var element in layout.Elements)
                write(records, element);

            records.Add(new Record((short)RecordType.ENDSTR, Array.Empty<byte>()));
            records.Add(new Record((short)RecordType.ENDLIB, Array.Empty<byte>()));

            return GDS.FromRecords(records);
        }

        ///<summary>
        ///The source's own record where it has one, and something sensible where it does not. Copied
        ///rather than rebuilt so the units come through exactly - a REAL8 is lossy, and recomputing one
        ///from a value read out of another would move it in the last bit.
        ///</summary>
        private static Record copyOf(Record? original, RecordType type, RecordData fallback)
        {
            if (original?.Data is not null)
                return new Record((short)type, original.Data.Encode());

            return new Record((short)type, fallback.Encode());
        }

        private static void write(List<Record> records, Element element)
        {
            if (element.Text is not null)
            {
                writeText(records, element);

                return;
            }

            if (element.Points.Count < 3)
                return;

            records.Add(new Record((short)RecordType.BOUNDARY, Array.Empty<byte>()));
            records.Add(new Record((short)RecordType.LAYER, new Int2Data(element.Layer.Key.Number).Encode()));
            records.Add(new Record((short)RecordType.DATATYPE, new Int2Data(element.Layer.Key.DataType).Encode()));
            records.Add(new Record((short)RecordType.XY, new Int4Data(closedRing(element.Points)).Encode()));
            records.Add(new Record((short)RecordType.ENDEL, Array.Empty<byte>()));
        }

        private static void writeText(List<Record> records, Element element)
        {
            if (element.Points.Count == 0)
                return;

            records.Add(new Record((short)RecordType.TEXT, Array.Empty<byte>()));
            records.Add(new Record((short)RecordType.LAYER, new Int2Data(element.Layer.Key.Number).Encode()));
            records.Add(new Record((short)RecordType.TEXTTYPE, new Int2Data(element.Layer.Key.DataType).Encode()));
            records.Add(new Record((short)RecordType.PRESENTATION, new BitArrayData(presentationOf(element.Presentation)).Encode()));
            records.Add(new Record((short)RecordType.XY, new Int4Data(element.Points[0].X, element.Points[0].Y).Encode()));
            records.Add(new Record((short)RecordType.STRING, new AsciiData(element.Text!).Encode()));
            records.Add(new Record((short)RecordType.ENDEL, Array.Empty<byte>()));
        }

        ///<summary>Horizontal in the low pair of bits, vertical in the next - see TextPresentation.</summary>
        private static byte[] presentationOf(TextPresentation presentation)
        {
            int word = (int)presentation.Horizontal | ((int)presentation.Vertical << 2) | (presentation.Font << 4);

            return new byte[] { 0x00, (byte)word };
        }

        ///<summary>GDSII wants a boundary's first corner repeated at the end; a flattened ring does not carry it.</summary>
        private static int[] closedRing(List<Element.Point> points)
        {
            bool closed = points[0].X == points[^1].X && points[0].Y == points[^1].Y;

            var values = new List<int>((points.Count + 1) * 2);

            foreach (var point in points)
            {
                values.Add(point.X);
                values.Add(point.Y);
            }

            if (!closed)
            {
                values.Add(points[0].X);
                values.Add(points[0].Y);
            }

            return values.ToArray();
        }

        ///<summary>
        ///The twelve numbers a BGNLIB or BGNSTR carries, when the source has none to copy. Fixed rather
        ///than the clock, so converting one file twice produces the same bytes twice.
        ///</summary>
        private static short[] epoch()
        {
            return new short[] { 70, 1, 1, 0, 0, 0, 70, 1, 1, 0, 0, 0 };
        }
    }
}
