using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace GDSViewer.Models
{
    ///
    ///Markdown as HTML, for a PDK's README in the manager - asked for 2026-11-01. The part of Markdown a README is written
    ///in: headings, paragraphs, lists inside lists, quotes, fenced code, tables, rules, and bold, italics, code, links
    ///and pictures inside a line.
    ///
    ///**Safe to put on the page as it comes**: a README arrives in somebody's zip, so every character of it is escaped
    ///before any tag is made, the only tags are the ones made here, and a link goes only to http, https or mailto - any
    ///other address, javascript: among them, is left as its words. Nothing in a README can put markup or a script on the
    ///page. **A picture is shown only from what the README carries** - its address is the caller's, from the path the
    ///README names, so a README cannot reach off the page for one; a picture it does not carry is its alternative text.
    ///
    ///Not CommonMark: no HTML passed through, no reference links, no setext headings. A README that leans on those still
    ///reads, as the words they are.
    ///
    public static class MarkdownHtml
    {
        public static string ToHtml(string markdown)
        {
            return ToHtml(markdown, null);
        }

        ///<param name="pictures">The address each picture named is shown from, or null where there is none to show - see PdkReadme.PictureUrl.</param>
        public static string ToHtml(string markdown, Func<string, string?>? pictures)
        {
            var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var html = new StringBuilder();

            blocks(lines, html, pictures);

            return html.ToString();
        }

        private static readonly Regex PictureNamed = new Regex(@"!\[[^\]]*\]\(([^)\s]+)(?:\s+""[^)]*"")?\)");

        ///<summary>The paths of the pictures a README names, in order and once each - what a zip it came in is searched for.</summary>
        public static List<string> PicturesIn(string markdown)
        {
            return PictureNamed.Matches(markdown).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        }

        private static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex Rule = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$");
        private static readonly Regex Item = new Regex(@"^(\s*)([-*+]|\d{1,9}[.)])\s+(.*)$");
        private static readonly Regex TableDivider = new Regex(@"^\s*\|?\s*:?-{1,}:?\s*(\|\s*:?-{1,}:?\s*)*\|?\s*$");

        private static void blocks(IReadOnlyList<string> lines, StringBuilder html, Func<string, string?>? pictures)
        {
            int i = 0;

            while (i < lines.Count)
            {
                string line = lines[i];

                if (line.Trim().Length == 0)
                {
                    i++;

                    continue;
                }

                if (line.TrimStart().StartsWith("```"))
                {
                    i = fence(lines, i, html);

                    continue;
                }

                var heading = Heading.Match(line);

                if (heading.Success)
                {
                    int level = heading.Groups[1].Length;

                    html.Append($"<h{level}>{inline(heading.Groups[2].Value, pictures)}</h{level}>");
                    i++;

                    continue;
                }

                if (Rule.IsMatch(line))
                {
                    html.Append("<hr>");
                    i++;

                    continue;
                }

                if (line.TrimStart().StartsWith('>'))
                {
                    i = quote(lines, i, html, pictures);

                    continue;
                }

                if (Item.IsMatch(line))
                {
                    i = list(lines, i, html, pictures);

                    continue;
                }

                if (line.Contains('|') && i + 1 < lines.Count && TableDivider.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'))
                {
                    i = table(lines, i, html, pictures);

                    continue;
                }

                i = paragraph(lines, i, html, pictures);
            }
        }

        private static int fence(IReadOnlyList<string> lines, int start, StringBuilder html)
        {
            var code = new StringBuilder();
            int i = start + 1;

            while (i < lines.Count && !lines[i].TrimStart().StartsWith("```"))
            {
                if (code.Length > 0)
                    code.Append('\n');

                code.Append(lines[i]);
                i++;
            }

            html.Append($"<pre><code>{escape(code.ToString())}</code></pre>");

            return i + 1;
        }

        private static int quote(IReadOnlyList<string> lines, int start, StringBuilder html, Func<string, string?>? pictures)
        {
            var inner = new List<string>();
            int i = start;

            while (i < lines.Count && lines[i].TrimStart().StartsWith('>'))
            {
                string text = lines[i].TrimStart().Substring(1);

                if (text.StartsWith(' '))
                    text = text.Substring(1);

                inner.Add(text);
                i++;
            }

            html.Append("<blockquote>");
            blocks(inner, html, pictures);
            html.Append("</blockquote>");

            return i;
        }

        ///
        ///A list, and the lists inside its items: an item is its marker's line and the lines under it indented past the
        ///marker, read again as blocks - so an item holding a list holds it as a list. An item of one paragraph is its
        ///words alone, without the paragraph round them.
        ///
        private static int list(IReadOnlyList<string> lines, int start, StringBuilder html, Func<string, string?>? pictures)
        {
            var first = Item.Match(lines[start]);
            int indent = first.Groups[1].Length;
            bool ordered = char.IsDigit(first.Groups[2].Value[0]);
            string tag = "ul";

            if (ordered)
                tag = "ol";

            html.Append($"<{tag}>");

            int i = start;

            while (i < lines.Count)
            {
                var item = Item.Match(lines[i]);

                //A list goes on while its items are as indented as its first and numbered as it is, or not.
                if (!isItemAt(lines[i], indent, ordered))
                    break;

                int contentIndent = item.Groups[1].Length + item.Groups[2].Length + 1;
                var content = new List<string> { item.Groups[3].Value };

                i++;

                while (i < lines.Count)
                {
                    string next = lines[i];

                    if (next.Trim().Length == 0)
                    {
                        //A blank line ends the item unless what follows is indented into it.
                        if (i + 1 < lines.Count && leading(lines[i + 1]) > indent && lines[i + 1].Trim().Length > 0)
                        {
                            content.Add("");
                            i++;

                            continue;
                        }

                        break;
                    }

                    var sibling = Item.Match(next);

                    if (sibling.Success && sibling.Groups[1].Length <= indent)
                        break;

                    if (leading(next) <= indent && !sibling.Success)
                    {
                        //A line run on from the item's words, not indented.
                        content[content.Count - 1] += " " + next.Trim();
                        i++;

                        continue;
                    }

                    content.Add(dedent(next, Math.Min(contentIndent, leading(next))));
                    i++;
                }

                html.Append("<li>");

                if (content.Count == 1)
                {
                    html.Append(inline(content[0], pictures));
                }
                else
                {
                    var inner = new StringBuilder();

                    blocks(content, inner, pictures);

                    html.Append(unwrapFirstParagraph(inner.ToString()));
                }

                html.Append("</li>");

                //A blank line between two items keeps them in one list.
                while (i + 1 < lines.Count && lines[i].Trim().Length == 0 && isItemAt(lines[i + 1], indent, ordered))
                    i++;
            }

            html.Append($"</{tag}>");

            return i;
        }

        private static bool isItemAt(string line, int indent, bool ordered)
        {
            var item = Item.Match(line);

            return item.Success && item.Groups[1].Length == indent && char.IsDigit(item.Groups[2].Value[0]) == ordered;
        }

        ///<summary>An item's first paragraph as its words, so a tight item with a list under it does not gain a gap.</summary>
        private static string unwrapFirstParagraph(string inner)
        {
            if (!inner.StartsWith("<p>"))
                return inner;

            int end = inner.IndexOf("</p>", StringComparison.Ordinal);

            return inner.Substring(3, end - 3) + inner.Substring(end + 4);
        }

        private static int table(IReadOnlyList<string> lines, int start, StringBuilder html, Func<string, string?>? pictures)
        {
            html.Append("<table><thead><tr>");

            foreach (string cell in cells(lines[start]))
                html.Append($"<th>{inline(cell, pictures)}</th>");

            html.Append("</tr></thead><tbody>");

            int i = start + 2;

            while (i < lines.Count && lines[i].Contains('|') && lines[i].Trim().Length > 0)
            {
                html.Append("<tr>");

                foreach (string cell in cells(lines[i]))
                    html.Append($"<td>{inline(cell, pictures)}</td>");

                html.Append("</tr>");
                i++;
            }

            html.Append("</tbody></table>");

            return i;
        }

        private static List<string> cells(string line)
        {
            string row = line.Trim();

            if (row.StartsWith('|'))
                row = row.Substring(1);

            if (row.EndsWith('|'))
                row = row.Substring(0, row.Length - 1);

            return row.Split('|').Select(cell => cell.Trim()).ToList();
        }

        private static int paragraph(IReadOnlyList<string> lines, int start, StringBuilder html, Func<string, string?>? pictures)
        {
            var words = new List<string>();
            int i = start;

            while (i < lines.Count && lines[i].Trim().Length > 0 && !startsBlock(lines, i))
            {
                words.Add(lines[i].Trim());
                i++;
            }

            //A line that starts a block of its own and could not be one - a lone pipe, say - is still read, as words.
            if (i == start)
            {
                words.Add(lines[i].Trim());
                i++;
            }

            html.Append($"<p>{inline(string.Join(" ", words), pictures)}</p>");

            return i;
        }

        private static bool startsBlock(IReadOnlyList<string> lines, int i)
        {
            string line = lines[i];

            return line.TrimStart().StartsWith("```")
                || Heading.IsMatch(line)
                || Rule.IsMatch(line)
                || line.TrimStart().StartsWith('>')
                || Item.IsMatch(line)
                || (line.Contains('|') && i + 1 < lines.Count && TableDivider.IsMatch(lines[i + 1]) && lines[i + 1].Contains('-'));
        }

        private static readonly Regex CodeSpan = new Regex(@"`([^`]+)`");
        private static readonly Regex Picture = new Regex(@"!\[([^\]]*)\]\(([^)\s]+)(?:\s+&quot;[^)]*&quot;)?\)");
        private static readonly Regex Link = new Regex(@"\[([^\]]+)\]\(([^)\s]+)(?:\s+&quot;[^)]*&quot;)?\)");
        private static readonly Regex Bold = new Regex(@"\*\*(\S(?:.*?\S)?)\*\*|__(\S(?:.*?\S)?)__");
        private static readonly Regex Italic = new Regex(@"(?<![\w*])\*(\S(?:.*?\S)?)\*(?![\w*])");

        ///
        ///A line's words: escaped first, then code spans set aside so nothing inside one is read as Markdown, then pictures,
        ///links, bold and italics. Italics by asterisks alone - an underscore is in too many names, sky130_fd_sc_hd among them.
        ///
        private static string inline(string text, Func<string, string?>? pictures)
        {
            string escaped = escape(text);
            var spans = new List<string>();

            escaped = CodeSpan.Replace(escaped, match =>
            {
                spans.Add($"<code>{match.Groups[1].Value}</code>");

                return $"\u0001{spans.Count - 1}\u0001";
            });

            //Before the links, which would take a picture's [words](path) for one. Set aside as the code spans are, so its
            //address is not read as Markdown either.
            escaped = Picture.Replace(escaped, match =>
            {
                string path = WebUtility.HtmlDecode(match.Groups[2].Value);
                string? address = pictures?.Invoke(path);

                if (address is null)
                    return match.Groups[1].Value;

                spans.Add($"<img src=\"{escape(address)}\" alt=\"{match.Groups[1].Value}\" loading=\"lazy\">");

                return $"\u0001{spans.Count - 1}\u0001";
            });

            escaped = Link.Replace(escaped, match =>
            {
                string address = WebUtility.HtmlDecode(match.Groups[2].Value);

                if (!safeAddress(address))
                    return match.Groups[1].Value;

                return $"<a href=\"{escape(address)}\" target=\"_blank\" rel=\"noopener noreferrer\">{match.Groups[1].Value}</a>";
            });

            escaped = Bold.Replace(escaped, match => $"<strong>{match.Groups[1].Value}{match.Groups[2].Value}</strong>");
            escaped = Italic.Replace(escaped, match => $"<em>{match.Groups[1].Value}</em>");

            return Regex.Replace(escaped, "\u0001(\\d+)\u0001", match => spans[int.Parse(match.Groups[1].Value)]);
        }

        private static bool safeAddress(string address)
        {
            return address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || address.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private static string escape(string text)
        {
            return WebUtility.HtmlEncode(text);
        }

        private static int leading(string line)
        {
            int count = 0;

            while (count < line.Length && line[count] == ' ')
                count++;

            return count;
        }

        private static string dedent(string line, int by)
        {
            int cut = Math.Min(by, leading(line));

            return line.Substring(cut);
        }
    }
}
