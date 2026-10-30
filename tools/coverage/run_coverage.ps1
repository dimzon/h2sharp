# Full test run with coverlet (Debug: Release inlining skews line numbers),
# then a merged coverage summary across all suites.
#
# Usage (from the repository root):
#   .\tools\coverage\run_coverage.ps1
#
# The suites must run sequentially: parallel builds contend on the shared
# output DLL and fail with CS2012 file locks.
#
# H2SHARP_NATIVE_TESTS=1 opts the native suite in, the same way CI does.

$ErrorActionPreference = 'Stop'

$suites = @(
    'tests/H2Sharp.Data.Tests/H2Sharp.Data.Tests.csproj',
    'tests/H2Sharp.MvStore.Tests/H2Sharp.MvStore.Tests.csproj',
    'tests/H2Sharp.Engine.Tests/H2Sharp.Engine.Tests.csproj',
    'tests/H2Sharp.Tests/H2Sharp.Tests.csproj',
    'tests/H2Sharp.Native.Tests/H2Sharp.Native.Tests.csproj'
)
# H2SharpSql.Specs has no coverlet collector and is not part of the run.

$env:H2SHARP_NATIVE_TESTS = '1'
foreach ($suite in $suites) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($suite)
    Write-Host "=== $name ==="
    # A suite with known red tests still finishes: keep going through the rest.
    & dotnet test $suite --no-restore -v:minimal `
        --collect:'XPlat Code Coverage' `
        --results-directory "artifacts/TestResults/$name"
}

Write-Host '=== merged coverage ==='
& py -3 tools/coverage/merge_cov.py --results-dir artifacts/TestResults
