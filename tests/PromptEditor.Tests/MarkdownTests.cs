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

    [Theory]
    [InlineData(false, "- one\n- two\nthree")]
    [InlineData(true, "1. one\n2. two\nthree")]
    public void ListsFormatSelectedLinesAndToggle(bool numbered, string expected)
    {
        const string text = "one\ntwo\nthree";
        var edit = Markdown.List(text, 1, 7, numbered);
        var formatted = Apply(text, edit);
        Assert.Equal(expected, formatted);
        Assert.Equal(text, Apply(formatted,
            Markdown.List(formatted, edit.SelectionStart, edit.SelectionLength, numbered)));
    }

    [Theory]
    [InlineData("- one\n- two", true, "1. one\n2. two")]
    [InlineData("3. one\n4. two", false, "- one\n- two")]
    [InlineData("3. one\n4. two", true, "one\ntwo")]
    [InlineData("", true, "1. ")]
    [InlineData("", false, "- ")]
    public void ListStylesReplaceExistingMarkers(string text, bool numbered, string expected) =>
        Assert.Equal(expected, Apply(text, Markdown.List(text, 0, text.Length, numbered)));

    [Theory]
    [InlineData("one\ntwo", 1, "# one\n# two")]
    [InlineData("# one\n## two", 3, "### one\n### two")]
    [InlineData("## one\n## two", 2, "one\ntwo")]
    [InlineData("###### one\n## two", 0, "one\ntwo")]
    [InlineData("", 2, "## ")]
    public void HeadingStylesReplaceAndToggle(string text, int level, string expected) =>
        Assert.Equal(expected, Apply(text, Markdown.Heading(text, 0, text.Length, level)));

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void HeadingRejectsInvalidLevels(int level) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Markdown.Heading("", 0, 0, level));

    [Theory]
    [InlineData("- item", "- item\n- ")]
    [InlineData("  * item", "  * item\n  * ")]
    [InlineData("3. item", "3. item\n4. ")]
    [InlineData("8) item", "8) item\n9) ")]
    [InlineData("- [x] done", "- [x] done\n- [ ] ")]
    [InlineData("> quote", "> quote\n> ")]
    [InlineData("> > nested", "> > nested\n> > ")]
    [InlineData("- ", "")]
    [InlineData("> ", "")]
    [InlineData("previous\n1. ", "previous\n")]
    public void RichTextEnterContinuesOrEndsMarkdownListsAndQuotes(string text, string expected)
    {
        var edit = Markdown.ContinueLine(text, text.Length, 0);
        Assert.NotNull(edit);
        var result = Apply(text, edit.Value);
        Assert.Equal(expected, result);
        Assert.Equal(result.Length, edit.Value.SelectionStart);
    }

    [Theory]
    [InlineData("plain", 5, 0)]
    [InlineData("- item", 0, 0)]
    [InlineData("- item", 2, 2)]
    public void OrdinaryOrSelectedTextUsesNormalEnter(string text, int start, int length) =>
        Assert.Null(Markdown.ContinueLine(text, start, length));

    private static string Apply(string text, TextEdit edit) =>
        text[..edit.Start] + edit.Replacement + text[(edit.Start + edit.Length)..];
}
