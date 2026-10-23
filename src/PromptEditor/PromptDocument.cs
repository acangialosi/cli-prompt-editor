using System.Text;

namespace PromptEditor;

public sealed class PromptDocument
{
    private readonly string path;
    private readonly byte[] originalBytes;
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    private readonly string newline;

    public string Text { get; }

    private PromptDocument(string path, byte[] bytes, Encoding encoding, int preambleLength)
    {
        this.path = path;
        originalBytes = bytes;
        this.encoding = encoding;
        preamble = bytes[..preambleLength];
        var original = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var firstBreak = original.IndexOfAny(['\r', '\n']);
        newline = firstBreak < 0 ? Environment.NewLine
            : original[firstBreak] == '\r'
                ? firstBreak + 1 < original.Length && original[firstBreak + 1] == '\n' ? "\r\n" : "\r"
                : "\n";
        Text = Normalize(original);
    }

    public static PromptDocument Open(string path)
    {
        path = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(path);
        // Check UTF-32 before UTF-16 because their little-endian BOMs overlap.
        (Encoding Encoding, int Skip) format =
            bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 }) ? (new UTF32Encoding(false, true, true), 4) :
            bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff }) ? (new UTF32Encoding(true, true, true), 4) :
            bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? (new UnicodeEncoding(false, true, true), 2) :
            bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? (new UnicodeEncoding(true, true, true), 2) :
            bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? (new UTF8Encoding(true, true), 3) :
            (new UTF8Encoding(false, true), 0);
        return new PromptDocument(path, bytes, format.Encoding, format.Skip);
    }

    public void Accept(string text)
    {
        text = Normalize(text);
        if (text == Text)
            return;

        var content = encoding.GetBytes(text.Replace("\n", newline, StringComparison.Ordinal));
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".prompt-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(preamble);
                stream.Write(content);
                stream.Flush(true);
            }

            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(originalBytes))
                throw new IOException("The prompt file changed outside this editor. Nothing was overwritten. " +
                                      "Copy your text before cancelling and reopening the editor.");

            File.Replace(temporary, path, null);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
