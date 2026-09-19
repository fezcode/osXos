<#
.SYNOPSIS
  Read, set, or bump the osXos release version everywhere it lives, then verify
  those places agree.

.DESCRIPTION
  The version appears in osXos.csproj (<Version>) and in forge.toml (the [app]
  version, the "osXos x.y.z" wizard strings, and the registry Version data).
  Keeping them in lockstep stops a release shipping mismatched numbers — the
  About panel reads its version off the assembly, so a stale forge.toml would
  otherwise advertise one version and install another.

  -RequireBuild additionally checks the published binary under dist/<rid>. The
  source files agreeing with each other proves nothing about what the app will
  actually display: publish a release from a stale dist and the installer
  advertises the new version while the About panel still reports the old one.

.EXAMPLE
  .\version.ps1                  # report current version + consistency
  .\version.ps1 -RequireBuild    # also check the published dist binary
  .\version.ps1 -Set 0.5.0       # set everywhere, then verify
  .\version.ps1 -Bump patch      # 0.4.0 -> 0.4.1 everywhere, then verify
  .\version.ps1 -Bump minor -DryRun
#>
param(
    [string] $Set,
    [ValidateSet('patch', 'minor', 'major')]
    [string] $Bump,
    [switch] $DryRun,
    [switch] $RequireBuild,
    [string] $Rid = 'win-x64',
    [string] $RepoRoot = $PSScriptRoot
)

# ---- pure version helpers ---------------------------------------------------

function Parse-SemVer([string] $text) {
    if ($text -notmatch '^\s*(\d+)\.(\d+)\.(\d+)\s*$') {
        throw "Not a valid x.y.z version: '$text'"
    }
    [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
    }
}

function Format-SemVer($v) { "{0}.{1}.{2}" -f $v.Major, $v.Minor, $v.Patch }

function Step-SemVer([string] $current, [string] $kind) {
    $v = Parse-SemVer $current
    switch ($kind) {
        'patch' { $v.Patch++ }
        'minor' { $v.Minor++; $v.Patch = 0 }
        'major' { $v.Major++; $v.Minor = 0; $v.Patch = 0 }
        default { throw "Unknown bump kind: '$kind'" }
    }
    Format-SemVer $v
}

function Normalize-Ver([string] $s) {
    # Collapse a 3- or 4-part version to canonical x.y.z.
    $p = $s.Trim() -split '\.'
    if ($p.Count -lt 3) { return $s.Trim() }
    "{0}.{1}.{2}" -f $p[0], $p[1], $p[2]
}

# ---- file locations ---------------------------------------------------------

function Get-CsprojPath([string] $root) { Join-Path $root 'osXos.csproj' }
function Get-ForgePath ([string] $root) { Join-Path $root 'forge.toml' }

# Every place a version lives, as { Where = <label>; Value = <canonical x.y.z> }.
function Get-VersionItems([string] $root) {
    $cs = [System.IO.File]::ReadAllText((Get-CsprojPath $root))
    $items = New-Object System.Collections.Generic.List[object]

    if ($cs -match '<Version>\s*(\d+(?:\.\d+)+)\s*</Version>') {
        $items.Add([pscustomobject]@{ Where = 'csproj <Version>'; Value = (Normalize-Ver $Matches[1]) })
    }

    $forgePath = Get-ForgePath $root
    if (Test-Path $forgePath) {
        $fg = [System.IO.File]::ReadAllText($forgePath)
        if ($fg -match '(?m)^version\s*=\s*"(\d+(?:\.\d+)+)"') {
            $items.Add([pscustomobject]@{ Where = 'forge [app] version'; Value = (Normalize-Ver $Matches[1]) })
        }
        $wiz = [regex]::Matches($fg, 'osXos (\d+\.\d+\.\d+)')
        for ($i = 0; $i -lt $wiz.Count; $i++) {
            $items.Add([pscustomobject]@{ Where = "forge wizard string #$($i + 1)"; Value = (Normalize-Ver $wiz[$i].Groups[1].Value) })
        }
        if ($fg -match 'value\s*=\s*"Version"\s*\r?\n\s*data\s*=\s*"(\d+(?:\.\d+)+)"') {
            $items.Add([pscustomobject]@{ Where = 'forge registry Version'; Value = (Normalize-Ver $Matches[1]) })
        }
    }
    , $items.ToArray()
}

# Rewrite every location to $target. Targeted replacements only — decoys such as
# name = "osXos" and the InstallDir registry entry are left alone.
function Set-Versions([string] $root, [string] $target, [switch] $DryRun) {
    $v      = Parse-SemVer $target          # validates the input
    $target = Format-SemVer $v

    $csprojPath = Get-CsprojPath $root
    $forgePath  = Get-ForgePath  $root

    $cs = [System.IO.File]::ReadAllText($csprojPath)
    $cs = [regex]::Replace($cs, '(<Version>)\s*\d+(?:\.\d+)+\s*(</Version>)', "`${1}$target`${2}")

    $fg = $null
    if (Test-Path $forgePath) {
        $fg = [System.IO.File]::ReadAllText($forgePath)
        $fg = [regex]::Replace($fg, '(?m)^(version\s*=\s*")\d+(?:\.\d+)+(")', "`${1}$target`${2}")
        $fg = [regex]::Replace($fg, 'osXos \d+\.\d+\.\d+', "osXos $target")
        $fg = [regex]::Replace($fg, '(value\s*=\s*"Version"\s*\r?\n\s*data\s*=\s*")\d+(?:\.\d+)+(")', "`${1}$target`${2}")
    }

    if (-not $DryRun) {
        # No BOM: the csproj and forge.toml are read by tools that do not expect one.
        $enc = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($csprojPath, $cs, $enc)
        if ($fg -ne $null) { [System.IO.File]::WriteAllText($forgePath, $fg, $enc) }
    }
}

# The version the published app will actually report at runtime.
#
# This is the one that matters and the one the source files cannot vouch for: the
# About panel reads it off the assembly, so a stale dist ships an old app under a
# new number while csproj and forge.toml agree perfectly with each other.
function Get-BuiltVersion([string] $root, [string] $rid) {
    $dir = Join-Path (Join-Path $root 'dist') $rid
    $dll = Join-Path $dir 'osXos.dll'
    if (Test-Path $dll) {
        try {
            return Normalize-Ver ([System.Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString())
        } catch {
            # Fall through to the exe below.
        }
    }
    $exe = Join-Path $dir 'osXos.exe'
    if (Test-Path $exe) {
        try {
            return Normalize-Ver ([System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion)
        } catch {
            return $null
        }
    }
    return $null
}

# Compare every location against $target (defaults to csproj <Version>).
function Test-Consistent([string] $root, [string] $target, [switch] $IncludeBuild, [string] $rid = 'win-x64') {
    $items = Get-VersionItems $root
    if (-not $target) {
        $target = ($items | Where-Object { $_.Where -eq 'csproj <Version>' } | Select-Object -First 1).Value
    }
    $results = foreach ($it in $items) {
        [pscustomobject]@{ Where = $it.Where; Value = $it.Value; Ok = ($it.Value -eq $target) }
    }

    # Only checked on demand. A bump legitimately leaves dist stale until the next
    # publish, so failing the everyday report on it would train people to ignore it.
    if ($IncludeBuild) {
        $built = Get-BuiltVersion $root $rid
        # Windows PowerShell 5.1 has no if-expression, so the label is computed first.
        $shown = '(not published)'
        if ($built) { $shown = $built }
        $results = @($results) + [pscustomobject]@{
            Where = "dist/$rid binary"
            Value = $shown
            Ok    = ($built -eq $target)
        }
    }
    [pscustomobject]@{
        Target = $target
        Items  = $results
        AllOk  = (@($results | Where-Object { -not $_.Ok }).Count -eq 0)
    }
}

# ---- CLI --------------------------------------------------------------------

function Show-Report([string] $root, [string] $target, [switch] $IncludeBuild, [string] $rid = 'win-x64') {
    $c = Test-Consistent $root $target -IncludeBuild:$IncludeBuild -rid $rid
    Write-Host "Version locations (target $($c.Target)):"
    foreach ($it in $c.Items) {
        $tag   = if ($it.Ok) { 'ok ' } else { 'BAD' }
        $color = if ($it.Ok) { 'DarkGreen' } else { 'Red' }
        Write-Host ("  [{0}] {1,-26} {2}" -f $tag, $it.Where, $it.Value) -ForegroundColor $color
    }
    if ($c.AllOk) { Write-Host "All locations agree." -ForegroundColor Green }
    else          { Write-Host "MISMATCH: not all locations agree." -ForegroundColor Red }
    if ($IncludeBuild -and -not $c.AllOk) {
        Write-Host "  The published build is what the About panel reports. Re-run .\build.ps1." -ForegroundColor Yellow
    }
    $c.AllOk
}

function Main {
    if ($Set -and $Bump) { Write-Error 'Specify only one of -Set or -Bump.'; exit 2 }

    if ($Set) {
        $target = Format-SemVer (Parse-SemVer $Set)
    }
    elseif ($Bump) {
        $cur = ((Get-VersionItems $RepoRoot) | Where-Object { $_.Where -eq 'csproj <Version>' } | Select-Object -First 1).Value
        if (-not $cur) { Write-Error 'Could not read the current version from osXos.csproj.'; exit 2 }
        $target = Step-SemVer $cur $Bump
        Write-Host "Bumping $cur -> $target ($Bump)" -ForegroundColor Cyan
    }
    else {
        # Report-only mode.
        $ok = Show-Report $RepoRoot $null -IncludeBuild:$RequireBuild -rid $Rid
        exit ([int](-not $ok))
    }

    if ($DryRun) {
        Write-Host "DryRun: would set version to $target everywhere (no files written)." -ForegroundColor Yellow
        exit 0
    }

    Set-Versions $RepoRoot $target
    Write-Host "Set version to $target. Verifying..." -ForegroundColor Cyan
    $ok = Show-Report $RepoRoot $target
    exit ([int](-not $ok))
}

# Run Main only when executed directly; dot-sourcing (e.g. from tests) sets
# InvocationName to '.' and must not trigger the CLI.
if ($MyInvocation.InvocationName -ne '.') { Main }
