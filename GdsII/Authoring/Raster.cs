namespace GdsII
{
    ///
    ///A picture made of squares, as outlines a layout can hold - which is what text and a logo both are by the
    ///time they reach silicon.
    ///
    ///**Merged, not heaped.** Every lit pixel as a square of its own would be a file of thousands of boundaries
    ///where a letter wants three, and a checker would measure the seams between them. So each run of lit pixels
    ///along a row becomes one rectangle and the rectangles are merged into the outlines they make together -
    ///<see cref="Booleans.Merge(IEnumerable{IReadOnlyList{Element.Point}})"/>, which also cuts any hole, the
    ///inside of an O, into the keyhole a GDSII boundary has to be.
    ///
    public static class Raster
    {
        ///
        ///The lit pixels of a bitmap as polygons, each pixel a square <paramref name="pixel"/> database units on a side.
        ///
        ///The bitmap is indexed [row, column] with row 0 at the top, the way a picture is read; the layout's y
        ///runs up, so row 0 lands highest. The bitmap's bottom left corner is the origin.
        ///
        ///Nothing lit is nothing returned, and so is a pixel of no size.
        ///
        public static List<List<Element.Point>> Polygons(bool[,] lit, int pixel)
        {
            if (pixel <= 0)
                return new List<List<Element.Point>>();

            int rows = lit.GetLength(0);
            int columns = lit.GetLength(1);

            var runs = new List<IReadOnlyList<Element.Point>>();

            for (int row = 0; row < rows; row++)
            {
                int bottom = (rows - 1 - row) * pixel;
                int column = 0;

                while (column < columns)
                {
                    if (!lit[row, column])
                    {
                        column++;

                        continue;
                    }

                    int start = column;

                    while (column < columns && lit[row, column])
                        column++;

                    runs.Add(Rectangle(start * pixel, bottom, column * pixel, bottom + pixel));
                }
            }

            if (runs.Count == 0)
                return new List<List<Element.Point>>();

            return Booleans.Merge(runs);
        }

        ///
        ///A picture written as rows of characters, # for a lit pixel and anything else for one that is not.
        ///
        ///The form the browser hands a decoded picture back in - a canvas is the only thing in the app that can
        ///read a PNG, and a row of characters is what crosses the interop plainly - and the form a test can draw
        ///one in.
        ///
        ///Ragged rows are padded out to the longest with unlit pixels, so a short row is a row with less lit in
        ///it rather than a picture that cannot be read.
        ///
        public static bool[,] FromRows(IReadOnlyList<string> rows)
        {
            int columns = 0;

            foreach (string row in rows)
                columns = Math.Max(columns, row.Length);

            var lit = new bool[rows.Count, columns];

            for (int row = 0; row < rows.Count; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                    lit[row, column] = rows[row][column] == '#';
            }

            return lit;
        }

        ///
        ///A bitmap turned a quarter to the right as it is read, so many times - its rows becoming its columns, the
        ///first row the last column. Negative turns it to the left, and any number is taken round the four.
        ///
        ///For writing or a picture turned where it lies; see the 2D view's Piece. A new bitmap, the one given left as it was.
        ///
        public static bool[,] Turned(bool[,] lit, int quarters)
        {
            int turns = ((quarters % 4) + 4) % 4;
            var turned = lit;

            for (int turn = 0; turn < turns; turn++)
                turned = QuarterRight(turned);

            if (ReferenceEquals(turned, lit))
                return (bool[,])lit.Clone();

            return turned;
        }

        ///<summary>A bitmap mirrored top to bottom: its last row first. A new bitmap, the one given left as it was.</summary>
        public static bool[,] MirroredDown(bool[,] lit)
        {
            int rows = lit.GetLength(0);
            int columns = lit.GetLength(1);

            var mirrored = new bool[rows, columns];

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                    mirrored[row, column] = lit[rows - 1 - row, column];
            }

            return mirrored;
        }

        ///<summary>One quarter to the right: what was the bottom row is the first column, read from the top.</summary>
        private static bool[,] QuarterRight(bool[,] lit)
        {
            int rows = lit.GetLength(0);
            int columns = lit.GetLength(1);

            var turned = new bool[columns, rows];

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                    turned[column, rows - 1 - row] = lit[row, column];
            }

            return turned;
        }

        ///<summary>A closed rectangle from its two corners, the way a boundary is written.</summary>
        private static List<Element.Point> Rectangle(int left, int bottom, int right, int top)
        {
            return new List<Element.Point>
            {
                new Element.Point(left, bottom),
                new Element.Point(right, bottom),
                new Element.Point(right, top),
                new Element.Point(left, top),
                new Element.Point(left, bottom)
            };
        }
    }
}
