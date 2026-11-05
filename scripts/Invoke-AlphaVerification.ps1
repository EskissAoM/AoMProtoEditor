[CmdletBinding()]
param([string]$DataBarPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Verification.Common.ps1')
$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot 'AoMDivineDataEditor.Tests\AoMDivineDataEditor.Tests.csproj'
$editorProject = Join-Path $projectRoot 'AoMDivineDataEditor.csproj'
$releaseExecutable = Join-Path $projectRoot 'bin\Release\net10.0\win-x64\AoMDivineDataEditor.exe'
$corpus = Resolve-VerificationCorpus -DataBarPath $DataBarPath -ProjectRoot $projectRoot
$resultsDirectory = Join-Path $projectRoot ('AoMDivineDataEditor.Tests\TestResults\alpha-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultsDirectory | Out-Null
$previous = Enter-RequiredVerification -DataBarPath $corpus
try {
    Invoke-RequiredDependencyAudit -TestProject $testProject -ResultsDirectory $resultsDirectory
    Invoke-DotNetStep -Name 'Strict Debug compilation' -Arguments @('build', $testProject, '-c', 'Debug', '--no-restore', '-warnaserror')
    Invoke-VerifiedTests -Name 'Alpha regressions including required corpus and known-defect acceptance checks' -TestProject $testProject -ResultsDirectory $resultsDirectory
    Invoke-StrictReleaseBuild -EditorProject $editorProject
    if (-not (Test-Path -LiteralPath $releaseExecutable -PathType Leaf)) { throw "Release executable missing: $releaseExecutable" }
    Write-Output "Alpha verification passed. Evidence: $resultsDirectory"
    Write-Output "Release executable: $releaseExecutable"
}
finally { Exit-RequiredVerification -Previous $previous }
