using System.Globalization;
using System.Text;

using static GdsII.GDS;
using static GdsII.GDS.Record;

namespace GdsII
{
    ///
    ///Reading SVG, as a GDSII library.
    ///
    ///**Why bother.** A logo, a microfluidic channel, a test pattern or a photonic device is as likely to be
    ///drawn in Inkscape or Illustrator as in a layout tool, and SVG is what those save. Getting one onto a mask
    ///normally means a round trip through DXF and a converter; this reads the drawing itself.
    ///
    ///**What a drawing means on a mask:**
    ///
    ///- **Paint is area.** A filled shape is the area its fill covers, by the fill rule it names - so a ring
    ///drawn as one path of two outlines is a ring, not a disc - and a stroked one is the area the pen
    ///covers, its width, joins and ends included. Both come out as boundaries, keyholed where there is a
    ///hole, since GDSII has none. See <see cref="Booleans.Fill"/> and <see cref="Booleans.Stroke"/>.
    ///- **Layers come from the drawing's own organization.** Inside an Inkscape layer, the layer's name
    ///decides - and a name that *is* a number, `68`, `68/20` or `L68D20`, is taken as that layer, the way a
    ///DXF's is. Outside one, each paint color is a layer of its own, drawn in that color. Anything else is
    ///numbered in the order it first appears.
    ///- **Units are the drawing's.** A document `100mm` wide with a viewBox of a hundred is a hundred
    ///millimeters, whatever its user units are called; a drawing in pixels is measured at the CSS pixel, a
    ///ninety-sixth of an inch, which is what every SVG tool means by one. A database unit is a nanometer.
    ///- **Y goes up.** SVG counts down the page and GDSII up it, so the drawing is turned over once here and
    ///comes out the right way up in the file - and in KLayout, and on the mask. This app's own view draws Y
    ///downwards, so it shows it mirrored, which is the long-standing trade every shape here makes.
    ///- **Curves are flattened**, to within <see cref="CurveToleranceDatabaseUnits"/> of the curve - the same
    ///figure the DXF reader uses, for the same reason.
    ///
    ///**What is skipped**, said rather than discovered: text, images, gradients' colors (a gradient fills like
    ///any paint, on a layer of its own), clipping, masks, markers, filters, and anything hidden. A drawing
    ///that relies on a clip path comes out unclipped.
    ///
    public static class SvgReader
    {
        #region Constants *******************************************************************

        ///<summary>The cell the drawing becomes. A drawing is not a library, so it is given one cell.</summary>
        public const string TopCell = "SVG";

        ///<summary>How far a straight edge standing in for a curve may sit off it, in database units. See DxfReader's.</summary>
        public const double CurveToleranceDatabaseUnits = 1;

        ///<summary>What a CSS pixel is in microns: a ninety-sixth of an inch.</summary>
        public const double MicronsPerPixel = 25400.0 / 96;

        private const short GdsVersion = 600;

        private const string LibraryName = "LIB";

        ///<summary>How deep a `use` may refer to a `use` before it is taken to be a loop.</summary>
        private const int MostUses = 32;

        ///
        ///How deep a drawing may nest - counting what a `use` draws as inside it - before it is refused.
        ///
        ///**A real drawing nests a dozen or two**, so this is far past any of them. It is here because every
        ///level is a call deeper, and a stack that runs out is not an error anything can catch: two thousand
        ///nested groups took the whole process down, where in the browser it would take the app.
        ///
        private const int MostNesting = 128;

        ///
        ///The most elements a drawing may come to once every `use` in it is drawn out.
        ///
        ///**No drawing anybody made comes near it, but a few lines can.** A group of two uses of the group
        ///before it doubles at every line, and the thirty-two `use` levels <see cref="MostUses"/> allows would
        ///come to four billion - from a file of a few kilobytes, with nothing else running while it is drawn.
        ///So a drawing that gets this far is refused, in a few seconds rather than never.
        ///
        private const int MostElements = 2_000_000;

        ///<summary>How many times a curve may be halved while it is flattened - a thousand segments a curve, which is past any tolerance worth asking for.</summary>
        private const int MostHalvings = 10;

        #endregion **************************************************************************



        #region Entry points ****************************************************************

        ///
        ///Whether these bytes start like an SVG document.
        ///
        ///**Lenient on purpose**, because a drawing's opening is anybody's: an XML declaration, a comment saying
        ///which program wrote it, a DOCTYPE long enough to push the root element past the bytes looked at. So
        ///anything that opens as markup and mentions svg early on is taken - none of the other formats read
        ///here start with a `&lt;`, so nothing else can be mistaken for one - and the reader says plainly if
        ///it turns out not to be one after all.
        ///
        public static bool LooksLikeSvg(ReadOnlySpan<byte> start)
        {
            int at = 0;

            //A UTF-8 byte order mark, which Windows tools still write.
            if (start.Length >= 3 && start[0] == 0xEF && start[1] == 0xBB && start[2] == 0xBF)
                at = 3;

            while (at < start.Length && (start[at] == (byte)' ' || start[at] == (byte)'\t' || start[at] == (byte)'\r' || start[at] == (byte)'\n'))
                at++;

            if (at >= start.Length || start[at] != (byte)'<')
                return false;

            string head = Encoding.UTF8.GetString(start[at..]);

            return head.Contains("svg", StringComparison.OrdinalIgnoreCase) || head.StartsWith("<?xml", StringComparison.Ordinal);
        }

        public static GDS Read(byte[] bytes)
        {
            return Read(decoded(bytes));
        }

        public static GDS Read(Stream stream)
        {
            using var copy = new MemoryStream();

            stream.CopyTo(copy);

            return Read(copy.ToArray());
        }

        public static async Task<GDS> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();

            await stream.CopyToAsync(copy, cancellationToken);

            return Read(copy.ToArray());
        }

        ///<summary>The library, from the document's text.</summary>
        public static GDS Read(string text)
        {
            var document = Markup.Parse(text);
            var root = document.Children.FirstOrDefault(child => child.Name == "svg");

            if (root is null)
                throw new InvalidDataException("This is markup, but there is no <svg> element in it to read.");

            var drawing = new Drawing(document);

            drawing.Draw(root);

            var gds = GDS.FromRecords(drawing.ToRecords());

            //Named and colored after what the drawing called them, which is the one thing a number cannot say.
            foreach (var layer in drawing.Layers)
            {
                if (!gds.AdditionalInformation.Layers.TryGetValue(layer.Key, out var made))
                    continue;

                made.Name = layer.Name;

                if (layer.Color is string color)
                {
                    made.Color = color;
                    made.ColorIsCustom = true;
                }
            }

            return gds;
        }

        private static string decoded(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            return Encoding.UTF8.GetString(bytes);
        }

        #endregion **************************************************************************



        #region The markup ******************************************************************

        ///
        ///Just enough XML to read a drawing: elements, their attributes, and the text of a style sheet.
        ///
        ///**Not a general XML reader, and not one pulled in from the framework either.** Everything that ships
        ///in this app is compiled into it, and the framework's XML is a megabyte of it for what a drawing needs
        ///of XML: tags, attributes and five entities. Declarations, comments and a DOCTYPE - with an internal
        ///subset, which is what Illustrator writes - are stepped over.
        ///
        internal sealed class Markup
        {
            public string Name { get; private init; } = "";

            public Dictionary<string, string> Attributes { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            public List<Markup> Children { get; } = new List<Markup>();

            public Markup? Parent { get; private init; }

            ///<summary>How far in this is: one for the root element, two for what is directly inside it.</summary>
            public int Depth { get; private init; }

            ///<summary>The text inside, kept for a style sheet and nothing else.</summary>
            public StringBuilder Text { get; } = new StringBuilder();

            public string? Attribute(string name)
            {
                if (Attributes.TryGetValue(name, out string? value))
                    return value;

                return null;
            }

            public static Markup Parse(string text)
            {
                var document = new Markup { Name = "#document" };
                var current = document;
                int at = 0;

                while (at < text.Length)
                {
                    int open = text.IndexOf('<', at);

                    if (open < 0)
                        break;

                    if (open > at && current.Name == "style")
                        current.Text.Append(text, at, open - at);

                    if (string.CompareOrdinal(text, open, "<!--", 0, 4) == 0)
                    {
                        at = past(text, "-->", open + 4);

                        continue;
                    }

                    if (string.CompareOrdinal(text, open, "<![CDATA[", 0, 9) == 0)
                    {
                        int end = text.IndexOf("]]>", open + 9, StringComparison.Ordinal);

                        if (end < 0)
                            end = text.Length;

                        if (current.Name == "style")
                            current.Text.Append(text, open + 9, end - (open + 9));

                        at = Math.Min(text.Length, end + 3);

                        continue;
                    }

                    if (string.CompareOrdinal(text, open, "<?", 0, 2) == 0)
                    {
                        at = past(text, "?>", open + 2);

                        continue;
                    }

                    if (string.CompareOrdinal(text, open, "<!", 0, 2) == 0)
                    {
                        at = pastDeclaration(text, open + 2);

                        continue;
                    }

                    if (string.CompareOrdinal(text, open, "</", 0, 2) == 0)
                    {
                        if (current.Parent is not null)
                            current = current.Parent;

                        at = past(text, ">", open + 2);

                        continue;
                    }

                    at = startTag(text, open + 1, ref current);
                }

                return document;
            }

            ///<summary>A start tag and its attributes; where it ended, and the element it opened if it stays open.</summary>
            private static int startTag(string text, int at, ref Markup current)
            {
                int nameStart = at;

                while (at < text.Length && !char.IsWhiteSpace(text[at]) && text[at] != '>' && text[at] != '/')
                    at++;

                var element = new Markup { Name = localName(text[nameStart..at]), Parent = current, Depth = current.Depth + 1 };

                //Here, where the markup is read, so nothing that walks it afterwards can be handed one too deep.
                if (element.Depth > MostNesting)
                    throw new InvalidDataException($"This drawing nests elements more than {MostNesting} deep. Real drawings nest a dozen or two, so it is refused rather than read.");

                current.Children.Add(element);

                while (at < text.Length)
                {
                    while (at < text.Length && char.IsWhiteSpace(text[at]))
                        at++;

                    if (at >= text.Length)
                        break;

                    if (text[at] == '>')
                    {
                        current = element;

                        return at + 1;
                    }

                    if (text[at] == '/')
                        return past(text, ">", at + 1);

                    int attributeStart = at;

                    while (at < text.Length && text[at] != '=' && !char.IsWhiteSpace(text[at]) && text[at] != '>' && text[at] != '/')
                        at++;

                    string name = text[attributeStart..at];

                    while (at < text.Length && char.IsWhiteSpace(text[at]))
                        at++;

                    //An attribute with no value, which XML does not allow and HTML habits produce.
                    if (at >= text.Length || text[at] != '=')
                        continue;

                    at++;

                    while (at < text.Length && char.IsWhiteSpace(text[at]))
                        at++;

                    if (at >= text.Length)
                        break;

                    char quote = text[at];

                    if (quote != '"' && quote != '\'')
                        continue;

                    int end = text.IndexOf(quote, at + 1);

                    if (end < 0)
                        end = text.Length;

                    element.Attributes[name] = entities(text[(at + 1)..end]);

                    at = Math.Min(text.Length, end + 1);
                }

                return at;
            }

            ///<summary>An element's own name, without the `svg:` a namespaced document writes on every one.</summary>
            private static string localName(string name)
            {
                if (name.StartsWith("svg:", StringComparison.Ordinal))
                    return name[4..];

                return name;
            }

            private static int past(string text, string what, int from)
            {
                int end = text.IndexOf(what, from, StringComparison.Ordinal);

                if (end < 0)
                    return text.Length;

                return end + what.Length;
            }

            ///<summary>Past a declaration, counting brackets so a DOCTYPE's internal subset does not end it early.</summary>
            private static int pastDeclaration(string text, int from)
            {
                int depth = 0;

                for (int at = from; at < text.Length; at++)
                {
                    if (text[at] == '[')
                        depth++;
                    else if (text[at] == ']')
                        depth--;
                    else if (text[at] == '>' && depth <= 0)
                        return at + 1;
                }

                return text.Length;
            }

            private static string entities(string value)
            {
                if (!value.Contains('&'))
                    return value;

                var built = new StringBuilder(value.Length);
                int at = 0;

                while (at < value.Length)
                {
                    int amp = value.IndexOf('&', at);

                    if (amp < 0)
                    {
                        built.Append(value, at, value.Length - at);

                        break;
                    }

                    built.Append(value, at, amp - at);

                    int semi = value.IndexOf(';', amp);

                    if (semi < 0)
                    {
                        built.Append(value, amp, value.Length - amp);

                        break;
                    }

                    string name = value[(amp + 1)..semi];

                    if (character(name) is string decoded)
                        built.Append(decoded);
                    else
                        built.Append(value, amp, (semi + 1) - amp);

                    at = semi + 1;
                }

                return built.ToString();
            }

            private static string? character(string name)
            {
                if (name == "lt")
                    return "<";

                if (name == "gt")
                    return ">";

                if (name == "amp")
                    return "&";

                if (name == "quot")
                    return "\"";

                if (name == "apos")
                    return "'";

                if (name.StartsWith("#x", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(name[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex))
                    return codePoint(hex);

                if (name.StartsWith('#')
                    && int.TryParse(name[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    return codePoint(number);

                return null;
            }

            ///
            ///The character a reference names, or null for a number that names none.
            ///
            ///**A surrogate is not a character.** The numbers from D800 to DFFF are the two halves UTF-16 writes
            ///one character above FFFF as, and `ConvertFromUtf32` throws on one alone - which escaped as an
            ///exception the app does not catch, and took the page to its error screen over an id nobody reads.
            ///Left as written instead, the way an entity this does not know is.
            ///
            private static string? codePoint(int value)
            {
                if (value <= 0 || value > 0x10FFFF)
                    return null;

                if (value >= 0xD800 && value <= 0xDFFF)
                    return null;

                return char.ConvertFromUtf32(value);
            }
        }

        #endregion **************************************************************************



        #region The drawing *****************************************************************

        ///<summary>A layer the drawing asked for: what it called it, and the color it drew it in when that is what decided.</summary>
        internal sealed record DrawnLayer(LayerKey Key, string Name, string? Color);

        ///
        ///The document walked into areas on layers.
        ///
        internal sealed class Drawing
        {
            private readonly Dictionary<string, Markup> byId = new Dictionary<string, Markup>(StringComparer.Ordinal);
            private readonly List<StyleRule> rules = new List<StyleRule>();

            ///<summary>What each layer drew, in the order the drawing first asked for each, before they are numbered.</summary>
            private readonly List<(string Name, string? Color, List<List<Element.Point>> Areas)> drawn = new List<(string, string?, List<List<Element.Point>>)>();

            private readonly Dictionary<string, int> drawnAt = new Dictionary<string, int>(StringComparer.Ordinal);

            ///<summary>How many elements have been drawn so far, every `use` drawn out. See <see cref="MostElements"/>.</summary>
            private int elementsDrawn;

            public List<DrawnLayer> Layers { get; } = new List<DrawnLayer>();

            public Drawing(Markup document)
            {
                index(document);
            }

            private void index(Markup element)
            {
                if (element.Attribute("id") is string id && !byId.ContainsKey(id))
                    byId[id] = element;

                if (element.Name == "style")
                    rules.AddRange(StyleRule.Parse(element.Text.ToString(), rules.Count));

                foreach (var child in element.Children)
                    index(child);
            }

            ///
            ///Draws the document from its root: the root's own size, and everything inside it.
            ///
            ///**Measured from the root's width and height**, which is where a document says how big it is. Its
            ///user units are mapped onto that through the viewBox, the way a browser maps them onto a window;
            ///with no viewBox a user unit is a pixel, and with no size the viewBox is taken at a pixel a unit.
            ///
            public void Draw(Markup root)
            {
                var toPixels = Matrix.Identity;

                if (box(root.Attribute("viewBox")) is (double minX, double minY, double width, double height) && width > 0 && height > 0)
                {
                    double across = pixels(root.Attribute("width"), width);
                    double down = pixels(root.Attribute("height"), height);

                    toPixels = viewport(minX, minY, width, height, across, down, root.Attribute("preserveAspectRatio"));
                }

                //Pixels to microns to database units, and turned over so Y goes up.
                double scale = MicronsPerPixel / DxfReader.MicronsPerDatabaseUnit;
                var toFile = new Matrix(scale, 0, 0, -scale, 0, 0);

                var style = Style.Root.Under(computed(root));

                foreach (var child in root.Children)
                    draw(child, toPixels.Then(toFile), style, null, 0, 2);
            }

            ///
            ///One element, and everything inside it.
            ///
            ///<paramref name="depth"/> counts how far in this is, through every `use` on the way - the markup is
            ///held to <see cref="MostNesting"/> where it is read, but a use draws its target inside itself, so a
            ///chain of them can go deeper than anything written down.
            ///
            private void draw(Markup element, Matrix outer, Style parent, string? onLayer, int uses, int depth)
            {
                if (skipped.Contains(element.Name))
                    return;

                if (depth > MostNesting)
                    throw new InvalidDataException($"This drawing nests more than {MostNesting} deep once each use in it is drawn. Real drawings nest a dozen or two, so it is refused rather than read.");

                elementsDrawn++;

                if (elementsDrawn > MostElements)
                    throw new InvalidDataException("This drawing comes to more than two million elements once each use in it is drawn, which is most likely a use repeating a use. It is refused rather than read.");

                var style = parent.Under(computed(element));

                if (style.Hidden)
                    return;

                var matrix = Matrix.Parse(element.Attribute("transform")).Then(outer);

                //An Inkscape layer is a group the drawing says is a layer, and what is in it goes on that layer.
                if (element.Name == "g" && element.Attribute("inkscape:groupmode") == "layer")
                    onLayer = element.Attribute("inkscape:label") ?? element.Attribute("id") ?? onLayer;

                if (element.Name == "g" || element.Name == "a" || element.Name == "switch")
                {
                    foreach (var child in element.Children)
                        draw(child, matrix, style, onLayer, uses, depth + 1);

                    return;
                }

                if (element.Name == "svg")
                {
                    drawNested(element, matrix, style, onLayer, uses, depth);

                    return;
                }

                if (element.Name == "use")
                {
                    drawUse(element, matrix, style, onLayer, uses, depth);

                    return;
                }

                if (Shape.Of(element) is List<Shape.Run> runs)
                    paint(runs, matrix, style, onLayer);
            }

            private void drawNested(Markup element, Matrix outer, Style style, string? onLayer, int uses, int depth)
            {
                double x = number(element.Attribute("x"));
                double y = number(element.Attribute("y"));
                var inner = Matrix.Translation(x, y);

                if (box(element.Attribute("viewBox")) is (double minX, double minY, double width, double height) && width > 0 && height > 0)
                {
                    double across = pixels(element.Attribute("width"), width);
                    double down = pixels(element.Attribute("height"), height);

                    inner = viewport(minX, minY, width, height, across, down, element.Attribute("preserveAspectRatio")).Then(inner);
                }

                foreach (var child in element.Children)
                    draw(child, inner.Then(outer), style, onLayer, uses, depth + 1);
            }

            ///
            ///A `use`: the element it names, drawn again where the use says, styled as though the use were its
            ///parent - which is what the format says a use is.
            ///
            private void drawUse(Markup element, Matrix outer, Style style, string? onLayer, int uses, int depth)
            {
                if (uses >= MostUses)
                    return;

                string? reference = element.Attribute("href") ?? element.Attribute("xlink:href");

                if (reference is null || !reference.StartsWith('#') || !byId.TryGetValue(reference[1..], out var target))
                    return;

                var placed = Matrix.Translation(number(element.Attribute("x")), number(element.Attribute("y"))).Then(outer);

                if (target.Name == "symbol")
                {
                    var inner = placed;

                    if (box(target.Attribute("viewBox")) is (double minX, double minY, double width, double height) && width > 0 && height > 0)
                    {
                        double across = pixels(element.Attribute("width") ?? target.Attribute("width"), width);
                        double down = pixels(element.Attribute("height") ?? target.Attribute("height"), height);

                        inner = viewport(minX, minY, width, height, across, down, target.Attribute("preserveAspectRatio")).Then(placed);
                    }

                    var symbolStyle = style.Under(computed(target));

                    foreach (var child in target.Children)
                        draw(child, inner, symbolStyle, onLayer, uses + 1, depth + 1);

                    return;
                }

                draw(target, placed, style, onLayer, uses + 1, depth + 1);
            }

            ///
            ///One shape's paint, as areas on layers: its fill, by its fill rule, and its stroke, by its pen.
            ///
            private void paint(List<Shape.Run> runs, Matrix matrix, Style style, string? onLayer)
            {
                var placed = runs.Select(run => run.Flattened(matrix, CurveToleranceDatabaseUnits)).ToList();

                if (style.Fill is string fill)
                {
                    //Every run filled as though closed, which is what a fill does with an open one.
                    var rings = placed.Where(run => run.Points.Count >= 3).Select(run => (IReadOnlyList<Element.Point>)run.Points);
                    var areas = Booleans.Fill(rings, style.EvenOdd);

                    add(onLayer ?? fill, colorOf(onLayer, fill), areas);
                }

                if (style.Stroke is string stroke && style.StrokeWidth > 0)
                {
                    //A pen's width through the transform's scale - exact for anything turned or scaled evenly,
                    //and the mean of the two for a drawing stretched more one way than the other.
                    double width = style.StrokeWidth * Math.Sqrt(Math.Abs(matrix.Determinant));

                    var lines = placed.Where(run => run.Points.Count >= 2).Select(run => ((IReadOnlyList<Element.Point>)run.Points, run.Closed));
                    var areas = Booleans.Stroke(lines, width, style.Join, style.End, style.MiterLimit, CurveToleranceDatabaseUnits);

                    add(onLayer ?? stroke, colorOf(onLayer, stroke), areas);
                }
            }

            ///<summary>The color a layer is drawn in: its paint's, when the paint is what made it a layer.</summary>
            private static string? colorOf(string? onLayer, string paint)
            {
                if (onLayer is not null || !paint.StartsWith('#'))
                    return null;

                return paint;
            }

            private void add(string layer, string? color, List<List<Element.Point>> areas)
            {
                if (areas.Count == 0)
                    return;

                if (!drawnAt.TryGetValue(layer, out int at))
                {
                    at = drawn.Count;
                    drawnAt[layer] = at;
                    drawn.Add((layer, color, new List<List<Element.Point>>()));
                }

                drawn[at].Areas.AddRange(areas);
            }

            ///
            ///The library: one cell, a boundary per area, each layer numbered.
            ///
            ///**A name that is a number is that layer**; the rest are numbered from one in the order they were
            ///first drawn, past every number a name took - so `L5D0` drawn second does not collide with the
            ///fifth color. Numbered here, once everything is known, which is what makes that possible.
            ///
            public List<Record> ToRecords()
            {
                var taken = new HashSet<LayerKey>();
                var keys = new LayerKey?[drawn.Count];

                for (int i = 0; i < drawn.Count; i++)
                {
                    if (DxfReader.NumberFromName(drawn[i].Name) is LayerKey named && taken.Add(named))
                        keys[i] = named;
                }

                short next = 1;

                for (int i = 0; i < drawn.Count; i++)
                {
                    if (keys[i] is not null)
                        continue;

                    while (taken.Contains(new LayerKey(next, 0)))
                        next++;

                    keys[i] = new LayerKey(next, 0);
                    taken.Add(keys[i]!.Value);
                }

                var records = new List<Record>
                {
                    Hierarchy.Make(RecordType.HEADER, new Int2Data(GdsVersion)),
                    Hierarchy.Make(RecordType.BGNLIB, new Int2Data(new short[12])),
                    Hierarchy.Make(RecordType.LIBNAME, new AsciiData(LibraryName)),
                    Hierarchy.Make(RecordType.UNITS, new Real8Data(new double[] { DxfReader.MicronsPerDatabaseUnit, DxfReader.MicronsPerDatabaseUnit / 1e6 })),
                    Hierarchy.Make(RecordType.BGNSTR, new Int2Data(new short[12])),
                    Hierarchy.Make(RecordType.STRNAME, new AsciiData(TopCell))
                };

                for (int i = 0; i < drawn.Count; i++)
                {
                    var key = keys[i]!.Value;

                    Layers.Add(new DrawnLayer(key, drawn[i].Name, drawn[i].Color));

                    foreach (var area in drawn[i].Areas)
                        appendBoundary(records, key, area);
                }

                records.Add(Hierarchy.Make(RecordType.ENDSTR, null));
                records.Add(Hierarchy.Make(RecordType.ENDLIB, null));

                return records;
            }

            private static void appendBoundary(List<Record> records, LayerKey layer, List<Element.Point> area)
            {
                if (area.Count < 3)
                    return;

                var coordinates = new int[(area.Count + 1) * 2];

                for (int i = 0; i < area.Count; i++)
                {
                    coordinates[i * 2] = area[i].X;
                    coordinates[(i * 2) + 1] = area[i].Y;
                }

                coordinates[area.Count * 2] = area[0].X;
                coordinates[(area.Count * 2) + 1] = area[0].Y;

                records.Add(Hierarchy.Make(RecordType.BOUNDARY, null));
                records.Add(Hierarchy.Make(RecordType.LAYER, new Int2Data(layer.Number)));
                records.Add(Hierarchy.Make(RecordType.DATATYPE, new Int2Data(layer.DataType)));
                records.Add(Hierarchy.Make(RecordType.XY, new Int4Data(coordinates)));
                records.Add(Hierarchy.Make(RecordType.ENDEL, null));
            }

            ///<summary>An element's own declarations: its attributes, then the style sheet's rules for it, then its style attribute.</summary>
            private Dictionary<string, string> computed(Markup element)
            {
                var declared = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (string property in Style.Properties)
                {
                    if (element.Attribute(property) is string value)
                        declared[property] = value.Trim();
                }

                foreach (var rule in rules.Where(rule => rule.Matches(element)).OrderBy(rule => rule.Specificity).ThenBy(rule => rule.Order))
                {
                    foreach (var declaration in rule.Declarations)
                        declared[declaration.Key] = declaration.Value;
                }

                if (element.Attribute("style") is string inline)
                {
                    foreach (var declaration in StyleRule.DeclarationsIn(inline))
                        declared[declaration.Key] = declaration.Value;
                }

                return declared;
            }

            ///<summary>The elements that draw nothing of their own, or draw only when something else names them.</summary>
            private static readonly HashSet<string> skipped = new HashSet<string>(StringComparer.Ordinal)
            {
                "defs", "symbol", "clipPath", "mask", "pattern", "marker", "linearGradient", "radialGradient",
                "title", "desc", "metadata", "style", "script", "text", "image", "foreignObject", "filter",
                "sodipodi:namedview"
            };
        }

        #endregion **************************************************************************



        #region Styles **********************************************************************

        ///
        ///What paints an element and how, worked out from its own declarations and its parent's.
        ///
        internal sealed record Style(
            string? Fill,
            bool EvenOdd,
            string? Stroke,
            double StrokeWidth,
            Booleans.StrokeJoin Join,
            Booleans.StrokeEnd End,
            double MiterLimit,
            string Color,
            bool Hidden,
            double FillOpacity,
            double StrokeOpacity)
        {
            ///<summary>The format's own starting values: filled black, no stroke.</summary>
            public static Style Root { get; } = new Style("#000000", false, null, 1, Booleans.StrokeJoin.Miter, Booleans.StrokeEnd.Butt, 4, "#000000", false, 1, 1);

            public static readonly string[] Properties =
            {
                "fill", "fill-rule", "fill-opacity", "stroke", "stroke-width", "stroke-linejoin", "stroke-linecap",
                "stroke-miterlimit", "stroke-opacity", "display", "visibility", "opacity", "color"
            };

            ///
            ///This style with a child's declarations over it.
            ///
            ///Paint, its rule, the pen and the color inherit; `display` does not, but nothing under a
            ///display:none is drawn, so it carries down as hidden all the same. An opacity of nothing is the
            ///same as not being drawn.
            ///
            public Style Under(Dictionary<string, string> declared)
            {
                string color = Color;

                if (declared.TryGetValue("color", out string? colorValue) && colorValue != "inherit" && Colors.Normalized(colorValue) is string normalized)
                    color = normalized;

                bool hidden = Hidden;

                if (declared.TryGetValue("display", out string? display) && display == "none")
                    hidden = true;

                if (declared.TryGetValue("visibility", out string? visibility) && (visibility == "hidden" || visibility == "collapse"))
                    hidden = true;

                if (declared.TryGetValue("opacity", out string? opacity) && fraction(opacity) is double seen && seen <= 0)
                    hidden = true;

                string? fill = Fill;

                if (declared.TryGetValue("fill", out string? fillValue) && fillValue != "inherit")
                    fill = paintOf(fillValue, color);

                string? stroke = Stroke;

                if (declared.TryGetValue("stroke", out string? strokeValue) && strokeValue != "inherit")
                    stroke = paintOf(strokeValue, color);

                bool evenOdd = EvenOdd;

                if (declared.TryGetValue("fill-rule", out string? rule) && rule != "inherit")
                    evenOdd = rule == "evenodd";

                double width = StrokeWidth;

                if (declared.TryGetValue("stroke-width", out string? widthValue) && widthValue != "inherit" && !widthValue.Contains('%'))
                    width = pixels(widthValue, StrokeWidth);

                var join = Join;

                if (declared.TryGetValue("stroke-linejoin", out string? joinValue))
                {
                    if (joinValue == "round")
                        join = Booleans.StrokeJoin.Round;
                    else if (joinValue == "bevel")
                        join = Booleans.StrokeJoin.Bevel;
                    else if (joinValue == "miter" || joinValue == "miter-clip" || joinValue == "arcs")
                        join = Booleans.StrokeJoin.Miter;
                }

                var end = End;

                if (declared.TryGetValue("stroke-linecap", out string? capValue))
                {
                    if (capValue == "round")
                        end = Booleans.StrokeEnd.Round;
                    else if (capValue == "square")
                        end = Booleans.StrokeEnd.Square;
                    else if (capValue == "butt")
                        end = Booleans.StrokeEnd.Butt;
                }

                double miter = MiterLimit;

                if (declared.TryGetValue("stroke-miterlimit", out string? miterValue) && number(miterValue) >= 1)
                    miter = number(miterValue);

                double fillOpacity = FillOpacity;

                if (declared.TryGetValue("fill-opacity", out string? fillOpacityValue) && fraction(fillOpacityValue) is double fillSeen)
                    fillOpacity = fillSeen;

                double strokeOpacity = StrokeOpacity;

                if (declared.TryGetValue("stroke-opacity", out string? strokeOpacityValue) && fraction(strokeOpacityValue) is double strokeSeen)
                    strokeOpacity = strokeSeen;

                //Paint nobody can see is not paint.
                string? shownFill = fill;

                if (fillOpacity <= 0)
                    shownFill = null;

                string? shownStroke = stroke;

                if (strokeOpacity <= 0)
                    shownStroke = null;

                return new Style(shownFill, evenOdd, shownStroke, width, join, end, miter, color, hidden, fillOpacity, strokeOpacity);
            }

            ///<summary>An opacity, as a fraction or a percentage; null for one that does not read as either, which leaves it as it was.</summary>
            private static double? fraction(string text)
            {
                string trimmed = text.Trim();

                if (trimmed.EndsWith('%')
                    && double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double percent))
                    return percent / 100;

                if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    return value;

                return null;
            }

            ///<summary>A paint as the layer it decides: a color as #rrggbb, a gradient by its name, or null for none.</summary>
            private static string? paintOf(string value, string color)
            {
                string trimmed = value.Trim();

                if (trimmed == "none" || trimmed == "transparent" || trimmed.Length == 0)
                    return null;

                if (trimmed == "currentColor")
                    return color;

                if (trimmed.StartsWith("url(", StringComparison.Ordinal))
                {
                    int close = trimmed.IndexOf(')');

                    if (close > 0)
                        return trimmed[..(close + 1)];

                    return trimmed;
                }

                return Colors.Normalized(trimmed) ?? trimmed;
            }
        }

        ///
        ///A style sheet's rules, as far as a drawing uses them.
        ///
        ///**Simple selectors only** - an element, a class, an id, or an element with a class - which is what
        ///every drawing program writes: Illustrator's `.cls-1{fill:#e30613;}` is the whole of its styling. A
        ///selector with anything more in it is passed over, as a rule that matches nothing here.
        ///
        internal sealed record StyleRule(string? Element, string? Class, string? Id, int Specificity, int Order, Dictionary<string, string> Declarations)
        {
            public bool Matches(Markup element)
            {
                if (Element is not null && Element != element.Name)
                    return false;

                if (Id is not null && element.Attribute("id") != Id)
                    return false;

                if (Class is not null)
                {
                    string classes = element.Attribute("class") ?? "";

                    if (!classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(Class))
                        return false;
                }

                return true;
            }

            public static List<StyleRule> Parse(string sheet, int order)
            {
                var found = new List<StyleRule>();
                string text = System.Text.RegularExpressions.Regex.Replace(sheet, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match rule in System.Text.RegularExpressions.Regex.Matches(text, @"([^{}]+)\{([^{}]*)\}"))
                {
                    var declarations = DeclarationsIn(rule.Groups[2].Value);

                    foreach (string selector in rule.Groups[1].Value.Split(','))
                    {
                        var simple = System.Text.RegularExpressions.Regex.Match(selector.Trim(), @"^([A-Za-z][\w-]*)?(?:\.([\w-]+))?(?:#([\w-]+))?$");

                        if (!simple.Success || selector.Trim().Length == 0)
                            continue;

                        string? element = nonEmpty(simple.Groups[1].Value);
                        string? @class = nonEmpty(simple.Groups[2].Value);
                        string? id = nonEmpty(simple.Groups[3].Value);

                        int specificity = 0;

                        if (id is not null)
                            specificity += 100;

                        if (@class is not null)
                            specificity += 10;

                        if (element is not null)
                            specificity += 1;

                        found.Add(new StyleRule(element, @class, id, specificity, order + found.Count, declarations));
                    }
                }

                return found;
            }

            ///<summary>`name: value; name: value`, as a dictionary, an !important taken off.</summary>
            public static Dictionary<string, string> DeclarationsIn(string text)
            {
                var declared = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (string part in text.Split(';'))
                {
                    int colon = part.IndexOf(':');

                    if (colon <= 0)
                        continue;

                    string name = part[..colon].Trim().ToLowerInvariant();
                    string value = part[(colon + 1)..].Replace("!important", "", StringComparison.OrdinalIgnoreCase).Trim();

                    if (name.Length > 0)
                        declared[name] = value;
                }

                return declared;
            }

            private static string? nonEmpty(string value)
            {
                if (value.Length == 0)
                    return null;

                return value;
            }
        }

        ///<summary>Colors as the six-digit hex a layer is drawn in.</summary>
        internal static class Colors
        {
            private static readonly Dictionary<string, string> named = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = "#000000", ["white"] = "#ffffff", ["red"] = "#ff0000", ["lime"] = "#00ff00",
                ["green"] = "#008000", ["blue"] = "#0000ff", ["yellow"] = "#ffff00", ["cyan"] = "#00ffff",
                ["aqua"] = "#00ffff", ["magenta"] = "#ff00ff", ["fuchsia"] = "#ff00ff", ["gray"] = "#808080",
                ["grey"] = "#808080", ["silver"] = "#c0c0c0", ["maroon"] = "#800000", ["olive"] = "#808000",
                ["purple"] = "#800080", ["teal"] = "#008080", ["navy"] = "#000080", ["orange"] = "#ffa500"
            };

            public static string? Normalized(string value)
            {
                string trimmed = value.Trim();

                if (named.TryGetValue(trimmed, out string? hex))
                    return hex;

                if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]{6}$"))
                    return trimmed.ToLowerInvariant();

                if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9a-fA-F]{3}$"))
                    return ("#" + trimmed[1] + trimmed[1] + trimmed[2] + trimmed[2] + trimmed[3] + trimmed[3]).ToLowerInvariant();

                var rgb = System.Text.RegularExpressions.Regex.Match(trimmed, @"^rgba?\(\s*([\d.]+%?)\s*[, ]\s*([\d.]+%?)\s*[, ]\s*([\d.]+%?)");

                if (rgb.Success)
                    return "#" + channel(rgb.Groups[1].Value) + channel(rgb.Groups[2].Value) + channel(rgb.Groups[3].Value);

                return null;
            }

            private static string channel(string value)
            {
                double amount;

                if (value.EndsWith('%'))
                    amount = number(value[..^1]) * 255 / 100;
                else
                    amount = number(value);

                return ((int)Math.Clamp(Math.Round(amount), 0, 255)).ToString("x2", CultureInfo.InvariantCulture);
            }
        }

        #endregion **************************************************************************



        #region Shapes **********************************************************************

        ///<summary>The outlines an element draws, before any paint: each a run of straight and curved pieces.</summary>
        internal static class Shape
        {
            ///<summary>One piece of a run: a line to a point, a cubic curve to one, or an arc of an ellipse to one.</summary>
            internal readonly record struct Piece(bool Curved, double X1, double Y1, double X2, double Y2, double X, double Y, Ellipse? Arc = null);

            ///
            ///An arc as the ellipse it is part of: its center, its two radii, the turn of its first axis as a
            ///cosine and a sine, and the angles it runs between.
            ///
            ///**Kept as an arc rather than turned into cubics**, which is how most readers draw one. The cubic
            ///that best fits a quarter circle sits up to 0.027% of the radius off it - 270 nanometers on a
            ///millimeter, measured, where every other curve here is within one. Flattened as the arc itself,
            ///through the transform, it is held to the same tolerance as everything else.
            ///
            internal sealed record Ellipse(double CenterX, double CenterY, double RadiusX, double RadiusY, double Cos, double Sin, double Start, double Sweep)
            {
                public (double X, double Y) At(double angle)
                {
                    double x = RadiusX * Math.Cos(angle);
                    double y = RadiusY * Math.Sin(angle);

                    return ((Cos * x) - (Sin * y) + CenterX, (Sin * x) + (Cos * y) + CenterY);
                }
            }

            ///<summary>One run of an outline, from its start through its pieces, and whether it closes.</summary>
            internal sealed class Run
            {
                public double StartX { get; init; }
                public double StartY { get; init; }
                public List<Piece> Pieces { get; } = new List<Piece>();
                public bool Closed { get; set; }

                ///<summary>Placed and flattened: through the matrix, every curve cut into lines within the tolerance, in whole units.</summary>
                public (List<Element.Point> Points, bool Closed) Flattened(Matrix matrix, double tolerance)
                {
                    var points = new List<Element.Point>();
                    var (x, y) = matrix.Apply(StartX, StartY);

                    add(points, x, y);

                    foreach (var piece in Pieces)
                    {
                        var to = matrix.Apply(piece.X, piece.Y);

                        if (piece.Arc is Ellipse arc)
                        {
                            //Its larger radius as it comes out of the transform, which is what the sagitta of a
                            //step is measured against - so a step is never further off the arc than asked.
                            double radius = Math.Max(arc.RadiusX, arc.RadiusY) * matrix.LargestStretch;
                            int steps = DxfCurves.SegmentsFor(radius, arc.Sweep, tolerance);

                            for (int step = 1; step < steps; step++)
                            {
                                var (onX, onY) = arc.At(arc.Start + (arc.Sweep * step / steps));
                                var placed = matrix.Apply(onX, onY);

                                add(points, placed.X, placed.Y);
                            }
                        }
                        else if (piece.Curved)
                        {
                            var one = matrix.Apply(piece.X1, piece.Y1);
                            var two = matrix.Apply(piece.X2, piece.Y2);

                            cubic(points, (x, y), one, two, to, tolerance, 0);
                        }

                        add(points, to.X, to.Y);

                        (x, y) = to;
                    }

                    return (points, Closed);
                }

                ///<summary>De Casteljau: halved until the control points sit within the tolerance of the chord.</summary>
                private static void cubic(
                    List<Element.Point> points,
                    (double X, double Y) from,
                    (double X, double Y) one,
                    (double X, double Y) two,
                    (double X, double Y) to,
                    double tolerance,
                    int depth)
                {
                    if (depth >= MostHalvings || flatEnough(from, one, two, to, tolerance))
                        return;

                    var a = middle(from, one);
                    var b = middle(one, two);
                    var c = middle(two, to);
                    var d = middle(a, b);
                    var e = middle(b, c);
                    var split = middle(d, e);

                    cubic(points, from, a, d, split, tolerance, depth + 1);

                    add(points, split.X, split.Y);

                    cubic(points, split, e, c, to, tolerance, depth + 1);
                }

                private static bool flatEnough((double X, double Y) from, (double X, double Y) one, (double X, double Y) two, (double X, double Y) to, double tolerance)
                {
                    return distanceToChord(one, from, to) <= tolerance && distanceToChord(two, from, to) <= tolerance;
                }

                private static double distanceToChord((double X, double Y) point, (double X, double Y) from, (double X, double Y) to)
                {
                    double dx = to.X - from.X;
                    double dy = to.Y - from.Y;
                    double length = Math.Sqrt((dx * dx) + (dy * dy));

                    if (length < 1e-9)
                        return Math.Sqrt(((point.X - from.X) * (point.X - from.X)) + ((point.Y - from.Y) * (point.Y - from.Y)));

                    return Math.Abs(((point.X - from.X) * dy) - ((point.Y - from.Y) * dx)) / length;
                }

                private static (double X, double Y) middle((double X, double Y) a, (double X, double Y) b)
                {
                    return ((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                }

                private static void add(List<Element.Point> points, double x, double y)
                {
                    if (Math.Abs(x) > int.MaxValue / 2 || Math.Abs(y) > int.MaxValue / 2 || double.IsNaN(x) || double.IsNaN(y))
                        throw new InvalidDataException("This drawing is too large for a GDSII file at a nanometer a unit.");

                    var point = new Element.Point((int)Math.Round(x), (int)Math.Round(y));

                    if (points.Count == 0 || !points[^1].Equals(point))
                        points.Add(point);
                }
            }

            ///<summary>The runs an element draws, or null for one that draws no outline.</summary>
            public static List<Run>? Of(Markup element)
            {
                if (element.Name == "path")
                    return Path(element.Attribute("d") ?? "");

                if (element.Name == "rect")
                    return rect(element);

                if (element.Name == "circle")
                {
                    double radius = number(element.Attribute("r"));

                    return ellipse(number(element.Attribute("cx")), number(element.Attribute("cy")), radius, radius);
                }

                if (element.Name == "ellipse")
                    return ellipse(number(element.Attribute("cx")), number(element.Attribute("cy")), number(element.Attribute("rx")), number(element.Attribute("ry")));

                if (element.Name == "line")
                {
                    var line = new Run { StartX = number(element.Attribute("x1")), StartY = number(element.Attribute("y1")) };

                    line.Pieces.Add(new Piece(false, 0, 0, 0, 0, number(element.Attribute("x2")), number(element.Attribute("y2"))));

                    return new List<Run> { line };
                }

                if (element.Name == "polyline" || element.Name == "polygon")
                {
                    var values = numbers(element.Attribute("points") ?? "");

                    if (values.Count < 4)
                        return null;

                    var run = new Run { StartX = values[0], StartY = values[1], Closed = element.Name == "polygon" };

                    for (int i = 2; i + 1 < values.Count; i += 2)
                        run.Pieces.Add(new Piece(false, 0, 0, 0, 0, values[i], values[i + 1]));

                    return new List<Run> { run };
                }

                return null;
            }

            private static List<Run>? rect(Markup element)
            {
                double x = number(element.Attribute("x"));
                double y = number(element.Attribute("y"));
                double width = number(element.Attribute("width"));
                double height = number(element.Attribute("height"));

                if (width <= 0 || height <= 0)
                    return null;

                double rx = number(element.Attribute("rx"));
                double ry = number(element.Attribute("ry"));

                //One radius given stands for both, as the format says.
                if (element.Attribute("rx") is null)
                    rx = ry;

                if (element.Attribute("ry") is null)
                    ry = rx;

                rx = Math.Min(Math.Max(rx, 0), width / 2);
                ry = Math.Min(Math.Max(ry, 0), height / 2);

                if (rx <= 0 || ry <= 0)
                {
                    var square = new Run { StartX = x, StartY = y, Closed = true };

                    square.Pieces.Add(new Piece(false, 0, 0, 0, 0, x + width, y));
                    square.Pieces.Add(new Piece(false, 0, 0, 0, 0, x + width, y + height));
                    square.Pieces.Add(new Piece(false, 0, 0, 0, 0, x, y + height));

                    return new List<Run> { square };
                }

                return Path(FormattableString.Invariant(
                    $"M{x + rx},{y} H{x + width - rx} A{rx},{ry} 0 0 1 {x + width},{y + ry} V{y + height - ry} A{rx},{ry} 0 0 1 {x + width - rx},{y + height} H{x + rx} A{rx},{ry} 0 0 1 {x},{y + height - ry} V{y + ry} A{rx},{ry} 0 0 1 {x + rx},{y} Z"));
            }

            private static List<Run>? ellipse(double cx, double cy, double rx, double ry)
            {
                if (rx <= 0 || ry <= 0)
                    return null;

                return Path(FormattableString.Invariant(
                    $"M{cx + rx},{cy} A{rx},{ry} 0 0 1 {cx},{cy + ry} A{rx},{ry} 0 0 1 {cx - rx},{cy} A{rx},{ry} 0 0 1 {cx},{cy - ry} A{rx},{ry} 0 0 1 {cx + rx},{cy} Z"));
            }

            ///
            ///A path's data, as runs: every command the format has, relative and absolute, with a quadratic
            ///raised to a cubic and an arc cut into quarter turns of cubic.
            ///
            ///**Read as far as it makes sense, and no further** - which is what the format itself asks of a
            ///reader: an error part way along keeps what came before it.
            ///
            public static List<Run> Path(string data)
            {
                var runs = new List<Run>();
                var tokens = new PathTokens(data);

                Run? run = null;
                double x = 0;
                double y = 0;
                double startX = 0;
                double startY = 0;

                //The last control point, for the smooth curves that reflect it.
                double lastControlX = 0;
                double lastControlY = 0;
                char last = ' ';

                char command = ' ';

                while (tokens.More)
                {
                    if (tokens.NextIsCommand)
                        command = tokens.Command();
                    else if (command == ' ')
                        break;

                    bool relative = char.IsLower(command);
                    char kind = char.ToUpperInvariant(command);

                    double offsetX = 0;
                    double offsetY = 0;

                    if (relative)
                    {
                        offsetX = x;
                        offsetY = y;
                    }

                    if (kind == 'Z')
                    {
                        if (run is not null)
                            run.Closed = true;

                        x = startX;
                        y = startY;
                        run = null;
                        last = 'Z';

                        //A Z can be followed by a number only by mistake; the next command letter decides.
                        command = ' ';

                        continue;
                    }

                    if (kind == 'M')
                    {
                        if (tokens.Numbers(2) is not double[] moved)
                            break;

                        x = moved[0] + offsetX;
                        y = moved[1] + offsetY;
                        startX = x;
                        startY = y;

                        run = new Run { StartX = x, StartY = y };
                        runs.Add(run);

                        //Further pairs after a move are lines, relative if the move was.
                        if (relative)
                            command = 'l';
                        else
                            command = 'L';

                        last = 'M';

                        continue;
                    }

                    run ??= startAgain(runs, x, y);

                    if (kind == 'L')
                    {
                        if (tokens.Numbers(2) is not double[] to)
                            break;

                        x = to[0] + offsetX;
                        y = to[1] + offsetY;

                        run.Pieces.Add(new Piece(false, 0, 0, 0, 0, x, y));
                    }
                    else if (kind == 'H')
                    {
                        if (tokens.Numbers(1) is not double[] to)
                            break;

                        x = to[0] + offsetX;

                        run.Pieces.Add(new Piece(false, 0, 0, 0, 0, x, y));
                    }
                    else if (kind == 'V')
                    {
                        if (tokens.Numbers(1) is not double[] to)
                            break;

                        y = to[0] + offsetY;

                        run.Pieces.Add(new Piece(false, 0, 0, 0, 0, x, y));
                    }
                    else if (kind == 'C' || kind == 'S')
                    {
                        double x1;
                        double y1;
                        double[]? rest;

                        if (kind == 'C')
                        {
                            if (tokens.Numbers(6) is not double[] all)
                                break;

                            x1 = all[0] + offsetX;
                            y1 = all[1] + offsetY;
                            rest = all[2..];
                        }
                        else
                        {
                            if (tokens.Numbers(4) is not double[] all)
                                break;

                            //The first control point is the last one reflected, after another cubic.
                            if (last == 'C' || last == 'S')
                            {
                                x1 = (2 * x) - lastControlX;
                                y1 = (2 * y) - lastControlY;
                            }
                            else
                            {
                                x1 = x;
                                y1 = y;
                            }

                            rest = all;
                        }

                        double x2 = rest[0] + offsetX;
                        double y2 = rest[1] + offsetY;
                        double toX = rest[2] + offsetX;
                        double toY = rest[3] + offsetY;

                        run.Pieces.Add(new Piece(true, x1, y1, x2, y2, toX, toY));

                        lastControlX = x2;
                        lastControlY = y2;
                        x = toX;
                        y = toY;
                    }
                    else if (kind == 'Q' || kind == 'T')
                    {
                        double qx;
                        double qy;
                        double toX;
                        double toY;

                        if (kind == 'Q')
                        {
                            if (tokens.Numbers(4) is not double[] all)
                                break;

                            qx = all[0] + offsetX;
                            qy = all[1] + offsetY;
                            toX = all[2] + offsetX;
                            toY = all[3] + offsetY;
                        }
                        else
                        {
                            if (tokens.Numbers(2) is not double[] all)
                                break;

                            if (last == 'Q' || last == 'T')
                            {
                                qx = (2 * x) - lastControlX;
                                qy = (2 * y) - lastControlY;
                            }
                            else
                            {
                                qx = x;
                                qy = y;
                            }

                            toX = all[0] + offsetX;
                            toY = all[1] + offsetY;
                        }

                        //A quadratic is a cubic with its controls two thirds of the way to the quadratic's.
                        run.Pieces.Add(new Piece(
                            true,
                            x + ((qx - x) * 2 / 3),
                            y + ((qy - y) * 2 / 3),
                            toX + ((qx - toX) * 2 / 3),
                            toY + ((qy - toY) * 2 / 3),
                            toX,
                            toY));

                        lastControlX = qx;
                        lastControlY = qy;
                        x = toX;
                        y = toY;
                    }
                    else if (kind == 'A')
                    {
                        if (tokens.Arc() is not double[] arc)
                            break;

                        double toX = arc[5] + offsetX;
                        double toY = arc[6] + offsetY;

                        foreach (var piece in Arc(x, y, arc[0], arc[1], arc[2], arc[3] != 0, arc[4] != 0, toX, toY))
                            run.Pieces.Add(piece);

                        x = toX;
                        y = toY;
                    }
                    else
                    {
                        //A letter this does not know: what came before it stands.
                        break;
                    }

                    last = kind;
                }

                return runs;
            }

            ///<summary>A drawing command with no move before it starts a run where the pen already is.</summary>
            private static Run startAgain(List<Run> runs, double x, double y)
            {
                var run = new Run { StartX = x, StartY = y };

                runs.Add(run);

                return run;
            }

            ///
            ///An elliptical arc from one point to another, as one piece.
            ///
            ///The endpoint form the format writes converted to a center, two radii and two angles - the
            ///conversion the SVG specification gives in its implementation notes, radii grown where they are
            ///too small to reach. See <see cref="Ellipse"/> for why it stays an arc.
            ///
            public static List<Piece> Arc(double fromX, double fromY, double rx, double ry, double turnDegrees, bool large, bool sweep, double toX, double toY)
            {
                var pieces = new List<Piece>();

                if (fromX == toX && fromY == toY)
                    return pieces;

                rx = Math.Abs(rx);
                ry = Math.Abs(ry);

                if (rx == 0 || ry == 0)
                {
                    pieces.Add(new Piece(false, 0, 0, 0, 0, toX, toY));

                    return pieces;
                }

                double phi = turnDegrees * Math.PI / 180;
                double cos = Math.Cos(phi);
                double sin = Math.Sin(phi);

                double dx = (fromX - toX) / 2;
                double dy = (fromY - toY) / 2;
                double x1 = (cos * dx) + (sin * dy);
                double y1 = (-sin * dx) + (cos * dy);

                double lambda = ((x1 * x1) / (rx * rx)) + ((y1 * y1) / (ry * ry));

                if (lambda > 1)
                {
                    rx *= Math.Sqrt(lambda);
                    ry *= Math.Sqrt(lambda);
                }

                double numerator = (rx * rx * ry * ry) - (rx * rx * y1 * y1) - (ry * ry * x1 * x1);
                double denominator = (rx * rx * y1 * y1) + (ry * ry * x1 * x1);
                double factor = Math.Sqrt(Math.Max(0, numerator / denominator));

                if (large == sweep)
                    factor = -factor;

                double cx1 = factor * rx * y1 / ry;
                double cy1 = -factor * ry * x1 / rx;

                double cx = (cos * cx1) - (sin * cy1) + ((fromX + toX) / 2);
                double cy = (sin * cx1) + (cos * cy1) + ((fromY + toY) / 2);

                double start = angle(1, 0, (x1 - cx1) / rx, (y1 - cy1) / ry);
                double delta = angle((x1 - cx1) / rx, (y1 - cy1) / ry, (-x1 - cx1) / rx, (-y1 - cy1) / ry);

                if (!sweep && delta > 0)
                    delta -= 2 * Math.PI;
                else if (sweep && delta < 0)
                    delta += 2 * Math.PI;

                //It lands exactly where it was asked to end, rather than where the arithmetic puts it.
                pieces.Add(new Piece(true, 0, 0, 0, 0, toX, toY, new Ellipse(cx, cy, rx, ry, cos, sin, start, delta)));

                return pieces;
            }

            private static double angle(double ux, double uy, double vx, double vy)
            {
                return Math.Atan2((ux * vy) - (uy * vx), (ux * vx) + (uy * vy));
            }
        }

        ///<summary>A path's data read as letters and numbers, the way the format packs them: `M1-2.5.5e3`.</summary>
        internal sealed class PathTokens
        {
            private readonly string text;
            private int at;

            public PathTokens(string text)
            {
                this.text = text;
            }

            public bool More
            {
                get
                {
                    skip();

                    return at < text.Length;
                }
            }

            public bool NextIsCommand
            {
                get
                {
                    skip();

                    return at < text.Length && char.IsLetter(text[at]) && text[at] != 'e' && text[at] != 'E';
                }
            }

            public char Command()
            {
                skip();

                return text[at++];
            }

            ///<summary>That many numbers, or null if the data runs out or holds something else first.</summary>
            public double[]? Numbers(int count)
            {
                var read = new double[count];

                for (int i = 0; i < count; i++)
                {
                    if (Number() is not double value)
                        return null;

                    read[i] = value;
                }

                return read;
            }

            ///
            ///An arc's seven, where the two flags may be packed with no space - `a5 5 0 0110 10` is a large
            ///sweep of radius five - because a flag is a single digit and the format lets writers run them on.
            ///
            public double[]? Arc()
            {
                if (Numbers(3) is not double[] radii)
                    return null;

                if (flag() is not double large || flag() is not double sweep)
                    return null;

                if (Numbers(2) is not double[] to)
                    return null;

                return new[] { radii[0], radii[1], radii[2], large, sweep, to[0], to[1] };
            }

            private double? flag()
            {
                skip();

                if (at < text.Length && (text[at] == '0' || text[at] == '1'))
                    return text[at++] - '0';

                return null;
            }

            public double? Number()
            {
                skip();

                int start = at;

                if (at < text.Length && (text[at] == '+' || text[at] == '-'))
                    at++;

                bool digits = false;

                while (at < text.Length && char.IsDigit(text[at]))
                {
                    at++;
                    digits = true;
                }

                if (at < text.Length && text[at] == '.')
                {
                    at++;

                    while (at < text.Length && char.IsDigit(text[at]))
                    {
                        at++;
                        digits = true;
                    }
                }

                if (!digits)
                {
                    at = start;

                    return null;
                }

                if (at < text.Length && (text[at] == 'e' || text[at] == 'E'))
                {
                    int exponent = at;

                    at++;

                    if (at < text.Length && (text[at] == '+' || text[at] == '-'))
                        at++;

                    if (at < text.Length && char.IsDigit(text[at]))
                    {
                        while (at < text.Length && char.IsDigit(text[at]))
                            at++;
                    }
                    else
                    {
                        at = exponent;
                    }
                }

                return double.Parse(text.AsSpan(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            private void skip()
            {
                while (at < text.Length && (char.IsWhiteSpace(text[at]) || text[at] == ','))
                    at++;
            }
        }

        #endregion **************************************************************************



        #region Transforms and lengths ******************************************************

        ///<summary>A 2D affine transform, as SVG writes one: x' = a x + c y + e, y' = b x + d y + f.</summary>
        internal readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
        {
            public static Matrix Identity { get; } = new Matrix(1, 0, 0, 1, 0, 0);

            public double Determinant
            {
                get { return (A * D) - (B * C); }
            }

            ///<summary>The most this stretches any length: the larger singular value of its linear part.</summary>
            public double LargestStretch
            {
                get
                {
                    double sum = (A * A) + (B * B) + (C * C) + (D * D);
                    double spread = Math.Sqrt(Math.Max(0, (sum * sum) - (4 * Determinant * Determinant)));

                    return Math.Sqrt((sum + spread) / 2);
                }
            }

            public static Matrix Translation(double x, double y)
            {
                return new Matrix(1, 0, 0, 1, x, y);
            }

            public (double X, double Y) Apply(double x, double y)
            {
                return ((A * x) + (C * y) + E, (B * x) + (D * y) + F);
            }

            ///<summary>This one, and then that one - so a child's transform `Then` its parent's is the whole of it.</summary>
            public Matrix Then(Matrix next)
            {
                return new Matrix(
                    (next.A * A) + (next.C * B),
                    (next.B * A) + (next.D * B),
                    (next.A * C) + (next.C * D),
                    (next.B * C) + (next.D * D),
                    (next.A * E) + (next.C * F) + next.E,
                    (next.B * E) + (next.D * F) + next.F);
            }

            ///<summary>A transform attribute: a list of functions, applied right to left as the format says.</summary>
            public static Matrix Parse(string? text)
            {
                var result = Identity;

                if (string.IsNullOrWhiteSpace(text))
                    return result;

                var functions = System.Text.RegularExpressions.Regex.Matches(text, @"(matrix|translate|scale|rotate|skewX|skewY)\s*\(([^)]*)\)");

                //The rightmost is applied first, so the list is folded from the right.
                for (int i = functions.Count - 1; i >= 0; i--)
                {
                    var values = numbers(functions[i].Groups[2].Value);
                    var step = function(functions[i].Groups[1].Value, values);

                    result = result.Then(step);
                }

                return result;
            }

            private static Matrix function(string name, List<double> values)
            {
                double at(int index, double fallback)
                {
                    if (index < values.Count)
                        return values[index];

                    return fallback;
                }

                if (name == "matrix" && values.Count >= 6)
                    return new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]);

                if (name == "translate")
                    return Translation(at(0, 0), at(1, 0));

                if (name == "scale")
                    return new Matrix(at(0, 1), 0, 0, at(1, at(0, 1)), 0, 0);

                if (name == "rotate")
                {
                    double radians = at(0, 0) * Math.PI / 180;
                    var turn = new Matrix(Math.Cos(radians), Math.Sin(radians), -Math.Sin(radians), Math.Cos(radians), 0, 0);
                    double cx = at(1, 0);
                    double cy = at(2, 0);

                    return Translation(-cx, -cy).Then(turn).Then(Translation(cx, cy));
                }

                if (name == "skewX")
                    return new Matrix(1, 0, Math.Tan(at(0, 0) * Math.PI / 180), 1, 0, 0);

                if (name == "skewY")
                    return new Matrix(1, Math.Tan(at(0, 0) * Math.PI / 180), 0, 1, 0, 0);

                return Identity;
            }
        }

        ///
        ///How a viewBox maps onto the space given it, by preserveAspectRatio: `none`, or scaled the same both
        ///ways to fit inside (`meet`, the default) or to fill (`slice`), and lined up as asked.
        ///
        private static Matrix viewport(double minX, double minY, double width, double height, double across, double down, string? aspect)
        {
            double scaleX = across / width;
            double scaleY = down / height;

            string fit = (aspect ?? "xMidYMid meet").Trim();

            if (fit.StartsWith("none", StringComparison.Ordinal))
                return Matrix.Translation(-minX, -minY).Then(new Matrix(scaleX, 0, 0, scaleY, 0, 0));

            double scale = Math.Min(scaleX, scaleY);

            if (fit.Contains("slice", StringComparison.Ordinal))
                scale = Math.Max(scaleX, scaleY);

            double spareX = across - (width * scale);
            double spareY = down - (height * scale);

            double offsetX = spareX / 2;
            double offsetY = spareY / 2;

            if (fit.Contains("xMin", StringComparison.Ordinal))
                offsetX = 0;
            else if (fit.Contains("xMax", StringComparison.Ordinal))
                offsetX = spareX;

            if (fit.Contains("YMin", StringComparison.Ordinal))
                offsetY = 0;
            else if (fit.Contains("YMax", StringComparison.Ordinal))
                offsetY = spareY;

            return Matrix.Translation(-minX, -minY).Then(new Matrix(scale, 0, 0, scale, offsetX, offsetY));
        }

        private static (double MinX, double MinY, double Width, double Height)? box(string? text)
        {
            if (text is null)
                return null;

            var values = numbers(text);

            if (values.Count < 4)
                return null;

            return (values[0], values[1], values[2], values[3]);
        }

        ///
        ///A length in CSS pixels: a number with its unit, or <paramref name="fallback"/> for none, nothing, or a
        ///percentage - which is of a viewport this reader does not have, and so is taken as the whole of what
        ///the viewBox already says.
        ///
        private static double pixels(string? text, double fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            var match = System.Text.RegularExpressions.Regex.Match(text.Trim(), @"^([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-zA-Z%]*)$");

            if (!match.Success)
                return fallback;

            double value = double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture);
            string unit = match.Groups[2].Value.ToLowerInvariant();

            if (unit == "" || unit == "px")
                return value;

            if (unit == "mm")
                return value * 96 / 25.4;

            if (unit == "cm")
                return value * 96 / 2.54;

            if (unit == "in")
                return value * 96;

            if (unit == "pt")
                return value * 96 / 72;

            if (unit == "pc")
                return value * 16;

            if (unit == "um")
                return value * 96 / 25400;

            //A font's size, which a drawing almost never measures in; the browser's default sixteen pixels.
            if (unit == "em")
                return value * 16;

            if (unit == "ex")
                return value * 8;

            return fallback;
        }

        private static double number(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return pixels(text, 0);
        }

        private static List<double> numbers(string text)
        {
            var found = new List<double>();
            var tokens = new PathTokens(text);

            while (tokens.More)
            {
                if (tokens.Number() is not double value)
                    break;

                found.Add(value);
            }

            return found;
        }

        #endregion **************************************************************************
    }
}
