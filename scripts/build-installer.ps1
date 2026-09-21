<#
.SYNOPSIS
  Builds artifacts\installer\Parrot-Setup-<version>.exe.

.DESCRIPTION
  Runs the tests, publishes the self-contained exe and compiles installer\Parrot.iss with
  Inno Setup. The version comes from <Version> in src\Parrot.App\Parrot.App.csproj.

.EXAMPLE
  pwsh scripts/build-installer.ps1
  pwsh scripts/build-installer.ps1 -SkipTests -Version 0.1.1   # a throwaway build for testing updates
#>
param(
    [switch] $SkipTests,
    # Overrides the csproj version without editing it.
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Parrot.App\Parrot.App.csproj'

if (-not $Version) {
    $Version = ([xml](Get-Content $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
}
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$Version'." }

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup not found. Install it: winget install JRSoftware.InnoSetup' }

Write-Host "Parrot $Version" -ForegroundColor Cyan

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'tests\Parrot.Core.Tests') --nologo
    if ($LASTEXITCODE) { throw 'Tests failed.' }
}

dotnet publish $project -p:PublishProfile=win-x64 -p:Version=$Version --nologo
if ($LASTEXITCODE) { throw 'Publish failed.' }

& $iscc /Qp "/DAppVersion=$Version" (Join-Path $root 'installer\Parrot.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed.' }

$installer = Join-Path $root "artifacts\installer\Parrot-Setup-$Version.exe"
Write-Host "`n$installer ($([math]::Round((Get-Item $installer).Length / 1MB, 1)) MB)" -ForegroundColor Green
