using System.Globalization;
using System.Net;
using System.Text;

namespace GdsII
{
    public partial class LayerNames
    {
        #region KLayout layer properties ****************************************************

        ///<summary>
        ///Whether this text is a KLayout layer-properties file - a .lyp - rather than a CSV: by the angle bracket every
        ///XML document starts with, which no row of a CSV can. Past a byte order mark and any blank space.
        ///</summary>
        private static bool isLayerProperties(string text)
        {
            foreach (char character in text)
            {
                if (character == '\uFEFF' || char.IsWhiteSpace(character))
                    continue;

                return character == '<';
            }

            return false;
        }

        ///
        ///A KLayout layer-properties file read into this mapping: each layer's name, and the color KLayout fills it with.
        ///
        ///**What a .lyp has that a layermap can hold**, and no more. It has no heights and no roles, and its stipples are
        ///KLayout's own bitmaps rather than anything drawn here - so a .lyp names and colors a layout, and the CSV columns
        ///do the rest.
        ///
        ///**Read the way KLayout reads it**, which LayerNamesTests checks against KLayout itself:
        ///
        ///- **An entry is a node naming one layer and data type**: `68/20@1`, `met1 68/20`, or `68`, which is data type 0.
        ///A wildcard is not one layer, a group is not drawn, and a source on another layout than the first - `@2` - is
        ///about some other file. A member does not take its group's layout: KLayout reads `1/0` under `*/*@2` as on the
        ///first.
        ///- **A group's color is its members'**: the outermost node on the way down that gives a fill color is the color,
        ///whatever a member says itself - whichever comes first in the file - and the brightness along the way adds up.
        ///See brighter.
        ///- **The first tab** of a file that has several, the one listed first.
        ///- A pair given twice takes the last, as a CSV row does.
        ///
        ///**A name ending in the entry's own pair has it taken off** - sky130's are `met1drawing_m 68/20` - since every
        ///label here shows the pair already. A line break, which a label has nowhere to put, is a space; a comma stays,
        ///since the row the name is later stored in quotes it - see csvField.
        ///
        ///**Its own small reader rather than the framework's XML**, for the reason <see cref="DrcReport"/> gives for its
        ///own: this library is compiled into the app, and System.Xml is a megabyte of it. A .lyp needs elements and their
        ///text, and no attributes at all.
        ///
        private void readLayerProperties(string xml)
        {
            var reading = new LayerPropertiesReading();

            reading.Scan(xml);

            if (!reading.SawRoot)
            {
                report("This is not a KLayout layer-properties file: there is no <layer-properties> in it.");

                return;
            }

            foreach (var node in reading.Nodes)
                resolve(node, null, 0);
        }

        ///<summary>One node and everything under it, as entries: with the color from above it if there was one, and the brightness so far.</summary>
        private void resolve(PropertiesNode node, int? color, int brightness)
        {
            int? own = readColor(node.FillColor);

            //The outermost color on the way down is the one drawn.
            if (color is null)
                color = own;

            brightness += readBrightness(node.FillBrightness);

            if (node.Members.Count > 0)
            {
                foreach (var member in node.Members)
                    resolve(member, color, brightness);

                return;
            }

            if (!tryReadSource(node.Source, out LayerKey key))
                return;

            string name = layerPropertiesName(node.Name, key);

            if (name.Length > 0)
                Names[key] = name;

            if (color is int fill)
                Colors[key] = "#" + brighter(fill, brightness).ToString("x6", CultureInfo.InvariantCulture);
        }

        ///
        ///The layer and data type a KLayout source names, if it names exactly one on the first layout.
        ///
        ///A source is words: a name, the layer and data type with the layout after an `@`, then a transformation and the
        ///rest, none of which matter here. The pair is the first word that reads as one - so `met1 68/20@1` is 68/20 - and
        ///a layer alone is data type 0, as KLayout takes it.
        ///
        private static bool tryReadSource(string source, out LayerKey key)
        {
            key = default;

            foreach (string word in source.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!char.IsAsciiDigit(word[0]))
                    continue;

                string pair = word;
                string layout = "";
                int at = word.IndexOf('@');

                if (at >= 0)
                {
                    pair = word[..at];
                    layout = word[(at + 1)..];
                }

                string layer = pair;
                string dataType = "0";
                int slash = pair.IndexOf('/');

                if (slash >= 0)
                {
                    layer = pair[..slash];
                    dataType = pair[(slash + 1)..];
                }

                //A word that starts with a digit and is not a pair - `1st`, or `78-79` - is a name, and the pair may follow.
                if (!tryParseNumber(layer, out short number) || !tryParseNumber(dataType, out short type))
                    continue;

                //The first layout, or every one: `@*` is drawn on each layout open, this one among them.
                if (layout.Length > 0 && layout != "1" && layout != "*")
                    return false;

                key = new LayerKey(number, type);

                return true;
            }

            return false;
        }

        ///<summary>A name as a label shows it: on one line, and without the pair it ends in when that is its own.</summary>
        private static string layerPropertiesName(string name, LayerKey key)
        {
            var kept = new StringBuilder(name.Length);

            foreach (char character in name)
            {
                if (char.IsControl(character))
                    kept.Append(' ');
                else
                    kept.Append(character);
            }

            string named = kept.ToString().Trim();
            string pair = key.Number.ToString(CultureInfo.InvariantCulture) + "/" + key.DataType.ToString(CultureInfo.InvariantCulture);

            foreach (string ending in new[] { "(" + pair + ")", pair })
            {
                if (!named.EndsWith(ending, StringComparison.Ordinal))
                    continue;

                string rest = named[..^ending.Length];

                //Its own word, after a space or a dash: `met1 68/20` ends in its pair, and `x168/20` does not.
                if (rest.Length > 0 && !char.IsWhiteSpace(rest[^1]) && rest[^1] != '-')
                    continue;

                return rest.TrimEnd().TrimEnd('-').TrimEnd();
            }

            return named;
        }

        ///<summary>A KLayout color, `#rrggbb` - or null for none, and for one that is not a color, which is reported.</summary>
        private int? readColor(string text)
        {
            string color = text.Trim();

            if (color.Length == 0)
                return null;

            if (color.Length == 7 && color[0] == '#' && int.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return rgb;

            report($"\"{color}\" is not a color, so the layers it was for keep their own.");

            return null;
        }

        ///<summary>A fill brightness, as KLayout writes it: a whole number, zero for none. One that is not a number is reported and left out.</summary>
        private int readBrightness(string text)
        {
            string brightness = text.Trim();

            if (brightness.Length == 0)
                return 0;

            if (int.TryParse(brightness, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                return value;

            report($"\"{brightness}\" is not a brightness, so it was left out.");

            return 0;
        }

        ///
        ///A color made brighter or darker by a KLayout fill brightness, the way KLayout does it: each channel moved a
        ///share of the way to white, or to black for a brightness below zero, and the share halving every 128.
        ///
        ///**In whole 256ths, as KLayout works it** - the share rounded to one, and each channel rounded down - rather
        ///than the smooth curve this is an approximation of, so the color is the one KLayout draws to the last bit. Found
        ///by asking KLayout for over nine hundred colors and brightnesses, and checked against it in LayerNamesTests.
        ///
        private static int brighter(int rgb, int brightness)
        {
            if (brightness == 0)
                return rgb;

            int share = (int)Math.Floor(0.5 + (256.0 * Math.Pow(2.0, -Math.Abs(brightness) / 128.0)));
            int brightened = 0;

            foreach (int shift in new[] { 16, 8, 0 })
            {
                int channel = (rgb >> shift) & 0xff;

                if (brightness < 0)
                    channel = (channel * share) >> 8;
                else
                    channel = 255 - (((255 - channel) * share) >> 8);

                brightened |= channel << shift;
            }

            return brightened;
        }

        ///<summary>A node of a .lyp as it was written - a layer, or a group with its members - before any of it is worked out.</summary>
        private sealed class PropertiesNode
        {
            public string Name { get; set; } = "";
            public string Source { get; set; } = "";
            public string FillColor { get; set; } = "";
            public string FillBrightness { get; set; } = "";
            public List<PropertiesNode> Members { get; } = new List<PropertiesNode>();
        }

        ///
        ///A .lyp walked tag by tag into the nodes of its first tab, the way DrcReport reads a report: the elements open
        ///around the scan, and the text since the last tag. Built into nodes before anything is worked out, since a group
        ///can give its color after its members.
        ///
        private sealed class LayerPropertiesReading
        {
            ///<summary>The nodes at the top of the first tab, in order.</summary>
            public List<PropertiesNode> Nodes { get; } = new List<PropertiesNode>();

            ///<summary>Whether there was a layer-properties element at all, which every .lyp has.</summary>
            public bool SawRoot { get; private set; }

            private readonly List<string> open = new List<string>();
            private readonly List<PropertiesNode> nodes = new List<PropertiesNode>();
            private readonly StringBuilder text = new StringBuilder();

            //Which tab the scan is in, counting from one, for a file of several - and whether it is the one read.
            private int tab;
            private bool tabbed;

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

                        //Encoded, so the decoding every text gets gives back exactly what was in it.
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
            }

            ///<summary>Whether the scan is inside the tab being read: the only one in a plain file, or the first of several.</summary>
            private bool readingThisTab()
            {
                return !tabbed || tab == 1;
            }

            private void started(string name)
            {
                string parent = "";

                if (open.Count > 0)
                    parent = open[^1];

                open.Add(name);
                text.Clear();

                if (name == "layer-properties-tabs" && open.Count == 1)
                {
                    tabbed = true;

                    return;
                }

                if (name == "layer-properties" && (open.Count == 1 || parent == "layer-properties-tabs"))
                {
                    SawRoot = true;

                    if (tabbed)
                        tab++;

                    return;
                }

                if (!readingThisTab())
                    return;

                bool topLevel = name == "properties" && parent == "layer-properties";
                bool member = name == "group-members" && (parent == "properties" || parent == "group-members") && nodes.Count > 0;

                if (!topLevel && !member)
                    return;

                var node = new PropertiesNode();

                if (topLevel)
                    Nodes.Add(node);
                else
                    nodes[^1].Members.Add(node);

                nodes.Add(node);
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

                text.Clear();
                open.RemoveAt(open.Count - 1);

                if (!readingThisTab())
                    return;

                if (name == "properties" || name == "group-members")
                {
                    if (nodes.Count > 0 && open.Count > 0 && isNode(open[^1], name))
                        nodes.RemoveAt(nodes.Count - 1);

                    return;
                }

                //A field of the node it is directly inside.
                if (nodes.Count == 0 || open.Count == 0 || (open[^1] != "properties" && open[^1] != "group-members"))
                    return;

                var node = nodes[^1];

                if (name == "name")
                    node.Name = value;
                else if (name == "source")
                    node.Source = value;
                else if (name == "fill-color")
                    node.FillColor = value;
                else if (name == "fill-brightness")
                    node.FillBrightness = value;
            }

            ///<summary>Whether an element by this name, closing inside this parent, was one of the nodes started.</summary>
            private static bool isNode(string parent, string name)
            {
                if (name == "properties")
                    return parent == "layer-properties";

                return parent == "properties" || parent == "group-members";
            }

            private static string decoded(string value)
            {
                if (!value.Contains('&'))
                    return value;

                return WebUtility.HtmlDecode(value);
            }

            private static int past(string xml, string what, int from)
            {
                int end = xml.IndexOf(what, from, StringComparison.Ordinal);

                if (end < 0)
                    return xml.Length;

                return end + what.Length;
            }
        }

        #endregion **************************************************************************
    }
}
