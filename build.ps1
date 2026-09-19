<#
.SYNOPSIS
  Run the tests, then publish osXos as a self-contained build into dist/<rid>.

.DESCRIPTION
  osXos ships one binary per operating system from one codebase, so this takes a
  runtime identifier. The Windows build is what installer.ps1 packages with Forge;
  the macOS and Linux builds are tarred with -Package, because Forge is
  Windows-only and there is no point pretending otherwise.

.EXAMPLE
  .\build.ps1
  .\build.ps1 -Rid osx-arm64 -Package
  .\build.ps1 -Rid linux-x64 -Package
  .\build.ps1 -All
#>
param(
  [ValidateSet('win-x64', 'win-arm64', 'osx-x64', 'osx-arm64', 'linux-x64', 'linux-arm64')]
  [string]   $Rid     = 'win-x64',
  [string]   $Config  = 'Release',
  [switch]   $Package,
  [switch]   $All,
  [switch]   $SkipTests
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# Every RID shipped by a release. -All builds the lot in one go.
$ReleaseRids = @('win-x64', 'osx-arm64', 'osx-x64', 'linux-x64')

function Invoke-Tests {
    Write-Host "Running tests..." -ForegroundColor Cyan
    dotnet test (Join-Path $PSScriptRoot "Tests\osXos.Tests") -c $Config --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed - not publishing." -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

function Get-PubDir([string] $rid) { Join-Path $PSScriptRoot "dist\$rid" }

function Invoke-Publish([string] $rid) {
    # Publish cannot overwrite a running exe; it fails part-way and leaves a stale
    # binary behind that looks like a successful build.
    $running = Get-Process -Name osXos -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping $($running.Count) running osXos instance(s)..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
    }

    $pubDir = Get-PubDir $rid
    if (Test-Path $pubDir) {
        Write-Host "Clearing $pubDir ..." -ForegroundColor Cyan
        Remove-Item $pubDir -Recurse -Force
    }

    # Published as a normal folder of assemblies rather than one packed exe. A
    # single-file build has to unpack itself to a temp directory on every cold
    # start before anything runs, and the installer copies a directory either way.
    Write-Host "`nPublishing osXos for $rid ($Config)..." -ForegroundColor Cyan
    dotnet publish (Join-Path $PSScriptRoot "osXos.csproj") -c $Config -r $rid --self-contained `
        -p:PublishSingleFile=false -p:DebugType=embedded `
        -o $pubDir --nologo | Write-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "PUBLISH FAILED - the binary in $pubDir is STALE." -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # The launcher is osXos.exe on Windows and a bare osXos elsewhere.
    $exeName = 'osXos'
    if ($rid -like 'win-*') { $exeName = 'osXos.exe' }
    $exe = Join-Path $pubDir $exeName
    if (-not (Test-Path $exe)) { throw "Publish reported success but $exe is missing." }

    $files = Get-ChildItem $pubDir -Recurse -File
    Write-Host "Done. Output: $pubDir" -ForegroundColor Green
    Write-Host ("  {0}: {1} MB" -f $exeName, [math]::Round((Get-Item $exe).Length / 1MB, 1))
    Write-Host ("  payload:    {0} files, {1} MB total" -f `
        $files.Count, [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1))
}

function Invoke-Package([string] $rid) {
    $pubDir = Get-PubDir $rid
    # Forge builds the Windows installer; installer.ps1 owns that. Everything else
    # gets a tarball, which is what a macOS or Linux user expects to download.
    if ($rid -like 'win-*') {
        Write-Host "  (win: run .\installer.ps1 to build the Forge installer)" -ForegroundColor DarkGray
        return
    }

    $version = ([xml](Get-Content (Join-Path $PSScriptRoot 'osXos.csproj'))).Project.PropertyGroup.Version `
        | Where-Object { $_ } | Select-Object -First 1
    $outDir = Join-Path $PSScriptRoot 'dist\packages'
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $tar = Join-Path $outDir "osXos-$version-$rid.tar.gz"

    Write-Host "Packaging $tar ..." -ForegroundColor Cyan
    # Explicitly the bsdtar that ships with Windows 10 1803+, never whatever `tar`
    # resolves to: Git for Windows puts GNU tar on PATH, and GNU tar reads a
    # "D:\..." argument as a remote host spec and tries to ssh to a machine called D.
    $bsdtar = Join-Path (Join-Path $env:SystemRoot 'System32') 'tar.exe'
    if (-not (Test-Path $bsdtar)) { throw "Windows bsdtar not found at $bsdtar" }

    # -C keeps the archive rooted at <rid>/ rather than burying the payload under
    # dist/<rid>/.
    & $bsdtar -czf $tar -C (Split-Path $pubDir -Parent) (Split-Path $pubDir -Leaf)
    if ($LASTEXITCODE -ne 0) { throw "tar failed for $rid" }

    Write-Host ("  {0} MB" -f [math]::Round((Get-Item $tar).Length / 1MB, 1)) -ForegroundColor Green
}

if (-not $SkipTests) { Invoke-Tests }

$rids = @($Rid)
if ($All) { $rids = $ReleaseRids }

foreach ($r in $rids) {
    Invoke-Publish $r
    if ($Package -or $All) { Invoke-Package $r }
}
