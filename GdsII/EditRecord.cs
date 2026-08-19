namespace GdsII
{
    ///<summary>
    ///One edit written down, in a form that survives the file being closed and opened again.
    ///
    ///**Why an edit cannot simply be stored as it stands.** A <see cref="LayoutEdit"/> in memory points at
    ///the very model objects it changes, and those objects do not come back: reopening a file parses it into
    ///new ones. So an edit has to name what it acted on by something the reopened file has too, and the only
    ///such name is where the element sits in its structure.
    ///
    ///**The index is the one at that edit's own moment, not now.** Undo walks the stack backwards, and by
    ///the time an edit is reached the library is in exactly the state that edit left - so the index it
    ///recorded then is the index that is right then. Recording where an element sits *today* would be wrong
    ///for every edit below one that added or removed something.
    ///
    ///A flat union rather than a class per kind, because this is written to a session as JSON and read back
    ///by a version of the app that may not be the one that wrote it. One shape with unused fields is a shape
    ///that can be extended without a reader having to know what it is looking at first.
    ///</summary>
    public sealed class EditRecord
    {
        ///<summary>"move", "vertex", "insert", "remove" or "group".</summary>
        public string Kind { get; set; } = "";

        ///<summary>The structure the edit acted in, by name - which is what a reopened file also has.</summary>
        public string Structure { get; set; } = "";

        ///<summary>Where in that structure's elements, at the moment this edit ran.</summary>
        public int At { get; set; } = -1;

        ///<summary>Which corner, for a vertex move. Minus one otherwise.</summary>
        public int Corner { get; set; } = -1;

        public int Dx { get; set; }

        public int Dy { get; set; }

        ///<summary>
        ///The element's whole records, for an insert or a remove.
        ///
        ///The records rather than an outline and a layer, because what was deleted may have been a path, a
        ///label or a placement - each with records of its own that an outline would throw away. These are
        ///what the element is; parsing them back is what every file goes through anyway.
        ///</summary>
        public List<SavedRecord>? Records { get; set; }

        ///<summary>What this edit is called, for an undo button to name.</summary>
        public string Label { get; set; } = "";

        ///
        ///What the edit changed, as it was and as it became. Read according to <see cref="Kind"/>, like
        ///everything else here: the coordinates for a "reshape", the layer and its data type for a "layer".
        ///
        ///**Both ends, rather than one and the operation between them.** A quarter turn is exactly reversible
        ///about a whole-numbered point in a cell nobody has turned, and only then; the reverse of anything
        ///else is a second rounding rather than the first one undone. Two copies cost more room and are exact
        ///for any shape in any cell - and for a layer, where "the operation between them" is not a thing that
        ///can be written down at all, they are the only way.
        ///
        public int[]? Before { get; set; }

        public int[]? After { get; set; }

        ///<summary>The edits a group holds, in the order they were applied.</summary>
        public List<EditRecord>? Parts { get; set; }

        ///
        ///What a label said, and what it says now. Null for every kind that is not a retype.
        ///
        ///Their own fields rather than squeezed into <see cref="Before"/> and <see cref="After"/>: those are
        ///numbers, and ASCII stored as a run of integers is a thing somebody reading a session would have to
        ///be told about. This class is a union with unused fields by design, which is what makes adding two
        ///cheaper than overloading two.
        ///
        public string? Said { get; set; }

        public string? Says { get; set; }
    }

    ///<summary>
    ///One GDSII record as a type and its payload, which is all a record is.
    ///
    ///The payload stays as the bytes it was read as rather than as the decoded value: a record's meaning
    ///depends on its type, and re-encoding a decoded value is a chance for a real that was read to come back
    ///as a real that is merely close.
    ///</summary>
    public sealed class SavedRecord
    {
        public short Type { get; set; }

        ///<summary>Base64, because this ends up in JSON.</summary>
        public string Data { get; set; } = "";
    }

    ///<summary>
    ///An undo stack written down: what has been done, and what has been taken back.
    ///
    ///**Both stacks are kept from the top.** Anything that cannot be written down - or that will not fit in
    ///the room a session has - is dropped from the bottom, which costs the oldest steps and leaves the ones
    ///nearest the present intact. Dropping from the middle would be worse than dropping nothing: the edits
    ///above a hole record where things sat *with* the missing edit applied, so undoing past it would move
    ///the wrong shapes.
    ///</summary>
    public sealed class SavedEdits
    {
        public List<EditRecord> Done { get; set; } = new List<EditRecord>();

        public List<EditRecord> Undone { get; set; } = new List<EditRecord>();
    }
}
