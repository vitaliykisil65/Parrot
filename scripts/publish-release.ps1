<#
.SYNOPSIS
  Publishes the version in Parrot.App.csproj as a GitHub release with its installer.

.DESCRIPTION
  Installed copies of Parrot check the latest GitHub release and offer it in the sidebar,
  so publishing a release is what ships an update. Steps:
    1. bump <Version> in src\Parrot.App\Parrot.App.csproj and commit;
    2. pwsh scripts/publish-release.ps1 -Notes "What changed"

  Needs the GitHub CLI (winget install GitHub.cli) signed in with `gh auth login`, and
  <UpdateRepository> in the csproj pointing at the repository the releases go to.

.EXAMPLE
  pwsh scripts/publish-release.ps1 -Notes "Update card in the sidebar"
  pwsh scripts/publish-release.ps1 -NotesFile notes.md
#>
param(
    [string] $Notes,
    [string] $NotesFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Parrot.App\Parrot.App.csproj'

$props = ([xml](Get-Content $project -Raw)).Project.PropertyGroup
$version = $props.Version | Where-Object { $_ } | Select-Object -First 1
$repository = $props.UpdateRepository | Where-Object { $_ } | Select-Object -First 1
$tag = "v$version"

if (-not $repository) { throw 'Set <UpdateRepository>owner/repo</UpdateRepository> in Parrot.App.csproj first.' }
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI not found: winget install GitHub.cli' }
if (-not $Notes -and -not $NotesFile) { throw 'Describe the release: -Notes "..." or -NotesFile path.' }

Push-Location $root
try {
    if (git status --porcelain) { throw 'Commit or stash your changes first: the release must match a commit.' }
    if (git tag --list $tag) { throw "Tag $tag already exists. Bump <Version> in Parrot.App.csproj." }

    & (Join-Path $PSScriptRoot 'build-installer.ps1')
    $installer = Join-Path $root "artifacts\installer\Parrot-Setup-$version.exe"

    git tag -a $tag -m "Parrot $version"
    git push origin HEAD $tag
    if ($LASTEXITCODE) { throw 'git push failed.' }

    $noteArgs = if ($NotesFile) { @('--notes-file', $NotesFile) } else { @('--notes', $Notes) }
    gh release create $tag $installer --repo $repository --title "Parrot $version" @noteArgs
    if ($LASTEXITCODE) { throw 'gh release create failed.' }

    Write-Host "`nReleased $tag to $repository. Installed copies will offer it within six hours." -ForegroundColor Green
}
finally {
    Pop-Location
}
