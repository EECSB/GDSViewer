namespace GdsII
{
    ///<summary>
    ///How big a layout is, and how much of it is covered.
    ///
    ///Two questions that sound like one. **Drawn area double-counts overlap and covered area does not**,
    ///and which of them somebody wants depends entirely on what they are asking. Summing the shapes on a
    ///layer answers "how much did the designer draw"; merging them first answers "how much of the wafer is
    ///metal", which is the one a density rule cares about, and the two differ by however much the shapes
    ///overlap - which on a real layer is a lot.
    ///
    ///Kept apart from <see cref="Bounds"/> because covered area needs the clipping library and a box does
    ///not. A consumer who only wants an extent should not pull that in behind it.
    ///</summary>
    public static class Measure
    {
        #region Extents *********************************************************************

        ///<summary>The box around everything drawn, labels included - their anchor is a point in the layout.</summary>
        public static Bounds BoundsOf(FlattenedLayout layout)
        {
            var bounds = Bounds.Empty;

            foreach (var element in layout.Elements)
                bounds = bounds.Union(element.Box);

            return bounds;
        }

        ///<summary>The box around one layer, or empty when nothing is on it.</summary>
        public static Bounds BoundsOf(FlattenedLayout layout, LayerKey layer)
        {
            var bounds = Bounds.Empty;

            foreach (var element in layout.Elements)
            {
                if (element.Layer.Key.Equals(layer))
                    bounds = bounds.Union(element.Box);
            }

            return bounds;
        }

        ///<summary>One box per layer that has anything on it, in the order the sidebar lists them.</summary>
        public static SortedDictionary<LayerKey, Bounds> BoundsByLayer(FlattenedLayout layout)
        {
            var byLayer = new SortedDictionary<LayerKey, Bounds>();

            foreach (var element in layout.Elements)
            {
                var key = element.Layer.Key;

                if (!byLayer.TryGetValue(key, out var bounds))
                    bounds = Bounds.Empty;

                byLayer[key] = bounds.Union(element.Box);
            }

            return byLayer;
        }

        #endregion **************************************************************************



        #region Area ************************************************************************

        ///<summary>
        ///The area a single outline encloses, in square database units.
        ///
        ///The shoelace formula, and the absolute value of it: nothing in GDSII says which way round a
        ///boundary is written and both appear in real files, so a signed result would report half a layer
        ///as negative. A ring that repeats its first point to close itself, which a GDSII boundary does,
        ///costs a zero-area term and needs no special case.
        ///
        ///Measured relative to the first point. The coordinates are on an absolute grid and a shape a few
        ///hundred units across can sit a hundred million out from the origin, where the products of raw
        ///coordinates are large enough that the difference between them loses precision that the answer
        ///is made of.
        ///</summary>
        public static double AreaOf(IReadOnlyList<Element.Point> outline)
        {
            if (outline.Count < 3)
                return 0;

            var origin = outline[0];

            double twiceArea = 0;

            for (int i = 0; i < outline.Count; i++)
            {
                var here = outline[i];
                var next = outline[(i + 1) % outline.Count];

                double x1 = here.X - (double)origin.X;
                double y1 = here.Y - (double)origin.Y;
                double x2 = next.X - (double)origin.X;
                double y2 = next.Y - (double)origin.Y;

                twiceArea += (x1 * y2) - (x2 * y1);
            }

            return Math.Abs(twiceArea) / 2;
        }

        ///<summary>
        ///Every shape on a layer added up, overlap counted twice.
        ///
        ///What the designer drew. Cheap, and the wrong answer to "how much of this layer is covered" -
        ///see <see cref="CoveredAreaOf"/>.
        ///
        ///Labels and open runs are left out, because neither encloses anything: a TEXT element is an anchor
        ///and a string, and a path of no width is a centerline whose two ends the area formula would join.
        ///</summary>
        public static double DrawnAreaOf(FlattenedLayout layout, LayerKey layer)
        {
            double area = 0;

            foreach (var element in layout.Elements)
            {
                if (element.Text is null && !element.IsOpen && element.Layer.Key.Equals(layer))
                    area += AreaOf(element.Points);
            }

            return area;
        }

        ///<summary>
        ///The ground a layer actually covers, with overlap counted once and holes taken off.
        ///
        ///Merged first, which is what makes it the true figure and also what makes it the expensive one -
        ///a clipping pass over every shape on the layer. It is the number a density rule is written
        ///against, so it is worth the pass.
        ///
        ///**The one layer, not all of them.** This used to merge the whole layout through
        ///<see cref="Booleans.MergeByLayer"/> and then throw away every layer but the one asked for, which
        ///is the right answer at twenty-one times the price when something walks the layers in turn - and a
        ///rule sweep does exactly that. Asked once from a properties panel nobody would notice; asked once
        ///per layer it is the difference between one clipping pass and one per layer.
        ///
        ///Labels and open runs take no part, for the reason <see cref="Booleans.MergeByLayer"/> leaves them
        ///out: neither encloses any ground.
        ///</summary>
        public static double CoveredAreaOf(FlattenedLayout layout, LayerKey layer)
        {
            var shapes = new List<IReadOnlyList<Element.Point>>();

            foreach (var element in layout.Elements)
            {
                if (element.Text is not null || element.IsOpen)
                    continue;

                if (element.Layer.Key.Equals(layer))
                    shapes.Add(element.Points);
            }

            double area = 0;

            foreach (var ring in Booleans.MergeToRings(shapes))
            {
                area += AreaOf(ring.Boundary);

                //A hole is ground the layer does not cover, however it was written down.
                foreach (var hole in ring.Holes)
                    area -= AreaOf(hole);
            }

            return area;
        }

        ///<summary>
        ///How much of a layer's own bounding box it covers, from 0 to 1.
        ///
        ///Against the layer's extent rather than the whole layout's, which is the comparison that says
        ///something about the layer rather than about where it happens to sit. Zero for a layer with
        ///nothing on it, and for one whose shapes are all on a single line.
        ///</summary>
        public static double DensityOf(FlattenedLayout layout, LayerKey layer)
        {
            var bounds = BoundsOf(layout, layer);

            if (bounds.IsEmpty || bounds.Area == 0)
                return 0;

            return CoveredAreaOf(layout, layer) / bounds.Area;
        }

        ///<summary>
        ///The emptiest and the fullest a window of a given size gets, stepped across the layout.
        ///
        ///**This is the figure a density rule is actually written against**, and it is not
        ///<see cref="DensityOf"/>. That one measures a layer against its own bounding box and answers "how
        ///solid is this layer overall" - a fair question, and one a foundry never asks. What a process cares
        ///about is the *worst* window: chemical-mechanical polishing dishes where metal is sparse and
        ///erodes where it is dense, and both faults are local. A chip that averages 40% can still have a
        ///hundred-micron square with nothing in it at all, and the average is what hides that.
        ///
        ///So the layer is merged once and a window of <paramref name="window"/> database units is stepped
        ///across its extent by <paramref name="step"/>, clipping the merged geometry to each. The window is
        ///usually stepped by less than its own width - a rule states both, and a step equal to the width
        ///would miss a sparse patch that straddles two windows.
        ///
        ///Only windows that fit entirely inside the layer's extent are measured - see
        ///<see cref="DensityWindows"/> for why one hanging over the edge fails every layout ever drawn.
        ///
        ///Null when the layer draws nothing, and null when it is smaller than the window: an empty layer
        ///has no windows to have a worst one, and neither has a layout the window does not fit inside.
        ///</summary>
        public static (double Least, double Most)? DensityRange(FlattenedLayout layout, LayerKey layer, int window, int step)
        {
            if (window <= 0 || step <= 0)
                return null;

            var bounds = BoundsOf(layout, layer);

            if (bounds.IsEmpty)
                return null;

            var shapes = new List<IReadOnlyList<Element.Point>>();

            foreach (var element in layout.Elements)
            {
                if (element.Text is not null || element.IsOpen)
                    continue;

                if (element.Layer.Key.Equals(layer))
                    shapes.Add(element.Points);
            }

            //Merged once, outside the sweep. Merging per window would be the same clipping pass repeated
            //for every position of the window, which on a die is thousands of them.
            var merged = Booleans.Merge(shapes);

            if (merged.Count == 0)
                return null;

            double least = double.MaxValue;
            double most = 0;

            foreach (var found in DensityWindows(merged, bounds, window, step))
            {
                least = Math.Min(least, found.Density);
                most = Math.Max(most, found.Density);
            }

            if (least > most)
                return null;

            return (least, most);
        }

        ///<summary>
        ///Every window of the sweep, with how much of it the merged geometry covers.
        ///
        ///Shared with the density *rule*, which asks the same question and wants the windows themselves
        ///rather than the range: a rule reports where the layout is too sparse, and the window is the where.
        ///
        ///Takes geometry that is already merged, because both callers have it and merging inside the sweep
        ///would be the same clipping pass repeated per window.
        ///
        ///**Only windows that fit entirely inside the extent.** A window allowed to hang over the edge is
        ///measuring the empty ground beyond the layout, and the corner ones hang over twice - so a sweep
        ///without this reports every layout ever drawn as too sparse at its own boundary, which a test
        ///caught on two solid blocks side by side. The ground outside a drawing is not a density fault; it
        ///is not part of the drawing.
        ///
        ///Nothing at all when the layout is smaller than the window. A hundred-micron rule has no opinion
        ///about a two-micron test cell, and inventing one by measuring a window that is mostly outside it
        ///would produce a failure about the window rather than about the layout.
        ///</summary>
        public static IEnumerable<(Bounds Window, double Density)> DensityWindows(
            List<List<Element.Point>> merged,
            Bounds extent,
            int window,
            int step)
        {
            if (window <= 0 || step <= 0 || extent.IsEmpty)
                yield break;

            long lastLeft = extent.Right - window;
            long lastBottom = extent.Top - window;

            if (lastLeft < extent.Left || lastBottom < extent.Bottom)
                yield break;

            double area = (double)window * window;

            for (long bottom = extent.Bottom; bottom <= lastBottom; bottom += step)
            {
                for (long left = extent.Left; left <= lastLeft; left += step)
                {
                    var box = new Bounds(
                        checked((int)left),
                        checked((int)bottom),
                        checked((int)(left + window)),
                        checked((int)(bottom + window)));

                    yield return (box, coveredWithin(merged, box) / area);
                }
            }
        }

        ///<summary>How much of a window one merged layer covers, by clipping it to the window.</summary>
        private static double coveredWithin(List<List<Element.Point>> merged, Bounds window)
        {
            var box = new List<IReadOnlyList<Element.Point>>
            {
                new List<Element.Point>
                {
                    new Element.Point { X = window.Left, Y = window.Bottom },
                    new Element.Point { X = window.Right, Y = window.Bottom },
                    new Element.Point { X = window.Right, Y = window.Top },
                    new Element.Point { X = window.Left, Y = window.Top }
                }
            };

            double covered = 0;

            foreach (var piece in Booleans.Combine(merged, box, BooleanOperation.And))
                covered += AreaOf(piece);

            return covered;
        }

        #endregion **************************************************************************
    }
}
