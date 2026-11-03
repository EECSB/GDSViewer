using Clipper2Lib;

namespace GdsII
{
    ///<summary>
    ///Which element of a layout is under a point.
    ///
    ///**This used to be the browser's job, and could not be tested.** A pointer event named the node it
    ///landed on, which meant every shape had to be a node - and that is what made panning a large layout
    ///cost 50.8 ms a frame. The picture is one path per layer now, so there is no node per shape to land on
    ///and the question has to be answered here.
    ///
    ///Which is the better place for it anyway. A DOM hit test answers "whatever is drawn on top", and while
    ///a cell is being edited that is the wrong answer: the layout around it is faded because it is not what
    ///the pointer is for, and a shape of another cell sitting over the one being worked on would still take
    ///the click. Asking the layout lets the cell being edited win, which is what somebody clicking through a
    ///faded context means. It is also directly testable, which the browser's answer never was.
    ///</summary>
    public static class Picking
    {
        ///<summary>
        ///The element under <paramref name="point"/>, or -1 for none.
        ///
        ///<paramref name="visibleLayers"/> keeps a hidden layer from taking a click on what is drawn under
        ///it - a shape nobody can see is not a shape anybody meant to choose. Null for every layer.
        ///
        ///<paramref name="context"/> is the cell being edited, whose shapes win over anything else at the
        ///same point. Null when the whole layout is being looked at rather than edited, which is when the
        ///last element wins - later in the file is later in the drawing.
        ///</summary>
        public static int At(
            FlattenedLayout layout,
            Element.Point point,
            IReadOnlySet<LayerKey>? visibleLayers = null,
            CellContext? context = null)
        {
            int found = -1;
            int inContext = -1;

            //
            //**Only the elements whose box holds the point**, out of the layout's index and in layout order -
            //so the walk below decides between the same candidates in the same order it always did, and the
            //last one still wins. It used to be every element: half a second a click on a real chip, on the
            //dev server, before anything was chosen.
            //
            foreach (int index in layout.Index.Under(point))
            {
                var element = layout.Elements[index];

                if (visibleLayers is not null && !visibleLayers.Contains(element.Layer.Key))
                    continue;

                if (!Covers(element, point))
                    continue;

                //**The last one wins, not the first.** Later in the layout is later in the drawing, so this
                //is the shape on top - which is the answer the DOM used to give and the one somebody
                //clicking expects. Returning the first match instead loses a shape just drawn to whatever
                //it was drawn over, which is exactly the case an editor is used for.
                found = index;

                if (context is not null && context.IsLookingThrough(element))
                    inContext = index;
            }

            //The cell being edited beats anything drawn over it, whatever the order.
            if (inContext >= 0)
                return inContext;

            return found;
        }

        ///<summary>
        ///Whether anything that picks may take this element at all: on a layer in <paramref name="reachable"/>,
        ///or on any layer when that is null.
        ///
        ///**One test for every way of picking.** <see cref="At"/> has always kept a hidden layer from taking a
        ///click; a rubber band and Select All asked only about a lock, so a band caught a hidden layer's
        ///shapes by their boxes and they came back as outlines over empty canvas. A click with a pixel of
        ///movement in it is a band, which made that a click as far as anybody pressing could tell.
        ///</summary>
        public static bool InReach(Element element, IReadOnlySet<LayerKey>? reachable)
        {
            return reachable is null || reachable.Contains(element.Layer.Key);
        }

        ///<summary>
        ///Every element a rubber band over <paramref name="box"/> catches, in layout order.
        ///
        ///Caught by touching rather than by being wholly inside, which is what a drag across a dense layout is
        ///usually reaching for - and only within <paramref name="reachable"/>, the same set a click is
        ///measured against. See <see cref="InReach"/>.
        ///</summary>
        public static List<int> Caught(FlattenedLayout layout, Bounds box, IReadOnlySet<LayerKey>? reachable = null)
        {
            var caught = new List<int>();

            //Out of the index, which reads only the part of the layout under the band - and in layout order,
            //which is the order this has always handed back.
            foreach (int index in layout.Index.Within(box))
            {
                if (!InReach(layout.Elements[index], reachable))
                    continue;

                caught.Add(index);
            }

            return caught;
        }

        ///<summary>
        ///Which of two elements a click on both of them means.
        ///
        ///**Labels are still nodes, and the geometry is not.** A label is drawn as its own text and the
        ///browser hit-tests it; everything else is a subpath of a layer's path and is found by
        ///<see cref="At"/>. So a click can turn up two answers, and something has to choose between them -
        ///which used to be the DOM's stacking order, back when both were nodes.
        ///
        ///The same rule it used: whatever is drawn later wins, and later in the layout is later in the
        ///drawing. Without it a label swallows every click meant for a shape drawn over it, because a
        ///name's box is far larger than the anchor it hangs from.
        ///
        ///The cell being edited still outranks both, for the reason given on <see cref="At"/>.
        ///</summary>
        public static int Preferred(FlattenedLayout layout, CellContext? context, int one, int other)
        {
            if (one < 0)
                return other;

            if (other < 0)
                return one;

            if (context is not null)
            {
                bool oneIsIn = context.IsLookingThrough(layout.Elements[one]);
                bool otherIsIn = context.IsLookingThrough(layout.Elements[other]);

                if (oneIsIn != otherIsIn)
                {
                    if (oneIsIn)
                        return one;

                    return other;
                }
            }

            return Math.Max(one, other);
        }

        ///<summary>
        ///Whether a shape covers a point: its box first, then its actual outline.
        ///
        ///The box rejects nearly every element for almost nothing, and it is already worked out and cached -
        ///see <see cref="Element.Box"/>. The layout's index has usually rejected the rest before this is
        ///asked; see <see cref="At"/>.
        ///
        ///**A label is its anchor and nothing else**, so it is never picked here. Labels are still drawn as
        ///their own nodes and the browser still hit-tests those; what this answers for is the geometry,
        ///which is the part that stopped being nodes.
        ///
        ///On the edge counts, the same as <see cref="Nets"/>: a click on the boundary of a shape is a click
        ///on the shape, and a rectangle whose edge refused would be unselectable along its whole outline.
        ///
        ///An open run has no inside. It is stroked rather than filled, so what there is to hit is the line
        ///itself - and the box test alone is what stands in for that, which is generous by a hair and the
        ///right way to be wrong about something two pixels wide.
        ///</summary>
        public static bool Covers(Element element, Element.Point point)
        {
            if (element.Text is not null)
                return false;

            if (!element.Box.Contains(point))
                return false;

            if (element.IsOpen)
                return true;

            var path = new Path64(element.Points.Count);

            foreach (var corner in element.Points)
                path.Add(new Point64(corner.X, corner.Y));

            return Clipper.PointInPolygon(new Point64(point.X, point.Y), path) != PointInPolygonResult.IsOutside;
        }
    }
}
