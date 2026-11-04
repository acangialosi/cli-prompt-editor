namespace PromptEditor;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length != 1)
        {
            MessageBox.Show(
                "Usage: PromptEditor.exe <prompt-file>\n\n" +
                "Set COPILOT_EDITOR to this app, then press Ctrl+G in Copilot CLI.\n" +
                "The editor waits until you choose Submit Prompt or close the window.",
                "Prompt Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 2;
        }

        try
        {
            var document = PromptDocument.Open(args[0]);
            using var editor = new EditorForm(document);
            Application.Run(editor);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException
                                      or System.Security.SecurityException)
        {
            MessageBox.Show(error.Message, "Cannot open prompt",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
