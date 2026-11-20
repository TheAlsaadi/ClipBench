<#
.SYNOPSIS
    Lists every NuGet package that ships inside ClipBench with its license and publisher, and checks both against the
    project's rules.

.DESCRIPTION
    The shipped packages are the ones in the app's lock file (src/ClipBench.App/packages.lock.json), less the packages
    that are used only to build. The license and authors come from each package's .nuspec in the local NuGet cache, so
    the script makes no network connection; run `dotnet restore --locked-mode` first.

    Without -Check it writes docs/DEPENDENCIES.md. With -Check it writes nothing and exits with 1 when:
      - a shipped package has a license outside the allow list and is not a named exception, or
      - a shipped package (or its family, such as SQLitePCLRaw) is missing from THIRD_PARTY_NOTICES.md, or the
        notices line that names it does not give the version that ships.

.PARAMETER Check
    Check only; exit 1 on any finding.
#>
[CmdletBinding()]
param(
    [switch] $Check
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Licenses that may ship (CLAUDE.md and PLAN.md Phase 9).
$allowed = @('MIT', 'Apache-2.0', 'BSD-2-Clause', 'BSD-3-Clause', 'ISC')

# Microsoft's own runtime pieces ship under Microsoft's license terms, not an open-source license. They are allowed by
# name, each with the reason.
$exceptions = [ordered]@{
    'Microsoft.WindowsAppSDK'             = 'Windows App SDK runtime, Microsoft Software License Terms (ADR 0001)'
    'Microsoft.Web.WebView2'              = 'WebView2 SDK, brought in by the Windows App SDK; Microsoft license terms (ClipBench never uses it: an architecture test forbids it)'
    'Microsoft.Windows.AI.MachineLearning' = 'Windows ML runtime, brought in by the Windows App SDK; Microsoft license terms'
}

# Packages used only while building: they generate code or tools and put nothing in the release.
$buildOnly = @(
    'Microsoft.Windows.CsWin32', 'Microsoft.Windows.SDK.Win32Metadata', 'Microsoft.Windows.SDK.Win32Docs',
    'Microsoft.Windows.WDK.Win32Metadata', 'Microsoft.Windows.SDK.BuildTools', 'Microsoft.Windows.SDK.BuildTools.MSIX'
)

$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }

function Get-Exception([string] $id) {
    foreach ($prefix in $exceptions.Keys) {
        if ($id -eq $prefix -or $id.StartsWith("$prefix.")) { return $exceptions[$prefix] }
    }
    return $null
}

function Read-Nuspec([string] $id, [string] $version) {
    $folder = Join-Path (Join-Path $cache $id.ToLowerInvariant()) $version.ToLowerInvariant()
    $file = Get-ChildItem -Path $folder -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $file) { throw "No .nuspec for $id $version in $folder. Run 'dotnet restore --locked-mode' first." }
    [xml] $xml = Get-Content -Raw -Path $file.FullName
    $metadata = $xml.package.metadata
    $license = $metadata.license
    $expression = $null
    if ($license -and $license.type -eq 'expression') { $expression = $license.'#text' }
    $described = if ($expression) { $expression }
                 elseif ($license) { "file: $($license.'#text')" }
                 elseif ($metadata.licenseUrl) { "url: $($metadata.licenseUrl)" }
                 else { 'none given' }
    [pscustomobject]@{ Expression = $expression; License = $described; Authors = $metadata.authors }
}

# Every shipped package, from every target and runtime section of the app's lock file.
$lock = Get-Content -Raw -Path (Join-Path $root 'src\ClipBench.App\packages.lock.json') | ConvertFrom-Json
$packages = @{}
foreach ($section in $lock.dependencies.PSObject.Properties) {
    foreach ($entry in $section.Value.PSObject.Properties) {
        if ($entry.Value.type -eq 'Project' -or $buildOnly -contains $entry.Name) { continue }
        $packages[$entry.Name] = [pscustomobject]@{
            Id = $entry.Name; Version = $entry.Value.resolved
            Kind = if ($entry.Value.type -eq 'Direct') { 'direct' } else { 'transitive' }
        }
    }
}

$notices = Get-Content -Path (Join-Path $root 'THIRD_PARTY_NOTICES.md')
$findings = New-Object System.Collections.Generic.List[string]
$rows = foreach ($package in $packages.Values | Sort-Object Id) {
    $spec = Read-Nuspec $package.Id $package.Version
    $exception = Get-Exception $package.Id
    $ok = $spec.Expression -and (@($spec.Expression -split ' (?:OR|AND) ' | Where-Object { $allowed -notcontains $_.Trim('() ') }).Count -eq 0)
    if (-not $ok -and -not $exception) {
        $findings.Add("$($package.Id) $($package.Version): license '$($spec.License)' is not allowed and is not a named exception")
    }

    # The notices may name the package itself or its family: the longest leading part of the id they mention, down to
    # one part unless that part is just Microsoft or System.
    $parts = $package.Id.Split('.')
    $line = $null
    for ($count = $parts.Length; $count -ge 1 -and -not $line; $count--) {
        $name = ($parts[0..($count - 1)] -join '.')
        if ($count -eq 1 -and @('Microsoft', 'System') -contains $name) { break }
        # The name must stand alone: Microsoft.Extensions.DependencyInjection must not match the line of its .Abstractions.
        $line = $notices | Where-Object { $_ -match ('(?<![\w.])' + [regex]::Escape($name) + '(?![\w.])') } | Select-Object -First 1
    }
    if (-not $line) {
        $findings.Add("$($package.Id) $($package.Version): not named in THIRD_PARTY_NOTICES.md")
    }
    elseif ($line -notmatch ('(?<![\w.])' + [regex]::Escape($package.Version) + '(?![\w])')) {
        $findings.Add("$($package.Id) $($package.Version): THIRD_PARTY_NOTICES.md names it without this version")
    }

    $why = if ($ok) { 'allowed' } else { "exception: $exception" }
    "| ``$($package.Id)`` | $($package.Version) | $($package.Kind) | $($spec.License) | $($spec.Authors) | $why |"
}

$document = @(
    '# Dependencies'
    ''
    'Every NuGet package that ships inside ClipBench, with its license and publisher. Generated by `scripts/audit-dependencies.ps1` from the app''s lock file and the packages'' own metadata; do not edit by hand. CI runs the same script with `-Check`.'
    ''
    "Allowed licenses: $($allowed -join ', '). Microsoft's own runtime pieces ship under Microsoft's terms and are named exceptions. Packages used only to build ($($buildOnly -join ', ')) put nothing in the release and are not listed."
    ''
    '| Package | Version | Reference | License | Publisher | Verdict |'
    '|---|---|---|---|---|---|'
) + $rows + @(
    ''
    "Findings: $(if ($findings.Count) { $findings -join '; ' } else { 'none' })."
    ''
    'Updates arrive as Dependabot pull requests and are merged by the owner; this file always shows what the main branch ships. Known vulnerabilities are checked separately in CI with `dotnet list package --vulnerable --include-transitive`.'
)
$text = ($document -join "`n") + "`n"
$target = Join-Path $root 'docs\DEPENDENCIES.md'

if ($Check) {
    $current = if (Test-Path $target) { [System.IO.File]::ReadAllText($target) -replace "`r`n", "`n" } else { '' }
    if ($current -ne $text) {
        $findings.Add('docs/DEPENDENCIES.md is out of date: run scripts/audit-dependencies.ps1 and commit the result')
    }

    if ($findings.Count -gt 0) {
        $findings | ForEach-Object { Write-Output "FINDING: $_" }
        exit 1
    }

    Write-Output "All $($packages.Count) shipped packages have an allowed license or a named exception, THIRD_PARTY_NOTICES.md names each one at the version that ships, and docs/DEPENDENCIES.md is current."
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($target, $text, $utf8)
Write-Output "Wrote docs/DEPENDENCIES.md: $($packages.Count) packages, $($findings.Count) findings."
