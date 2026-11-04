# Agent instructions

The authoritative instructions for coding agents in this repository are in
[`.github/copilot-instructions.md`](.github/copilot-instructions.md). Read them
before making changes; this file only summarizes the essentials.

- **Test:** `dotnet test PromptEditor.sln -c Release` (Windows, .NET 8 SDK;
  warnings are errors). Use `--filter` for targeted runs first.
- **No side effects:** use temporary prompt files and synthetic images; mock
  Refine and never start Copilot in tests. Don't run `scripts/Install.ps1`,
  change `COPILOT_EDITOR`/`VISUAL`/`EDITOR`, overwrite the installed app in
  `%LOCALAPPDATA%\PromptEditor`, or read real Copilot session state.
- **Privacy:** never commit real prompts, Copilot session logs, attachment
  copies, real-content screenshots, or credentials. Editing stays local unless
  the user clicks Refine, and the Refine helper has no tools.
- **Pull requests:** work on a feature branch and fill in the
  [pull request template](.github/PULL_REQUEST_TEMPLATE.md). Changes to
  workflows, Dependabot, security tooling, agent instructions, the installer,
  publishing or release logic, token-handling code, or Refine's data access
  need explicit maintainer confirmation before merging.
- **Merging:** merge only when the user or maintainer explicitly asks or
  authorizes it; an implementation request alone is not authorization.
