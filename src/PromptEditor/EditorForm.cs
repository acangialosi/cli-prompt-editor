using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace PromptEditor;

public sealed class EditorForm : Form
{
    private readonly PromptDocument document;
    private readonly MarkdownRichTextBox editor = new();
    private readonly ToolStripStatusLabel counts = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel position = new();
    private readonly ToolStripTextBox search = new() { AutoSize = false, Width = 190 };
    private readonly ToolStrip findBar = new()
    {
        Visible = false, GripStyle = ToolStripGripStyle.Hidden,
        Dock = DockStyle.None, Anchor = AnchorStyles.None
    };
    private readonly System.Windows.Forms.Timer countTimer = new() { Interval = 180 };
    private readonly System.Windows.Forms.Timer escapeTimer = new() { Interval = 750 };
    private readonly ToolStripDropDownButton headings = new("Text") { AccessibleName = "Heading" };
    private readonly ToolStripStatusLabel modeStatus = new("Rich Text  |  Local unless refined");
    private readonly Label notice = new();
    private readonly ToolTip tooltips = new();
    private readonly List<Image> toolbarImages = [];
    private readonly ImageAttachments attachments;
    private readonly Func<IDataObject?> readClipboard;
    private readonly Func<string, CancellationToken, Task<string>> refine;
    private readonly Action<string> showError;
    private readonly ToolStripButton refineButton = new("Refine") { ToolTipText = RefineTooltip };
    private readonly List<ToolStripItem> lockedWhileRefining = [];
    private CancellationTokenSource? refinement;
    private bool closed;
    private readonly FlowLayoutPanel attachmentStrip = new()
    {
        Dock = DockStyle.Fill, Height = 108, AutoScroll = true,
        WrapContents = false, Visible = false, Margin = new Padding(0, 6, 0, 0)
    };
    private IReadOnlyList<ImageAttachment> visibleAttachments = [];
    private bool accepted;
    private bool dirty;
    private long? escapePressedAt;
    private string? noticeBeforeEscape;
    private const string EscapeNotice = "Press Esc again to cancel.";
    private const string RefineTooltip =
        "Rewrite this prompt with Copilot (Ctrl+Shift+R). Sends the prompt and recent conversation " +
        "from this Copilot session to Copilot; nothing is submitted. Ctrl+Z restores your draft.";

    public EditorForm(PromptDocument document) : this(document, new ImageAttachments(), Clipboard.GetDataObject)
    {
    }

    internal EditorForm(PromptDocument document, ImageAttachments attachments, Func<IDataObject?> readClipboard,
        Func<string, CancellationToken, Task<string>>? refine = null, Action<string>? showError = null)
    {
        this.document = document;
        this.attachments = attachments;
        this.readClipboard = readClipboard;
        this.refine = refine ?? PromptRefiner.RefineAsync;
        this.showError = showError ?? (message => MessageBox.Show(this, message + "\n\nThe prompt was not changed.",
            "Cannot refine prompt", MessageBoxButtons.OK, MessageBoxIcon.Error));
        SuspendLayout();
        Text = "Prompt Editor";
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(1080, 780);
        MinimumSize = new Size(700, 440);
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(247, 248, 250);
        KeyPreview = true;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18, 12, 18, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, Anchor = AnchorStyles.None,
            ImageScalingSize = new Size(20, 20), ShowItemToolTips = true,
            Padding = new Padding(4, 4, 4, 4), BackColor = BackColor,
            AccessibleName = "Markdown formatting", Margin = Padding.Empty
        };
        headings.DisplayStyle = ToolStripItemDisplayStyle.Text;
        headings.AutoSize = false;
        headings.Size = new Size(68, 36);
        headings.Margin = new Padding(2);
        headings.ToolTipText = "Heading style (Ctrl+Alt+H for heading 2)";
        AddMenuItem(headings, "Normal text", "Remove the heading from selected lines", () => Heading(0));
        for (var level = 1; level <= 6; level++)
        {
            var headingLevel = level;
            AddMenuItem(headings, $"Heading {level}", $"Toggle level-{level} Markdown heading",
                () => Heading(headingLevel));
        }
        toolbar.Items.Add(headings);
        var lists = new ToolStripDropDownButton("Lists", CreateIcon("Bullets"))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image, AccessibleName = "Lists",
            ToolTipText = "List style (Ctrl+Shift+L for bullets)",
            AutoSize = false, Size = new Size(64, 36), Margin = new Padding(2)
        };
        AddMenuItem(lists, "Bullets", "Bulleted list (Ctrl+Shift+L)", () => List(false));
        AddMenuItem(lists, "Numbers", "Numbered list", () => List(true));
        AddMenuItem(lists, "Tasks", "Toggle checklist", () => Prefix("- [ ] "));
        toolbar.Items.Add(lists);
        AddIconButton(toolbar, "Bold", "Bold (Ctrl+B)", () => Wrap("**"));
        AddIconButton(toolbar, "Italic", "Italic (Ctrl+I)", () => Wrap("*"));
        AddIconButton(toolbar, "Link", "Insert link (Ctrl+K)",
            () => Apply(Markdown.Link(editor.Text, editor.SelectionStart, editor.SelectionLength)));
        AddIconButton(toolbar, "Quote", "Toggle block quote", () => Prefix("> "));
        AddIconButton(toolbar, "Code", "Inline code (Ctrl+E)", () => Wrap("`"));
        AddIconButton(toolbar, "Code block", "Fenced code block (Ctrl+Shift+K)",
            () => Apply(Markdown.CodeBlock(editor.Text, editor.SelectionStart, editor.SelectionLength)));
        foreach (var command in new[] { "/fleet", "/refine" })
        {
            var button = new ToolStripButton(command)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text, AccessibleName = command,
                ToolTipText = $"Prepend {command} to the prompt without running or sending it.",
                AutoSize = false, Size = new Size(64, 36), Margin = new Padding(2)
            };
            button.Click += (_, _) => PrependCommand(command);
            toolbar.Items.Add(button);
        }
        var actions = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.None, Anchor = AnchorStyles.None,
            ImageScalingSize = new Size(20, 20), ShowItemToolTips = true,
            Padding = new Padding(4, 0, 4, 4), BackColor = BackColor,
            AccessibleName = "Editor actions", Margin = Padding.Empty
        };
        lockedWhileRefining.AddRange(toolbar.Items.Cast<ToolStripItem>());
        lockedWhileRefining.Add(AddIconButton(actions, "Undo", "Undo (Ctrl+Z)", editor.Undo));
        lockedWhileRefining.Add(AddIconButton(actions, "Redo", "Redo (Ctrl+Y)", editor.Redo));
        AddIconButton(actions, "Find", "Find (Ctrl+F)", ShowFind);
        var wrap = new ToolStripButton("Wrap")
        {
            CheckOnClick = true, Checked = true, ToolTipText = "Toggle word wrap"
        };
        wrap.CheckedChanged += (_, _) => editor.WordWrap = wrap.Checked;
        actions.Items.Add(wrap);
        AddButton(actions, "A-", "Smaller text (Ctrl+-)", () => Zoom(-0.1f));
        AddButton(actions, "A+", "Larger text (Ctrl++)", () => Zoom(0.1f));
        var richText = new ToolStripButton("Rich Text")
        {
            Checked = true, ToolTipText = "Edit formatted text; the underlying document stays Markdown."
        };
        var markdown = new ToolStripButton("Markdown")
        {
            ToolTipText = "Edit the Markdown source, including formatting markers."
        };
        void SetMode(bool rich)
        {
            ResetEscape();
            editor.RichTextMode = rich;
            richText.Checked = rich;
            markdown.Checked = !rich;
            modeStatus.Text = rich ? "Rich Text  |  Local unless refined" : "Markdown  |  Local unless refined";
            editor.Focus();
        }
        richText.Click += (_, _) => SetMode(true);
        markdown.Click += (_, _) => SetMode(false);
        actions.Items.Add(richText);
        actions.Items.Add(markdown);
        actions.Items.Add(new ToolStripSeparator());
        refineButton.AccessibleName = "Refine with Copilot";
        refineButton.Click += (_, _) => Refine();
        actions.Items.Add(refineButton);
        foreach (var button in actions.Items.OfType<ToolStripButton>())
        {
            button.Margin = new Padding(2, 0, 2, 0);
            if (button.DisplayStyle == ToolStripItemDisplayStyle.Image)
                button.Size = new Size(32, 28);
        }
        toolbar.MouseDown += (_, _) => ResetEscape();
        actions.MouseDown += (_, _) => ResetEscape();
        header.Controls.Add(toolbar, 0, 0);
        header.Controls.Add(actions, 0, 1);
        layout.Controls.Add(header, 0, 0);

        findBar.ShowItemToolTips = true;
        findBar.Items.Add(new ToolStripLabel("Find") { ToolTipText = "Find text in the prompt" });
        search.ToolTipText = "Search text (Enter or F3 finds the next match)";
        findBar.Items.Add(search);
        AddButton(findBar, "Next", "Next match (F3)", FindNext);
        AddButton(findBar, "Close", "Close find (Esc)", HideFind);
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { HideFind(); e.SuppressKeyPress = true; }
        };
        layout.Controls.Add(findBar, 0, 1);

        editor.Dock = DockStyle.Fill;
        editor.BorderStyle = BorderStyle.FixedSingle;
        editor.BackColor = Color.White;
        editor.ForeColor = Color.FromArgb(28, 33, 42);
        editor.Font = new Font("Consolas", 12);
        editor.AcceptsTab = true;
        editor.DetectUrls = false;
        editor.EnableAutoDragDrop = false;
        editor.HideSelection = false;
        editor.WordWrap = true;
        editor.ScrollBars = RichTextBoxScrollBars.Both;
        editor.Text = document.Text;
        editor.ClearUndo();
        editor.TextChanged += (_, _) =>
        {
            ResetEscape();
            dirty = editor.Text != document.Text;
            Text = dirty ? "Prompt Editor *" : "Prompt Editor";
            UpdateAttachments();
            countTimer.Stop();
            countTimer.Start();
        };
        editor.SelectionChanged += (_, _) => UpdatePosition();
        editor.MouseDown += (_, _) => ResetEscape();
        tooltips.SetToolTip(editor,
            "Edit Rich Text or Markdown. Ctrl+V pastes text or images; Ctrl+Shift+V pastes text only. Ctrl+Enter returns the prompt.");
        layout.Controls.Add(editor, 0, 2);
        layout.Controls.Add(attachmentStrip, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        notice.Text = "Returns edits to Copilot; does not send.";
        notice.AutoSize = true;
        notice.Anchor = AnchorStyles.Left;
        notice.ForeColor = Color.FromArgb(85, 91, 103);
        tooltips.SetToolTip(notice, "Returning the prompt does not submit it to Copilot.");
        footer.Controls.Add(notice, 0, 0);
        var use = new Button
        {
            Text = "Submit Prompt (Ctrl+Enter)", TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill, Margin = new Padding(4, 10, 0, 8),
            BackColor = Color.FromArgb(30, 91, 190), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
        };
        use.Click += (_, _) => AcceptPrompt();
        tooltips.SetToolTip(use, "Save edits and return to Copilot without submitting (Ctrl+Enter).");
        footer.Controls.Add(use, 1, 0);
        layout.Controls.Add(footer, 0, 4);

        var status = new StatusStrip
        {
            Dock = DockStyle.Fill, SizingGrip = false, BackColor = BackColor, ShowItemToolTips = true
        };
        counts.ToolTipText =
            "Approximate Markdown text tokens: one token per four characters, rounded up. " +
            "Model-dependent; excludes image tokens and conversation/tool overhead.";
        position.ToolTipText = "Current cursor line and column";
        status.Items.Add(counts);
        status.Items.Add(position);
        modeStatus.ToolTipText =
            "Stored as Markdown in both views. No telemetry; nothing leaves this computer unless you click Refine.";
        status.Items.Add(modeStatus);
        layout.Controls.Add(status, 0, 5);
        var previewErrors = attachments.Restore(document.Text);
        if (previewErrors.Count > 0)
        {
            notice.Text = "Some previews unavailable; references kept.";
            tooltips.SetToolTip(notice, string.Join(Environment.NewLine, previewErrors));
        }
        countTimer.Tick += (_, _) => { countTimer.Stop(); UpdateCounts(); };
        escapeTimer.Tick += (_, _) => ResetEscape();
        Deactivate += (_, _) => ResetEscape();
        UpdateCounts();
        UpdatePosition();
        Shown += (_, _) =>
        {
            editor.RefreshPresentation();
            editor.Focus();
            editor.Select(editor.TextLength, 0);
        };
        FormClosing += ConfirmDiscard;
        FormClosed += (_, _) => StopRefining();
        ResumeLayout(true);
        foreach (var strip in new[] { toolbar, actions })
        {
            strip.ImageScalingSize = LogicalToDeviceUnits(new Size(20, 20));
            foreach (ToolStripItem item in strip.Items)
            {
                if (!item.AutoSize)
                    item.Size = LogicalToDeviceUnits(item.Size);
                item.Margin = new Padding(LogicalToDeviceUnits(item.Margin.Left),
                    LogicalToDeviceUnits(item.Margin.Top), LogicalToDeviceUnits(item.Margin.Right),
                    LogicalToDeviceUnits(item.Margin.Bottom));
            }
        }
        UpdateAttachments();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape)
            ResetEscape();
        if (keyData == (Keys.Control | Keys.Enter)) { AcceptPrompt(); return true; }
        if (keyData == (Keys.Control | Keys.Shift | Keys.R)) { Refine(); return true; }
        if (keyData == Keys.Escape)
        {
            if ((msg.LParam.ToInt64() & (1L << 30)) != 0)
                return true;
            if (findBar.Visible)
            {
                HideFind();
                return true;
            }
            if (refinement != null)
            {
                refinement.Cancel();
                return true;
            }
            var now = Environment.TickCount64;
            if (escapePressedAt is long previous && now - previous <= escapeTimer.Interval)
            {
                ResetEscape();
                Close();
                return true;
            }
            ResetEscape();
            escapePressedAt = now;
            noticeBeforeEscape = notice.Text;
            notice.Text = EscapeNotice;
            escapeTimer.Start();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F)) { ShowFind(); return true; }
        if (keyData == Keys.F3) { if (!findBar.Visible) ShowFind(); else FindNext(); return true; }
        if (!editor.Focused)
            return base.ProcessCmdKey(ref msg, keyData);
        // A read-only RichTextBox still honors undo/redo, which would bypass the refine lock.
        if (refinement != null && keyData is (Keys.Control | Keys.Z) or (Keys.Control | Keys.Y)
                or (Keys.Control | Keys.Shift | Keys.Z) or (Keys.Alt | Keys.Back))
            return true;

        switch (keyData)
        {
            case Keys.Control | Keys.B: Wrap("**"); return true;
            case Keys.Control | Keys.I: Wrap("*"); return true;
            case Keys.Control | Keys.E: Wrap("`"); return true;
            case Keys.Control | Keys.Alt | Keys.H: Heading(2); return true;
            case Keys.Control | Keys.Shift | Keys.L: List(false); return true;
            case Keys.Control | Keys.Shift | Keys.K:
                Apply(Markdown.CodeBlock(editor.Text, editor.SelectionStart, editor.SelectionLength)); return true;
            case Keys.Control | Keys.K:
                Apply(Markdown.Link(editor.Text, editor.SelectionStart, editor.SelectionLength)); return true;
            case Keys.Control | Keys.V:
            case Keys.Shift | Keys.Insert:
                Paste(false); return true;
            case Keys.Control | Keys.Shift | Keys.V:
                Paste(true); return true;
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Shift | Keys.Oemplus:
            case Keys.Control | Keys.Add: Zoom(0.1f); return true;
            case Keys.Control | Keys.OemMinus:
            case Keys.Control | Keys.Subtract: Zoom(-0.1f); return true;
            case Keys.Control | Keys.D0: editor.ZoomFactor = 1; return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static void AddButton(ToolStrip strip, string text, string tooltip, Action action)
    {
        var button = new ToolStripButton(text) { ToolTipText = tooltip };
        button.Click += (_, _) => action();
        strip.Items.Add(button);
    }

    private void Paste(bool textOnly)
    {
        if (refinement != null)
            return;
        try
        {
            var data = readClipboard();
            if (data == null)
                return;
            var pasted = textOnly ? null : attachments.ReadClipboard(data);
            if (pasted == null)
            {
                if (data.GetData(DataFormats.UnicodeText) is string text)
                    editor.SelectedText = PromptDocument.Normalize(text);
                return;
            }

            var start = editor.SelectionStart;
            var end = start + editor.SelectionLength;
            var prefix = start > 0 && !char.IsWhiteSpace(editor.Text[start - 1]) ? " " : "";
            var suffix = end < editor.TextLength && !char.IsWhiteSpace(editor.Text[end]) ? " " : "";
            var references = prefix + string.Join("\n", pasted.Select(image => image.Reference)) + suffix;
            Apply(new TextEdit(start, editor.SelectionLength, references, start + references.Length, 0));
            notice.Text = pasted.Count == 1 ? "Image added. Saved on Submit Prompt."
                : $"{pasted.Count} images added. Saved on Submit Prompt.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                      or ExternalException or ArgumentException or NotSupportedException
                                      or System.Security.SecurityException)
        {
            MessageBox.Show(this, error.Message + "\n\nThe prompt was not changed.",
                "Cannot paste", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateAttachments()
    {
        var referenced = attachments.ReferencedIn(editor.Text);
        if (visibleAttachments.SequenceEqual(referenced))
            return;
        visibleAttachments = referenced;
        attachmentStrip.SuspendLayout();
        while (attachmentStrip.Controls.Count > 0)
        {
            var control = attachmentStrip.Controls[0];
            attachmentStrip.Controls.Remove(control);
            control.Dispose();
        }
        foreach (var image in referenced)
        {
            var card = new Panel
            {
                Size = LogicalToDeviceUnits(new Size(114, 88)),
                Margin = new Padding(0, 0, LogicalToDeviceUnits(10), 0),
                BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White
            };
            var preview = new PictureBox
            {
                Image = image.Thumbnail, SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(LogicalToDeviceUnits(6), LogicalToDeviceUnits(16)),
                Size = LogicalToDeviceUnits(new Size(96, 64)),
                AccessibleName = $"Preview of {image.Name}"
            };
            tooltips.SetToolTip(preview, image.Name);
            var remove = new ImageRemoveButton
            {
                Location = new Point(LogicalToDeviceUnits(86), 0),
                Size = LogicalToDeviceUnits(new Size(26, 26)),
                AccessibleName = $"Remove {image.Name}"
            };
            tooltips.SetToolTip(remove, $"Remove {image.Name}");
            remove.Click += (_, _) =>
            {
                var text = editor.Text.Replace(image.Reference, "", StringComparison.Ordinal);
                Apply(new TextEdit(0, editor.TextLength, text, Math.Min(editor.SelectionStart, text.Length), 0));
            };
            card.Controls.Add(preview);
            card.Controls.Add(remove);
            remove.BringToFront();
            attachmentStrip.Controls.Add(card);
        }
        attachmentStrip.Visible = referenced.Count > 0;
        attachmentStrip.ResumeLayout(true);
    }

    private Image CreateIcon(string name)
    {
        var image = ToolbarIcons.Create(name);
        toolbarImages.Add(image);
        return image;
    }

    private ToolStripButton AddIconButton(ToolStrip strip, string text, string tooltip, Action action)
    {
        var button = new ToolStripButton(text, CreateIcon(text))
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image, AccessibleName = text,
            ToolTipText = tooltip, AutoSize = false, Size = new Size(44, 36), Margin = new Padding(2)
        };
        button.Click += (_, _) => action();
        strip.Items.Add(button);
        return button;
    }

    private static void AddMenuItem(ToolStripDropDownButton menu, string text, string tooltip, Action action)
    {
        var item = new ToolStripMenuItem(text) { ToolTipText = tooltip };
        item.Click += (_, _) => action();
        menu.DropDown.ShowItemToolTips = true;
        menu.DropDownItems.Add(item);
    }

    private void Heading(int level) =>
        Apply(Markdown.Heading(editor.Text, editor.SelectionStart, editor.SelectionLength, level));

    private void List(bool numbered) =>
        Apply(Markdown.List(editor.Text, editor.SelectionStart, editor.SelectionLength, numbered));

    private void Wrap(string marker) =>
        Apply(Markdown.Wrap(editor.Text, editor.SelectionStart, editor.SelectionLength, marker));

    private void Prefix(string prefix) =>
        Apply(Markdown.PrefixLines(editor.Text, editor.SelectionStart, editor.SelectionLength, prefix));

    private void PrependCommand(string command)
    {
        var prefix = command + " ";
        Apply(new TextEdit(0, 0, prefix, editor.SelectionStart + prefix.Length, editor.SelectionLength));
    }

    private void Apply(TextEdit edit)
    {
        if (refinement != null)
            return;
        editor.Select(edit.Start, edit.Length);
        editor.SelectedText = edit.Replacement;
        editor.Select(edit.SelectionStart, edit.SelectionLength);
        editor.RefreshPresentation();
        editor.Focus();
    }

    private async void Refine()
    {
        if (refinement != null)
        {
            refinement.Cancel();
            return;
        }
        ResetEscape();
        var original = editor.Text;
        if (string.IsNullOrWhiteSpace(original))
        {
            notice.Text = "Nothing to refine yet.";
            return;
        }

        var cancellation = new CancellationTokenSource();
        SetRefining(cancellation);
        string? refined = null;
        string? failure = null;
        try
        {
            refined = await refine(original, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        // This async void handler is the last place an unexpected failure can be reported without crashing.
        catch (Exception error)
        {
            failure = error.Message;
        }
        finally
        {
            // The source has no timer or wait handle to release, and this can run inline from its own Cancel().
            refinement = null;
        }
        if (closed || IsDisposed)
            return;

        SetRefining(null);
        if (failure != null)
        {
            notice.Text = "Refine failed. Prompt unchanged.";
            showError(failure);
        }
        else if (refined == null)
            notice.Text = "Refine cancelled. Prompt unchanged.";
        else
            notice.Text = ApplyRefinement(original, refined);
    }

    // Cancellation can complete the refine inline, so mark the form closed first to skip touching its controls.
    private void StopRefining()
    {
        closed = true;
        refinement?.Cancel();
    }

    private void SetRefining(CancellationTokenSource? cancellation)
    {
        refinement = cancellation;
        var running = cancellation != null;
        editor.ReadOnly = running;
        foreach (var item in lockedWhileRefining)
            item.Enabled = !running;
        refineButton.Text = running ? "Cancel Refine" : "Refine";
        refineButton.ToolTipText = running ? "Stop refining and keep your prompt unchanged (Esc)." : RefineTooltip;
        if (running)
            notice.Text = "Refining with Copilot\u2026 Esc cancels.";
    }

    private string ApplyRefinement(string original, string refined)
    {
        if (editor.Text != original)
            return "Prompt changed while refining; result discarded.";
        refined = PromptDocument.Normalize(refined);
        var missing = attachments.ReferencedIn(original).Except(attachments.ReferencedIn(refined))
            .Select(image => image.Reference).ToList();
        if (missing.Count > 0)
            refined = refined.TrimEnd() + "\n\n" + string.Join("\n", missing);
        if (refined.Trim() == original.Trim())
            return "No changes suggested.";
        Apply(new TextEdit(0, editor.TextLength, refined, refined.Length, 0));
        return "Refined with Copilot. Ctrl+Z restores your draft.";
    }

    private void Zoom(float delta) => editor.ZoomFactor = Math.Clamp(editor.ZoomFactor + delta, 0.6f, 3f);

    private void ShowFind()
    {
        findBar.Visible = true;
        search.Focus();
        search.SelectAll();
    }

    private void HideFind() { ResetEscape(); findBar.Visible = false; editor.Focus(); }

    private void ResetEscape()
    {
        escapeTimer.Stop();
        escapePressedAt = null;
        if (notice.Text == EscapeNotice && noticeBeforeEscape != null)
            notice.Text = noticeBeforeEscape;
        noticeBeforeEscape = null;
    }

    private void FindNext()
    {
        if (search.Text.Length == 0)
            return;
        var start = editor.SelectionStart + editor.SelectionLength;
        var found = editor.Text.IndexOf(search.Text, start, StringComparison.OrdinalIgnoreCase);
        if (found < 0)
            found = editor.Text.IndexOf(search.Text, StringComparison.OrdinalIgnoreCase);
        if (found < 0)
        {
            notice.Text = "No matches.";
            return;
        }
        editor.Select(found, search.Text.Length);
        editor.ScrollToCaret();
        notice.Text = "Match selected. F3 finds the next match.";
    }

    private void UpdateCounts()
    {
        var text = editor.Text;
        var words = Regex.Count(text, @"\S+");
        var tokens = ((long)text.Length + 3) / 4;
        counts.Text = $"{words:N0} words  |  {text.Length:N0} characters  |  ~{tokens:N0} tokens";
    }

    private void UpdatePosition()
    {
        var offset = editor.SelectionStart;
        var text = editor.Text;
        var line = text.AsSpan(0, offset).Count('\n');
        var lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        position.Text = $"Ln {line + 1}, Col {offset - lineStart + 1}  |  ";
        var lineEnd = text.IndexOf('\n', lineStart);
        var heading = Regex.Match(text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)], @"^ {0,3}(#{1,6})(?:\s|$)");
        headings.Text = heading.Success ? $"H{heading.Groups[1].Length}" : "Text";
    }

    private void AcceptPrompt()
    {
        try
        {
            attachments.Accept(editor.Text, document.Accept);
            accepted = true;
            Close();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                      or System.Security.SecurityException or ArgumentException)
        {
            MessageBox.Show(this, error.Message + "\n\nYour text is still in the editor.",
                "Cannot return prompt", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ConfirmDiscard(object? sender, FormClosingEventArgs e)
    {
        if (accepted || !dirty)
            return;
        e.Cancel = MessageBox.Show(this, "Discard your edits and keep the original Copilot prompt?",
            "Discard edits?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopRefining();
            countTimer.Dispose();
            escapeTimer.Dispose();
            tooltips.Dispose();
        }
        base.Dispose(disposing);
        if (disposing)
        {
            attachments.Dispose();
            foreach (var image in toolbarImages)
                image.Dispose();
        }
    }

    private sealed class ImageRemoveButton : Button
    {
        public ImageRemoveButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            var hovered = ClientRectangle.Contains(PointToClient(MousePosition));
            using var background = new SolidBrush(hovered ? Color.FromArgb(226, 230, 236) : Color.FromArgb(245, 247, 250));
            e.Graphics.FillEllipse(background, 1, 1, Width - 2, Height - 2);
            using var pen = new Pen(Color.FromArgb(40, 45, 52), Math.Max(1.5f, Width / 16f));
            var inset = Width * 0.35f;
            e.Graphics.DrawLine(pen, inset, inset, Width - inset, Height - inset);
            e.Graphics.DrawLine(pen, Width - inset, inset, inset, Height - inset);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3));
        }
    }
}
