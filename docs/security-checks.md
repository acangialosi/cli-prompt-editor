# Repository security checks

Security checks run in GitHub Actions, not as local hooks. Contributors need no
security tooling installed locally.

## Required checks

Branch protection on `main` should require these status checks:

| Check | Workflow | What it verifies |
| --- | --- | --- |
| `windows` | `Build and test` (`.github/workflows/build.yml`) | Tests pass and the portable Windows app publishes. |
| `security-secrets` | `Security` (`.github/workflows/security.yml`) | No secrets in the full Git history of all fetched refs. |
| `security-dependencies` | `Security` | No known-vulnerable NuGet packages, direct or transitive. |

The `Security` workflow runs on pull requests, pushes to `main`, weekly
(Tuesdays 07:37 UTC, so new advisories are caught without code changes), and
manually. All workflows use top-level `permissions: contents: read`, actions
pinned to full commit SHAs, checkout with `persist-credentials: false`, and no
repository secrets.

CI checks run after code reaches GitHub. They block merging, not the initial
push; GitHub secret push protection (below) is the server-side push gate.

## GitHub-side protections

These are repository settings, not files in this repository:

- **Secret scanning** and **push protection**, which reject pushes containing
  supported provider credential patterns.
- **Dependabot alerts** and **Dependabot security updates** (fix PRs, never
  merged automatically).
- **CodeQL default setup** for C# and GitHub Actions.
- **Branch protection on `main`**: pull requests required with 0 required
  approvals; strict required status checks (branch must be up to date) for the
  three checks above; conversation resolution required; force pushes and
  deletion blocked; rules enforced for administrators.

`.github/dependabot.yml` also schedules weekly version updates for NuGet
packages and for the SHA-pinned GitHub Actions (grouped into one PR). Both wait
seven days after a release (cooldown). Dependabot updates action SHAs together
with their `# vX.Y.Z` comments.

## Secret scanning (`security-secrets`)

The job checks out complete history (`fetch-depth: 0`) and fails if the
repository is shallow, so coverage is never silently narrowed. It downloads a
pinned [gitleaks](https://github.com/gitleaks/gitleaks) Linux x64 release into
`$RUNNER_TEMP` and verifies a SHA-256 digest hard-coded in the workflow
(`GITLEAKS_VERSION`, `GITLEAKS_SHA256`) before running it.

gitleaks then scans every commit reachable from all fetched refs
(`gitleaks git --log-opts="--all"`) with its built-in rules:

- `GITLEAKS_CONFIG` / `GITLEAKS_CONFIG_TOML` are unset, and a repository
  `.gitleaks.toml` fails the job, so a change cannot weaken the rules.
- `--ignore-gitleaks-allow` means inline `gitleaks:allow` comments cannot
  bypass the gate.
- `--redact` keeps secret values out of logs. The JSON report is written to
  `$RUNNER_TEMP` and never uploaded. On findings, the job prints only
  `RuleID`, `File`, `StartLine` and `Commit` (no secret, match, message, author
  or email) and fails.
- `--exit-code 2` separates findings (2) from scanner errors (any other
  non-zero code). Scanner errors and missing or malformed reports fail the job.

The scan is local to the runner. gitleaks does not validate credentials with
their providers, and no secrets or code are sent to any other service.

## Dependency auditing (`security-dependencies`)

On `windows-latest` (the app's target), the job restores `PromptEditor.sln`
with NuGet Audit forced on for direct and transitive packages
(`NuGetAuditMode=all`, `NuGetAuditLevel=low`), using `--force-evaluate
--no-cache` so results reflect a fresh resolution. These warnings are promoted
to errors:

- `NU1901`-`NU1904`: a low, moderate, high or critical advisory.
- `NU1900`: vulnerability data could not be fetched, so network failures fail
  instead of passing silently.
- `NU1905`: a configured audit source provides no vulnerability data.

It then runs `dotnet list package --vulnerable --include-transitive --format
json`, prints each vulnerable package's project, version, severity and advisory
URL, and fails if any are reported or the command fails.

`setup-dotnet` installs the latest .NET 8 SDK (8.0.x), matching the build job.
No `global.json` pins the SDK, so a newer SDK preinstalled on the runner may be
selected instead; the commands were verified with both SDK 8.0.427 and 10.0.403.
Setting `NuGetAuditMode=all` matters on SDK 8, whose default audits only direct
references for `net8.0` projects. The advisory data is the GitHub Advisory
Database served by nuget.org; only package IDs and versions are sent to it.

## Remediation

- **Secret finding:** inspect the reported commit and file. If the credential
  is real, revoke or rotate it first, since it is exposed regardless of later
  commits. Deleting the latest copy does not remove it from history; any
  history rewrite must be coordinated explicitly with collaborators, and these
  checks never rewrite history.
- **Dependency finding:** update the affected package (or the direct reference
  that pulls in a vulnerable transitive package) in a reviewed PR, often via a
  Dependabot security update.
- **Tool or network failure:** inspect the failed job and fix the cause. Do not
  treat an incomplete scan or audit as clean.
- **False positive:** add the finding's gitleaks fingerprint to
  `.gitleaksignore` in a reviewed PR with a justification. There are currently
  no exceptions. Do not use inline `gitleaks:allow`, a custom config, or NuGet
  advisory suppressions to silence findings.

Automatic Copilot remediation and a custom security-tool updater are
intentionally not used. Upgrading the pinned gitleaks version is a manual,
reviewed PR: update `GITLEAKS_VERSION` and take `GITLEAKS_SHA256` for
`gitleaks_<version>_linux_x64.tar.gz` from the release's official
`gitleaks_<version>_checksums.txt`.

## Coverage limits

A clean result does not prove that no secret exists or that dependencies are
safe. gitleaks matches patterns with filters; it misses unknown or unusual
formats and does not scan inside archives. CI sees only history fetched from
GitHub. NuGet Audit covers known advisories for NuGet packages only, not the
.NET runtime/SDK, Windows, or unpublished vulnerabilities. CodeQL and GitHub
secret scanning provide complementary, not complete, coverage.
