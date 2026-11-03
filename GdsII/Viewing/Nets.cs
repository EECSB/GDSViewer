using Clipper2Lib;

namespace GdsII
{
    ///
    ///Which shapes are electrically the same thing.
    ///
    ///**The rule, and why it is the rule.** Two shapes on the same conductor layer that touch are one net.
    ///Two shapes on *different* conductor layers that touch are not - metal1 and metal2 cross each other all
    ///over a real chip without meeting, and a tool that joined them would report almost every layout as one
    ///enormous net. What joins two conductors is a via, which connects the layers it overlaps - only the ones its
    ///own height range touches, where a layermap has given the heights (see <see cref="Stacked"/>).
    ///
    ///That is the whole model, and it is why <see cref="LayerRole"/> exists. Working connectivity out from
    ///the file alone is not possible: a GDSII file is numbered shapes, and nothing in it says which numbers
    ///are metal. With no roles set, nothing is connected to anything - which is the honest answer, and the UI
    ///says so rather than showing an empty highlight.
    ///
    ///**What this is not.** It is not an LVS extractor. It has no notion of a device, a terminal, a well, or
    ///a resistance, and it will happily call two things one net that a real extractor would separate on a
    ///rule this knows nothing about. It answers one question - what is this piece of metal attached to - and
    ///that question is worth answering on its own.
    ///
    public static class Nets
    {
        ///
        ///How far apart two shapes may be and still count as touching, in database units.
        ///
        ///**Abutting shapes have to connect.** Two rectangles that share an edge intersect in nothing at all
        ///as far as a polygon clipper is concerned - the overlap has zero area - and yet they are the same
        ///wire. So one of them is grown by this much before the two are compared, which catches an edge
        ///shared exactly and a gap of less than a unit.
        ///
        ///One unit, not a tolerance somebody tuned. A database unit is a nanometer on most files and the
        ///coordinates are whole numbers, so anything a unit apart was meant to be touching.
        ///
        public const int Touching = 1;

        ///
        ///Every shape on the same net as one of them, given as indexes into the layout's elements.
        ///
        ///Includes the shape asked about, so an answer is never empty for a shape that takes part - and *is*
        ///empty for one that does not, which is how the caller tells "nothing else is attached" from "this
        ///layer has no role and the question cannot be asked".
        ///
        ///A breadth-first walk out from the starting shape rather than a full extraction of every net in the
        ///file. One net is what somebody clicking a wire is asking about, and the whole file is the expensive
        ///thing this deliberately does not do.
        ///
        public static HashSet<int> Reaching(FlattenedLayout layout, int from)
        {
            var walk = Begin(layout, from);

            walk.Step(int.MaxValue);

            return walk.Found;
        }

        ///
        ///The same walk, taken a number of shapes at a time - see <see cref="Walk.Step"/>.
        ///
        ///For a net too big to want all at once. A supply net on a real chip is tens of thousands of shapes,
        ///and somebody clicking a wire usually wants to see where it goes next rather than every last one of
        ///them; a walk that stops lets them ask for more.
        ///
        public static Walk Begin(FlattenedLayout layout, int from)
        {
            return new Walk(layout, from);
        }

        ///
        ///A net being traced out from one shape, as far as it has got.
        ///
        ///Breadth first, so what a step adds is the next ring out from the shape it started at: the first
        ///hundred are the hundred nearest it in hops, which is where somebody following a wire is looking.
        ///
        public sealed class Walk
        {
            private readonly Parts parts;

            private readonly Queue<int> toVisit = new Queue<int>();

            ///<summary>What the shape being visited touches and the last step had no room for, taken first next time.</summary>
            private readonly Queue<int> waiting = new Queue<int>();

            internal Walk(FlattenedLayout layout, int from)
            {
                parts = new Parts(layout);

                if (!parts.Holds(from))
                    return;

                Found.Add(from);
                toVisit.Enqueue(from);
            }

            ///<summary>Every shape reached so far, the one it started at included. Empty for a shape that takes no part.</summary>
            public HashSet<int> Found { get; } = new HashSet<int>();

            ///<summary>Whether the whole net has been reached, so another step would add nothing.</summary>
            public bool Done => toVisit.Count == 0 && waiting.Count == 0;

            ///
            ///Reaches at most this many more shapes, and says how many it did.
            ///
            ///**Exactly that many, unless the net runs out first.** A via can touch hundreds of shapes, so a
            ///step that only stopped between shapes visited would overshoot by however many the last one held;
            ///what it touched beyond the count waits for the next step instead.
            ///
            public int Step(int most)
            {
                int added = 0;

                while (added < most && !Done)
                {
                    if (waiting.Count == 0)
                    {
                        foreach (int next in parts.Touching(toVisit.Dequeue()))
                        {
                            if (!Found.Contains(next))
                                waiting.Enqueue(next);
                        }
                    }

                    while (added < most && waiting.Count > 0)
                    {
                        int next = waiting.Dequeue();

                        if (!Found.Add(next))
                            continue;

                        toVisit.Enqueue(next);
                        added++;
                    }
                }

                lookAhead();

                return added;
            }

            ///
            ///Visits on until something new is waiting, or there is nothing left to visit.
            ///
            ///**So that <see cref="Done"/> is true the moment it is.** Without this a net found to its last shape
            ///still has that shape to visit, and the walk says there is more - a Next that then reaches nothing.
            ///Usually one more shape visited; at the end of a net, the shapes on its edge, none of which lead on.
            ///
            private void lookAhead()
            {
                while (toVisit.Count > 0 && !waiting.Any(next => !Found.Contains(next)))
                {
                    waiting.Clear();

                    foreach (int next in parts.Touching(toVisit.Dequeue()))
                    {
                        if (!Found.Contains(next))
                            waiting.Enqueue(next);
                    }
                }

                //What waits is only ever what is still to be found, so an empty net-edge leaves nothing behind.
                if (!waiting.Any(next => !Found.Contains(next)))
                    waiting.Clear();
            }
        }

        ///
        ///Every net in the layout, each as the set of shapes on it.
        ///
        ///**Built once rather than by asking <see cref="Reaching"/> repeatedly.** That one answers about a
        ///shape somebody clicked and builds its own <see cref="Parts"/> to do it, which is right for one
        ///question and quadratic for all of them - a file with four hundred nets would build the same
        ///adjacency four hundred times.
        ///
        ///The whole file rather than one net is the expensive thing this library otherwise declines to do,
        ///and it is here because an antenna rule is a question about every net at once: the ratio of metal
        ///to gate is not something one net can be asked in isolation without knowing it is the worst.
        ///
        ///Nets of one shape are included. A piece of metal attached to nothing is still a net, and on an
        ///antenna rule it is the interesting kind - a long wire reaching no gate at all is exactly what the
        ///rule exists to catch.
        ///
        public static List<HashSet<int>> All(FlattenedLayout layout)
        {
            var nets = new List<HashSet<int>>();

            var parts = new Parts(layout);

            var placed = new HashSet<int>();

            for (int i = 0; i < layout.Elements.Count; i++)
            {
                if (!parts.Holds(i) || placed.Contains(i))
                    continue;

                var net = new HashSet<int> { i };
                var toVisit = new Queue<int>();

                toVisit.Enqueue(i);

                while (toVisit.Count > 0)
                {
                    int at = toVisit.Dequeue();

                    foreach (int next in parts.Touching(at))
                    {
                        if (net.Add(next))
                            toVisit.Enqueue(next);
                    }
                }

                foreach (int index in net)
                    placed.Add(index);

                nets.Add(net);
            }

            return nets;
        }

        ///
        ///What is needed to ask whether two shapes meet.
        ///
        ///Built once per question rather than cached on the layout: the roles are settings somebody is
        ///changing while looking at the answer, and a cache that outlived a change to them would be a wrong
        ///answer that looked authoritative.
        ///
        ///**What a shape might touch comes from the layout's index**, the one the view culls with, as whatever
        ///lies under the shape's box grown by the slack. It was every shape that takes part, checked against
        ///each shape reached, so a net of ten thousand shapes in a layout of two hundred thousand was two billion
        ///box tests - and tracing the RGB mixer's supply froze the page. A shape is asked about its neighbors
        ///now, and the work goes with the size of the net rather than with the size of the net times the file.
        ///
        private sealed class Parts
        {
            private readonly FlattenedLayout layout;

            ///<summary>
            ///Each shape grown by <see cref="Touching"/>, worked out the first time it is needed.
            ///
            ///Lazily, because a breadth-first walk out from one shape reaches a handful of a file's
            ///thousands - growing every shape up front would be most of the cost of the whole thing spent on
            ///shapes the walk never looks at.
            ///</summary>
            private readonly Dictionary<int, List<List<Element.Point>>> grown = new Dictionary<int, List<List<Element.Point>>>();

            public Parts(FlattenedLayout layout)
            {
                this.layout = layout;
            }

            ///<summary>Whether this index is one of the shapes that take part; see Nets.TakesPart.</summary>
            public bool Holds(int index)
            {
                if (index < 0 || index >= layout.Elements.Count)
                    return false;

                return Nets.TakesPart(layout.Elements[index]);
            }

            ///<summary>Everything directly attached to one shape - one step, not the whole net. In layout order.</summary>
            public IEnumerable<int> Touching(int index)
            {
                var element = layout.Elements[index];
                var box = element.Box;

                foreach (int other in layout.Index.Within(box.Grown(Nets.Touching)))
                {
                    if (other == index)
                        continue;

                    var candidate = layout.Elements[other];

                    if (!Nets.TakesPart(candidate) || !CanMeet(element, candidate))
                        continue;

                    //The boxes first, grown by the same slack the shapes are, because it rejects most of what is
                    //left for almost nothing and the geometry test is the expensive one.
                    if (!Near(box, candidate.Box))
                        continue;

                    //
                    //**Two rectangles meet exactly when their boxes do**, which is most of what a real layout
                    //is made of and most of what a trace asks. The grow is mitered, so a rectangle grown by one
                    //unit is its box grown by one, and that shares area with another rectangle when the two
                    //boxes overlap or touch - corners included. Clipper gives the same answer at the cost of a
                    //grow and an intersection per pair: tracing one of the RGB mixer's supplies was thirty
                    //seconds of them.
                    //
                    if (IsRectangle(element) && IsRectangle(candidate))
                    {
                        if (BoxesMeet(box, candidate.Box))
                            yield return other;

                        continue;
                    }

                    if (Meets(GrownOf(index), candidate.Points))
                        yield return other;
                }
            }

            private List<List<Element.Point>> GrownOf(int index)
            {
                if (grown.TryGetValue(index, out var already))
                    return already;

                var made = Booleans.Grow(new[] { layout.Elements[index].Points }, Nets.Touching);

                grown[index] = made;

                return made;
            }
        }

        ///
        ///What a net is called: the labels written onto it.
        ///
        ///**A net has no name of its own anywhere in the file.** GDSII stores shapes, and the way a layout
        ///says which piece of metal is VPWR is to put a TEXT element down on top of it. So the name is not
        ///read, it is *found* - by asking which labels land on the shapes the net is made of.
        ///
        ///A label takes no part in connectivity, which is why <see cref="Reaching"/> skips it: it is an
        ///annotation and conducts nothing. This is the separate question, asked afterwards of a net that has
        ///already been worked out.
        ///
        ///**Matched by layer number, like everything else here.** A PDK writes the label on a different data
        ///type from the metal - `68/16` naming what is drawn on `68/20` - so requiring the whole pair to match
        ///would find no names at all on the files this is for. The anchor has to be *on* the shape, though:
        ///a label floating in the gap between two wires names neither of them.
        ///
        ///In the order the file holds them, and without repeats. More than one distinct name on one net is
        ///worth seeing rather than hiding - it is either two spellings of the same thing or two nets that are
        ///shorted, and both are things somebody would want to know.
        ///
        public static List<string> NamesOn(FlattenedLayout layout, IReadOnlyCollection<int> net)
        {
            var names = new List<string>();

            if (net.Count == 0)
                return names;

            //The net's shapes, by layer number, so a label is only compared against metal it could be on.
            var byNumber = new Dictionary<short, List<Element>>();

            foreach (int index in net)
            {
                if (index < 0 || index >= layout.Elements.Count)
                    continue;

                var shape = layout.Elements[index];

                if (shape.Text is not null || shape.Points.Count < 3)
                    continue;

                if (!byNumber.TryGetValue(shape.Layer.Number, out var on))
                {
                    on = new List<Element>();
                    byNumber[shape.Layer.Number] = on;
                }

                on.Add(shape);
            }

            var seen = new HashSet<string>();

            foreach (var label in layout.Elements)
            {
                if (label.Text is not string says || says.Length == 0 || label.Points.Count == 0)
                    continue;

                if (!byNumber.TryGetValue(label.Layer.Number, out var on))
                    continue;

                if (!Anywhere(label.Points[0], on))
                    continue;

                if (seen.Add(says))
                    names.Add(says);
            }

            return names;
        }

        ///<summary>Whether a point lands on any of those shapes, inside or on the edge.</summary>
        private static bool Anywhere(Element.Point at, List<Element> shapes)
        {
            var point = new Point64(at.X, at.Y);

            foreach (var shape in shapes)
            {
                //The box first: it rejects nearly every pair for almost nothing, and a label sits on one
                //shape out of however many the net has.
                if (!shape.Box.Contains(at))
                    continue;

                var path = new Path64(shape.Points.Count);

                foreach (var corner in shape.Points)
                    path.Add(new Point64(corner.X, corner.Y));

                //On the edge counts. A pin label is routinely placed on the boundary of the shape it names,
                //and refusing those would lose the names on exactly the shapes people label.
                if (Clipper.PointInPolygon(point, path) != PointInPolygonResult.IsOutside)
                    return true;
            }

            return false;
        }

        ///<summary>What a shape's layer is for, which is the whole of what decides connectivity.</summary>
        public static LayerRole RoleOf(Element element)
        {
            return element.Layer.Role;
        }

        ///
        ///Whether a net can be traced from this shape at all.
        ///
        ///**The one condition, asked in one place.** A caller offering the operation and the walk performing
        ///it have to agree about this or the button is offered on something that comes back empty - which is
        ///exactly what happened: a pin *label* sits on a conducting layer, so its role passes, and it has one
        ///point, so the walk refused it. Pressed, it did nothing at all, which reads as a net of one shape.
        ///
        ///A label conducts nothing. It names a net - see <see cref="NamesOn"/> - and is not part of one.
        ///
        public static bool TakesPart(Element element)
        {
            return RoleOf(element) != LayerRole.None && element.Text is null && element.Points.Count >= 3;
        }

        ///
        ///Whether two shapes are the kind of thing that could be connected, before any geometry is looked at.
        ///
        ///**The layer number, not the whole pair.** A PDK spells one physical layer as several data types -
        ///drawing, pin, label - and they are the same piece of metal. Requiring the data types to match too
        ///would break a net at every pin, which is exactly where somebody clicks.
        ///
        public static bool CanMeet(Element one, Element other)
        {
            var first = RoleOf(one);
            var second = RoleOf(other);

            if (first == LayerRole.None || second == LayerRole.None)
                return false;

            //A via is the only way two different conductors ever meet - but only the ones it reaches up and down.
            if (first == LayerRole.Via || second == LayerRole.Via)
                return Stacked(one.Layer, other.Layer);

            return one.Layer.Number == other.Layer.Number;
        }

        ///
        ///Whether two layers reach each other up and down the stack, so a via on one can join the other.
        ///
        ///**A via joins the two layers it sits between, not everything it overlaps.** Seen from above, an mcon
        ///between li1 and met1 also lies under whatever met2, met3 and met4 run over it, and joining all of them
        ///shorted every route on a chip to every other: on the RGB mixer, twelve shapes picked anywhere each traced
        ///to more than seventy thousand, and a trace froze the page walking what was the whole chip. Where a
        ///layermap has given the layers their heights, a via joins only what its own height range touches - li1 at
        ///940 to 1,040 and met1 from 1,370 for an mcon at 1,040 to 1,370, and not met2 at 2,000.
        ///
        ///Where either has no height of its own, the stack says nothing, and the via joins whatever it overlaps
        ///as it always did: a file with roles and no heights is still traceable, with that limitation.
        ///
        public static bool Stacked(Layer one, Layer other)
        {
            if (one.CustomHeight is not int oneBottom || other.CustomHeight is not int otherBottom)
                return true;

            long oneTop = (long)oneBottom + Math.Max(0, one.Depth);
            long otherTop = (long)otherBottom + Math.Max(0, other.Depth);

            return oneBottom <= otherTop && otherBottom <= oneTop;
        }

        ///<summary>Whether two boxes are within the slack that counts as touching.</summary>
        public static bool Near(Bounds one, Bounds other)
        {
            if (one.IsEmpty || other.IsEmpty)
                return false;

            if (one.Right + Touching < other.Left || other.Right + Touching < one.Left)
                return false;

            if (one.Top + Touching < other.Bottom || other.Top + Touching < one.Bottom)
                return false;

            return true;
        }

        ///
        ///Whether a shape is a rectangle squared to the axes and covering some area: four corners, each one a
        ///corner of its box, with the first repeated at the end or not.
        ///
        private static bool IsRectangle(Element element)
        {
            var points = element.Points;
            int count = points.Count;

            if (count == 5 && points[0].Equals(points[4]))
                count = 4;

            if (count != 4)
                return false;

            var box = element.Box;

            if (box.Width <= 0 || box.Height <= 0)
                return false;

            for (int i = 0; i < 4; i++)
            {
                if (points[i].X != box.Left && points[i].X != box.Right)
                    return false;

                if (points[i].Y != box.Bottom && points[i].Y != box.Top)
                    return false;

                //Four different corners: one written twice leaves a triangle with the same box.
                for (int j = 0; j < i; j++)
                {
                    if (points[i].Equals(points[j]))
                        return false;
                }
            }

            return true;
        }

        ///<summary>Whether two boxes overlap or touch, edges and corners included.</summary>
        private static bool BoxesMeet(Bounds one, Bounds other)
        {
            return one.Left <= other.Right && other.Left <= one.Right && one.Bottom <= other.Top && other.Bottom <= one.Top;
        }

        ///<summary>Whether a grown shape and another actually share any area.</summary>
        private static bool Meets(List<List<Element.Point>> grown, IReadOnlyList<Element.Point> other)
        {
            if (grown.Count == 0)
                return false;

            return Booleans.Combine(grown, new[] { other }, BooleanOperation.And).Count > 0;
        }

        ///<summary>Whether anything in the file has been told what it is for, which is what makes this askable.</summary>
        public static bool AnyRolesSet(IEnumerable<Layer> layers)
        {
            foreach (var layer in layers)
            {
                if (layer.Role != LayerRole.None)
                    return true;
            }

            return false;
        }
    }
}
