using System.Text;
using Xunit;

namespace PromptEditor.Tests;

public sealed class PromptDocumentTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "prompt-editor-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(directory, "prompt with spaces.md");

    public PromptDocumentTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void PreservesNewlineStyleAndDoesNotAddTrailingNewline(string newline)
    {
        File.WriteAllText(FilePath, "one" + newline + "two", new UTF8Encoding(false));
        var document = PromptDocument.Open(FilePath);
        Assert.Equal("one\ntwo", document.Text);
        document.Accept("one\ntwo\nthree");
        Assert.Equal("one" + newline + "two" + newline + "three", File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void CancelDoesNotTouchOriginal()
    {
        var bytes = Encoding.UTF8.GetBytes("original\r\nmixed\n");
        File.WriteAllBytes(FilePath, bytes);
        _ = PromptDocument.Open(FilePath);
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void UnchangedAcceptPreservesExactBytesAndTimestamp()
    {
        var bytes = Encoding.UTF8.GetBytes("original\r\nmixed\n");
        File.WriteAllBytes(FilePath, bytes);
        var timestamp = File.GetLastWriteTimeUtc(FilePath);
        var document = PromptDocument.Open(FilePath);
        document.Accept(document.Text);
        Assert.Equal(bytes, File.ReadAllBytes(FilePath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(FilePath));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public void PreservesEncodingAndUnicode(string name)
    {
        Encoding encoding = name switch
        {
            "utf8" => new UTF8Encoding(false, true),
            "utf8-bom" => new UTF8Encoding(true, true),
            "utf16-le" => new UnicodeEncoding(false, true, true),
            "utf16-be" => new UnicodeEncoding(true, true, true),
            "utf32-le" => new UTF32Encoding(false, true, true),
            _ => new UTF32Encoding(true, true, true)
        };
        File.WriteAllText(FilePath, "Hello \u4e16\u754c \ud83d\ude80\n", encoding);
        var document = PromptDocument.Open(FilePath);
        Assert.Equal("Hello \u4e16\u754c \ud83d\ude80\n", document.Text);
        document.Accept("Updated \u4e16\u754c \ud83d\ude80\n");
        var expected = encoding.GetPreamble().Concat(encoding.GetBytes("Updated \u4e16\u754c \ud83d\ude80\n")).ToArray();
        Assert.Equal(expected, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void ConcurrentChangesAreNotOverwritten()
    {
        File.WriteAllText(FilePath, "original");
        var document = PromptDocument.Open(FilePath);
        File.WriteAllText(FilePath, "external change");
        Assert.Throws<IOException>(() => document.Accept("my change"));
        Assert.Equal("external change", File.ReadAllText(FilePath));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void MissingFileIsNotCreated() =>
        Assert.Throws<FileNotFoundException>(() => PromptDocument.Open(FilePath));

    [Fact]
    public void InvalidUtf8IsRejectedRatherThanSilentlyReplaced()
    {
        File.WriteAllBytes(FilePath, [0xff, 0xff, 0xff]);
        Assert.Throws<DecoderFallbackException>(() => PromptDocument.Open(FilePath));
    }

    [Fact]
    public void ReadOnlyFileRemainsIntact()
    {
        File.WriteAllText(FilePath, "original");
        var document = PromptDocument.Open(FilePath);
        File.SetAttributes(FilePath, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => document.Accept("new"));
            Assert.Equal("original", File.ReadAllText(FilePath));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            File.SetAttributes(FilePath, FileAttributes.Normal);
        }
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(directory))
            File.Delete(file);
        Directory.Delete(directory);
    }
}
