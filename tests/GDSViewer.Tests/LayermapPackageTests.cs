using System.IO.Compression;
using System.Text;
using GDSViewer.Models;

namespace GDSViewer.Tests;

///<summary>
///Covers LayermapPackage: a layermap and the pictures its texture column names, as one zip - read for a layermap an
///address names, with the zip work PdkPackage shares. Pure byte work, so it is tested here rather than in a browser.
///</summary>
public class LayermapPackageTests
{
    private static readonly byte[] Png = { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 9, 8, 7 };

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

    [Fact]
    public void A_layermap_and_its_pictures_come_back_out_of_their_zip()
    {
        string csv = "#layer,datatype,name\n65,20,met1,,,,,,,,copper.png\n";

        byte[] zip = zipOf(("Mosfet layers.csv", Encoding.UTF8.GetBytes(csv)), ("copper.png", Png), ("poly.jpg", Jpeg));

        Assert.True(LayermapPackage.IsZip(zip));

        var read = LayermapPackage.Read(zip);

        Assert.NotNull(read);
        Assert.Equal(csv, read.Value.Csv);
        Assert.Equal(2, read.Value.Files.Count);
        Assert.Equal(Png, read.Value.Files["copper.png"]);
        Assert.Equal(Jpeg, read.Value.Files["poly.jpg"]);
    }

    ///
    ///A zip made by hand often has everything a folder down. The CSV nearest the top is the layermap, and the files in
    ///its folder come back by their path from there - which is how its texture column names them - while anything
    ///outside that folder is not the layermap's.
    ///
    [Fact]
    public void Files_are_named_from_the_folder_the_CSV_is_in()
    {
        byte[] zip = zipOf(
            ("sky130/notes/readme.csv", Encoding.UTF8.GetBytes("deeper")),
            ("sky130/layers.csv", Encoding.UTF8.GetBytes("65,20,met1")),
            ("sky130/textures/copper.png", Png),
            ("elsewhere/poly.jpg", Jpeg));

        var read = LayermapPackage.Read(zip);

        Assert.NotNull(read);
        Assert.Equal("65,20,met1", read.Value.Csv);
        Assert.Equal(Png, read.Value.Files["textures/copper.png"]);

        //Every file in the CSV's folder and below it, and nothing from beside that folder under any name.
        Assert.Equal(new[] { "notes/readme.csv", "textures/copper.png" }, read.Value.Files.Keys.Order(StringComparer.Ordinal));
    }

    ///
    ///A zip is held to what its files unpack to, all of them together: a few kilobytes of zeros can say they hold
    ///gigabytes, and finding out by unpacking them would fill the browser's memory first.
    ///
    [Fact]
    public void A_zip_that_unpacks_past_the_limit_is_refused()
    {
        byte[] zip = zipOf(
            ("layers.csv", Encoding.UTF8.GetBytes("65,20,met1,,,,,,,,copper.png")),
            ("copper.png", new byte[LayermapPackage.LargestUnpacked]));

        Assert.True(zip.Length < 1024 * 1024);
        Assert.Null(LayermapPackage.Read(zip));
    }

    ///<summary>Some tools write their paths with backslashes; read the same as slashes.</summary>
    [Fact]
    public void A_zip_with_backslashes_in_its_paths_reads_the_same()
    {
        byte[] zip = zipOf(("maps\\layers.csv", Encoding.UTF8.GetBytes("65,20,met1")), ("maps\\copper.png", Png));

        var read = LayermapPackage.Read(zip);

        Assert.NotNull(read);
        Assert.Equal(Png, read.Value.Files["copper.png"]);
    }

    [Fact]
    public void A_zip_with_no_CSV_in_it_is_no_layermap()
    {
        Assert.Null(LayermapPackage.Read(zipOf(("copper.png", Png))));
    }

    [Fact]
    public void Bytes_that_are_not_a_zip_are_neither()
    {
        byte[] csv = Encoding.UTF8.GetBytes("65,20,met1\n");

        Assert.False(LayermapPackage.IsZip(csv));
        Assert.Null(LayermapPackage.Read(csv));
    }
}
