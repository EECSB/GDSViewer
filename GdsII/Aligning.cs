namespace GdsII
{
    ///<summary>
    ///Which edge of a set of shapes to bring into line.
    ///
    ///**Named by the coordinate, not by a side of the screen.** Which way "top" points depends on which way
    ///the view draws Y, and a layout format and a screen do not agree about that - so a name borrowed from
    ///one of them is wrong in the other. Whoever draws the button decides what to call it; this only says
    ///which number is being made equal. The same reasoning as <see cref="Turn"/>.
    ///</summary>
    public enum Edge
    {
        LeastX,
        MiddleX,
        MostX,
        LeastY,
        MiddleY,
        MostY
    }

    ///<summary>Which way a row of shapes is being spaced out.</summary>
    public enum Along
    {
        X,
        Y
    }

    ///<summary>
    ///Bringing a set of shapes into line, and spacing them out evenly.
    ///
    ///Boxes in and offsets out, one per box in the order they were given. Nothing here knows what a shape is
    ///or which cell it belongs to - it is arithmetic over rectangles, which is what makes it checkable
    ///without a layout in front of it.
    ///</summary>
    public static class Aligning
    {
        ///
        ///How far each box has to move for them all to line up on one edge.
        ///
        ///**Against the whole set, not against one of them.** Lining up on the leftmost left edge is what
        ///somebody who selected a handful of shapes and pressed "left" means; picking one of them to be the
        ///one that stays would need a way to say which, and the answer would still usually be "the leftmost".
        ///
        ///A box that is already on the edge comes back as no offset at all, so a set that is already lined up
        ///produces nothing to do rather than a row of moves by zero.
        ///
        public static IReadOnlyList<(int Dx, int Dy)> Aligned(IReadOnlyList<Bounds> boxes, Edge edge)
        {
            var offsets = new List<(int Dx, int Dy)>();

            if (boxes.Count == 0)
                return offsets;

            var whole = Bounds.Empty;

            foreach (var box in boxes)
                whole = whole.Union(box);

            foreach (var box in boxes)
            {
                if (edge == Edge.LeastX)
                    offsets.Add((whole.Left - box.Left, 0));
                else if (edge == Edge.MostX)
                    offsets.Add((whole.Right - box.Right, 0));
                else if (edge == Edge.MiddleX)
                    offsets.Add((middleOffset(whole.Left, whole.Right, box.Left, box.Right), 0));
                else if (edge == Edge.LeastY)
                    offsets.Add((0, whole.Bottom - box.Bottom));
                else if (edge == Edge.MostY)
                    offsets.Add((0, whole.Top - box.Top));
                else
                    offsets.Add((0, middleOffset(whole.Bottom, whole.Top, box.Bottom, box.Top)));
            }

            return offsets;
        }

        ///<summary>
        ///How far a box has to move for its middle to sit on the middle of the whole set.
        ///
        ///Worked out as a doubled distance and halved once at the end, so a box whose middle falls between
        ///two units is rounded once rather than twice - rounding each middle first and subtracting them
        ///leaves shapes a unit apart from each other for no reason anybody could point at.
        ///</summary>
        private static int middleOffset(long wholeLeast, long wholeMost, long boxLeast, long boxMost)
        {
            long doubled = (wholeLeast + wholeMost) - (boxLeast + boxMost);

            return (int)Math.Round(doubled / 2.0, MidpointRounding.AwayFromZero);
        }

        ///
        ///How far each box has to move for their middles to be evenly spaced.
        ///
        ///**Evenly spaced middles, not equal gaps between the edges.** For boxes of one size the two are the
        ///same answer, which is most of what gets spaced out on a chip - a row of vias, a row of pins. Where
        ///they differ, this is the one that behaves: chip geometry overlaps by design, every contact sitting
        ///inside the metal it connects, and for boxes that overlap there is no free space to divide. Equal
        ///gaps then works out a *negative* gap and marches the middle ones outward past the two on the ends,
        ///which is how a row of overlapping layers ends up flung across the cell by a button labeled "space
        ///out". Middles cannot do that: every one of them lands between the outermost two by construction.
        ///
        ///**The two on the ends do not move.** They are what the spacing is measured between, so moving them
        ///would change the answer to the question being asked - and it is what makes pressing the button a
        ///second time do nothing.
        ///
        ///Fewer than three boxes has nothing between the ends to space out, and comes back as nothing to do.
        ///
        public static IReadOnlyList<(int Dx, int Dy)> SpacedOut(IReadOnlyList<Bounds> boxes, Along along)
        {
            var offsets = new List<(int Dx, int Dy)>();

            foreach (var box in boxes)
                offsets.Add((0, 0));

            if (boxes.Count < 3)
                return offsets;

            //In the order they sit, which is not the order they were chosen in.
            var order = new List<int>();

            for (int i = 0; i < boxes.Count; i++)
                order.Add(i);

            order.Sort((left, right) => doubledMiddle(boxes[left], along).CompareTo(doubledMiddle(boxes[right], along)));

            //Doubled throughout, because a box of odd width has no whole-numbered middle of its own - and
            //halving each one before spacing them would round every box twice for no reason.
            long first = doubledMiddle(boxes[order[0]], along);
            long last = doubledMiddle(boxes[order[^1]], along);

            double step = (last - first) / (double)(order.Count - 1);

            for (int i = 1; i < order.Count - 1; i++)
            {
                int which = order[i];

                int shift = (int)Math.Round((first + (step * i) - doubledMiddle(boxes[which], along)) / 2.0,
                    MidpointRounding.AwayFromZero);

                if (along == Along.X)
                    offsets[which] = (shift, 0);
                else
                    offsets[which] = (0, shift);
            }

            return offsets;
        }

        ///<summary>Twice a box's middle along that axis, which is a whole number where the middle may not be.</summary>
        private static long doubledMiddle(Bounds box, Along along)
        {
            if (along == Along.X)
                return box.Left + box.Right;

            return box.Bottom + box.Top;
        }
    }
}
