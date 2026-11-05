param([string]$OutputPath = 'F10_F3_CHECKPOINT10_SOURCE_INVENTORY.csv')

# Read-only lexical inventory, not a claim that every constructor is an input or
# that its local width is final. Reconcile ownership in F10_F3_CHECKPOINT10.md.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputPath))
if (-not $destination.StartsWith($projectRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Inventory destination must be inside the project.'
}
$files = @('Classes', 'Controls', 'Windows', 'Styles', 'Services') | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $projectRoot $_) -Recurse -File |
        Where-Object { $_.Extension -in '.cs', '.axaml' }
}
$files += Get-Item -LiteralPath (Join-Path $projectRoot 'App.axaml')
$records = foreach ($file in $files | Sort-Object FullName) {
    $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName).Replace('\', '/')
    $owner = switch -Regex ($relative) {
        'ProtoEditorWindow' { 'Unit/Actions/Ability/Tactics: explicit owner builders and finalizers'; break }
        'TechnologyEditorView' { 'Technology: explicit factories plus FinalizeTechnologyPresentation'; break }
        'PowerEditorView|PowerEditorDynamicControls' { 'Power: fixed manifest and dynamic field factories'; break }
        'MajorGodEditorView' { 'Major God: fixed schema and dynamic helpers'; break }
        '(?:ProtoUnitCommand|ProtoUnitTransform)' { 'Command/Transform: shared factory, standalone owner, or manager/dialog shell'; break }
        '(?:CultureEditorWindow|MinorGodEditorWindow)' { 'Compact editor: explicit owner roles'; break }
        'Manager|Prompt|PickerWindow|AssetDestinationWindow|AssetFolderTreeWindow|Settings|AnimFileViewer|OrphanCleanup' { 'Separate manager/dialog/XML shell policy'; break }
        default { 'Shared infrastructure, theme, or display shell: inspect context' }
    }
    $lines = [IO.File]::ReadAllLines($file.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $kinds = @()
        if ($lines[$i] -match '\b(?:Width|MinWidth|MaxWidth)\s*=') { $kinds += 'Width assignment/constraint' }
        if ($lines[$i] -match '<Setter\s+Property="(?:Width|MinWidth|MaxWidth)"') { $kinds += 'XAML width style setter' }
        if ($lines[$i] -match '\bselectAllOnFirstClick\s*:\s*false') { $kinds += 'Legacy selection opt-out' }
        if ($lines[$i] -match '\bnew\s*\(') { $kinds += 'Target-typed construction (includes non-input objects)' }
        foreach ($kind in $kinds) {
            [pscustomobject]@{
                File = $relative; Line = $i + 1; Kind = $kind; Owner = $owner
                Source = $lines[$i].Trim()
                Context = ($lines[[Math]::Max(0, $i - 2)..[Math]::Min($lines.Length - 1, $i + 3)] |
                    ForEach-Object { $_.Trim() }) -join ' '
            }
        }
    }
}
$records | Export-Csv -LiteralPath $destination -NoTypeInformation -Encoding utf8
$records | Group-Object Kind | ForEach-Object { '{0}: {1}' -f $_.Name, $_.Count }
'Production files scanned: {0}; files with inventory hits: {1}' -f $files.Count, @($records.File | Sort-Object -Unique).Count
