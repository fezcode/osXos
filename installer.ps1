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
  [ValidateSet('win-x64')]
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
$outDir      = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "dist\installer"))

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
$version = ([xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'osXos.csproj'))).Project.PropertyGroup.Version |
    Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid installer version: $version" }
$setupPath = [System.IO.Path]::GetFullPath((Join-Path $outDir "osXos-Setup-$version.exe"))
if (-not [string]::Equals((Split-Path $setupPath -Parent), $outDir, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer output must remain directly inside $outDir."
}
foreach ($path in @((Split-Path $outDir -Parent), $outDir)) {
    if ((Test-Path -LiteralPath $path) -and
        ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Refusing to build through a linked installer output directory: $path"
    }
}

function Get-PeSubsystem([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE executable: $Path" }
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 64 -or $peOffset -gt $stream.Length - 94) { throw "Invalid PE header: $Path" }
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x4550) { throw "Invalid PE signature: $Path" }
        $stream.Position = $peOffset + 24 + 68
        $reader.ReadUInt16()
    } finally { $reader.Dispose() }
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
if ((Get-PeSubsystem $forgeGui) -ne 2) { throw "Forge must be a GUI-subsystem executable: $forgeGui" }

Write-Host "`n[4/4] Building Setup.exe..." -ForegroundColor Cyan

# forge.exe is a GUI-subsystem binary, so $LASTEXITCODE is not propagated
# through PowerShell's call operator. Use Start-Process -Wait -PassThru instead.
function Invoke-Forge {
    param([string[]]$ForgeArgs)
    # Start-Process joins ArgumentList into one Windows command line, so each
    # argument needs quoting (and trailing backslashes need doubling inside it).
    $quotedArgs = @($ForgeArgs | ForEach-Object {
        '"' + [regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
    })
    $p = Start-Process -FilePath $forgeGui -ArgumentList ($quotedArgs -join ' ') -WorkingDirectory $PSScriptRoot -Wait -PassThru -WindowStyle Hidden
    if ($p.ExitCode -ne 0) { throw "forge $($ForgeArgs -join ' ') failed (exit $($p.ExitCode))" }
}

Invoke-Forge @("validate", "forge.toml")
# Only this version's intended output can be replaced. Older release installers
# stay in place, and a failed build cannot be mistaken for a previous artifact.
if (Test-Path -LiteralPath $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
Invoke-Forge @("build", "--out", $outDir)

$setup = Get-Item -LiteralPath $setupPath -ErrorAction Stop
if ($setup.PSIsContainer -or $setup.Length -le 0) { throw "Installer payload is missing or empty: $setupPath" }

# Verify the PE subsystem is GUI (2); a console-subsystem setup flashes a
# terminal window at users.
$subsys   = Get-PeSubsystem $setup.FullName
$subName  = switch ($subsys) { 2 { "GUI" } 3 { "CONSOLE" } default { "OTHER($subsys)" } }
if ($subsys -ne 2) { throw "Setup must use the GUI subsystem; got $subName at $setupPath" }

Write-Host ""
Write-Host "Done. Output: $($setup.FullName)" -ForegroundColor Green
Write-Host ("  size:      {0} MB" -f [math]::Round($setup.Length/1MB,1))
Write-Host ("  subsystem: {0}" -f $subName)
