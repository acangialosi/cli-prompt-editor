using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Xunit;

namespace PromptEditor.Tests;

public sealed class EditorFormTests
{
    [Fact]
    public void EnterInsertsNewlineWithoutWritingOrClosing() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, path) =>
        {
            editor.Select(editor.TextLength, 0);
            Assert.True(editor.Multiline);
            SendMessage(editor.Handle, 0x0100, (nint)Keys.Enter, 1);
            SendMessage(editor.Handle, 0x0102, (nint)'\r', 1); // WM_CHAR, directed only to this control.
            SendMessage(editor.Handle, 0x0101, (nint)Keys.Enter, 1);
            Assert.Equal("original\n", editor.Text);
            Assert.True(form.Visible);
            Assert.Equal("original", File.ReadAllText(path));
            editor.Undo();
            Assert.Equal("original", editor.Text);
        });
    });

    [Fact]
    public void UsePromptButtonWritesUnicodeAndCloses() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, path) =>
        {
            editor.Text = "new \u4e16\u754c \ud83d\ude80\n\n## Context";
            var button = Descendants(form).OfType<Button>().Single(control => control.Text == "Submit Prompt (Ctrl+Enter)");
            button.PerformClick();
            Assert.False(form.Visible);
            Assert.Equal("new \u4e16\u754c \ud83d\ude80\r\n\r\n## Context", File.ReadAllText(path));
        });
    });

    [Fact]
    public void CtrlEnterAcceptsButOrdinaryEnterHasNoFormCommand() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, path) =>
        {
            editor.Text = "changed";
            Assert.True(Command(form, Keys.Control | Keys.Enter));
            Assert.False(form.Visible);
            Assert.Equal("changed", File.ReadAllText(path));
        });
    });

    [Fact]
    public void UnchangedDoubleEscapeDoesNotWriteAndThereIsNoCancelButton() => OnUiThread(() =>
    {
        WithEditor("original\r\n", (form, _, path) =>
        {
            Assert.DoesNotContain(Descendants(form).OfType<Button>(), control => control.Text == "Cancel");
            Command(form, Keys.Escape);
            Command(form, Keys.Escape);
            Assert.False(form.Visible);
            Assert.Equal("original\r\n", File.ReadAllText(path));
        });
    });

    [Fact]
    public void FormattingUsesPlainMarkdownAndCanBeUndone() => OnUiThread(() =>
    {
        WithEditor("hello world", (form, editor, path) =>
        {
            editor.Select(6, 5);
            var toolbar = Descendants(form).OfType<ToolStrip>()
                .Single(strip => strip.Items.OfType<ToolStripButton>().Any(button => button.Text == "Bold"));
            toolbar.Items.OfType<ToolStripButton>().Single(button => button.Text == "Bold").PerformClick();
            Assert.Equal("hello **world**", editor.Text);
            Assert.Equal("world", editor.SelectedText);
            editor.Undo();
            Assert.Equal("hello world", editor.Text);
            Assert.Equal("hello world", File.ReadAllText(path));
        });
    });

    [Fact]
    public void LargePromptIsNotTruncated() => OnUiThread(() =>
    {
        var text = string.Concat(Enumerable.Repeat("context \u4e16\u754c \ud83d\ude80\n", 20000));
        WithEditor(text, (form, editor, path) =>
        {
            Assert.Equal(text, editor.Text);
            editor.Select(editor.TextLength, 0);
            editor.SelectedText = "\nlast piece";
            Assert.True(Command(form, Keys.Control | Keys.Enter));
            Assert.Equal(text + "\nlast piece", File.ReadAllText(path));
        });
    });

    [Fact]
    public void ToolbarUsesAccessibleIconsTooltipsAndNoIntroductoryHeadings() => OnUiThread(() =>
    {
        WithEditor("text", (form, editor, _) =>
        {
            var toolbar = FormattingToolbar(form);
            foreach (var name in new[] { "Bold", "Italic", "Link", "Quote", "Code", "Code block", "Undo", "Redo", "Find" })
            {
                var button = Descendants(form).OfType<ToolStrip>().SelectMany(strip => strip.Items.OfType<ToolStripButton>())
                    .Single(item => item.Text == name);
                Assert.Equal(ToolStripItemDisplayStyle.Image, button.DisplayStyle);
                Assert.NotNull(button.Image);
                Assert.Equal(name, button.AccessibleName);
            }
            foreach (var strip in Descendants(form).OfType<ToolStrip>())
                AssertTooltips(strip.Items);
            var tooltips = (ToolTip)typeof(EditorForm)
                .GetField("tooltips", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            foreach (var control in Descendants(form).Where(control => control is Button or Label or RichTextBox))
                Assert.False(string.IsNullOrWhiteSpace(tooltips.GetToolTip(control)));
            Assert.DoesNotContain(Descendants(form).OfType<Label>(),
                label => label.Text.Contains("A little room") || label.Text.Contains("Compose in Markdown"));
            Assert.True(editor.Height > 500);
        });
    });

    [Fact]
    public void ToolbarAndFindStayCenteredWhenResized() => OnUiThread(() =>
    {
        WithEditor("text", (form, _, _) =>
        {
            foreach (var size in new[] { form.MinimumSize, new Size(1080, 780), new Size(1400, 900) })
            {
                form.Size = size;
                Command(form, Keys.Control | Keys.F);
                Application.DoEvents();
                foreach (var strip in Descendants(form).OfType<ToolStrip>().Where(strip => strip is not StatusStrip))
                {
                    var parent = Assert.IsType<TableLayoutPanel>(strip.Parent);
                    Assert.InRange(Math.Abs(strip.Left + strip.Width / 2 - parent.ClientSize.Width / 2), 0, 2);
                    Assert.DoesNotContain(strip.Items.Cast<ToolStripItem>(), item => item.IsOnOverflow);
                }
                Command(form, Keys.Escape);
            }
        });
    });

    [Fact]
    public void NumberHeadingLinkAndHistoryControlsEditMarkdown() => OnUiThread(() =>
    {
        WithEditor("one\ntwo", (form, editor, _) =>
        {
            var toolbar = FormattingToolbar(form);
            var actions = UtilityToolbar(form);
            var lists = toolbar.Items.OfType<ToolStripDropDownButton>().Single(item => item.AccessibleName == "Lists");
            editor.SelectAll();
            lists.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Numbers").PerformClick();
            Assert.Equal("1. one\n2. two", editor.Text);
            actions.Items.OfType<ToolStripButton>().Single(item => item.Text == "Undo").PerformClick();
            Assert.Equal("one\ntwo", editor.Text);
            actions.Items.OfType<ToolStripButton>().Single(item => item.Text == "Redo").PerformClick();
            Assert.Equal("1. one\n2. two", editor.Text);
            editor.SelectAll();
            lists.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Bullets").PerformClick();
            Assert.Equal("- one\n- two", editor.Text);

            editor.Text = "title";
            var headings = toolbar.Items.OfType<ToolStripDropDownButton>().Single(item => item.AccessibleName == "Heading");
            headings.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Heading 1").PerformClick();
            Assert.Equal("# title", editor.Text);
            Assert.Equal("H1", headings.Text);
            headings.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Heading 3").PerformClick();
            Assert.Equal("### title", editor.Text);
            Assert.Equal("H3", headings.Text);
            headings.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Text == "Normal text").PerformClick();
            Assert.Equal("title", editor.Text);
            Assert.Equal("Text", headings.Text);
            editor.SelectAll();
            toolbar.Items.OfType<ToolStripButton>().Single(item => item.Text == "Link").PerformClick();
            Assert.Equal("[title](https://)", editor.Text);
            Assert.Equal("https://", editor.SelectedText);
        });
    });

    [Fact]
    public void ImagePasteAddsAReferenceAndPreviewWithUndoRedoAndRemoval() => OnUiThread(() =>
    {
        WithEditor("beforeafter", (form, editor, path) =>
        {
            editor.Select(6, 0);
            Assert.True(Command(form, Keys.Control | Keys.V));
            var reference = Regex.Match(editor.Text, "@\"[^\"]+\"").Value;
            Assert.NotEmpty(reference);
            Assert.Equal($"before {reference} after", editor.Text);
            var preview = Assert.Single(Descendants(form).OfType<PictureBox>());
            Assert.True(preview.Visible);
            var strip = Assert.IsType<FlowLayoutPanel>(preview.Parent!.Parent);
            Assert.True(strip.Height >= 80);
            Assert.True(editor.Bottom <= strip.Top);
            form.Size = form.MinimumSize;
            Application.DoEvents();
            Assert.True(editor.Height > 100);
            Assert.True(preview.Visible);
            var use = Descendants(form).OfType<Button>().Single(button => button.Text == "Submit Prompt (Ctrl+Enter)");
            Assert.True(use.Right <= use.Parent!.ClientSize.Width);
            Assert.True(use.Width >= use.GetPreferredSize(Size.Empty).Width);
            Assert.Equal("beforeafter", File.ReadAllText(path));
            Assert.False(Directory.Exists(path + ".attachments"));
            editor.Undo();
            Assert.Equal("beforeafter", editor.Text);
            Assert.Empty(Descendants(form).OfType<PictureBox>());
            editor.Redo();
            Assert.Single(Descendants(form).OfType<PictureBox>());
            var remove = Descendants(form).OfType<Button>()
                .Single(button => button.AccessibleName == "Remove Screenshot.png");
            Assert.Empty(remove.Text);
            Assert.True(remove.Width <= 30);
            Assert.True(remove.Top < Descendants(form).OfType<PictureBox>().Single().Top);
            Assert.Empty(remove.Parent!.Controls.OfType<Label>());
            remove.PerformClick();
            Assert.DoesNotContain(reference, editor.Text);
            Assert.Empty(Descendants(form).OfType<PictureBox>());
            editor.Undo();
            Assert.Contains(reference, editor.Text);
            Assert.Single(Descendants(form).OfType<PictureBox>());
        }, ScreenshotClipboard);
    });

    [Fact]
    public void ImageAcceptanceWritesImageBeforeReturningPromptAndRetainsItAfterClose() => OnUiThread(() =>
    {
        WithEditor("describe ", (form, editor, path) =>
        {
            Assert.True(Command(form, Keys.Shift | Keys.Insert));
            var imagePath = Regex.Match(editor.Text, "@\"([^\"]+)\"").Groups[1].Value;
            var text = editor.Text;
            Assert.False(File.Exists(imagePath));
            Assert.True(Command(form, Keys.Control | Keys.Enter));
            Assert.False(form.Visible);
            Assert.Equal(text, PromptDocument.Normalize(File.ReadAllText(path)));
            Assert.True(File.Exists(imagePath));
            using var saved = Image.FromFile(imagePath);
            Assert.Equal(new Size(40, 30), saved.Size);
        }, ScreenshotClipboard);
    });

    [Fact]
    public void ReopenedThumbnailsFitTheStripAtTheCurrentDpi() => OnUiThread(() =>
    {
        WithEditor("describe ", (form, _, path) =>
        {
            Command(form, Keys.Control | Keys.V);
            Command(form, Keys.Control | Keys.Enter);
            using var reopened = new EditorForm(PromptDocument.Open(path),
                new ImageAttachments(path + ".attachments"), Clipboard.GetDataObject);
            reopened.Show();
            Application.DoEvents();
            var preview = Assert.Single(Descendants(reopened).OfType<PictureBox>());
            var card = Assert.IsType<Panel>(preview.Parent);
            var strip = Assert.IsType<FlowLayoutPanel>(card.Parent);
            Assert.Equal(reopened.LogicalToDeviceUnits(new Size(114, 88)), card.Size);
            Assert.True(card.ClientRectangle.Contains(preview.Bounds));
            Assert.True(card.Bottom <= strip.ClientSize.Height);
            Assert.False(strip.VerticalScroll.Visible);
            Command(reopened, Keys.Escape);
            Command(reopened, Keys.Escape);
        }, ScreenshotClipboard);
    });

    [Fact]
    public void TextOnlyPasteDoesNotAddAnImageEvenWhenClipboardContainsBoth() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, path) =>
        {
            editor.SelectAll();
            Assert.True(Command(form, Keys.Control | Keys.Shift | Keys.V));
            Assert.Equal("plain\ntext", editor.Text);
            Assert.Empty(Descendants(form).OfType<PictureBox>());
            Assert.False(Directory.Exists(path + ".attachments"));
            Assert.Equal("original", File.ReadAllText(path));
        }, () =>
        {
            var data = new DataObject(DataFormats.UnicodeText, "plain\r\ntext");
            data.SetData("PNG", new byte[] { 1, 2, 3 });
            return data;
        });
    });

    [Fact]
    public void OrdinaryTextPasteStillUsesPlainTextAndCanBeUndone() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, _) =>
        {
            editor.SelectAll();
            Assert.True(Command(form, Keys.Control | Keys.V));
            Assert.Equal("plain\ntext", editor.Text);
            editor.Undo();
            Assert.Equal("original", editor.Text);
        }, () =>
        {
            var data = new DataObject(DataFormats.UnicodeText, "plain\r\ntext");
            data.SetData(DataFormats.Rtf, @"{\rtf1\b rich\b0}");
            return data;
        });
    });

    [Fact]
    public void CancellingAfterUndoingImagePasteLeavesNoImageFiles() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, path) =>
        {
            Command(form, Keys.Control | Keys.V);
            editor.Undo();
            Command(form, Keys.Escape);
            Command(form, Keys.Escape);
            Assert.False(form.Visible);
            Assert.Equal("original", File.ReadAllText(path));
            Assert.False(Directory.Exists(path + ".attachments"));
        }, ScreenshotClipboard);
    });

    [Fact]
    public void FormattingControlsMatchNotepadOrderAndSpacing() => OnUiThread(() =>
    {
        WithEditor("text", (form, _, _) =>
        {
            var toolbar = FormattingToolbar(form);
            Assert.Equal(new[] { "Heading", "Lists", "Bold", "Italic", "Link", "Quote", "Code", "Code block",
                    "/fleet", "/refine" },
                toolbar.Items.Cast<ToolStripItem>().Select(item => item.AccessibleName));
            Assert.Empty(toolbar.Items.OfType<ToolStripSeparator>());
            var buttons = toolbar.Items.OfType<ToolStripButton>()
                .Where(button => button.DisplayStyle == ToolStripItemDisplayStyle.Image).ToArray();
            Assert.Equal(6, buttons.Length);
            foreach (var button in buttons)
                Assert.Equal(new Size(44, 36), button.Size);
            for (var index = 1; index < buttons.Length; index++)
                Assert.Equal(48, buttons[index].Bounds.Left - buttons[index - 1].Bounds.Left);
            Assert.True(UtilityToolbar(form).Top >= toolbar.Bottom);
        });
    });

    [Theory]
    [InlineData("/fleet", false, "")]
    [InlineData("/fleet", true, "")]
    [InlineData("/refine", false, "")]
    [InlineData("/refine", true, "")]
    [InlineData("/fleet", false, "Fix **this**\n@\"C:\\images\\screenshot.png\" \u4e16\u754c")]
    [InlineData("/fleet", true, "Fix **this**\n@\"C:\\images\\screenshot.png\" \u4e16\u754c")]
    [InlineData("/refine", false, "Fix **this**\n@\"C:\\images\\screenshot.png\" \u4e16\u754c")]
    [InlineData("/refine", true, "Fix **this**\n@\"C:\\images\\screenshot.png\" \u4e16\u754c")]
    public void SlashButtonsPrependWithoutRunningOrSavingAndSupportUndo(
        string command, bool richText, string original) => OnUiThread(() =>
    {
        WithEditor(original, (form, editor, path) =>
        {
            Assert.IsType<MarkdownRichTextBox>(editor).RichTextMode = richText;
            var start = original.Length > 0 ? 1 : 0;
            var length = original.Length > 0 ? 2 : 0;
            editor.Select(start, length);
            var button = FormattingToolbar(form).Items.OfType<ToolStripButton>()
                .Single(item => item.Text == command);
            button.PerformClick();
            Assert.Equal(command + " " + original, editor.Text);
            Assert.Equal(start + command.Length + 1, editor.SelectionStart);
            Assert.Equal(length, editor.SelectionLength);
            Assert.True(editor.Focused);
            Assert.True(form.Visible);
            Assert.Equal(original, File.ReadAllText(path));
            editor.Undo();
            Assert.Equal(original, editor.Text);
            editor.Redo();
            Assert.Equal(command + " " + original, editor.Text);
            Command(form, Keys.Control | Keys.Enter);
            Assert.Equal(command + " " + original, PromptDocument.Open(path).Text);
        });
    });

    [Theory]
    [InlineData("Quote", "> text")]
    [InlineData("Code", "`text`")]
    [InlineData("Code block", "```\ntext\n```")]
    public void FormerMoreCommandsUseIconButtonsAndCanBeUndone(string command, string expected) => OnUiThread(() =>
    {
        WithEditor("text", (form, editor, _) =>
        {
            editor.SelectAll();
            FormattingToolbar(form).Items.OfType<ToolStripButton>()
                .Single(button => button.Text == command).PerformClick();
            Assert.Equal(expected, editor.Text);
            editor.Undo();
            Assert.Equal("text", editor.Text);
        });
    });

    [Fact]
    public void RichTextIsDefaultAndKeepsMarkdownSourceAndUndoHistory() => OnUiThread(() =>
    {
        const string source = "# Heading\n\n**bold** and *italic* with `code`";
        WithEditor(source, (form, editor, path) =>
        {
            var richEditor = Assert.IsType<MarkdownRichTextBox>(editor);
            Assert.True(richEditor.RichTextMode);
            Assert.Equal(source, editor.Text);
            editor.Select(source.IndexOf("bold", StringComparison.Ordinal), 4);
            Assert.True(editor.SelectionFont!.Bold);
            Assert.Contains("\\v", editor.Rtf);
            var actions = UtilityToolbar(form).Items.OfType<ToolStripButton>().ToArray();
            actions.Single(button => button.Text == "Markdown").PerformClick();
            Assert.False(richEditor.RichTextMode);
            Assert.Equal(source, editor.Text);
            Assert.False(editor.SelectionFont!.Bold);
            editor.Select(editor.TextLength, 0);
            editor.SelectedText = " more";
            actions.Single(button => button.Text == "Rich Text").PerformClick();
            Assert.True(richEditor.RichTextMode);
            Assert.Equal(source + " more", editor.Text);
            editor.Undo();
            Assert.Equal(source, editor.Text);
            editor.Redo();
            Assert.Equal(source + " more", editor.Text);
            Assert.Equal(source, File.ReadAllText(path));
        });
    });

    [Theory]
    [InlineData("# Heading", "Heading", true, false, "Segoe UI")]
    [InlineData("**bold**", "bold", true, false, "Segoe UI")]
    [InlineData("*italic*", "italic", false, true, "Segoe UI")]
    [InlineData("***both***", "both", true, true, "Segoe UI")]
    [InlineData("`**literal**`", "literal", false, false, "Consolas")]
    [InlineData("```md\n**literal**\n```", "literal", false, false, "Consolas")]
    [InlineData("Hello \ud83d\ude80 \u4e16\u754c **bold**", "bold", true, false, "Segoe UI")]
    public void RichTextRendersMarkdownWithoutChangingIt(string source, string content,
        bool bold, bool italic, string family) => OnUiThread(() =>
    {
        WithEditor(source, (_, editor, _) =>
        {
            editor.Select(source.IndexOf(content, StringComparison.Ordinal), content.Length);
            using var font = editor.SelectionFont!;
            Assert.Equal(bold, font.Bold);
            Assert.Equal(italic, font.Italic);
            Assert.Equal(family, font.FontFamily.Name);
            Assert.Equal(source, editor.Text);
        });
    });

    [Theory]
    [InlineData("Heading\n=======")]
    [InlineData("#")]
    [InlineData("**unfinished")]
    [InlineData("```\nunterminated")]
    [InlineData("[**nested label**](https://example.com/a_(b))")]
    [InlineData("| heading | column |\n| --- | --- |\n| text | value |")]
    [InlineData("<script>literal HTML, never executed</script>")]
    [InlineData("@\"C:\\Users\\you\\Attachments\\image.png\"")]
    [InlineData("- outer\n  - nested\n\n  paragraph\n\n  ```\n  code\n  ```")]
    public void SwitchingViewsAndAcceptingNeverRewritesMarkdown(string source) => OnUiThread(() =>
    {
        WithEditor(source, (form, editor, path) =>
        {
            editor.Select(Math.Min(2, editor.TextLength), Math.Min(3, Math.Max(0, editor.TextLength - 2)));
            var selection = (editor.SelectionStart, editor.SelectionLength);
            foreach (var name in new[] { "Markdown", "Rich Text", "Markdown", "Rich Text" })
            {
                UtilityToolbar(form).Items.OfType<ToolStripButton>().Single(button => button.Text == name).PerformClick();
                Assert.Equal(source, editor.Text);
                Assert.Equal(selection, (editor.SelectionStart, editor.SelectionLength));
                Assert.Equal("Prompt Editor", form.Text);
                Assert.False(editor.CanUndo);
            }
            Command(form, Keys.Control | Keys.Enter);
            Assert.Equal(source, File.ReadAllText(path));
        });
    });

    [Fact]
    public void RichTextSyntaxIsHiddenVisuallyAndSourceModeRevealsIt() => OnUiThread(() =>
    {
        WithEditor("**bold**", (form, editor, _) =>
        {
            Assert.Equal(editor.GetPositionFromCharIndex(0), editor.GetPositionFromCharIndex(2));
            UtilityToolbar(form).Items.OfType<ToolStripButton>()
                .Single(button => button.Text == "Markdown").PerformClick();
            Assert.True(editor.GetPositionFromCharIndex(2).X > editor.GetPositionFromCharIndex(0).X);
        });
    });

    [Fact]
    public void RichTextTypingInsideBoldPreservesMarkdownAndUndo() => OnUiThread(() =>
    {
        WithEditor("**bold**", (_, editor, _) =>
        {
            editor.Select(6, 0);
            SendMessage(editor.Handle, 0x0102, (nint)'!', 1);
            Assert.Equal("**bold!**", editor.Text);
            Assert.IsType<MarkdownRichTextBox>(editor).RefreshPresentation();
            editor.Select(6, 1);
            Assert.True(editor.SelectionFont!.Bold);
            editor.Undo();
            Assert.Equal("**bold**", editor.Text);
        });
    });

    [Theory]
    [InlineData("- item", "- item\n- ")]
    [InlineData("> quote", "> quote\n> ")]
    [InlineData("- ", "")]
    [InlineData("```\n- code\n```", "```\n- code\n\n```")]
    public void RichTextEnterWritesMarkdownNotJustParagraphFormatting(string source, string expected) => OnUiThread(() =>
    {
        WithEditor(source, (_, editor, _) =>
        {
            editor.Select(source.StartsWith("```", StringComparison.Ordinal) ? source.LastIndexOf('\n') : source.Length, 0);
            SendMessage(editor.Handle, 0x0100, (nint)Keys.Enter, 1);
            SendMessage(editor.Handle, 0x0102, (nint)'\r', 1);
            SendMessage(editor.Handle, 0x0101, (nint)Keys.Enter, 1);
            Assert.Equal(expected, editor.Text);
            editor.Undo();
            Assert.Equal(source, editor.Text);
        });
    });

    [Fact]
    public void SubmitPromptLabelIsCenteredAndFitsAtEveryWindowSize() => OnUiThread(() =>
    {
        WithEditor("text", (form, _, _) =>
        {
            var button = Descendants(form).OfType<Button>().Single(control => control.Text == "Submit Prompt (Ctrl+Enter)");
            foreach (var size in new[] { form.MinimumSize, new Size(1080, 780) })
            {
                form.Size = size;
                Application.DoEvents();
                var text = TextRenderer.MeasureText(button.Text, button.Font);
                Assert.False(button.AutoSize);
                Assert.Equal(ContentAlignment.MiddleCenter, button.TextAlign);
                Assert.True(button.ClientSize.Width >= text.Width);
                Assert.True(button.ClientSize.Height >= text.Height);
                Assert.True(button.Parent!.ClientRectangle.Contains(button.Bounds));
            }
        });
    });

    [Fact]
    public void ModeSwitchPreservesImagePreviewsAndSavesMarkdownReferences() => OnUiThread(() =>
    {
        WithEditor("describe ", (form, editor, path) =>
        {
            Command(form, Keys.Control | Keys.V);
            var source = editor.Text;
            var preview = Assert.Single(Descendants(form).OfType<PictureBox>());
            foreach (var name in new[] { "Markdown", "Rich Text" })
            {
                UtilityToolbar(form).Items.OfType<ToolStripButton>().Single(button => button.Text == name).PerformClick();
                Assert.Equal(source, editor.Text);
                Assert.Same(preview, Assert.Single(Descendants(form).OfType<PictureBox>()));
            }
            editor.Undo();
            Assert.Equal("describe ", editor.Text);
            editor.Redo();
            Assert.Equal(source, editor.Text);
            Command(form, Keys.Control | Keys.Enter);
            Assert.Equal(source, PromptDocument.Normalize(File.ReadAllText(path)));
            Assert.True(File.Exists(Regex.Match(source, "@\"([^\"]+)\"").Groups[1].Value));
        }, ScreenshotClipboard);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SwitchingModesPreservesOriginalEncodingBomAndMixedNewlines(bool utf16) => OnUiThread(() =>
    {
        WithEditor("", (form, _, path) =>
        {
            Command(form, Keys.Escape);
            Command(form, Keys.Escape);
            var encoding = utf16 ? (System.Text.Encoding)System.Text.Encoding.Unicode : new System.Text.UTF8Encoding(true);
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes("# Heading\r\n\r\n**context**\n\u4e16\u754c")).ToArray();
            File.WriteAllBytes(path, bytes);
            using var reopened = new EditorForm(PromptDocument.Open(path),
                new ImageAttachments(path + ".attachments"), Clipboard.GetDataObject);
            reopened.Show();
            foreach (var name in new[] { "Markdown", "Rich Text" })
                UtilityToolbar(reopened).Items.OfType<ToolStripButton>().Single(button => button.Text == name).PerformClick();
            Command(reopened, Keys.Control | Keys.Enter);
            Assert.Equal(bytes, File.ReadAllBytes(path));
        });
    });

    [Fact]
    public void LinkDestinationStaysVisibleWhileTypingItsUrl() => OnUiThread(() =>
    {
        WithEditor("docs", (form, editor, _) =>
        {
            editor.SelectAll();
            FormattingToolbar(form).Items.OfType<ToolStripButton>().Single(button => button.Text == "Link").PerformClick();
            Assert.Equal("https://", editor.SelectedText);
            editor.SelectedText = "https://example.com";
            var richEditor = Assert.IsType<MarkdownRichTextBox>(editor);
            richEditor.RefreshPresentation();
            var urlStart = editor.Text.IndexOf("https", StringComparison.Ordinal);
            Assert.True(editor.GetPositionFromCharIndex(urlStart + 5).X > editor.GetPositionFromCharIndex(urlStart).X);
            editor.Select(1, 0);
            richEditor.RefreshPresentation();
            Assert.Equal(editor.GetPositionFromCharIndex(urlStart), editor.GetPositionFromCharIndex(urlStart + 5));
        });
    });

    [Fact]
    public void SingleEscapeKeepsContextAndDoubleEscapeCancels() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, path) =>
        {
            Assert.True(Command(form, Keys.Escape));
            Assert.True(form.Visible);
            Assert.Equal("original", editor.Text);
            Assert.Equal("original", File.ReadAllText(path));
            Assert.True(Command(form, Keys.Escape));
            Assert.False(form.Visible);
            Assert.Equal("original", File.ReadAllText(path));
        });
    });

    [Fact]
    public void SlowEscapesAndHeldEscapeDoNotCancel() => OnUiThread(() =>
    {
        WithEditor("original", (form, _, _) =>
        {
            Command(form, Keys.Escape);
            typeof(EditorForm).GetField("escapePressedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(form, Environment.TickCount64 - 2000);
            Command(form, Keys.Escape);
            Assert.True(form.Visible);
            Command(form, Keys.Escape, repeated: true);
            Assert.True(form.Visible);
            Command(form, Keys.Escape);
            Assert.False(form.Visible);
        });
    });

    [Fact]
    public void EscapeClosesFindWithoutArmingCancellation() => OnUiThread(() =>
    {
        WithEditor("original", (form, _, _) =>
        {
            Command(form, Keys.Control | Keys.F);
            Command(form, Keys.Escape);
            Assert.True(form.Visible);
            Assert.Null(typeof(EditorForm).GetField("escapePressedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form));
            Command(form, Keys.Escape);
            Assert.True(form.Visible);
            Command(form, Keys.Escape);
            Assert.False(form.Visible);
        });
    });

    [Fact]
    public void OtherKeysAndEditsDisarmEscapeCancellation() => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, _) =>
        {
            var state = typeof(EditorForm).GetField("escapePressedAt", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Command(form, Keys.Escape);
            Command(form, Keys.Left);
            Assert.Null(state.GetValue(form));
            Command(form, Keys.Escape);
            editor.SelectedText = "changed";
            Assert.Null(state.GetValue(form));
            editor.Text = "original";
            Command(form, Keys.Escape);
            Assert.True(form.Visible);
            Command(form, Keys.Escape);
            Assert.False(form.Visible);
        });
    });

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("abcd", 1, 1)]
    [InlineData("abcde", 1, 2)]
    [InlineData("hello world", 2, 3)]
    [InlineData("\u4e16\u754c", 1, 1)]
    public void StatusIncludesClearlyApproximateTextTokenCount(string text, int words, int tokens) => OnUiThread(() =>
    {
        WithEditor("original", (form, editor, _) =>
        {
            editor.Text = text;
            typeof(EditorForm).GetMethod("UpdateCounts", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            var counts = (ToolStripStatusLabel)typeof(EditorForm)
                .GetField("counts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
            Assert.Equal($"{words:N0} words  |  {text.Length:N0} characters  |  ~{tokens:N0} tokens", counts.Text);
            Assert.Contains("excludes image tokens", counts.ToolTipText);
        });
    });

    [Fact]
    public void RefineLocksEditingThenReplacesThePromptAsOneUndoableEdit() => OnUiThread(() =>
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? sent = null;
        WithEditor("fix bug", (form, editor, path) =>
        {
            form.Size = form.MinimumSize;
            var button = RefineButton(form);
            Assert.True(Command(form, Keys.Control | Keys.Shift | Keys.R));
            Application.DoEvents();
            Assert.Equal("fix bug", sent);
            Assert.Equal("Cancel Refine", button.Text);
            Assert.False(button.IsOnOverflow);
            Assert.True(editor.ReadOnly);
            Assert.All(FormattingToolbar(form).Items.Cast<ToolStripItem>(), item => Assert.False(item.Enabled));
            Assert.False(UtilityToolbar(form).Items.OfType<ToolStripButton>().Single(item => item.Text == "Undo").Enabled);
            Assert.True(Command(form, Keys.Control | Keys.V));
            Assert.True(Command(form, Keys.Control | Keys.Z));
            Assert.Equal("fix bug", editor.Text);

            pending.SetResult("Fix the login bug in `auth.ts`.\r\n");
            PumpUntil(() => !editor.ReadOnly);
            Assert.Equal("Fix the login bug in `auth.ts`.\n", editor.Text);
            Assert.Equal("Refine", button.Text);
            Assert.True(FormattingToolbar(form).Items.Cast<ToolStripItem>().All(item => item.Enabled));
            Assert.Equal("Refined with Copilot. Ctrl+Z restores your draft.", Notice(form));
            Assert.Equal("fix bug", File.ReadAllText(path));
            Assert.Empty(Descendants(form).OfType<PictureBox>());
            editor.Undo();
            Assert.Equal("fix bug", editor.Text);
        }, ScreenshotClipboard, refine: (draft, _) =>
        {
            sent = draft;
            return pending.Task;
        });
    });

    [Fact]
    public void EscapeOrTheButtonCancelsRefineWithoutClosingOrChangingThePrompt() => OnUiThread(() =>
    {
        var calls = 0;
        WithEditor("draft", (form, editor, _) =>
        {
            var button = RefineButton(form);
            button.PerformClick();
            Assert.True(editor.ReadOnly);
            Assert.True(Command(form, Keys.Escape));
            PumpUntil(() => !editor.ReadOnly);
            Assert.True(form.Visible);
            Assert.Null(typeof(EditorForm).GetField("escapePressedAt", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(form));
            Assert.Equal("draft", editor.Text);
            Assert.Equal("Refine cancelled. Prompt unchanged.", Notice(form));

            button.PerformClick();
            Assert.Equal("Cancel Refine", button.Text);
            button.PerformClick();
            PumpUntil(() => !editor.ReadOnly);
            Assert.Equal("Refine", button.Text);
            Assert.Equal("draft", editor.Text);
            Assert.Equal(2, calls);
        }, refine: async (_, cancellation) =>
        {
            calls++;
            await Task.Delay(Timeout.Infinite, cancellation);
            return "never";
        });
    });

    [Fact]
    public void ClosingTheEditorStopsARunningRefine() => OnUiThread(() =>
    {
        var cancellation = CancellationToken.None;
        WithEditor("draft", (form, _, path) =>
        {
            Command(form, Keys.Control | Keys.Shift | Keys.R);
            Assert.True(Command(form, Keys.Control | Keys.Enter));
            Assert.False(form.Visible);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal("draft", File.ReadAllText(path));
            var running = typeof(EditorForm).GetField("refinement", BindingFlags.Instance | BindingFlags.NonPublic)!;
            PumpUntil(() => running.GetValue(form) == null);
        }, refine: async (_, token) =>
        {
            cancellation = token;
            await Task.Delay(Timeout.Infinite, token);
            return "never";
        });
    });

    [Fact]
    public void RefineFailuresAreReportedAndBlankPromptsAreNotSent() => OnUiThread(() =>
    {
        var errors = new List<string>();
        var calls = 0;
        WithEditor(" \n", (form, editor, _) =>
        {
            Assert.True(Command(form, Keys.Control | Keys.Shift | Keys.R));
            Assert.Equal(0, calls);
            Assert.Equal("Nothing to refine yet.", Notice(form));
            editor.Text = "draft";
            Command(form, Keys.Control | Keys.Shift | Keys.R);
            PumpUntil(() => errors.Count > 0);
            Assert.Equal("Copilot CLI was not found.", Assert.Single(errors));
            Assert.Equal("draft", editor.Text);
            Assert.False(editor.ReadOnly);
            Assert.Equal("Refine failed. Prompt unchanged.", Notice(form));
        }, refine: (_, _) =>
        {
            calls++;
            return Task.FromException<string>(new PromptRefinerException("Copilot CLI was not found."));
        }, showError: errors.Add);
    });

    [Fact]
    public void RefineKeepsPastedScreenshotsAndReportsUnchangedResults() => OnUiThread(() =>
    {
        var response = "Describe this screenshot.";
        WithEditor("describe ", (form, editor, _) =>
        {
            editor.Select(editor.TextLength, 0);
            Command(form, Keys.Control | Keys.V);
            var reference = Regex.Match(editor.Text, "@\"[^\"]+\"").Value;
            Assert.NotEmpty(reference);
            Command(form, Keys.Control | Keys.Shift | Keys.R);
            PumpUntil(() => !editor.ReadOnly);
            Assert.Equal("Describe this screenshot.\n\n" + reference, editor.Text);
            Assert.Single(Descendants(form).OfType<PictureBox>());

            response = editor.Text + "\n";
            Command(form, Keys.Control | Keys.Shift | Keys.R);
            PumpUntil(() => !editor.ReadOnly);
            Assert.Equal("No changes suggested.", Notice(form));
            Assert.Equal("Describe this screenshot.\n\n" + reference, editor.Text);
        }, ScreenshotClipboard, refine: (_, _) => Task.FromResult(response));
    });

    private static ToolStripButton RefineButton(EditorForm form) =>
        UtilityToolbar(form).Items.OfType<ToolStripButton>().Single(item => item.AccessibleName == "Refine with Copilot");

    private static string Notice(EditorForm form) =>
        ((Label)typeof(EditorForm).GetField("notice", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!)
        .Text;

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "Timed out waiting for the editor.");
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static IDataObject ScreenshotClipboard() =>
        new DataObject(DataFormats.Bitmap, new Bitmap(40, 30));

    private static ToolStrip FormattingToolbar(EditorForm form) =>
        Descendants(form).OfType<ToolStrip>()
            .Single(strip => strip.Items.OfType<ToolStripButton>().Any(button => button.Text == "Bold"));

    private static ToolStrip UtilityToolbar(EditorForm form) =>
        Descendants(form).OfType<ToolStrip>().Single(strip => strip.AccessibleName == "Editor actions");

    private static void AssertTooltips(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripSeparator)
                continue;
            Assert.False(string.IsNullOrWhiteSpace(item.ToolTipText), $"Missing tooltip for {item.Text}");
            if (item is ToolStripDropDownItem menu)
                AssertTooltips(menu.DropDownItems);
        }
    }

    private static bool Command(EditorForm form, Keys keys, bool repeated = false)
    {
        var method = typeof(EditorForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var message = new Message { LParam = repeated ? (nint)(1L << 30) : 0 };
        return (bool)method.Invoke(form, [message, keys])!;
    }

    private static void WithEditor(string original, Action<EditorForm, RichTextBox, string> test,
        Func<IDataObject?>? clipboard = null, Func<string, CancellationToken, Task<string>>? refine = null,
        Action<string>? showError = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prompt ui \u4e16\u754c {Guid.NewGuid():N}.md");
        File.WriteAllText(path, original);
        try
        {
            using var form = new EditorForm(PromptDocument.Open(path),
                new ImageAttachments(path + ".attachments"), clipboard ?? Clipboard.GetDataObject,
                refine ?? ((_, _) => throw new InvalidOperationException("Tests must not call Copilot.")),
                showError ?? (message => throw new InvalidOperationException(message)));
            form.Show();
            Application.DoEvents();
            var editor = Descendants(form).OfType<RichTextBox>().Single();
            test(form, editor, path);
            // Restore the original in memory so cleanup never opens a discard dialog.
            if (!form.IsDisposed)
                editor.Text = PromptDocument.Normalize(original);
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(path + ".attachments"))
                Directory.Delete(path + ".attachments", recursive: true);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void OnUiThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            // Application.Run keeps this installed in the app; bare DoEvents loops would otherwise uninstall it
            // and resume awaits on the thread pool.
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            try { action(); }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI test did not finish.");
        if (failure != null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
