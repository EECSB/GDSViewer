using System.Globalization;
using System.Net;
using System.Text;

namespace GdsII
{
    public static partial class DrcReport
    {
        #region Reading *********************************************************************

        ///<summary>How far into a file <see cref="LooksLikeReport"/> looks for the root element.</summary>
        private const int LookedAt = 1024;

        ///<summary>How many references deep a cell may be placed before the chain is taken to be a loop.</summary>
        private const int MostReferenceDepth = 64;

        ///<summary>
        ///Whether these bytes start like a KLayout report database.
        ///
        ///**By what is in it rather than by what it is called.** The rules panel takes a deck through the same
        ///Import, and a deck is a text file named anything at all - so the two are told apart by the one thing a
        ///report always has near its start, which is its root element. KLayout writes it straight after the XML
        ///declaration.
        ///</summary>
        public static bool LooksLikeReport(ReadOnlySpan<byte> start)
        {
            string head = Encoding.UTF8.GetString(start[..Math.Min(start.Length, LookedAt)]);

            return head.Contains("<report-database", StringComparison.Ordinal);
        }

        ///<summary>A report, from the bytes of its file. See <see cref="Read(string, GDS, FlattenedLayout)"/>.</summary>
        public static DrcReportFile Read(byte[] bytes, GDS gds, FlattenedLayout layout)
        {
            int skip = 0;

            //A UTF-8 byte order mark, which an editor that has been through the file may have left.
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                skip = 3;

            return Read(Encoding.UTF8.GetString(bytes, skip, bytes.Length - skip), gds, layout);
        }

        ///<summary>
        ///What another tool found, read back from its report database and placed on this layout.
        ///
        ///The other half of <see cref="Write(DrcResult, DrcDeck, GDS, string, string)"/>, and the half that makes the
        ///format worth having: KLayout's own DRC, or anybody's script, writes one of these, and here it is laid over
        ///the layout it was made from.
        ///
        ///**Placed the way KLayout places it.** A report files each item under a cell and gives its values in
        ///that cell's own coordinates, in microns - so where it lands depends on where the cell is. The report
        ///says so itself where it can: KLayout's deep mode gives every cell one reference, the transform from
        ///that cell to the top, which is the instance its marker browser shows the item at. A cell the report
        ///gives no reference for is found in the layout instead, at the first place it is drawn, which is
        ///what the marker browser falls back on too. An item in a cell neither can place is listed and not
        ///drawn, and <see cref="DrcReportFile.Problems"/> says how many.
        ///
        ///**What each kind of value becomes**: a polygon its outline, a box its four corners, an edge a line,
        ///an edge pair the four-sided area between its two edges, a path its outline at its width, and a label
        ///a point at its anchor with its text kept as a note. A number or a piece of text is a note and nothing
        ///is drawn for it. A kind this does not know is counted and said rather than guessed at.
        ///
        ///**Its own small reader rather than the framework's XML**, for the reason <see cref="SvgReader"/>
        ///gives: everything in this library is compiled into the app, and System.Xml is a megabyte of it.
        ///A report needs less of XML than a drawing does - elements and their text, and no attributes at
        ///all - and it is read as it goes rather than built into a tree first, since a report from a whole
        ///chip runs to tens of megabytes.
        ///</summary>
        ///<exception cref="InvalidDataException">The text is not a report database.</exception>
        public static DrcReportFile Read(string xml, GDS gds, FlattenedLayout layout)
        {
            var reading = new Reading(gds, layout);

            reading.Scan(xml);

            return reading.Finish();
        }

        #endregion **************************************************************************



        #region The reading *****************************************************************

        ///<summary>One report on its way in: where the scan is, what has been declared, and what is placed so far.</summary>
        private sealed class Reading
        {
            private readonly DrcReportFile file = new DrcReportFile();
            private readonly FlattenedLayout layout;

            ///<summary>Microns to this layout's database units, which is the first step every value takes.</summary>
            private readonly Transform toUnits;

            //Where the scan is: the elements open around it, and the text since the last tag.
            private readonly List<string> open = new List<string>();
            private readonly StringBuilder text = new StringBuilder();
            private bool sawRoot;

            //The categories, as the report declares them, and the ones its items name that it never did.
            private readonly List<CategoryFrame> categoryFrames = new List<CategoryFrame>();
            private readonly List<CategoryFrame> openCategories = new List<CategoryFrame>();
            private readonly Dictionary<string, DrcReportCategory> categories = new Dictionary<string, DrcReportCategory>(StringComparer.Ordinal);
            private readonly List<string> undeclared = new List<string>();

            //The cells, by the name an item gives them - with the variant, when there is one.
            private readonly Dictionary<string, CellDeclaration> cells = new Dictionary<string, CellDeclaration>(StringComparer.Ordinal);
            private CellDeclaration? cell;
            private string referenceParent = "";
            private string referenceTrans = "";
            private bool cellsRead;

            //The item being read, and any that came before the cells they are in.
            private PendingItem? item;
            private readonly List<PendingItem> waiting = new List<PendingItem>();

            //Where each cell lands, worked out once.
            private readonly Dictionary<string, Transform?> placed = new Dictionary<string, Transform?>(StringComparer.Ordinal);
            private readonly HashSet<string> resolving = new HashSet<string>(StringComparer.Ordinal);
            private Dictionary<string, Transform>? inLayout;
            private Transform? top;
            private bool topMissing;

            //What could not be read or placed, counted rather than listed one by one.
            private readonly SortedDictionary<string, int> unreadKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            private readonly SortedSet<string> missingCells = new SortedSet<string>(StringComparer.Ordinal);
            private int outOfRange;

            public Reading(GDS gds, FlattenedLayout layout)
            {
                this.layout = layout;

                double perUnit = DxfWriter.MicronsPerUnit(gds);

                toUnits = new Transform(1 / perUnit, 0, 0, 1 / perUnit, 0, 0);
            }

            #region The scan

            ///<summary>Walks the text once, tag by tag, handing each element to what it means where it is.</summary>
            public void Scan(string xml)
            {
                int at = 0;

                while (at < xml.Length)
                {
                    int start = xml.IndexOf('<', at);

                    if (start < 0)
                        break;

                    text.Append(xml, at, start - at);

                    if (string.CompareOrdinal(xml, start, "<!--", 0, 4) == 0)
                    {
                        at = past(xml, "-->", start + 4);

                        continue;
                    }

                    if (string.CompareOrdinal(xml, start, "<![CDATA[", 0, 9) == 0)
                    {
                        int end = xml.IndexOf("]]>", start + 9, StringComparison.Ordinal);

                        if (end < 0)
                            end = xml.Length;

                        text.Append(WebUtility.HtmlEncode(xml[(start + 9)..end]));

                        at = Math.Min(xml.Length, end + 3);

                        continue;
                    }

                    if (string.CompareOrdinal(xml, start, "<?", 0, 2) == 0)
                    {
                        at = past(xml, "?>", start + 2);

                        continue;
                    }

                    if (string.CompareOrdinal(xml, start, "<!", 0, 2) == 0)
                    {
                        at = past(xml, ">", start + 2);

                        continue;
                    }

                    int close = xml.IndexOf('>', start + 1);

                    if (close < 0)
                        break;

                    if (xml[start + 1] == '/')
                        ended(xml[(start + 2)..close].Trim());
                    else
                    {
                        bool empty = xml[close - 1] == '/';
                        int nameEnd = start + 1;

                        while (nameEnd < close && !char.IsWhiteSpace(xml[nameEnd]) && xml[nameEnd] != '/')
                            nameEnd++;

                        string name = xml[(start + 1)..nameEnd];

                        started(name);

                        if (empty)
                            ended(name);
                    }

                    at = close + 1;
                }

                if (!sawRoot)
                    throw new InvalidDataException("This is not a KLayout report database: there is no <report-database> in it.");
            }

            private void started(string name)
            {
                if (open.Count == 0 && name == "report-database")
                    sawRoot = true;

                open.Add(name);
                text.Clear();

                string parent = parentOf(1);

                if (name == "category" && parent == "categories")
                {
                    var frame = new CategoryFrame();

                    if (openCategories.Count > 0)
                        frame.Path.AddRange(openCategories[^1].Path);

                    openCategories.Add(frame);
                    categoryFrames.Add(frame);

                    return;
                }

                if (name == "cell" && parent == "cells")
                {
                    cell = new CellDeclaration();

                    return;
                }

                if (name == "ref" && parent == "references")
                {
                    referenceParent = "";
                    referenceTrans = "";

                    return;
                }

                if (name == "item" && parent == "items")
                    item = new PendingItem();
            }

            private void ended(string name)
            {
                //Closing something that is not open is a file that does not nest, and is stepped over.
                int at = open.LastIndexOf(name);

                if (at < 0)
                {
                    text.Clear();

                    return;
                }

                open.RemoveRange(at + 1, open.Count - at - 1);

                string value = decoded(text.ToString());
                string parent = parentOf(1);

                text.Clear();

                open.RemoveAt(open.Count - 1);

                if (name == "cells" && parent == "report-database")
                {
                    cellsRead = true;

                    foreach (var pending in waiting)
                        place(pending);

                    waiting.Clear();

                    return;
                }

                if (parent == "report-database")
                {
                    header(name, value);

                    return;
                }

                if (parent == "category" && openCategories.Count > 0)
                {
                    var frame = openCategories[^1];

                    //Onto the path as soon as it is read, so a category declared inside this one - which comes
                    //after the name - starts from a path that has it.
                    if (name == "name" && !frame.Named)
                    {
                        frame.Name = value.Trim();
                        frame.Path.Add(frame.Name);
                        frame.Named = true;
                    }
                    else if (name == "description")
                        frame.Description = value.Trim();

                    return;
                }

                if (name == "category" && parent == "categories" && openCategories.Count > 0)
                {
                    declare(openCategories[^1]);

                    openCategories.RemoveAt(openCategories.Count - 1);

                    return;
                }

                if (cell is not null)
                {
                    cellPart(name, parent, value);

                    return;
                }

                if (item is not null)
                    itemPart(name, parent, value);
            }

            ///<summary>The name of the element this many levels out from the innermost one open, or empty.</summary>
            private string parentOf(int levels)
            {
                int at = open.Count - 1 - levels;

                if (at < 0)
                    return "";

                return open[at];
            }

            private void header(string name, string value)
            {
                if (name == "description")
                    file.Description = value.Trim();
                else if (name == "original-file")
                    file.OriginalFile = value.Trim();
                else if (name == "generator")
                    file.Generator = value.Trim();
                else if (name == "top-cell")
                    file.TopCell = value.Trim();
            }

            private void cellPart(string name, string parent, string value)
            {
                if (cell is not CellDeclaration declaring)
                    return;

                if (parent == "cell")
                {
                    if (name == "name")
                        declaring.Name = value.Trim();
                    else if (name == "variant")
                        declaring.Variant = value.Trim();

                    return;
                }

                if (parent == "ref")
                {
                    if (name == "parent")
                        referenceParent = value.Trim();
                    else if (name == "trans")
                        referenceTrans = value.Trim();

                    return;
                }

                if (name == "ref")
                {
                    declaring.References.Add((referenceParent, referenceTrans));

                    return;
                }

                if (name == "cell" && parent == "cells")
                {
                    //The first declaration of a name stands. A second is a file that contradicts itself, and the
                    //cell it names is still the same one.
                    cells.TryAdd(declaring.QualifiedName(), declaring);

                    cell = null;
                }
            }

            private void itemPart(string name, string parent, string value)
            {
                if (item is not PendingItem reading)
                    return;

                if (parent == "item")
                {
                    if (name == "tags")
                        reading.Tags = value;
                    else if (name == "category")
                        reading.Category = value;
                    else if (name == "cell")
                        reading.Cell = value.Trim();
                    else if (name == "visited")
                        reading.Visited = value.Trim() == "true";
                    else if (name == "comment")
                        reading.Comment = value.Trim();

                    return;
                }

                if (name == "value" && parent == "values")
                {
                    reading.Values.Add(value);

                    return;
                }

                if (name == "item" && parent == "items")
                {
                    //Cells are declared ahead of the items in every report KLayout writes, so this is the case
                    //that waits - but a file put together by hand may not keep to that.
                    if (cellsRead)
                        place(reading);
                    else
                        waiting.Add(reading);

                    item = null;
                }
            }

            ///<summary>Ends the scan: anything still waiting is placed, and what went wrong is said.</summary>
            public DrcReportFile Finish()
            {
                foreach (var pending in waiting)
                    place(pending);

                waiting.Clear();

                //Declared first, in the order the report declares them - a parent ahead of what is inside it -
                //and then any category an item named that the report never declared. Not the one that held what the
                //check did not get to, whose sentences are NotChecked and not faults.
                foreach (var frame in categoryFrames)
                {
                    if (categories.TryGetValue(keyOf(frame.Path), out var declared)
                        && !file.Categories.Contains(declared)
                        && !onlySentences(declared))
                        file.Categories.Add(declared);
                }

                foreach (string key in undeclared)
                {
                    var category = categories[key];

                    if (!file.Categories.Contains(category) && !onlySentences(category))
                        file.Categories.Add(category);
                }

                said();

                return file;
            }

            private void said()
            {
                if (unreadKinds.Count > 0)
                {
                    int count = unreadKinds.Values.Sum();
                    string kinds = string.Join(", ", unreadKinds.Select(kind => $"{kind.Key} ({kind.Value})"));

                    file.Problems.Add($"Left out {count} value(s) of a kind this does not read: {kinds}.");
                }

                int unplaced = file.Unplaced;

                if (unplaced > 0)
                {
                    string names = string.Join(", ", missingCells.Take(5));

                    if (missingCells.Count > 5)
                        names += $" and {missingCells.Count - 5} more";

                    file.Problems.Add($"{unplaced} item(s) are in cells this layout does not have ({names}), so they are listed and not drawn.");
                }

                if (topMissing)
                    file.Problems.Add($"The report is about the cell {file.TopCell}, which this layout does not have. Its markers are drawn where the report puts them, which may not be on this layout.");

                if (outOfRange > 0)
                    file.Problems.Add($"Left out {outOfRange} value(s) lying outside the coordinates this layout can hold.");
            }

            #endregion

            #region Categories

            private void declare(CategoryFrame frame)
            {
                if (!frame.Named)
                {
                    frame.Path.Add(frame.Name);
                    frame.Named = true;
                }

                var category = categoryFor(frame.Path);

                category.Description = frame.Description;
            }

            ///<summary>The category under this path, made when it is first named.</summary>
            private DrcReportCategory categoryFor(List<string> path)
            {
                string key = keyOf(path);

                if (categories.TryGetValue(key, out var known))
                    return known;

                var made = new DrcReportCategory { Name = string.Join(" / ", path) };

                categories[key] = made;

                //Kept in order, for the ones the report never declared. A declared one is put in its own place
                //by Finish whichever list it is in.
                undeclared.Add(key);

                return made;
            }

            private static string keyOf(List<string> path)
            {
                return string.Join('\u001f', path);
            }

            ///
            ///The names in an item's category, outermost first.
            ///
            ///**Quoted where a name needs it and bare where it does not**, joined by dots - `'m1.width'.'sub'`, or
            ///`plain`. A name with a dot or a space in it is quoted, since a bare one would read as two.
            ///
            private static List<string> categoryPath(string written)
            {
                var path = new List<string>();
                string trimmed = written.Trim();
                int at = 0;

                while (at < trimmed.Length)
                {
                    if (trimmed[at] == '\'' || trimmed[at] == '"')
                        path.Add(quoted(trimmed, ref at));
                    else
                    {
                        int dot = trimmed.IndexOf('.', at);

                        if (dot < 0)
                            dot = trimmed.Length;

                        path.Add(trimmed[at..dot].Trim());

                        at = dot;
                    }

                    while (at < trimmed.Length && trimmed[at] != '.')
                        at++;

                    at++;
                }

                if (path.Count == 0)
                    path.Add("(no category)");

                return path;
            }

            #endregion

            #region Placing an item

            private void place(PendingItem pending)
            {
                //A sentence about the check rather than a fault in the layout - see NotCheckedCategory.
                if (notesOf(pending) is List<string> notes)
                {
                    file.NotChecked.AddRange(notes);

                    return;
                }

                var category = categoryFor(categoryPath(pending.Category));
                var tags = tagsOf(pending.Tags);

                bool waived = tags.Contains("waived");

                string name = nameOf(pending.Cell);
                Transform? where = placementOf(pending.Cell);

                if (where is null)
                    missingCells.Add(name);

                var made = new DrcReportItem
                {
                    Category = category.Name,
                    Cell = name,
                    Waived = waived,
                    Visited = pending.Visited,
                    Comment = pending.Comment,
                    Tags = tags,
                    Placed = where is not null
                };

                foreach (string value in pending.Values)
                    read(value, where, made);

                category.Items++;

                if (waived)
                    category.Waived++;

                file.Items.Add(made);
            }

            ///
            ///What an item under <see cref="NotCheckedCategory"/> says, when text is all it holds - or null for any other
            ///item, which is placed as what it is.
            ///
            private static List<string>? notesOf(PendingItem pending)
            {
                var path = categoryPath(pending.Category);

                if (path.Count != 1 || path[0] != NotCheckedCategory || pending.Values.Count == 0)
                    return null;

                var notes = new List<string>();

                foreach (string value in pending.Values)
                {
                    string body = value.Trim();

                    if (!body.StartsWith("text:", StringComparison.Ordinal))
                        return null;

                    notes.Add(unquoted(body[5..]));
                }

                return notes;
            }

            ///<summary>Whether a category is the one the sentences in <see cref="DrcReportFile.NotChecked"/> came from, with no faults in it.</summary>
            private static bool onlySentences(DrcReportCategory category)
            {
                return category.Name == NotCheckedCategory && category.Items == 0;
            }

            ///<summary>The cell's own name, without the variant a report adds to tell its contexts apart.</summary>
            private string nameOf(string qualified)
            {
                if (cells.TryGetValue(qualified, out var declared))
                    return declared.Name;

                return qualified;
            }

            ///<summary>Microns in this cell to database units in the layout, or null where nothing can say.</summary>
            private Transform? placementOf(string qualified)
            {
                if (placed.TryGetValue(qualified, out var known))
                    return known;

                var found = resolve(qualified, 0);

                placed[qualified] = found;

                return found;
            }

            private Transform? resolve(string qualified, int depth)
            {
                if (placed.TryGetValue(qualified, out var known))
                    return known;

                if (depth > MostReferenceDepth || !resolving.Add(qualified))
                    return null;

                try
                {
                    string name = nameOf(qualified);

                    if (name == file.TopCell && file.TopCell.Length > 0)
                        return toUnits.Then(topInLayout());

                    //The report's own word first: the first reference that leads anywhere.
                    if (cells.TryGetValue(qualified, out var declared))
                    {
                        foreach (var reference in declared.References)
                        {
                            if (transformOf(reference.Trans) is not Transform step)
                                continue;

                            if (resolve(reference.Parent, depth + 1) is Transform above)
                                return step.Then(above);
                        }
                    }

                    //Then the layout's, for a cell the report says nothing about placing.
                    if (layoutPlacements().TryGetValue(name, out var drawn))
                        return toUnits.Then(drawn);

                    //A report with no top cell named is taken to be about the layout as it is drawn.
                    if (file.TopCell.Length == 0)
                        return toUnits;

                    return null;
                }
                finally
                {
                    resolving.Remove(qualified);
                }
            }

            ///<summary>Where the report's top cell is in the layout: nowhere moved for a cell drawn at the top.</summary>
            private Transform topInLayout()
            {
                if (top is Transform known)
                    return known;

                if (layoutPlacements().TryGetValue(file.TopCell, out var found))
                    top = found;
                else
                {
                    top = Transform.Identity;
                    topMissing = true;
                }

                return top.Value;
            }

            ///
            ///The first place each structure is drawn, worked out the first time anything asks.
            ///
            ///From the flattened layout, whose every element knows the chain of placements it was reached
            ///through - and every element of one visit shares that chain, so a visit is looked at once however
            ///many shapes are in it. A structure holding only placements appears in its children's chains.
            ///
            private Dictionary<string, Transform> layoutPlacements()
            {
                if (inLayout is not null)
                    return inLayout;

                inLayout = new Dictionary<string, Transform>(StringComparer.Ordinal);

                object? last = null;

                foreach (var element in layout.Elements)
                {
                    if (element.Source is not ElementSource source)
                        continue;

                    if (ReferenceEquals(source.Path, last))
                        continue;

                    last = source.Path;

                    foreach (var level in source.Ancestry)
                        inLayout.TryAdd(level.Structure, level.Placement);
                }

                return inLayout;
            }

            #endregion

            #region Values

            ///<summary>One value, drawn onto the item where it has a place and kept as a note where it does not.</summary>
            private void read(string value, Transform? where, DrcReportItem into)
            {
                string body = value.Trim();

                //A value can carry a tag of its own ahead of it - `[important] box: ...` - which says nothing about
                //where it is.
                if (body.StartsWith('['))
                {
                    int shut = body.IndexOf(']');

                    if (shut > 0)
                        body = body[(shut + 1)..].TrimStart();
                }

                int colon = body.IndexOf(':');

                if (colon < 0)
                {
                    unread("unnamed");

                    return;
                }

                string kind = body[..colon].Trim();
                string rest = body[(colon + 1)..].Trim();

                if (kind == "float")
                {
                    into.Notes.Add(rest);

                    return;
                }

                if (kind == "text")
                {
                    into.Notes.Add(unquoted(rest));

                    return;
                }

                if (kind == "label")
                {
                    label(rest, where, into);

                    return;
                }

                if (kind != "polygon" && kind != "box" && kind != "edge" && kind != "edge-pair" && kind != "path")
                {
                    unread(kind);

                    return;
                }

                //Nowhere to draw it, which the item already says by not being placed.
                if (where is not Transform placing)
                    return;

                List<Element.Point>? marker = null;

                if (kind == "polygon")
                    marker = polygon(rest, placing);
                else if (kind == "box")
                    marker = box(rest, placing);
                else if (kind == "edge")
                    marker = edge(rest, placing);
                else if (kind == "edge-pair")
                    marker = edgePair(rest, placing);
                else
                    marker = path(rest, placing);

                if (marker is null)
                {
                    unread(kind);

                    return;
                }

                if (marker.Count > 0)
                    into.Markers.Add(marker);
            }

            private void unread(string kind)
            {
                unreadKinds.TryGetValue(kind, out int seen);
                unreadKinds[kind] = seen + 1;
            }

            ///
            ///A polygon's outline: `(x,y;x,y;...)`, with each hole after a slash.
            ///
            ///**The holes are not drawn.** A marker is an outline drawn to say where to look, and the ring around
            ///a hole says the same place again from the inside - the outer one is the answer to "where".
            ///
            private List<Element.Point>? polygon(string written, Transform placing)
            {
                string? inside = bracketed(written);

                if (inside is null)
                    return null;

                int hole = inside.IndexOf('/');

                if (hole >= 0)
                    inside = inside[..hole];

                return pointsOf(inside, placing);
            }

            ///<summary>A box, `(left,bottom;right,top)`, as its four corners.</summary>
            private List<Element.Point>? box(string written, Transform placing)
            {
                string? inside = bracketed(written);

                if (inside is null)
                    return null;

                var corners = numbersOf(inside);

                if (corners is null || corners.Count != 2)
                    return null;

                var (left, bottom) = corners[0];
                var (right, top) = corners[1];

                return pointsAt(new List<(double, double)> { (left, bottom), (left, top), (right, top), (right, bottom) }, placing);
            }

            ///<summary>An edge, `(x,y;x,y)`: a line, or a point when it begins where it ends.</summary>
            private List<Element.Point>? edge(string written, Transform placing)
            {
                string? inside = bracketed(written);

                if (inside is null)
                    return null;

                var ends = pointsOf(inside, placing);

                if (ends is null)
                    return null;

                //Out of range, which pointsAt has already counted.
                if (ends.Count == 0)
                    return ends;

                if (ends.Count != 2)
                    return null;

                if (ends[0].X == ends[1].X && ends[0].Y == ends[1].Y)
                    ends.RemoveAt(1);

                return ends;
            }

            ///
            ///An edge pair, `(edge)/(edge)` - or `(edge)|(edge)` for one KLayout knows to be symmetric - as the
            ///area between its two edges.
            ///
            ///**What a width or a space check answers in**, which makes it the commonest value in a real report:
            ///the two edges that are too close together. The four ends taken in turn make the area between them,
            ///unless the second edge runs the same way as the first, when that order crosses itself in the middle
            ///and the second edge is taken the other way round.
            ///
            private List<Element.Point>? edgePair(string written, Transform placing)
            {
                string trimmed = written.Trim();
                int shut = trimmed.IndexOf(')');

                if (shut < 0 || shut + 1 >= trimmed.Length || (trimmed[shut + 1] != '/' && trimmed[shut + 1] != '|'))
                    return null;

                var first = edge(trimmed[..(shut + 1)], placing);
                var second = edge(trimmed[(shut + 2)..], placing);

                if (first is null || second is null)
                    return null;

                if (first.Count == 0 || second.Count == 0)
                    return new List<Element.Point>();

                var a1 = first[0];
                var a2 = first[^1];
                var b1 = second[0];
                var b2 = second[^1];

                if (crosses(a2, b1, b2, a1))
                    (b1, b2) = (b2, b1);

                var area = new List<Element.Point> { a1, a2, b1, b2 };

                //Two points that coincide, which an edge pair of two touching edges has, are one corner.
                for (int i = area.Count - 1; i > 0; i--)
                {
                    if (area[i].X == area[i - 1].X && area[i].Y == area[i - 1].Y)
                        area.RemoveAt(i);
                }

                return area;
            }

            ///
            ///A path, `(x,y;...) w=0.5 bx=0 ex=0 r=false`, as the outline it covers.
            ///
            ///Built after it is placed rather than before, with the width scaled the way the placement scales
            ///everything else - which is what <see cref="PathOutline"/> asks of its callers.
            ///
            private List<Element.Point>? path(string written, Transform placing)
            {
                string trimmed = written.Trim();
                int shut = trimmed.IndexOf(')');

                if (!trimmed.StartsWith('(') || shut < 0)
                    return null;

                var centerline = pointsOf(trimmed[1..shut], placing);

                if (centerline is null)
                    return null;

                double width = 0;
                double begin = 0;
                double end = 0;
                bool round = false;

                foreach (string setting in trimmed[(shut + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int equals = setting.IndexOf('=');

                    if (equals < 0)
                        continue;

                    string key = setting[..equals];
                    string amount = setting[(equals + 1)..];

                    if (key == "r")
                        round = amount == "true";
                    else if (key == "w")
                        number(amount, out width);
                    else if (key == "bx")
                        number(amount, out begin);
                    else if (key == "ex")
                        number(amount, out end);
                }

                double scale = placing.Scale;

                int pathType = 0;

                if (round)
                    pathType = 1;
                else if (begin != 0 || end != 0)
                    pathType = 4;

                return PathOutline.Build(
                    centerline,
                    (int)Math.Round(Math.Abs(width) * scale),
                    pathType,
                    (int)Math.Round(begin * scale),
                    (int)Math.Round(end * scale));
            }

            ///<summary>A label, `('text',r0 x,y)`: a point where it is anchored, and its text as a note.</summary>
            private void label(string written, Transform? where, DrcReportItem into)
            {
                string trimmed = written.Trim();
                int at = 1;

                if (trimmed.Length < 2 || trimmed[0] != '(' || (trimmed[1] != '\'' && trimmed[1] != '"'))
                {
                    unread("label");

                    return;
                }

                string said = quoted(trimmed, ref at);
                int comma = trimmed.IndexOf(',', at);
                int shut = trimmed.IndexOf(')', at);

                into.Notes.Add(said);

                if (where is not Transform placing)
                    return;

                if (comma < 0 || shut < comma || transformOf(trimmed[(comma + 1)..shut]) is not Transform anchor)
                {
                    unread("label");

                    return;
                }

                var point = pointsAt(new List<(double, double)> { (anchor.Dx, anchor.Dy) }, placing);

                if (point is not null)
                    into.Markers.Add(point);
            }

            ///<summary>What is between the brackets of `( ... )`, or null without them.</summary>
            private static string? bracketed(string written)
            {
                string trimmed = written.Trim();

                if (trimmed.Length < 2 || trimmed[0] != '(' || trimmed[^1] != ')')
                    return null;

                return trimmed[1..^1];
            }

            private List<Element.Point>? pointsOf(string inside, Transform placing)
            {
                var numbers = numbersOf(inside);

                if (numbers is null)
                    return null;

                return pointsAt(numbers, placing);
            }

            ///<summary>`x,y;x,y;...` as numbers, or null where one is not a number.</summary>
            private static List<(double X, double Y)>? numbersOf(string inside)
            {
                var found = new List<(double, double)>();

                foreach (string pair in inside.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    int comma = pair.IndexOf(',');

                    if (comma < 0)
                        return null;

                    if (!number(pair[..comma], out double x) || !number(pair[(comma + 1)..], out double y))
                        return null;

                    found.Add((x, y));
                }

                return found;
            }

            ///
            ///Points in a cell's microns, placed and rounded onto the layout's grid.
            ///
            ///Rounded once, at the end, so a placement several references deep costs half a unit at most rather
            ///than half a unit at every level. Null when any point lands outside what a coordinate can hold,
            ///which is a value this leaves out and says it has.
            ///
            private List<Element.Point>? pointsAt(List<(double X, double Y)> microns, Transform placing)
            {
                var points = new List<Element.Point>(microns.Count);

                foreach (var (x, y) in microns)
                {
                    var (placedX, placedY) = placing.ApplyTo(x, y);

                    double roundX = Math.Round(placedX);
                    double roundY = Math.Round(placedY);

                    if (double.IsNaN(roundX)
                        || double.IsNaN(roundY)
                        || roundX < int.MinValue
                        || roundX > int.MaxValue
                        || roundY < int.MinValue
                        || roundY > int.MaxValue)
                    {
                        outOfRange++;

                        return new List<Element.Point>();
                    }

                    points.Add(new Element.Point((int)roundX, (int)roundY));
                }

                return points;
            }

            #endregion
        }

        #endregion **************************************************************************



        #region What a report writes in its own notation ************************************

        ///
        ///A transform as KLayout writes one: `r90 *1 10.5,-2.25`, or `m45 *1 0,0` for one that mirrors.
        ///
        ///**`m` names the line it mirrors across, not a turn.** Mirroring across the x axis and then turning by
        ///an angle is the same as mirroring across a line at half that angle, and that line is what KLayout
        ///writes - so `m45` is the x axis mirrored and then turned a quarter, which swaps x and y. Each part
        ///may be left out: `1,2` is a move alone and `r90 1,2` has no magnification, both of which KLayout
        ///reads as well as writes. Null for anything else.
        ///
        private static Transform? transformOf(string written)
        {
            bool mirrored = false;
            double angle = 0;
            double magnification = 1;
            double dx = 0;
            double dy = 0;

            foreach (string part in written.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part[0] == 'r')
                {
                    if (!number(part[1..], out angle))
                        return null;

                    mirrored = false;
                }
                else if (part[0] == 'm')
                {
                    if (!number(part[1..], out double line))
                        return null;

                    mirrored = true;
                    angle = line * 2;
                }
                else if (part[0] == '*')
                {
                    if (!number(part[1..], out magnification))
                        return null;
                }
                else
                {
                    int comma = part.IndexOf(',');

                    if (comma < 0 || !number(part[..comma], out dx) || !number(part[(comma + 1)..], out dy))
                        return null;
                }
            }

            return Transform.ForPlacement(mirrored, magnification, angle, dx, dy);
        }

        ///<summary>The tags an item carries - `waived,#mine` - without the `#` that marks one somebody made.</summary>
        private static List<string> tagsOf(string written)
        {
            var tags = new List<string>();
            int at = 0;

            while (at < written.Length)
            {
                while (at < written.Length && (written[at] == ',' || char.IsWhiteSpace(written[at])))
                    at++;

                if (at >= written.Length)
                    break;

                if (written[at] == '#')
                    at++;

                if (at < written.Length && (written[at] == '\'' || written[at] == '"'))
                {
                    tags.Add(quoted(written, ref at));

                    continue;
                }

                int comma = written.IndexOf(',', at);

                if (comma < 0)
                    comma = written.Length;

                string tag = written[at..comma].Trim();

                if (tag.Length > 0)
                    tags.Add(tag);

                at = comma;
            }

            return tags;
        }

        ///<summary>A quoted string KLayout wrote, `'it\'s'`, as the text it holds. Text without quotes is kept as it is.</summary>
        private static string unquoted(string written)
        {
            string trimmed = written.Trim();

            if (trimmed.Length == 0 || (trimmed[0] != '\'' && trimmed[0] != '"'))
                return trimmed;

            int at = 0;

            return quoted(trimmed, ref at);
        }

        ///<summary>The quoted string starting at <paramref name="at"/>, which is left just past its closing quote.</summary>
        private static string quoted(string written, ref int at)
        {
            char quote = written[at];
            var built = new StringBuilder();

            at++;

            while (at < written.Length && written[at] != quote)
            {
                if (written[at] == '\\' && at + 1 < written.Length)
                {
                    at++;

                    built.Append(escapedCharacter(written[at]));
                }
                else
                    built.Append(written[at]);

                at++;
            }

            at = Math.Min(written.Length, at + 1);

            return built.ToString();
        }

        private static char escapedCharacter(char written)
        {
            if (written == 'n')
                return '\n';

            if (written == 't')
                return '\t';

            if (written == 'r')
                return '\r';

            return written;
        }

        ///<summary>A number as KLayout writes one - invariant, and in exponent form when it is very small or large.</summary>
        private static bool number(string written, out double value)
        {
            return double.TryParse(written.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && double.IsFinite(value);
        }

        ///<summary>Whether segment one-two crosses segment three-four, each passing through the other.</summary>
        private static bool crosses(Element.Point one, Element.Point two, Element.Point three, Element.Point four)
        {
            long side1 = turn(three, four, one);
            long side2 = turn(three, four, two);
            long side3 = turn(one, two, three);
            long side4 = turn(one, two, four);

            return ((side1 > 0 && side2 < 0) || (side1 < 0 && side2 > 0))
                && ((side3 > 0 && side4 < 0) || (side3 < 0 && side4 > 0));
        }

        private static long turn(Element.Point from, Element.Point to, Element.Point point)
        {
            return ((long)(to.X - from.X) * (point.Y - from.Y)) - ((long)(to.Y - from.Y) * (point.X - from.X));
        }

        private static string decoded(string text)
        {
            if (!text.Contains('&'))
                return text;

            return WebUtility.HtmlDecode(text);
        }

        private static int past(string text, string what, int from)
        {
            int end = text.IndexOf(what, from, StringComparison.Ordinal);

            if (end < 0)
                return text.Length;

            return end + what.Length;
        }

        #endregion **************************************************************************



        #region What is held while reading **************************************************

        private sealed class CategoryFrame
        {
            ///<summary>The names of the categories around this one, and then its own once it has been read.</summary>
            public List<string> Path { get; } = new List<string>();

            public string Name { get; set; } = "";

            ///<summary>Whether the name is on the path yet.</summary>
            public bool Named { get; set; }

            public string Description { get; set; } = "";
        }

        private sealed class CellDeclaration
        {
            public string Name { get; set; } = "";

            public string Variant { get; set; } = "";

            ///<summary>Each place the report says this cell sits: the cell around it, and the transform into that cell.</summary>
            public List<(string Parent, string Trans)> References { get; } = new List<(string, string)>();

            ///<summary>The name an item gives this cell by: its own, and the variant after a colon when it has one.</summary>
            public string QualifiedName()
            {
                if (Variant.Length == 0)
                    return Name;

                return $"{Name}:{Variant}";
            }
        }

        private sealed class PendingItem
        {
            public string Tags { get; set; } = "";

            public string Category { get; set; } = "";

            public string Cell { get; set; } = "";

            public bool Visited { get; set; }

            public string Comment { get; set; } = "";

            public List<string> Values { get; } = new List<string>();
        }

        #endregion **************************************************************************
    }

    ///<summary>
    ///A report database read back and placed on a layout: what it says about itself, its categories, and its
    ///items. See <see cref="DrcReport.Read(string, GDS, FlattenedLayout)"/>.
    ///</summary>
    public sealed class DrcReportFile
    {
        ///<summary>What the report calls itself - for KLayout's DRC, the name the script gave its report.</summary>
        public string Description { get; internal set; } = "";

        ///<summary>What wrote it. KLayout's DRC names the script that ran.</summary>
        public string Generator { get; internal set; } = "";

        ///<summary>The layout the report was made from, when the report says.</summary>
        public string OriginalFile { get; internal set; } = "";

        ///<summary>The cell the report's items are placed under.</summary>
        public string TopCell { get; internal set; } = "";

        ///<summary>
        ///Every category, in the order the report declares them, with how many items each holds.
        ///
        ///**Empty ones included**, since a report declares a category for every rule its script ran - which is
        ///how a clean rule is told from one that was never checked. Somebody listing them for a person to read
        ///will usually want only the ones with something in them.
        ///</summary>
        public List<DrcReportCategory> Categories { get; } = new List<DrcReportCategory>();

        ///<summary>Every item, in the order the report lists them.</summary>
        public List<DrcReportItem> Items { get; } = new List<DrcReportItem>();

        ///<summary>What could not be read or placed, a sentence each. Empty when everything was.</summary>
        public List<string> Problems { get; } = new List<string>();

        ///
        ///What the check that wrote the report said it did not get to, a sentence each: layout its rules never saw, rules
        ///it could not measure, a deck it could not read - see <see cref="DrcReport.NotCheckedCategory"/>. Empty for a
        ///report of a check that got to everything, and for one from a tool that does not say.
        ///
        ///**Not among <see cref="Items"/>**, nor its category among <see cref="Categories"/>: a sentence about the check
        ///is not a fault in the layout, and counted as one it would be a fault with nowhere to be found.
        ///
        public List<string> NotChecked { get; } = new List<string>();

        ///<summary>How many items somebody has waived - marked as looked at and accepted.</summary>
        public int Waived => Items.Count(item => item.Waived);

        ///<summary>How many items are in cells nothing could place, and so have nothing drawn.</summary>
        public int Unplaced => Items.Count(item => !item.Placed);
    }

    ///<summary>One category of a report: usually one rule, and how much was found under it.</summary>
    public sealed class DrcReportCategory
    {
        ///<summary>
        ///The category's name, with the names of any it sits inside ahead of it: `m1.width`, or
        ///`m1.width / sub` for one a category holds.
        ///</summary>
        public required string Name { get; init; }

        ///<summary>What the category is, as the report words it. Empty when it gives nothing.</summary>
        public string Description { get; internal set; } = "";

        ///<summary>How many items are in it, waived ones included.</summary>
        public int Items { get; internal set; }

        ///<summary>How many of those are waived.</summary>
        public int Waived { get; internal set; }
    }

    ///<summary>
    ///One item of a report: something found, in a category, filed under a cell - placed on the layout as the
    ///markers it draws, with whatever it says that is not a place kept as notes.
    ///</summary>
    public sealed class DrcReportItem
    {
        ///<summary>The category it is in, as <see cref="DrcReportCategory.Name"/> spells it.</summary>
        public required string Category { get; init; }

        ///<summary>The cell it is filed under, without the variant number a report adds to a cell's name.</summary>
        public required string Cell { get; init; }

        ///<summary>
        ///Whether somebody has waived it: looked at it and accepted it, which KLayout's marker browser records
        ///as a tag named `waived`.
        ///</summary>
        public bool Waived { get; init; }

        ///<summary>Whether it has been looked at in a marker browser.</summary>
        public bool Visited { get; init; }

        ///<summary>The comment somebody wrote on it. Empty for none.</summary>
        public string Comment { get; init; } = "";

        ///<summary>Every tag on it, `waived` among them when it is.</summary>
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

        ///<summary>Whether its cell could be found. An item that could not is listed but has nothing drawn.</summary>
        public bool Placed { get; init; }

        ///<summary>
        ///What it draws, in the layout's database units, one marker for each value that has a place.
        ///
        ///A marker of three points or more is an area, two is a line - an edge - and one is a point, which an
        ///edge of no length or a label is.
        ///</summary>
        public List<List<Element.Point>> Markers { get; } = new List<List<Element.Point>>();

        ///<summary>What it says that has no place: a measured number, a piece of text, a label's words.</summary>
        public List<string> Notes { get; } = new List<string>();

        ///<summary>The box around every marker it draws. Empty when it draws none.</summary>
        public Bounds Bounds
        {
            get
            {
                if (box is null)
                {
                    var around = Bounds.Empty;

                    foreach (var marker in Markers)
                        around = around.Union(Bounds.Of(marker));

                    box = around;
                }

                return box.Value;
            }
        }

        private Bounds? box;
    }
}
