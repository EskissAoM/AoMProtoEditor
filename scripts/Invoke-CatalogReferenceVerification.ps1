[CmdletBinding()]
param(
    [string[]]$Group,
    [string]$DataBarPath,
    [switch]$LiveCatalogs,
    [ValidateRange(1, 8)][int]$MethodsPerProcess = 8
)

# Focused reference or live-catalog assurance, not the full release gate.
# The checked-in manifest maps current consumer IDs to existing executable tests.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Verification.Common.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$manifestName = if ($LiveCatalogs) { 'CatalogLifecycleChecks.json' } else { 'CatalogReferenceChecks.json' }
$manifest = @(Get-Content -LiteralPath (Join-Path $PSScriptRoot $manifestName) -Raw | ConvertFrom-Json)
if ($Group) {
    foreach ($id in $Group) {
        if ($id -cnotin $manifest.Id) { throw "Unknown reference verification group '$id'." }
    }
    $manifest = @($manifest | Where-Object { $_.Id -cin $Group })
}
$corpus = Resolve-VerificationCorpus -DataBarPath $DataBarPath -ProjectRoot $projectRoot
$resultPrefix = if ($LiveCatalogs) { 'catalog-lifecycles-' } else { 'catalog-references-' }
$results = Join-Path $projectRoot ('AoMDivineDataEditor.Tests\TestResults\' + $resultPrefix + [Guid]::NewGuid().ToString('N'))
$binaryDirectory = Join-Path $results 'test-bin'
New-Item -ItemType Directory -Path $binaryDirectory | Out-Null
$testProject = Join-Path $projectRoot 'AoMDivineDataEditor.Tests\AoMDivineDataEditor.Tests.csproj'
$assembly = Join-Path $binaryDirectory 'AoMDivineDataEditor.Tests.dll'
$previous = Enter-RequiredVerification -DataBarPath $corpus
$previousTemp = $env:TEMP
$previousTmp = $env:TMP
$env:TEMP = Join-Path $results 'temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP | Out-Null
try {
    Invoke-DotNetStep -Name 'Strict reference test compilation' -Arguments @(
        'build', $testProject, '-c', 'Debug', '--no-restore', '-warnaserror',
        '-p:UseSharedCompilation=false', '-nodeReuse:false', '-o', $binaryDirectory)
    $evidence = @()
    foreach ($entry in $manifest) {
        $selectors = @($entry.Checks | ForEach-Object { 'AoMDivineDataEditor.Tests.' + $_ })
        $filter = ($selectors | ForEach-Object { "FullyQualifiedName~$_" }) -join '|'
        $listed = & dotnet vstest $assembly /ListTests "/TestCaseFilter:$filter" 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Discovery failed for $($entry.Id): $listed" }
        $methods = @($listed | ForEach-Object {
            $line = $_.ToString().Trim()
            if ($line -match '^(AoMDivineDataEditor\.Tests\.(?:[A-Za-z0-9_]+\.)+[A-Za-z0-9_]+)(?:\(|$)') { $Matches[1] }
        } | Sort-Object -Unique)
        # A green nonempty batch must not conceal a renamed/deleted registration.
        foreach ($selector in $selectors) {
            if (-not @($methods | Where-Object { $_ -ceq $selector -or $_.StartsWith($selector + '.', [StringComparison]::Ordinal) }).Count) {
                throw "No discovered test for manifest registration '$selector'."
            }
        }
        $reports = @()
        $passed = 0
        for ($offset = 0; $offset -lt $methods.Count; $offset += $MethodsPerProcess) {
            $batch = @($methods | Select-Object -Skip $offset -First $MethodsPerProcess)
            $batchFilter = ($batch | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
            $number = 1 + [int][Math]::Floor($offset / [double]$MethodsPerProcess)
            $report = Join-Path $results "$($entry.Id)-$number.trx"
            Invoke-DotNetStep -Name "$($entry.Id), method batch $number" -Arguments @(
                'vstest', $assembly, "/TestCaseFilter:$batchFilter", "/ResultsDirectory:$results",
                "/Logger:trx;LogFileName=$([IO.Path]::GetFileName($report))", '/Logger:console;verbosity=minimal',
                '/Blame:CollectHangDump;TestTimeout=90s;HangDumpType=none')
            Assert-CompleteTestResult -Path $report
            [xml]$trx = Get-Content -LiteralPath $report -Raw
            $definitions = @($trx.SelectNodes('//*[local-name()="TestMethod"]') | ForEach-Object { $_.className + '.' + $_.name })
            foreach ($method in $batch) {
                if ($method -cnotin $definitions) { throw "Result omitted requested method '$method'." }
            }
            $passed += [int]$trx.SelectSingleNode('//*[local-name()="Counters"]').passed
            $reports += [IO.Path]::GetFileName($report)
        }
        $evidence += [pscustomobject]@{ Group = $entry.Id; Consumers = $entry.Consumers; Methods = $methods; Passed = $passed; Reports = $reports }
        $evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $results 'coverage.json') -Encoding utf8
    }
    Write-Output "Selected reference groups passed: $($manifest.Id -join ', '). Evidence: $results"
} finally {
    $env:TEMP = $previousTemp
    $env:TMP = $previousTmp
    Exit-RequiredVerification -Previous $previous
}
