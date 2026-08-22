using Clipper2Lib;

namespace GdsII
{
    ///
    ///Which shapes are electrically the same thing.
    ///
    ///**The rule, and why it is the rule.** Two shapes on the same conductor layer that touch are one net.
    ///Two shapes on *different* conductor layers that touch are not - metal1 and metal2 cross each other all
    ///over a real chip without meeting, and a tool that joined them would report almost every layout as one
    ///enormous net. What joins two conductors is a via, which is why a via connects whatever it overlaps
    ///regardless of layer.
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
            var found = new HashSet<int>();

            if (from < 0 || from >= layout.Elements.Count)
                return found;

            var parts = Parts.Of(layout);

            if (!parts.Holds(from))
                return found;

            var toVisit = new Queue<int>();

            found.Add(from);
            toVisit.Enqueue(from);

            while (toVisit.Count > 0)
            {
                int at = toVisit.Dequeue();

                foreach (int next in parts.Touching(at))
                {
                    if (found.Add(next))
                        toVisit.Enqueue(next);
                }
            }

            return found;
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

            var parts = Parts.Of(layout);

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
        ///The shapes that take part, with what is needed to ask whether two of them meet.
        ///
        ///Built once per question rather than cached on the layout: the roles are settings somebody is
        ///changing while looking at the answer, and a cache that outlived a change to them would be a wrong
        ///answer that looked authoritative.
        ///
        private sealed class Parts
        {
            private readonly FlattenedLayout layout;

            ///<summary>Which elements take part at all, and their boxes - the cheap test, done first.</summary>
            private readonly List<int> taking = new List<int>();

            private readonly Dictionary<int, Bounds> boxes = new Dictionary<int, Bounds>();

            ///<summary>
            ///Each shape grown by <see cref="Touching"/>, worked out the first time it is needed.
            ///
            ///Lazily, because a breadth-first walk out from one shape reaches a handful of a file's
            ///thousands - growing every shape up front would be most of the cost of the whole thing spent on
            ///shapes the walk never looks at.
            ///</summary>
            private readonly Dictionary<int, List<List<Element.Point>>> grown = new Dictionary<int, List<List<Element.Point>>>();

            private Parts(FlattenedLayout layout)
            {
                this.layout = layout;
            }

            public static Parts Of(FlattenedLayout layout)
            {
                var parts = new Parts(layout);

                for (int i = 0; i < layout.Elements.Count; i++)
                {
                    var element = layout.Elements[i];

                    if (!Nets.TakesPart(element))
                        continue;

                    parts.taking.Add(i);
                    parts.boxes[i] = element.Box;
                }

                return parts;
            }

            ///<summary>Whether this index is one of the shapes that take part; see Nets.TakesPart.</summary>
            public bool Holds(int index)
            {
                return boxes.ContainsKey(index);
            }

            ///<summary>Everything directly attached to one shape - one step, not the whole net.</summary>
            public IEnumerable<int> Touching(int index)
            {
                var box = boxes[index];
                var element = layout.Elements[index];

                foreach (int other in taking)
                {
                    if (other == index)
                        continue;

                    if (!CanMeet(element, layout.Elements[other]))
                        continue;

                    //The boxes first, grown by the same slack the shapes are, because it rejects almost
                    //every pair for almost nothing and the geometry test is the expensive one.
                    if (!Near(box, boxes[other]))
                        continue;

                    if (Meets(GrownOf(index), layout.Elements[other].Points))
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

            //A via joins whatever it overlaps, which is the only way two different conductors ever meet.
            if (first == LayerRole.Via || second == LayerRole.Via)
                return true;

            return one.Layer.Number == other.Layer.Number;
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
