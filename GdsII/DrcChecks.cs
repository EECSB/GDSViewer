namespace GdsII
{
    ///<summary>
    ///The geometric questions a rule asks, answered by sizing and set operations.
    ///
    ///**This is DRC in the square metric, reporting regions rather than edge pairs.** That is a real
    ///description rather than an apology: mitered sizing on rectilinear geometry is exactly the Minkowski
    ///sum with a square, which is exactly the square metric - one of the three KLayout names, alongside
    ///Euclidean and projection. It parts company with Euclidean only on edges that are not axis-aligned.
    ///
    ///What it does not give is an **edge pair**. A real engine answers "these two edges are 130 apart";
    ///this answers "this region is too narrow", which is enough to see and to click on and not enough to
    ///express a rule qualified by edge direction. Those rules are refused by name rather than approximated -
    ///see <see cref="DrcDeck.Refused"/>.
    ///
    ///**Exactly at the limit passes.** A minimum width of 140 forbids 139 and allows 140, and a checker
    ///that flagged every minimum-width wire would be useless, since drawing to minimum is what layout is.
    ///The cost of guaranteeing that on an integer grid is named on <see cref="narrowerThan"/>: for an
    ///**even** limit, a width or gap of exactly one database unit under it is missed. On a layout snapped
    ///to a manufacturing grid - five units on sky130 - no such width can exist, so the gap is unreachable
    ///rather than merely small.
    ///</summary>
    public static class DrcChecks
    {
        #region How far to size *************************************************************

        ///<summary>
        ///The radius for an opening or a closing: shrink by this and grow back, and what did not survive
        ///was narrower than the limit.
        ///
        ///A shape of width w disappears when shrunk by r on both sides exactly when w is at most 2r, so to
        ///flag everything below the limit and nothing at it, 2r wants to be limit - 1. That is only whole
        ///when the limit is odd. For an even limit this rounds **down**, which misses a width of exactly
        ///limit - 1 and never reports one that is legal - the safe direction of the two, because a false
        ///report at the minimum would fire on most of the layout while the miss is a single unit wide.
        ///</summary>
        private static int narrowerThan(long limit)
        {
            return (int)((limit - 1) / 2);
        }

        ///<summary>
        ///The radius for a gap measured between two layers, where both sides are grown into it.
        ///
        ///Two shapes a gap g apart have grown regions that overlap in area exactly when g is less than 2r,
        ///so 2r wants to be the limit itself. Whole when the limit is even, and rounding down for an odd one
        ///misses a gap of limit - 1 the same way and in the same direction.
        ///</summary>
        private static int apartBy(long limit)
        {
            return (int)(limit / 2);
        }

        //
        //Why an opening grows back one unit further than it shrank, and a closing shrinks back one further.
        //
        //**Because shrinking and growing by the same amount does not return the same shape.** Clipper works
        //in integers and a mitered offset of a 45-degree edge lands between them, so the corner is rounded -
        //inward on the way in and inward again on the way out. Measured on one regular octagon 400 across:
        //shrunk by 24 and grown back by 24 it comes back 359 square units smaller, a ring about a third of a
        //unit thick all the way round. `merged NOT opened` then reports that ring as a width violation.
        //
        //It is invisible on rectilinear geometry, where every offset lands on an integer and the same
        //octagon test on a square loses exactly nothing - which is why sky130 layouts never showed it and a
        //generated layout of octagons showed 188,742 of them. KLayout, asked about that octagon, says there
        //is no violation, and so does this project's own edge engine.
        //
        //One unit is the smallest amount that covers a sub-unit rounding, and clipping the result back to
        //the shape stops the extra unit from reaching outside it. What it costs is that a neck within one
        //unit of the limit may be recovered and not reported, which is the same one-unit gap the halving
        //already has and is documented on narrowerThan.
        //

        private static List<List<Element.Point>> none()
        {
            return new List<List<Element.Point>>();
        }

        #endregion **************************************************************************



        #region Width and spacing ***********************************************************

        ///<summary>
        ///Where a layer is narrower than the limit.
        ///
        ///An opening: shrunk by half the limit and grown back, a neck too thin to survive the shrink does
        ///not come back, and what the layer has that the opened version does not is the offending region.
        ///</summary>
        public static List<List<Element.Point>> Width(IEnumerable<IReadOnlyList<Element.Point>> shapes, long limit)
        {
            int radius = narrowerThan(limit);

            if (radius <= 0)
                return none();

            var merged = Booleans.Merge(shapes);

            //Grown back one unit further than it was shrunk, and clipped to the shape. See the note on rounding
            //above narrowerThan.
            var opened = Booleans.Combine(
                Booleans.Grow(Booleans.Grow(merged, -radius), radius + 1),
                merged,
                BooleanOperation.And);

            return Booleans.Combine(merged, opened, BooleanOperation.Not);
        }

        ///<summary>
        ///Where one layer comes closer to itself than the limit - between two shapes, and inside the notch
        ///of one.
        ///
        ///A closing, which is the opening backwards: grown by half the limit and shrunk back, a gap too
        ///narrow to survive the growth is filled in and does not reopen. What the closed version has that
        ///the layer does not is the gap that was too tight.
        ///
        ///Both kinds at once, deliberately. <see cref="Notch(IEnumerable{IReadOnlyList{Element.Point}}, long)"/>
        ///is the same measurement with the ones bounded by a single shape kept.
        ///</summary>
        public static List<List<Element.Point>> Space(IEnumerable<IReadOnlyList<Element.Point>> shapes, long limit)
        {
            int radius = narrowerThan(limit);

            if (radius <= 0)
                return none();

            var merged = Booleans.Merge(shapes);

            //Shrunk back one unit further than it was grown, and the shape put back. The mirror of the
            //opening's slack.
            var closed = Booleans.Combine(
                Booleans.Grow(Booleans.Grow(merged, radius), -(radius + 1)),
                merged,
                BooleanOperation.Or);

            return Booleans.Combine(closed, merged, BooleanOperation.Not);
        }

        ///<summary>
        ///Where two layers come closer to each other than the limit.
        ///
        ///**Measured in the ground outside both**, which is what makes this survive layers that overlap.
        ///Growing one layer and intersecting the other reports the whole ring inside an implant around the
        ///diffusion it covers - a rule about how close two things come, answered with the place they are
        ///deliberately on top of each other. Both are grown into the gap instead and everything either of
        ///them covers is taken back off, so what is left is empty ground too tight to be there.
        ///</summary>
        public static List<List<Element.Point>> Space(
            IEnumerable<IReadOnlyList<Element.Point>> one,
            IEnumerable<IReadOnlyList<Element.Point>> other,
            long limit)
        {
            int radius = apartBy(limit);

            if (radius <= 0)
                return none();

            var first = Booleans.Merge(one);
            var second = Booleans.Merge(other);

            var reaching = Booleans.Combine(
                Booleans.Grow(first, radius),
                Booleans.Grow(second, radius),
                BooleanOperation.And);

            var either = Booleans.Combine(first, second, BooleanOperation.Or);

            return Booleans.Combine(reaching, either, BooleanOperation.Not);
        }

        ///<summary>
        ///Where a single shape comes closer to itself than the limit - the inside of a U, not the gap
        ///between two separate pieces.
        ///
        ///The gaps <see cref="Space(IEnumerable{IReadOnlyList{Element.Point}}, long)"/> finds, kept when the
        ///shape on each side of them is the same one. A
        ///gap grown by a single unit touches whatever bounds it, so counting how many of the merged pieces
        ///it reaches says which kind it is: one is a notch, two or more is two shapes too close together.
        ///
        ///A boolean per gap per piece, which is the expensive way round. Worth knowing before this is
        ///pointed at a full chip; a notch rule is usually written against one layer of a cell.
        ///</summary>
        public static List<List<Element.Point>> Notch(IEnumerable<IReadOnlyList<Element.Point>> shapes, long limit)
        {
            var merged = Booleans.Merge(shapes);

            var gaps = Space(merged, limit);

            var notches = new List<List<Element.Point>>();

            foreach (var gap in gaps)
            {
                if (piecesTouching(gap, merged) == 1)
                    notches.Add(gap);
            }

            return notches;
        }

        private static int piecesTouching(List<Element.Point> gap, List<List<Element.Point>> pieces)
        {
            //One unit, the way a traced net decides two shapes abut: coordinates are whole, so anything
            //this close was meant to be touching.
            var reaching = Booleans.Grow(new[] { gap }, 1);

            int touching = 0;

            foreach (var piece in pieces)
            {
                if (Booleans.Combine(reaching, new[] { piece }, BooleanOperation.And).Count > 0)
                    touching++;
            }

            return touching;
        }

        #endregion **************************************************************************



        #region Enclosure and extension *****************************************************

        ///<summary>
        ///Where the second layer fails to surround the first by the limit.
        ///
        ///**Exact, unlike the width and spacing checks**, because only one layer is grown and there is no
        ///half distance to round: the inner layer grown by the limit is where the outer one has to reach,
        ///and whatever sticks out is the shortfall. Grown by exactly the enclosure it has, the two
        ///boundaries coincide and nothing is left over, so an enclosure exactly at the limit passes.
        ///</summary>
        public static List<List<Element.Point>> Enclosure(
            IEnumerable<IReadOnlyList<Element.Point>> inner,
            IEnumerable<IReadOnlyList<Element.Point>> outer,
            long limit)
        {
            var reach = Booleans.Grow(Booleans.Merge(inner), (int)limit);

            return Booleans.Combine(reach, Booleans.Merge(outer), BooleanOperation.Not);
        }

        //
        //There is no extension check here, and the absence is the finding.
        //
        //One was written and removed. It read Grow(past, n) NOT reaching, which is Enclosure with its two
        //arguments swapped - the same six lines, measuring in every direction at once. Every extension rule
        //a real deck carries is directional: an endcap is poly reaching past diffusion at the two ends of a
        //channel, and says nothing about its sides.
        //
        //Run against a transistor, the omnidirectional version reported the sides and only the sides. Both
        //of sky130's extension rules came back with two violations each, every one of them the axis the
        //rule does not mean and none of them the axis it does. That is not a check with noise in it; it is
        //a check whose entire output was wrong, and a report nobody can act on is worse than one that says
        //it did not look.
        //
        //Direction needs edge pairs, which DrcEdges now has - so this is where an extension check would go
        //when one is written against it, rather than a hole nobody remembers the reason for.
        //
        #endregion **************************************************************************



        #region Area ************************************************************************

        ///<summary>
        ///Shapes covering less ground than the limit, in square database units.
        ///
        ///Merged first, or two overlapping shapes would each be measured on their own and a piece of metal
        ///large enough between them would be reported twice as too small. A hole is ground the shape does
        ///not cover and comes off the total, which is what makes this the area a rule means rather than the
        ///area of the outline.
        ///</summary>
        public static List<List<Element.Point>> Area(IEnumerable<IReadOnlyList<Element.Point>> shapes, long limit)
        {
            var small = new List<List<Element.Point>>();

            foreach (var ring in Booleans.MergeToRings(shapes))
            {
                double area = Measure.AreaOf(ring.Boundary);

                foreach (var hole in ring.Holes)
                    area -= Measure.AreaOf(hole);

                if (area < limit)
                    small.Add(ring.Boundary);
            }

            return small;
        }

        ///<summary>
        ///Holes smaller than the limit.
        ///
        ///Stated apart from the area of a shape because real decks state it apart - sky130 gives hvtp.5 and
        ///hvtp.6 the same number for two different things. A gap left in an implant is as much a
        ///manufacturing problem as an island of one, and neither figure says anything about the other.
        ///</summary>
        public static List<List<Element.Point>> HoleArea(IEnumerable<IReadOnlyList<Element.Point>> shapes, long limit)
        {
            var small = new List<List<Element.Point>>();

            foreach (var ring in Booleans.MergeToRings(shapes))
            {
                foreach (var hole in ring.Holes)
                {
                    if (Measure.AreaOf(hole) < limit)
                        small.Add(hole);
                }
            }

            return small;
        }

        ///<summary>
        ///Windows of the layout where the layer covers less than the limit.
        ///
        ///**The marker is the window**, not the geometry in it, because the fault is the emptiness rather
        ///than any shape - there is nothing to outline where a density rule fails. Somebody looking at one
        ///needs to see the square that came up short and what little is inside it.
        ///
        ///<paramref name="tenths"/> is tenths of a percent, so 300 is 30%: a deck holds whole numbers and a
        ///ratio needs a unit that survives being one.
        ///</summary>
        public static List<List<Element.Point>> Density(
            IEnumerable<IReadOnlyList<Element.Point>> shapes,
            int window,
            int step,
            long tenths)
        {
            var merged = Booleans.Merge(shapes);

            if (merged.Count == 0 || window <= 0 || step <= 0)
                return none();

            var extent = Bounds.Empty;

            foreach (var shape in merged)
                extent = extent.Union(Bounds.Of(shape));

            double wanted = tenths / 1000.0;

            var sparse = new List<List<Element.Point>>();

            foreach (var found in Measure.DensityWindows(merged, extent, window, step))
            {
                if (found.Density >= wanted)
                    continue;

                sparse.Add(new List<Element.Point>
                {
                    new Element.Point { X = found.Window.Left, Y = found.Window.Bottom },
                    new Element.Point { X = found.Window.Right, Y = found.Window.Bottom },
                    new Element.Point { X = found.Window.Right, Y = found.Window.Top },
                    new Element.Point { X = found.Window.Left, Y = found.Window.Top }
                });
            }

            return sparse;
        }

        #endregion **************************************************************************



        #region Off the grid ****************************************************************

        ///<summary>
        ///Coordinates that do not sit on the grid the file was drawn on.
        ///
        ///Points rather than regions, because that is what the fault is: there is no area to a corner in the
        ///wrong place.
        ///
        ///**The grid is given rather than recovered.** <see cref="Grid.Of"/> reads back the coarsest grid a
        ///library's coordinates all sit on, which is the right answer for snapping and a circular one here:
        ///it is their greatest common divisor, so a single coordinate at 3 among a file of multiples of 5
        ///drags it to 1 - and nothing is off a grid of 1. The stray coordinate would define away the grid it
        ///was supposed to be caught by. So the caller states the manufacturing grid, which is PDK data like
        ///every other number in a deck.
        ///
        ///A grid of one is still accepted and reports nothing, since every whole coordinate sits on it.
        ///</summary>
        public static List<Element.Point> OffGrid(IEnumerable<Element> elements, int grid)
        {
            var off = new List<Element.Point>();

            if (grid <= 1)
                return off;

            foreach (var element in elements)
            {
                if (!Checkable(element))
                    continue;

                foreach (var point in element.Points)
                {
                    if (IsOffGrid(point, grid))
                        off.Add(point);
                }
            }

            return off;
        }

        ///<summary>
        ///Whether a coordinate misses the grid. Negative coordinates too - the remainder of a negative
        ///number is negative in this language and comparing it against zero is right either way.
        ///</summary>
        public static bool IsOffGrid(Element.Point point, int grid)
        {
            if (grid <= 1)
                return false;

            return point.X % grid != 0 || point.Y % grid != 0;
        }

        ///<summary>
        ///Whether an element's coordinates are geometry that gets manufactured.
        ///
        ///**A label is not.** Its anchor is where a string is drawn on a screen, and nothing on the mask
        ///comes from it - so a label sitting between grid points is not a fault, and reporting one sends
        ///somebody looking for a shape that is not there. The same reasoning that keeps labels out of a
        ///merge, applied to a check that has no area in it at all.
        ///</summary>
        public static bool Checkable(Element element)
        {
            return element.Text is null;
        }

        #endregion **************************************************************************



        #region Exemptions ******************************************************************

        ///<summary>
        ///The violations left once the ground a rule is excepted inside is taken off them.
        ///
        ///The cheap one of the three qualifiers a real deck uses, and the reason it is worth honoring: an
        ///exemption region is one more subtraction, where the other two need edge pairs. A rule exempted
        ///inside a marker is found as usual and then trimmed.
        ///</summary>
        public static List<List<Element.Point>> Outside(
            IEnumerable<IReadOnlyList<Element.Point>> violations,
            IEnumerable<IReadOnlyList<Element.Point>> exempt)
        {
            return Booleans.Combine(violations, exempt, BooleanOperation.Not);
        }

        #endregion **************************************************************************
    }
}
