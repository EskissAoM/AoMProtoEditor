[CmdletBinding()]
param(
    [ValidateRange(1, 32)]
    [int]$WorkflowBatchSize = 8,

    [string]$DataBarPath
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot 'AoMDivineDataEditor.Tests\AoMDivineDataEditor.Tests.csproj'
$editorProject = Join-Path $projectRoot 'AoMDivineDataEditor.csproj'
$debugExecutable = Join-Path $projectRoot 'bin\Debug\net10.0\AoMDivineDataEditor.exe'
$releaseExecutable = Join-Path $projectRoot 'bin\Release\net10.0\win-x64\AoMDivineDataEditor.exe'
$workflowClass = 'AoMDivineDataEditor.Tests.PowerEditorWorkflowTests'

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Output "`n=== $Name ==="
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-TestFilter {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Filter
    )

    Invoke-DotNetStep -Name $Name -Arguments @(
        'test', $testProject,
        '-c', 'Debug',
        '--no-build',
        '--no-restore',
        '--filter', $Filter,
        '--logger', 'console;verbosity=minimal'
    )
}

if ([string]::IsNullOrWhiteSpace($DataBarPath)) {
    $DataBarPath = $env:AOM_DATA_BAR
}

if ([string]::IsNullOrWhiteSpace($DataBarPath)) {
    $DataBarPath = Join-Path (Split-Path -Parent $projectRoot) `
        'Age of Mythology Retold\game\data\Data.bar'
}

if (-not (Test-Path -LiteralPath $DataBarPath -PathType Leaf)) {
    throw "The Power release gate requires the original Data.bar corpus. Pass -DataBarPath or set AOM_DATA_BAR. The resolved path was '$DataBarPath'."
}

$DataBarPath = (Resolve-Path -LiteralPath $DataBarPath).Path
$previousDataBarPath = $env:AOM_DATA_BAR

try {
    $env:AOM_DATA_BAR = $DataBarPath

    Invoke-DotNetStep -Name 'Debug compile with warnings treated as errors' -Arguments @(
        'build', $testProject,
        '-c', 'Debug',
        '--no-restore',
        '-warnaserror'
    )

    Invoke-TestFilter -Name 'Power unit, architecture, validation, interaction, and memory regressions' `
        -Filter 'FullyQualifiedName~Power&FullyQualifiedName!~PowerEditorWorkflowTests&Category!=Integration'

    $corpusTests = @(
        'InstalledOriginalCorpusMatchesTheCheckedInSchemaBaseline',
        'EveryInstalledOriginalOpensWithoutPopulationFailure',
        'UntouchedModEligibleOriginalClonesRemainSemanticallyIdenticalAfterOpenAndSave'
    )
    foreach ($testName in $corpusTests) {
        Invoke-TestFilter -Name "Original corpus: $testName" `
            -Filter "FullyQualifiedName=AoMDivineDataEditor.Tests.PowerEditorAuditBaselineTests.$testName"
    }

    Write-Output "`n=== Discover Power workflow regressions ==="
    $listOutput = & dotnet test $testProject `
        -c Debug `
        --no-build `
        --no-restore `
        --list-tests `
        --filter "FullyQualifiedName~$workflowClass" 2>&1
    if ($LASTEXITCODE -ne 0) {
        $listOutput | Write-Output
        throw "Power workflow test discovery failed with exit code $LASTEXITCODE."
    }

    $workflowTests = @($listOutput |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_.StartsWith("$workflowClass.", [StringComparison]::Ordinal) } |
        Sort-Object -Unique)
    if ($workflowTests.Count -eq 0) {
        throw 'No PowerEditorWorkflowTests were discovered; the release gate cannot claim workflow coverage.'
    }

    Write-Output "Discovered $($workflowTests.Count) Power workflow tests."
    $batchCount = [Math]::Ceiling($workflowTests.Count / [double]$WorkflowBatchSize)
    for ($offset = 0; $offset -lt $workflowTests.Count; $offset += $WorkflowBatchSize) {
        $batch = @($workflowTests | Select-Object -Skip $offset -First $WorkflowBatchSize)
        $filter = ($batch | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'
        $batchNumber = [Math]::Floor($offset / $WorkflowBatchSize) + 1
        Invoke-TestFilter -Name "Power workflow batch $batchNumber of $batchCount ($($batch.Count) tests)" `
            -Filter $filter
    }

    $supportFilter = @(
        'FullyQualifiedName~CrossModuleReferenceWorkflowTests',
        'FullyQualifiedName~FileIntegrityTransactionTests',
        'FullyQualifiedName~ModXmlDocumentSafetyTests',
        'FullyQualifiedName~ModDeletionSafetyTests'
    ) -join '|'
    Invoke-TestFilter -Name 'Cross-module, transaction, malformed-input, and filesystem safety regressions' `
        -Filter $supportFilter

    if (-not (Test-Path -LiteralPath $debugExecutable -PathType Leaf)) {
        throw "The Debug executable was not produced at '$debugExecutable'."
    }

    Invoke-DotNetStep -Name 'Release compile with warnings treated as errors' -Arguments @(
        'build', $editorProject,
        '-c', 'Release',
        '--no-restore',
        '-warnaserror'
    )

    if (-not (Test-Path -LiteralPath $releaseExecutable -PathType Leaf)) {
        throw "The Release executable was not produced at '$releaseExecutable'."
    }

    $debugArtifact = Get-Item -LiteralPath $debugExecutable
    $releaseArtifact = Get-Item -LiteralPath $releaseExecutable
    Write-Output "`nPower Editor release gate passed."
    Write-Output "Original corpus: $DataBarPath"
    Write-Output "Power workflows: $($workflowTests.Count) tests in $batchCount isolated batches"
    Write-Output "Debug executable: $($debugArtifact.FullName) ($($debugArtifact.Length) bytes)"
    Write-Output "Release executable: $($releaseArtifact.FullName) ($($releaseArtifact.Length) bytes)"
}
finally {
    $env:AOM_DATA_BAR = $previousDataBarPath
}
