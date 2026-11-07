using System.Text;
using GDSViewer.Ai;

namespace GDSViewer.Tests;

///
///A PDF sent as its words (2026-11-07, from GraphDBViewer): a model set to take a PDF as text - Send PDFs as their text
///under AI settings - is sent the words PdfPig reads out of it in the page, so Gemini and a server of one's own take one,
///and a PDF with no words to read says why rather than going as nothing. The tick and the chat's Send are the ask-ai
///spec's.
///
public class AiPdfTextTests
{
    ///<summary>A real one-page PDF that says what it is given, in Helvetica, with its cross-reference table where it says it is.</summary>
    private static byte[] OnePage(string words)
    {
        string stream = $"BT /F1 12 Tf 72 720 Td ({words}) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        int xref = pdf.Length;

        pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");

        foreach (int offset in offsets)
            pdf.Append($"{offset:D10} 00000 n \n");

        pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");

        return Encoding.Latin1.GetBytes(pdf.ToString());
    }

    [Fact]
    public void A_pdfs_words_are_read_out_of_it()
    {
        var (text, problem) = PdfText.Read("rules.pdf", OnePage("met1 width is 0.14 um"));

        Assert.Null(problem);
        Assert.Contains("met1 width is 0.14 um", text);
    }

    ///<summary>Sent as its words, a PDF goes to Gemini - which is not sent the file - as text inside its name's tags.</summary>
    [Fact]
    public void A_pdf_sent_as_its_text_goes_as_its_words_to_a_model_that_cannot_read_the_file()
    {
        var pdf = new AiAttachment("rules.pdf", "application/pdf", OnePage("met1 width is 0.14 um"));

        Assert.Contains("Tick Send PDFs as their text", AiAttachment.NotSentTo(LlmConnection.Gemini, pdf));
        Assert.Null(AiAttachment.NotSentTo(LlmConnection.Gemini, pdf, pdfAsText: true));

        pdf.AsText = true;

        Assert.Equal(AttachmentKind.Text, pdf.SentKind);
        Assert.Equal(AiAttachment.PdfPageTextTokens, pdf.Tokens);

        pdf.ReadPdf();

        Assert.True(pdf.PdfRead);
        Assert.Null(pdf.Unreadable);

        var part = Assert.Single(AiAttachment.Parts(new[] { pdf }, LlmConnection.Gemini));

        Assert.Equal(AttachmentKind.Text, part.Kind);
        Assert.StartsWith("<attachment name=\"rules.pdf\">", part.Text);
        Assert.Contains("met1 width is 0.14 um", part.Text);
        Assert.True(pdf.Tokens < AiAttachment.PdfPageTextTokens);
    }

    ///<summary>The same file to a model that reads PDFs goes as the file while the model's setting is off.</summary>
    [Fact]
    public void A_pdf_not_sent_as_its_text_goes_as_the_file()
    {
        var pdf = new AiAttachment("rules.pdf", "application/pdf", OnePage("met1 width is 0.14 um"));
        var parts = AiAttachment.Parts(new[] { pdf }, LlmConnection.Anthropic);

        Assert.Equal(AttachmentKind.Pdf, parts[1].Kind);
        Assert.Equal(AiAttachment.PdfPageTokens, pdf.Tokens);
        Assert.Equal("", pdf.Text);
    }

    ///<summary>A PDF the browser named by its type alone is read as one too (GraphDBViewer's fix of the same day).</summary>
    [Fact]
    public void A_pdf_known_only_by_its_type_is_read_as_a_pdf()
    {
        var pdf = new AiAttachment("scan", "application/pdf", OnePage("poly over diff"));

        pdf.AsText = true;
        pdf.ReadPdf();

        Assert.Equal(AttachmentKind.Pdf, pdf.Kind);
        Assert.Null(pdf.Unreadable);
        Assert.Contains("poly over diff", pdf.Text);
    }

    ///<summary>One with no words, as a scan has none, says so, and is sent as that line rather than as nothing.</summary>
    [Fact]
    public void A_pdf_with_no_words_says_why()
    {
        var pdf = new AiAttachment("scan.pdf", "application/pdf", Encoding.Latin1.GetBytes("%PDF-1.7\n<< /Type /Page >>\n%%EOF"));

        pdf.AsText = true;
        pdf.ReadPdf();

        Assert.Contains("scanned PDF", pdf.Unreadable);

        var part = Assert.Single(AiAttachment.Parts(new[] { pdf }, LlmConnection.Gemini));

        Assert.Contains("scanned PDF", part.Text);
    }

    ///<summary>The setting is saved with the model, and copied with it, as its rates are.</summary>
    [Fact]
    public void The_setting_is_off_until_ticked_and_saved_with_the_model()
    {
        Assert.False(new LlmConnection().PdfAsText);

        var saved = new Dictionary<string, LlmConnection> { ["Gemini"] = new LlmConnection { Name = "Gemini", ProviderType = LlmConnection.Gemini, PdfAsText = true } };
        var read = LlmConnectionStore.Read(LlmConnectionStore.Write(saved))["Gemini"];

        Assert.True(read.PdfAsText);
        Assert.True(new LlmConnection(read).PdfAsText);
    }
}
