param(
    [Parameter(Mandatory = $true)][string]$DataBarPath,
    [string]$UnitName = 'MilitaryCamp',
    [string]$ApplicationDirectory = (Join-Path (Split-Path $PSScriptRoot) 'bin/Debug/net10.0')
)

# Read-only audit: load the archive and exercise production helpers on detached
# XML copies. No editor window is opened and no mod/archive/settings file is saved.
$ErrorActionPreference = 'Stop'
[System.Reflection.Assembly]::LoadFrom((Join-Path $ApplicationDirectory 'AoMDivineDataEditor.dll')) | Out-Null

function Get-TagOrder([System.Xml.Linq.XElement]$Element) {
    @($Element.Elements() | ForEach-Object { $_.Name.LocalName })
}

function Get-Shape([System.Xml.Linq.XElement]$Element) {
    $Element.ToString([System.Xml.Linq.SaveOptions]::DisableFormatting)
}

$archiveStream = [System.IO.File]::OpenRead($DataBarPath)
try {
    $archive = [AoMDivineDataEditor.GameData.BarArchive]::new($archiveStream)
    $archiveError = [AoMDivineDataEditor.GameData.BarArchiveLoadError]::None
    if (!$archive.Load([ref]$archiveError)) { throw "Archive load failed: $archiveError" }
    $entry = $archive.Entries | Where-Object Name -eq 'proto.xml.XMB' | Select-Object -First 1
    if ($null -eq $entry) { throw 'proto.xml.XMB was not found.' }
    $xml = [AoMDivineDataEditor.GameData.XmbReader]::ToFormattedXml($entry.ReadDataDecompressed($archiveStream))
    $document = [System.Xml.Linq.XDocument]::Parse($xml, [System.Xml.Linq.LoadOptions]::PreserveWhitespace)
    $source = [AoMDivineDataEditor.Classes.ProtoXmlHandler]::GetUnitElement($document.Root, $UnitName)
    if ($null -eq $source) { throw "Unit not found: $UnitName" }
    $before = Get-Shape $source

    $root = [System.Xml.Linq.XElement]::Parse('<protomods/>')
    $copy = [AoMDivineDataEditor.Classes.ProtoXmlHandler]::CloneUnit($root, $source, 'OrderingAuditCopy')
    $cloneOrder = Get-TagOrder $copy

    # This is the same field-writing helper/field sequence used by
    # AssignGeneratedStringIds during Unit duplication.
    foreach ($tag in @('displaynameid', 'editornameid', 'rollovertextid', 'shortrollovertextid')) {
        [AoMDivineDataEditor.Classes.ProtoXmlHandler]::SetSimpleField($copy, $tag, "AUDIT_$tag")
    }
    $identityOrder = Get-TagOrder $copy
    $beforeStructuredCapture = [System.Xml.Linq.XElement]::new($copy)
    [AoMDivineDataEditor.Classes.ProtoUnitStatsXmlPreserver]::PreserveUnmanagedContent($beforeStructuredCapture, $copy, $null)
    [AoMDivineDataEditor.Classes.ProtoUnitXmlOrderPolicy]::NormalizeForEditor($copy)

    $newField = [System.Xml.Linq.XElement]::new($source)
    [AoMDivineDataEditor.Classes.ProtoXmlHandler]::SetSimpleField($newField, 'turnrate', '1')
    [AoMDivineDataEditor.Classes.ProtoUnitStatsXmlPreserver]::PreserveUnmanagedContent($source, $newField, $null)
    [AoMDivineDataEditor.Classes.ProtoUnitXmlOrderPolicy]::NormalizeForEditor($newField)

    $comments = [System.Xml.Linq.XElement]::Parse('<unit><protoaction><name>A</name></protoaction><!-- describes hp --><maxhitpoints>10</maxhitpoints></unit>')
    [AoMDivineDataEditor.Classes.ProtoUnitXmlOrderPolicy]::NormalizeForEditor($comments)
    $preserved = [System.Xml.Linq.XElement]::Parse('<unit><future xml:space="preserve">  <x/>  </future></unit>', [System.Xml.Linq.LoadOptions]::PreserveWhitespace)
    $preservedBefore = Get-Shape $preserved.Element('future')
    [AoMDivineDataEditor.Classes.ProtoUnitXmlOrderPolicy]::NormalizeForEditor($preserved)

    [pscustomobject]@{
        Scope = 'Archive read + in-memory production-helper replay; not a full UI duplication/save replay'
        Unit = $UnitName
        ArchiveEntry = $entry.RelativePath
        SourceTags = @(Get-TagOrder $source)
        CloneTags = @($cloneOrder)
        CloneKeepsOrder = (($cloneOrder -join '|') -ceq ((Get-TagOrder $source) -join '|'))
        AfterGeneratedIdentityTags = @($identityOrder)
        AfterPlacementRepairAndOuterPolicyTags = @(Get-TagOrder $copy)
        AfterAddingTurnRateTags = @(Get-TagOrder $newField)
        SourceTreeUnchanged = ($before -ceq (Get-Shape $source))
        CommentAssociationProbe = (Get-Shape $comments)
        XmlSpacePreserveUnchanged = ($preservedBefore -ceq (Get-Shape $preserved.Element('future')))
        XmlSpaceBefore = $preservedBefore
        XmlSpaceAfter = (Get-Shape $preserved.Element('future'))
    }
}
finally { $archiveStream.Dispose() }
