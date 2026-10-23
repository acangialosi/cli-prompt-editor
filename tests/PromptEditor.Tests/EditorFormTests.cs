using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
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
            var button = Descendants(form).OfType<Button>().Single(control => control.Text.StartsWith("Use prompt"));
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
    public void UnchangedCancelDoesNotWrite() => OnUiThread(() =>
    {
        WithEditor("original\r\n", (form, _, path) =>
        {
            Descendants(form).OfType<Button>().Single(control => control.Text == "Cancel").PerformClick();
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

    private static bool Command(EditorForm form, Keys keys)
    {
        var method = typeof(EditorForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (bool)method.Invoke(form, [new Message(), keys])!;
    }

    private static void WithEditor(string original, Action<EditorForm, RichTextBox, string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), $"prompt ui \u4e16\u754c {Guid.NewGuid():N}.md");
        File.WriteAllText(path, original);
        try
        {
            using var form = new EditorForm(PromptDocument.Open(path));
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
