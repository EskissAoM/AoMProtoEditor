# Shared release mechanics. Dot-sourcing this file does not execute any verification.
function Invoke-DotNetStep {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string[]]$Arguments)
    Write-Output "`n=== $Name ==="
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE. Remaining steps were not verified." }
}

function Resolve-VerificationCorpus {
    param([string]$DataBarPath, [Parameter(Mandatory)][string]$ProjectRoot)
    if ([string]::IsNullOrWhiteSpace($DataBarPath)) { $DataBarPath = $env:AOM_DATA_BAR }
    if ([string]::IsNullOrWhiteSpace($DataBarPath)) {
        $DataBarPath = Join-Path (Split-Path -Parent $ProjectRoot) 'Age of Mythology Retold\game\data\Data.bar'
    }
    if (-not (Test-Path -LiteralPath $DataBarPath -PathType Leaf)) {
        throw "Required original corpus missing at '$DataBarPath'. Pass -DataBarPath or set AOM_DATA_BAR. No release verification was completed."
    }
    return (Resolve-Path -LiteralPath $DataBarPath).Path
}

function Enter-RequiredVerification {
    param([Parameter(Mandatory)][string]$DataBarPath)
    $previous = @{}
    foreach ($name in @('AOM_DATA_BAR', 'AOM_REQUIRE_CORPUS', 'AOM_RUN_KNOWN_FAILURES', 'UPDATE_POWER_SCHEMA_BASELINE')) {
        $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    $env:AOM_DATA_BAR = $DataBarPath
    $env:AOM_REQUIRE_CORPUS = '1'
    $env:AOM_RUN_KNOWN_FAILURES = '1'
    $env:UPDATE_POWER_SCHEMA_BASELINE = '0'
    return $previous
}

function Exit-RequiredVerification {
    param([Parameter(Mandatory)][hashtable]$Previous)
    foreach ($name in $Previous.Keys) { [Environment]::SetEnvironmentVariable($name, $Previous[$name], 'Process') }
}

function Assert-CleanDependencyReport {
    param([Parameter(Mandatory)][string]$Json, [string]$ExpectedProjectPath)
    # NuGet v1 omits frameworks when the requested vulnerable-package list is
    # empty. Bind that success shape to the known invocation/project instead of
    # interpreting any object with no packages as a completed audit.
    if ([string]::IsNullOrWhiteSpace($ExpectedProjectPath)) { throw 'Dependency audit requires the expected project path.' }
    $expectedPath = [IO.Path]::GetFullPath($ExpectedProjectPath)
    if (-not $Json.TrimStart().StartsWith('{')) { throw 'Dependency audit must return a JSON object.' }
    $report = $Json | ConvertFrom-Json -ErrorAction Stop

    function Assert-NoAuditProblems {
        param($Object, [string]$Location)
        $problems = $Object.PSObject.Properties['problems']
        if ($null -ne $problems -and ($problems.Value -isnot [array] -or $problems.Value.Count -ne 0)) {
            throw "Dependency audit reported errors or malformed problems at $Location."
        }
    }

    if ($report -isnot [pscustomobject] -or
        ($report.version -isnot [int] -and $report.version -isnot [long]) -or $report.version -ne 1) {
        throw 'Dependency audit returned an unsupported or missing report version.'
    }
    Assert-NoAuditProblems $report 'report'
    if ($report.parameters -isnot [string] -or [string]::IsNullOrWhiteSpace($report.parameters)) {
        throw 'Dependency audit did not identify its requested options.'
    }
    $options = @($report.parameters.Trim() -split '\s+')
    if ($options.Count -ne 2 -or $options -cnotcontains '--vulnerable' -or $options -cnotcontains '--include-transitive') {
        throw 'Dependency audit must cover vulnerable direct and transitive packages without scope filters.'
    }
    if ($report.sources -isnot [array] -or $report.sources.Count -eq 0) {
        throw 'Dependency audit returned no source list.'
    }
    foreach ($source in $report.sources) {
        if ($source -isnot [string] -or [string]::IsNullOrWhiteSpace($source)) { throw 'Dependency audit returned an invalid source.' }
    }
    if ($report.projects -isnot [array] -or $report.projects.Count -eq 0) { throw 'Dependency audit returned no project list.' }
    $seenProjects = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $foundExpected = $false
    $vulnerable = @()
    foreach ($project in $report.projects) {
        if ($project -isnot [pscustomobject] -or $project.path -isnot [string] -or [string]::IsNullOrWhiteSpace($project.path) -or
            -not [IO.Path]::IsPathRooted($project.path)) { throw 'Dependency audit returned a missing or invalid project identity.' }
        $projectPath = [IO.Path]::GetFullPath($project.path)
        if (-not $seenProjects.Add($projectPath)) { throw 'Dependency audit returned duplicate project identities.' }
        if ($projectPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) { $foundExpected = $true }
        Assert-NoAuditProblems $project $project.path
        $frameworks = $project.PSObject.Properties['frameworks']
        if ($null -eq $frameworks) { continue } # Supported v1 clean-report shape.
        if ($frameworks.Value -isnot [array] -or $frameworks.Value.Count -eq 0) { throw 'Dependency audit returned an invalid framework list.' }
        foreach ($framework in $frameworks.Value) {
            if ($framework -isnot [pscustomobject] -or $framework.framework -isnot [string] -or
                [string]::IsNullOrWhiteSpace($framework.framework)) { throw 'Dependency audit returned an invalid framework entry.' }
            Assert-NoAuditProblems $framework $framework.framework
            foreach ($name in @('topLevelPackages', 'transitivePackages')) {
                $packages = $framework.PSObject.Properties[$name]
                if ($null -eq $packages) { continue }
                if ($packages.Value -isnot [array]) { throw "Dependency audit returned an invalid $name list." }
                foreach ($package in $packages.Value) {
                    if ($package -isnot [pscustomobject] -or $package.id -isnot [string] -or [string]::IsNullOrWhiteSpace($package.id) -or
                        $package.resolvedVersion -isnot [string] -or [string]::IsNullOrWhiteSpace($package.resolvedVersion)) {
                        throw 'Dependency audit returned an incomplete package entry.'
                    }
                    Assert-NoAuditProblems $package $package.id
                    $advisories = $package.PSObject.Properties['vulnerabilities']
                    if ($null -eq $advisories) { continue }
                    if ($advisories.Value -isnot [array]) { throw 'Dependency audit returned invalid vulnerability data.' }
                    if ($advisories.Value.Count -gt 0) { $vulnerable += "$($package.id) $($package.resolvedVersion)" }
                }
            }
        }
    }
    if (-not $foundExpected) { throw "Dependency audit did not cover expected project '$expectedPath'." }
    if ($vulnerable.Count -gt 0) { throw ('Dependency advisories block release: ' + (($vulnerable | Sort-Object -Unique) -join ', ')) }
}

function Invoke-RequiredDependencyAudit {
    param([Parameter(Mandatory)][string]$TestProject,
          [Parameter(Mandatory)][string]$ResultsDirectory)
    # Refresh audit metadata; unavailable audit sources must not produce stale green results.
    Invoke-DotNetStep -Name 'Restore and refresh dependency audit metadata' -Arguments @(
        'restore', $TestProject, '--force-evaluate', '-p:NuGetAudit=true', '-p:NuGetAuditMode=all',
        '-p:NuGetAuditLevel=low', '-p:WarningsAsErrors=NU1900')
    # The test project's transitive graph includes the referenced editor plus test-only dependencies.
    $json = & dotnet list $TestProject package --vulnerable --include-transitive --format json --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Online dependency audit failed with exit code $LASTEXITCODE." }
    $jsonText = $json -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $ResultsDirectory 'dependency-audit.json'), $jsonText)
    Assert-CleanDependencyReport -Json $jsonText -ExpectedProjectPath $TestProject
}

function Invoke-StrictReleaseBuild {
    param([Parameter(Mandatory)][string]$EditorProject)
    # Debug/test restore does not prepare the Release-only runtime identifier.
    # Refresh that graph explicitly before the no-restore compilation.
    Invoke-DotNetStep -Name 'Restore Release runtime assets and audit metadata' -Arguments @(
        'restore', $EditorProject, '-p:Configuration=Release', '--force-evaluate',
        '-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low',
        '-warnaserror')
    Invoke-DotNetStep -Name 'Strict Release compilation' -Arguments @(
        'build', $EditorProject, '-c', 'Release', '--no-restore', '-warnaserror')
}

function Assert-CompleteTestResult {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing test result: $Path" }
    [xml]$result = Get-Content -LiteralPath $Path -Raw
    $counters = $result.SelectSingleNode('//*[local-name()="Counters"]')
    $summary = $result.SelectSingleNode('//*[local-name()="ResultSummary"]')
    if ($null -eq $counters -or [int]$counters.total -le 0 -or
        [int]$counters.passed -ne [int]$counters.total -or
        [int]$counters.executed -ne [int]$counters.total -or $summary.outcome -ne 'Completed') {
        throw "Incomplete, skipped, failed, aborted or empty test run: $Path"
    }
}

function Invoke-VerifiedTests {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$TestProject,
          [Parameter(Mandatory)][string]$ResultsDirectory, [string]$Filter)
    $fileName = 'tests-' + [Guid]::NewGuid().ToString('N') + '.trx'
    $arguments = @('test', $TestProject, '-c', 'Debug', '--no-build', '--no-restore',
        '--results-directory', $ResultsDirectory, '--logger', "trx;LogFileName=$fileName")
    if (-not [string]::IsNullOrWhiteSpace($Filter)) { $arguments += @('--filter', $Filter) }
    Invoke-DotNetStep -Name $Name -Arguments $arguments
    Assert-CompleteTestResult -Path (Join-Path $ResultsDirectory $fileName)
}
