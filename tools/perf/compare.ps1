# Copyright 2026 H2Sharp contributors.
# Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
#
# Compares two BenchmarkDotNet JSON exports and prints a markdown table:
#
#   powershell tools/perf/compare.ps1 -Baseline artifacts/perf/baseline -Head artifacts/perf/head
#
# Each export directory must contain one <benchmark>-report-full-compressed.json per benchmark.
param(
    [Parameter(Mandatory = $true)][string]$Baseline,
    [Parameter(Mandatory = $true)][string]$Head,
    [double]$GatePercent = 5.0
)

function Get-Report($dir, $name) {
    $path = Join-Path $dir $name
    if (-not (Test-Path $path)) { return $null }
    return Get-Content $path -Raw | ConvertFrom-Json
}

function Format-Delta($before, $after) {
    if ($null -eq $before -or $null -eq $after) { return 'n/a' }
    if ($before -eq 0) {
        if ($after -eq 0) { return '0.0%' }
        return 'new'
    }
    $delta = ($after - $before) / $before * 100.0
    return ('{0:+0.0;-0.0;0.0}%' -f $delta)
}

$rows = @()
foreach ($headFile in Get-ChildItem $Head -Filter '*-report-full-compressed.json') {
    $baselineReport = Get-Report $Baseline $headFile.Name
    if ($null -eq $baselineReport) { continue }
    $headReport = Get-Content $headFile.FullName -Raw | ConvertFrom-Json

    $b = $baselineReport.Benchmarks[0]
    $h = $headReport.Benchmarks[0]
    if ($null -eq $b -or $null -eq $h) { continue }

    $g0 = $b.Memory.Gen0Collections / $b.Memory.TotalOperations
    $h0 = $h.Memory.Gen0Collections / $h.Memory.TotalOperations

    $rows += [pscustomobject]@{
        Benchmark = $h.FullName.Replace('H2Sharp.Benchmarks.', '')
        BeforeNs  = [math]::Round($b.Statistics.Mean, 0)
        AfterNs   = [math]::Round($h.Statistics.Mean, 0)
        MeanDelta = Format-Delta $b.Statistics.Mean $h.Statistics.Mean
        BeforeAlloc = [math]::Round($b.Memory.BytesAllocatedPerOperation, 0)
        AfterAlloc  = [math]::Round($h.Memory.BytesAllocatedPerOperation, 0)
        AllocDelta = Format-Delta $b.Memory.BytesAllocatedPerOperation $h.Memory.BytesAllocatedPerOperation
        BeforeGen0 = ('{0:F6}' -f $g0)
        AfterGen0  = ('{0:F6}' -f $h0)
        Gen0Delta  = Format-Delta $g0 $h0
    }
}

$rows | Sort-Object Benchmark | Format-Table Benchmark, BeforeNs, AfterNs, MeanDelta, BeforeAlloc, AfterAlloc, AllocDelta, BeforeGen0, AfterGen0, Gen0Delta -AutoSize | Out-String -Width 240 | Write-Output

# Gate: any benchmark slower by more than GatePercent% is a regression worth flagging.
$regressions = $rows | Where-Object {
    $_.MeanDelta -match '^\+' -and [double]($_.MeanDelta.TrimEnd('%')) -gt $GatePercent
}
if ($regressions) {
    Write-Host "WARNING: time regressions > $GatePercent%:"
    $regressions | ForEach-Object { Write-Host "  $($_.Benchmark): $($_.MeanDelta)" }
    exit 1
}
exit 0
