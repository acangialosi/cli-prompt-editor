using System.Text.RegularExpressions;

namespace PromptEditor;

public readonly record struct TextEdit(
    int Start, int Length, string Replacement, int SelectionStart, int SelectionLength);

public static class Markdown
{
    public static TextEdit Wrap(string text, int start, int length, string marker, string placeholder = "text")
    {
        var selected = text.Substring(start, length);
        if (length >= marker.Length * 2 &&
            selected.StartsWith(marker, StringComparison.Ordinal) &&
            selected.EndsWith(marker, StringComparison.Ordinal))
        {
            var inner = selected[marker.Length..^marker.Length];
            return new(start, length, inner, start, inner.Length);
        }

        if (start >= marker.Length && start + length + marker.Length <= text.Length &&
            text.AsSpan(start - marker.Length, marker.Length).SequenceEqual(marker) &&
            text.AsSpan(start + length, marker.Length).SequenceEqual(marker))
            return new(start - marker.Length, length + 2 * marker.Length, selected,
                start - marker.Length, length);

        var content = length == 0 ? placeholder : selected;
        return new(start, length, marker + content + marker, start + marker.Length, content.Length);
    }

    public static TextEdit PrefixLines(string text, int start, int length, string prefix)
    {
        var (from, to) = LineRange(text, start, length);
        var lines = text[from..to].Split('\n');
        var remove = lines.All(line => line.StartsWith(prefix, StringComparison.Ordinal));
        var result = string.Join("\n", lines.Select(line => remove ? line[prefix.Length..] : prefix + line));
        return new(from, to - from, result, from, result.Length);
    }

    public static TextEdit CodeBlock(string text, int start, int length)
    {
        var (from, to) = LineRange(text, start, length);
        var content = text[from..to];
        if (content.Length == 0)
            content = "code";
        var longestRun = Regex.Matches(content, "`+").Select(match => match.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longestRun + 1));
        var replacement = fence + "\n" + content + "\n" + fence;
        return new(from, to - from, replacement, from + fence.Length + 1, content.Length);
    }

    public static TextEdit Link(string text, int start, int length)
    {
        var label = length == 0 ? "link text" : text.Substring(start, length);
        var replacement = $"[{label}](https://)";
        return new(start, length, replacement, start + label.Length + 3, 8);
    }

    private static (int From, int To) LineRange(string text, int start, int length)
    {
        var from = start == 0 ? 0 : text.LastIndexOf('\n', start - 1) + 1;
        var end = start + length;
        if (length > 0 && text[end - 1] == '\n')
            end--;
        var to = text.IndexOf('\n', end);
        return (from, to < 0 ? text.Length : to);
    }
}
