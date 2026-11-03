using System.IO.Compression;
using System.Text;

namespace GDSViewer.Models
{
    ///<summary>
    ///A layermap and the pictures its texture column names, as one zip - read for a layermap an address names, and the
    ///zip work a whole PDK's zip shares: see <see cref="PdkPackage"/>.
    ///
    ///**The pictures beside the CSV**, in the zip as in a folder: the texture column gives each by its name from the
    ///CSV's own folder - see <see cref="GdsII.LayerNames.IsTextureName"/> - so unpacked, the zip is a folder a CSV and
    ///its pictures sit in together, and the CSV in it reads the same in any tool that reads one.
    ///
    ///**Written by nothing here since 2026-10-19**: the layer panel's Export, which wrote one, went with its row to the
    ///PDK manager, whose Export writes a whole PDK - the same folder of a CSV and its pictures, with the deck beside them.
    ///</summary>
    public static class LayermapPackage
    {
        ///<summary>
        ///The largest a zip's files may be put together, in bytes, unpacked - so a small file that says it holds
        ///gigabytes is refused rather than filling the browser's memory finding out.
        ///</summary>
        public const long LargestUnpacked = 64 * 1024 * 1024;

        ///<summary>Whether these bytes are a zip, by the four every one of them starts with.</summary>
        public static bool IsZip(byte[] bytes)
        {
            return bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
        }

        internal static void writeEntry(ZipArchive zip, string name, byte[] bytes)
        {
            //Deflated, the pictures with the CSV: a PNG or a JPEG hardly shrinks, but one setting for everything keeps this short.
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);

            using var stream = entry.Open();

            stream.Write(bytes, 0, bytes.Length);
        }

        ///
        ///The layermap in a zip, and the files beside it - or null when it holds no CSV, or is not a zip at all.
        ///
        ///**The CSV nearest the top**: the shallowest, and the first of those, since a zip made by hand often has its
        ///files a folder down. Every other file in that folder or below it comes back by its path from there, which
        ///is how the CSV names them; anything outside it is not the layermap's.
        ///
        ///Paths are read with either slash, since some zips are made with backslashes, and a file's size is what the
        ///zip says it unpacks to - held to <see cref="LargestUnpacked"/> across all of them.
        ///
        public static (string Csv, Dictionary<string, byte[]> Files)? Read(byte[] bytes)
        {
            try
            {
                using var memory = new MemoryStream(bytes);
                using var zip = new ZipArchive(memory, ZipArchiveMode.Read);

                ZipArchiveEntry? csv = null;

                foreach (var entry in zip.Entries)
                {
                    if (!pathOf(entry).EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (csv is null || depthOf(pathOf(entry)) < depthOf(pathOf(csv)))
                        csv = entry;
                }

                if (csv is null)
                    return null;

                string folder = folderOf(pathOf(csv));
                var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

                long unpacked = csv.Length;

                foreach (var entry in zip.Entries)
                {
                    string path = pathOf(entry);

                    if (entry == csv || path.EndsWith('/') || !path.StartsWith(folder, StringComparison.Ordinal))
                        continue;

                    unpacked += entry.Length;

                    if (unpacked > LargestUnpacked)
                        return null;

                    files[path.Substring(folder.Length)] = readEntry(entry);
                }

                return (Encoding.UTF8.GetString(readEntry(csv)), files);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }

        internal static string pathOf(ZipArchiveEntry entry)
        {
            return entry.FullName.Replace('\\', '/');
        }

        internal static int depthOf(string path)
        {
            return path.Count(character => character == '/');
        }

        ///<summary>The folder a path is in, with its slash - or empty for the top of the zip.</summary>
        internal static string folderOf(string path)
        {
            int slash = path.LastIndexOf('/');

            if (slash < 0)
                return "";

            return path.Substring(0, slash + 1);
        }

        internal static byte[] readEntry(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var copy = new MemoryStream();

            stream.CopyTo(copy);

            return copy.ToArray();
        }
    }
}
