<#
.SYNOPSIS
  Build the osXos Windows installer (Setup.exe) with Forge.

.DESCRIPTION
  Publishes the app (unless -SkipBuild), makes sure a GUI-subsystem forge.exe
  exists, then validates forge.toml and stamps the payload into a Setup.exe under
  dist/installer.

.EXAMPLE
  .\installer.ps1
  .\installer.ps1 -SkipBuild
#>
param(
  [string]$Rid      = "win-x64",
  [string]$Config   = "Release",
  [string]$ForgeDir = "..\Forge",
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$forgeRoot   = Resolve-Path $ForgeDir
$forgeGui    = Join-Path $forgeRoot "build\forge.exe"
$forgeSrc    = Join-Path $forgeRoot "cmd\forge"
$uninstaller = Join-Path $forgeRoot "build\uninstall.exe"
$outDir      = Join-Path $PSScriptRoot "dist\installer"

if (-not $SkipBuild) {
    Write-Host "[1/4] Publishing osXos ($Rid, $Config)..." -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot "build.ps1") -Rid $Rid -Config $Config
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 failed (exit $LASTEXITCODE)" }
} else {
    Write-Host "[1/4] Skipping publish (-SkipBuild)" -ForegroundColor DarkGray
}

# Runs after the publish above, and checks the published binary too: the source
# files agreeing with each other says nothing about the version the installed app
# will actually report. With -SkipBuild in particular, a stale dist would other-
# wise ship an old app under the new version number.
Write-Host "`n[2/4] Verifying the version is consistent..." -ForegroundColor Cyan
& (Join-Path $PSScriptRoot "version.ps1") -RequireBuild -Rid $Rid
if ($LASTEXITCODE -ne 0) {
    throw "Version mismatch. If only dist/$Rid disagrees, re-run .\build.ps1; otherwise run .\version.ps1 -Set x.y.z"
}

Write-Host "`n[3/4] Ensuring a GUI-subsystem forge.exe..." -ForegroundColor Cyan
if (-not (Test-Path $uninstaller)) {
    throw "Missing $uninstaller. Run 'gobake build' in $forgeRoot first."
}
$needBuild = -not (Test-Path $forgeGui)
if (-not $needBuild) {
    $srcLatest = (Get-ChildItem $forgeSrc -Recurse -Filter *.go |
                  Sort-Object LastWriteTime -Descending |
                  Select-Object -First 1).LastWriteTime
    if ((Get-Item $forgeGui).LastWriteTime -lt $srcLatest) { $needBuild = $true }
}
if ($needBuild) {
    Write-Host "  building $forgeGui..." -ForegroundColor DarkGray
    Push-Location $forgeRoot
    try {
        go build -tags "desktop,production" -ldflags "-H windowsgui -X main.Version=local-gui" -o build\forge.exe ./cmd/forge/
        if ($LASTEXITCODE -ne 0) { throw "go build forge.exe failed (exit $LASTEXITCODE)" }
    } finally { Pop-Location }
} else {
    Write-Host "  up to date: $forgeGui" -ForegroundColor DarkGray
}

Write-Host "`n[4/4] Building Setup.exe..." -ForegroundColor Cyan

# forge.exe is a GUI-subsystem binary, so $LASTEXITCODE is not propagated
# through PowerShell's call operator. Use Start-Process -Wait -PassThru instead.
function Invoke-Forge {
    param([string[]]$ForgeArgs)
    $p = Start-Process -FilePath $forgeGui -ArgumentList $ForgeArgs -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) { throw "forge $($ForgeArgs -join ' ') failed (exit $($p.ExitCode))" }
}

Invoke-Forge @("validate", "forge.toml")
Invoke-Forge @("build", "--out", $outDir)

$setup = Get-ChildItem $outDir -Filter "osXos-Setup-*.exe" |
         Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw "Setup.exe not found in $outDir after build." }

# Verify the PE subsystem is GUI (2); a console-subsystem setup flashes a
# terminal window at users.
$bytes    = [System.IO.File]::ReadAllBytes($setup.FullName)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
$subsys   = [BitConverter]::ToUInt16($bytes, $peOffset + 24 + 68)
$subName  = switch ($subsys) { 2 { "GUI" } 3 { "CONSOLE" } default { "OTHER($subsys)" } }

Write-Host ""
Write-Host "Done. Output: $($setup.FullName)" -ForegroundColor Green
Write-Host ("  size:      {0} MB" -f [math]::Round($setup.Length/1MB,1))
Write-Host ("  subsystem: {0}" -f $subName)
if ($subsys -ne 2) {
    Write-Warning "Subsystem is not GUI - a console window will appear when users run Setup.exe."
}
