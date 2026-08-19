using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///<summary>
    ///One change to a library, and how to take it back.
    ///
    ///**A command rather than a snapshot.** Copying the whole library before every change would be
    ///trivially correct and would cost a copy of the file per keystroke; a real layout is hundreds of
    ///megabytes flattened, and an undo stack of those is not an undo stack. Each of these instead holds
    ///only what it needs to reverse itself, which for a move is two numbers.
    ///
    ///**The record list and the model are the same records.** A GDS holds a flat list of records and a
    ///tree built over it, and the tree's nodes are the very objects in the list rather than copies - which
    ///is what lets a move change a coordinate in one place and have the download, the text view and both
    ///renderers all see it. Anything that adds or removes records has to keep the two in step by hand, and
    ///that is what <see cref="ElementRecords"/> is for.
    ///
    ///**An edit can also be written down.** <see cref="Describe"/> turns one into an <see cref="EditRecord"/>
    ///that survives the file being closed, and <see cref="Rebuild"/> turns it back - which is what lets an
    ///undo stack outlive the page it was made on.
    ///</summary>
    public abstract class LayoutEdit
    {
        ///<summary>What this did, for an undo button to name.</summary>
        public abstract string Description { get; }

        public abstract void Apply();

        public abstract void Revert();

        ///<summary>
        ///This edit in a form that can be stored, or null when it cannot be written down.
        ///
        ///Null rather than a half-filled record: an edit that has never run does not know where it acted,
        ///and one whose structure has gone did not act anywhere that still exists. Either would come back as
        ///an edit pointing at the wrong shape, which is worse than an edit that does not come back at all.
        ///</summary>
        public abstract EditRecord? Describe();

        ///<summary>
        ///Builds an edit back from what was written down, against a library that has just been opened.
        ///
        ///Null for anything that does not fit the file in hand: a structure it names that is not there, an
        ///index past the end, a kind this version does not know. A restored stack is a convenience, and the
        ///one thing it must never do is apply itself to the wrong shape.
        ///</summary>
        public static LayoutEdit? Rebuild(EditRecord written, GDS gds)
        {
            if (written.Kind == GroupKind)
                return rebuildGroup(written, gds);

            //Before the lookup below, because a whole cell is the one edit that acts on the library rather
            //than inside one of its structures - it has no structure to name.
            if (written.Kind == CellKind || written.Kind == CellGoneKind)
            {
                if (ElementRecords.Read(written.Records) is not List<Record> whole)
                    return null;

                if (written.Kind == CellKind)
                    return new AddStructure(gds, whole, nameOr(written.Label, "Cell"));

                return new RemoveStructure(gds, nameOr(written.Label, "Cell"), whole, written.At);
            }

            //Also before the lookup: a rename holds both names, and which of them the library answers to
            //depends on whether it is about to be applied or taken back.
            if (written.Kind == RenameKind)
            {
                if (written.Structure.Length == 0 || written.Label.Length == 0)
                    return null;

                return new RenameStructure(gds, written.Structure, written.Label);
            }

            if (StructureNamed(gds, written.Structure) is not StructureModel structure)
                return null;

            if (written.At < 0)
                return null;

            if (written.Kind == MoveKind)
                return new MoveElement(structure, written.At, written.Dx, written.Dy);

            if (written.Kind == VertexKind)
                return new MoveVertex(structure, written.At, written.Corner, written.Dx, written.Dy);

            if (written.Kind == LayerKind)
            {
                if (written.Before is not int[] from || written.After is not int[] onto)
                    return null;

                if (from.Length < 2 || onto.Length < 2)
                    return null;

                return new RelayerElement(
                    gds,
                    structure,
                    written.At,
                    new LayerKey((short)from[0], (short)from[1]),
                    new LayerKey((short)onto[0], (short)onto[1]));
            }

            if (written.Kind == TextKind)
            {
                if (written.Said is not string said || written.Says is not string says)
                    return null;

                return new RetextElement(structure, written.At, said, says);
            }

            if (written.Kind == ReshapeKind)
            {
                if (written.Before is not int[] before || written.After is not int[] after)
                    return null;

                if (before.Length == 0 || after.Length == 0)
                    return null;

                return new ReshapeElement(structure, written.At, before, after, nameOr(written.Label, "Turn"));
            }

            if (ElementRecords.Read(written.Records) is not List<Record> records)
                return null;

            //A session written before inserts carried a name has none, and "Draw" is what they all were.
            if (written.Kind == InsertKind)
                return new AddElement(gds, structure, written.At, records, nameOr(written.Label, "Draw"));

            if (written.Kind == RemoveKind)
                return new DeleteElement(gds, structure, written.At, records);

            return null;
        }

        private static LayoutEdit? rebuildGroup(EditRecord written, GDS gds)
        {
            if (written.Parts is null || written.Parts.Count == 0)
                return null;

            var parts = new List<LayoutEdit>();

            //All of them or none. Half a gesture on the stack would undo half of what one press made.
            foreach (var part in written.Parts)
            {
                if (Rebuild(part, gds) is not LayoutEdit edit)
                    return null;

                parts.Add(edit);
            }

            return new CompoundEdit(written.Label, parts);
        }

        private static string nameOr(string written, string fallback)
        {
            if (string.IsNullOrEmpty(written))
                return fallback;

            return written;
        }

        internal const string MoveKind = "move";
        internal const string VertexKind = "vertex";
        internal const string ReshapeKind = "reshape";
        internal const string LayerKind = "layer";
        internal const string TextKind = "text";
        internal const string CellKind = "cell";
        internal const string CellGoneKind = "cellgone";
        internal const string RenameKind = "rename";
        internal const string InsertKind = "insert";
        internal const string RemoveKind = "remove";
        internal const string GroupKind = "group";

        ///<summary>The name a structure is known by, which is how an edit says where it acted.</summary>
        internal static string NameOf(StructureModel structure)
        {
            if (structure.STRNAME?.Data is AsciiData name)
                return name.Value;

            return "";
        }

        internal static StructureModel? StructureNamed(GDS gds, string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return gds.StreamFormat.Structures.FirstOrDefault(each => NameOf(each) == name);
        }
    }

    ///<summary>
    ///An edit that acts on an element a structure already holds.
    ///
    ///**Where, not which.** The element is addressed as a place in its structure and not only as the object,
    ///because the object does not survive the file being closed: reopening parses new ones, and an edit read
    ///back from a session has an index and nothing else.
    ///
    ///**The index is taken as the edit runs, not when it was made.** Several edits made in one gesture are
    ///made against one state and then applied one after another - three shapes deleted together are at three
    ///indexes when the gesture starts and at three different ones by the time the third runs. Asking at the
    ///wrong moment is how a group delete takes out shapes nobody chose.
    ///</summary>
    public abstract class ElementEdit : LayoutEdit
    {
        protected readonly StructureModel structure;

        ///<summary>What this was handed, or null when it was read back from a session.</summary>
        private readonly ElementModel? model;

        ///<summary>Where the element was the last time this ran. Minus one until it has.</summary>
        protected int at = -1;

        protected ElementEdit(StructureModel structure, ElementModel model)
        {
            this.structure = structure;
            this.model = model;
        }

        protected ElementEdit(StructureModel structure, int at)
        {
            this.structure = structure;
            this.model = null;
            this.at = at;
        }

        ///<summary>Finds the element this acts on and records where it is, for the reverse to find it again.</summary>
        protected ElementModel? Locate()
        {
            if (model is not null)
                at = structure.Elements.IndexOf(model);

            return Current();
        }

        ///<summary>The element at the place already recorded, without asking again where that is.</summary>
        protected ElementModel? Current()
        {
            if (at < 0 || at >= structure.Elements.Count)
                return null;

            return structure.Elements[at];
        }
    }

    ///<summary>
    ///Moves one element by a whole number of database units, in its own cell's coordinates.
    ///
    ///Its own coordinates, not the layout's. A cell placed at a quarter turn draws a shape sideways, so a
    ///drag to the right on screen is a drag *up* in the cell - and the number written into the file has to
    ///be the second one. Turning the first into the second is <see cref="CellContext.ToLocal"/>'s job; by
    ///the time it reaches here the translation is already the cell's own.
    ///</summary>
    public sealed class MoveElement : ElementEdit
    {
        private readonly int dx;
        private readonly int dy;

        public MoveElement(StructureModel structure, ElementModel model, int dx, int dy) : base(structure, model)
        {
            this.dx = dx;
            this.dy = dy;
        }

        ///<summary>The form read back from a session, which knows the place rather than the object.</summary>
        public MoveElement(StructureModel structure, int at, int dx, int dy) : base(structure, at)
        {
            this.dx = dx;
            this.dy = dy;
        }

        public override string Description
        {
            get { return "Move"; }
        }

        public override void Apply()
        {
            translate(Locate(), dx, dy);
        }

        public override void Revert()
        {
            translate(Current(), -dx, -dy);
        }

        public override EditRecord? Describe()
        {
            if (at < 0)
                return null;

            return new EditRecord
            {
                Kind = MoveKind,
                Structure = NameOf(structure),
                At = at,
                Dx = dx,
                Dy = dy
            };
        }

        ///<summary>
        ///Adds the offset to every coordinate pair, in place.
        ///
        ///A new Int4Data rather than a mutated one: the values are exposed as an array and something else
        ///may be holding it - and an edit that changed an array under a reader would be the kind of bug
        ///that shows up somewhere unrelated.
        ///</summary>
        private static void translate(ElementModel? model, int byX, int byY)
        {
            if (model?.Element.XY?.Data is not Int4Data xy)
                return;

            var moved = new int[xy.Values.Length];

            for (int i = 0; i + 1 < xy.Values.Length; i += 2)
            {
                moved[i] = xy.Values[i] + byX;
                moved[i + 1] = xy.Values[i + 1] + byY;
            }

            //An odd trailing value would be a malformed record; carried across rather than dropped, since
            //an edit is not the place to start deciding a file is wrong.
            if (xy.Values.Length % 2 != 0)
                moved[^1] = xy.Values[^1];

            model.Element.XY.Data = new Int4Data(moved);
        }
    }

    ///<summary>
    ///Moves one corner of an element, in its own cell's coordinates.
    ///
    ///**The closing point moves with the first.** A GDSII boundary repeats its opening corner at the end
    ///to close the ring, so dragging corner zero and leaving the copy behind opens the outline into a
    ///hook - which draws as a filled shape with a slit in it and reads back as a valid file, which is the
    ///worst combination. Whichever of the pair is dragged, both go.
    ///</summary>
    public sealed class MoveVertex : ElementEdit
    {
        private readonly int corner;
        private readonly int dx;
        private readonly int dy;

        public MoveVertex(StructureModel structure, ElementModel model, int corner, int dx, int dy)
            : base(structure, model)
        {
            this.corner = corner;
            this.dx = dx;
            this.dy = dy;
        }

        ///<summary>The form read back from a session, which knows the place rather than the object.</summary>
        public MoveVertex(StructureModel structure, int at, int corner, int dx, int dy) : base(structure, at)
        {
            this.corner = corner;
            this.dx = dx;
            this.dy = dy;
        }

        public override string Description
        {
            get { return "Move corner"; }
        }

        public override void Apply()
        {
            translate(Locate(), dx, dy);
        }

        public override void Revert()
        {
            translate(Current(), -dx, -dy);
        }

        public override EditRecord? Describe()
        {
            if (at < 0)
                return null;

            return new EditRecord
            {
                Kind = VertexKind,
                Structure = NameOf(structure),
                At = at,
                Corner = corner,
                Dx = dx,
                Dy = dy
            };
        }

        private void translate(ElementModel? model, int byX, int byY)
        {
            if (model?.Element.XY?.Data is not Int4Data xy)
                return;

            int count = xy.Values.Length / 2;

            if (corner < 0 || corner >= count)
                return;

            var moved = xy.Values.ToArray();

            //**Asked before the move, not after.** A ring that closes on itself has one corner written
            //twice; moving one copy first and then asking whether the two match answers no, because one
            //of them has just been moved - and the twin is left where it was, which is the hook this whole
            //branch exists to prevent.
            bool closed = count > 1
                && moved[0] == moved[(count - 1) * 2]
                && moved[1] == moved[((count - 1) * 2) + 1];

            move(moved, corner, byX, byY);

            if (closed)
            {
                if (corner == 0)
                    move(moved, count - 1, byX, byY);
                else if (corner == count - 1)
                    move(moved, 0, byX, byY);
            }

            model.Element.XY.Data = new Int4Data(moved);
        }

        private static void move(int[] values, int at, int byX, int byY)
        {
            values[at * 2] += byX;
            values[(at * 2) + 1] += byY;
        }
    }

    ///
    ///An element's coordinates becoming other coordinates, and coming back to exactly the ones they were.
    ///
    ///What a turn or a mirror is, once the arithmetic has been done: the shape is the same element on the
    ///same layer in the same place in its structure, with different numbers in its XY.
    ///
    ///**Both sets are kept.** A quarter turn could be undone by turning the other way, and about a
    ///whole-numbered point in a cell nobody has turned that is exact - but only then. About a cell placed at
    ///some angle nobody chose, or about a box whose middle falls between two units, turning back is a second
    ///rounding rather than the first one undone, and the file does not come back the file it was. Keeping
    ///what was there costs a copy of the coordinates and is exact for any shape in any cell.
    ///
    public sealed class ReshapeElement : ElementEdit
    {
        private readonly int[] before;
        private readonly int[] after;
        private readonly string description;

        ///<summary><paramref name="after"/> is in the structure's own coordinates, like everything an edit writes.</summary>
        public ReshapeElement(StructureModel structure, ElementModel model, int[] after, string description)
            : base(structure, model)
        {
            this.before = CoordinatesOf(model);
            this.after = after;
            this.description = description;
        }

        ///<summary>The form read back from a session, which carries both ends because neither is computable.</summary>
        public ReshapeElement(StructureModel structure, int at, int[] before, int[] after, string description)
            : base(structure, at)
        {
            this.before = before;
            this.after = after;
            this.description = description;
        }

        ///<summary>An element's coordinates as they stand, or nothing for an element that carries none.</summary>
        public static int[] CoordinatesOf(ElementModel model)
        {
            if (model.Element.XY?.Data is not Int4Data xy)
                return Array.Empty<int>();

            return xy.Values.ToArray();
        }

        public override string Description
        {
            get { return description; }
        }

        public override void Apply()
        {
            write(Locate(), after);
        }

        public override void Revert()
        {
            write(Current(), before);
        }

        public override EditRecord? Describe()
        {
            if (at < 0 || before.Length == 0 || after.Length == 0)
                return null;

            return new EditRecord
            {
                Kind = ReshapeKind,
                Structure = NameOf(structure),
                At = at,
                Label = description,
                Before = before,
                After = after
            };
        }

        ///<summary>
        ///A new Int4Data over a copy, for the same reason a move builds one: the values are handed out as an
        ///array, and this edit holds the only copy of what was there before.
        ///</summary>
        private static void write(ElementModel? model, int[] coordinates)
        {
            if (model?.Element.XY is null || coordinates.Length == 0)
                return;

            model.Element.XY.Data = new Int4Data(coordinates.ToArray());
        }
    }

    ///
    ///Puts a whole cell into the library, and takes it out again.
    ///
    ///**The same shape as adding an element, one level up.** Its records are built, parsed by the very
    ///constructor every structure in every file goes through, and put into the flat list with the model
    ///beside them - so a cell made here is held to the same rules as one that was read, and there is one
    ///place that knows what a well-formed structure looks like.
    ///
    ///It goes in front of ENDLIB, after everything already there. Where a cell sits in the file says nothing
    ///about it: a placement may name a cell defined later, which is why a library can be read at all.
    ///
    public sealed class AddStructure : LayoutEdit
    {
        private readonly GDS gds;
        private readonly List<Record> records;
        private readonly string named;

        ///<summary>The model those records were parsed into, kept so a redo puts back the same one.</summary>
        private StructureModel? made;

        ///<summary><paramref name="contents"/> are the element records that go in it, in order.</summary>
        public AddStructure(GDS gds, string name, IEnumerable<Record> contents)
        {
            this.gds = gds;
            this.named = name;

            records = new List<Record> { Hierarchy.Make(RecordType.BGNSTR, null), Hierarchy.Make(RecordType.STRNAME, new AsciiData(name)) };

            //
            //**The library's own timestamps rather than the clock.**
            //
            //A BGNSTR carries when the cell was last changed and last looked at, and there is no honest
            //answer to either for a cell that has just been invented. Copying the library's own says "as old
            //as the file it is in", which is at least true of the file - and it keeps this deterministic,
            //where reading a clock would make every test of it depend on when it ran.
            //
            if (gds.StreamFormat.BGNLIB?.Data is RecordData stamps)
                records[0] = new Record((short)RecordType.BGNSTR, stamps.Encode());

            records.AddRange(contents);
            records.Add(Hierarchy.Make(RecordType.ENDSTR, null));
        }

        ///<summary>The form read back from a session, which carries the whole cell because the file may not.</summary>
        public AddStructure(GDS gds, List<Record> records, string named)
        {
            this.gds = gds;
            this.records = records;
            this.named = named;
        }

        public override string Description
        {
            get { return "Make cell"; }
        }

        public override void Apply()
        {
            int start = gds.Records.IndexOf(gds.StreamFormat.ENDLIB);

            if (start < 0)
                throw new InvalidOperationException("This library has no ENDLIB, so there is nowhere to put a cell.");

            if (made is null)
            {
                int from = 0;

                made = new StructureModel(ref from, records);
            }

            gds.Records.InsertRange(start, records);
            gds.StreamFormat.Structures.Add(made);

            //Anything in it may be on a layer the table has never seen; the same guard as records going in.
            foreach (var element in made.Elements)
                ElementRecords.RegisterLayerOf(gds, element);
        }

        ///
        ///**Found by name in the library, not by the objects this edit holds.**
        ///
        ///An edit read back from a session was never the one that put the cell there - the file it is being
        ///undone against was parsed after the fact, so its BGNSTR and its model are different objects
        ///entirely. Reaching for the ones this instance happens to hold worked for a cell made in this
        ///session and silently did nothing for one restored from a stored stack, which is a cell that cannot
        ///be undone away.
        ///
        public override void Revert()
        {
            if (Hierarchy.Named(gds, named) is not StructureModel living)
                return;

            int start = gds.Records.IndexOf(living.BGNSTR);
            int end = gds.Records.IndexOf(living.ENDSTR);

            if (start < 0 || end < start)
                return;

            gds.Records.RemoveRange(start, end - start + 1);
            gds.StreamFormat.Structures.Remove(living);
        }

        public override EditRecord? Describe()
        {
            return new EditRecord
            {
                Kind = CellKind,
                Label = named,
                Records = ElementRecords.Write(records)
            };
        }
    }

    ///
    ///Renames a cell, and every placement that names it with it.
    ///
    ///**A name is not only on the cell.** A library refers to a cell by writing its name into an SNAME on
    ///every reference, so changing the STRNAME alone leaves every instance pointing at a cell that no longer
    ///exists - a file that still parses, still opens, and draws nothing where the instances were. The two
    ///halves are one edit for that reason.
    ///
    ///Resolved by name each time it runs rather than held as a model, because it works in both directions:
    ///applied it looks for the old name, reverted it looks for the new one, and an edit read back from a
    ///session has neither object to hold.
    ///
    public sealed class RenameStructure : LayoutEdit
    {
        private readonly GDS gds;
        private readonly string from;
        private readonly string to;

        public RenameStructure(GDS gds, string from, string to)
        {
            this.gds = gds;
            this.from = from;
            this.to = to;
        }

        public override string Description
        {
            get { return "Rename cell"; }
        }

        public override void Apply()
        {
            rename(from, to);
        }

        public override void Revert()
        {
            rename(to, from);
        }

        public override EditRecord? Describe()
        {
            return new EditRecord
            {
                Kind = RenameKind,
                Structure = from,
                Label = to
            };
        }

        private void rename(string was, string becomes)
        {
            if (Hierarchy.Named(gds, was) is not StructureModel structure)
                return;

            structure.STRNAME.Data = new AsciiData(becomes);

            //Every reference in the library, not only the ones in cells this happens to have looked at.
            foreach (var other in gds.StreamFormat.Structures)
            {
                foreach (var element in other.Elements)
                {
                    if (Hierarchy.PlacedBy(element) != was)
                        continue;

                    if (Hierarchy.SnameOf(element) is Record sname)
                        sname.Data = new AsciiData(becomes);
                }
            }
        }
    }

    ///
    ///Takes a whole cell out of the library, and puts it back.
    ///
    ///The mirror of <see cref="AddStructure"/>, and what the last instance of a cell takes with it when it is
    ///flattened away: this app draws a cell nothing references as a top of its own, so a flatten that left
    ///the cell behind would put every shape on screen twice - once inline where it was just written and once
    ///more as the orphan drawing itself.
    ///
    ///Put back where it was rather than on the end, so undoing is the file byte for byte and not merely the
    ///same cells in another order.
    ///
    public sealed class RemoveStructure : LayoutEdit
    {
        private readonly GDS gds;
        private readonly string named;

        private List<Record> removed;
        private StructureModel? taken;
        private int recordAt = -1;

        public RemoveStructure(GDS gds, string name)
        {
            this.gds = gds;
            this.named = name;
            this.removed = new List<Record>();
        }

        ///<summary>
        ///The form read back from a session, which carries the cell because the file no longer does - and
        ///where it was, because putting it back on the end is a file with the same cells and different bytes.
        ///</summary>
        public RemoveStructure(GDS gds, string name, List<Record> records, int recordAt)
        {
            this.gds = gds;
            this.named = name;
            this.removed = records;
            this.recordAt = recordAt;
        }

        public override string Description
        {
            get { return "Remove cell"; }
        }

        public override void Apply()
        {
            if (Hierarchy.Named(gds, named) is not StructureModel living)
                return;

            int start = gds.Records.IndexOf(living.BGNSTR);
            int end = gds.Records.IndexOf(living.ENDSTR);

            if (start < 0 || end < start)
                return;

            recordAt = start;
            removed = gds.Records.GetRange(start, end - start + 1);
            taken = living;

            gds.Records.RemoveRange(start, removed.Count);
            gds.StreamFormat.Structures.Remove(living);
        }

        public override void Revert()
        {
            if (removed.Count == 0)
                return;

            int at = recordAt;

            if (at < 0 || at > gds.Records.Count)
                at = gds.Records.IndexOf(gds.StreamFormat.ENDLIB);

            if (at < 0)
                return;

            //A restored edit has records and a place for them but no model, since the page it was parsed on
            //has gone.
            if (taken is null)
            {
                int from = 0;

                taken = new StructureModel(ref from, removed);
            }

            //
            //**Where it sits among the cells follows from where its records go**, rather than being a second
            //number to carry and keep in step. The cells whose own records start before this point are the
            //ones before it in the list, which is true however the edit got here - and a restored one that
            //guessed "on the end" put the first cell in the file back as the last, which is the same cells
            //and different bytes.
            //
            int position = 0;

            foreach (var other in gds.StreamFormat.Structures)
            {
                if (gds.Records.IndexOf(other.BGNSTR) < at)
                    position++;
            }

            gds.Records.InsertRange(at, removed);
            gds.StreamFormat.Structures.Insert(position, taken);

            foreach (var element in taken.Elements)
                ElementRecords.RegisterLayerOf(gds, element);
        }

        public override EditRecord? Describe()
        {
            if (removed.Count == 0)
                return null;

            return new EditRecord
            {
                Kind = CellGoneKind,
                Label = named,
                At = recordAt,
                Records = ElementRecords.Write(removed)
            };
        }
    }

    ///
    ///Moves an element onto another layer.
    ///
    ///Two numbers written into two records it already has, which is the whole of what a layer is - the pair
    ///on the element. Its geometry, its records, its place in the structure and everything else about it are
    ///left alone, so this is the one edit that changes what a shape is *for* without changing where it is.
    ///
    ///The second half of the pair is spelled differently for every element - DATATYPE on a boundary,
    ///TEXTTYPE on a label - and goes through the record the model already holds rather than through one
    ///found by name; see <see cref="ElementRecords.LayerOf"/>.
    ///
    public sealed class RelayerElement : ElementEdit
    {
        private readonly GDS gds;
        private readonly LayerKey moved;

        ///<summary>Where it came from, taken the first time this runs and kept for every undo after.</summary>
        private LayerKey? was;

        public RelayerElement(GDS gds, StructureModel structure, ElementModel model, LayerKey onto)
            : base(structure, model)
        {
            this.gds = gds;
            this.moved = onto;
        }

        ///<summary>The form read back from a session, which carries both ends because neither is on the file.</summary>
        public RelayerElement(GDS gds, StructureModel structure, int at, LayerKey was, LayerKey onto)
            : base(structure, at)
        {
            this.gds = gds;
            this.was = was;
            this.moved = onto;
        }

        public override string Description
        {
            get { return "Change layer"; }
        }

        public override void Apply()
        {
            if (Locate() is not ElementModel model)
                return;

            was ??= ElementRecords.LayerOf(model);

            ElementRecords.WriteLayer(model, moved);

            //A layer the table has never heard of is one the flattener skips, and a restored edit may name
            //one whose last shape has since gone; see the same guard where records go in.
            ElementRecords.Register(gds, moved);
        }

        public override void Revert()
        {
            if (Current() is ElementModel model && was is LayerKey back)
                ElementRecords.WriteLayer(model, back);
        }

        public override EditRecord? Describe()
        {
            if (at < 0 || was is not LayerKey back)
                return null;

            return new EditRecord
            {
                Kind = LayerKind,
                Structure = NameOf(structure),
                At = at,
                Label = "Change layer",
                Before = new int[] { back.Number, back.DataType },
                After = new int[] { moved.Number, moved.DataType }
            };
        }
    }

    ///
    ///Changing what a label says.
    ///
    ///**One record written, not an element rebuilt.** Every TEXT element carries a STRING, so unlike a width
    ///or an end style there is nothing to add - which means the label keeps its place in the file, its
    ///justification, its properties and its identity, and undo is exact for free because nothing was removed.
    ///
    ///The same shape as <see cref="RelayerElement"/>, and for the same reason: what a label said is not
    ///recoverable from the file once it has been changed, so it is taken at the moment the edit first runs
    ///and kept for every undo after.
    ///
    public sealed class RetextElement : ElementEdit
    {
        private readonly string says;

        ///<summary>What it said, taken the first time this runs and kept for every undo after.</summary>
        private string? said;

        public RetextElement(StructureModel structure, ElementModel model, string says)
            : base(structure, model)
        {
            this.says = AddElement.AsAscii(says);
        }

        ///<summary>The form read back from a session, which carries both ends because neither is on the file.</summary>
        public RetextElement(StructureModel structure, int at, string said, string says)
            : base(structure, at)
        {
            this.said = said;
            this.says = AddElement.AsAscii(says);
        }

        public override string Description
        {
            get { return "Retype label"; }
        }

        public override void Apply()
        {
            if (Locate() is not ElementModel model)
                return;

            said ??= TextOf(model) ?? "";

            write(model, says);
        }

        public override void Revert()
        {
            if (Current() is ElementModel model && said is string back)
                write(model, back);
        }

        ///<summary>What a label says, or null for anything that is not one.</summary>
        public static string? TextOf(ElementModel model)
        {
            if (model.Element is not TextModel text)
                return null;

            if (text.TextBody.STRING?.Data is AsciiData says)
                return says.Value;

            return "";
        }

        private static void write(ElementModel model, string says)
        {
            if (model.Element is TextModel text && text.TextBody.STRING is Record record)
                record.Data = new AsciiData(says);
        }

        public override EditRecord? Describe()
        {
            if (at < 0 || said is not string back)
                return null;

            return new EditRecord
            {
                Kind = TextKind,
                Structure = NameOf(structure),
                At = at,
                Label = "Retype label",
                Said = back,
                Says = says
            };
        }
    }

    ///<summary>
    ///Putting an element's records into a structure and taking them out again, which is the whole of what
    ///adding and deleting have in common.
    ///
    ///**The records are the only copy, and the model is parsed back from them.** Parsing is what every
    ///element in every file goes through, so a shape an undo puts back is held to the same rules as one that
    ///was read - and there is no second copy of the element to keep in step with the first. It is also what
    ///lets a *deleted* element come back at all after the page has been closed: the outline alone would
    ///throw away everything that made it a path, a label or a placement.
    ///</summary>
    internal static class ElementRecords
    {
        ///<summary>
        ///Takes element number <paramref name="at"/> out of the structure, and hands back its records.
        ///</summary>
        public static List<Record>? Take(GDS gds, StructureModel structure, int at)
        {
            if (at < 0 || at >= structure.Elements.Count)
                return null;

            var model = structure.Elements[at];

            //An element's records run unbroken from the one that opens it to its ENDEL, so the span is
            //found from its two ends rather than by counting the optional records in between.
            int start = gds.Records.IndexOf(model.Element.Opening);
            int end = gds.Records.IndexOf(model.ENDEL);

            if (start < 0 || end < start)
                return null;

            var taken = gds.Records.GetRange(start, end - start + 1);

            gds.Records.RemoveRange(start, taken.Count);
            structure.Elements.RemoveAt(at);

            return taken;
        }

        ///<summary>
        ///Puts an element's records in as number <paramref name="at"/>, ahead of whatever is there.
        ///
        ///<paramref name="model"/> is the node those records already had, when there is one - so an undo puts
        ///back the very element that was taken out rather than an equal one, and anything still holding it is
        ///holding something that is in the file again. Null is the restored case, where the page the element
        ///was parsed on has gone, and it is parsed afresh.
        ///</summary>
        public static bool Put(GDS gds, StructureModel structure, int at, List<Record> records, ElementModel? model = null)
        {
            if (at < 0 || at > structure.Elements.Count || records.Count == 0)
                return false;

            int start = recordIndex(gds, structure, at);

            if (start < 0)
                return false;

            if (model is null)
            {
                int from = 0;

                model = new ElementModel(ref from, records);
            }

            //The very records the model was parsed over, so the flat list and the tree share objects - which
            //is what lets a later edit change a coordinate once and have everything that reads the file see
            //it.
            gds.Records.InsertRange(start, records);
            structure.Elements.Insert(at, model);

            register(gds, model);

            return true;
        }

        ///
        ///**A layer the library has never heard of has to be introduced.**
        ///
        ///The flattener draws an element by looking its layer up in the table the parser built, and skips it
        ///outright when the lookup misses. So an element on a new layer is genuinely in the file - it
        ///survives a save and a reload - and invisible in the app that just put it there.
        ///
        ///Both ways in need this, which is why it lives here rather than in the edit that draws. Drawing on a
        ///layer the file does not use is the obvious case. The other one only shows up after a reload:
        ///deleting the last shape on a layer and saving leaves a file with no such layer in it, so the table
        ///built from that file has no row - and undoing the deletion afterwards put the shape back into the
        ///records and nowhere on the screen.
        ///
        ///Left in place on the way back out: an empty layer costs a row in the sidebar, where taking one away
        ///could remove a row somebody had just colored.
        ///
        private static void register(GDS gds, ElementModel model)
        {
            RegisterLayerOf(gds, model);
        }

        ///<summary>The same, for a caller that has an element rather than a key.</summary>
        public static void RegisterLayerOf(GDS gds, ElementModel model)
        {
            if (LayerOf(model) is not LayerKey key)
                return;

            Register(gds, key);
        }

        ///<summary>The same, for an edit that knows the layer without having an element to read it off.</summary>
        public static void Register(GDS gds, LayerKey key)
        {
            if (!gds.AdditionalInformation.Layers.ContainsKey(key))
                gds.AdditionalInformation.Layers[key] = new Layer(key, NewLayerColor);
        }

        ///
        ///The layer pair an element is on, or null for one that has no layer at all - a placement.
        ///
        ///Through DataTypeRecord rather than by looking for a DATATYPE record, because the format spells the
        ///second half of the pair four ways: DATATYPE on a boundary and a path, TEXTTYPE on a label, BOXTYPE
        ///on a box, NODETYPE on a node. That property is the one place that knows which of them the element
        ///in hand carries, and reading it any other way is how a label ends up on a layer nothing else uses.
        ///
        public static LayerKey? LayerOf(ElementModel model)
        {
            if (model.Element is not IHasLayer element)
                return null;

            short number = -1;

            if (element.LAYER?.Data is Int2Data layer)
                number = layer.Value;

            short dataType = LayerKey.UnknownDataType;

            if (element.DataTypeRecord?.Data is Int2Data elementDataType)
                dataType = elementDataType.Value;

            return new LayerKey(number, dataType);
        }

        ///<summary>Writes that pair back, into the two records the element already has.</summary>
        public static void WriteLayer(ElementModel model, LayerKey key)
        {
            if (model.Element is not IHasLayer element)
                return;

            if (element.LAYER is not null)
                element.LAYER.Data = new Int2Data(key.Number);

            if (element.DataTypeRecord is not null)
                element.DataTypeRecord.Data = new Int2Data(key.DataType);
        }

        ///<summary>
        ///What a layer met for the first time is colored. Overwritten by the palette as soon as the app
        ///assigns colors from how many layers a file has, so this only has to be valid.
        ///</summary>
        private const string NewLayerColor = "#808080";

        ///<summary>Where element number <paramref name="at"/> starts, or where a new one put there would go.</summary>
        private static int recordIndex(GDS gds, StructureModel structure, int at)
        {
            if (at >= 0 && at < structure.Elements.Count)
                return gds.Records.IndexOf(structure.Elements[at].Element.Opening);

            //Past the last element is in front of the structure's own ENDSTR, which is where its last
            //element already is.
            return gds.Records.IndexOf(structure.ENDSTR);
        }

        ///<summary>Records as they go into a session: a type and the bytes, which is all a record is.</summary>
        public static List<SavedRecord> Write(IReadOnlyList<Record> records)
        {
            var written = new List<SavedRecord>();

            foreach (var record in records)
            {
                written.Add(new SavedRecord
                {
                    Type = (short)record.Type,
                    Data = Convert.ToBase64String(record.Data?.Encode() ?? Array.Empty<byte>())
                });
            }

            return written;
        }

        ///<summary>The same, back again. Null when anything about them cannot be read.</summary>
        public static List<Record>? Read(List<SavedRecord>? written)
        {
            if (written is null || written.Count == 0)
                return null;

            var records = new List<Record>();

            try
            {
                foreach (var one in written)
                    records.Add(new Record(one.Type, Convert.FromBase64String(one.Data)));
            }
            catch (FormatException)
            {
                return null;
            }

            return records;
        }
    }

    ///<summary>
    ///Puts a new element into a structure - a boundary, or a label.
    ///
    ///Built by parsing the records it is made of rather than by assembling a model by hand. The parsing
    ///constructor is what every file goes through, so a shape drawn here is held to the same rules as one
    ///that was read - and there is only one place that knows what a well-formed element looks like.
    ///
    ///One class for both, because what an element *is* differs only in the records that go in: everything
    ///after that - finding the place, keeping the flat list and the tree in step, writing itself down,
    ///taking itself back out - is the same work, and the two halves silently disagreeing about any of it is
    ///the failure this exists to make impossible.
    ///</summary>
    public sealed class AddElement : LayoutEdit
    {
        private readonly GDS gds;
        private readonly StructureModel structure;
        private readonly List<Record> records;
        private readonly string description;

        ///<summary>Where it goes, or null to put it after everything the structure already holds.</summary>
        private readonly int? placeAt;

        ///<summary>The node the records were parsed into, kept so a redo puts back the same one.</summary>
        private ElementModel? made;

        ///
        ///What was added, once it has been. Null until <see cref="Apply"/> has run, and the same node
        ///afterwards however many times it is reverted and redone - which is the point of keeping it.
        ///
        ///For a caller that has to find the new shape again afterwards. A paste re-flattens the library, so
        ///every index into the drawn layout is a different shape than it was - and matching by coordinates
        ///would pick the wrong one of two identical shapes, which pasting on top of something is exactly how
        ///to produce. The model is the identity that survives the flatten: <see cref="Element.Source"/>
        ///carries it, so the elements drawn from these are findable by reference.
        ///
        public ElementModel? Made
        {
            get { return made; }
        }

        private int at = -1;

        ///<summary>
        ///<paramref name="outline"/> is in the structure's own coordinates, and is closed here if it is not
        ///closed already - a boundary whose last corner is not its first is one every reader complains
        ///about, and the caller of a drawing tool should not have to remember.
        ///</summary>
        public AddElement(GDS gds, StructureModel structure, LayerKey layer, IReadOnlyList<Element.Point> outline)
        {
            this.gds = gds;
            this.structure = structure;
            this.placeAt = null;
            this.description = "Draw";

            var closed = withoutRepeats(outline);

            if (closed.Count > 0 && (closed[0].X != closed[^1].X || closed[0].Y != closed[^1].Y))
                closed.Add(closed[0]);

            var coordinates = new int[closed.Count * 2];

            for (int i = 0; i < closed.Count; i++)
            {
                coordinates[i * 2] = closed[i].X;
                coordinates[(i * 2) + 1] = closed[i].Y;
            }

            records = new List<Record>
            {
                make(RecordType.BOUNDARY, null),
                make(RecordType.LAYER, new Int2Data(layer.Number)),
                make(RecordType.DATATYPE, new Int2Data(layer.DataType)),
                make(RecordType.XY, new Int4Data(coordinates)),
                make(RecordType.ENDEL, null)
            };
        }

        ///<summary>
        ///A path down a centerline in the structure's own coordinates.
        ///
        ///Its records come from <see cref="Paths.Records"/> rather than being assembled here, so that a path
        ///the drawing tool makes and one the width control rewrites are the same records in the same order.
        ///</summary>
        public AddElement(GDS gds, StructureModel structure, LayerKey layer, IReadOnlyList<Element.Point> along, int width, Paths.Ends ends)
        {
            this.gds = gds;
            this.structure = structure;
            this.placeAt = null;
            this.description = "Draw path";

            records = Paths.Records(layer, along, width, ends) ?? new List<Record>();
        }

        ///
        ///A label at one point in the structure's own coordinates.
        ///
        ///**Centered on the point, both ways.** The anchor is where somebody clicked, and a label that hung
        ///below and to the right of the click would be a label placed somewhere nobody pointed at. The
        ///format's own default for a missing PRESENTATION is left and top, which is why the record is
        ///written rather than left out.
        ///
        ///**No MAG.** This view draws every label at one readable size on purpose - see SvgWriter - so a size
        ///written here would be a number that changed nothing on screen and something elsewhere, which is
        ///the worst kind of control. A label with no magnification is the commonest thing in a real file.
        ///
        ///TEXTTYPE rather than DATATYPE: the format spells the second half of the layer pair differently for
        ///each element, and a label carrying a DATATYPE is one no reader will pair with its layer.
        ///
        public AddElement(GDS gds, StructureModel structure, LayerKey layer, Element.Point at, string text)
        {
            this.gds = gds;
            this.structure = structure;
            this.placeAt = null;
            this.description = "Label";

            var centered = new TextPresentation(HorizontalPresentation.Center, VerticalPresentation.Middle, 0);

            records = new List<Record>
            {
                make(RecordType.TEXT, null),
                make(RecordType.LAYER, new Int2Data(layer.Number)),
                make(RecordType.TEXTTYPE, new Int2Data(layer.DataType)),
                make(RecordType.PRESENTATION, new BitArrayData(centered.Encode())),
                make(RecordType.XY, new Int4Data(new int[] { at.X, at.Y })),
                make(RecordType.STRING, new AsciiData(AsAscii(text))),
                make(RecordType.ENDEL, null)
            };
        }

        ///
        ///A copy of an element that is already in the library, moved by a distance in its own cell's
        ///coordinates.
        ///
        ///**Its records, not its outline.** A path copies as a path, keeping its width and its ends; a label
        ///keeps what it says and how it is justified; anything carrying properties keeps them. Rebuilding a
        ///copy from the corners it happens to draw would quietly turn every one of those into a polygon - the
        ///same reason a deleted element is stored as its records rather than as a shape.
        ///
        ///Null for an element whose records are not in this library, which is the only way this can fail.
        ///
        public static AddElement? CopyOf(GDS gds, StructureModel structure, ElementModel model, int dx, int dy)
        {
            int start = gds.Records.IndexOf(model.Element.Opening);
            int end = gds.Records.IndexOf(model.ENDEL);

            if (start < 0 || end < start)
                return null;

            var copied = new List<Record>();

            foreach (var record in gds.Records.GetRange(start, end - start + 1))
            {
                //Through the bytes, which is the one way to copy a record without knowing what it holds.
                var made = new Record((short)record.Type, record.Data?.Encode() ?? Array.Empty<byte>());

                if (record.Type == RecordType.XY && made.Data is Int4Data xy)
                    made.Data = new Int4Data(shifted(xy.Values, dx, dy));

                copied.Add(made);
            }

            return new AddElement(gds, structure, copied, "Copy");
        }

        ///<summary>
        ///Every coordinate pair moved. An odd trailing value would be a malformed record; carried across
        ///rather than dropped, since a copy is not the place to start deciding a file is wrong.
        ///</summary>
        private static int[] shifted(int[] values, int dx, int dy)
        {
            var moved = new int[values.Length];

            for (int i = 0; i + 1 < values.Length; i += 2)
            {
                moved[i] = values[i] + dx;
                moved[i + 1] = values[i + 1] + dy;
            }

            if (values.Length % 2 != 0)
                moved[^1] = values[^1];

            return moved;
        }

        ///<summary>Records built here rather than read back, so they go after everything already in the cell.</summary>
        public AddElement(GDS gds, StructureModel structure, List<Record> records, string description)
        {
            this.gds = gds;
            this.structure = structure;
            this.records = records;
            this.placeAt = null;
            this.description = description;
        }

        ///<summary>
        ///The form read back from a session: the records themselves, and the place they went.
        ///
        ///The place is kept from the start rather than worked out on the first Apply, because a restored
        ///edit's first move is usually the *reverse* one - it comes back onto a stack that has already been
        ///applied, so the next thing that happens to it is an undo.
        ///
        ///What it was called comes back with it, so an undo button restored from a session says "Label"
        ///about a label rather than "Draw" about everything.
        ///</summary>
        public AddElement(GDS gds, StructureModel structure, int at, List<Record> records, string description = "Draw")
        {
            this.gds = gds;
            this.structure = structure;
            this.records = records;
            this.placeAt = at;
            this.at = at;
            this.description = description;
        }

        ///
        ///What of a string a GDSII file can hold.
        ///
        ///The format's strings are ASCII, and the encoder maps anything else to a question mark - so a label
        ///typed with a micron sign becomes one that reads "?" and nobody finds out until they open the file
        ///somewhere else. Dropped here instead, where what lands on screen is what went in the file.
        ///
        ///Capped at the length the format allows a record's payload to be, less the byte an odd-length
        ///string is padded with.
        ///
        public static string AsAscii(string text)
        {
            var kept = new System.Text.StringBuilder();

            foreach (char letter in text)
            {
                if (kept.Length >= LongestLabel)
                    break;

                if (letter >= ' ' && letter <= '~')
                    kept.Append(letter);
            }

            return kept.ToString();
        }

        ///<summary>A record's payload is at most 65530 bytes, but no reader anywhere expects a label near it.</summary>
        public const int LongestLabel = 512;

        public override string Description
        {
            get { return description; }
        }

        public override void Apply()
        {
            at = placeAt ?? structure.Elements.Count;

            if (!ElementRecords.Put(gds, structure, at, records, made))
                throw new InvalidOperationException("That structure is not in this library, so nothing can be added to it.");

            made = structure.Elements[at];
        }

        public override void Revert()
        {
            if (at < 0)
                return;

            ElementRecords.Take(gds, structure, at);
        }

        public override EditRecord? Describe()
        {
            if (at < 0)
                return null;

            return new EditRecord
            {
                Kind = InsertKind,
                Structure = NameOf(structure),
                At = at,
                Label = description,
                Records = ElementRecords.Write(records)
            };
        }

        ///
        ///The outline with any corner that repeats the one before it dropped, and the closing repeat with it.
        ///
        ///**A repeated corner is a side of no length.** Nothing in the format forbids one and most readers
        ///will take it, but it is a point on the outline with no direction - which is where an offset or a
        ///boolean over that shape stops behaving, some distance away from whatever drew it.
        ///
        ///They arrive without anybody asking. GDSII coordinates are whole numbers and a curve is not: an
        ///ellipse asked for at sixty-four sides on a radius of thirty units has several pairs of corners
        ///that round to the same pair of integers, and the closer to the poles the more of them there are.
        ///
        ///The seam is undone rather than left, so a ring handed over already closed does not keep a repeat
        ///there - the caller below closes it again, and closing a ring that is closed is what put two copies
        ///of the first corner in.
        ///
        private static List<Element.Point> withoutRepeats(IReadOnlyList<Element.Point> outline)
        {
            var kept = new List<Element.Point>();

            foreach (var point in outline)
            {
                if (kept.Count > 0 && kept[^1].Equals(point))
                    continue;

                kept.Add(point);
            }

            while (kept.Count > 1 && kept[0].Equals(kept[^1]))
                kept.RemoveAt(kept.Count - 1);

            return kept;
        }

        private static Record make(RecordType type, RecordData? data)
        {
            return new Record((short)type, data?.Encode() ?? Array.Empty<byte>());
        }
    }

    ///<summary>
    ///Takes one element out of its structure, and puts it back.
    ///
    ///The only edit that changes the *shape* of the library rather than a number in it, which makes it the
    ///one that has to touch both halves: the element's records come out of the flat list, and its node comes
    ///out of the structure. Miss either and the two disagree - the download would still carry a shape the
    ///view no longer draws, or the other way about.
    ///</summary>
    public sealed class DeleteElement : ElementEdit
    {
        private readonly GDS gds;

        ///<summary>What was taken out, so putting it back puts back all of it and not just its outline.</summary>
        private List<Record> removed;

        ///<summary>The node those records had, so an undo puts back the very element rather than an equal one.</summary>
        private ElementModel? taken;

        public DeleteElement(GDS gds, StructureModel structure, ElementModel model) : base(structure, model)
        {
            this.gds = gds;
            this.removed = new List<Record>();
        }

        ///<summary>
        ///The form read back from a session, which carries the records because the file no longer does -
        ///a deletion that has not been undone is a shape that is not in the saved file at all.
        ///</summary>
        public DeleteElement(GDS gds, StructureModel structure, int at, List<Record> records) : base(structure, at)
        {
            this.gds = gds;
            this.removed = records;
        }

        public override string Description
        {
            get { return "Delete"; }
        }

        public override void Apply()
        {
            if (Locate() is not ElementModel found)
                throw new InvalidOperationException("That element is not in this library, so it cannot be removed from it.");

            if (ElementRecords.Take(gds, structure, at) is not List<Record> span)
                throw new InvalidOperationException("That element's records are not in this library, so it cannot be removed from it.");

            removed = span;
            taken = found;
        }

        public override void Revert()
        {
            if (at < 0 || removed.Count == 0)
                return;

            ElementRecords.Put(gds, structure, at, removed, taken);
        }

        public override EditRecord? Describe()
        {
            if (at < 0 || removed.Count == 0)
                return null;

            return new EditRecord
            {
                Kind = RemoveKind,
                Structure = NameOf(structure),
                At = at,
                Records = ElementRecords.Write(removed)
            };
        }
    }

    ///<summary>
    ///Several edits as one, for a change made to more than one shape at a time.
    ///
    ///**Reverted backwards.** Undoing forwards happens to work for a handful of moves, where the edits do
    ///not touch each other - and stops working the moment one does. Deleting three shapes takes them out
    ///at three positions, and putting the first back before the third would put it back into a list the
    ///third has not yet returned to, at an index that means something different. Backwards, every edit
    ///finds the library exactly as it left it.
    ///</summary>
    public sealed class CompoundEdit : LayoutEdit
    {
        private readonly List<LayoutEdit> edits;
        private readonly string description;

        public CompoundEdit(string description, IEnumerable<LayoutEdit> edits)
        {
            this.description = description;
            this.edits = edits.ToList();
        }

        public override string Description
        {
            get
            {
                if (edits.Count == 1)
                    return description;

                return $"{description} {edits.Count} shapes";
            }
        }

        public int Count
        {
            get { return edits.Count; }
        }

        public override void Apply()
        {
            foreach (var edit in edits)
                edit.Apply();
        }

        public override void Revert()
        {
            for (int i = edits.Count - 1; i >= 0; i--)
                edits[i].Revert();
        }

        ///<summary>All of them or none: half a gesture on the stack would undo half of what one press made.</summary>
        public override EditRecord? Describe()
        {
            var parts = new List<EditRecord>();

            foreach (var edit in edits)
            {
                if (edit.Describe() is not EditRecord written)
                    return null;

                parts.Add(written);
            }

            return new EditRecord
            {
                Kind = GroupKind,
                Label = description,
                Parts = parts
            };
        }
    }

    ///<summary>
    ///What has been done and what has been taken back.
    ///
    ///Two stacks, which is the whole of it. Doing something new clears the redo stack, because a history
    ///that let you redo your way into a future that no longer follows from the present is worse than one
    ///that forgets.
    ///</summary>
    public sealed class EditHistory
    {
        private readonly List<LayoutEdit> done = new List<LayoutEdit>();
        private readonly List<LayoutEdit> undone = new List<LayoutEdit>();

        public bool CanUndo
        {
            get { return done.Count > 0; }
        }

        public bool CanRedo
        {
            get { return undone.Count > 0; }
        }

        ///<summary>What an undo would take back, for a button to name. Null when there is nothing.</summary>
        public string? NextUndo
        {
            get
            {
                if (done.Count == 0)
                    return null;

                return done[^1].Description;
            }
        }

        public string? NextRedo
        {
            get
            {
                if (undone.Count == 0)
                    return null;

                return undone[^1].Description;
            }
        }

        public int Count
        {
            get { return done.Count; }
        }

        public int RedoCount
        {
            get { return undone.Count; }
        }

        ///<summary>Applies an edit and remembers it.</summary>
        public void Do(LayoutEdit edit)
        {
            edit.Apply();

            done.Add(edit);
            undone.Clear();
        }

        public bool Undo()
        {
            if (done.Count == 0)
                return false;

            var edit = done[^1];

            done.RemoveAt(done.Count - 1);

            edit.Revert();
            undone.Add(edit);

            return true;
        }

        public bool Redo()
        {
            if (undone.Count == 0)
                return false;

            var edit = undone[^1];

            undone.RemoveAt(undone.Count - 1);

            edit.Apply();
            done.Add(edit);

            return true;
        }

        ///<summary>
        ///Forgets everything, for when a different file is opened - an undo stack whose edits point at a
        ///library that is no longer open would apply them to nothing, or worse, to something.
        ///</summary>
        public void Clear()
        {
            done.Clear();
            undone.Clear();
        }

        ///<summary>
        ///Both stacks written down, so they can be put back after the page has gone.
        ///
        ///**Kept from the top, dropped from the bottom.** What is nearest the present is what somebody is
        ///about to reach for, and it is also the only part that can be dropped from safely: an edit records
        ///where things sat *with everything below it applied*, so leaving a hole in the middle would have
        ///the edits above it undoing the wrong shapes. Anything that cannot be written down ends the stack
        ///there, and so does running out of the room a session has.
        ///</summary>
        public SavedEdits Describe()
        {
            return new SavedEdits
            {
                Done = describe(done),
                Undone = describe(undone)
            };
        }

        private static List<EditRecord> describe(List<LayoutEdit> stack)
        {
            var written = new List<EditRecord>();
            long payload = 0;

            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (written.Count >= MostSteps || payload > MostPayload)
                    break;

                if (stack[i].Describe() is not EditRecord one)
                    break;

                payload += weigh(one);
                written.Add(one);
            }

            //Back into stack order, so a reader does not have to know which end this wrote first.
            written.Reverse();

            return written;
        }

        ///<summary>
        ///Roughly how much room an edit takes, which is its records and nothing else - a move is three
        ///numbers however big the shape it moves.
        ///</summary>
        private static long weigh(EditRecord written)
        {
            long total = 0;

            foreach (var record in written.Records ?? new List<SavedRecord>())
                total += record.Data.Length;

            foreach (var part in written.Parts ?? new List<EditRecord>())
                total += weigh(part);

            return total;
        }

        ///<summary>
        ///How far back a stored history goes. Deep enough that nobody working normally reaches the end, and
        ///bounded because this is written to a browser's storage on every edit.
        ///</summary>
        public const int MostSteps = 100;

        ///<summary>
        ///How much deleted geometry a stored history will carry, in base64 characters. A deletion is the one
        ///edit that has to hold the shape itself, and a handful of large polygons would otherwise cost more
        ///room than the file they came out of.
        ///</summary>
        public const int MostPayload = 1_000_000;

        ///<summary>
        ///Puts a written-down stack back, against a library that has just been opened.
        ///
        ///Anything that cannot be rebuilt ends the stack there, for the same reason it does going the other
        ///way. Everything already here is dropped first: this is what a file arriving with a history of its
        ///own means, and merging two would be merging two different pasts.
        ///</summary>
        public void Restore(GDS gds, SavedEdits saved)
        {
            done.Clear();
            undone.Clear();

            done.AddRange(rebuild(gds, saved.Done));
            undone.AddRange(rebuild(gds, saved.Undone));
        }

        private static List<LayoutEdit> rebuild(GDS gds, List<EditRecord> written)
        {
            var stack = new List<LayoutEdit>();

            for (int i = written.Count - 1; i >= 0; i--)
            {
                if (LayoutEdit.Rebuild(written[i], gds) is not LayoutEdit edit)
                    break;

                stack.Add(edit);
            }

            stack.Reverse();

            return stack;
        }
    }
}
