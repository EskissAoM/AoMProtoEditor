[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $projectRoot 'AoMDivineDataEditor.Tests\AoMDivineDataEditor.Tests.csproj'
$editorProject = Join-Path $projectRoot 'AoMDivineDataEditor.csproj'
$releaseExecutable = Join-Path $projectRoot 'bin\Release\net10.0\win-x64\AoMDivineDataEditor.exe'

& dotnet test $testProject --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "The alpha regression suite failed with exit code $LASTEXITCODE."
}

& dotnet build $editorProject -c Release --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "The alpha Release build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $releaseExecutable -PathType Leaf)) {
    throw "The Release executable was not produced at '$releaseExecutable'."
}

$executable = Get-Item -LiteralPath $releaseExecutable
Write-Output "Alpha verification passed."
Write-Output "Release executable: $($executable.FullName)"
Write-Output "Release executable size: $($executable.Length) bytes"
