using System.IO.Compression;
using System.Text;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///Covers PdkPackage: a whole PDK - its layermap, its rule deck and the pictures its textures name - as one zip, written
///for the PDK manager's Export and read for its Import, and the same files chosen or dropped together. Pure byte work,
///so it is tested here rather than in a browser.
///</summary>
public class PdkPackageTests
{
    private static readonly byte[] Png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    private const string Csv = "#layer,datatype,name\n68,20,met1,,,,,,,,copper.png\n";
    private const string Deck = "layer met1 68/20\nrule met1.1 width met1 140 \"Met1 width\"\n";

    ///<summary>A zip of these entries, by their names as given, so a test can make one the way some other tool would.</summary>
    private static byte[] zipOf(params (string Name, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var stream = zip.CreateEntry(entry.Name).Open();

                stream.Write(entry.Bytes, 0, entry.Bytes.Length);
            }
        }

        return memory.ToArray();
    }

    private static byte[] bytesOf(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    [Fact]
    public void A_PDK_comes_back_out_of_the_zip_it_was_written_into()
    {
        byte[] zip = PdkPackage.Write("sky130 copy", Csv, Deck, new Dictionary<string, byte[]> { ["copper.png"] = Png });

        var read = PdkPackage.Read(zip);

        Assert.NotNull(read);
        Assert.Equal(Csv, read.Layers);
        Assert.Equal(Deck, read.Rules);
        Assert.Equal("sky130 copy", read.Name);
        Assert.Equal(Png, Assert.Single(read.Files).Value);
        Assert.Equal(Png, read.Files["copper.png"]);
    }

    ///
    ///Unpacked, Export's zip is a folder named for the PDK with the two files in it under names that say which PDK they
    ///are and what each one is - each readable on its own by whatever reads a layermap or a deck.
    ///
    [Fact]
    public void Written_it_is_a_folder_named_for_the_PDK_holding_its_layers_and_its_rules()
    {
        byte[] zip = PdkPackage.Write("sky130", Csv, Deck, new Dictionary<string, byte[]>());

        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);

        Assert.Equal(new[] { "sky130/sky130 layers.csv", "sky130/sky130 rules.drc" }, archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
    }

    ///
    ///A zip made by hand is read by what its files are, not by Export's names: the CSV is the layermap and the .drc the
    ///deck wherever they sit, the files in the CSV's folder are its own by their path from there, and the PDK is named
    ///for the folder at the top.
    ///
    [Fact]
    public void A_zip_made_by_hand_is_read_by_what_its_files_are()
    {
        byte[] zip = zipOf(
            ("mypdk/tables/map.csv", bytesOf("65,20,diff\n")),
            ("mypdk/tables/checks.drc", bytesOf("layer diff 65/20\n")),
            ("mypdk/tables/pictures/diff.png", Png),
            ("mypdk/README.md", bytesOf("notes")));

        var read = PdkPackage.Read(zip);

        Assert.NotNull(read);
        Assert.Equal("65,20,diff\n", read.Layers);
        Assert.Equal("layer diff 65/20\n", read.Rules);
        Assert.Equal("mypdk", read.Name);
        Assert.Equal(new[] { "pictures/diff.png" }, read.Files.Keys);
    }

    [Fact]
    public void Files_at_the_top_of_a_zip_name_no_PDK()
    {
        var read = PdkPackage.Read(zipOf(("layers.csv", bytesOf("65,20,diff\n")), ("rules.drc", bytesOf(Deck))));

        Assert.NotNull(read);
        Assert.Null(read.Name);
    }

    ///<summary>KLayout's own layer file, where the zip has no CSV - the layer table's Import takes either.</summary>
    [Fact]
    public void A_lyp_is_the_layermap_where_there_is_no_CSV()
    {
        var read = PdkPackage.Read(zipOf(("pdk/layers.lyp", bytesOf("<layer-properties/>")), ("pdk/rules.drc", bytesOf(Deck))));

        Assert.NotNull(read);
        Assert.Equal("<layer-properties/>", read.Layers);
    }

    [Fact]
    public void A_CSV_anywhere_comes_before_a_lyp_nearer_the_top()
    {
        var read = PdkPackage.Read(zipOf(("layers.lyp", bytesOf("<layer-properties/>")), ("deep/down/layers.csv", bytesOf("65,20,diff\n"))));

        Assert.NotNull(read);
        Assert.Equal("65,20,diff\n", read.Layers);
    }

    ///<summary>The nearest the top of two CSVs is the layermap, as a layermap's own zip reads.</summary>
    [Fact]
    public void Of_two_CSVs_the_one_nearer_the_top_is_the_layermap()
    {
        var read = PdkPackage.Read(zipOf(("pdk/old/layers.csv", bytesOf("1,0,old\n")), ("pdk/layers.csv", bytesOf("2,0,new\n"))));

        Assert.NotNull(read);
        Assert.Equal("2,0,new\n", read.Layers);
    }

    ///
    ///A .txt is either, as the two tabs' own imports take it: a deck when it reads as one - layer, derive and rule lines -
    ///and a layermap when it does not. A .drc is the deck ahead of a .txt that reads as one.
    ///
    [Fact]
    public void A_txt_is_a_deck_when_it_reads_as_one_and_a_layermap_when_it_does_not()
    {
        var read = PdkPackage.Read(zipOf(("pdk/deck.txt", bytesOf(Deck)), ("pdk/map.txt", bytesOf("65,20,diff\n"))));

        Assert.NotNull(read);
        Assert.Equal(Deck, read.Rules);
        Assert.Equal("65,20,diff\n", read.Layers);
    }

    [Fact]
    public void A_drc_is_the_deck_ahead_of_a_txt_that_reads_as_one()
    {
        string drc = "layer diff 65/20\n";

        var read = PdkPackage.Read(zipOf(("deck.txt", bytesOf(Deck)), ("pdk/deep/rules.drc", bytesOf(drc))));

        Assert.NotNull(read);
        Assert.Equal(drc, read.Rules);
    }

    ///<summary>A Mac's resource forks - a folder of their own, and "._" beside each file - would otherwise be a second CSV.</summary>
    [Fact]
    public void What_a_Mac_leaves_in_a_zip_is_not_taken_for_a_file()
    {
        byte[] zip = zipOf(
            ("__MACOSX/pdk/._layers.csv", new byte[] { 0, 5, 22, 7 }),
            ("pdk/._layers.csv", new byte[] { 0, 5, 22, 7 }),
            ("pdk/.DS_Store", new byte[] { 0, 0, 0, 1 }),
            ("pdk/layers.csv", bytesOf("65,20,diff\n")));

        var read = PdkPackage.Read(zip);

        Assert.NotNull(read);
        Assert.Equal("65,20,diff\n", read.Layers);
        Assert.Empty(read.Files);
    }

    ///<summary>The mark a spreadsheet saves in front of a CSV, which read as a character would have been part of the first field.</summary>
    [Fact]
    public void A_spreadsheets_byte_order_mark_is_not_part_of_the_layermap()
    {
        var read = PdkPackage.Read(zipOf(("layers.csv", new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytesOf("65,20,diff\n")).ToArray())));

        Assert.NotNull(read);
        Assert.Equal("65,20,diff\n", read.Layers);
    }

    ///<summary>The folder picked whole rather than zipped: a CSV, a deck and the pictures, chosen or dropped together.</summary>
    [Fact]
    public void Files_chosen_together_are_a_PDK_too()
    {
        var read = PdkPackage.FromFiles(new List<(string, byte[])>
        {
            ("sky130 layers.csv", bytesOf(Csv)),
            ("sky130 rules.drc", bytesOf(Deck)),
            ("copper.png", Png)
        });

        Assert.NotNull(read);
        Assert.Equal(Csv, read.Layers);
        Assert.Equal(Deck, read.Rules);
        Assert.Null(read.Name);
        Assert.Equal(Png, read.Files["copper.png"]);
    }

    [Fact]
    public void A_zip_among_the_files_chosen_is_unpacked_where_it_lies()
    {
        byte[] zip = PdkPackage.Write("mine", Csv, Deck, new Dictionary<string, byte[]>());

        var read = PdkPackage.FromFiles(new List<(string, byte[])> { ("mine.zip", zip) });

        Assert.NotNull(read);
        Assert.Equal(Deck, read.Rules);
        Assert.Equal("mine", read.Name);
    }

    [Fact]
    public void Neither_a_layermap_nor_a_deck_is_no_PDK()
    {
        Assert.Null(PdkPackage.Read(zipOf(("pdk/notes.md", bytesOf("hello")), ("pdk/picture.png", Png))));
        Assert.Null(PdkPackage.FromFiles(new List<(string, byte[])> { ("picture.png", Png) }));
    }

    [Fact]
    public void What_is_not_a_zip_is_no_PDK()
    {
        Assert.Null(PdkPackage.Read(bytesOf("layer met1 68/20")));
        Assert.Null(PdkPackage.FromFiles(new List<(string, byte[])> { ("broken.zip", new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 }) }));
    }

    #region The README

    ///<summary>A PDK's README goes out in its folder under its own name, and comes back as it went - a PDF as its bytes.</summary>
    [Fact]
    public void A_README_comes_back_out_of_the_zip_it_was_written_into()
    {
        var markdown = new PdkReadme { Name = "README.md", Text = "# sky130\n\nOpen source." };
        var read = PdkPackage.Read(PdkPackage.Write("sky130", Csv, Deck, new Dictionary<string, byte[]>(), markdown))!;

        Assert.Equal("README.md", read.Readme!.Name);
        Assert.Equal(markdown.Text, read.Readme.Text);
        Assert.Equal(PdkReadmeKind.Markdown, read.Readme.Kind);
        Assert.Empty(read.Files);

        byte[] pdf = bytesOf("%PDF-1.7 not really");
        var manual = PdkReadme.From("manual.pdf", pdf)!;
        var back = PdkPackage.Read(PdkPackage.Write("sky130", Csv, Deck, new Dictionary<string, byte[]>(), manual))!;

        Assert.Equal(PdkReadmeKind.Pdf, back.Readme!.Kind);
        Assert.Null(back.Readme.Text);
        Assert.Equal(pdf, back.Readme.Bytes());
    }

    ///
    ///**A file named README is taken ahead of any other document**, Markdown ahead of HTML, a PDF and text, the nearest
    ///the top of each - and README.txt is the README, never the layermap a .txt could otherwise be.
    ///
    [Fact]
    public void The_README_is_the_best_named_document_and_a_README_txt_is_no_layermap()
    {
        byte[] zip = zipOf(
            ("pdk/pdk layers.csv", bytesOf(Csv)),
            ("pdk/pdk rules.drc", bytesOf(Deck)),
            ("pdk/docs/notes.md", bytesOf("notes")),
            ("pdk/README.pdf", bytesOf("%PDF")),
            ("pdk/docs/README.md", bytesOf("# deeper")));

        Assert.Equal("README.md", PdkPackage.Read(zip)!.Readme!.Name);

        var onlyText = PdkPackage.FromFiles(new List<(string, byte[])> { ("README.txt", bytesOf("About it.")), ("rules.drc", bytesOf(Deck)) })!;

        Assert.Null(onlyText.Layers);
        Assert.Equal("About it.", onlyText.Readme!.Text);

        var plain = PdkPackage.Read(zipOf(("pdk/pdk layers.csv", bytesOf(Csv)), ("pdk/notes.txt", bytesOf("not a readme"))))!;

        Assert.Null(plain.Readme);
    }

    [Theory]
    [InlineData("README.md", PdkReadmeKind.Markdown)]
    [InlineData("guide.MARKDOWN", PdkReadmeKind.Markdown)]
    [InlineData("readme.htm", PdkReadmeKind.Html)]
    [InlineData("Manual.pdf", PdkReadmeKind.Pdf)]
    [InlineData("readme.txt", PdkReadmeKind.Text)]
    public void A_READMEs_kind_is_read_from_its_name(string name, PdkReadmeKind kind)
    {
        Assert.Equal(kind, PdkReadme.KindOf(name));
    }

    [Fact]
    public void A_picture_or_a_deck_is_no_README()
    {
        Assert.Null(PdkReadme.KindOf("copper.png"));
        Assert.Null(PdkReadme.From("rules.drc", bytesOf(Deck)));
    }

    ///
    ///**A Markdown README's pictures come with it** (2026-11-01): the ones it names, found from its own folder - a
    ///README down in docs/ naming images/stack.svg finds docs/images/stack.svg - and written back out beside it, where
    ///it names them. A picture it names that is not in the zip, or one outside its folder, is not taken.
    ///
    [Fact]
    public void A_READMEs_pictures_come_in_with_it_and_go_back_out_beside_it()
    {
        byte[] stack = bytesOf("<svg/>");
        byte[] zip = zipOf(
            ("pdk/pdk layers.csv", bytesOf(Csv)),
            ("pdk/docs/README.md", bytesOf("# pdk\n\n![The stack](images/stack.svg)\n![gone](missing.png)\n![out](../secret.png)")),
            ("pdk/docs/images/stack.svg", stack),
            ("pdk/secret.png", bytesOf("png")));

        var readme = PdkPackage.Read(zip)!.Readme!;

        Assert.Equal(new[] { "images/stack.svg" }, readme.Pictures!.Keys);
        Assert.Equal("data:image/svg+xml;base64," + Convert.ToBase64String(stack), readme.PictureUrl("images/stack.svg"));
        Assert.Null(readme.PictureUrl("missing.png"));

        var back = PdkPackage.Read(PdkPackage.Write("mine", Csv, Deck, new Dictionary<string, byte[]>(), readme))!;

        Assert.Equal(stack, Convert.FromBase64String(back.Readme!.Pictures!["images/stack.svg"]));
        Assert.Equal(stack, back.Files["images/stack.svg"]);

        //A copy is its own, pictures and all.
        var copy = readme.Copy();

        copy.Pictures!.Clear();

        Assert.Single(readme.Pictures);
    }

    #endregion

    ///<summary>A PDK is named by whoever made it, and a file is named by what every system it may land on allows.</summary>
    [Theory]
    [InlineData("sky130", "sky130")]
    [InlineData("gf180: 5V", "gf180_ 5V")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("  spaced  ", "spaced")]
    [InlineData("???", "PDK")]
    [InlineData("", "PDK")]
    public void A_name_a_file_cannot_hold_is_made_one_it_can(string name, string file)
    {
        Assert.Equal(file, PdkPackage.FileNameOf(name));
    }
}
