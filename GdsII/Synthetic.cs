using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///
    ///Layouts made to order, for measuring against.
    ///
    ///**Nothing in this repository is big enough to be slow.** The 897 bundled cells are all under 60 KB, the
    ///largest count any end-to-end test asserts is 74 shapes, and the wall this exists to find sits somewhere
    ///around twenty thousand. So every performance claim about this app has been made about a file that does
    ///not exist, and the first honest step is to be able to make one.
    ///
    ///Generated rather than committed. A half-million-element layout is a large file to keep in a repository
    ///forever, it would be checked by the corpus test on every run, and the one thing a benchmark fixture must
    ///be is *adjustable* - the interesting question is where the curve bends, which needs a family of sizes
    ///rather than one specimen.
    ///
    ///**Two shapes of layout, because the format has two ways to get big.** Written out flat, where the file
    ///is as large as what it draws; and arrayed, where one `AREF` record of a small cell expands on reading
    ///into as much geometry as you like. The second is the case that matters most, and the one that proves
    ///file size is the wrong thing to measure: four kilobytes can become half a million elements.
    ///
    public static class Synthetic
    {
        ///<summary>The GDSII version written into what comes out - release 6, as the other writers use.</summary>
        private const short GdsVersion = 600;

        ///<summary>One database unit in microns. A nanometer, as nearly every real file uses.</summary>
        private const double MicronsPerDatabaseUnit = 0.001;

        public const string TopCell = "TOP";

        public const string LeafCell = "LEAF";

        ///<summary>
        ///How wide one shape is, in database units, and how far apart they sit.
        ///
        ///The pitch is a little *less* than the width on purpose, so neighbors on a row overlap. A layer of
        ///shapes that never touch is a layer `Booleans.MergeByLayer` finishes instantly, which would make the
        ///one measured cliff in the app disappear from the very benchmark meant to find it. Overlap is also
        ///the common case: 171 of the 897 bundled cells have a layer whose shapes overlap.
        ///</summary>
        private const int ShapeWidth = 400;

        private const int Pitch = 360;

        ///
        ///A layout holding <paramref name="perCell"/> shapes, repeated across a
        ///<paramref name="columns"/> by <paramref name="rows"/> array.
        ///
        ///Flattens to `perCell × columns × rows` elements. One column and one row writes the shapes straight
        ///into the top cell, so the file is as large as what it draws; anything more puts them in a leaf and
        ///places it once as an `AREF`, so the file stays tiny however much it draws.
        ///
        ///<paramref name="layers"/> shapes are spread over that many layer numbers, a row at a time rather
        ///than a shape at a time - so neighbors along a row share a layer and overlap, which is what gives
        ///the merge something to do.
        ///
        ///<paramref name="corners"/> is how many corners each shape has. Four is a rectangle, which is most
        ///of what a real layout is; more makes each element's coordinate list longer without changing how many
        ///there are, which is the other axis worth pulling on.
        ///
        public static GDS Layout(int perCell, int columns = 1, int rows = 1, int layers = 8, int corners = 4)
        {
            perCell = Math.Max(1, perCell);
            columns = Math.Max(1, columns);
            rows = Math.Max(1, rows);
            layers = Math.Max(1, layers);
            corners = Math.Max(3, corners);

            var records = new List<Record>
            {
                Hierarchy.Make(RecordType.HEADER, new Int2Data(GdsVersion)),
                Hierarchy.Make(RecordType.BGNLIB, new Int2Data(new short[12])),
                Hierarchy.Make(RecordType.LIBNAME, new AsciiData("LIB")),
                Hierarchy.Make(RecordType.UNITS, new Real8Data(new double[] { MicronsPerDatabaseUnit, MicronsPerDatabaseUnit / 1e6 }))
            };

            bool arrayed = columns > 1 || rows > 1;

            //The leaf comes first, because a cell has to be defined before anything places it.
            if (arrayed)
                appendCell(records, LeafCell, perCell, layers, corners);

            records.Add(Hierarchy.Make(RecordType.BGNSTR, new Int2Data(new short[12])));
            records.Add(Hierarchy.Make(RecordType.STRNAME, new AsciiData(TopCell)));

            if (arrayed)
            {
                int across = Across(perCell);
                int down = (perCell + across - 1) / across;

                //One step past the last, which is what an AREF's XY says - see Hierarchy.AsArray.
                var placement = Hierarchy.PlacementRecords(LeafCell, new Element.Point(0, 0), false, 0);

                var array = Hierarchy.AsArray(
                    placement,
                    columns,
                    rows,
                    (across + 1) * Pitch,
                    0,
                    0,
                    (down + 1) * Pitch);

                if (array is not null)
                    records.AddRange(array);
            }
            else
            {
                appendShapes(records, perCell, layers, corners);
            }

            records.Add(Hierarchy.Make(RecordType.ENDSTR, null));
            records.Add(Hierarchy.Make(RecordType.ENDLIB, null));

            return GDS.FromRecords(records);
        }

        ///<summary>How many shapes go across, so a cell comes out roughly square rather than a long strip.</summary>
        public static int Across(int shapes)
        {
            return Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Math.Max(1, shapes))));
        }

        ///<summary>How many elements <see cref="Layout"/> draws for those arguments, without building it.</summary>
        public static long Drawn(int perCell, int columns = 1, int rows = 1)
        {
            return (long)Math.Max(1, perCell) * Math.Max(1, columns) * Math.Max(1, rows);
        }

        private static void appendCell(List<Record> records, string name, int shapes, int layers, int corners)
        {
            records.Add(Hierarchy.Make(RecordType.BGNSTR, new Int2Data(new short[12])));
            records.Add(Hierarchy.Make(RecordType.STRNAME, new AsciiData(name)));

            appendShapes(records, shapes, layers, corners);

            records.Add(Hierarchy.Make(RecordType.ENDSTR, null));
        }

        private static void appendShapes(List<Record> records, int shapes, int layers, int corners)
        {
            int across = Across(shapes);

            for (int i = 0; i < shapes; i++)
            {
                int column = i % across;
                int row = i / across;

                //By row rather than by shape, so neighbors along a row share a layer and their overlap is
                //real overlap on one layer rather than two layers crossing.
                short layer = (short)(64 + (row % layers));

                records.Add(Hierarchy.Make(RecordType.BOUNDARY, null));
                records.Add(Hierarchy.Make(RecordType.LAYER, new Int2Data(layer)));
                records.Add(Hierarchy.Make(RecordType.DATATYPE, new Int2Data(20)));
                records.Add(Hierarchy.Make(RecordType.XY, new Int4Data(outline(column * Pitch, row * Pitch, corners))));
                records.Add(Hierarchy.Make(RecordType.ENDEL, null));
            }
        }

        ///
        ///One shape's corners, closed.
        ///
        ///Four corners is an axis-aligned rectangle, because that is what a layout is mostly made of and a
        ///rounded one would measure the polygon path rather than the common one. More than four is a regular
        ///polygon inscribed in the same box - the same shape the ellipse tool draws, and the same thing a
        ///rounded corner or a converted arc turns into.
        ///
        private static int[] outline(int left, int bottom, int corners)
        {
            if (corners == 4)
            {
                int right = left + ShapeWidth;
                int top = bottom + ShapeWidth;

                return new int[] { left, bottom, right, bottom, right, top, left, top, left, bottom };
            }

            double centerX = left + (ShapeWidth / 2.0);
            double centerY = bottom + (ShapeWidth / 2.0);
            double radius = ShapeWidth / 2.0;

            var points = new int[(corners + 1) * 2];

            for (int i = 0; i < corners; i++)
            {
                double angle = i * 2 * Math.PI / corners;

                points[i * 2] = (int)Math.Round(centerX + (radius * Math.Cos(angle)));
                points[(i * 2) + 1] = (int)Math.Round(centerY + (radius * Math.Sin(angle)));
            }

            //Closed, which is what every reader expects of a boundary.
            points[corners * 2] = points[0];
            points[(corners * 2) + 1] = points[1];

            return points;
        }
    }
}
