using System.Globalization;
using System.Text;

using static GdsII.GDS;

namespace GdsII
{
    ///
    ///Writing a GDSII library out as DXF.
    ///
    ///**The direction that had no answer.** A DXF could be opened and came back out as GDSII, which is fine
    ///until the person who sent you the drawing wants it back - a MEMS house working in AutoCAD, a
    ///packaging drawing that has to go back to the mechanical side, anybody whose tool reads DXF and does
    ///not read GDSII. Converting through a third program to get there is the thing this app exists not to
    ///need.
    ///
    ///**Release 12, and old-style runs.** LWPOLYLINE is compact and is a release 14 entity; POLYLINE with
    ///its own VERTEX records is verbose and is read by every DXF reader ever written, including the ones
    ///still running in mask shops. That is the trade, and the second half of it is worth more here than the
    ///file size - the whole point of writing a DXF is that somebody else's program opens it. It is also
    ///what KLayout writes, which is a vote from the tool most likely to be on the other end.
    ///
    ///Nothing is lost by the old release: GDSII has no curves, so ELLIPSE, SPLINE and HATCH - the entities
    ///release 12 does not have - are entities this could never produce.
    ///
    ///**The mapping**, which is the reader's run backwards:
    ///
    ///- **A layer is named `L68D20`**, the pair spelled out. DXF layers are names and GDSII layers are
    ///  numbers, so the numbers have to live in the name or not at all - and this is the spelling KLayout
    ///  uses, the one <see cref="DxfReader.NumberFromName"/> reads, and one of the few that contains no
    ///  character DXF forbids in a layer name. A name from a layermap is not used: `/` is illegal in one,
    ///  and a name that cannot carry the numbers loses them.
    ///- **Coordinates are microns**, with `$INSUNITS` saying so rather than leaving it to be assumed.
    ///- **Every placed structure is a block**; the top-level ones' elements go straight into ENTITIES,
    ///  which is what a drawing is. A file with more than one top puts all of their elements there
    ///  together, since that is what having more than one top means.
    ///- **A boundary or a box is a closed run, a path is an open one** with the drawing's constant width.
    ///  The reader's rule, in reverse, and for the same reason: an open run has no area.
    ///- **An SREF is an INSERT and an AREF is a MINSERT**, which is release 12's own repeated insert - one
    ///  entity for a whole array, the way an AREF is one record. An array whose axes are not the drawing's
    ///  becomes one INSERT per position instead, because MINSERT spaces along the block's own axes and
    ///  cannot say anything else.
    ///
    public static class DxfWriter
    {
        #region Constants *******************************************************************

        ///<summary>Release 12, which is the last one every reader agrees about.</summary>
        private const string AcadVersion = "AC1009";

        ///<summary>`$INSUNITS` for microns, which is what the coordinates are written in.</summary>
        private const int Microns = 13;

        ///
        ///How many digits a coordinate is written with.
        ///
        ///Six is a database unit of a picometer, which is a thousand times finer than any layout uses and
        ///is comfortably inside what a double holds exactly for a number this size. Round-tripping with
        ///"R17" instead would be exact and would write `0.10000000000000001` for a tenth of a micron, which
        ///is a file nobody can read and a difference nobody can measure.
        ///
        private const string Coordinate = "0.######";

        #endregion **************************************************************************



        #region Writing *********************************************************************

        ///<summary>The library as a DXF, in the encoding the file is written in.</summary>
        public static byte[] Write(GDS gds)
        {
            //ASCII rather than UTF-8: release 12 predates any statement about encoding, and a byte over 127
            //in a release 12 file is read as whatever code page the reader happens to be in. AsAscii has
            //already taken anything else out of every name and label on the way into the library.
            return Encoding.ASCII.GetBytes(Text(gds));
        }

        ///<summary>The same, as the text it is - which is what the tests read and what a person can look at.</summary>
        public static string Text(GDS gds)
        {
            var drawing = new StringBuilder();

            double microns = MicronsPerUnit(gds);

            var placed = PlacedNames(gds);

            appendHeader(drawing);
            appendTables(drawing, gds);

            appendBlocks(drawing, gds, placed, microns);
            appendEntities(drawing, gds, placed, microns);

            Pair(drawing, 0, "EOF");

            return drawing.ToString();
        }

        ///
        ///How many microns one database unit is, off the library's own UNITS.
        ///
        ///The second value in that record is meters per database unit, which is the one that means anything
        ///absolute - the first is a ratio between two units the file names and nothing else. A file with no
        ///UNITS at all, or one saying zero, is taken as the nanometer nearly every real file uses; the
        ///alternative is dividing by zero and writing a drawing of infinities.
        ///
        public static double MicronsPerUnit(GDS gds)
        {
            if (gds.StreamFormat?.UNITS?.Data is Real8Data units && units.Values.Length > 1 && units.Values[1] > 0)
                return units.Values[1] * 1e6;

            return 0.001;
        }

        ///<summary>The name a GDSII layer and datatype are given, which is the only place DXF can hold them.</summary>
        public static string LayerName(short layer, short dataType)
        {
            //A negative datatype is what the reader uses for one the file did not give, so it is not one.
            if (dataType < 0)
                dataType = 0;

            return $"L{layer}D{dataType}";
        }

        #endregion **************************************************************************



        #region The sections ****************************************************************

        private static void appendHeader(StringBuilder drawing)
        {
            Pair(drawing, 0, "SECTION");
            Pair(drawing, 2, "HEADER");

            Pair(drawing, 9, "$ACADVER");
            Pair(drawing, 1, AcadVersion);

            //Said rather than left to be assumed. A drawing with no units opens at whatever the reader
            //guesses, which for a layout is a thousand times out in one direction or the other.
            Pair(drawing, 9, "$INSUNITS");
            Pair(drawing, 70, Microns);

            Pair(drawing, 0, "ENDSEC");
        }

        ///
        ///The LAYER table, which is where a DXF declares its layers before anything uses one.
        ///
        ///Every layer the library draws on, in order, so a reader that builds its layer list from the table
        ///gets them all - and so a layer that is declared but never drawn on does not appear, which would
        ///be a layer nobody can find anything on.
        ///
        private static void appendTables(StringBuilder drawing, GDS gds)
        {
            Pair(drawing, 0, "SECTION");
            Pair(drawing, 2, "TABLES");

            Pair(drawing, 0, "TABLE");
            Pair(drawing, 2, "LAYER");

            var layers = LayersIn(gds);

            Pair(drawing, 70, layers.Count);

            foreach (var key in layers)
            {
                Pair(drawing, 0, "LAYER");
                Pair(drawing, 2, LayerName(key.Number, key.DataType));
                Pair(drawing, 70, 0);
                Pair(drawing, 62, 7);
                Pair(drawing, 6, "CONTINUOUS");
            }

            Pair(drawing, 0, "ENDTAB");
            Pair(drawing, 0, "ENDSEC");
        }

        ///<summary>Every layer anything is drawn on, in the order the file first draws on one.</summary>
        public static List<LayerKey> LayersIn(GDS gds)
        {
            var layers = new List<LayerKey>();
            var seen = new HashSet<LayerKey>();

            foreach (var structure in gds.StreamFormat.Structures)
            {
                foreach (var element in structure.Elements)
                {
                    if (KeyOf(element) is not LayerKey key)
                        continue;

                    if (seen.Add(key))
                        layers.Add(key);
                }
            }

            return layers;
        }

        private static void appendBlocks(StringBuilder drawing, GDS gds, HashSet<string> placed, double microns)
        {
            Pair(drawing, 0, "SECTION");
            Pair(drawing, 2, "BLOCKS");

            foreach (var structure in gds.StreamFormat.Structures)
            {
                string name = Hierarchy.NameOf(structure);

                //Only what something places. A top-level structure's own elements go into ENTITIES, and a
                //block nothing inserts is a block no reader will draw.
                if (name.Length == 0 || !placed.Contains(name))
                    continue;

                Pair(drawing, 0, "BLOCK");
                Pair(drawing, 2, name);
                Pair(drawing, 70, 0);

                //The base point, at the origin - which is where a GDSII cell's own origin is, and is what
                //makes an INSERT of it land where the SREF said.
                Pair(drawing, 10, 0);
                Pair(drawing, 20, 0);
                Pair(drawing, 30, 0);

                foreach (var element in structure.Elements)
                    appendElement(drawing, element, microns);

                Pair(drawing, 0, "ENDBLK");
            }

            Pair(drawing, 0, "ENDSEC");
        }

        private static void appendEntities(StringBuilder drawing, GDS gds, HashSet<string> placed, double microns)
        {
            Pair(drawing, 0, "SECTION");
            Pair(drawing, 2, "ENTITIES");

            foreach (var structure in gds.StreamFormat.Structures)
            {
                string name = Hierarchy.NameOf(structure);

                if (name.Length > 0 && placed.Contains(name))
                    continue;

                foreach (var element in structure.Elements)
                    appendElement(drawing, element, microns);
            }

            Pair(drawing, 0, "ENDSEC");
        }

        ///<summary>Every structure something else places, which is what has to become a block.</summary>
        public static HashSet<string> PlacedNames(GDS gds)
        {
            var placed = new HashSet<string>();

            foreach (var structure in gds.StreamFormat.Structures)
            {
                foreach (string name in Hierarchy.Places(structure))
                    placed.Add(name);
            }

            return placed;
        }

        #endregion **************************************************************************



        #region The elements ****************************************************************

        private static void appendElement(StringBuilder drawing, ElementModel element, double microns)
        {
            if (element.Element is BoundaryModel || element.Element is BoxModel)
                appendRun(drawing, element, microns, closed: true, width: 0);
            else if (element.Element is PathModel path)
                appendRun(drawing, element, microns, closed: false, width: WidthOf(path) * microns);
            else if (element.Element is TextModel text)
                appendText(drawing, element, text, microns);
            else if (element.Element is SrefModel sref)
                appendPlacement(drawing, element, Hierarchy.SnameOf(element), microns, 1, 1, 0, 0);
            else if (element.Element is ArefModel aref)
                appendArray(drawing, element, aref, microns);
        }

        ///
        ///A run of points, as the release 12 polyline: the entity, then a VERTEX each, then a SEQEND.
        ///
        ///Closed is bit one of the flags, the same bit the reader looks at. A closed run drops the repeat of
        ///its first point, which a GDSII boundary carries and a DXF one does not - left in, it is a
        ///zero-length edge in every reader that opens the file.
        ///
        private static void appendRun(StringBuilder drawing, ElementModel element, double microns, bool closed, double width)
        {
            var points = PointsOf(element);

            if (points.Count < 2)
                return;

            if (closed && points.Count > 1 && points[0].X == points[^1].X && points[0].Y == points[^1].Y)
                points.RemoveAt(points.Count - 1);

            if (closed && points.Count < 3)
                return;

            string layer = LayerOf(element);

            Pair(drawing, 0, "POLYLINE");
            Pair(drawing, 8, layer);

            //66 is what says the vertices follow. Release 12 requires it and later releases ignore it, so
            //writing it costs nothing and leaving it out loses the shape in an older reader.
            Pair(drawing, 66, 1);

            if (closed)
                Pair(drawing, 70, 1);
            else
                Pair(drawing, 70, 0);

            if (width > 0)
            {
                //The starting and ending width of every segment, which is what a constant-width run is.
                Pair(drawing, 40, width, Coordinate);
                Pair(drawing, 41, width, Coordinate);
            }

            foreach (var point in points)
            {
                Pair(drawing, 0, "VERTEX");
                Pair(drawing, 8, layer);
                Pair(drawing, 10, point.X * microns, Coordinate);
                Pair(drawing, 20, point.Y * microns, Coordinate);
            }

            Pair(drawing, 0, "SEQEND");
            Pair(drawing, 8, layer);
        }

        private static void appendText(StringBuilder drawing, ElementModel element, TextModel text, double microns)
        {
            string says = "";

            if (text.TextBody.STRING?.Data is AsciiData ascii)
                says = ascii.Value;

            if (says.Length == 0)
                return;

            var points = PointsOf(element);

            if (points.Count == 0)
                return;

            Pair(drawing, 0, "TEXT");
            Pair(drawing, 8, LayerOf(element));
            Pair(drawing, 10, points[0].X * microns, Coordinate);
            Pair(drawing, 20, points[0].Y * microns, Coordinate);

            //A height, because a DXF text with none is a text nothing draws. GDSII says how big a label is
            //through a magnification on a font size the format never states, so there is no height to carry
            //across - this is the one the 2D and 3D views draw labels at, in microns.
            Pair(drawing, 40, SvgWriter.LabelFontSize * microns, Coordinate);

            Pair(drawing, 1, says);
        }

        ///<summary>A placement, with its transform - and its repeats when it is a whole array.</summary>
        private static void appendPlacement(
            StringBuilder drawing,
            ElementModel element,
            Record? sname,
            double microns,
            int columns,
            int rows,
            double across,
            double down)
        {
            if (sname?.Data is not AsciiData named || named.Value.Length == 0)
                return;

            var points = PointsOf(element);

            if (points.Count == 0)
                return;

            (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(element);

            if (columns > 1 || rows > 1)
                Pair(drawing, 0, "MINSERT");
            else
                Pair(drawing, 0, "INSERT");

            Pair(drawing, 8, LayerOf(element));
            Pair(drawing, 2, named.Value);
            Pair(drawing, 10, points[0].X * microns, Coordinate);
            Pair(drawing, 20, points[0].Y * microns, Coordinate);

            //
            //A reflection, as the negative scale DXF spells one with.
            //
            //GDSII reflects about the X axis and then rotates; DXF has no flag, so the reflection becomes a
            //negative Y scale and the angle is unchanged. That is the reader's own second case run
            //backwards - see DxfReader.MirrorOf.
            //
            Pair(drawing, 41, magnification, Coordinate);

            if (mirrored)
                Pair(drawing, 42, -magnification, Coordinate);
            else
                Pair(drawing, 42, magnification, Coordinate);

            Pair(drawing, 43, magnification, Coordinate);

            if (angle != 0)
                Pair(drawing, 50, angle, Coordinate);

            if (columns > 1 || rows > 1)
            {
                Pair(drawing, 70, columns);
                Pair(drawing, 71, rows);
                Pair(drawing, 44, across, Coordinate);
                Pair(drawing, 45, down, Coordinate);
            }
        }

        ///
        ///An array, as one repeated insert when its axes are the drawing's and as one insert each when not.
        ///
        ///**MINSERT spaces along the block's own axes and can say nothing else.** A GDSII AREF carries a
        ///vector per axis and so can lay a grid out at any angle or shear it; the two agree exactly when
        ///the columns run along X and the rows along Y, which is what nearly every array in a real file
        ///does. When they do not, one entity per position says the same thing and says it correctly.
        ///
        private static void appendArray(StringBuilder drawing, ElementModel element, ArefModel aref, double microns)
        {
            var points = PointsOf(element);

            if (points.Count < 3 || aref.COLROW.Data is not Int2Data counts || counts.Values.Length < 2)
                return;

            int columns = Math.Max(1, (int)counts.Values[0]);
            int rows = Math.Max(1, (int)counts.Values[1]);

            //The three points are the origin, where the columns end, and where the rows end - so each step
            //is the difference over how many of them there are.
            double columnX = (points[1].X - (double)points[0].X) / columns;
            double columnY = (points[1].Y - (double)points[0].Y) / columns;

            double rowX = (points[2].X - (double)points[0].X) / rows;
            double rowY = (points[2].Y - (double)points[0].Y) / rows;

            if (columnY == 0 && rowX == 0)
            {
                appendPlacement(drawing, element, aref.SNAME, microns, columns, rows, columnX * microns, rowY * microns);

                return;
            }

            //Not axis-aligned, so one insert per position - which is what the array actually is.
            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    double x = points[0].X + (columnX * column) + (rowX * row);
                    double y = points[0].Y + (columnY * column) + (rowY * row);

                    appendAt(drawing, element, aref.SNAME, microns, x, y);
                }
            }
        }

        ///<summary>One placement of an array's block, at a position worked out rather than read.</summary>
        private static void appendAt(StringBuilder drawing, ElementModel element, Record sname, double microns, double x, double y)
        {
            if (sname.Data is not AsciiData named || named.Value.Length == 0)
                return;

            (bool mirrored, double angle, double magnification) = Hierarchy.TransformOf(element);

            Pair(drawing, 0, "INSERT");
            Pair(drawing, 8, LayerOf(element));
            Pair(drawing, 2, named.Value);
            Pair(drawing, 10, x * microns, Coordinate);
            Pair(drawing, 20, y * microns, Coordinate);
            Pair(drawing, 41, magnification, Coordinate);

            if (mirrored)
                Pair(drawing, 42, -magnification, Coordinate);
            else
                Pair(drawing, 42, magnification, Coordinate);

            Pair(drawing, 43, magnification, Coordinate);

            if (angle != 0)
                Pair(drawing, 50, angle, Coordinate);
        }

        #endregion **************************************************************************



        #region Reading the library *********************************************************

        ///<summary>An element's layer and datatype, or null for the ones that have none.</summary>
        public static LayerKey? KeyOf(ElementModel element)
        {
            if (element.Element is not IHasLayer layered)
                return null;

            if (layered.LAYER?.Data is not Int2Data layer)
                return null;

            short dataType = 0;

            if (layered.DataTypeRecord?.Data is Int2Data given && given.Value >= 0)
                dataType = given.Value;

            return new LayerKey(layer.Value, dataType);
        }

        ///
        ///The DXF layer an element goes on.
        ///
        ///A placement has none - GDSII does not put an SREF on a layer - and DXF requires every entity to
        ///name one, so those go on layer `0`, which is the layer every DXF has and the one a block
        ///reference conventionally sits on.
        ///
        private static string LayerOf(ElementModel element)
        {
            if (KeyOf(element) is LayerKey key)
                return LayerName(key.Number, key.DataType);

            return "0";
        }

        private static int WidthOf(PathModel path)
        {
            if (path.WIDTH?.Data is Int4Data width && width.Values.Length > 0 && width.Values[0] > 0)
                return width.Values[0];

            return 0;
        }

        private static List<Element.Point> PointsOf(ElementModel element)
        {
            var points = new List<Element.Point>();

            if (element.Element.XY?.Data is not Int4Data xy)
                return points;

            for (int i = 0; i + 1 < xy.Values.Length; i += 2)
                points.Add(new Element.Point(xy.Values[i], xy.Values[i + 1]));

            return points;
        }

        #endregion **************************************************************************



        #region The pairs *******************************************************************

        ///
        ///One group code and its value, which is the whole of a DXF's structure.
        ///
        ///Two lines each, and the code left-padded to three columns the way every writer does it - not
        ///because anything reads the padding, but because a person opening the file in a text editor is one
        ///of the reasons to write the text flavor at all.
        ///
        private static void Pair(StringBuilder drawing, int code, string value)
        {
            drawing.Append(code.ToString(CultureInfo.InvariantCulture).PadLeft(3));
            drawing.Append('\n');
            drawing.Append(value);
            drawing.Append('\n');
        }

        private static void Pair(StringBuilder drawing, int code, int value)
        {
            Pair(drawing, code, value.ToString(CultureInfo.InvariantCulture));
        }

        ///<summary>A real, invariant - these are numbers in a data file rather than prose.</summary>
        private static void Pair(StringBuilder drawing, int code, double value, string format)
        {
            Pair(drawing, code, value.ToString(format, CultureInfo.InvariantCulture));
        }

        #endregion **************************************************************************
    }
}
