using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PromptEditor;

internal sealed class ImageAttachment(string path, string name, byte[] content, Image thumbnail) : IDisposable
{
    public string Path { get; } = path;
    public string Name { get; } = name;
    public string Reference => $"@\"{Path}\"";
    public byte[] Content { get; } = content;
    public Image Thumbnail { get; } = thumbnail;
    public bool Persisted { get; set; }

    public void Dispose() => Thumbnail.Dispose();
}

internal sealed class ImageAttachments : IDisposable
{
    private readonly string directory;
    private readonly List<ImageAttachment> images = [];

    public ImageAttachments(string? directory = null)
    {
        if (directory == null)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local))
                throw new IOException("Cannot locate the local application data folder for image attachments.");
            directory = Path.Combine(local, "PromptEditor", "Attachments");
        }
        this.directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    public IReadOnlyList<ImageAttachment> ReferencedIn(string text) =>
        images.Where(image => text.Contains(image.Reference, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<string> Restore(string text)
    {
        var errors = new List<string>();
        foreach (Match match in Regex.Matches(text, "@\"([^\"\\r\\n]+)\""))
        {
            var path = match.Groups[1].Value;
            if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                || !string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _)
                || images.Any(image => string.Equals(image.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                var image = FromBytes(Path.GetFileName(path), File.ReadAllBytes(path), FileFormat(path), path);
                images.Add(image);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                          or ArgumentException or ExternalException or System.Security.SecurityException)
            {
                errors.Add(error.Message);
            }
        }
        return errors;
    }

    public IReadOnlyList<ImageAttachment>? ReadClipboard(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop))
        {
            if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } files)
                throw new InvalidDataException("The clipboard does not contain readable image file paths.");
            return AddFiles(files);
        }

        if (data.GetDataPresent("PNG", false))
        {
            using var content = new MemoryStream();
            switch (data.GetData("PNG", false))
            {
                case Stream stream:
                    using (stream)
                    {
                        if (stream.CanSeek)
                            stream.Position = 0;
                        stream.CopyTo(content);
                    }
                    break;
                case byte[] bytes:
                    content.Write(bytes);
                    break;
                default:
                    throw new InvalidDataException("The clipboard PNG image could not be read.");
            }
            var image = FromBytes("Screenshot.png", content.ToArray(), ImageFormat.Png);
            images.Add(image);
            return [image];
        }

        if (data.GetDataPresent(DataFormats.Bitmap))
        {
            if (data.GetData(DataFormats.Bitmap) is not Image bitmap)
                throw new InvalidDataException("The clipboard image could not be read.");
            using (bitmap)
            using (var content = new MemoryStream())
            {
                bitmap.Save(content, ImageFormat.Png);
                var image = FromBytes("Screenshot.png", content.ToArray(), ImageFormat.Png);
                images.Add(image);
                return [image];
            }
        }
        return null;
    }

    public IReadOnlyList<ImageAttachment> AddFiles(IEnumerable<string> paths)
    {
        var added = new List<ImageAttachment>();
        var completed = false;
        try
        {
            foreach (var path in paths)
            {
                var format = FileFormat(path);
                added.Add(FromBytes(Path.GetFileName(path), File.ReadAllBytes(path), format));
            }
            images.AddRange(added);
            completed = true;
            return added;
        }
        finally
        {
            if (!completed)
                foreach (var image in added)
                    image.Dispose();
        }
    }

    private static ImageFormat FileFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => ImageFormat.Png,
        ".jpg" or ".jpeg" => ImageFormat.Jpeg,
        ".gif" => ImageFormat.Gif,
        ".bmp" => ImageFormat.Bmp,
        _ => throw new InvalidDataException(
            $"Cannot attach \"{Path.GetFileName(path)}\". Supported image files: PNG, JPEG, GIF, and BMP.")
    };

    private ImageAttachment FromBytes(string name, byte[] bytes, ImageFormat expectedFormat, string? existingPath = null)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var image = Decode(stream, name);
        if (image.RawFormat.Guid != expectedFormat.Guid)
            throw new InvalidDataException($"\"{name}\" does not contain the expected image format.");
        var extension = expectedFormat.Equals(ImageFormat.Jpeg) ? ".jpg"
            : expectedFormat.Equals(ImageFormat.Gif) ? ".gif" : ".png";
        if (expectedFormat.Equals(ImageFormat.Bmp))
        {
            using var png = new MemoryStream();
            image.Save(png, ImageFormat.Png);
            bytes = png.ToArray();
        }

        var thumbnail = new Bitmap(96, 64);
        var completed = false;
        try
        {
            using var graphics = Graphics.FromImage(thumbnail);
            graphics.Clear(Color.White);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Min(96f / image.Width, 64f / image.Height);
            var width = Math.Max(1, (int)(image.Width * scale));
            var height = Math.Max(1, (int)(image.Height * scale));
            graphics.DrawImage(image, (96 - width) / 2, (64 - height) / 2, width, height);
            var attachment = new ImageAttachment(existingPath ?? Path.Combine(directory, $"{Guid.NewGuid():N}{extension}"),
                name, bytes, thumbnail) { Persisted = existingPath != null };
            completed = true;
            return attachment;
        }
        finally
        {
            if (!completed)
                thumbnail.Dispose();
        }
    }

    private static Image Decode(Stream stream, string name)
    {
        try
        {
            return Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        }
        catch (Exception error) when (error is ArgumentException or OutOfMemoryException)
        {
            // GDI+ also reports invalid image data as OutOfMemoryException.
            throw new InvalidDataException($"\"{name}\" is invalid, unsupported, or too large to decode.", error);
        }
    }

    public void Accept(string text, Action<string> writePrompt)
    {
        var referenced = ReferencedIn(text);
        var created = new List<string>();
        var completed = false;
        Exception? failure = null;
        try
        {
            if (referenced.Count > 0)
                Directory.CreateDirectory(directory);
            foreach (var image in referenced)
            {
                if (image.Persisted)
                {
                    if (!File.ReadAllBytes(image.Path).AsSpan().SequenceEqual(image.Content))
                        throw new IOException($"The attached image changed outside this editor: {image.Path}");
                    continue;
                }
                using var stream = new FileStream(image.Path, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough);
                created.Add(image.Path);
                stream.Write(image.Content);
                stream.Flush(true);
            }
            writePrompt(text);
            foreach (var image in referenced)
                image.Persisted = true;
            completed = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
                                      or NotSupportedException or System.Security.SecurityException)
        {
            failure = error;
            throw;
        }
        finally
        {
            if (!completed)
            {
                var cleanupErrors = new List<Exception>();
                foreach (var path in created)
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                                  or System.Security.SecurityException)
                    {
                        cleanupErrors.Add(new IOException($"Could not remove \"{path}\": {error.Message}", error));
                    }
                }
                if (cleanupErrors.Count > 0)
                    throw new IOException(
                        (failure == null ? "" : failure.Message + "\n\n") +
                        "Some unpublished image copies could not be removed. Remove them before retrying:\n" +
                        string.Join("\n", cleanupErrors.Select(error => error.Message)),
                        new AggregateException(cleanupErrors));
            }
        }
    }

    public void Dispose()
    {
        foreach (var image in images)
            image.Dispose();
    }
}
