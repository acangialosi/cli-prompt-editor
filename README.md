# Prompt Editor

A small, native Windows Markdown editor for **Copilot CLI's Ctrl+G**. Room to collect context, rearrange thoughts, and write a prompt without accidentally sending it.

No browser, Electron, account, telemetry, or file-management UI, and no network calls of its own. The app reads the prompt file supplied by Copilot and content you paste. It writes the prompt and any attached image copies only when you explicitly choose **Submit Prompt**. The optional **Refine** button is the only feature that contacts Copilot; see [Refining with Copilot](#refining-with-copilot).

## What it does

- Resizable editing area that opens in **Rich Text**, with a **Markdown** switch for editing the source. Both views edit the same Markdown document.
- Word wrap, plain-text paste, undo/redo, find, zoom, word/character counts, and an approximate token count.
- Paste image files copied from Explorer or screenshots copied by Snipping Tool / Print Screen. Copilot file references appear at the cursor, with a thumbnail strip for reviewing and removing images.
- Centered, Notepad-style Markdown controls for headings, lists, bold, italic, links, quotes, inline code, and fenced code blocks, with spacious icon buttons and tooltips. **Lists** includes bullets, numbered lists, and checklists. Undo, redo, find, wrap, and zoom sit in a compact second row.
- **Enter inserts a newline.** **Submit Prompt (Ctrl+Enter)** returns your edits to Copilot; it does **not** send the prompt to the model.
- **/fleet** and **/refine** buttons prepend the corresponding slash command to your draft. They only edit the text; they do not run the command or send the prompt. Undo restores the previous draft.
- **Refine (Ctrl+Shift+R)** asks Copilot to rewrite your draft more clearly, using the current Copilot conversation as context. The result replaces the prompt as one undoable edit.
- Press **Escape twice within 750 ms**, or close the window, to cancel without changing the original file. A single Escape keeps your context, or closes Find if it is open. Unsaved edits still require discard confirmation.
- Preserves Unicode, BOM, and the original newline convention. Refuses to overwrite a file changed by another process. Failed writes keep your edits on screen.

Rich Text displays headings, bold, italic, lists, quotes, links, and code with native formatting while hiding their Markdown markers. The formatting toolbar edits the Markdown underneath. Enter continues lists and quotes; Enter on an empty item ends them. Markdown mode shows the exact source in a monospace font.

Switching views preserves source text, selection, undo/redo, and attachment references; it never converts or rewrites the document. Unrecognized syntax stays visible and unchanged rather than being discarded. HTML is not executed, remote content is not loaded, and pasted text is not converted from HTML or RTF. Use Markdown mode for advanced syntax or direct editing of formatting markers.

## Pasting images

**Ctrl+V** or **Shift+Insert** pastes copied image files, clipboard PNG images, or clipboard bitmaps. Otherwise, it pastes plain text. **Ctrl+Shift+V** always pastes text only, even when the clipboard also contains an image. Paste applies to the editing area; the find box remains text-only.

Image files supported by this version are **PNG, JPEG, GIF, and BMP**. BMP files and bitmap screenshots are saved as PNG; other supported file formats are preserved. WEBP, HEIC, HEIF, PDFs, and other file types are not supported by this editor's built-in image decoder. Unsupported, corrupt, or unreadable file selections are rejected as a whole without changing the prompt. There is no built-in screen capture tool.

Each image is represented by a quoted absolute Copilot reference, such as `@"C:\Users\you\AppData\Local\PromptEditor\Attachments\<id>.png"`. The thumbnail strip follows these references: undoing paste or deleting a reference removes its preview, and redo restores it. The corner **X** deletes that image's references from the prompt without deleting the source file. Thumbnails have no filename label; hover to see the filename. Previews of this editor's saved attachments are restored when you reopen the prompt.

Pasted images stay in memory until **Submit Prompt**. Only images still referenced in the prompt are written to `%LOCALAPPDATA%\PromptEditor\Attachments`, before the prompt file is updated. Failed saves keep your edits available for retry and attempt to remove newly written image copies. Cancelling writes no attachments. Original image files are never modified.

Accepted image copies **remain on disk after the editor closes**, including when a reference is removed in a later editing session. Copilot may read them only when you eventually send the prompt, and saved prompts may still reference them. There is no automatic expiry: delete copies manually when no longer needed, especially for sensitive screenshots. Copilot may request permission to read the attachment location, and image understanding requires an image-capable model. **Submit Prompt** itself never sends anything to the model.

## Refining with Copilot

**Refine** (in the actions row, or **Ctrl+Shift+R**) sends the current draft to Copilot and replaces it with a clearer, better-organized version that keeps your intent, file paths, `@` references, commands, and code intact. **Ctrl+Z** restores your draft. While Copilot is working, the prompt is read-only; **Esc**, **Ctrl+Shift+R**, or the **Cancel Refine** button stops it and leaves the prompt unchanged.

When the editor is opened from a Copilot CLI session with Ctrl+G, Refine also includes recent messages from that conversation (up to about 16,000 characters, newest first) so it can resolve references like "that error". Copilot CLI doesn't tell its editor which session it belongs to, so Prompt Editor uses `COPILOT_AGENT_SESSION_ID` when it is set, or otherwise finds the session that the `copilot` process which launched it currently has in use. It reads the messages from that session's `events.jsonl` file under `%USERPROFILE%\.copilot\session-state` (or `COPILOT_HOME`). This relies on undocumented Copilot CLI details; if no session can be found, Refine uses only the draft.

How it works and what to expect:

- Refine runs a separate, headless `copilot` process. It **never resumes, writes to, or sends messages in your interactive session**, which can safely keep running.
- The helper has **no tools**: it cannot run commands, edit files, or fetch URLs. Repository and custom instructions are not loaded.
- It requires Copilot CLI on `PATH` and a signed-in account. It uses your default model and counts toward your Copilot usage like any other request. Refine gives up after two minutes.
- Your draft and the conversation context are sent to Copilot. Pasted images are sent only as their `@"..."` file references, not as image content; references dropped by the rewrite are re-added at the end.
- Each refine creates a separate local Copilot session named **Prompt Editor refine** (with cloud sync off), which may appear in your session history.

## Install and connect to Copilot

Requires Windows 10/11 x64. For a local build, install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The locally installed app uses the .NET 8 **Desktop Runtime** (included with the SDK).

From PowerShell in this repository:

```powershell
.\scripts\Install.ps1 -ConfigureCopilot
```

This installs into `%LOCALAPPDATA%\PromptEditor` and sets the user-level `COPILOT_EDITOR` environment variable. Restart an already-running Copilot session (and use a new terminal for future sessions), then press **Ctrl+G** while composing a prompt.

For manual configuration, or a portable build downloaded from the repository's Actions artifacts:

```powershell
$env:COPILOT_EDITOR = 'C:\path with spaces\PromptEditor.exe'
copilot
```

To persist that command for future terminals:

```powershell
[Environment]::SetEnvironmentVariable('COPILOT_EDITOR', 'C:\path with spaces\PromptEditor.exe', 'User')
```

Copilot checks `COPILOT_EDITOR`, `VISUAL`, then `EDITOR`. Using `COPILOT_EDITOR` leaves your other tools' default editor alone. Store the executable path without embedded double quotes, even when it contains spaces; the single quotes above are PowerShell string delimiters, not part of the value. Do not use `start` or a launcher that exits early: Copilot must wait for this editor to close.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Enter / Tab | Insert newline / indentation |
| Ctrl+Enter | Submit Prompt: save, return to Copilot, and close |
| Escape | Close Find, cancel a running refine, or show the double-Escape reminder |
| Escape twice within 750 ms | Cancel (confirm if edited) |
| Ctrl+Shift+R | Refine the prompt with Copilot / cancel a running refine |
| Ctrl+B / Ctrl+I | Toggle bold / italic |
| Ctrl+Alt+H | Toggle level-two heading |
| Ctrl+Shift+L | Toggle bullet list |
| Ctrl+E / Ctrl+Shift+K | Inline code / fenced code block |
| Ctrl+K | Insert link and select destination |
| Ctrl+F / F3 | Find / next match (wraps around) |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+V / Shift+Insert | Paste images or plain text |
| Ctrl+Shift+V | Paste plain text only |
| Ctrl++ / Ctrl+- / Ctrl+0 | Zoom in / out / reset |

Line-formatting controls apply to every selected line. Formatting an empty selection inserts a selected placeholder you can immediately replace.

Word, character, and token counts use the underlying Markdown, including formatting markers and attachment references. The status bar estimates text tokens as character count divided by four, rounded up. This is model-dependent, not an exact tokenizer; it excludes image tokens and conversation/tool overhead.

## Build and test

```powershell
dotnet build PromptEditor.sln -c Release
dotnet test PromptEditor.sln -c Release
```

Create a portable, self-contained executable (no runtime installation required):

```powershell
dotnet publish .\src\PromptEditor\PromptEditor.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\artifacts\PromptEditor
```

GitHub Actions runs the tests and publishes the portable app as a build artifact. Local runtime-dependent builds are smaller; portable builds bundle the Windows Desktop runtime and are larger.

## Editor contract

```text
PromptEditor.exe <existing-prompt-file>
```

One process/window per invocation, blocking until it closes. Exactly one path is required, including paths with spaces or Unicode. Exit code `0` means the window closed normally (accepted or cancelled), `1` means the input file could not be opened, and `2` means invalid arguments. Errors are shown in native dialogs.

Edits and newly pasted images stay in memory until accepted. An unchanged acceptance leaves the prompt file byte-for-byte untouched. Changed text is written via a sibling temporary file and atomic replacement; mixed newline styles are normalized to the first newline style on an edited save. BOM-less input must be valid UTF-8; UTF-8/16/32 BOMs are supported. Closing without accepting does not persist a draft. Accepted image references use separately retained local files as described above.

To remove the Copilot integration:

```powershell
[Environment]::SetEnvironmentVariable('COPILOT_EDITOR', $null, 'User')
Remove-Item Env:\COPILOT_EDITOR -ErrorAction SilentlyContinue
```
