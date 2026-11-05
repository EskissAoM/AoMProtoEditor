[CmdletBinding()]
param(
    [ValidateRange(1, 32)]
    [int]$WorkflowBatchSize = 8,

    [string]$DataBarPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Verification.Common.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot 'AoMDivineDataEditor.Tests\AoMDivineDataEditor.Tests.csproj'
$editorProject = Join-Path $projectRoot 'AoMDivineDataEditor.csproj'
$debugExecutable = Join-Path $projectRoot 'bin\Debug\net10.0\AoMDivineDataEditor.exe'
$releaseExecutable = Join-Path $projectRoot 'bin\Release\net10.0\win-x64\AoMDivineDataEditor.exe'
$workflowClass = 'AoMDivineDataEditor.Tests.PowerEditorWorkflowTests'

function Invoke-TestFilter {
    param(
        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [string]$Filter
    )

    Invoke-VerifiedTests -Name $Name -TestProject $testProject -ResultsDirectory $resultsDirectory -Filter $Filter
}

$DataBarPath = Resolve-VerificationCorpus -DataBarPath $DataBarPath -ProjectRoot $projectRoot
$resultsDirectory = Join-Path $projectRoot ('AoMDivineDataEditor.Tests\TestResults\power-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultsDirectory | Out-Null
$previous = Enter-RequiredVerification -DataBarPath $DataBarPath

try {
    Invoke-RequiredDependencyAudit -TestProject $testProject -ResultsDirectory $resultsDirectory

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

    Invoke-TestFilter -Name 'Technology corpus inventory' `
        -Filter 'FullyQualifiedName=AoMDivineDataEditor.Tests.TechnologyEffectCatalogInventoryTests.ConfiguredOriginalCorpusMatchesTheCheckedInAudit'
    Invoke-TestFilter -Name 'Manager and recovery safety acceptance checks' -Filter 'FullyQualifiedName~BaselineSafetyTests'

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

    Invoke-StrictReleaseBuild -EditorProject $editorProject

    if (-not (Test-Path -LiteralPath $releaseExecutable -PathType Leaf)) {
        throw "The Release executable was not produced at '$releaseExecutable'."
    }

    $debugArtifact = Get-Item -LiteralPath $debugExecutable
    $releaseArtifact = Get-Item -LiteralPath $releaseExecutable
    Write-Output "`nPower Editor release gate passed."
    Write-Output "Evidence: $resultsDirectory"
    Write-Output "Original corpus: $DataBarPath"
    Write-Output "Power workflows: $($workflowTests.Count) tests in $batchCount isolated batches"
    Write-Output "Debug executable: $($debugArtifact.FullName) ($($debugArtifact.Length) bytes)"
    Write-Output "Release executable: $($releaseArtifact.FullName) ($($releaseArtifact.Length) bytes)"
}
finally {
    Exit-RequiredVerification -Previous $previous
}
