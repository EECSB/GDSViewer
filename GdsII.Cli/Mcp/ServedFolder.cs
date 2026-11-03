namespace GdsII.Cli
{
    ///
    ///The one folder `gds mcp` reads and writes in, and the check every path a model names goes through.
    ///
    ///**The model is not trusted with the disk.** It is told which folder it works in and it names files in
    ///it, but what it names can come from anywhere - a label in a layout, a line in a document it was asked to
    ///follow - so a path is taken as a request and checked here, never handed to the file system as it came.
    ///A path that would leave the folder is refused before anything is opened: by `..`, by being absolute
    ///somewhere else, by a drive or a server of its own, or by a link inside the folder that points out of it.
    ///
    ///**A link that stays inside is followed**, since moving a file somewhere tidier and leaving a link behind
    ///is ordinary. Only where it leads is checked. What is not a link is not looked into: OneDrive marks every
    ///file it syncs as a reparse point, and a check that refused those would refuse most of a Windows user's
    ///Documents folder.
    ///
    public sealed class ServedFolder
    {
        public ServedFolder(string folder)
        {
            Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

            //A drive or the file system's root keeps its separator through the trim - "C:\", "/" - and
            //anything under it starts with exactly that.
            within = Root;

            if (!Path.EndsInDirectorySeparator(within))
                within += Path.DirectorySeparatorChar;
        }

        ///<summary>The folder, as a full path with no separator at its end - unless it is a drive's root.</summary>
        public string Root { get; }

        ///<summary>What every path under the folder starts with: the folder and one separator.</summary>
        private readonly string within;

        ///
        ///How paths are compared: without regard to case on Windows and macOS, whose file systems ignore it by
        ///default, and exactly elsewhere - so Chip.gds and chip.gds are one file where the disk says they are.
        ///
        private static StringComparison Comparison
        {
            get
            {
                if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                    return StringComparison.OrdinalIgnoreCase;

                return StringComparison.Ordinal;
            }
        }

        ///<summary>Whether two full paths name the same place, in the folder's own terms.</summary>
        public static bool Same(string? one, string? other)
        {
            if (one is null || other is null)
                return false;

            return string.Equals(Path.TrimEndingDirectorySeparator(one), Path.TrimEndingDirectorySeparator(other), Comparison);
        }

        ///<summary>Whether a full path is the folder or anything under it.</summary>
        public bool Holds(string full)
        {
            string trimmed = Path.TrimEndingDirectorySeparator(full);

            if (string.Equals(trimmed, Root, Comparison))
                return true;

            return trimmed.StartsWith(within, Comparison);
        }

        ///
        ///The full path a model's path names, or a refusal saying why it names nothing this folder may give it.
        ///
        ///Relative to the folder, whichever separator it uses; an absolute path is taken too, as long as it is
        ///in the folder, since a model told where the folder is will sometimes write it out in full.
        ///
        public string Resolve(string given)
        {
            if (given.Trim().Length == 0)
                throw new McpRefusal("Say which file, by its path in the folder, like chip.gds or cells/inv.gds.");

            string full = resolve(given);

            if (string.Equals(Path.TrimEndingDirectorySeparator(full), Root, Comparison))
                throw new McpRefusal($"{given} is the folder itself. Name a file in it, like chip.gds.");

            return full;
        }

        ///<summary>The same for a folder, which may be the folder itself - "." or nothing at all.</summary>
        public string ResolveFolder(string given)
        {
            if (given.Trim().Length == 0)
                return Root;

            return resolve(given);
        }

        private string resolve(string given)
        {
            string asked = given.Trim();

            if (asked.Contains('\0'))
                throw new McpRefusal($"\"{given}\" is not a path.");

            string full;

            try
            {
                full = Path.GetFullPath(asked, Root);
            }
            catch (Exception wrong) when (wrong is ArgumentException || wrong is NotSupportedException || wrong is PathTooLongException)
            {
                throw new McpRefusal($"\"{given}\" is not a path: {wrong.Message}");
            }

            if (!Holds(full))
                throw new McpRefusal($"{given} is outside the folder this works in, {Root}. Paths are relative to it, like chip.gds or cells/inv.gds.");

            if (OperatingSystem.IsWindows())
                refuseWhatWindowsReadsOtherwise(given, full);

            refuseLinksOut(given, full);

            return full;
        }

        ///
        ///Names Windows takes for something other than a file: a device, like CON or NUL - reading CON waits on
        ///the console for good - and a second stream of a file, after a colon.
        ///
        private void refuseWhatWindowsReadsOtherwise(string given, string full)
        {
            string inside = full.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (string part in inside.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Contains(':'))
                    throw new McpRefusal($"{given} names a stream inside a file, not a file.");

                string stem = part.Split('.')[0].TrimEnd(' ');

                if (Devices.Contains(stem))
                    throw new McpRefusal($"{given} names a device on Windows, not a file.");
            }
        }

        private static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        private static readonly HashSet<string> Devices = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        ///
        ///Follows every link on the way from the folder to the path, and refuses one that ends outside.
        ///
        ///Each step of the path that exists is asked whether it is a link; the ones that do not exist yet - a
        ///file about to be saved, a folder save_file will make - cannot be. A link to a link is followed to its
        ///end, which is the one that matters.
        ///
        private void refuseLinksOut(string given, string full)
        {
            string inside = full.Substring(Root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string at = Root;

            //The folder itself is not asked: it is what the person chose, link or not.
            foreach (string part in inside.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
            {
                at = Path.Combine(at, part);

                FileSystemInfo step;

                if (Directory.Exists(at))
                    step = new DirectoryInfo(at);
                else if (File.Exists(at))
                    step = new FileInfo(at);
                else
                    return;

                if (step.LinkTarget is null)
                    continue;

                FileSystemInfo? end;

                try
                {
                    end = step.ResolveLinkTarget(returnFinalTarget: true);
                }
                catch (IOException)
                {
                    throw new McpRefusal($"{given} goes through a link that leads nowhere.");
                }

                if (end is null || !Holds(Path.GetFullPath(end.FullName)))
                    throw new McpRefusal($"{given} goes through a link that leads out of the folder, which this does not follow.");
            }
        }

        ///<summary>A full path in the folder as a model should write it back: relative, with forward slashes.</summary>
        public string Relative(string full)
        {
            string inside = Path.GetRelativePath(Root, full);

            return inside.Replace(Path.DirectorySeparatorChar, '/');
        }

        #region Listing *********************************************************************

        ///<summary>What a file in the folder is to these tools, from its extension.</summary>
        public enum Kind
        {
            Other,
            Layout,
            Layermap,
            Deck
        }

        public static Kind KindOf(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension is ".gds" or ".gds2" or ".gdsii" or ".oas" or ".oasis" or ".dxf" or ".svg")
                return Kind.Layout;

            if (extension is ".csv" or ".lyp")
                return Kind.Layermap;

            if (extension == ".drc")
                return Kind.Deck;

            return Kind.Other;
        }

        ///<summary>How deep a listing goes below the folder it starts in.</summary>
        public const int Deepest = 8;

        ///
        ///Every layout, layermap and deck in a folder and the folders under it, as full paths in order.
        ///
        ///**Not into what nobody keeps layouts in**: folders whose names start with a dot - .git above all - and
        ///the build and package folders a project folder fills up with, bin, obj and node_modules. Nor through a
        ///link to a folder, inside or out: following one is how a listing goes round in a circle.
        ///
        public List<string> Files(string start)
        {
            var found = new List<string>();

            walk(start, 0, found);

            found.Sort(StringComparer.Ordinal);

            return found;
        }

        private static void walk(string folder, int depth, List<string> found)
        {
            IEnumerable<string> files;
            IEnumerable<string> folders;

            try
            {
                files = Directory.EnumerateFiles(folder).ToList();
                folders = Directory.EnumerateDirectories(folder).ToList();
            }
            catch (Exception unreadable) when (unreadable is IOException || unreadable is UnauthorizedAccessException)
            {
                return;
            }

            foreach (string file in files)
            {
                if (KindOf(file) != Kind.Other)
                    found.Add(file);
            }

            if (depth >= Deepest)
                return;

            foreach (string inner in folders)
            {
                string name = Path.GetFileName(inner);

                if (name.StartsWith('.') || Skipped.Contains(name))
                    continue;

                if (new DirectoryInfo(inner).LinkTarget is not null)
                    continue;

                walk(inner, depth + 1, found);
            }
        }

        private static readonly HashSet<string> Skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

        #endregion **************************************************************************
    }

    ///
    ///Something wrong with a call to one of `gds mcp`'s own tools, said the way the model should hear it.
    ///
    ///Thrown anywhere in reading the arguments or checking a path, and turned into an error answer rather
    ///than let out - the same contract the library's tools keep, so a refusal reads the same from either.
    ///
    public sealed class McpRefusal : Exception
    {
        public McpRefusal(string message) : base(message)
        {
        }
    }
}
