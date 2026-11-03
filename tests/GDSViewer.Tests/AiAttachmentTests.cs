using System.Text;
using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///The files a question carries: which are taken and why one is turned away, what each becomes in a request, and what
///the estimate weighs it at.
///
///Ported with the feature from ConfigurationRuleEditor's AssistantAttachmentTests. Its workbooks and documents are read
///in the page here rather than on a server - how is OfficeTextTests; whether and when, and what is sent, are here.
///
public class AiAttachmentTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    private static byte[] Pdf(string body = "")
    {
        return Encoding.Latin1.GetBytes("%PDF-1.7\n" + body + "\n%%EOF");
    }

    private static byte[] Text(string text)
    {
        return Encoding.UTF8.GetBytes(text);
    }

    #region What a file is ********************************************************

    [Theory]
    [InlineData("shot.png", "", AttachmentKind.Image)]
    [InlineData("photo.JPG", "", AttachmentKind.Image)]
    [InlineData("rules.pdf", "", AttachmentKind.Pdf)]
    [InlineData("sky130A.drc", "", AttachmentKind.Text)]
    [InlineData("layers.lyp", "", AttachmentKind.Text)]
    [InlineData("inverter.spice", "", AttachmentKind.Text)]
    [InlineData("top.v", "", AttachmentKind.Text)]
    [InlineData("map.csv", "application/vnd.ms-excel", AttachmentKind.Text)]
    [InlineData("dragged", "image/webp", AttachmentKind.Image)]
    [InlineData("notes", "text/plain", AttachmentKind.Text)]
    [InlineData("mosfet.gds", "application/octet-stream", AttachmentKind.Unsupported)]
    [InlineData("table.xlsx", "", AttachmentKind.Spreadsheet)]
    [InlineData("macros.XLSM", "", AttachmentKind.Spreadsheet)]
    [InlineData("manual.docx", "", AttachmentKind.Document)]
    [InlineData("dragged", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", AttachmentKind.Spreadsheet)]
    [InlineData("dragged", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", AttachmentKind.Document)]
    [InlineData("old.xls", "", AttachmentKind.Unsupported)]
    [InlineData("old.doc", "", AttachmentKind.Unsupported)]
    [InlineData("slides.pptx", "", AttachmentKind.Unsupported)]
    [InlineData("setup.exe", "", AttachmentKind.Unsupported)]
    public void The_name_decides_before_the_type(string name, string type, AttachmentKind kind)
    {
        Assert.Equal(kind, AiAttachment.KindOf(name, type));
    }

    ///<summary>A layout is the app's to open, and an Office file this page cannot read is a save away from one it can - each said, rather than "not readable".</summary>
    [Fact]
    public void A_layout_and_an_office_file_this_page_cannot_read_are_turned_away_with_what_to_do_instead()
    {
        Assert.Contains("Open it with Open", AiAttachment.Problem("mosfet.gds", "", new byte[] { 0, 2, 0, 0 }));
        Assert.Contains("from before 2007, which this page cannot read. Save it as an .xlsx", AiAttachment.Problem("table.xls", "", new byte[] { 1 }));
        Assert.Contains("Save it as a .docx or a PDF", AiAttachment.Problem("manual.doc", "", new byte[] { 1 }));
        Assert.Contains("PowerPoint deck", AiAttachment.Problem("slides.pptx", "", new byte[] { 1 }));
        Assert.Contains("not a file the assistant can read", AiAttachment.Problem("setup.exe", "", new byte[] { 1 }));
    }

    ///
    ///A workbook or a document is turned away when it is not one: text with the name, the other kind with the name, and
    ///one locked with a password - which Office wraps as it wrapped its files before 2007, so that is said too.
    ///
    [Fact]
    public void A_workbook_or_a_document_that_is_not_one_is_turned_away()
    {
        byte[] document = Office("word/document.xml", "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>");
        byte[] locked = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0 };

        Assert.Contains("is not an Excel workbook, whatever its name says", AiAttachment.Problem("table.xlsx", "", Text("a,b\n1,2\n")));
        Assert.Contains("is not an Excel workbook, whatever its name says", AiAttachment.Problem("table.xlsx", "", document));
        Assert.Null(AiAttachment.Problem("manual.docx", "", document));
        Assert.Contains("is locked with a password, or is a Word document from before 2007", AiAttachment.Problem("manual.docx", "", locked));
    }

    ///
    ///A workbook whose parts cannot be read is turned away when it is attached, with what went wrong - read then, once,
    ///rather than found out when the question goes - and the box keeps nothing.
    ///
    [Fact]
    public void A_workbook_that_cannot_be_read_is_turned_away_when_it_is_attached()
    {
        byte[] broken = Office("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"S\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>",
            ("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>"),
            ("xl/worksheets/sheet1.xml", "<worksheet><sheetData><row></worksheet>"));

        var chat = new AiChat();
        var file = new AiAttachment("broken.xlsx", "", broken);

        Assert.Null(AiAttachment.Problem(file.Name, file.MediaType, file.Data));
        Assert.StartsWith("broken.xlsx could not be read as an Excel workbook: ", chat.Attach(file));
        Assert.Empty(chat.Pending);
    }

    ///<summary>An Office package whose main part is the one given, with any other parts beside it.</summary>
    private static byte[] Office(string main, string xml, params (string Path, string Xml)[] others)
    {
        using var memory = new MemoryStream();

        using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            string rels = $"<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"{main}\"/></Relationships>";

            foreach (var (path, part) in new[] { ("_rels/.rels", rels), (main, xml) }.Concat(others))
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());

                writer.Write(part);
            }
        }

        return memory.ToArray();
    }

    [Fact]
    public void A_file_that_is_not_what_its_name_says_is_turned_away()
    {
        Assert.Contains("not a PNG, JPEG, GIF or WebP", AiAttachment.Problem("shot.png", "image/png", Text("hello")));
        Assert.Contains("not a PDF", AiAttachment.Problem("rules.pdf", "", Text("hello")));
        Assert.Contains("not a text file", AiAttachment.Problem("deck.drc", "", new byte[] { 0x6C, 0, 0x61, 0 }));
        Assert.Contains("is empty", AiAttachment.Problem("deck.drc", "", Array.Empty<byte>()));
    }

    [Fact]
    public void A_picture_can_be_half_what_a_document_can()
    {
        Assert.Null(AiAttachment.Refusal("shot.png", "", AiAttachment.MostImageBytes));
        Assert.Contains("5 MB at most", AiAttachment.Refusal("shot.png", "", AiAttachment.MostImageBytes + 1));

        Assert.Null(AiAttachment.Refusal("rules.pdf", "", AiAttachment.MostImageBytes + 1));
        Assert.Contains("10 MB at most", AiAttachment.Refusal("rules.pdf", "", AiAttachment.MostFileBytes + 1));
    }

    [Fact]
    public void A_pictures_type_is_read_off_its_bytes()
    {
        Assert.Equal("image/png", AiAttachment.ImageType(Png));
        Assert.Equal("image/jpeg", AiAttachment.ImageType(Jpeg));
        Assert.Equal("image/gif", AiAttachment.ImageType(Encoding.ASCII.GetBytes("GIF89a....")));
        Assert.Equal("image/webp", AiAttachment.ImageType(Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")));
        Assert.Null(AiAttachment.ImageType(Text("not a picture")));
    }

    #endregion ********************************************************************



    #region Reading text **********************************************************

    [Fact]
    public void Text_is_utf8_unless_a_mark_says_otherwise_and_latin1_when_it_is_not()
    {
        Assert.Equal("1 µm", AiAttachment.Decode(Text("1 µm")));
        Assert.Equal("1 µm", AiAttachment.Decode(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Text("1 µm")).ToArray()));
        Assert.Equal("1 µm", AiAttachment.Decode(new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("1 µm")).ToArray()));

        //A µ written in a Windows code page is one byte, which is not UTF-8 on its own.
        Assert.Equal("1 µm", AiAttachment.Decode(new byte[] { 0x31, 0x20, 0xB5, 0x6D }));
    }

    [Fact]
    public void A_text_file_goes_between_tags_and_is_cut_where_it_runs_long()
    {
        string whole = AiAttachment.Wrapped("deck.drc", "width met1 0.14");

        Assert.Equal("<attachment name=\"deck.drc\">" + Environment.NewLine + "width met1 0.14" + Environment.NewLine + "</attachment>", whole);

        string cut = AiAttachment.Wrapped("netlist.sp", new string('x', AiAttachment.MostTextCharacters + 1234));

        Assert.Contains("[Cut short here. The file goes on for another 1,234 characters, which were left out.]", cut);

        //Counted rather than searched for: xUnit searches as the culture does, and looking for 200,001 x's among
        //200,000 that way took a minute and ten seconds.
        Assert.Equal(AiAttachment.MostTextCharacters, cut.Count(character => character == 'x'));
    }

    ///<summary>Pages counted where they can be seen; then the page tree's count; then a guess from the size.</summary>
    [Fact]
    public void A_pdfs_pages_are_counted_for_the_estimate()
    {
        Assert.Equal(3, AiAttachment.PageCount(Pdf("<< /Type /Pages /Count 3 >> << /Type /Page >> << /Type/Page >> << /Type /Page\n>>")));
        Assert.Equal(7, AiAttachment.PageCount(Pdf("<< /Type /Catalog >> << /Count 7 >> stream...")));
        Assert.Equal(4, AiAttachment.PageCount(new byte[200_000]));
        Assert.Equal(1, AiAttachment.PageCount(Pdf()));
    }

    #endregion ********************************************************************



    #region What a request carries ************************************************

    ///<summary>Files ahead of the question, each named: a picture after a line with its name, a text file inside tags carrying it.</summary>
    [Fact]
    public void Each_file_goes_named_and_in_the_order_it_was_attached()
    {
        var files = new[] { new AiAttachment("shot.png", "image/png", Png), new AiAttachment("deck.drc", "", Text("width met1 0.14")) };

        var parts = AiAttachment.Parts(files, LlmConnection.Anthropic);

        Assert.Equal(new[] { AttachmentKind.Text, AttachmentKind.Image, AttachmentKind.Text }, parts.Select(part => part.Kind));
        Assert.Equal("Attached file: shot.png", parts[0].Text);
        Assert.Equal(Convert.ToBase64String(Png), parts[1].Base64);
        Assert.StartsWith("<attachment name=\"deck.drc\">", parts[2].Text);
    }

    ///<summary>The type the bytes say, not the name: Anthropic refuses a JPEG sent as a PNG.</summary>
    [Fact]
    public void A_jpeg_called_png_is_sent_as_a_jpeg()
    {
        var parts = AiAttachment.Parts(new[] { new AiAttachment("shot.png", "image/png", Jpeg) }, LlmConnection.Anthropic);

        Assert.Equal("image/jpeg", parts[1].MediaType);
    }

    ///
    ///A file the model is not sent is still said, in a line - left out without a word, it would be a question about a
    ///file the person never sent. A PDF to Gemini through Google's compatible endpoint, which documents no PDFs, and a
    ///file unticked in the files panel.
    ///
    [Fact]
    public void A_file_not_sent_is_said_in_a_line()
    {
        var pdf = new AiAttachment("rules.pdf", "", Pdf());
        var shot = new AiAttachment("shot.png", "", Png) { Excluded = true };

        var parts = AiAttachment.Parts(new[] { pdf, shot }, LlmConnection.Gemini);

        Assert.All(parts, part => Assert.Equal(AttachmentKind.Text, part.Kind));
        Assert.Contains("rules.pdf, which this model cannot be sent", parts[0].Text);
        Assert.Contains("shot.png here, and has since left it out", parts[1].Text);
    }

    [Theory]
    [InlineData(LlmConnection.Anthropic, true)]
    [InlineData(LlmConnection.OpenAI, true)]
    [InlineData(LlmConnection.Gemini, false)]
    [InlineData(LlmConnection.Custom, false)]
    public void A_pdf_goes_where_it_is_known_to_be_read(string provider, bool sent)
    {
        Assert.Equal(sent, AiAttachment.SentTo(provider, AttachmentKind.Pdf));
        Assert.True(AiAttachment.SentTo(provider, AttachmentKind.Image));
        Assert.True(AiAttachment.SentTo(provider, AttachmentKind.Text));

        //Read out as words in the page, so every model is sent them.
        Assert.True(AiAttachment.SentTo(provider, AttachmentKind.Spreadsheet));
        Assert.True(AiAttachment.SentTo(provider, AttachmentKind.Document));
        Assert.Equal(sent, AiAttachment.NotSentTo(provider, new AiAttachment("rules.pdf", "", Pdf())) is null);
    }

    ///<summary>What the estimate weighs a file at: a picture's fixed share, a page's, the text's length - and a line where it is not sent.</summary>
    [Fact]
    public void A_file_is_weighed_at_what_it_adds_to_a_request()
    {
        var shot = new AiAttachment("shot.png", "", Png);
        var deck = new AiAttachment("deck.drc", "", Text(new string('w', 3500)));
        var pdf = new AiAttachment("rules.pdf", "", Pdf("<< /Type /Page >> << /Type /Page >>"));

        Assert.Equal(AiAttachment.ImageTokens + 15, AiEstimate.Attachment(shot, LlmConnection.Anthropic));
        Assert.Equal(1000 + 15, AiEstimate.Attachment(deck, LlmConnection.Anthropic));
        Assert.Equal(2 * AiAttachment.PdfPageTokens + 15, AiEstimate.Attachment(pdf, LlmConnection.Anthropic));
        Assert.Equal(25, AiEstimate.Attachment(pdf, LlmConnection.Gemini));

        shot.Excluded = true;

        Assert.Equal(25, AiEstimate.Attachment(shot, LlmConnection.Anthropic));
    }

    #endregion ********************************************************************
}
