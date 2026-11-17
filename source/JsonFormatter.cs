using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PostKun;

/// <summary>JSON の整形・判定の純粋関数群。</summary>
public static class JsonFormatter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        // 日本語などを \uXXXX にせず、そのまま読めるようにする
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>JSON として正しければ整形して true。不正なら false で元の文字列をそのまま返す。</summary>
    public static bool TryFormat(string input, out string formatted)
    {
        formatted = input;
        if (string.IsNullOrWhiteSpace(input)) return false;
        try
        {
            using var doc = JsonDocument.Parse(input);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, WriterOptions))
                doc.WriteTo(writer);
            formatted = Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings(Environment.NewLine);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Content-Type が JSON 系か(application/json, application/problem+json など)。</summary>
    public static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var mediaType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return mediaType == "application/json" || mediaType == "text/json" || mediaType.EndsWith("+json");
    }
}
