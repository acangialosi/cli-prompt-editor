using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PromptEditor;

internal sealed class MarkdownRichTextBox : RichTextBox
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePreciseSourceLocation().Build();
    private readonly System.Windows.Forms.Timer renderTimer = new() { Interval = 120 };
    private ITextDocument? textDocument;
    private bool rendering;
    private bool richTextMode = true;

    public MarkdownRichTextBox()
    {
        renderTimer.Tick += (_, _) => RefreshPresentation();
    }

    public bool RichTextMode
    {
        get => richTextMode;
        set
        {
            if (richTextMode == value)
                return;
            richTextMode = value;
            RefreshPresentation();
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (rendering)
            return;
        base.OnTextChanged(e);
        renderTimer.Stop();
        renderTimer.Start();
    }

    protected override void OnSelectionChanged(EventArgs e)
    {
        if (rendering)
            return;
        base.OnSelectionChanged(e);
        if (richTextMode)
        {
            renderTimer.Stop();
            renderTimer.Start();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (richTextMode && !ReadOnly && e.KeyCode == Keys.Enter && !e.Control && !e.Alt)
        {
            var inCode = Markdig.Markdown.Parse(Text, Pipeline).Descendants().OfType<CodeBlock>()
                .Any(block => block.Span.Start <= SelectionStart && block.Span.End >= SelectionStart);
            var edit = (!inCode && !e.Shift ? Markdown.ContinueLine(Text, SelectionStart, SelectionLength) : null)
                ?? new TextEdit(SelectionStart, SelectionLength, "\n", SelectionStart + 1, 0);
            Select(edit.Start, edit.Length);
            SelectedText = edit.Replacement;
            Select(edit.SelectionStart, edit.SelectionLength);
            RefreshPresentation();
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    public void RefreshPresentation()
    {
        renderTimer.Stop();
        if (!IsHandleCreated || rendering)
            return;
        var source = Text;
        var start = SelectionStart;
        var length = SelectionLength;
        var modified = Modified;
        var scroll = new Point();
        SendMessage(Handle, 0x04DD, 0, ref scroll); // EM_GETSCROLLPOS
        var document = GetTextDocument();
        rendering = true;
        document.Undo(-9999995); // tomSuspend keeps presentation changes out of undo/redo.
        document.Freeze();
        try
        {
            SelectAll();
            SetFormat(new CharacterFormat
            {
                Mask = 0xE400010F, Effects = 0,
                Height = 240,
                TextColor = ColorTranslator.ToWin32(ForeColor),
                BackColor = ColorTranslator.ToWin32(BackColor),
                FaceName = richTextMode ? "Segoe UI" : "Consolas"
            });
            SelectionBullet = false;
            SelectionIndent = 0;
            SelectionHangingIndent = 0;
            if (richTextMode)
                Render(source, start);
        }
        finally
        {
            Select(start, length);
            if (length == 0)
                SetFormat(new CharacterFormat { Mask = 0x100, Effects = 0 });
            Modified = modified;
            SendMessage(Handle, 0x04DE, 0, ref scroll); // EM_SETSCROLLPOS
            document.Unfreeze();
            document.Undo(-9999994); // tomResume
            rendering = false;
        }
    }

    private void Render(string source, int selectionStart)
    {
        var markdown = Markdig.Markdown.Parse(source, Pipeline);
        foreach (var node in markdown.Descendants())
        {
            switch (node)
            {
                case HeadingBlock heading when heading.Inline is { } content &&
                                                content.Span.End >= content.Span.Start:
                    Format(heading.Span, new CharacterFormat
                    {
                        Mask = 0x80000001, Effects = 1,
                        Height = new[] { 440, 360, 320, 280, 260, 240 }[heading.Level - 1]
                    });
                    Hide(heading.Span.Start, content.Span.Start - heading.Span.Start);
                    Hide(content.Span.End + 1, heading.Span.End - content.Span.End);
                    break;
                case EmphasisInline emphasis:
                    var effect = emphasis.DelimiterCount == 2 ? 1u : 2u;
                    Format(emphasis.Span, new CharacterFormat { Mask = effect, Effects = effect });
                    Hide(emphasis.Span.Start, emphasis.DelimiterCount);
                    Hide(emphasis.Span.End - emphasis.DelimiterCount + 1, emphasis.DelimiterCount);
                    break;
                case CodeInline code:
                    Code(code.Span);
                    Hide(code.Span.Start, code.DelimiterCount);
                    Hide(code.Span.End - code.DelimiterCount + 1, code.DelimiterCount);
                    break;
                case FencedCodeBlock code:
                    Code(code.Span);
                    var firstLineEnd = source.IndexOf('\n', code.Span.Start);
                    if (firstLineEnd >= 0 && firstLineEnd < code.Span.End)
                        Hide(code.Span.Start, firstLineEnd - code.Span.Start + 1);
                    if (code.ClosingFencedCharCount > 0)
                    {
                        var lastLineStart = source.LastIndexOf('\n', code.Span.End) + 1;
                        Hide(lastLineStart, code.Span.End - lastLineStart + 1);
                    }
                    break;
                case LinkInline link when !link.IsImage && link.FirstChild is not null &&
                                          link.LastChild is not null && !link.IsAutoLink:
                    var labelStart = link.FirstChild.Span.Start;
                    var labelEnd = link.LastChild.Span.End + 1;
                    // Keep the link source visible while its destination is being edited.
                    if (selectionStart >= labelEnd && selectionStart <= link.Span.End)
                        break;
                    Format(link.Span, new CharacterFormat
                    {
                        Mask = 0x40000004, Effects = 4,
                        TextColor = ColorTranslator.ToWin32(Color.FromArgb(30, 91, 190))
                    });
                    Hide(link.Span.Start, labelStart - link.Span.Start);
                    Hide(labelEnd, link.Span.End - labelEnd + 1);
                    break;
                case QuoteBlock quote:
                    Format(quote.Span, new CharacterFormat
                    {
                        Mask = 0x40000000,
                        TextColor = ColorTranslator.ToWin32(Color.FromArgb(85, 91, 103))
                    });
                    SelectionIndent = LogicalToDeviceUnits(14);
                    foreach (Match match in Regex.Matches(
                                 source.Substring(quote.Span.Start, quote.Span.Length), @"(?m)^ {0,3}>[ \t]?"))
                        Hide(quote.Span.Start + match.Index, match.Length);
                    break;
                case ListItemBlock item when item.Parent is ListBlock { IsOrdered: false }:
                    var marker = Regex.Match(
                        source.Substring(item.Span.Start, item.Span.Length), @"^[ \t]*[-+*][ \t]+");
                    if (marker.Success)
                    {
                        var lineEnd = source.IndexOf('\n', item.Span.Start);
                        Select(item.Span.Start, (lineEnd < 0 ? source.Length : lineEnd) - item.Span.Start);
                        SelectionBullet = true;
                        var depth = 1;
                        for (var parent = item.Parent.Parent; parent is not null; parent = parent.Parent)
                            if (parent is ListItemBlock)
                                depth++;
                        SelectionIndent = LogicalToDeviceUnits(18 * depth);
                        Hide(item.Span.Start, marker.Length);
                    }
                    break;
            }
        }
    }

    private void Code(SourceSpan span) => Format(span, new CharacterFormat
    {
        Mask = 0x24000000, FaceName = "Consolas",
        BackColor = ColorTranslator.ToWin32(Color.FromArgb(239, 242, 246))
    });

    private void Hide(int start, int length)
    {
        if (length > 0)
            Format(new SourceSpan(start, start + length - 1),
                new CharacterFormat { Mask = 0x100, Effects = 0x100 });
    }

    private void Format(SourceSpan span, CharacterFormat format)
    {
        if (span.Start < 0 || span.End >= TextLength || span.End < span.Start)
            throw new InvalidOperationException("A Markdown formatting range is outside the prompt.");
        Select(span.Start, span.Length);
        SetFormat(format);
    }

    private void SetFormat(CharacterFormat format)
    {
        format.Size = (uint)Marshal.SizeOf<CharacterFormat>();
        format.FaceName ??= "";
        if (SendMessage(Handle, 0x0444, 1, ref format) == 0) // EM_SETCHARFORMAT
            throw new InvalidOperationException("Windows could not apply Markdown text formatting.");
    }

    private ITextDocument GetTextDocument()
    {
        if (textDocument is not null)
            return textDocument;
        if (SendMessage(Handle, 0x043C, 0, out nint pointer) == 0)
            throw new InvalidOperationException("Windows rich text editing is unavailable.");
        try
        {
            textDocument = (ITextDocument)Marshal.GetObjectForIUnknown(pointer);
            return textDocument;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (textDocument is not null)
        {
            Marshal.ReleaseComObject(textDocument);
            textDocument = null;
        }
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            renderTimer.Dispose();
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct CharacterFormat
    {
        public uint Size, Mask, Effects;
        public int Height, Offset, TextColor;
        public byte CharSet, PitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string? FaceName;
        public ushort Weight;
        public short Spacing;
        public int BackColor, Locale, Reserved;
        public short Style;
        public ushort Kerning;
        public byte UnderlineType, Animation, RevisionAuthor, ReservedByte;
    }

    [ComImport, Guid("8CC497C0-A1DF-11CE-8098-00AA0047BE5D"),
     InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface ITextDocument
    {
        [return: MarshalAs(UnmanagedType.BStr)] string GetName();
        [return: MarshalAs(UnmanagedType.Interface)] object GetSelection();
        int GetStoryCount();
        [return: MarshalAs(UnmanagedType.Interface)] object GetStoryRanges();
        int GetSaved();
        void SetSaved(int value);
        float GetDefaultTabStop();
        void SetDefaultTabStop(float value);
        void New();
        void Open([In] ref object value, int flags, int codePage);
        void Save([In] ref object value, int flags, int codePage);
        int Freeze();
        int Unfreeze();
        void BeginEditCollection();
        void EndEditCollection();
        int Undo(int count);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, ref CharacterFormat format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, ref Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, out nint pointer);
}
