using System.Globalization;
using static GdsII.GDS.Record;
using static GdsII.GDS;

namespace GdsII;

///
///Bringing one library's cells into another, so a file can be placed inside a file.
///
///**A GDSII file already is a library of cells, so importing one is copying its cells across and then
///placing its top.** Nothing is flattened and nothing is merged: the incoming cells arrive whole, keeping
///their own hierarchy, and what lands in the open cell is one `SREF` naming the incoming top. Place it
///twice and it costs one more placement rather than a second copy of the geometry - which is the whole
///reason the format has cells, and the reason this is an import rather than a paste.
///
///Two things have to be reconciled on the way in, and both are silent when they are wrong:
///
///**Names.** Two libraries invented their names independently, so a clash is ordinary rather than
///exceptional - both files having a cell called `top` says nothing about the cells being related. A clash
///that went unnoticed would have placements in the incoming hierarchy resolving to the *host's* cell of
///that name, quietly drawing the wrong geometry. So every incoming name that is already taken is renamed,
///and every `SNAME` inside the incoming records is rewritten to match - the rename has to reach the
///references or it breaks the hierarchy it was protecting.
///
///**Units.** A coordinate means nothing without the file's `UNITS`: 1000 database units is a micron in one
///file and a nanometer in another. Copying the numbers across unchanged would draw the incoming layout at
///the wrong size, by a factor nothing on screen would explain. So coordinates are scaled by the ratio of
///the two files' meters-per-database-unit, which is what KLayout does on the same operation.
///
public static class Importing
{
    ///<summary>What an import would do, worked out before anything is changed.</summary>
    public sealed class Plan
    {
        ///<summary>The edits that add the incoming cells, in an order that leaves the library valid.</summary>
        public List<LayoutEdit> Edits { get; } = new List<LayoutEdit>();

        ///<summary>The name the incoming top cell ended up under, which is what there is to place.</summary>
        public string TopCell { get; set; } = "";

        ///<summary>Incoming name to the name it was given, for the ones that had to change.</summary>
        public List<(string From, string To)> Renamed { get; } = new List<(string From, string To)>();

        ///<summary>What incoming coordinates were multiplied by. 1 when the two files agree, which is usual.</summary>
        public double Scale { get; set; } = 1;

        ///<summary>How many cells came across, the renamed ones included.</summary>
        public int Cells { get; set; }
    }

    ///
    ///Works out the import without performing it.
    ///
    ///Separate from doing it because the caller has a dialog to fill in - how many cells, what got renamed,
    ///whether anything was scaled - and because the answer is worth having before the library is touched.
    ///
    ///Null when there is nothing to import: an empty library, or one whose cells cannot be read.
    ///
    public static Plan? PlanFor(GDS into, GDS incoming)
    {
        var cells = incoming.StreamFormat?.Structures;

        if (cells is null || cells.Count == 0)
            return null;

        var plan = new Plan { Scale = scaleBetween(into, incoming) };

        //
        //**Every name decided before any records are rewritten.**
        //
        //A reference can name a cell that appears later in the file, so rewriting as we go would leave the
        //forward ones pointing at the old name. Two passes: settle the whole mapping, then rewrite against
        //it.
        //
        var renamedTo = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(Hierarchy.Names(into), StringComparer.Ordinal);

        foreach (var cell in cells)
        {
            string was = Hierarchy.NameOf(cell);

            if (was.Length == 0)
                continue;

            string now = was;

            if (taken.Contains(now))
            {
                now = freeName(taken, was);

                plan.Renamed.Add((was, now));
            }

            taken.Add(now);
            renamedTo[was] = now;
        }

        foreach (var cell in cells)
        {
            string was = Hierarchy.NameOf(cell);

            if (was.Length == 0 || !renamedTo.TryGetValue(was, out string? now))
                continue;

            plan.Edits.Add(new AddStructure(into, now, contentsOf(cell, incoming, renamedTo, plan.Scale)));
            plan.Cells++;
        }

        plan.TopCell = topOf(incoming, renamedTo);

        if (plan.TopCell.Length == 0)
            return null;

        return plan;
    }

    ///
    ///The incoming library's top cell, under whatever name it ended up with.
    ///
    ///**The first top rather than all of them.** A library may have several cells nothing places, and all of
    ///them come across - but one of them is what the pointer carries, and offering a choice of tops before
    ///the file is even on screen asks a question about a file nobody has seen yet. The first is the file's
    ///own order, which is the order its writer chose. The rest are in the library and can be placed from the
    ///cell list afterwards.
    ///
    private static string topOf(GDS incoming, Dictionary<string, string> renamedTo)
    {
        foreach (var summary in Hierarchy.Summarize(incoming))
        {
            if (summary.IsTop && renamedTo.TryGetValue(summary.Name, out string? named))
                return named;
        }

        //Every cell places another, which a valid library cannot do - but a malformed one can, and the first
        //cell is a better answer than refusing the file.
        foreach (string named in renamedTo.Values)
            return named;

        return "";
    }

    ///
    ///The element records of one incoming cell, rewritten to belong to the host library.
    ///
    ///Copied rather than referenced: these records stay in the file they came from, which the caller may
    ///still have open, and an edit that shared them would change both libraries at once.
    ///
    private static List<Record> contentsOf(
        StructureModel cell,
        GDS incoming,
        Dictionary<string, string> renamedTo,
        double scale)
    {
        var records = new List<Record>();

        int start = incoming.Records.IndexOf(cell.STRNAME);
        int end = incoming.Records.IndexOf(cell.ENDSTR);

        if (start < 0 || end <= start)
            return records;

        for (int at = start + 1; at < end; at++)
        {
            Record record = incoming.Records[at];
            byte[] data = record.Data?.Encode() ?? Array.Empty<byte>();

            //A reference names a cell, and that cell may have been renamed on the way in.
            if (record.Type == RecordType.SNAME && record.Data is AsciiData sname)
            {
                string named = sname.Value;

                if (renamedTo.TryGetValue(named, out string? now))
                    data = new AsciiData(now).Encode();
            }

            records.Add(new Record((short)record.Type, data));
        }

        if (scale != 1)
            rescale(records, scale);

        return records;
    }

    ///
    ///Multiplies every coordinate and every width by the ratio between the two files' units.
    ///
    ///`XY` and `WIDTH` and nothing else: those are the two records that hold a length in database units. A
    ///`MAG` is a ratio and means the same in either file, an angle is degrees, and a layer number is a
    ///number - scaling any of those would be scaling something that has no unit.
    ///
    private static void rescale(List<Record> records, double scale)
    {
        for (int at = 0; at < records.Count; at++)
        {
            Record record = records[at];

            if (record.Type != RecordType.XY && record.Type != RecordType.WIDTH)
                continue;

            if (record.Data is not Int4Data numbers)
                continue;

            var scaled = new int[numbers.Values.Length];

            for (int each = 0; each < scaled.Length; each++)
                scaled[each] = (int)Math.Round(numbers.Values[each] * scale, MidpointRounding.AwayFromZero);

            records[at] = new Record((short)record.Type, new Int4Data(scaled).Encode());
        }
    }

    ///
    ///How much bigger an incoming database unit is than one of the host's.
    ///
    ///Off the second value of `UNITS`, which is meters per database unit and is the only one of the two that
    ///says anything absolute - the first is per *user* unit, which is a display convention rather than a
    ///size. A file missing the record, or carrying a nonsense zero, is taken to agree rather than being
    ///scaled by an invented number.
    ///
    private static double scaleBetween(GDS into, GDS incoming)
    {
        double host = metersPerUnit(into);
        double guest = metersPerUnit(incoming);

        if (host <= 0 || guest <= 0)
            return 1;

        double scale = guest / host;

        //
        //A hair either side of 1 is the same file's units read back through eight bytes of a format that is
        //not IEEE 754, not a real difference. Scaling by 1.0000000001 would move nothing and round some
        //coordinates by one unit on the way.
        //
        if (Math.Abs(scale - 1) < 1e-9)
            return 1;

        return scale;
    }

    private static double metersPerUnit(GDS gds)
    {
        if (gds.StreamFormat?.UNITS?.Data is not Real8Data units)
            return 0;

        if (units.Values.Length < 2)
            return 0;

        return units.Values[1];
    }

    ///<summary>"top" becomes "top_1", then _2, past whatever the host already has and whatever this import
    ///has already claimed.</summary>
    private static string freeName(HashSet<string> taken, string stem)
    {
        for (int at = 1; at < 10000; at++)
        {
            string tried = AddElement.AsAscii(FormattableString.Invariant($"{stem}_{at}"));

            if (tried.Length > 0 && !taken.Contains(tried))
                return tried;
        }

        return AddElement.AsAscii(FormattableString.Invariant($"{stem}_IMPORTED"));
    }
}
