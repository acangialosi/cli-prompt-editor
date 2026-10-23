using System.Text.RegularExpressions;

namespace PromptEditor;

public sealed class EditorForm : Form
{
    private readonly PromptDocument document;
    private readonly RichTextBox editor = new();
    private readonly ToolStripStatusLabel counts = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel position = new();
    private readonly ToolStripTextBox search = new() { AutoSize = false, Width = 190 };
    private readonly ToolStrip findBar = new() { Visible = false, GripStyle = ToolStripGripStyle.Hidden };
    private readonly System.Windows.Forms.Timer countTimer = new() { Interval = 180 };
    private readonly Label notice = new();
    private bool accepted;
    private bool dirty;

    public EditorForm(PromptDocument document)
    {
        this.document = document;
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label
        {
            Text = "A little room to think.", Font = new Font("Segoe UI Semibold", 17),
            AutoSize = true, Location = new Point(0, 0)
        });
        header.Controls.Add(new Label
        {
            Text = "Compose in Markdown. Enter makes a new line, never submits.",
            AutoSize = true, Location = new Point(1, 35), ForeColor = Color.FromArgb(85, 91, 103)
        });
        layout.Controls.Add(header, 0, 0);

        var toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Fill,
            Padding = new Padding(0, 4, 0, 6), BackColor = BackColor
        };
        AddButton(toolbar, "Bold", "Bold (Ctrl+B)", () => Wrap("**"));
        AddButton(toolbar, "Italic", "Italic (Ctrl+I)", () => Wrap("*"));
        AddButton(toolbar, "Heading", "Heading (Ctrl+Alt+H)", () => Prefix("## "));
        toolbar.Items.Add(new ToolStripSeparator());
        AddButton(toolbar, "Bullets", "Bullet list (Ctrl+Shift+L)", () => Prefix("- "));
        AddButton(toolbar, "Tasks", "Checklist", () => Prefix("- [ ] "));
        AddButton(toolbar, "Quote", "Block quote", () => Prefix("> "));
        AddButton(toolbar, "Code", "Inline code (Ctrl+E)", () => Wrap("`"));
        AddButton(toolbar, "Code block", "Fenced code block (Ctrl+Shift+K)",
            () => Apply(Markdown.CodeBlock(editor.Text, editor.SelectionStart, editor.SelectionLength)));
        AddButton(toolbar, "Link", "Link (Ctrl+K)",
            () => Apply(Markdown.Link(editor.Text, editor.SelectionStart, editor.SelectionLength)));
        toolbar.Items.Add(new ToolStripSeparator());
        AddButton(toolbar, "Undo", "Undo (Ctrl+Z)", editor.Undo);
        AddButton(toolbar, "Redo", "Redo (Ctrl+Y)", editor.Redo);
        AddButton(toolbar, "Find", "Find (Ctrl+F)", ShowFind);
        var wrap = new ToolStripButton("Wrap")
        {
            CheckOnClick = true, Checked = true, ToolTipText = "Toggle word wrap"
        };
        wrap.CheckedChanged += (_, _) => editor.WordWrap = wrap.Checked;
        toolbar.Items.Add(wrap);
        AddButton(toolbar, "A-", "Smaller text (Ctrl+-)", () => Zoom(-0.1f));
        AddButton(toolbar, "A+", "Larger text (Ctrl++)", () => Zoom(0.1f));
        layout.Controls.Add(toolbar, 0, 1);

        findBar.Items.Add(new ToolStripLabel("Find"));
        findBar.Items.Add(search);
        AddButton(findBar, "Next", "Next match (F3)", FindNext);
        AddButton(findBar, "Close", "Close find (Esc)", HideFind);
        search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { FindNext(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { HideFind(); e.SuppressKeyPress = true; }
        };
        layout.Controls.Add(findBar, 0, 2);

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
            dirty = editor.Text != document.Text;
            Text = dirty ? "Prompt Editor *" : "Prompt Editor";
            countTimer.Stop();
            countTimer.Start();
        };
        editor.SelectionChanged += (_, _) => UpdatePosition();
        layout.Controls.Add(editor, 0, 3);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        notice.Text = "Use prompt returns edits; it does not submit.";
        notice.AutoSize = true;
        notice.Anchor = AnchorStyles.Left;
        notice.ForeColor = Color.FromArgb(85, 91, 103);
        footer.Controls.Add(notice, 0, 0);
        var cancel = new Button { Text = "Cancel", Dock = DockStyle.Fill, Margin = new Padding(4, 10, 4, 8) };
        cancel.Click += (_, _) => Close();
        footer.Controls.Add(cancel, 1, 0);
        var use = new Button
        {
            Text = "Use prompt  Ctrl+Enter", Dock = DockStyle.Fill, Margin = new Padding(4, 10, 0, 8),
            BackColor = Color.FromArgb(30, 91, 190), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
        };
        use.Click += (_, _) => AcceptPrompt();
        footer.Controls.Add(use, 2, 0);
        layout.Controls.Add(footer, 0, 4);

        var status = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false, BackColor = BackColor };
        status.Items.Add(counts);
        status.Items.Add(position);
        status.Items.Add(new ToolStripStatusLabel("Markdown  |  Local only"));
        layout.Controls.Add(status, 0, 5);
        countTimer.Tick += (_, _) => { countTimer.Stop(); UpdateCounts(); };
        UpdateCounts();
        UpdatePosition();
        Shown += (_, _) => { editor.Focus(); editor.Select(editor.TextLength, 0); };
        FormClosing += ConfirmDiscard;
        ResumeLayout(true);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Enter)) { AcceptPrompt(); return true; }
        if (keyData == Keys.Escape)
        {
            if (findBar.Visible) HideFind(); else Close();
            return true;
        }
        if (keyData == (Keys.Control | Keys.F)) { ShowFind(); return true; }
        if (keyData == Keys.F3) { if (!findBar.Visible) ShowFind(); else FindNext(); return true; }
        if (!editor.Focused)
            return base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.Control | Keys.B: Wrap("**"); return true;
            case Keys.Control | Keys.I: Wrap("*"); return true;
            case Keys.Control | Keys.E: Wrap("`"); return true;
            case Keys.Control | Keys.Alt | Keys.H: Prefix("## "); return true;
            case Keys.Control | Keys.Shift | Keys.L: Prefix("- "); return true;
            case Keys.Control | Keys.Shift | Keys.K:
                Apply(Markdown.CodeBlock(editor.Text, editor.SelectionStart, editor.SelectionLength)); return true;
            case Keys.Control | Keys.K:
                Apply(Markdown.Link(editor.Text, editor.SelectionStart, editor.SelectionLength)); return true;
            case Keys.Control | Keys.V:
            case Keys.Control | Keys.Shift | Keys.V:
            case Keys.Shift | Keys.Insert:
                editor.Paste(DataFormats.GetFormat(DataFormats.UnicodeText)); return true;
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

    private void Wrap(string marker) =>
        Apply(Markdown.Wrap(editor.Text, editor.SelectionStart, editor.SelectionLength, marker));

    private void Prefix(string prefix) =>
        Apply(Markdown.PrefixLines(editor.Text, editor.SelectionStart, editor.SelectionLength, prefix));

    private void Apply(TextEdit edit)
    {
        editor.Select(edit.Start, edit.Length);
        editor.SelectedText = edit.Replacement;
        editor.Select(edit.SelectionStart, edit.SelectionLength);
        editor.Focus();
    }

    private void Zoom(float delta) => editor.ZoomFactor = Math.Clamp(editor.ZoomFactor + delta, 0.6f, 3f);

    private void ShowFind()
    {
        findBar.Visible = true;
        search.Focus();
        search.SelectAll();
    }

    private void HideFind() { findBar.Visible = false; editor.Focus(); }

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
        counts.Text = $"{words:N0} words  |  {text.Length:N0} characters";
    }

    private void UpdatePosition()
    {
        var offset = editor.SelectionStart;
        var text = editor.Text;
        var line = text.AsSpan(0, offset).Count('\n');
        var lineStart = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        position.Text = $"Ln {line + 1}, Col {offset - lineStart + 1}  |  ";
    }

    private void AcceptPrompt()
    {
        try
        {
            document.Accept(editor.Text);
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
            countTimer.Dispose();
        base.Dispose(disposing);
    }
}
