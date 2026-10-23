# Prompt Editor

A small, native Windows Markdown editor for **Copilot CLI's Ctrl+G**. Room to collect context, rearrange thoughts, and write a prompt without accidentally sending it.

No browser, Electron, account, network calls, telemetry, or file-management UI. The app only reads the prompt file supplied by Copilot and writes it back when you explicitly choose **Use prompt**.

## What it does

- Resizable, word-wrapped editing area with plain-text paste, undo/redo, find, zoom, and word/character counts.
- Lightweight Markdown controls: bold, italic, headings, bullets, checklists, quotes, links, inline code, and fenced code blocks.
- **Enter inserts a newline.** **Ctrl+Enter** (or **Use prompt**) returns your edits to Copilot; it does **not** submit the prompt.
- Cancel, Escape, or closing the window leaves the original file untouched. Unsaved edits require discard confirmation.
- Preserves Unicode, BOM, and the original newline convention. Refuses to overwrite a file changed by another process. Failed writes keep your edits on screen.

Markdown remains visible as text: this is a composition pad, not a rendered preview or rich-text document editor. Pasted text is not automatically converted into Markdown.

## Install and connect to Copilot

Requires Windows 10/11 x64. For a local build, install the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The locally installed app uses the .NET 8 **Desktop Runtime** (included with the SDK).

From PowerShell in this repository:

```powershell
.\scripts\Install.ps1 -ConfigureCopilot
```

This installs into `%LOCALAPPDATA%\PromptEditor` and sets the user-level `COPILOT_EDITOR` environment variable. Restart an already-running Copilot session (and use a new terminal for future sessions), then press **Ctrl+G** while composing a prompt.

For manual configuration, or a portable build downloaded from the repository's Actions artifacts:

```powershell
$env:COPILOT_EDITOR = '"C:\path with spaces\PromptEditor.exe"'
copilot
```

To persist that command for future terminals:

```powershell
[Environment]::SetEnvironmentVariable('COPILOT_EDITOR', '"C:\path with spaces\PromptEditor.exe"', 'User')
```

Copilot checks `COPILOT_EDITOR`, `VISUAL`, then `EDITOR`. Using `COPILOT_EDITOR` leaves your other tools' default editor alone. Do not use `start` or a launcher that exits early: Copilot must wait for this editor to close.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| Enter / Tab | Insert newline / indentation |
| Ctrl+Enter | Use prompt and close |
| Escape | Close find, or cancel (confirm if edited) |
| Ctrl+B / Ctrl+I | Toggle bold / italic |
| Ctrl+Alt+H | Toggle level-two heading |
| Ctrl+Shift+L | Toggle bullet list |
| Ctrl+E / Ctrl+Shift+K | Inline code / fenced code block |
| Ctrl+K | Insert link and select destination |
| Ctrl+F / F3 | Find / next match (wraps around) |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl+V / Ctrl+Shift+V | Paste plain text |
| Ctrl++ / Ctrl+- / Ctrl+0 | Zoom in / out / reset |

Line-formatting controls apply to every selected line. Formatting an empty selection inserts a selected placeholder you can immediately replace.

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

Edits stay in memory until accepted. An unchanged acceptance is byte-for-byte untouched. Changed text is written via a sibling temporary file and atomic replacement; mixed newline styles are normalized to the first newline style on an edited save. BOM-less input must be valid UTF-8; UTF-8/16/32 BOMs are supported. Closing without accepting does not persist a draft.

To remove the Copilot integration:

```powershell
[Environment]::SetEnvironmentVariable('COPILOT_EDITOR', $null, 'User')
Remove-Item Env:\COPILOT_EDITOR -ErrorAction SilentlyContinue
```
