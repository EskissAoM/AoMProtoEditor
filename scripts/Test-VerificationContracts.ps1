# Fast offline checks of release-gate decisions; no dotnet processes or game data required.
$ErrorActionPreference = 'Stop'
$previousNativeExitCode = $global:LASTEXITCODE
. (Join-Path $PSScriptRoot 'Verification.Common.ps1')
$checks = 0
function Expect-Rejection {
    param([scriptblock]$Action, [string]$Name = 'input')
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected the verification gate to reject $Name." }
    $script:checks++
}

$fixtureRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('AoM-verification-' + [Guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    $expectedProject = Join-Path $fixtureRoot 'Editor.Tests.csproj'
    $cleanReportJson = [pscustomobject]@{
        version = 1; parameters = '--vulnerable --include-transitive'
        sources = @('https://api.nuget.org/v3/index.json'); problems = @()
        projects = @([pscustomobject]@{
            path = $expectedProject; problems = @()
            frameworks = @([pscustomobject]@{
                framework = 'net10.0'; topLevelPackages = @(); transitivePackages = @()
            })
        })
    } | ConvertTo-Json -Depth 16

    # Small JSON fixture builder in the existing harness. Each case starts fresh.
    function New-DependencyReportJson {
        param([scriptblock]$Change)
        $report = $cleanReportJson | ConvertFrom-Json
        if ($null -ne $Change) { & $Change $report }
        return ($report | ConvertTo-Json -Depth 16)
    }

    foreach ($change in @(
        { param($r) },
        # Actual NuGet v1 clean shape recorded during F08: no frameworks/problems.
        { param($r) $r.PSObject.Properties.Remove('problems'); $r.projects[0].PSObject.Properties.Remove('problems'); $r.projects[0].PSObject.Properties.Remove('frameworks') },
        { param($r) $r.parameters = '--include-transitive --vulnerable' },
        { param($r) $r.projects[0].path = $r.projects[0].path.Replace('\', '/').ToUpperInvariant() },
        { param($r) $r | Add-Member -NotePropertyName futureExtension -NotePropertyValue 'retained'; $r.projects[0] | Add-Member -NotePropertyName futureProjectField -NotePropertyValue 7 },
        { param($r) $r.projects = @([pscustomobject]@{ path = (Join-Path $fixtureRoot 'ReferencedEditor.csproj') }, $r.projects[0]) },
        { param($r) $r.projects[0].frameworks[0].topLevelPackages = @([pscustomobject]@{ id = 'Example'; resolvedVersion = '1.0'; vulnerabilities = @() }) }
    )) {
        Assert-CleanDependencyReport -Json (New-DependencyReportJson $change) -ExpectedProjectPath $expectedProject
        $checks++
    }

    $negativeCases = @(
        @{ Name='missing version'; Change={ param($r) $r.PSObject.Properties.Remove('version') } },
        @{ Name='unsupported version'; Change={ param($r) $r.version = 2 } },
        @{ Name='string version'; Change={ param($r) $r.version = '1' } },
        @{ Name='missing invocation'; Change={ param($r) $r.PSObject.Properties.Remove('parameters') } },
        @{ Name='missing transitive audit'; Change={ param($r) $r.parameters = '--vulnerable' } },
        @{ Name='framework-filtered audit'; Change={ param($r) $r.parameters += ' --framework net10.0' } },
        @{ Name='duplicate invocation flag'; Change={ param($r) $r.parameters = '--vulnerable --vulnerable' } },
        @{ Name='missing sources'; Change={ param($r) $r.PSObject.Properties.Remove('sources') } },
        @{ Name='empty sources'; Change={ param($r) $r.sources = @() } },
        @{ Name='scalar sources'; Change={ param($r) $r.sources = 'https://api.nuget.org/v3/index.json' } },
        @{ Name='null source'; Change={ param($r) $r.sources = @($null) } },
        @{ Name='blank source'; Change={ param($r) $r.sources = @(' ') } },
        @{ Name='root source problem'; Change={ param($r) $r.problems = @('offline') } },
        @{ Name='malformed root problems'; Change={ param($r) $r.problems = 'offline' } },
        @{ Name='empty projects'; Change={ param($r) $r.projects = @() } },
        @{ Name='scalar projects'; Change={ param($r) $r.projects = $r.projects[0] } },
        @{ Name='null project'; Change={ param($r) $r.projects = @($null) } },
        @{ Name='missing project identity'; Change={ param($r) $r.projects[0].PSObject.Properties.Remove('path') } },
        @{ Name='blank project identity'; Change={ param($r) $r.projects[0].path = '' } },
        @{ Name='relative project identity'; Change={ param($r) $r.projects[0].path = 'Editor.Tests.csproj' } },
        @{ Name='wrong project'; Change={ param($r) $r.projects[0].path = Join-Path $fixtureRoot 'Wrong.csproj' } },
        @{ Name='duplicate project'; Change={ param($r) $r.projects = @($r.projects[0], $r.projects[0]) } },
        @{ Name='project source problem'; Change={ param($r) $r.projects[0].problems = @('offline') } },
        @{ Name='null problems'; Change={ param($r) $r.projects[0].problems = $null } },
        @{ Name='null frameworks'; Change={ param($r) $r.projects[0].frameworks = $null } },
        @{ Name='empty frameworks'; Change={ param($r) $r.projects[0].frameworks = @() } },
        @{ Name='scalar framework list'; Change={ param($r) $r.projects[0].frameworks = $r.projects[0].frameworks[0] } },
        @{ Name='empty framework object'; Change={ param($r) $r.projects[0].frameworks = @([pscustomobject]@{}) } },
        @{ Name='numeric framework'; Change={ param($r) $r.projects[0].frameworks = @(42) } },
        @{ Name='null framework'; Change={ param($r) $r.projects[0].frameworks = @($null) } },
        @{ Name='blank framework name'; Change={ param($r) $r.projects[0].frameworks[0].framework = ' ' } },
        @{ Name='framework problem'; Change={ param($r) $r.projects[0].frameworks[0] | Add-Member -NotePropertyName problems -NotePropertyValue @('offline') } },
        @{ Name='null package list'; Change={ param($r) $r.projects[0].frameworks[0].topLevelPackages = $null } },
        @{ Name='scalar package list'; Change={ param($r) $r.projects[0].frameworks[0].transitivePackages = [pscustomobject]@{ id='Example'; resolvedVersion='1' } } },
        @{ Name='numeric package'; Change={ param($r) $r.projects[0].frameworks[0].topLevelPackages = @(42) } },
        @{ Name='null package'; Change={ param($r) $r.projects[0].frameworks[0].transitivePackages = @($null) } },
        @{ Name='missing package identity'; Change={ param($r) $r.projects[0].frameworks[0].topLevelPackages = @([pscustomobject]@{ resolvedVersion='1' }) } },
        @{ Name='missing resolved version'; Change={ param($r) $r.projects[0].frameworks[0].transitivePackages = @([pscustomobject]@{ id='Example' }) } },
        @{ Name='null advisory list'; Change={ param($r) $r.projects[0].frameworks[0].topLevelPackages = @([pscustomobject]@{ id='Example'; resolvedVersion='1'; vulnerabilities=$null }) } },
        @{ Name='scalar advisory list'; Change={ param($r) $r.projects[0].frameworks[0].transitivePackages = @([pscustomobject]@{ id='Example'; resolvedVersion='1'; vulnerabilities='unknown' }) } }
    )
    foreach ($case in $negativeCases) {
        $json = New-DependencyReportJson $case.Change
        Expect-Rejection { Assert-CleanDependencyReport -Json $json -ExpectedProjectPath $expectedProject } -Name $case.Name
    }
    foreach ($json in @('{', 'null', '[]', ('[' + $cleanReportJson + ']'))) {
        Expect-Rejection { Assert-CleanDependencyReport -Json $json -ExpectedProjectPath $expectedProject } -Name 'invalid JSON root'
    }
    Expect-Rejection { Assert-CleanDependencyReport -Json $cleanReportJson } -Name 'missing expected project context'
    foreach ($list in @('topLevelPackages', 'transitivePackages')) {
        foreach ($severity in @('Low', 'Moderate', 'High', 'Critical', 'Unknown')) {
            $json = New-DependencyReportJson {
                param($r)
                $r.projects[0].frameworks[0].$list = @([pscustomobject]@{
                    id='Example'; resolvedVersion='1'; vulnerabilities=@([pscustomobject]@{ severity=$severity })
                })
            }
            Expect-Rejection { Assert-CleanDependencyReport -Json $json -ExpectedProjectPath $expectedProject } -Name "$list $severity advisory"
        }
    }
    Expect-Rejection { Resolve-VerificationCorpus -DataBarPath (Join-Path $fixtureRoot 'missing.bar') -ProjectRoot $fixtureRoot }

    $resultPath = Join-Path $fixtureRoot 'result.trx'
    foreach ($case in @(
        @{ total=1; passed=1; executed=1; outcome='Completed'; valid=$true },
        @{ total=1; passed=0; executed=0; outcome='Completed'; valid=$false },
        @{ total=1; passed=1; executed=1; outcome='Aborted'; valid=$false },
        @{ total=0; passed=0; executed=0; outcome='Completed'; valid=$false },
        @{ total=1; passed=0; executed=1; outcome='Failed'; valid=$false }
    )) {
        [IO.File]::WriteAllText($resultPath, "<TestRun><ResultSummary outcome='$($case.outcome)'><Counters total='$($case.total)' passed='$($case.passed)' executed='$($case.executed)' /></ResultSummary></TestRun>")
        if ($case.valid) { Assert-CompleteTestResult -Path $resultPath; $checks++ }
        else { Expect-Rejection { Assert-CompleteTestResult -Path $resultPath } }
    }

    $previous = Enter-RequiredVerification -DataBarPath (Join-Path $fixtureRoot 'Data.bar')
    try {
        if ($env:AOM_REQUIRE_CORPUS -ne '1' -or $env:AOM_RUN_KNOWN_FAILURES -ne '1' -or $env:UPDATE_POWER_SCHEMA_BASELINE -ne '0') { throw 'Required mode flags were not set.' }
        $checks++
    } finally { Exit-RequiredVerification -Previous $previous }
    foreach ($name in $previous.Keys) {
        if ([string][Environment]::GetEnvironmentVariable($name, 'Process') -cne [string]$previous[$name]) { throw "Environment was not restored: $name" }
    }
    $checks++

    # Local function shadows dotnet only inside this check's script scope.
    function dotnet { $global:LASTEXITCODE = 7 }
    Expect-Rejection { Invoke-DotNetStep -Name 'Expected native failure' -Arguments @('not-executed') }
    Remove-Item Function:\dotnet

    # Exercise the real audit wrapper with a local command double only. This
    # proves ordering, context forwarding and failure propagation without NuGet.
    function dotnet {
        [void]$script:mockCalls.Add(@($args))
        if ($args[0] -eq 'restore') {
            $global:LASTEXITCODE = if ($script:mockMode -eq 'restore-error') { 7 } else { 0 }
            return
        }
        $global:LASTEXITCODE = if ($script:mockMode -eq 'list-error') { 7 } else { 0 }
        return $script:mockReport
    }
    foreach ($mode in @('restore-error', 'list-error', 'wrong-project', 'clean')) {
        $script:mockMode = $mode
        $script:mockCalls = [Collections.Generic.List[object]]::new()
        $script:mockReport = if ($mode -eq 'wrong-project') {
            New-DependencyReportJson { param($r) $r.projects[0].path = Join-Path $fixtureRoot 'Wrong.csproj' }
        } else {
            New-DependencyReportJson { param($r) $r.projects[0].PSObject.Properties.Remove('frameworks') }
        }
        $auditResults = Join-Path $fixtureRoot $mode
        New-Item -ItemType Directory -Path $auditResults | Out-Null
        if ($mode -eq 'clean') {
            Invoke-RequiredDependencyAudit -TestProject $expectedProject -ResultsDirectory $auditResults
            $checks++
        } else {
            Expect-Rejection { Invoke-RequiredDependencyAudit -TestProject $expectedProject -ResultsDirectory $auditResults } -Name "$mode audit wrapper"
        }
        $expectedCalls = if ($mode -eq 'restore-error') { 1 } else { 2 }
        if ($script:mockCalls.Count -ne $expectedCalls -or $script:mockCalls[0][0] -ne 'restore' -or
            $script:mockCalls[0] -notcontains '--force-evaluate' -or
            $script:mockCalls[0] -notcontains '-p:NuGetAuditMode=all' -or
            $script:mockCalls[0] -notcontains '-p:NuGetAuditLevel=low' -or
            $script:mockCalls[0] -notcontains '-p:WarningsAsErrors=NU1900') { throw "Audit restore/order contract failed: $mode" }
        if ($expectedCalls -eq 2 -and ($script:mockCalls[1][0] -ne 'list' -or
            $script:mockCalls[1][1] -ne $expectedProject -or
            $script:mockCalls[1] -notcontains '--include-transitive' -or
            $script:mockCalls[1] -notcontains '--vulnerable' -or
            $script:mockCalls[1] -notcontains '--no-restore')) { throw "Audit list contract failed: $mode" }
        $savedReport = Join-Path $auditResults 'dependency-audit.json'
        if ($mode -in @('restore-error', 'list-error')) {
            if (Test-Path -LiteralPath $savedReport) { throw "Native failure produced success evidence: $mode" }
        } elseif (-not (Test-Path -LiteralPath $savedReport -PathType Leaf)) { throw "Audit evidence was not recorded: $mode" }
        $checks++
    }
    Remove-Item Function:\dotnet
    # Release has its own runtime graph. Restore failures must prevent compilation,
    # and compilation failures must remain failures rather than use an old EXE.
    function dotnet {
        [void]$script:releaseCalls.Add(@($args))
        $global:LASTEXITCODE = if (($args[0] -eq 'restore' -and $script:releaseMode -eq 'restore-error') -or
            ($args[0] -eq 'build' -and $script:releaseMode -eq 'build-error')) { 7 } else { 0 }
    }
    foreach ($mode in @('restore-error', 'build-error', 'clean')) {
        $script:releaseMode = $mode
        $script:releaseCalls = [Collections.Generic.List[object]]::new()
        if ($mode -eq 'clean') { Invoke-StrictReleaseBuild -EditorProject $expectedProject; $checks++ }
        else { Expect-Rejection { Invoke-StrictReleaseBuild -EditorProject $expectedProject } -Name "$mode Release build" }
        $expectedCalls = if ($mode -eq 'restore-error') { 1 } else { 2 }
        $restoreCall = $script:releaseCalls[0]
        if ($script:releaseCalls.Count -ne $expectedCalls -or $restoreCall[0] -ne 'restore' -or
            $restoreCall[1] -ne $expectedProject -or $restoreCall -notcontains '-p:Configuration=Release' -or
            $restoreCall -notcontains '--force-evaluate' -or $restoreCall -notcontains '-p:NuGetAudit=true' -or
            $restoreCall -notcontains '-p:NuGetAuditMode=all' -or $restoreCall -notcontains '-p:NuGetAuditLevel=low' -or
            $restoreCall -notcontains '-warnaserror') { throw "Release restore contract failed: $mode" }
        if ($expectedCalls -eq 2 -and (($script:releaseCalls[1] -join '|') -cne
            (@('build', $expectedProject, '-c', 'Release', '--no-restore', '-warnaserror') -join '|'))) {
            throw "Strict Release compilation contract failed: $mode"
        }
        $checks++
    }
    Remove-Item Function:\dotnet
    Write-Output "Verification contract checks passed: $checks. No builds/tests/network requests were launched."
}
finally {
    $global:LASTEXITCODE = $previousNativeExitCode
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $fixtureRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($fixtureRoot)).StartsWith('AoM-verification-')) { throw 'Refusing cleanup outside the fixture directory.' }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
