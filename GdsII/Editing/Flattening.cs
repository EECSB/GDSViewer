using static GdsII.GDS;

namespace GdsII
{
    ///
    ///Taking placements apart: what each one draws, written into the cell that holds it instead.
    ///
    ///The inverse of making a cell, and the one that has to do arithmetic. Every element of the placed cell is
    ///copied into the placing one through the placement, so what was drawn stays exactly where it was drawn -
    ///and that is more than moving coordinates, for the reasons <see cref="Hierarchy.Through(IReadOnlyList{Record}, Transform)"/>
    ///gives. Exact for a placement that is square, rounded for one at some angle nobody chose: the same trade
    ///every other edit here makes.
    ///
    ///**One level.** A placement inside the placed cell comes across as a placement, turned with the one being
    ///flattened; flattening that one too is another flatten. **An array is one placement**, so it comes apart
    ///into every place it has, whichever of them was pointed at.
    ///
    public static class Flattening
    {
        ///
        ///The most elements one flatten writes.
        ///
        ///An array is where a number like that comes from: a hundred by a hundred of a cell holding ten shapes is a
        ///hundred thousand elements, which is a different file rather than the same one taken apart - and every
        ///one of them goes in through the same edit a single shape does.
        ///
        public const int MostElements = 10_000;

        ///<summary>How many elements flattening a placement writes: everything in the cell it places, once per instance.</summary>
        public static long CountOf(GDS gds, ElementModel placement)
        {
            if (Hierarchy.PlacedBy(placement) is not string name || Hierarchy.Named(gds, name) is not StructureModel placed)
                return 0;

            return Hierarchy.InstanceCount(placement) * placed.Elements.Count;
        }

        ///
        ///The cells that leave the library when these placements are flattened: each one whose every placement
        ///is among them.
        ///
        ///**The cell goes with its last placement.** A cell nothing places is drawn as a top of its own, so leaving
        ///an emptied-out cell behind would put every shape on screen twice: once inline where it has just been
        ///written, and once more as the orphan drawing itself. A cell something else still places stays.
        ///
        public static List<string> CellsGoing(GDS gds, IReadOnlyList<ElementModel> placements)
        {
            var flattening = new Dictionary<string, int>(StringComparer.Ordinal);
            var order = new List<string>();
            var seen = new HashSet<ElementModel>(ReferenceEqualityComparer.Instance);

            foreach (var placement in placements)
            {
                if (!seen.Add(placement) || Hierarchy.PlacedBy(placement) is not string name)
                    continue;

                if (!flattening.ContainsKey(name))
                {
                    flattening[name] = 0;
                    order.Add(name);
                }

                flattening[name]++;
            }

            return order.Where(name => Hierarchy.PlacementsOf(gds, name) == flattening[name]).ToList();
        }

        ///
        ///The edits that flatten placements in one cell: each placement taken out and what it draws put in at its
        ///place, then the cells that went with their last placement taken out of the library.
        ///
        ///**Where the placement was**, so the cell reads in the same order it did. **Last first**, because each
        ///index was found before anything changed - working back from the end means every placement is still
        ///where it was found when its turn comes, since only what follows it has moved.
        ///
        ///Null when one of them is not a placement in that cell, places a cell the library does not have or the
        ///cell it is in; when together they would write more than <see cref="MostElements"/>; or when something
        ///would land past what a coordinate holds. Nothing is applied here - the caller puts the edits on the
        ///history as one step, so a flatten is one press of Undo.
        ///
        public static List<LayoutEdit>? Edits(GDS gds, StructureModel inside, IReadOnlyList<ElementModel> placements)
        {
            var found = new List<(int At, ElementModel Placement, StructureModel Placed)>();
            long writing = 0;

            foreach (var placement in placements)
            {
                if (found.Any(each => ReferenceEquals(each.Placement, placement)))
                    continue;

                int at = indexIn(inside, placement);

                if (at < 0 || Hierarchy.PlacedBy(placement) is not string name || name == Hierarchy.NameOf(inside))
                    return null;

                if (Hierarchy.Named(gds, name) is not StructureModel placed)
                    return null;

                writing += CountOf(gds, placement);
                found.Add((at, placement, placed));
            }

            if (found.Count == 0 || writing > MostElements)
                return null;

            found.Sort((one, other) => other.At.CompareTo(one.At));

            //Each placed cell's records copied once, however many instances they are written through.
            var copied = new Dictionary<StructureModel, List<List<Record>>>(ReferenceEqualityComparer.Instance);
            var edits = new List<LayoutEdit>();

            foreach (var (at, placement, placed) in found)
            {
                if (!copied.TryGetValue(placed, out var contents))
                {
                    contents = new List<List<Record>>();

                    foreach (var element in placed.Elements)
                    {
                        if (ElementRecords.Copied(gds, element) is not List<Record> span)
                            return null;

                        contents.Add(span);
                    }

                    copied[placed] = contents;
                }

                edits.Add(new DeleteElement(gds, inside, placement));

                int put = at;

                foreach (var instance in Hierarchy.InstancesOf(placement))
                {
                    foreach (var span in contents)
                    {
                        if (Hierarchy.Through(span, instance, instance) is not List<Record> moved)
                            return null;

                        edits.Add(new AddElement(gds, inside, put, moved, "Flatten"));
                        put++;
                    }
                }
            }

            foreach (string name in CellsGoing(gds, found.Select(each => each.Placement).ToList()))
                edits.Add(new RemoveStructure(gds, name));

            return edits;
        }

        ///<summary>Where an element is in its cell, by the element itself rather than by anything equal to it.</summary>
        private static int indexIn(StructureModel inside, ElementModel model)
        {
            for (int i = 0; i < inside.Elements.Count; i++)
            {
                if (ReferenceEquals(inside.Elements[i], model))
                    return i;
            }

            return -1;
        }
    }
}
