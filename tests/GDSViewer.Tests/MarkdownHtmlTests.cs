using GDSViewer.Models;

namespace GDSViewer.Tests;

///
///Covers MarkdownHtml: a PDK's README turned into the page the PDK manager shows. What a README is written in comes out
///as its tags, and **nothing in one can put markup or a script on the page** - it arrives in somebody's zip, so every
///character is escaped and a link goes only to http, https or mailto.
///
public class MarkdownHtmlTests
{
    [Fact]
    public void Headings_paragraphs_and_rules_are_their_tags()
    {
        string html = MarkdownHtml.ToHtml("# SKY130\n\nThe open PDK,\nfrom SkyWater.\n\n---\n\n## Layers");

        Assert.Equal("<h1>SKY130</h1><p>The open PDK, from SkyWater.</p><hr><h2>Layers</h2>", html);
    }

    [Fact]
    public void Bold_italics_and_code_inside_a_line()
    {
        string html = MarkdownHtml.ToHtml("**Five** metals, *one* `li1`, and sky130_fd_sc_hd keeps its underscores");

        Assert.Equal("<p><strong>Five</strong> metals, <em>one</em> <code>li1</code>, and sky130_fd_sc_hd keeps its underscores</p>", html);
    }

    ///<summary>Nothing inside a code span is read as Markdown: its asterisks stay asterisks.</summary>
    [Fact]
    public void A_code_span_is_left_as_it_is()
    {
        Assert.Equal("<p><code>a **b** c</code></p>", MarkdownHtml.ToHtml("`a **b** c`"));
    }

    [Fact]
    public void A_list_inside_a_list_is_a_list()
    {
        string html = MarkdownHtml.ToHtml("- Metals\n  - met1\n  - met2\n- Vias\n\n1. one\n2. two");

        Assert.Equal("<ul><li>Metals<ul><li>met1</li><li>met2</li></ul></li><li>Vias</li></ul><ol><li>one</li><li>two</li></ol>", html);
    }

    [Fact]
    public void A_table_is_a_table()
    {
        string html = MarkdownHtml.ToHtml("| Layer | GDS |\n|---|---|\n| `met1` | 68/20 |");

        Assert.Equal("<table><thead><tr><th>Layer</th><th>GDS</th></tr></thead><tbody><tr><td><code>met1</code></td><td>68/20</td></tr></tbody></table>", html);
    }

    [Fact]
    public void Fenced_code_and_quotes()
    {
        Assert.Equal("<pre><code>rule m1.1 width met1 140\n# &lt;kept&gt;</code></pre>", MarkdownHtml.ToHtml("```\nrule m1.1 width met1 140\n# <kept>\n```"));
        Assert.Equal("<blockquote><p>Not for production.</p></blockquote>", MarkdownHtml.ToHtml("> Not for production."));
    }

    [Fact]
    public void A_link_opens_on_its_own()
    {
        string html = MarkdownHtml.ToHtml("[the docs](https://skywater-pdk.readthedocs.io/en/main/)");

        Assert.Equal("<p><a href=\"https://skywater-pdk.readthedocs.io/en/main/\" target=\"_blank\" rel=\"noopener noreferrer\">the docs</a></p>", html);
    }

    ///
    ///**A README cannot put anything on the page but its words**: markup is shown as the text it is, and a link to
    ///anything but http, https or mailto - javascript: above all - is its words and no link.
    ///
    [Theory]
    [InlineData("<script>alert(1)</script>", "<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>")]
    [InlineData("<img src=x onerror=alert(1)>", "<p>&lt;img src=x onerror=alert(1)&gt;</p>")]
    [InlineData("[click](javascript:alert(1))", "<p>click)</p>")]
    [InlineData("[click](data:text/html,hi)", "<p>click</p>")]
    [InlineData("[q](https://a.example/\"onmouseover=\"x)", "<p><a href=\"https://a.example/&quot;onmouseover=&quot;x\" target=\"_blank\" rel=\"noopener noreferrer\">q</a></p>")]
    public void Nothing_in_a_README_becomes_markup_or_a_script(string markdown, string html)
    {
        Assert.Equal(html, MarkdownHtml.ToHtml(markdown));
    }

    ///
    ///**A picture is shown only from what the README carries** (2026-11-01): its address is the caller's, from the path
    ///named, and one the caller has nothing for is its words - so a README cannot reach off the page, or past its zip.
    ///
    [Fact]
    public void A_picture_is_shown_from_the_address_given_for_its_path_alone()
    {
        var carried = new Dictionary<string, string> { ["images/stack.svg"] = "data:image/svg+xml;base64,AAAA" };
        string html = MarkdownHtml.ToHtml("![The stack](images/stack.svg) and ![gone](https://tracker.example/x.png)", path => carried.GetValueOrDefault(path));

        Assert.Equal("<p><img src=\"data:image/svg+xml;base64,AAAA\" alt=\"The stack\" loading=\"lazy\"> and gone</p>", html);
        Assert.Equal("<p>The stack</p>", MarkdownHtml.ToHtml("![The stack](images/stack.svg)"));
        Assert.Equal(new[] { "images/stack.svg", "b.png" }, MarkdownHtml.PicturesIn("![a](images/stack.svg)\n![b](b.png \"title\") [a link](c.png) ![again](images/stack.svg)"));
    }

    ///<summary>The alternative text is escaped as every other word is.</summary>
    [Fact]
    public void A_pictures_words_are_words()
    {
        string html = MarkdownHtml.ToHtml("![\"><script>x</script>](a.png)", path => "data:image/png;base64,AAAA");

        Assert.DoesNotContain("<script", html);
        Assert.Contains("alt=\"&quot;&gt;&lt;script&gt;x&lt;/script&gt;\"", html);
    }

    [Theory]
    [InlineData("stack.svg", true)]
    [InlineData("images/stack.png", true)]
    [InlineData("../stack.png", false)]
    [InlineData("images/../../stack.png", false)]
    [InlineData("/etc/stack.png", false)]
    [InlineData("C:/stack.png", false)]
    [InlineData("https://a.example/stack.png", false)]
    [InlineData("stack.exe", false)]
    public void A_pictures_path_stays_beside_the_README(string path, bool kept)
    {
        Assert.Equal(kept, PdkReadme.IsPicturePath(path));
    }

    ///<summary>The README that ships with sky130 reads through without a line lost to a tag left open.</summary>
    [Fact]
    public void The_shipped_sky130_README_renders()
    {
        string path = Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", "resources", "GDS Files", "sky130-README.md");
        string html = MarkdownHtml.ToHtml(File.ReadAllText(path));

        Assert.StartsWith("<h1>SkyWater SKY130</h1>", html);
        Assert.Contains("<td><code>met1</code></td><td>68/20</td>", html);
        Assert.Contains("<li><code>sky130_fd_sc_hd</code>", html);
        Assert.Equal(count(html, "<table>"), count(html, "</table>"));
        Assert.Equal(count(html, "<ul>"), count(html, "</ul>"));
        Assert.DoesNotContain("<script", html);

        //Its pictures, each one beside it.
        var pictures = MarkdownHtml.PicturesIn(File.ReadAllText(path));

        Assert.Equal(3, pictures.Count);

        foreach (string picture in pictures)
        {
            Assert.True(PdkReadme.IsPicturePath(picture), picture);
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, picture)), picture);
        }
    }

    ///<summary>The README that ships with gf180mcu (2026-11-02) the same way, with its one picture, the PDK's cross-section, beside it.</summary>
    [Fact]
    public void The_shipped_gf180mcu_README_renders()
    {
        string path = Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", "resources", "GDS Files", "gf180mcu-README.md");
        string html = MarkdownHtml.ToHtml(File.ReadAllText(path));

        Assert.StartsWith("<h1>GlobalFoundries GF180MCU</h1>", html);
        Assert.Contains("<td><code>Metal1</code></td><td>34/0</td>", html);
        Assert.Contains("<td><code>Metal5</code></td><td>81/0</td>", html);
        Assert.Contains("<li><code>gf180mcu_fd_sc_mcu7t5v0</code>", html);
        Assert.Equal(count(html, "<table>"), count(html, "</table>"));
        Assert.Equal(count(html, "<ul>"), count(html, "</ul>"));
        Assert.DoesNotContain("<script", html);

        var picture = Assert.Single(MarkdownHtml.PicturesIn(File.ReadAllText(path)));

        Assert.True(PdkReadme.IsPicturePath(picture), picture);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, picture)), picture);
    }

    ///<summary>And ihp-sg13g2's (2026-11-02), with its one picture, the stack drawn to scale from the process specification, beside it.</summary>
    [Fact]
    public void The_shipped_ihp_sg13g2_README_renders()
    {
        string path = Path.Combine(GdsTestData.RepositoryRoot, "wwwroot", "resources", "GDS Files", "ihp-sg13g2-README.md");
        string html = MarkdownHtml.ToHtml(File.ReadAllText(path));

        Assert.StartsWith("<h1>IHP SG13G2</h1>", html);
        Assert.Contains("<td><code>Metal1</code></td><td>8/0</td>", html);
        Assert.Contains("<td><code>TopMetal2</code></td><td>134/0</td>", html);
        Assert.Contains("<li><code>sg13g2_stdcell</code>", html);
        Assert.Equal(count(html, "<table>"), count(html, "</table>"));
        Assert.Equal(count(html, "<ul>"), count(html, "</ul>"));
        Assert.DoesNotContain("<script", html);

        var picture = Assert.Single(MarkdownHtml.PicturesIn(File.ReadAllText(path)));

        Assert.True(PdkReadme.IsPicturePath(picture), picture);
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, picture)), picture);
    }

    private static int count(string text, string part)
    {
        int found = 0;
        int at = 0;

        while ((at = text.IndexOf(part, at, StringComparison.Ordinal)) >= 0)
        {
            found++;
            at += part.Length;
        }

        return found;
    }
}
