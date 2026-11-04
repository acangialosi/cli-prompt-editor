using System.Drawing.Imaging;
using Xunit;

namespace PromptEditor.Tests;

public sealed class ImageAttachmentsTests
{
    [Theory]
    [InlineData(".png", ".png")]
    [InlineData(".jpg", ".jpg")]
    [InlineData(".jpeg", ".jpg")]
    [InlineData(".gif", ".gif")]
    [InlineData(".bmp", ".png")]
    public void FilesAreStagedInMemoryAndSavedInCopilotSupportedFormats(string extension, string savedExtension) =>
        WithAttachments((attachments, root, directory) =>
        {
            var source = Path.Combine(root, "source image \u4e16\u754c" + extension);
            var format = extension switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".gif" => ImageFormat.Gif,
                ".bmp" => ImageFormat.Bmp,
                _ => ImageFormat.Png
            };
            using (var bitmap = new Bitmap(20, 10))
                bitmap.Save(source, format);
            var original = File.ReadAllBytes(source);
            var attachment = Assert.Single(attachments.AddFiles([source]));
            Assert.Equal(Path.GetFileName(source), attachment.Name);
            Assert.Equal(savedExtension, Path.GetExtension(attachment.Path));
            Assert.Equal($"@\"{attachment.Path}\"", attachment.Reference);
            Assert.False(Directory.Exists(directory));
            File.Delete(source);
            attachments.Accept(attachment.Reference, text =>
            {
                Assert.Equal(attachment.Reference, text);
                Assert.True(File.Exists(attachment.Path));
            });
            using var saved = Image.FromFile(attachment.Path);
            Assert.Equal(new Size(20, 10), saved.Size);
            if (extension != ".bmp")
                Assert.Equal(original, File.ReadAllBytes(attachment.Path));
        });

    [Fact]
    public void BitmapClipboardScreenshotsBecomePngFiles() => WithAttachments((attachments, _, _) =>
    {
        var data = new DataObject(DataFormats.Bitmap, new Bitmap(40, 30));
        var attachment = Assert.Single(attachments.ReadClipboard(data)!);
        Assert.Equal("Screenshot.png", attachment.Name);
        Assert.EndsWith(".png", attachment.Path);
        using var stream = new MemoryStream(attachment.Content);
        using var image = Image.FromStream(stream);
        Assert.Equal(ImageFormat.Png.Guid, image.RawFormat.Guid);
        Assert.Equal(new Size(40, 30), image.Size);
        Assert.False(File.Exists(attachment.Path));
    });

    [Fact]
    public void ClipboardPngPreservesBytesAndTransparency() => WithAttachments((attachments, _, _) =>
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, Color.FromArgb(80, 20, 40, 60));
        using var bytes = new MemoryStream();
        bitmap.Save(bytes, ImageFormat.Png);
        var expected = bytes.ToArray();
        var data = new DataObject();
        data.SetData("PNG", false, new MemoryStream(expected));
        data.SetData(DataFormats.UnicodeText, "not the image");
        var attachment = Assert.Single(attachments.ReadClipboard(data)!);
        Assert.Equal(expected, attachment.Content);
        using var stream = new MemoryStream(attachment.Content);
        using var decoded = new Bitmap(stream);
        Assert.Equal(80, decoded.GetPixel(0, 0).A);
    });

    [Fact]
    public void MultipleClipboardFilesTakePrecedenceOverText() => WithAttachments((attachments, root, _) =>
    {
        var first = WritePng(root, "first.png");
        var second = WritePng(root, "second.png");
        var data = new DataObject(DataFormats.FileDrop, new[] { first, second });
        data.SetData(DataFormats.UnicodeText, "some text");
        var pasted = attachments.ReadClipboard(data)!;
        Assert.Equal(new[] { "first.png", "second.png" }, pasted.Select(image => image.Name));
        Assert.Equal(2, attachments.ReferencedIn(string.Join("\n", pasted.Select(image => image.Reference))).Count);
    });

    [Fact]
    public void TextClipboardIsNotAnImage() => WithAttachments((attachments, _, _) =>
        Assert.Null(attachments.ReadClipboard(new DataObject(DataFormats.UnicodeText, "text"))));

    [Theory]
    [InlineData("file.txt", "unsupported")]
    [InlineData("corrupt.png", "not an image")]
    public void InvalidFileBatchesAreRejectedWithoutSavingAnything(string name, string content) =>
        WithAttachments((attachments, root, directory) =>
        {
            var good = WritePng(root, "good.png");
            var bad = Path.Combine(root, name);
            File.WriteAllText(bad, content);
            Assert.Throws<InvalidDataException>(() => attachments.AddFiles([good, bad]));
            Assert.False(Directory.Exists(directory));
        });

    [Fact]
    public void MismatchedFileExtensionIsRejected() => WithAttachments((attachments, root, _) =>
    {
        var png = WritePng(root, "renamed.jpg");
        Assert.Throws<InvalidDataException>(() => attachments.AddFiles([png]));
    });

    [Fact]
    public void OnlyReferencedImagesAreSaved() => WithAttachments((attachments, root, directory) =>
    {
        var pasted = attachments.AddFiles([WritePng(root, "first.png"), WritePng(root, "second.png")]);
        attachments.Accept(pasted[1].Reference, _ => { });
        Assert.False(File.Exists(pasted[0].Path));
        Assert.True(File.Exists(pasted[1].Path));
        Assert.Single(Directory.GetFiles(directory));
    });

    [Fact]
    public void RemovingAllReferencesDoesNotCreateStorage() => WithAttachments((attachments, root, directory) =>
    {
        attachments.AddFiles([WritePng(root, "first.png")]);
        attachments.Accept("just text", text => Assert.Equal("just text", text));
        Assert.False(Directory.Exists(directory));
    });

    [Fact]
    public void PromptWriteFailureRollsBackFilesAndCanBeRetried() => WithAttachments((attachments, root, directory) =>
    {
        var attachment = Assert.Single(attachments.AddFiles([WritePng(root, "first.png")]));
        Assert.Throws<IOException>(() => attachments.Accept(attachment.Reference, _ => throw new IOException("failed")));
        Assert.Empty(Directory.GetFiles(directory));
        attachments.Accept(attachment.Reference, _ => { });
        Assert.True(File.Exists(attachment.Path));
    });

    [Fact]
    public void PromptConflictLeavesOriginalPromptAndNoAttachments() => WithAttachments((attachments, root, directory) =>
    {
        var path = Path.Combine(root, "prompt.md");
        File.WriteAllText(path, "original");
        var document = PromptDocument.Open(path);
        var attachment = Assert.Single(attachments.AddFiles([WritePng(root, "first.png")]));
        File.WriteAllText(path, "changed elsewhere");
        Assert.Throws<IOException>(() => attachments.Accept(attachment.Reference, document.Accept));
        Assert.Equal("changed elsewhere", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(directory));
    });

    [Fact]
    public void ExistingDestinationIsNotOverwrittenOrDeleted() => WithAttachments((attachments, root, _) =>
    {
        var attachment = Assert.Single(attachments.AddFiles([WritePng(root, "first.png")]));
        Directory.CreateDirectory(Path.GetDirectoryName(attachment.Path)!);
        File.WriteAllText(attachment.Path, "another process");
        var wrote = false;
        Assert.Throws<IOException>(() => attachments.Accept(attachment.Reference, _ => wrote = true));
        Assert.False(wrote);
        Assert.Equal("another process", File.ReadAllText(attachment.Path));
    });

    [Fact]
    public void FailureWritingALaterImageRollsBackEarlierImagesOnly() => WithAttachments((attachments, root, directory) =>
    {
        var pasted = attachments.AddFiles([WritePng(root, "first.png"), WritePng(root, "second.png")]);
        Directory.CreateDirectory(directory);
        File.WriteAllText(pasted[1].Path, "another process");
        var wrote = false;
        var references = string.Join("\n", pasted.Select(image => image.Reference));
        Assert.Throws<IOException>(() => attachments.Accept(references, _ => wrote = true));
        Assert.False(wrote);
        Assert.False(File.Exists(pasted[0].Path));
        Assert.Equal("another process", File.ReadAllText(pasted[1].Path));
    });

    [Fact]
    public void UnwritableStorageDoesNotWriteThePrompt() => WithAttachments((_, root, directory) =>
    {
        File.WriteAllText(directory, "a file, not a directory");
        using var attachments = new ImageAttachments(directory);
        var attachment = Assert.Single(attachments.AddFiles([WritePng(root, "first.png")]));
        var wrote = false;
        Assert.Throws<IOException>(() => attachments.Accept(attachment.Reference, _ => wrote = true));
        Assert.False(wrote);
    });

    [Fact]
    public void RollbackReportsLockedFilesAndStillRemovesOtherNewCopies() => WithAttachments((attachments, root, _) =>
    {
        var pasted = attachments.AddFiles([WritePng(root, "first.png"), WritePng(root, "second.png")]);
        FileStream? held = null;
        try
        {
            var references = string.Join("\n", pasted.Select(image => image.Reference));
            var error = Assert.Throws<IOException>(() => attachments.Accept(references, _ =>
            {
                held = new FileStream(pasted[0].Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                throw new IOException("prompt write failed");
            }));
            Assert.Contains("prompt write failed", error.Message);
            Assert.Contains(pasted[0].Path, error.Message);
            Assert.True(File.Exists(pasted[0].Path));
            Assert.False(File.Exists(pasted[1].Path));
        }
        finally
        {
            held?.Dispose();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReopeningManagedReferencesRestoresPreviewsAndKeepsAcceptedFiles(bool trailingSeparator) =>
        WithAttachments((attachments, root, directory) =>
        {
            var attachment = Assert.Single(attachments.AddFiles([WritePng(root, "first.png")]));
            attachments.Accept(attachment.Reference, _ => { });
            using var reopened = new ImageAttachments(trailingSeparator ? directory + Path.DirectorySeparatorChar : directory);
            Assert.Empty(reopened.Restore(attachment.Reference + "\n" + attachment.Reference));
            var restored = Assert.Single(reopened.ReferencedIn(attachment.Reference));
            Assert.Equal(attachment.Path, restored.Path);
            Assert.Equal(attachment.Content, restored.Content);
            reopened.Accept(attachment.Reference, _ => { });
            reopened.Accept("removed", _ => { });
            Assert.True(File.Exists(attachment.Path));
        });

    [Fact]
    public void RestoreReportsMissingFilesWithoutReadingUnmanagedPaths() => WithAttachments((attachments, root, directory) =>
    {
        var unmanaged = Path.Combine(root, Guid.NewGuid().ToString("N") + ".png");
        var missing = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        var errors = attachments.Restore($"@\"{unmanaged}\" @\"{missing}\"");
        Assert.Single(errors);
        Assert.Empty(attachments.ReferencedIn($"@\"{missing}\""));
    });

    [Fact]
    public void RestoreReportsCorruptImagesInsteadOfLosingPromptText() => WithAttachments((attachments, _, directory) =>
    {
        Directory.CreateDirectory(directory);
        var corrupt = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllText(corrupt, "corrupt");
        Assert.Single(attachments.Restore($"@\"{corrupt}\""));
    });

    [Fact]
    public void ChangedAcceptedImageStopsPromptAcceptance() => WithAttachments((attachments, root, _) =>
    {
        var attachment = Assert.Single(attachments.AddFiles([WritePng(root, "first.png")]));
        attachments.Accept(attachment.Reference, _ => { });
        File.WriteAllText(attachment.Path, "changed");
        var wrote = false;
        Assert.Throws<IOException>(() => attachments.Accept(attachment.Reference, _ => wrote = true));
        Assert.False(wrote);
        Assert.Equal("changed", File.ReadAllText(attachment.Path));
    });

    private static string WritePng(string root, string name)
    {
        var path = Path.Combine(root, name);
        using var bitmap = new Bitmap(20, 10);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static void WithAttachments(Action<ImageAttachments, string, string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"prompt images \u4e16\u754c {Guid.NewGuid():N}");
        var directory = Path.Combine(root, "attachments with spaces");
        Directory.CreateDirectory(root);
        try
        {
            using var attachments = new ImageAttachments(directory);
            test(attachments, root, directory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
