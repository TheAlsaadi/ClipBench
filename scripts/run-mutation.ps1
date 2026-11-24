<#
.SYNOPSIS
    Runs Stryker.NET mutation testing on ClipBench.Core or on the encryption code in ClipBench.Storage.

.DESCRIPTION
    Stryker changes the code one small piece at a time (a > becomes >=, a statement is removed) and runs the tests
    against each change. A change the tests do not notice is a "survivor": a place where the tests could be stronger.
    The tool is pinned in dotnet-tools.json (ADR 0020). It runs by hand, not in CI; a full Core run takes about
    15 minutes. The report opens from tests/<project>/StrykerOutput/<time>/reports/mutation-report.html.

    Each run fails when the score drops below the "break" threshold in that project's stryker-config.json, which is
    set to the score recorded in docs/MUTATION.md.

.PARAMETER Project
    Core (the default) or Storage.

.PARAMETER Mutate
    Optional file patterns to limit the run, for example "**/Pasting/QueueRunner.cs".
#>
[CmdletBinding()]
param(
    [ValidateSet('Core', 'Storage')]
    [string] $Project = 'Core',

    [string[]] $Mutate = @()
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }
}
finally {
    Pop-Location
}

$arguments = @('stryker', '--config-file', 'stryker-config.json')
foreach ($pattern in $Mutate) {
    $arguments += @('--mutate', $pattern)
}

Push-Location (Join-Path $root "tests\ClipBench.$Project.Tests")
try {
    dotnet @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
