using GdsII;

namespace GDSViewer.Tests;

///
///DXF that a real tool wrote, rather than DXF written by hand to exercise one branch.
///
///**Every other fixture in DxfTests is a string in the test file.** That is the right way to isolate a
///group code, and it has one blind spot that matters: a hand-written file only contains what the person
///writing it knew to put in. An exporter writes what it writes - a HEADER with variables nobody asked
///about, a LAYER table with colors and linetypes, old-style POLYLINE and VERTEX where an LWPOLYLINE would
///do, a SEQEND after every run - and any of that can be the thing a reader trips over.
///
///The file is `Mosfet.gds` written out as DXF by **KLayout 0.30.9**, so the original is right there to
///compare against: the same geometry exists in both, and a conversion that loses or moves anything shows
///up as a difference rather than as a number somebody has to judge.
///
///Regenerating it needs KLayout and this, which is the same shape as the OASIS conversion the suite
///already uses:
///
///    layout = pya.Layout()
///    layout.read("wwwroot/resources/GDS Files/Sky130 GDS/Mosfet.gds")
///    options = pya.SaveLayoutOptions()
///    options.format = "DXF"
///    layout.write("tests/GDSViewer.Tests/fixtures/klayout-written.dxf", options)
///
public class DxfRealFileTests
{
    private const string Original = "wwwroot/resources/GDS Files/Sky130 GDS/Mosfet.gds";
    private const string Written = "tests/GDSViewer.Tests/fixtures/klayout-written.dxf";

    private static string PathTo(string relative)
    {
        return Path.Combine(GdsTestData.RepositoryRoot, relative);
    }

    private static FlattenedLayout Drawn(GDS gds)
    {
        return GdsFlattener.Flatten(gds);
    }

    private static FlattenedLayout FromDxf()
    {
        //Through the bytes and back, the way every other reading here is checked: a library assembled
        //wrongly reads back perfectly from the model that made it.
        return Drawn(new GDS(DxfReader.Read(File.ReadAllBytes(PathTo(Written))).Serialize()));
    }

    private static FlattenedLayout FromGds()
    {
        return Drawn(new GDS(File.ReadAllBytes(PathTo(Original))));
    }


    ///<summary>It is read as a DXF at all, off its contents rather than its name.</summary>
    [Fact]
    public void A_drawing_a_real_tool_wrote_is_recognized()
    {
        byte[] bytes = File.ReadAllBytes(PathTo(Written));

        Assert.True(DxfReader.LooksLikeDxf(bytes));
    }

    ///
    ///**The layer numbers survive the round trip**, which is the whole reason the name is read as a number.
    ///
    ///KLayout names its DXF layers `L65D20` - the GDSII pair, spelled out - so a converted file carries its
    ///numbering in the only place DXF has to put one. Before the name was read, this file came back as
    ///layers 0 through 8 in declaration order and the correspondence with the original was gone.
    ///
    [Fact]
    public void The_layer_numbers_come_back_from_the_names()
    {
        var fromDxf = FromDxf().Elements.Select(element => element.Layer.Key).Distinct().OrderBy(key => key).ToList();
        var fromGds = FromGds().Elements.Select(element => element.Layer.Key).Distinct().OrderBy(key => key).ToList();

        Assert.Equal(fromGds, fromDxf);

        //And they are the file's own numbers rather than an index that happens to have the same count.
        Assert.Contains(new LayerKey(65, 20), fromDxf);
        Assert.Contains(new LayerKey(68, 20), fromDxf);
    }

    ///<summary>Every shape arrives, and no extra ones.</summary>
    [Fact]
    public void The_same_shapes_arrive()
    {
        Assert.Equal(FromGds().Elements.Count, FromDxf().Elements.Count);
    }

    ///
    ///And in the same places, to the database unit.
    ///
    ///The scale is the part that could be silently out by a thousand: KLayout writes microns and says
    ///nothing about `$INSUNITS`, which is exactly the case the reader assumes microns for. If that
    ///assumption were wrong this layout would open a thousand times too big and look perfectly fine.
    ///
    [Fact]
    public void The_geometry_lands_where_it_did()
    {
        var fromDxf = Bounds.Of(FromDxf().Elements.SelectMany(element => element.Points).ToList());
        var fromGds = Bounds.Of(FromGds().Elements.SelectMany(element => element.Points).ToList());

        Assert.Equal(fromGds.Left, fromDxf.Left);
        Assert.Equal(fromGds.Bottom, fromDxf.Bottom);
        Assert.Equal(fromGds.Width, fromDxf.Width);
        Assert.Equal(fromGds.Height, fromDxf.Height);
    }

    ///<summary>Shape by shape rather than only as a bounding box, which two different drawings can share.</summary>
    [Fact]
    public void Every_shape_lands_where_it_did()
    {
        var fromDxf = Outlines(FromDxf());
        var fromGds = Outlines(FromGds());

        Assert.Equal(fromGds, fromDxf);
    }

    ///
    ///Each shape as its layer and its points, in a form two readings of the same drawing can be compared in.
    ///
    ///**Wound the same way round, because the two files are not.** KLayout writes its DXF rings in the
    ///opposite direction to the GDSII it read them from - `-600,600 550,600 550,1100 -600,1100` one way and
    ///`-600,600 -600,1100 550,1100 550,600` the other. That is the same polygon: GDSII does not specify a
    ///winding, nothing downstream here reads one, and Clipper is run with a nonzero fill where a single
    ///ring's direction changes nothing at all. Comparing the sequences as written would have failed on a
    ///difference that is not one.
    ///
    private static List<string> Outlines(FlattenedLayout layout)
    {
        var outlines = new List<string>();

        foreach (var element in layout.Elements)
        {
            if (!string.IsNullOrEmpty(element.Text))
                continue;

            outlines.Add($"{element.Layer.Key}:{Ring(element.Points)}");
        }

        outlines.Sort(StringComparer.Ordinal);

        return outlines;
    }

    ///<summary>One ring, from its lowest point and going counterclockwise, whichever way it arrived.</summary>
    private static string Ring(IReadOnlyList<Element.Point> points)
    {
        var ring = new List<Element.Point>(points);

        //The closing point is the first one said twice, which is a repeat rather than a vertex.
        if (ring.Count > 1 && ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y)
            ring.RemoveAt(ring.Count - 1);

        if (ring.Count < 3)
            return string.Join(" ", ring.Select(point => $"{point.X},{point.Y}"));

        //The shoelace sum with its sign kept. Measure.AreaOf takes the absolute value, deliberately -
        //nothing downstream cares which way a ring is written, which is the same reason this normalizes.
        double twice = 0;

        for (int i = 0; i < ring.Count; i++)
        {
            var here = ring[i];
            var next = ring[(i + 1) % ring.Count];

            twice += ((double)here.X * next.Y) - ((double)next.X * here.Y);
        }

        if (twice < 0)
            ring.Reverse();

        //From whichever vertex is lowest and then leftmost, so the two readings start in the same place.
        int start = 0;

        for (int i = 1; i < ring.Count; i++)
        {
            if (ring[i].Y < ring[start].Y || (ring[i].Y == ring[start].Y && ring[i].X < ring[start].X))
                start = i;
        }

        var ordered = new List<string>();

        for (int i = 0; i < ring.Count; i++)
        {
            var point = ring[(start + i) % ring.Count];

            ordered.Add($"{point.X},{point.Y}");
        }

        return string.Join(" ", ordered);
    }

    ///
    ///The labels come across too, with their text and where they sit.
    ///
    ///KLayout writes them as TEXT entities, which is the one entity here that carries a string rather than
    ///geometry - and a reader can drop those without any of the shape comparisons above noticing.
    ///
    [Fact]
    public void The_labels_come_across()
    {
        var fromDxf = FromDxf().Elements.Where(element => !string.IsNullOrEmpty(element.Text))
            .Select(element => $"{element.Text}@{element.Points[0].X},{element.Points[0].Y}")
            .OrderBy(one => one, StringComparer.Ordinal).ToList();

        var fromGds = FromGds().Elements.Where(element => !string.IsNullOrEmpty(element.Text))
            .Select(element => $"{element.Text}@{element.Points[0].X},{element.Points[0].Y}")
            .OrderBy(one => one, StringComparer.Ordinal).ToList();

        Assert.NotEmpty(fromGds);
        Assert.Equal(fromGds, fromDxf);
    }

    ///
    ///And the old-style POLYLINE is what it writes, which is the shape of file this fixture exists for.
    ///
    ///A run of VERTEX entities after the POLYLINE, closed by a SEQEND - not the LWPOLYLINE that carries its
    ///own points and that a hand-written fixture reaches for first. Asserted rather than assumed, so that a
    ///regenerated fixture that switched form does not quietly stop testing this path.
    ///
    [Fact]
    public void The_fixture_is_the_old_style_of_run()
    {
        string text = File.ReadAllText(PathTo(Written));

        Assert.Contains("\nPOLYLINE\n", text.Replace("\r\n", "\n"));
        Assert.Contains("\nVERTEX\n", text.Replace("\r\n", "\n"));
        Assert.Contains("\nSEQEND\n", text.Replace("\r\n", "\n"));

        //And it says nothing about units, which is the case the micron assumption is for.
        Assert.DoesNotContain("$INSUNITS", text);
    }
}
