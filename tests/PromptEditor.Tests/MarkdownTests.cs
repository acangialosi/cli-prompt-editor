using Xunit;

namespace PromptEditor.Tests;

public class MarkdownTests
{
    [Fact]
    public void BoldKeepsSelectionAndToggles()
    {
        var edit = Markdown.Wrap("hello world", 6, 5, "**");
        var text = Apply("hello world", edit);
        Assert.Equal("hello **world**", text);
        Assert.Equal("world", text.Substring(edit.SelectionStart, edit.SelectionLength));
        Assert.Equal("hello world", Apply(text, Markdown.Wrap(text, edit.SelectionStart, edit.SelectionLength, "**")));
    }

    [Fact]
    public void SelectedMarkersCanBeRemoved() =>
        Assert.Equal("hello", Apply("**hello**", Markdown.Wrap("**hello**", 0, 9, "**")));

    [Fact]
    public void EmptySelectionInsertsSelectedPlaceholder()
    {
        var edit = Markdown.Wrap("", 0, 0, "*");
        Assert.Equal("*text*", edit.Replacement);
        Assert.Equal(1, edit.SelectionStart);
        Assert.Equal(4, edit.SelectionLength);
    }

    [Theory]
    [InlineData("- ")]
    [InlineData("## ")]
    [InlineData("> ")]
    [InlineData("- [ ] ")]
    public void PrefixesWholeSelectedLinesAndToggles(string prefix)
    {
        const string text = "one\ntwo\nthree";
        var edit = Markdown.PrefixLines(text, 1, 7, prefix);
        var formatted = Apply(text, edit);
        Assert.Equal(prefix + "one\n" + prefix + "two\nthree", formatted);
        Assert.Equal(text, Apply(formatted,
            Markdown.PrefixLines(formatted, edit.SelectionStart, edit.SelectionLength, prefix)));
    }

    [Fact]
    public void CursorAtEndOfTrailingNewlineFormatsEmptyLine() =>
        Assert.Equal("one\n- ", Apply("one\n", Markdown.PrefixLines("one\n", 4, 0, "- ")));

    [Fact]
    public void CodeBlockUsesLongerFenceThanEmbeddedCode()
    {
        const string code = "```\nhello\n```";
        var edit = Markdown.CodeBlock(code, 0, code.Length);
        Assert.Equal("````\n```\nhello\n```\n````", edit.Replacement);
        Assert.Equal(code, edit.Replacement.Substring(edit.SelectionStart, edit.SelectionLength));
    }

    [Fact]
    public void LinkSelectsDestination()
    {
        var edit = Markdown.Link("docs", 0, 4);
        Assert.Equal("[docs](https://)", edit.Replacement);
        Assert.Equal("https://", edit.Replacement.Substring(edit.SelectionStart, edit.SelectionLength));
    }

    private static string Apply(string text, TextEdit edit) =>
        text[..edit.Start] + edit.Replacement + text[(edit.Start + edit.Length)..];
}
