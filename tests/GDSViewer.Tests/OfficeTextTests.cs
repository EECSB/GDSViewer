using System.IO.Compression;
using System.Text;
using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///An Excel workbook and a Word document read out as the words a model is sent - see OfficeText. Each file is built here
///as the zip of XML parts it is, holding what Excel and Word write: shared strings and runs, styles naming number
///formats, rows and cells that leave their positions out, tracked changes, fields, text boxes written two ways.
///
///Read against real files too, on 2026-10-30: eight workbooks Excel wrote came out cell for cell as openpyxl reads them.
///
public class OfficeTextTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private const string Package = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    ///<summary>A zip of the parts given, by their paths.</summary>
    private static byte[] Zip(params (string Path, string Xml)[] parts)
    {
        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (path, xml) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));

                writer.Write(xml);
            }
        }

        return memory.ToArray();
    }

    private static string Rels(params (string Id, string Type, string Target)[] relations)
    {
        var xml = new StringBuilder($"<Relationships xmlns=\"{Package}\">");

        foreach (var (id, type, target) in relations)
            xml.Append($"<Relationship Id=\"{id}\" Type=\"{Relations}/{type}\" Target=\"{target}\"/>");

        return xml.Append("</Relationships>").ToString();
    }

    ///
    ///A workbook of the sheets given, each its sheetData's rows and named Sheet1, Sheet2..., with shared strings and
    ///cell styles when they are given. A sheet named with a leading ! is hidden.
    ///
    private static byte[] Workbook(string[] sheets, string[]? strings = null, string styles = "", bool date1904 = false, string[]? names = null)
    {
        var parts = new List<(string, string)>();
        var book = new StringBuilder($"<workbook xmlns=\"{Main}\" xmlns:r=\"{Relations}\">");
        var relations = new List<(string, string, string)>();

        if (date1904)
            book.Append("<workbookPr date1904=\"1\"/>");

        book.Append("<sheets>");

        for (int i = 0; i < sheets.Length; i++)
        {
            string name = names?[i] ?? $"Sheet{i + 1}";
            string state = "";

            if (name.StartsWith('!'))
            {
                name = name.Substring(1);
                state = " state=\"hidden\"";
            }

            book.Append($"<sheet name=\"{name}\" sheetId=\"{i + 1}\"{state} r:id=\"rId{i + 1}\"/>");
            relations.Add(($"rId{i + 1}", "worksheet", $"worksheets/sheet{i + 1}.xml"));
            parts.Add(($"xl/worksheets/sheet{i + 1}.xml", $"<worksheet xmlns=\"{Main}\" xmlns:r=\"{Relations}\"><sheetData>{sheets[i]}</sheetData></worksheet>"));
        }

        book.Append("</sheets></workbook>");

        if (strings is not null)
        {
            relations.Add(("rIdS", "sharedStrings", "sharedStrings.xml"));
            parts.Add(("xl/sharedStrings.xml", $"<sst xmlns=\"{Main}\">{string.Concat(strings.Select(each => $"<si>{each}</si>"))}</sst>"));
        }

        if (styles.Length > 0)
        {
            relations.Add(("rIdT", "styles", "styles.xml"));
            parts.Add(("xl/styles.xml", $"<styleSheet xmlns=\"{Main}\">{styles}</styleSheet>"));
        }

        parts.Add(("_rels/.rels", Rels(("rId1", "officeDocument", "xl/workbook.xml"))));
        parts.Add(("xl/workbook.xml", book.ToString()));
        parts.Add(("xl/_rels/workbook.xml.rels", Rels(relations.ToArray())));

        return Zip(parts.ToArray());
    }

    private static string Header(string name, string hidden = "")
    {
        return $"Sheet \"{name}\"{hidden} - each line is a row: its number, then its cells, separated by tabs.\n";
    }

    #region A workbook ************************************************************

    ///
    ///Every sheet a row to a line, its number first and its cells between tabs: strings shared, rich and inline, numbers
    ///to Excel's fifteen digits, a cell or a row with no position being the next one, empty cells kept between and left
    ///off the end, an empty row left out, a formula with no result given as itself, and a hidden sheet said to be one.
    ///
    [Fact]
    public void Every_sheet_is_a_row_to_a_line_with_its_number_first_and_its_cells_between_tabs()
    {
        string[] strings =
        {
            "<t>Rule</t>",
            "<r><t>Width </t></r><r><rPr><b/></rPr><t>(µm)</t></r>",
            "<t>met1.1</t><rPh sb=\"0\" eb=\"1\"><t>メタル</t></rPh>",
            "<t xml:space=\"preserve\">two\tlines\nhere</t>"
        };

        string first = "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c><c r=\"C1\" t=\"inlineStr\"><is><t>Note</t></is></c></row>"
            + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\"><v>0.14000000000000001</v></c><c r=\"C2\" t=\"str\"><f>A2&amp;\"!\"</f><v>met1.1!</v></c></row>"
            + "<row r=\"4\"><c r=\"A4\" t=\"b\"><v>1</v></c><c r=\"D4\"><v>3</v></c><c r=\"E4\"/></row>"
            + "<row r=\"5\"><c r=\"A5\"/></row>"
            + "<row><c><v>7</v></c><c t=\"e\"><v>#DIV/0!</v></c></row>"
            + "<row r=\"9\"><c r=\"B9\"><f>SUM(B2:B4)</f></c><c r=\"C9\" t=\"s\"><v>3</v></c></row>";

        string second = "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>kept</t></is></c></row>";

        var (text, cut) = OfficeText.Workbook(Workbook(new[] { first, second }, strings, names: new[] { "Rules", "!Old" }), 10_000);

        Assert.False(cut);
        Assert.Equal(Header("Rules")
            + "1\tRule\tWidth (µm)\tNote\n"
            + "2\tmet1.1\t0.14\tmet1.1!\n"
            + "4\tTRUE\t\t\t3\n"
            + "6\t7\t#DIV/0!\n"
            + "9\t\t=SUM(B2:B4)\ttwo lines here\n"
            + "\n"
            + Header("Old", " (hidden)")
            + "1\tkept\n"
            + "\n", text);
    }

    ///
    ///A number reads as what its format makes it mean: a date or a time from Excel's count of days, Excel's own formats
    ///by number and a file's by their codes; a percentage times a hundred; and the rest as the number, whatever
    ///decimals, words or colors the format adds round it.
    ///
    [Fact]
    public void A_number_reads_as_a_date_a_time_or_a_percentage_where_its_format_makes_it_one()
    {
        string styles = "<numFmts>"
            + "<numFmt numFmtId=\"164\" formatCode=\"yyyy-mm-dd hh:mm\"/>"
            + "<numFmt numFmtId=\"165\" formatCode=\"0.0%\"/>"
            + "<numFmt numFmtId=\"166\" formatCode=\"[h]:mm:ss\"/>"
            + "<numFmt numFmtId=\"167\" formatCode=\"#,##0.00 &quot;days&quot;\"/>"
            + "<numFmt numFmtId=\"168\" formatCode=\"[Red]0.00;[Blue]-0.00\"/>"
            + "<numFmt numFmtId=\"169\" formatCode=\"mm:ss\"/>"
            + "</numFmts><cellXfs>"
            + "<xf numFmtId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"164\"/><xf numFmtId=\"165\"/><xf numFmtId=\"166\"/>"
            + "<xf numFmtId=\"167\"/><xf numFmtId=\"18\"/><xf numFmtId=\"10\"/><xf numFmtId=\"168\"/><xf numFmtId=\"169\"/>"
            + "</cellXfs>";

        string rows = "<row r=\"1\">"
            + "<c r=\"A1\" s=\"1\"><v>45231</v></c>"
            + "<c r=\"B1\" s=\"2\"><v>45231.5</v></c>"
            + "<c r=\"C1\" s=\"3\"><v>0.125</v></c>"
            + "<c r=\"D1\" s=\"4\"><v>1.5</v></c>"
            + "<c r=\"E1\" s=\"5\"><v>2.5</v></c>"
            + "<c r=\"F1\" s=\"6\"><v>0.75</v></c>"
            + "<c r=\"G1\" s=\"7\"><v>0.0005</v></c>"
            + "<c r=\"H1\" s=\"8\"><v>-1.5</v></c>"
            + "<c r=\"I1\" s=\"9\"><v>0.0104166666666667</v></c>"
            + "<c r=\"J1\" s=\"0\"><v>1E+20</v></c>"
            + "</row>"
            + "<row r=\"2\"><c r=\"A2\" s=\"1\"><v>59</v></c><c r=\"B2\" s=\"1\"><v>1</v></c><c r=\"C2\" s=\"1\"><v>61</v></c></row>";

        var (text, _) = OfficeText.Workbook(Workbook(new[] { rows }, styles: styles), 10_000);

        Assert.Equal(Header("Sheet1")
            + "1\t2023-11-01\t2023-11-01 12:00\t12.5%\t36:00:00\t2.5\t18:00\t0.05%\t-1.5\t00:15\t1E+20\n"
            + "2\t1900-02-28\t1900-01-01\t1900-03-01\n"
            + "\n", text);
    }

    ///<summary>A workbook from a Mac of old counts its days from 1904, and says so.</summary>
    [Fact]
    public void A_workbook_that_counts_from_1904_says_so_and_is_read_that_way()
    {
        string styles = "<cellXfs><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs>";
        string rows = "<row r=\"1\"><c r=\"A1\" s=\"1\"><v>0</v></c><c r=\"B1\" s=\"1\"><v>43769</v></c></row>";

        var (text, _) = OfficeText.Workbook(Workbook(new[] { rows }, styles: styles, date1904: true), 10_000);

        Assert.Equal(Header("Sheet1") + "1\t1904-01-01\t2023-11-01\n\n", text);
    }

    ///
    ///A sheet is read no further than what would be sent, and says it was cut: a million rows stop being read there.
    ///The attachment says so where it stops, without a number for the rest - which was never read to be counted.
    ///
    [Fact]
    public void A_long_sheet_is_read_only_as_far_as_is_sent_and_says_it_goes_on()
    {
        var rows = new StringBuilder();

        for (int row = 1; row <= 20_000; row++)
            rows.Append($"<row r=\"{row}\"><c r=\"A{row}\" t=\"inlineStr\"><is><t>a row of words</t></is></c></row>");

        byte[] data = Workbook(new[] { rows.ToString() });
        var (text, cut) = OfficeText.Workbook(data, 1000);

        Assert.True(cut);
        Assert.Equal(1000, text.Length);

        var file = new AiAttachment("long.xlsx", "", data);
        string sent = AiAttachment.Parts(new[] { file }, LlmConnection.Anthropic).Single().Text;

        Assert.True(file.Text.Length <= AiAttachment.MostTextCharacters);
        Assert.Contains("[Cut short here. The workbook goes on past this point, and the rest was left out.]", sent);
        Assert.DoesNotContain("characters, which were left out", sent);
    }

    ///<summary>A chart on a sheet of its own has no cells, and is said to be there rather than left out.</summary>
    [Fact]
    public void A_chart_sheet_is_named_as_one()
    {
        byte[] data = Zip(
            ("_rels/.rels", Rels(("rId1", "officeDocument", "/xl/workbook.xml"))),
            ("xl/workbook.xml", $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Relations}\"><sheets><sheet name=\"Plot\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Data\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>"),
            ("xl/_rels/workbook.xml.rels", Rels(("rId1", "chartsheet", "chartsheets/sheet1.xml"), ("rId2", "worksheet", "worksheets/sheet1.xml"))),
            ("xl/chartsheets/sheet1.xml", $"<chartsheet xmlns=\"{Main}\"/>"),
            ("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{Main}\"><sheetData><row r=\"1\"><c r=\"A1\"><v>1</v></c></row></sheetData></worksheet>"));

        var (text, _) = OfficeText.Workbook(data, 10_000);

        Assert.Equal("Sheet \"Plot\" is a chart, with no cells to read.\n\n" + Header("Data") + "1\t1\n\n", text);
    }

    ///<summary>Excel's Strict format writes the same parts in namespaces of its own, and reads the same.</summary>
    [Fact]
    public void A_workbook_saved_as_strict_reads_the_same()
    {
        const string strict = "http://purl.oclc.org/ooxml/spreadsheetml/main";
        const string strictRelations = "http://purl.oclc.org/ooxml/officeDocument/relationships";

        byte[] data = Zip(
            ("_rels/.rels", $"<Relationships xmlns=\"{Package}\"><Relationship Id=\"rId1\" Type=\"{strictRelations}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>"),
            ("xl/workbook.xml", $"<workbook xmlns=\"{strict}\" xmlns:r=\"{strictRelations}\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
            ("xl/_rels/workbook.xml.rels", $"<Relationships xmlns=\"{Package}\"><Relationship Id=\"rId1\" Type=\"{strictRelations}/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>"),
            ("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{strict}\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>strict</t></is></c></row></sheetData></worksheet>"));

        var (text, _) = OfficeText.Workbook(data, 10_000);

        Assert.Equal(Header("S") + "1\tstrict\n\n", text);
    }

    ///
    ///A part that unpacks past what is read at all is refused before it is read: a zip of a few kilobytes can hold
    ///gigabytes, in a page that has to go on running.
    ///
    [Fact]
    public void A_part_that_unpacks_too_far_is_refused_unread()
    {
        var small = new[]
        {
            ("_rels/.rels", Rels(("rId1", "officeDocument", "xl/workbook.xml"))),
            ("xl/workbook.xml", $"<workbook xmlns=\"{Main}\" xmlns:r=\"{Relations}\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>"),
            ("xl/_rels/workbook.xml.rels", Rels(("rId1", "worksheet", "worksheets/sheet1.xml")))
        };

        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (path, xml) in small)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());

                writer.Write(xml);
            }

            using var sheet = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();
            byte[] spaces = Encoding.ASCII.GetBytes(new string(' ', 1 << 20));

            sheet.Write(Encoding.ASCII.GetBytes($"<worksheet xmlns=\"{Main}\"><sheetData>"));

            for (long written = 0; written <= OfficeText.MostPartBytes; written += spaces.Length)
                sheet.Write(spaces);

            sheet.Write(Encoding.ASCII.GetBytes("</sheetData></worksheet>"));
        }

        var file = new AiAttachment("bomb.xlsx", "", memory.ToArray());

        Assert.Null(AiAttachment.Problem(file.Name, file.MediaType, file.Data));
        Assert.Contains("could not be read as an Excel workbook: its xl/worksheets/sheet1.xml unpacks to 101 MB, and a part can be read at 100 MB at most", file.Unreadable);
    }

    #endregion ********************************************************************



    #region A document ************************************************************

    ///
    ///A document's paragraphs a line each and its tables a row to a line, with what the document says now: runs joined,
    ///tabs and breaks kept, words deleted with changes tracked left out and those inserted in, a field as what Word last
    ///showed, a content control's words, a text box once after its paragraph though Word writes it two ways, empty
    ///paragraphs no more than one empty line - and the page header and footer first and last, once each.
    ///
    [Fact]
    public void A_documents_paragraphs_and_tables_are_read_as_it_says_now_with_its_header_and_footer()
    {
        const string drawing = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";

        string body = "<w:p><w:r><w:t xml:space=\"preserve\">Design </w:t></w:r><w:r><w:rPr><w:b/></w:rPr><w:t>rules</w:t></w:r></w:p>"
            + "<w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"720\"/></w:tabs></w:pPr><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t><w:br/><w:t>c</w:t></w:r></w:p>"
            + "<w:p><w:del w:id=\"1\" w:author=\"x\"><w:r><w:delText>old</w:delText><w:tab/></w:r></w:del><w:ins w:id=\"2\" w:author=\"x\"><w:r><w:t>new</w:t></w:r></w:ins></w:p>"
            + "<w:p><w:moveFrom w:id=\"3\" w:author=\"x\"><w:r><w:t>moved away</w:t></w:r></w:moveFrom><w:moveTo w:id=\"4\" w:author=\"x\"><w:r><w:t>moved here</w:t></w:r></w:moveTo></w:p>"
            + "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>3</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>"
            + "<w:p><w:hyperlink r:id=\"rIdL\"><w:r><w:t>a link</w:t></w:r></w:hyperlink></w:p>"
            + "<w:p/><w:p></w:p>"
            + "<w:tbl><w:tblPr/><w:tr><w:tc><w:p><w:r><w:t>Layer</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Width</w:t></w:r></w:p></w:tc><w:tc><w:p/></w:tc></w:tr>"
            + "<w:tr><w:tc><w:p><w:r><w:t>met1</w:t></w:r></w:p><w:p><w:r><w:t>met2</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>0.14</w:t></w:r></w:p></w:tc></w:tr></w:tbl>"
            + "<w:sdt><w:sdtPr><w:alias w:val=\"Box\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>inside a control</w:t></w:r></w:p></w:sdtContent></w:sdt>"
            + "<w:p><w:r><w:t>before the box</w:t></w:r><w:r><mc:AlternateContent>"
            + "<mc:Choice Requires=\"wps\"><w:drawing><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>in the box</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></w:drawing></mc:Choice>"
            + "<mc:Fallback><w:pict><w:txbxContent><w:p><w:r><w:t>in the box</w:t></w:r></w:p></w:txbxContent></w:pict></mc:Fallback>"
            + "</mc:AlternateContent></w:r></w:p>"
            + "<w:p><w:pPr><w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/></w:sectPr></w:pPr></w:p>"
            + "<w:sectPr><w:headerReference w:type=\"default\" r:id=\"rIdH\"/><w:headerReference w:type=\"first\" r:id=\"rIdH2\"/><w:footerReference w:type=\"default\" r:id=\"rIdF\"/></w:sectPr>";

        string document = $"<w:document xmlns:w=\"{Word}\" xmlns:r=\"{Relations}\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" xmlns:wps=\"{drawing}\"><w:body>{body}</w:body></w:document>";
        string margin = $"xmlns:w=\"{Word}\"";

        byte[] data = Zip(
            ("_rels/.rels", Rels(("rId1", "officeDocument", "word/document.xml"))),
            ("word/document.xml", document),
            ("word/_rels/document.xml.rels", Rels(("rIdH", "header", "header1.xml"), ("rIdH2", "header", "header2.xml"), ("rIdF", "footer", "footer1.xml")).Replace("</Relationships>", "<Relationship Id=\"rIdL\" Type=\"x/hyperlink\" Target=\"https://example.com\" TargetMode=\"External\"/></Relationships>")),
            ("word/header1.xml", $"<w:hdr {margin}><w:p><w:r><w:t>Confidential</w:t></w:r></w:p></w:hdr>"),
            ("word/header2.xml", $"<w:hdr {margin}><w:p><w:r><w:t>Confidential</w:t></w:r></w:p></w:hdr>"),
            ("word/footer1.xml", $"<w:ftr {margin}><w:p><w:r><w:t xml:space=\"preserve\">Page </w:t></w:r><w:fldSimple w:instr=\" PAGE \"><w:r><w:t>1</w:t></w:r></w:fldSimple></w:p></w:ftr>"));

        Assert.Equal("[Page header] Confidential\n"
            + "\n"
            + "Design rules\n"
            + "a\tb\n"
            + "c\n"
            + "new\n"
            + "moved here\n"
            + "3\n"
            + "a link\n"
            + "\n"
            + "Layer\tWidth\n"
            + "met1 met2\t0.14\n"
            + "inside a control\n"
            + "before the box\n"
            + "in the box\n"
            + "\n"
            + "[Page footer] Page 1", OfficeText.Document(data));
    }

    #endregion ********************************************************************



    #region What the bytes are ****************************************************

    [Fact]
    public void The_main_part_says_whether_a_file_is_a_workbook_or_a_document()
    {
        Assert.Equal("workbook", OfficeText.MainElement(Workbook(new[] { "" })));
        Assert.Equal("document", OfficeText.MainElement(Zip(("_rels/.rels", Rels(("rId1", "officeDocument", "word/document.xml"))), ("word/document.xml", $"<w:document xmlns:w=\"{Word}\"/>"))));
        Assert.Null(OfficeText.MainElement(Encoding.UTF8.GetBytes("width met1 0.14")));
        Assert.Null(OfficeText.MainElement(Zip(("notes.txt", "hello"))));
    }

    #endregion ********************************************************************
}
