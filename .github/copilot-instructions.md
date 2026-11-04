# Copilot instructions for cli-prompt-editor

These are the authoritative instructions for coding agents in this repository.
[`AGENTS.md`](../AGENTS.md) only summarizes them.

## Project overview

A small native Windows Markdown prompt editor (.NET 8 WinForms, C#) used as
Copilot CLI's Ctrl+G editor through `COPILOT_EDITOR`. Key code in
`src/PromptEditor/`:

- `Program.cs` – entry point; opens the prompt file passed by Copilot CLI.
- `EditorForm.cs` – editor window, toolbar, Submit Prompt / Cancel / Refine.
- `MarkdownRichTextBox.cs`, `Markdown.cs`, `ToolbarIcons.cs` – Markdown editing,
  formatting, and toolbar rendering.
- `PromptDocument.cs` – atomic save of the prompt file.
- `ImageAttachments.cs` – pasted images, saved to
  `%LOCALAPPDATA%\PromptEditor\Attachments` only on Submit Prompt.
- `PromptRefiner.cs` – optional Refine: runs a headless `copilot` process with
  no tools, no custom instructions, and `--sync-session=off`, and reads recent
  conversation from `%USERPROFILE%\.copilot\session-state\<id>\events.jsonl`.
- `ProcessTree.cs` – walks parent processes (used to find the launching
  Copilot CLI session for Refine).

Tests are in `tests/PromptEditor.Tests` (xUnit; WinForms UI tests run on an STA
thread). `scripts/Install.ps1` publishes to `%LOCALAPPDATA%\PromptEditor` and,
with `-ConfigureCopilot`, sets the user `COPILOT_EDITOR` variable.

## Build and test

Windows only; requires the .NET 8 SDK. `Directory.Build.props` enables
`TreatWarningsAsErrors`, so warnings fail the build.

```powershell
dotnet build PromptEditor.sln -c Release
dotnet test PromptEditor.sln -c Release
dotnet test PromptEditor.sln -c Release --filter "FullyQualifiedName~PromptDocumentTests"
```

Pull request checks on Windows are authoritative: `windows` (build, test,
publish), `security-secrets`, `security-dependencies`, and CodeQL.

## Delivery

- Establish verifiable acceptance criteria from the request and existing
  conventions; verify those outcomes directly without expanding scope.
- Follow existing patterns, preserve unrelated work (including uncommitted
  changes in the worktree), and fix failures caused by your change.
- Add focused regression tests when behavior changes. Run the smallest relevant
  tests first (`--filter`), then the full suite.
- Never weaken or skip tests to get a passing result, and never bypass
  repository protections.
- Never claim validation that was not performed. Distinguish automated UI tests
  from interactive Windows desktop checks, and list any interactive checks that
  were not performed.
- Make routine decisions independently; ask only when a material product
  decision, missing requirement, destructive action, or permission blocks safe
  progress.

## Pull requests

- Work on a feature branch and deliver through a pull request; `main` is
  protected. Fill in the
  [pull request template](PULL_REQUEST_TEMPLATE.md).
- Evaluate review findings against the code. Fix valid findings on the latest
  commit and explain rejected ones with a concrete reason.
- Merging is not implied by an implementation request. Merge only when the
  user or maintainer explicitly asks or authorizes it, and only after required
  checks, reviews, and approvals pass.
- If permissions, unavailable services, or repeated failures block completion,
  report the exact blocker and remaining work instead of claiming completion.

### Privileged changes

These need explicit maintainer confirmation before merging, even when every
check passes:

- `.github/workflows/` and `.github/dependabot.yml`.
- These instructions, `AGENTS.md`, and the pull request template.
- Security tooling and `docs/security-checks.md`.
- `scripts/Install.ps1` and any publishing, packaging, or release logic.
- Code that handles tokens or secrets.
- Any change that expands Refine's tools, its session or conversation access,
  the files it reads, or the data sent to Copilot or anywhere off-machine.

Leave such pull requests unmerged and ask the maintainer to confirm.

## No-side-effect development

- Use temporary prompt files and synthetic clipboard images in tests and manual
  checks.
- Mock or inject refinement; tests must never start Copilot. Do not invoke real
  Copilot refinement to validate a change.
- Do not run `scripts/Install.ps1`, and do not set or alter `COPILOT_EDITOR`,
  `VISUAL`, or `EDITOR`.
- Do not overwrite an installed app in `%LOCALAPPDATA%\PromptEditor`; publish to
  `artifacts\` or another repository-local folder instead.
- Do not read real Copilot session state or conversation history
  (`%USERPROFILE%\.copilot\session-state`); use synthetic `events.jsonl`
  fixtures in temporary directories.

## Privacy

Never commit real prompts, Copilot session logs (`events.jsonl`,
`session-state/`), attachment copies, screenshots containing real content, or
credentials and tokens.

Preserve these invariants:

- Editing is local-only unless the user clicks Refine.
- Submit Prompt returns the prompt to Copilot CLI but never sends it to a model.
- Cancelling writes nothing (no prompt changes, no attachments).
- The Refine helper process runs with no tools.
