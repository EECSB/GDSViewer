namespace GdsII
{
    ///<summary>
    ///Where the elements of a flattened layout are, so that a question about one part of it does not walk the
    ///whole of it.
    ///
    ///**Every question the 2D view asks used to be a walk of every element.** Which shapes are on screen,
    ///which one is under the pointer, which ones a band catches, how big the layout is: each tested every
    ///element's box. That is nothing on a standard cell and it was the whole of the wait on a real chip - the
    ///RGB mixer flattens to 222,274 elements, and on the dev server a click spent half a second walking them
    ///before anything was chosen, and every redraw spent seconds more.
    ///
    ///**A uniform grid, built once per layout.** Each element is written into the cells its box covers, in
    ///layout order, so a question about a box reads only the cells under it. A layout's elements are spread
    ///over its extent far more evenly than anything a tree is built for - rows of standard cells, a grid of
    ///vias - and a grid is a few arrays rather than a node per element, which matters in a 32-bit address
    ///space that the layout itself is already filling.
    ///
    ///**And every answer comes back in layout order**, which is not a detail: later in the layout is later in
    ///the drawing, so the order decides which of two overlapping shapes a click takes and which one a picture
    ///draws on top. Whatever this finds is sorted back into that order, so a caller that used to walk the list
    ///gets exactly the same answer it got before, from fewer elements.
    ///
    ///**An edit that moves a shape keeps the grid.** Building one is a walk of every element with arithmetic
    ///on each, which on the dev server cost more than the flatten it followed. A layout updated in place - see
    ///<see cref="GdsFlattener.Reflatten"/> - has the same elements at the same places but a few, so
    ///<see cref="After"/> keeps the grid it already has and sets those few aside: every answer is the grid's,
    ///less what it holds for them, plus what they say now. Once enough have been set aside that the grid is
    ///more out of date than not, a new one is built.
    ///
    ///**It assumes the list it was built over does not change.** A flattened layout is replaced rather than
    ///edited, and <see cref="FlattenedLayout.Index"/> builds a new one when the count moves or the list is
    ///replaced, which catches the cases a caller could plausibly get wrong.
    ///</summary>
    public sealed class LayoutIndex
    {
        #region Constants *******************************************************************

        ///<summary>
        ///About how many elements a cell of the grid should hold.
        ///
        ///Fewer cells than elements, so the grid costs less memory than the layout it indexes, and enough
        ///that a box a screen wide reads a small share of them. A shape on a real layout usually covers one
        ///to four cells at this size.
        ///</summary>
        private const int ElementsPerCell = 2;

        ///<summary>
        ///The most cells the grid is ever given, so a layout of millions of elements cannot ask for a grid
        ///larger than itself. Two million cells is eight megabytes of offsets.
        ///</summary>
        private const int MostCells = 2_000_000;

        ///<summary>
        ///How many cells one element may be written into before it goes to a coarser grid instead.
        ///
        ///A power ring or a guard band spans the whole layout, and writing it into every cell it covers would
        ///put one element into thousands of lists.
        ///
        ///**Coarser, not to one side.** It was kept in a list of its own and tested against every question, on
        ///the grounds that there were only ever a handful - and on a chip there are tens of thousands: with cells
        ///a little over half a micron across, every route longer than about ten microns is one. The RGB mixer set
        ///aside 28,132 of its 222,274 elements, so every question read 28,132 boxes, and tracing one of its supply
        ///nets asked 58,698 questions - 26 seconds of nothing but that. Each coarser grid has
        ///<see cref="CoarserBy"/> times fewer cells, so a long wire fits a few cells one or two levels up, and a
        ///grid of one cell takes whatever is left.
        ///</summary>
        private const int MostCellsPerElement = 16;

        ///<summary>How many times fewer cells each coarser grid has: four times wider each way.</summary>
        private const int CoarserBy = 16;

        ///<summary>
        ///How many elements may be set aside from a grid before a new one is built - or a sixteenth of the
        ///layout, if that is more. Every question tests each of them, so the list has to stay short beside
        ///what a question reads out of the grid.
        ///</summary>
        private const int MostSetAside = 2000;

        #endregion **************************************************************************



        #region Fields **********************************************************************

        private readonly IReadOnlyList<Element> elements;

        ///<summary>The grid, which may have been built over an earlier copy of this layout; see <see cref="After"/>.</summary>
        private readonly Grid grid;

        ///<summary>The elements that have changed since the grid was built, which it no longer answers for. Empty for a grid built over this very layout.</summary>
        private readonly HashSet<int> setAside;

        ///<summary>The same, in layout order, for the questions that walk them.</summary>
        private readonly int[] setAsideInOrder;

        ///<summary>What the layout as a whole comes to, worked out the first time it is asked for.</summary>
        private Summary? summary;

        private long? largestArea;

        #endregion **************************************************************************



        #region Constructors ****************************************************************

        ///<summary>Indexes these elements. The list is held, not copied: see the note on the class.</summary>
        public LayoutIndex(IReadOnlyList<Element> elements)
            : this(elements, Grid.Build(elements), new HashSet<int>())
        {
        }

        ///
        ///The same, offering to stop as it goes: <paramref name="pause"/> is awaited every so many elements while
        ///the grid is built and the layout summed up, so a caller on a single thread can let its page answer - and
        ///throw from it to give the index up. See <see cref="Pauses"/>.
        ///
        ///**Summed as well as gridded**, which the constructor leaves for the first question that needs it: the
        ///extent and the layers in use are a walk of every element too, and a view opening a layout asks for them
        ///straight away. Done here, that walk is one that can stop as well.
        ///
        ///**The constructor's own walks, a run of elements at a time** - each walk is a method over a range, which
        ///the constructor calls once for everything and this calls a run at a time with a pause between. The
        ///loops stay plain ones either way: inside an async method their locals would live on the heap, which in
        ///the browser's interpreter made an index half as slow again.
        ///
        public static async ValueTask<LayoutIndex> BuildAsync(IReadOnlyList<Element> elements, Func<ValueTask> pause)
        {
            var index = new LayoutIndex(elements, await Grid.BuildAsync(elements, pause), new HashSet<int>());

            var summing = new Summing();

            for (int from = 0; from < elements.Count; from += Pauses.Every)
            {
                index.sum(summing, from, Math.Min(elements.Count, from + Pauses.Every));

                await pause();
            }

            index.summary = summing.Summed();

            return index;
        }

        private LayoutIndex(IReadOnlyList<Element> elements, Grid grid, HashSet<int> setAside)
        {
            this.elements = elements;
            Count = elements.Count;

            this.grid = grid;
            this.setAside = setAside;

            setAsideInOrder = setAside.ToArray();
            Array.Sort(setAsideInOrder);
        }

        ///
        ///The index of a layout that is <paramref name="before"/>'s with the elements at
        ///<paramref name="changed"/> drawn differently - the same number of elements, in the same order.
        ///
        ///Shares the grid, and sets the changed elements aside from it. A new grid when the list is a
        ///different length, which is not the edit this is for, or when so much is set aside that walking it
        ///would cost more than reading the grid.
        ///
        public static LayoutIndex After(LayoutIndex before, IReadOnlyList<Element> elements, IEnumerable<int> changed)
        {
            if (elements.Count != before.Count)
                return new LayoutIndex(elements);

            var aside = new HashSet<int>(before.setAside);

            aside.UnionWith(changed);

            if (aside.Count > Math.Max(MostSetAside, elements.Count / 16))
                return new LayoutIndex(elements);

            var after = new LayoutIndex(elements, before.grid, aside);

            //What the layout as a whole comes to, carried over and corrected for what changed - rather than
            //walked again, which on the dev server was two of the four seconds a redraw after a move took.
            if (before.summary is Summary was)
                after.summary = was.After(before.elements, elements, changed);

            return after;
        }

        #endregion **************************************************************************



        #region Properties ******************************************************************

        ///<summary>How many elements this was built over, which is how a stale one is spotted.</summary>
        public int Count { get; }

        ///<summary>The box around every element, labels included. Empty for an empty layout.</summary>
        public Bounds Extent
        {
            get { return summed().Extent; }
        }

        ///<summary>
        ///The box around the shapes alone - a label is an anchor with a name hung on it, and says nothing
        ///about how big the drawing is.
        ///</summary>
        public Bounds ShapeExtent
        {
            get { return summed().ShapeExtent; }
        }

        ///<summary>
        ///The layers the shapes are on, each once, in the order each first appears.
        ///
        ///What a picture's style block is written from - in that order, because the order rules are written in
        ///is part of the picture, and it used to be worked out by walking every element on every redraw.
        ///</summary>
        public IReadOnlyList<Layer> ShapeLayers
        {
            get { return summed().ShapeLayers; }
        }

        ///<summary>The area of the largest shape's box, or zero when there are no shapes.</summary>
        public long LargestArea
        {
            get
            {
                largestArea ??= largest();

                return largestArea.Value;
            }
        }

        #endregion **************************************************************************



        #region Questions *******************************************************************

        ///<summary>Whether this was built over that very list, at the length it has now.</summary>
        public bool Indexes(IReadOnlyList<Element> list)
        {
            return ReferenceEquals(elements, list) && list.Count == Count;
        }

        ///<summary>
        ///The box around every element on these layers - the extent of a picture that draws only them.
        ///
        ///What a view frames when it opens a layout too large to draw all of first and measure: the same
        ///box measuring the drawing gave, without the drawing.
        ///</summary>
        public Bounds ExtentOf(IReadOnlySet<LayerKey> layers)
        {
            var extent = Bounds.Empty;

            foreach (var layer in summed().LayerExtents)
            {
                if (layers.Contains(layer.Key))
                    extent = extent.Union(layer.Value);
            }

            return extent;
        }

        ///<summary>
        ///Every element whose box meets <paramref name="box"/>, edges included, in layout order.
        ///
        ///<paramref name="smallest"/> leaves out every shape whose box covers less area than that, which is
        ///how a picture leaves out what is smaller than a pixel; zero keeps them all. It never leaves out a
        ///label: whether labels are wanted at all is <paramref name="withLabels"/>.
        ///
        ///**Two ways to the same answer, and the cheaper one is taken.** A small box reads the cells under it.
        ///A box over most of the layout would read most of the grid, so when a smallest size is given and the
        ///shapes at least that size are fewer than what the cells hold, those are walked instead - largest
        ///first, stopping at the first one too small. At a chip's fit that is a quarter of the shapes, and the
        ///rest are never looked at.
        ///</summary>
        public List<int> Within(Bounds box, long smallest = 0, bool withLabels = true)
        {
            if (box.IsEmpty || Count == 0)
                return new List<int>();

            var found = grid.Within(box, smallest, withLabels);

            if (setAside.Count == 0)
                return found;

            //What the grid holds for these is where they were, so it goes, and where they are now comes in.
            found.RemoveAll(setAside.Contains);

            foreach (int i in setAsideInOrder)
            {
                if (Grid.Wanted(elements[i], box, smallest, withLabels))
                    found.Add(i);
            }

            found.Sort();

            return found;
        }

        ///<summary>
        ///Every element whose box holds <paramref name="point"/>, edges included, in layout order.
        ///
        ///Its box, not its outline - which of these the point is actually inside is the caller's question,
        ///and an expensive one, which is why it is only asked of what this returns.
        ///</summary>
        public List<int> Under(Element.Point point)
        {
            if (Count == 0)
                return new List<int>();

            var found = grid.Under(point);

            if (setAside.Count == 0)
                return found;

            found.RemoveAll(setAside.Contains);

            foreach (int i in setAsideInOrder)
            {
                if (elements[i].Box.Contains(point))
                    found.Add(i);
            }

            found.Sort();

            return found;
        }

        #endregion **************************************************************************



        #region The layout as a whole *******************************************************

        ///
        ///The extents, and the layers in use with how many shapes each holds.
        ///
        ///The counts are what let a layer drop out of <see cref="ShapeLayers"/> when an edit takes its last
        ///shape somewhere else, without a walk to find that out.
        ///
        private sealed record Summary(
            Bounds Extent,
            Bounds ShapeExtent,
            IReadOnlyList<Layer> ShapeLayers,
            Dictionary<LayerKey, Bounds> LayerExtents,
            Dictionary<LayerKey, int> ShapesOn)
        {
            ///
            ///The same for a layout whose elements at <paramref name="changed"/> are drawn differently.
            ///
            ///**The layers exactly, the extents as a box that still holds everything.** A shape moved out
            ///grows an extent, and that is exact. One moved in from the edge leaves it as large as it was,
            ///since only a walk could say how far it should shrink - and nothing that reads one minds: a
            ///view framed a little wide, and a fill pattern whose tile, worked out from the extent, keeps the
            ///size it had rather than shifting under somebody's edit. The next flatten works them out afresh.
            ///
            ///A layer an edit brings in goes on the end rather than where its first shape is - the order of
            ///two layers' rules decides nothing, since no shape is on both.
            ///
            public Summary After(IReadOnlyList<Element> before, IReadOnlyList<Element> now, IEnumerable<int> changed)
            {
                var everything = Extent;
                var shapes = ShapeExtent;
                var extents = new Dictionary<LayerKey, Bounds>(LayerExtents);
                var counts = new Dictionary<LayerKey, int>(ShapesOn);
                var layers = new List<Layer>(ShapeLayers);

                foreach (int i in changed)
                {
                    var was = before[i];
                    var became = now[i];

                    if (was.Text is null)
                        counts[was.Layer.Key] = counts.GetValueOrDefault(was.Layer.Key) - 1;

                    var box = became.Box;

                    everything = everything.Union(box);

                    extents.TryGetValue(became.Layer.Key, out var onLayer);
                    extents[became.Layer.Key] = onLayer.Union(box);

                    if (became.Text is not null)
                        continue;

                    shapes = shapes.Union(box);

                    counts[became.Layer.Key] = counts.GetValueOrDefault(became.Layer.Key) + 1;

                    if (!layers.Any(layer => layer.Key == became.Layer.Key))
                        layers.Add(became.Layer);
                }

                layers.RemoveAll(layer => counts.GetValueOrDefault(layer.Key) <= 0);

                return new Summary(everything, shapes, layers, extents, counts);
            }
        }

        ///
        ///**A walk of every element, once per index** - each box is already worked out and kept, so this is a
        ///union per element and nothing more. An index made after an edit is handed its summary by the one
        ///before it rather than walking; see <see cref="Summary.After"/>.
        ///
        private Summary summed()
        {
            if (summary is not null)
                return summary;

            var summing = new Summing();

            sum(summing, 0, elements.Count);

            summary = summing.Summed();

            return summary;
        }

        ///<summary>What summing a layout gathers, kept between the runs of a walk that pauses - see <see cref="BuildAsync"/>.</summary>
        private sealed class Summing
        {
            public Bounds Everything = Bounds.Empty;
            public Bounds Shapes = Bounds.Empty;
            public readonly List<Layer> Layers = new List<Layer>();
            public readonly Dictionary<LayerKey, int> Counts = new Dictionary<LayerKey, int>();
            public readonly Dictionary<LayerKey, Bounds> Extents = new Dictionary<LayerKey, Bounds>();

            public Summary Summed()
            {
                return new Summary(Everything, Shapes, Layers, Extents, Counts);
            }
        }

        ///<summary>The walk itself, over the elements from <paramref name="from"/> up to <paramref name="to"/>.</summary>
        private void sum(Summing into, int from, int to)
        {
            var everything = into.Everything;
            var shapes = into.Shapes;
            var layers = into.Layers;
            var counts = into.Counts;
            var extents = into.Extents;

            for (int i = from; i < to; i++)
            {
                var element = elements[i];
                var box = element.Box;

                everything = everything.Union(box);

                extents.TryGetValue(element.Layer.Key, out var onLayer);
                extents[element.Layer.Key] = onLayer.Union(box);

                if (element.Text is not null)
                    continue;

                shapes = shapes.Union(box);

                counts.TryGetValue(element.Layer.Key, out int already);
                counts[element.Layer.Key] = already + 1;

                if (already == 0)
                    layers.Add(element.Layer);
            }

            into.Everything = everything;
            into.Shapes = shapes;
        }

        ///
        ///The largest shape's box: the grid's largest that has not been set aside, against what the ones set
        ///aside are now. Exact either way - the grid's list runs largest first, so the walk past what has been
        ///set aside is as long as what has been set aside, and no longer.
        ///
        private long largest()
        {
            long found = grid.LargestApartFrom(setAside);

            foreach (int i in setAsideInOrder)
            {
                if (elements[i].Text is null)
                    found = Math.Max(found, elements[i].Box.Area);
            }

            return found;
        }

        #endregion **************************************************************************



        #region The grid ********************************************************************

        ///<summary>
        ///The cells, and the shapes in size order - everything about an index that costs a walk with
        ///arithmetic to build, and is shared by the indexes that follow an edit.
        ///
        ///**It tests boxes against the list it was built over**, which is a different list from the layout's
        ///own once an edit has happened. The two hold the very same element at every place but the ones set
        ///aside, and those are taken out of every answer before it is handed on.
        ///</summary>
        private sealed class Grid
        {
            private readonly IReadOnlyList<Element> elements;

            ///<summary>The elements in the cells they cover, the finest grid first. Null for a layout with nothing in it.</summary>
            private readonly Cells? cells;

            ///
            ///Every shape - every element but a label - in order of size, the largest first.
            ///
            ///**By the power of two its box's area falls under, not sorted exactly.** A sort of a quarter of a
            ///million areas was most of what an index cost to build on the dev server, and nothing here needs
            ///the order inside one power of two: a walk from the largest stops at the first group too small
            ///and checks the last group it reads one shape at a time. Counted into place, one pass and another.
            ///
            private readonly int[] bySize;

            ///<summary>Where each power of two's run starts in <see cref="bySize"/>, the largest first; one longer than there are powers.</summary>
            private readonly int[] sizeStarts;

            ///<summary>How many powers of two an area can fall under: a long holds up to 2^63.</summary>
            private const int Powers = 64;

            ///<summary>Every label, in layout order.</summary>
            private readonly int[] labels;

            ///<summary>The grid over these elements, built in one go.</summary>
            public static Grid Build(IReadOnlyList<Element> elements)
            {
                var gathering = new Gathering();

                gather(elements, gathering, 0, elements.Count);

                Cells? cells = null;

                if (!gathering.Everything.IsEmpty)
                    cells = Cells.Build(elements, Enumerable.Range(0, elements.Count).ToArray(), gathering.Everything, cellsWanted(elements));

                return new Grid(elements, gathering, cells);
            }

            ///<summary>The same grid, built a run of elements at a time with <paramref name="pause"/> awaited between - see <see cref="BuildAsync"/>.</summary>
            public static async ValueTask<Grid> BuildAsync(IReadOnlyList<Element> elements, Func<ValueTask> pause)
            {
                var gathering = new Gathering();

                for (int from = 0; from < elements.Count; from += Pauses.Every)
                {
                    gather(elements, gathering, from, Math.Min(elements.Count, from + Pauses.Every));

                    await pause();
                }

                Cells? cells = null;

                if (!gathering.Everything.IsEmpty)
                    cells = await Cells.BuildAsync(elements, Enumerable.Range(0, elements.Count).ToArray(), gathering.Everything, cellsWanted(elements), pause);

                return new Grid(elements, gathering, cells);
            }

            ///<summary>About how many cells the finest grid over these elements gets.</summary>
            private static int cellsWanted(IReadOnlyList<Element> elements)
            {
                return Math.Clamp(elements.Count / ElementsPerCell, 1, MostCells);
            }

            ///<summary>What the first walk over the elements finds, kept between the runs of a walk that pauses.</summary>
            private sealed class Gathering
            {
                public Bounds Everything = Bounds.Empty;
                public readonly List<int> Shapes = new List<int>();
                public readonly List<int> Labels = new List<int>();
            }

            ///<summary>The first walk, over the elements from <paramref name="from"/> up to <paramref name="to"/>: every box, and which are shapes and which labels.</summary>
            private static void gather(IReadOnlyList<Element> elements, Gathering into, int from, int to)
            {
                var everything = into.Everything;
                var shapes = into.Shapes;
                var labels = into.Labels;

                for (int i = from; i < to; i++)
                {
                    var element = elements[i];

                    everything = everything.Union(element.Box);

                    if (element.Text is null)
                        shapes.Add(i);
                    else
                        labels.Add(i);
                }

                into.Everything = everything;
            }

            ///<summary>The shapes in size order and the labels in layout order, from what the walk found - with the cells it built.</summary>
            private Grid(IReadOnlyList<Element> elements, Gathering gathered, Cells? cells)
            {
                this.elements = elements;
                this.cells = cells;

                var shapeIndexes = gathered.Shapes;

                labels = gathered.Labels.ToArray();

                //Largest first, so a question that leaves out whatever is smaller than a pixel can stop at the
                //first one that is. See bySize.
                var powers = new int[shapeIndexes.Count];
                var counted = new int[Powers + 1];

                for (int k = 0; k < shapeIndexes.Count; k++)
                {
                    powers[k] = powerOf(elements[shapeIndexes[k]].Box.Area);
                    counted[(Powers - 1 - powers[k]) + 1]++;
                }

                for (int p = 0; p < Powers; p++)
                    counted[p + 1] += counted[p];

                sizeStarts = counted;
                bySize = new int[shapeIndexes.Count];

                var placed = new int[Powers];

                for (int k = 0; k < shapeIndexes.Count; k++)
                {
                    int run = Powers - 1 - powers[k];

                    bySize[sizeStarts[run] + placed[run]] = shapeIndexes[k];
                    placed[run]++;
                }
            }

            ///<summary>See <see cref="LayoutIndex.Within"/>, which this answers for the elements the grid was built over.</summary>
            public List<int> Within(Bounds box, long smallest, bool withLabels)
            {
                var found = new List<int>();

                if (cells is null)
                    return found;

                if (smallest > 0 && bigEnough(smallest) < cells.CountUnder(box))
                {
                    //Every group at least as big as the smallest, and the one it falls in checked shape by shape.
                    int stop = sizeStarts[(Powers - 1 - powerOf(smallest)) + 1];

                    for (int k = 0; k < stop; k++)
                    {
                        var shape = elements[bySize[k]];

                        if (shape.Box.Area >= smallest && box.Intersects(shape.Box))
                            found.Add(bySize[k]);
                    }

                    if (withLabels)
                    {
                        foreach (int i in labels)
                        {
                            if (box.Intersects(elements[i].Box))
                                found.Add(i);
                        }
                    }

                    found.Sort();

                    return found;
                }

                cells.Within(box, smallest, withLabels, found);

                //An element over several cells was found once per cell.
                return sortedOnce(found);
            }

            ///<summary>See <see cref="LayoutIndex.Under"/>.</summary>
            public List<int> Under(Element.Point point)
            {
                var found = new List<int>();

                if (cells is null)
                    return found;

                cells.Under(point, found);

                //An element is in one grid only, and under a point in one cell of it, so there is nothing to fold -
                //only the grids' runs to put back in order.
                found.Sort();

                return found;
            }

            ///
            ///The largest shape's box among those not in <paramref name="apart"/>, or zero.
            ///
            ///The first group from the top holding one that is not set aside, read whole - the largest is
            ///somewhere in it, since every group above it was empty of anything that counts.
            ///
            public long LargestApartFrom(HashSet<int> apart)
            {
                for (int run = 0; run < Powers; run++)
                {
                    long largest = -1;

                    for (int k = sizeStarts[run]; k < sizeStarts[run + 1]; k++)
                    {
                        if (!apart.Contains(bySize[k]))
                            largest = Math.Max(largest, elements[bySize[k]].Box.Area);
                    }

                    if (largest >= 0)
                        return largest;
                }

                return 0;
            }

            ///<summary>Whether an element is one a question about a box is asking for.</summary>
            public static bool Wanted(Element element, Bounds box, long smallest, bool withLabels)
            {
                if (element.Text is not null)
                {
                    if (!withLabels)
                        return false;
                }
                else if (smallest > 0 && element.Box.Area < smallest)
                {
                    return false;
                }

                return box.Intersects(element.Box);
            }

            ///<summary>
            ///How many shapes a walk for this smallest size would read: every group at least that big, and the
            ///one it falls in. More than the shapes that pass, by at most one group - which is what deciding
            ///between the two ways to an answer needs, and no more.
            ///</summary>
            private int bigEnough(long smallest)
            {
                return sizeStarts[(Powers - 1 - powerOf(smallest)) + 1];
            }

            ///<summary>Which power of two an area falls under: the number of bits it takes, so zero is zero and one is one.</summary>
            private static int powerOf(long area)
            {
                if (area <= 0)
                    return 0;

                return Math.Min(Powers - 1, 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)area));
            }

            private static List<int> sortedOnce(List<int> found)
            {
                if (found.Count < 2)
                    return found;

                found.Sort();

                int kept = 1;

                for (int k = 1; k < found.Count; k++)
                {
                    if (found[k] != found[kept - 1])
                    {
                        found[kept] = found[k];
                        kept++;
                    }
                }

                found.RemoveRange(kept, found.Count - kept);

                return found;
            }
        }

        ///<summary>
        ///Elements written into the cells of a grid over the layout, and the ones too big for it in a coarser grid
        ///of their own - see <see cref="MostCellsPerElement"/>.
        ///
        ///Each coarser grid has <see cref="CoarserBy"/> times fewer cells over the same ground, down to one cell,
        ///which every element fits. An element is in exactly one of them.
        ///</summary>
        private sealed class Cells
        {
            private readonly IReadOnlyList<Element> elements;

            private readonly int left;
            private readonly int bottom;
            private readonly double cellWidth;
            private readonly double cellHeight;
            private readonly int columns;
            private readonly int rows;

            //
            //The three below are written once, by Build or BuildAsync, rather than in the constructor - which is what
            //lets the filling be done a run at a time. Nothing outside those two sees a Cells before they are.
            //

            ///<summary>Where each cell's run starts in <see cref="items"/>. One longer than the cell count, so a cell's run is starts[c] up to starts[c + 1].</summary>
            private int[] starts = Array.Empty<int>();

            ///<summary>Every cell's elements, cell after cell, each run in layout order.</summary>
            private int[] items = Array.Empty<int>();

            ///<summary>The elements that cover too many of these cells, in a coarser grid. Null when there are none.</summary>
            private Cells? coarser;

            ///<summary>The elements at <paramref name="indexes"/>, written into a grid of about <paramref name="wanted"/> cells over <paramref name="everything"/>.</summary>
            public static Cells Build(IReadOnlyList<Element> elements, int[] indexes, Bounds everything, int wanted)
            {
                var built = new Cells(elements, everything, wanted);
                var filling = new Filling(built.columns * built.rows, indexes.Length);

                built.count(indexes, filling, 0, indexes.Length);
                built.startRuns(filling);
                built.place(indexes, filling, 0, indexes.Length);

                if (filling.Aside.Count > 0)
                    built.coarser = Build(elements, filling.Aside.ToArray(), everything, Math.Max(1, wanted / CoarserBy));

                return built;
            }

            ///<summary>The same, a run of elements at a time with <paramref name="pause"/> awaited between - see <see cref="BuildAsync"/>.</summary>
            public static async ValueTask<Cells> BuildAsync(IReadOnlyList<Element> elements, int[] indexes, Bounds everything, int wanted, Func<ValueTask> pause)
            {
                var built = new Cells(elements, everything, wanted);
                var filling = new Filling(built.columns * built.rows, indexes.Length);

                for (int from = 0; from < indexes.Length; from += Pauses.Every)
                {
                    built.count(indexes, filling, from, Math.Min(indexes.Length, from + Pauses.Every));

                    await pause();
                }

                built.startRuns(filling);

                for (int from = 0; from < indexes.Length; from += Pauses.Every)
                {
                    built.place(indexes, filling, from, Math.Min(indexes.Length, from + Pauses.Every));

                    await pause();
                }

                if (filling.Aside.Count > 0)
                    built.coarser = await BuildAsync(elements, filling.Aside.ToArray(), everything, Math.Max(1, wanted / CoarserBy), pause);

                return built;
            }

            ///<summary>What the two passes keep between them, and between the runs of a walk that pauses.</summary>
            private sealed class Filling
            {
                public readonly int[] Counts;
                public readonly bool[] TooBig;
                public readonly List<int> Aside = new List<int>();
                public int[] Filled = Array.Empty<int>();

                public Filling(int cells, int elements)
                {
                    Counts = new int[cells + 1];
                    TooBig = new bool[elements];
                }
            }

            ///<summary>The grid's shape: where it starts, and how many cells of what size.</summary>
            private Cells(IReadOnlyList<Element> elements, Bounds everything, int wanted)
            {
                this.elements = elements;

                left = everything.Left;
                bottom = everything.Bottom;

                //Plus one, because the corners are inclusive: a layout that is one point is one unit across.
                double width = everything.Width + 1;
                double height = everything.Height + 1;

                //Square cells where the layout allows it, so a box is the same number of cells whichever way it
                //is turned. Each axis is bounded on its own, so a layout that is one long line still gets a grid
                //that fits in memory.
                columns = (int)Math.Clamp(Math.Round(Math.Sqrt(wanted * width / height)), 1, wanted);
                rows = Math.Clamp(wanted / columns, 1, wanted);

                cellWidth = width / columns;
                cellHeight = height / rows;
            }

            //
            //Two passes, counting and then filling, so every cell's run is one slice of one array rather than a list
            //of its own - a quarter of a million lists is a quarter of a million objects.
            //

            ///<summary>The first pass, over the elements at indexes[from] up to indexes[to]: how many entries each cell gets, and which elements are too big for these cells.</summary>
            private void count(int[] indexes, Filling filling, int from, int to)
            {
                int count = columns * rows;
                var counts = filling.Counts;

                for (int k = from; k < to; k++)
                {
                    var (fromColumn, toColumn, fromRow, toRow) = cellsUnder(elements[indexes[k]].Box);

                    //One cell holds anything, so the coarsest grid keeps all that reaches it.
                    if (count > 1 && ((long)(toColumn - fromColumn + 1) * (toRow - fromRow + 1)) > MostCellsPerElement)
                    {
                        filling.TooBig[k] = true;
                        filling.Aside.Add(indexes[k]);

                        continue;
                    }

                    for (int row = fromRow; row <= toRow; row++)
                    {
                        for (int column = fromColumn; column <= toColumn; column++)
                            counts[(row * columns) + column + 1]++;
                    }
                }
            }

            ///<summary>Between the passes: where each cell's run starts, and room for all of them.</summary>
            private void startRuns(Filling filling)
            {
                int count = columns * rows;
                var counts = filling.Counts;

                for (int c = 0; c < count; c++)
                    counts[c + 1] += counts[c];

                starts = counts;
                items = new int[starts[count]];

                filling.Filled = new int[count];
            }

            ///<summary>The second pass, over the same elements: each one into the runs of the cells it covers.</summary>
            private void place(int[] indexes, Filling filling, int from, int to)
            {
                var filled = filling.Filled;

                for (int k = from; k < to; k++)
                {
                    if (filling.TooBig[k])
                        continue;

                    var (fromColumn, toColumn, fromRow, toRow) = cellsUnder(elements[indexes[k]].Box);

                    for (int row = fromRow; row <= toRow; row++)
                    {
                        for (int column = fromColumn; column <= toColumn; column++)
                        {
                            int cell = (row * columns) + column;

                            items[starts[cell] + filled[cell]] = indexes[k];
                            filled[cell]++;
                        }
                    }
                }
            }

            ///<summary>How many entries the cells under a box hold, here and in the coarser grids - more than the elements, by those over several cells.</summary>
            public long CountUnder(Bounds box)
            {
                var (fromColumn, toColumn, fromRow, toRow) = cellsUnder(box);

                long inCells = 0;

                for (int row = fromRow; row <= toRow; row++)
                    inCells += starts[(row * columns) + toColumn + 1] - starts[(row * columns) + fromColumn];

                if (coarser is not null)
                    inCells += coarser.CountUnder(box);

                return inCells;
            }

            ///<summary>Adds every element a question about a box wants - once per cell it is in, so the caller folds them.</summary>
            public void Within(Bounds box, long smallest, bool withLabels, List<int> found)
            {
                var (fromColumn, toColumn, fromRow, toRow) = cellsUnder(box);

                for (int row = fromRow; row <= toRow; row++)
                {
                    int from = starts[(row * columns) + fromColumn];
                    int to = starts[(row * columns) + toColumn + 1];

                    for (int k = from; k < to; k++)
                    {
                        if (Grid.Wanted(elements[items[k]], box, smallest, withLabels))
                            found.Add(items[k]);
                    }
                }

                coarser?.Within(box, smallest, withLabels, found);
            }

            ///<summary>Adds every element whose box holds the point.</summary>
            public void Under(Element.Point point, List<int> found)
            {
                int cell = (rowOf(point.Y) * columns) + columnOf(point.X);

                for (int k = starts[cell]; k < starts[cell + 1]; k++)
                {
                    if (elements[items[k]].Box.Contains(point))
                        found.Add(items[k]);
                }

                coarser?.Under(point, found);
            }

            ///<summary>
            ///The cells a box covers, clamped to the grid.
            ///
            ///**The same floor for an element's box and for a question's**, which is what makes the answer
            ///complete: a point both boxes share lands in one cell, and that cell is in both ranges.
            ///</summary>
            private (int FromColumn, int ToColumn, int FromRow, int ToRow) cellsUnder(Bounds box)
            {
                return (columnOf(box.Left), columnOf(box.Right), rowOf(box.Bottom), rowOf(box.Top));
            }

            private int columnOf(int x)
            {
                return Math.Clamp((int)Math.Floor(((double)x - left) / cellWidth), 0, columns - 1);
            }

            private int rowOf(int y)
            {
                return Math.Clamp((int)Math.Floor(((double)y - bottom) / cellHeight), 0, rows - 1);
            }
        }

        #endregion **************************************************************************
    }
}
