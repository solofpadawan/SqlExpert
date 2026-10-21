using System.Text;

namespace SqlExpert.Core.SqlEditor;

/// <summary>
/// Reads/writes .sql files keeping their encoding: BOM (UTF-8/UTF-16/UTF-32) is honoured, BOM-less files are
/// UTF-8 when valid, otherwise Windows-1252 (legacy ANSI scripts). New files are saved as UTF-8 with BOM.
/// </summary>
public static class SqlFileText
{
    static SqlFileText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding DefaultEncoding { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return (new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), DefaultEncoding);
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            return (new UTF32Encoding(false, true).GetString(bytes, 4, bytes.Length - 4), new UTF32Encoding(false, true));
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return (Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), Encoding.Unicode);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return (Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), Encoding.BigEndianUnicode);

        try
        {
            var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
            return (strict.GetString(bytes), new UTF8Encoding(false));
        }
        catch (DecoderFallbackException)
        {
            var ansi = Encoding.GetEncoding(1252);
            return (ansi.GetString(bytes), ansi);
        }
    }

    public static (string Text, Encoding Encoding) Read(string path) => Decode(File.ReadAllBytes(path));

    /// <summary>Writes atomically with <paramref name="encoding"/> (BOM included when the encoding has one).</summary>
    public static void Write(string path, string text, Encoding? encoding = null)
    {
        encoding ??= DefaultEncoding;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text, encoding);
        File.Move(tmp, path, overwrite: true);
    }
}
