using System.Text;
using JUtility.Core.Files;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JUtility.App.Services;

// Text layer of a PDF (PdfPig, managed). A scanned PDF has little or no text layer; the caller then falls back to
// Windows OCR. Runs only on "Copy text" or a Prompt Builder {{file_text}}, off the UI thread.
internal static class FileTrayPdfText
{
    public const int MaxPages = 300;

    public static string Extract(string path, CancellationToken cancellation)
    {
        using PdfDocument document = PdfDocument.Open(path);
        var text = new StringBuilder();
        bool truncated = false;
        int number = 0;
        foreach (Page page in document.GetPages())
        {
            cancellation.ThrowIfCancellationRequested();
            if (++number > MaxPages || text.Length > FileTrayText.MaxChars)
            {
                truncated = true;
                break;
            }

            string pageText = ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(pageText)) continue;
            if (text.Length > 0) text.Append("\n\n");
            text.Append(pageText);
        }

        string all = text.Length > FileTrayText.MaxChars ? text.ToString(0, FileTrayText.MaxChars) : text.ToString();
        return FileTrayText.Finish(all, truncated || text.Length > FileTrayText.MaxChars);
    }
}
