using System.Text;
using DocumentFormat.OpenXml.Packaging;
using ExcelDataReader;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OD.MyLocalAgent.Core;

/// <summary>Loads bounded local files into Direct Chat attachment content.</summary>
public static class DirectChatAttachmentLoader
{
    /// <summary>Loads a supported image, document, or detected text file.</summary>
    public static async Task<DirectChatAttachment> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The selected attachment was not found.", fullPath);
        }

        const long maximumBytes = 10 * 1024 * 1024;
        if (info.Length > maximumBytes)
        {
            throw new DirectChatAttachmentException("Attachments are limited to 10 MB.");
        }

        var extension = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ImageMediaTypes.TryGetValue(extension, out var mediaType))
        {
            return new DirectChatAttachment
            {
                FileName = Path.GetFileName(fullPath),
                MediaType = mediaType,
                Kind = DirectChatAttachmentKind.Image,
                Data = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false),
            };
        }

        if (DocumentMediaTypes.TryGetValue(extension, out mediaType))
        {
            return new DirectChatAttachment
            {
                FileName = Path.GetFileName(fullPath),
                MediaType = mediaType,
                Kind = DirectChatAttachmentKind.Text,
                Text = await Task.Run(
                        () => ExtractOfficeOrPdfText(fullPath, extension),
                        cancellationToken)
                    .ConfigureAwait(false),
            };
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var text = DecodeTextFile(bytes);
        const int maximumCharacters = 64_000;
        if (text.Length > maximumCharacters)
        {
            text = text[..maximumCharacters] + "\n[Attachment text truncated]";
        }

        return new DirectChatAttachment
        {
            FileName = Path.GetFileName(fullPath),
            MediaType = TextMediaTypes.GetValueOrDefault(extension, "text/plain"),
            Kind = DirectChatAttachmentKind.Text,
            Text = text,
        };
    }

    private static readonly IReadOnlyDictionary<string, string> ImageMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp", [".gif"] = "image/gif", [".bmp"] = "image/bmp",
        };

    private static readonly IReadOnlyDictionary<string, string> TextMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".txt"] = "text/plain", [".md"] = "text/markdown", [".csv"] = "text/csv",
            [".json"] = "application/json", [".xml"] = "application/xml", [".log"] = "text/plain",
            [".yaml"] = "application/yaml", [".yml"] = "application/yaml", [".toml"] = "text/plain",
            [".cs"] = "text/plain", [".csproj"] = "application/xml", [".sln"] = "text/plain",
            [".js"] = "text/javascript", [".ts"] = "text/plain", [".html"] = "text/html",
            [".css"] = "text/css", [".py"] = "text/plain", [".ps1"] = "text/plain",
        };

    private static readonly IReadOnlyDictionary<string, string> DocumentMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".xls"] = "application/vnd.ms-excel",
        };

    private static string ExtractOfficeOrPdfText(string path, string extension) => extension switch
    {
        ".pdf" => ExtractPdfText(path),
        ".docx" => ExtractDocxText(path),
        ".xlsx" or ".xls" => ExtractSpreadsheetText(path),
        _ => throw new InvalidOperationException("The selected document format is not supported."),
    };

    private static string ExtractPdfText(string path)
    {
        using var pdf = PdfDocument.Open(path);
        return Limit(string.Join("\n\n", pdf.GetPages().Take(100).Select(static page => ContentOrderTextExtractor.GetText(page))));
    }

    private static string ExtractDocxText(string path)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        return Limit(body?.InnerText ?? string.Empty);
    }

    private static string ExtractSpreadsheetText(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = File.OpenRead(path);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var text = new StringBuilder();
        var rows = 0;
        do
        {
            text.AppendLine($"Sheet: {reader.Name}");
            while (rows < 1_000 && reader.Read())
            {
                var values = Enumerable.Range(0, Math.Min(reader.FieldCount, 256))
                    .Select(index => Convert.ToString(reader.GetValue(index), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                text.AppendLine(string.Join('\t', values));
                rows++;
            }
        }
        while (rows < 1_000 && reader.NextResult());
        return Limit(text.ToString());
    }

    private static string Limit(string text)
    {
        const int maximumCharacters = 64_000;
        return text.Length <= maximumCharacters ? text : text[..maximumCharacters] + "\n[Attachment text truncated]";
    }

    private static string DecodeTextFile(byte[] bytes)
    {
        if (bytes.AsSpan().IndexOf((byte)0) >= 0 && !HasUnicodeByteOrderMark(bytes))
        {
            throw new DirectChatAttachmentException(
                "This file appears to be binary and cannot be attached. Choose a text document, a supported office document, or an image.");
        }

        try
        {
            var text = GetTextEncoding(bytes).GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

            var disallowedControlCharacters = text.Count(static character =>
                char.IsControl(character) && character is not ('\r' or '\n' or '\t' or '\f'));
            if (disallowedControlCharacters > Math.Max(3, text.Length / 100))
            {
                throw new DirectChatAttachmentException(
                    "This file appears to be binary and cannot be attached. Choose a text document, a supported office document, or an image.");
            }

            return text;
        }
        catch (DecoderFallbackException)
        {
            throw new DirectChatAttachmentException(
                "This file is not valid text and cannot be attached. Choose a text document, a supported office document, or an image.");
        }
    }

    private static Encoding GetTextEncoding(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 })) return new UTF32Encoding(false, true, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF })) return new UTF32Encoding(true, true, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return new UnicodeEncoding(false, true, true);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return new UnicodeEncoding(true, true, true);
        return new UTF8Encoding(false, true);
    }

    private static bool HasUnicodeByteOrderMark(byte[] bytes) =>
        bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }) ||
        bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF });
}
