namespace GdsII
{
    ///
    ///The grid a layout was drawn on, worked out from the layout itself.
    ///
    ///**Nothing in a GDSII file says what its grid is.** The format records a database unit and then whole
    ///numbers of them, and every tool that made the file was snapping to something coarser - a manufacturing
    ///grid, a routing pitch, whatever the PDK asked for. That number is not written down anywhere, but it is
    ///recoverable: if every coordinate in the file is a multiple of some distance, that distance is at worst
    ///a multiple of the grid the file was drawn on, and in practice it is the grid.
    ///
    ///Worth recovering because the alternative is a round number that has nothing to do with the file. A
    ///grid of one micron over a layout whose coordinates all divide by five nanometers draws lines that
    ///cross the geometry rather than running along it, and snapping to it moves every corner it touches off
    ///the grid the file was actually built on. Which is the opposite of what snapping is for.
    ///
    public static class Grid
    {
        ///
        ///The coarsest grid every coordinate in the library sits on, in database units. One when there is
        ///nothing to go on.
        ///
        ///The greatest common divisor of the coordinates themselves rather than of the distances between
        ///them, because snapping lands on multiples counted from the origin - which is where the file counts
        ///from too. A layout whose shapes are five apart but all sitting on an odd offset is on a grid of
        ///one as far as anything that snaps is concerned, and saying otherwise would move it.
        ///
        ///Zeros contribute nothing, since everything divides zero. A library of nothing but the origin comes
        ///back as one for the same reason.
        ///
        public static int Of(GDS? gds)
        {
            if (gds is null)
                return 1;

            int divisor = 0;

            foreach (var structure in gds.StreamFormat.Structures)
            {
                foreach (var element in structure.Elements)
                {
                    if (element.Element.XY?.Data is not Int4Data xy)
                        continue;

                    foreach (int coordinate in xy.Values)
                    {
                        divisor = greatestCommonDivisor(divisor, Math.Abs((long)coordinate));

                        //As coarse as it can get, and no run of further coordinates will make it coarser.
                        if (divisor == 1)
                            return 1;
                    }
                }
            }

            if (divisor < 1)
                return 1;

            return divisor;
        }

        ///
        ///The pitch a file should open on, in database units.
        ///
        ///**The file's own grid, raised by tens until it is worth drawing.** Snapping to <see cref="Of"/>
        ///directly is right and drawing it is not: on the bundled cell that grid is five units, which at the
        ///opening fit puts a line every 0.73 pixels - a wash of color rather than a grid, and 178 of them.
        ///A tenth of that many is a grid you can read and count against.
        ///
        ///So the pitch stays a whole multiple of what the file was built on - nothing is ever placed off the
        ///grid the file already sits on, which is the whole reason for reading it - and the multiple is
        ///chosen against the size of the layout rather than against the zoom, because a pitch is a stored
        ///setting and one that moved when you scrolled would be a different control.
        ///
        ///A five-hundredth of the longer side is the target: it puts roughly five heavy lines across the
        ///view at the opening fit, which is about what a micron gave on this cell and is what "a grid" looks
        ///like. Mosfet is 2,800 units across and drawn on five, so it opens on fifty - 0.05 µm.
        ///
        ///Ten rather than one-two-five: a decimal step keeps the readout a round number in whatever unit is
        ///chosen, and a grid of 25 nm reads as arbitrary where 50 does not.
        ///
        public static int Opening(GDS? gds, long across)
        {
            int own = Of(gds);

            if (across <= 0)
                return own;

            long pitch = own;

            //
            //Multiplied out rather than dividing, which is what the first version did and got wrong.
            //
            //`across / 500` on Mosfet is 2800/500, and in whole numbers that is five rather than 5.6 - so a
            //grid of five looked like it had already reached a five-hundredth and the file opened on the
            //pitch that draws 178 lines. Comparing `pitch * 500` against the layout asks the same question
            //with nothing thrown away.
            //
            //Bounded by the layout, so the loop cannot run away: once a pitch is as wide as the whole thing
            //it is certainly a five-hundredth of it.
            while (pitch * 500 < across && pitch < across)
                pitch *= 10;

            if (pitch > int.MaxValue)
                return int.MaxValue;

            return (int)pitch;
        }

        ///<summary>Taken in long, because a coordinate negated can be one past what an int holds.</summary>
        private static int greatestCommonDivisor(long left, long right)
        {
            while (right != 0)
            {
                long carried = left % right;

                left = right;
                right = carried;
            }

            if (left > int.MaxValue)
                return int.MaxValue;

            return (int)left;
        }
    }
}
