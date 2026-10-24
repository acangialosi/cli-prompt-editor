[CmdletBinding()]
param(
    [string] $InstallDirectory = (Join-Path $env:LOCALAPPDATA 'PromptEditor'),
    [switch] $ConfigureCopilot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\PromptEditor\PromptEditor.csproj'
& dotnet publish $project -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -o $InstallDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Publishing Prompt Editor failed (exit $LASTEXITCODE). Install the .NET 8 SDK."
}

$executable = Join-Path $InstallDirectory 'PromptEditor.exe'
# Copilot resolves the executable directly; embedded quotes become part of its name.
$editorCommand = $executable
if ($ConfigureCopilot) {
    [Environment]::SetEnvironmentVariable('COPILOT_EDITOR', $editorCommand, 'User')
    $env:COPILOT_EDITOR = $editorCommand
    Write-Host 'Configured COPILOT_EDITOR for this shell and future terminals.'
    Write-Host 'Restart an already-running Copilot session to pick up this change.'
}
Write-Host "Installed: $executable"
Write-Host ('To configure this shell: $env:COPILOT_EDITOR = ''{0}''' -f $editorCommand)
