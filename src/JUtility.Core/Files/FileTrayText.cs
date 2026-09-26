using System.IO.Compression;
using System.Text;
using System.Xml;
using JUtility.Core.Models;
using JUtility.Core.Services;

namespace JUtility.Core.Files;

/// <summary>
/// Package-free text for .txt and .docx. PDF text and OCR live in the WPF app (PdfPig and Windows OCR).
/// Runs only when the user asks for the text; the result goes to the clipboard or a prompt and is never saved.
/// </summary>
public static class FileTrayText
{
    public const int MaxChars = 200_000;
    private const long MaxDocumentXmlBytes = 64L * 1024 * 1024;
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public static string ReadPlainText(string path, int maxChars = MaxChars)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maxChars + 1];
        int read = reader.ReadBlock(buffer, 0, buffer.Length);
        return Finish(new string(buffer, 0, Math.Min(read, maxChars)), read > maxChars);
    }

    /// <summary>Paragraph text of word/document.xml (tabs and line breaks kept). No DTDs, no external resources.</summary>
    public static string ExtractDocx(string path, int maxChars = MaxChars)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry document = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("This .docx has no main document part.");
        if (document.Length > MaxDocumentXmlBytes) throw new InvalidDataException("This .docx is too large to read here.");
        using Stream xml = document.Open();
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true });
        var text = new StringBuilder();
        bool truncated = false, positioned = false;
        // ReadElementContentAsString already moves to the next node, so the loop must not Read() past it.
        while (positioned || reader.Read())
        {
            positioned = false;
            if (text.Length > maxChars)
            {
                truncated = true;
                break;
            }

            if (reader.NamespaceURI != WordNamespace) continue;
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "t":
                        text.Append(reader.ReadElementContentAsString());
                        positioned = true;
                        break;
                    case "tab":
                        text.Append('\t');
                        break;
                    case "p" when reader.IsEmptyElement: // <w:p/> has no end element
                        text.Append('\n');
                        break;
                    case "br":
                    case "cr":
                        text.Append('\n');
                        break;
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
            {
                text.Append('\n');
            }
        }

        return Finish(text.Length > maxChars ? text.ToString(0, maxChars) : text.ToString(), truncated);
    }

    /// <summary>Normalizes line endings, trims trailing spaces and runs of blank lines, and marks truncation.</summary>
    public static string Finish(string text, bool truncated)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var result = new StringBuilder();
        int blank = 0;
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (++blank > 1 || result.Length == 0) continue;
            }
            else
            {
                blank = 0;
            }

            result.Append(line).Append('\n');
        }

        string finished = result.ToString().TrimEnd();
        return truncated ? finished + "\n\n[... truncated by Power Ops]" : finished;
    }

    public static bool HasText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Count(c => !char.IsWhiteSpace(c)) >= 16;
}

/// <summary>Prompt Builder placeholders for a tray file: {{file}} is its name, {{file_text}} its text (read on Compose).</summary>
public static class FileTrayPrompt
{
    public const string FileVariable = "file", TextVariable = "file_text";

    public static IReadOnlyList<string> BuiltIns { get; } = Array.AsReadOnly(new[] { FileVariable, TextVariable });

    public static bool NeedsText(IEnumerable<PromptModuleEntry> modules) =>
        PromptComposer.FindVariables(modules).Contains(TextVariable, StringComparer.OrdinalIgnoreCase);

    public static bool UsesFile(IEnumerable<PromptModuleEntry> modules) =>
        PromptComposer.FindVariables(modules).Any(x => BuiltIns.Contains(x, StringComparer.OrdinalIgnoreCase));

    /// <summary>Only the values that are known; an unknown placeholder stays visible in the prompt, like other variables.</summary>
    public static IReadOnlyDictionary<string, string> Variables(FileTrayEntry? entry, string? text)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entry is not null) variables[FileVariable] = entry.Name;
        if (!string.IsNullOrEmpty(text)) variables[TextVariable] = text;
        return variables;
    }

    public static string Placeholder(string variable) => "{{" + variable + "}}";
}
