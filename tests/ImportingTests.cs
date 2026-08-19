using GdsII;
using static GdsII.GDS.Record;

namespace GDSViewer.Tests;

///
///Bringing one library's cells into another.
///
///Two of the things this does are invisible when they go wrong, which is why they are pinned hardest: a
///renamed cell whose references were not rewritten draws the *host's* cell of that name, and a file whose
///units were not reconciled draws at the wrong size. Both produce a picture rather than an error.
///
public class ImportingTests
{
    ///<summary>A library of one cell holding one square, with whatever name and units are asked for.</summary>
    private static byte[] Library(string cell, double metersPerUnit = 1e-9, int side = 100)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("TESTLIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(metersPerUnit))),
            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(cell)),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(5)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, side, 0, side, side, 0, side, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),
            GdsTestData.Record(RecordType.ENDLIB));
    }

    ///
    ///A library of two cells, the second placing the first - so the reference is inside the import rather
    ///than pointing out of it.
    ///
    private static byte[] Hierarchical(string leaf, string top)
    {
        return GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("TESTLIB")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),

            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(leaf)),
            GdsTestData.Record(RecordType.BOUNDARY),
            GdsTestData.Record(RecordType.LAYER, GdsTestData.Int2(7)),
            GdsTestData.Record(RecordType.DATATYPE, GdsTestData.Int2(0)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0, 50, 0, 50, 50, 0, 50, 0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.BGNSTR, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.STRNAME, GdsTestData.Ascii(top)),
            GdsTestData.Record(RecordType.SREF),
            GdsTestData.Record(RecordType.SNAME, GdsTestData.Ascii(leaf)),
            GdsTestData.Record(RecordType.XY, GdsTestData.Int4(0, 0)),
            GdsTestData.Record(RecordType.ENDEL),
            GdsTestData.Record(RecordType.ENDSTR),

            GdsTestData.Record(RecordType.ENDLIB));
    }

    private static void Apply(Importing.Plan plan)
    {
        foreach (var edit in plan.Edits)
            edit.Apply();
    }

    [Fact]
    public void The_cells_arrive_and_the_top_is_named()
    {
        var host = new GDS(Library("HOST"));
        var guest = new GDS(Library("GUEST"));

        var plan = Importing.PlanFor(host, guest);

        Assert.NotNull(plan);
        Assert.Equal("GUEST", plan!.TopCell);
        Assert.Equal(1, plan.Cells);
        Assert.Empty(plan.Renamed);

        Apply(plan);

        //In the library, beside the one that was already there.
        Assert.Equal(new[] { "HOST", "GUEST" }, Hierarchy.Names(host));
    }

    ///<summary>Nothing is placed by planning alone - the caller places the top itself, where the pointer says.</summary>
    [Fact]
    public void Importing_places_nothing_by_itself()
    {
        var host = new GDS(Library("HOST"));

        var plan = Importing.PlanFor(host, new GDS(Library("GUEST")));

        Apply(plan!);

        Assert.Equal(0, Hierarchy.PlacementsOf(host, "GUEST"));
    }

    ///<summary>Two files each calling their cell "TOP" is ordinary, and the host's keeps the name.</summary>
    [Fact]
    public void A_name_already_taken_is_renamed()
    {
        var host = new GDS(Library("TOP"));
        var guest = new GDS(Library("TOP", side: 40));

        var plan = Importing.PlanFor(host, guest);

        Assert.Equal("TOP_1", plan!.TopCell);
        Assert.Equal(("TOP", "TOP_1"), plan.Renamed.Single());

        Apply(plan);

        Assert.Equal(new[] { "TOP", "TOP_1" }, Hierarchy.Names(host));

        //And the host's own cell still holds what it held - the import went beside it, not over it.
        var kept = GdsFlattener.Flatten(host, "TOP").Elements.Single();

        Assert.Equal(100, kept.Points.Max(point => point.X));
    }

    ///
    ///**The rename reaches the references, which is the whole point of renaming.**
    ///
    ///The guest places its own leaf. If the leaf is renamed on the way in and the SREF is not rewritten with
    ///it, that placement resolves against the host's cell of the old name - and the import silently draws
    ///the host's geometry instead of its own. Both libraries deliberately use the same two names, so a
    ///reference left alone finds something and the failure is a wrong picture rather than a missing cell.
    ///
    [Fact]
    public void A_reference_follows_the_cell_it_names_through_the_rename()
    {
        var host = new GDS(Hierarchical("LEAF", "TOP"));
        var guest = new GDS(Hierarchical("LEAF", "TOP"));

        var plan = Importing.PlanFor(host, guest);

        Apply(plan!);

        Assert.Equal("TOP_1", plan!.TopCell);

        //What the imported top places is the imported leaf, not the host's.
        var placed = Hierarchy.Places(Hierarchy.Named(host, "TOP_1")!);

        Assert.Equal("LEAF_1", placed.Single());
    }

    ///
    ///A guest in nanometers going into a host in microns is a thousand-fold difference, and unscaled it
    ///would draw a thousand times too small. The host here reads 1e-6 meters per unit against the guest's
    ///1e-9, so every coordinate is a thousandth of what it was.
    ///
    [Fact]
    public void Coordinates_are_scaled_when_the_files_disagree_about_units()
    {
        var host = new GDS(Library("HOST", metersPerUnit: 1e-6));
        var guest = new GDS(Library("GUEST", metersPerUnit: 1e-9, side: 100000));

        var plan = Importing.PlanFor(host, guest);

        Assert.Equal(0.001, plan!.Scale, 12);

        Apply(plan);

        var drawn = GdsFlattener.Flatten(host, "GUEST").Elements.Single();

        //100,000 nanometer units are 100 of the host's micron units - the same distance, in the host's terms.
        Assert.Equal(100, drawn.Points.Max(point => point.X));
    }

    ///<summary>The usual case, where both files are the sky130 default and nothing should move.</summary>
    [Fact]
    public void Matching_units_leave_every_coordinate_alone()
    {
        var host = new GDS(Library("HOST"));
        var guest = new GDS(Library("GUEST", side: 250));

        var plan = Importing.PlanFor(host, guest);

        Assert.Equal(1, plan!.Scale);

        Apply(plan);

        var drawn = GdsFlattener.Flatten(host, "GUEST").Elements.Single();

        Assert.Equal(250, drawn.Points.Max(point => point.X));
    }

    ///<summary>The import is copied, so editing the host afterwards cannot reach back into the opened file.</summary>
    [Fact]
    public void The_imported_records_are_the_hosts_own()
    {
        var host = new GDS(Library("HOST"));
        var guest = new GDS(Library("GUEST"));

        Apply(Importing.PlanFor(host, guest)!);

        foreach (var record in host.Records)
            Assert.DoesNotContain(record, guest.Records);
    }

    ///<summary>Undoing puts the library back to exactly the file it was, which is what makes this safe to try.</summary>
    [Fact]
    public void Undoing_the_import_leaves_the_library_as_it_was()
    {
        var host = new GDS(Library("HOST"));
        byte[] before = host.Serialize();

        var plan = Importing.PlanFor(host, new GDS(Hierarchical("LEAF", "TOP")))!;

        Apply(plan);

        Assert.Equal(3, Hierarchy.Names(host).Count);

        for (int at = plan.Edits.Count - 1; at >= 0; at--)
            plan.Edits[at].Revert();

        Assert.Equal(before, host.Serialize());
    }

    ///<summary>A library with no cells has nothing to bring, and says so rather than importing nothing.</summary>
    [Fact]
    public void A_library_with_no_cells_is_refused()
    {
        var host = new GDS(Library("HOST"));

        byte[] empty = GdsTestData.Concat(
            GdsTestData.Record(RecordType.HEADER, GdsTestData.Int2(600)),
            GdsTestData.Record(RecordType.BGNLIB, GdsTestData.Timestamps()),
            GdsTestData.Record(RecordType.LIBNAME, GdsTestData.Ascii("EMPTY")),
            GdsTestData.Record(RecordType.UNITS, GdsTestData.Concat(GdsTestData.Real8(0.001), GdsTestData.Real8(1e-9))),
            GdsTestData.Record(RecordType.ENDLIB));

        Assert.Null(Importing.PlanFor(host, new GDS(empty)));
    }
}
