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
    ///**And corners put on a grid**: a manufacturing grid, the one a deck's off-grid rules ask a layer's
    ///corners to sit on - see <see cref="DrcDeck.GridFor"/>. For corners somebody worked out rather than
    ///placed: a circle's, an arc's, a stroke of lettering's land wherever the arithmetic puts them, and
    ///rounded to the database unit that is nearly always between two grid points. Rounded to the grid
    ///instead, they are where the check wants them. What a grid can do to a shape is the other half: corners
    ///a fraction of a step apart land on one point, which <see cref="Snap(IReadOnlyList{Element.Point}, int)"/>
    ///drops, and a corner within a step of an edge it is not on can land across it, which
    ///<see cref="Crossing"/> finds. Whether that is a refusal or a shape left as it was is the caller's to say.
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

        ///<summary>A number of database units on the nearest multiple of the grid, half a step going away from zero as a unit rounds.</summary>
        public static int Snap(double units, int grid)
        {
            int step = Math.Max(1, grid);

            return (int)(Math.Round(units / step, MidpointRounding.AwayFromZero) * step);
        }

        public static Element.Point Snap(Element.Point point, int grid)
        {
            return new Element.Point(Snap(point.X, grid), Snap(point.Y, grid));
        }

        ///<summary>
        ///An outline's corners each on the nearest point of the grid, with the repeats that makes dropped, and
        ///closed again if it came closed. On a grid of one, the outline as it was.
        ///</summary>
        public static List<Element.Point> Snap(IReadOnlyList<Element.Point> outline, int grid)
        {
            if (grid <= 1)
                return outline.ToList();

            bool closed = outline.Count > 1 && outline[0].Equals(outline[^1]);
            var moved = new List<Element.Point>(outline.Count);

            foreach (var corner in outline)
            {
                var at = Snap(corner, grid);

                if (moved.Count == 0 || !moved[^1].Equals(at))
                    moved.Add(at);
            }

            while (moved.Count > 1 && moved[0].Equals(moved[^1]))
                moved.RemoveAt(moved.Count - 1);

            if (closed && moved.Count > 0)
                moved.Add(moved[0]);

            return moved;
        }

        ///
        ///The outline a path is checked by, when the grid cannot hold the path as one - with which of its parts put
        ///that outline off it, in words: "slanted runs and round ends". Null when the path can stay a path: there is
        ///no grid, it has no width, nothing about it was worked out, or its outline is on the grid anyway.
        ///
        ///**The outline is what a check reads, not the centerline.** It is offset half the width either side of
        ///every corner, and where the path bends, runs on a slant or ends round, that offset lands between grid
        ///points whatever the centerline is on - so a centerline on the grid is not enough. The outline put on the
        ///grid is: drawn as a polygon, it covers what the path would have, to within half a step.
        ///
        ///**Only for the parts that were worked out.** A path square to the axes whose outline is off the grid is
        ///off it because of the numbers it was given - a width of an odd number of steps, a point between two - and
        ///those are whoever drew it to change, as a rectangle's corners are. It stays a path, and the check says so.
        ///
        ///<paramref name="corners"/> are the ones a bent centerline was bent from, null for a path that runs
        ///straight through its corners. Its slants are read from those rather than from the arcs, every run of which
        ///is one.
        ///
        ///Whether the outline crosses itself is the caller's to ask. A wire that comes back within its own width of
        ///itself has an outline that does, and no one polygon is that outline - a path is the only thing that draws
        ///it, so it stays one.
        ///
        public static (List<Element.Point> Outline, string Parts)? PathOutlineOff(
            IReadOnlyList<Element.Point> centerline,
            int width,
            Paths.Ends ends,
            int grid,
            IReadOnlyList<Element.Point>? corners = null)
        {
            //A grid of one has every corner on it, so the outline would only be built to say so. No width is a
            //hairline, whose outline is its centerline: the points it was given, wherever they are.
            if (grid <= 1 || width <= 0)
                return null;

            var worked = new List<string>();

            if (corners is not null)
                worked.Add("bends");

            var runs = corners ?? centerline;

            for (int i = 0; i + 1 < runs.Count; i++)
            {
                if (runs[i].X != runs[i + 1].X && runs[i].Y != runs[i + 1].Y)
                {
                    worked.Add("slanted runs");

                    break;
                }
            }

            if (ends == Paths.Ends.Round)
                worked.Add("round ends");

            if (worked.Count == 0)
                return null;

            var outline = PathOutline.Build(centerline, width, (int)ends, 0, 0);

            if (outline.All(corner => corner.Equals(Snap(corner, grid))))
                return null;

            return (outline, listed(worked));
        }

        ///<summary>Words as a sentence lists them: "bends", "bends and round ends", "bends, slanted runs and round ends".</summary>
        private static string listed(IReadOnlyList<string> words)
        {
            if (words.Count <= 1)
                return string.Join("", words);

            return $"{string.Join(", ", words.Take(words.Count - 1))} and {words[^1]}";
        }

        ///<summary>
        ///What a set operation or a grow made, each outline put on the grid as
        ///<see cref="Snap(IReadOnlyList{Element.Point}, int)"/> puts one, less the ones the grid has no room for. On
        ///a grid of one, the outlines as they were.
        ///
        ///**Every corner of them is worked out**, not only the new ones: a boolean puts a corner wherever two edges
        ///cross, at the nearest database unit, and a grow moves every edge there is. Corners that were on the grid
        ///going in stay where they were, and the rest go to the nearest point of it.
        ///
        ///**Flattened is a piece narrower than a step all along** - a sliver where two curves nearly meet, or what
        ///is left of a shape shrunk almost away. On the grid it is a line or a point, so it is left out and counted.
        ///**Folded is one the grid moved across itself**, which is not left out: see <see cref="Snapped.Fold"/>.
        ///</summary>
        public static Snapped SnapAll(IEnumerable<IReadOnlyList<Element.Point>> outlines, int grid)
        {
            var snapped = new Snapped();

            foreach (var outline in outlines)
            {
                if (grid <= 1)
                {
                    snapped.Outlines.Add(outline.ToList());

                    continue;
                }

                (var ring, var moved) = ringOnGrid(outline, grid);

                if (moved.Contains(true))
                    snapped.Moved = true;

                //The fold before the area: a bow tie's two halves wind opposite ways, and one the grid made with
                //halves alike covers nothing by the arithmetic while being two pieces of the answer.
                if (crossingAfter(ring, moved) is (int first, int second))
                {
                    snapped.Fold ??= (ring[first], ring[second]);

                    continue;
                }

                if (ring.Count < 3 || Measure.AreaOf(ring) == 0)
                {
                    snapped.Flattened++;

                    continue;
                }

                //Closed again if it came closed, as Snap leaves one.
                if (outline.Count > 1 && outline[0].Equals(outline[^1]))
                    ring.Add(ring[0]);

                snapped.Outlines.Add(ring);
            }

            return snapped;
        }

        ///<summary>What <see cref="SnapAll"/> made of a set of outlines.</summary>
        public sealed class Snapped
        {
            ///<summary>The outlines on the grid, less the ones it flattened or folded.</summary>
            public List<List<Element.Point>> Outlines { get; } = new List<List<Element.Point>>();

            ///<summary>How many it flattened: pieces narrower than a step all along, which the grid has no room for.</summary>
            public int Flattened { get; internal set; }

            ///<summary>
            ///Where the first outline the grid folded crosses itself, as the corners its two crossing edges start at -
            ///or null when it folded none.
            ///
            ///**One fold is reason to keep none of them.** The outline it spoiled is part of the answer, often most
            ///of it - a fold is one corner within a step of an edge it is not on, at a sharp point or across a narrow
            ///neck, rather than a piece thin all along - and the answer less a part is a different answer.
            ///</summary>
            public (Element.Point First, Element.Point Second)? Fold { get; internal set; }

            ///<summary>Whether the grid moved any corner at all, which outlines already on it do not.</summary>
            public bool Moved { get; internal set; }
        }

        ///<summary>
        ///An outline's corners on the grid as a ring - repeats dropped, the closing one too - and which of them the grid
        ///moved. A corner another one landed on counts as moved: the edge it starts is new.
        ///</summary>
        private static (List<Element.Point> Ring, List<bool> Moved) ringOnGrid(IReadOnlyList<Element.Point> outline, int grid)
        {
            var ring = new List<Element.Point>(outline.Count);
            var moved = new List<bool>(outline.Count);

            foreach (var corner in outline)
            {
                var at = Snap(corner, grid);
                bool shifted = !at.Equals(corner);

                if (ring.Count > 0 && ring[^1].Equals(at))
                {
                    moved[^1] = moved[^1] || shifted;

                    continue;
                }

                ring.Add(at);
                moved.Add(shifted);
            }

            while (ring.Count > 1 && ring[0].Equals(ring[^1]))
            {
                moved[0] = moved[0] || moved[^1];

                ring.RemoveAt(ring.Count - 1);
                moved.RemoveAt(moved.Count - 1);
            }

            return (ring, moved);
        }

        ///<summary>
        ///Where a ring the grid moved crosses itself, as the corners its two crossing edges start at - looking only at
        ///the edges a moved corner ends. The rest are where they were, and an outline a set operation made did not
        ///cross itself before; and a large one with two corners where edges crossed is two edges to look along rather
        ///than every pair there is.
        ///</summary>
        private static (int First, int Second)? crossingAfter(List<Element.Point> ring, List<bool> moved)
        {
            int count = ring.Count;

            for (int i = 0; i < count; i++)
            {
                int after = (i + 1) % count;

                if (!moved[i] && !moved[after])
                    continue;

                for (int j = 0; j < count; j++)
                {
                    int next = (j + 1) % count;

                    //The edge itself, and the two that share a corner with it.
                    if (j == i || j == after || next == i)
                        continue;

                    if (properlyCross(ring[i], ring[after], ring[j], ring[next]))
                        return (Math.Min(i, j), Math.Max(i, j));
                }
            }

            return null;
        }

        ///<summary>
        ///Where an outline crosses itself, as the two corners each crossing edge starts at - or null when it does not.
        ///
        ///**Crossing, not touching.** Two edges that cross in an X make a bow tie, which GDSII will store and every
        ///tool fills differently. Two edges lying on top of each other are a keyhole, which is how the format
        ///writes a hole - see <see cref="Booleans"/> - and are left alone.
        ///</summary>
        public static (int First, int Second)? Crossing(IReadOnlyList<Element.Point> outline)
        {
            var edges = edgesOf(outline);
            int count = edges.Count;

            for (int i = 0; i < count; i++)
            {
                for (int j = i + 2; j < count; j++)
                {
                    //The last edge meets the first at the closing corner, which is not a crossing.
                    if (i == 0 && j == count - 1)
                        continue;

                    if (properlyCross(edges[i].From, edges[i].To, edges[j].From, edges[j].To))
                        return (i, j);
                }
            }

            return null;
        }

        ///<summary>The edges round an outline, closed whether or not its last corner repeats its first.</summary>
        private static List<(Element.Point From, Element.Point To)> edgesOf(IReadOnlyList<Element.Point> points)
        {
            var edges = new List<(Element.Point, Element.Point)>();

            for (int i = 0; i + 1 < points.Count; i++)
                edges.Add((points[i], points[i + 1]));

            if (points.Count >= 3 && !points[0].Equals(points[^1]))
                edges.Add((points[^1], points[0]));

            return edges;
        }

        ///<summary>Whether two segments cross in an X - each one's ends strictly either side of the other.</summary>
        private static bool properlyCross(Element.Point a, Element.Point b, Element.Point c, Element.Point d)
        {
            long one = side(a, b, c);
            long two = side(a, b, d);
            long three = side(c, d, a);
            long four = side(c, d, b);

            return ((one > 0 && two < 0) || (one < 0 && two > 0)) && ((three > 0 && four < 0) || (three < 0 && four > 0));
        }

        ///<summary>Which side of the line through a and b a point is on: positive left, negative right, zero on it.</summary>
        private static long side(Element.Point a, Element.Point b, Element.Point point)
        {
            return ((long)(b.X - a.X) * (point.Y - a.Y)) - ((long)(b.Y - a.Y) * (point.X - a.X));
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
