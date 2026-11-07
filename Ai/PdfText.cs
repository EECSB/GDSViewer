using System.Text;

namespace GDSViewer.Ai
{
    ///
    ///The words of a PDF, read out of it in the page with PdfPig, for a model set to take a PDF as its text - see
    ///<see cref="LlmConnection.PdfAsText"/> (2026-11-07, from GraphDBViewer, which had it from 2026-11-02): a model that
    ///cannot read the file, Gemini through its OpenAI-compatible endpoint or a server of one's own, is sent its words, and
    ///any model is sent them for less than a page's picture costs.
    ///
    ///**PdfPig comes with the app**, where GraphDBViewer fetches it on first use: compiled ahead of time, an app with
    ///its assemblies set aside by BlazorWebAssemblyLazyLoad dies on boot - see the project file.
    ///
    public static class PdfText
    {
        ///
        ///A PDF's words, or why there are none - never both. A scan has pictures of its pages and no words, and says so;
        ///a file PdfPig cannot open says that rather than throwing.
        ///
        public static (string? Text, string? Problem) Read(string name, byte[] data)
        {
            string text;

            try
            {
                text = pages(data);
            }
            catch (Exception)
            {
                return (null, $"{name} could not be read as a PDF.");
            }

            if (string.IsNullOrWhiteSpace(text))
                return (null, $"{name} has no words to read.");

            return (text, null);
        }

        ///
        ///The text of a PDF, page by page. ContentOrderTextExtractor follows the document's own order rather than stitching
        ///words together by position, which keeps a two-column page from interleaving its columns. A page that cannot be
        ///read is skipped rather than failing the file: one broken page in a hundred should cost that page.
        ///
        private static string pages(byte[] data)
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(data);
            var builder = new StringBuilder();

            foreach (var page in document.GetPages())
            {
                string text;

                try
                {
                    text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
                }
                catch (Exception)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                builder.AppendLine(text);
                builder.AppendLine();
            }

            return builder.ToString().Trim();
        }
    }
}
