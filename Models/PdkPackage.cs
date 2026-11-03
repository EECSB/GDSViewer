using System.IO.Compression;
using System.Text;
using GdsII;

namespace GDSViewer.Models
{
    ///<summary>
    ///A whole PDK as one zip - its layermap, its rule deck and the pictures the layermap's texture column names - for the
    ///PDK manager's import and export, so a PDK arrives from somebody's folder, or goes from one browser to another, in
    ///one file rather than a table at a time. Asked for 2026-10-18.
    ///
    ///**A layermap's own zip with the deck beside the CSV** - see <see cref="LayermapPackage"/>. Unpacked, it is a folder
    ///named for the PDK holding `sky130 layers.csv`, `sky130 rules.drc` and the pictures, and each of them reads on its
    ///own in whatever reads that kind of file.
    ///
    ///**A zip made by hand is read by what its files are**, not by those names: the layermap is the CSV nearest the top,
    ///or a KLayout .lyp where there is no CSV, and the deck is the .drc nearest the top - or a .txt that reads as one.
    ///The same goes for the files chosen or dropped together, which is that folder picked whole; see <see cref="FromFiles"/>.
    ///
    ///**And what the PDK says about itself** (2026-11-01): a README - Markdown, HTML, a PDF or text - shown in the manager
    ///when the PDK is picked. A file named README is taken ahead of any other document, Markdown ahead of HTML, a PDF
    ///and text, and the one nearest the top of each; see <see cref="PdkReadme"/>.
    ///</summary>
    public static class PdkPackage
    {
        ///<summary>What a package held. Either half may be missing; a package with neither is no package.</summary>
        public sealed class Contents
        {
            ///<summary>The layermap's text - a CSV, or a KLayout .lyp - or null for none.</summary>
            public string? Layers { get; init; }

            ///<summary>The rule deck's text, or null for none.</summary>
            public string? Rules { get; init; }

            ///<summary>The files beside the layermap, by their path from its folder - the pictures its texture column names among them.</summary>
            public Dictionary<string, byte[]> Files { get; init; } = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            ///<summary>What the package calls the PDK - the folder its files sit in - or null where they sit at the top.</summary>
            public string? Name { get; init; }

            ///<summary>What the PDK says about itself, or null where the package has no README.</summary>
            public PdkReadme? Readme { get; init; }
        }

        ///<summary>A zip of a PDK: its layermap and its deck in a folder of its name, and each picture and its README beside them.</summary>
        public static byte[] Write(
            string name,
            string layers,
            string rules,
            IReadOnlyDictionary<string, byte[]> pictures,
            PdkReadme? readme = null)
        {
            string file = FileNameOf(name);
            using var memory = new MemoryStream();

            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                LayermapPackage.writeEntry(zip, $"{file}/{file} layers.csv", Encoding.UTF8.GetBytes(layers));
                LayermapPackage.writeEntry(zip, $"{file}/{file} rules.drc", Encoding.UTF8.GetBytes(rules));

                var written = new HashSet<string>(StringComparer.Ordinal);

                foreach (var picture in pictures)
                {
                    LayermapPackage.writeEntry(zip, $"{file}/{picture.Key}", picture.Value);
                    written.Add(picture.Key);
                }

                if (readme is not null)
                {
                    LayermapPackage.writeEntry(zip, $"{file}/{FileNameOf(readme.Name)}", readme.Bytes());

                    //Its pictures beside it, where it names them - unless a texture of the same name went in already.
                    foreach (var picture in readme.Pictures ?? new Dictionary<string, string>())
                    {
                        if (PdkReadme.IsPicturePath(picture.Key) && written.Add(picture.Key))
                            LayermapPackage.writeEntry(zip, $"{file}/{picture.Key}", Convert.FromBase64String(picture.Value));
                    }
                }
            }

            return memory.ToArray();
        }

        ///
        ///A PDK's name as a file's: what a file name cannot hold on any system put as underscores, and a name with nothing
        ///else in it "PDK". The same set everywhere, rather than the platform's own, which in the browser is only the slash.
        ///
        public static string FileNameOf(string name)
        {
            var file = new StringBuilder();

            foreach (char character in name.Trim())
            {
                if ("\\/:*?\"<>|".Contains(character) || char.IsControl(character))
                    file.Append('_');
                else
                    file.Append(character);
            }

            string written = file.ToString().Trim('.', ' ');

            if (written.Trim('_').Length == 0)
                return "PDK";

            return written;
        }

        ///<summary>The PDK in a zip, or null when it is not a zip, holds neither a layermap nor a deck, or unpacks to too much.</summary>
        public static Contents? Read(byte[] bytes)
        {
            var files = new List<(string Path, byte[] Bytes)>();

            if (!tryUnpack(bytes, files))
                return null;

            return gather(files);
        }

        ///
        ///The PDK in files chosen or dropped together: each zip among them unpacked where it lies, and every other file
        ///taken at the top - a CSV, a deck and the pictures the CSV names, picked out of their folder at once. Null when
        ///they hold neither a layermap nor a deck, or a zip among them could not be read.
        ///
        public static Contents? FromFiles(IReadOnlyList<(string Name, byte[] Bytes)> chosen)
        {
            var files = new List<(string Path, byte[] Bytes)>();

            foreach (var file in chosen)
            {
                if (LayermapPackage.IsZip(file.Bytes))
                {
                    if (!tryUnpack(file.Bytes, files))
                        return null;
                }
                else
                {
                    files.Add((Path.GetFileName(file.Name), file.Bytes));
                }
            }

            return gather(files);
        }

        ///<summary>A zip's files by their paths, with either slash read as one - false when it is no zip, or unpacks past <see cref="LayermapPackage.LargestUnpacked"/>.</summary>
        private static bool tryUnpack(byte[] bytes, List<(string Path, byte[] Bytes)> files)
        {
            try
            {
                using var memory = new MemoryStream(bytes);
                using var zip = new ZipArchive(memory, ZipArchiveMode.Read);

                long unpacked = 0;

                foreach (var entry in zip.Entries)
                {
                    string path = LayermapPackage.pathOf(entry);

                    if (path.EndsWith('/') || leftBehind(path))
                        continue;

                    unpacked += entry.Length;

                    if (unpacked > LayermapPackage.LargestUnpacked)
                        return false;

                    files.Add((path, LayermapPackage.readEntry(entry)));
                }

                return true;
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        ///
        ///What a zipping program leaves in a zip that is nobody's file: a Mac's resource forks, in a folder of their own and
        ///beside each file as "._name", and its folder settings. Taken as files, one of those forks would be a CSV.
        ///
        private static bool leftBehind(string path)
        {
            string name = path.Substring(path.LastIndexOf('/') + 1);

            return path.StartsWith("__MACOSX/", StringComparison.Ordinal) || name.StartsWith("._", StringComparison.Ordinal) || name == ".DS_Store";
        }

        ///<summary>The layermap, the deck and what sits beside the layermap, picked out of a package's files by what they are.</summary>
        private static Contents? gather(List<(string Path, byte[] Bytes)> files)
        {
            (string Path, string Text)? layers = null;
            (string Path, string Text)? deck = null;
            (string Path, string Text)? deckAsText = null;
            int layersRank = int.MaxValue;
            (string Path, byte[] Bytes)? readme = null;
            int readmeRank = int.MaxValue;

            foreach (var file in files)
            {
                string extension = System.IO.Path.GetExtension(file.Path).ToLowerInvariant();
                string text = "";

                //A README before anything else: README.txt is no layermap, which a .txt otherwise could be.
                if (readmeRankOf(file.Path) is int ranked)
                {
                    if (ranked < readmeRank)
                    {
                        readme = file;
                        readmeRank = ranked;
                    }

                    continue;
                }

                if (extension == ".drc" || extension == ".csv" || extension == ".lyp" || extension == ".txt")
                    text = textOf(file.Bytes);

                if (extension == ".drc")
                {
                    deck = nearer(deck, file.Path, text);

                    continue;
                }

                //A .txt is a deck when it reads as one, and a layermap otherwise - the two tabs' own imports take both.
                if (extension == ".txt" && DrcDeck.Parse(text).Count > 0)
                {
                    deckAsText = nearer(deckAsText, file.Path, text);

                    continue;
                }

                //A layermap ranked by its kind first and its depth second: a CSV anywhere ahead of a .lyp, and the one
                //nearest the top of each.
                int rank;

                if (extension == ".csv")
                    rank = 0;
                else if (extension == ".lyp")
                    rank = 1;
                else if (extension == ".txt")
                    rank = 2;
                else
                    continue;

                rank = rank * 1000 + depthOf(file.Path);

                if (rank < layersRank)
                {
                    layers = (file.Path, text);
                    layersRank = rank;
                }
            }

            var rules = deck ?? deckAsText;

            if (layers is null && rules is null)
                return null;

            var beside = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            if (layers is not null)
            {
                string folder = LayermapPackage.folderOf(layers.Value.Path);

                foreach (var file in files)
                {
                    if (file.Path == layers.Value.Path || file.Path == rules?.Path || file.Path == readme?.Path || !file.Path.StartsWith(folder, StringComparison.Ordinal))
                        continue;

                    beside[file.Path.Substring(folder.Length)] = file.Bytes;
                }
            }

            return new Contents
            {
                Layers = layers?.Text,
                Rules = rules?.Text,
                Files = beside,
                Name = topFolder(layers?.Path ?? rules!.Value.Path),
                Readme = readmeOf(readme, files)
            };
        }

        ///
        ///How good a README a file would make, lower better - or null for a file that is none: one named README ahead of
        ///any other document, Markdown ahead of HTML, a PDF and text, and the nearest the top of each. A .txt is a README
        ///only by its name, since a deck or a layermap can be one too.
        ///
        private static int? readmeRankOf(string path)
        {
            var kind = PdkReadme.KindOf(path);
            bool named = PdkReadme.IsNamedReadme(path);

            if (kind is null || (kind == PdkReadmeKind.Text && !named))
                return null;

            int rank = (int)kind.Value * 1000 + depthOf(path);

            if (!named)
                rank += 10000;

            return rank;
        }

        ///<summary>The README, with the pictures a Markdown one names found beside it in the package - see PdkReadme.Pictures.</summary>
        private static PdkReadme? readmeOf((string Path, byte[] Bytes)? file, List<(string Path, byte[] Bytes)> files)
        {
            if (file is null)
                return null;

            var readme = PdkReadme.From(System.IO.Path.GetFileName(file.Value.Path), file.Value.Bytes);

            if (readme is null || readme.Kind != PdkReadmeKind.Markdown)
                return readme;

            string folder = LayermapPackage.folderOf(file.Value.Path);
            var pictures = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (string path in MarkdownHtml.PicturesIn(readme.Text ?? ""))
            {
                if (!PdkReadme.IsPicturePath(path))
                    continue;

                string wanted = path.Replace('\\', '/');

                if (wanted.StartsWith("./", StringComparison.Ordinal))
                    wanted = wanted.Substring(2);

                wanted = folder + wanted;

                foreach (var each in files)
                {
                    if (each.Path == wanted)
                    {
                        pictures[path] = Convert.ToBase64String(each.Bytes);

                        break;
                    }
                }
            }

            if (pictures.Count > 0)
                readme.Pictures = pictures;

            return readme;
        }

        ///<summary>Whichever of two files is nearer the top of the package - the first found when they are as near.</summary>
        private static (string Path, string Text) nearer((string Path, string Text)? kept, string path, string text)
        {
            if (kept is null || depthOf(path) < depthOf(kept.Value.Path))
                return (path, text);

            return kept.Value;
        }

        private static int depthOf(string path)
        {
            return path.Count(character => character == '/');
        }

        ///
        ///The folder at the top of the package a file sits in - what Export names for the PDK, and what a folder zipped
        ///whole is called - or null for a file at the top itself.
        ///
        private static string? topFolder(string path)
        {
            int slash = path.IndexOf('/');

            if (slash <= 0)
                return null;

            return path.Substring(0, slash);
        }

        ///<summary>A file as text, without the byte-order mark a spreadsheet puts in front of the CSV it saves.</summary>
        private static string textOf(byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes).TrimStart('﻿');
        }
    }
}
