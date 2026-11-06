# Copyright 2026 H2Sharp contributors.
# Licensed under the MPL 2.0 or the EPL 1.0 (see LICENSE-H2.txt).
#
# Runs the Java-vs-.NET benchmark suite (tools/perf/java-bench/CrossBench.java vs
# tools/perf/H2Sharp.CrossBench) and prints a markdown comparison table:
#
#   powershell tools/perf/bench-cross.ps1 -Label first
#
# Requirements: JDK (javac/java on PATH), the reference jar built
# (cd reference/h2/h2 && mvn -B -q -DskipTests package), and the .NET SDK.
# Results land in artifacts/perf/cross/<label>-java.json and <label>-dotnet.json.
param(
    [string]$Label = "cross"
)

# Decimal point in the table regardless of the thread locale (JSON inputs are invariant).
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$javaDir = Join-Path $PSScriptRoot "java-bench"
$crossProj = Join-Path $PSScriptRoot "H2Sharp.CrossBench"
$outDir = Join-Path $root "artifacts\perf\cross"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# --- locate the reference jar (same glob as JavaOracle) ---
$jar = Get-ChildItem (Join-Path $root "reference\h2\h2\target") -Filter "h2-*.jar" |
    Where-Object { $_.Name -notmatch "sources|javadoc" } |
    Select-Object -First 1
if (-not $jar) {
    Write-Host "ERROR: reference H2 jar not found. Build it with:" -ForegroundColor Red
    Write-Host "  (cd reference/h2/h2 && mvn -B -q -DskipTests package)"
    exit 2
}

# --- compile and run the Java driver ---
$javaOut = Join-Path $outDir "java-classes"
New-Item -ItemType Directory -Force -Path $javaOut | Out-Null
& javac -encoding UTF-8 -cp $jar.FullName -d $javaOut (Join-Path $javaDir "CrossBench.java")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$javaJson = Join-Path $outDir "$Label-java.json"
& java -cp "$javaOut;$($jar.FullName)" CrossBench | Set-Content -Encoding utf8 $javaJson
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# --- build and run the .NET driver ---
$dotnetJson = Join-Path $outDir "$Label-dotnet.json"
dotnet run -c Release --project $crossProj | Set-Content -Encoding utf8 $dotnetJson
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# --- merge and print the comparison table ---
$javaResults = @{}
Get-Content $javaJson | ConvertFrom-Json | ForEach-Object { $javaResults[$_.benchmark] = $_ }
$dotnetResults = @{}
Get-Content $dotnetJson | ConvertFrom-Json | ForEach-Object { $dotnetResults[$_.benchmark] = $_ }

$rows = foreach ($name in ($javaResults.Keys | Sort-Object)) {
    $j = $javaResults[$name]
    $d = $dotnetResults[$name]
    if (-not $d) { continue }
    [pscustomobject]@{
        Scenario        = $name
        JavaNs         = [math]::Round($j.meanNs, 1)
        DotnetNs       = [math]::Round($d.meanNs, 1)
        Ratio          = ('{0:F2}x' -f ($j.meanNs / $d.meanNs))
        JavaOpsSec     = [math]::Round($j.opsPerSec, 0)
        DotnetOpsSec   = [math]::Round($d.opsPerSec, 0)
        DotnetAllocB   = [math]::Round($d.allocBytesPerOp, 0)
    }
}

Write-Host "Java vs .NET (H2 reference 2.5.x vs H2Sharp port), label: $Label"
$rows | Format-Table Scenario, JavaNs, DotnetNs, Ratio, JavaOpsSec, DotnetOpsSec, DotnetAllocB -AutoSize |
    Out-String -Width 220 | Write-Output
