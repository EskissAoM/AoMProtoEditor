param(
    [string]$ApplicationDirectory = (Join-Path (Split-Path $PSScriptRoot) 'bin/Debug/net10.0')
)

# Audit only: production helpers run on synthetic, detached XML. This script
# returns JSON to stdout; it never saves mod, archive, or settings files.
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::LoadFrom((Join-Path $ApplicationDirectory 'AoMDivineDataEditor.dll')) | Out-Null

function Read-Xml([string]$Text) {
    [System.Xml.Linq.XElement]::Parse($Text, [System.Xml.Linq.LoadOptions]::PreserveWhitespace)
}
function Get-Order([System.Xml.Linq.XElement]$Element) {
    @($Element.Elements() | ForEach-Object { $_.Name.LocalName })
}
function Get-Shape([System.Xml.Linq.XElement]$Element) {
    $Element.ToString([System.Xml.Linq.SaveOptions]::DisableFormatting)
}

$binding = [Reflection.BindingFlags]'NonPublic,Static'
$insertTechnology = [AoMDivineDataEditor.Windows.TechnologyEditorView].GetMethod('InsertBeforeEffectsOrAppend', $binding)
$normalizeEffect = [AoMDivineDataEditor.Windows.TechnologyEditorView].GetMethod('NormalizeEffectAttributeOrder', $binding)
if ($null -eq $insertTechnology -or $null -eq $normalizeEffect) { throw 'Technology helper was not found.' }

$technology = Read-Xml '<tech name="Example"><displaynameid>STR_NAME</displaynameid><status>UNOBTAINABLE</status><prereqs><techstatus status="Obtained">Other</techstatus></prereqs><effects><effect type="Data" /></effects></tech>'
$technologyBefore = Get-Order $technology
$null = $insertTechnology.Invoke($null, [object[]]@($technology, (Read-Xml '<icon>example.png</icon>')))
$effect = Read-Xml '<effect delay="1" relativity="Assign" amount="2" future="keep" subtype="Hitpoints" type="Data" />'
$null = $normalizeEffect.Invoke($null, [object[]]@($effect))
$mixedEffects = Read-Xml '<tech name="Mixed"><effect type="First"/><effects><effect type="Second"/><extension/><effect type="Third"/></effects><effect type="Fourth"/></tech>'
$mixedBefore = Get-Shape $mixedEffects
$effectSequence = @([AoMDivineDataEditor.Classes.TechnologyXmlShapePolicy]::EnumerateEffectsInDocumentOrder($mixedEffects) | ForEach-Object { $_.Attribute('type').Value })

$power = Read-Xml '<power name="Example" type="Bolt"/>'
foreach ($pair in @(@('placement', 'Full'), @('activetime', '0'), @('powerplayerrelation', 'All'), @('displaynameid', 'STR_NAME'), @('rolloverid', 'STR_ROLLOVER'))) {
    $null = [AoMDivineDataEditor.Classes.PowerCatalog]::SetField($power, $pair[0], $pair[1])
}
$powerNewOrder = Get-Order $power
$null = [AoMDivineDataEditor.Classes.PowerCatalog]::SetField($power, 'icon', 'example.png')
$null = [AoMDivineDataEditor.Classes.PowerCatalog]::AddRepeatedField($power, 'targettype', 'First')
$null = [AoMDivineDataEditor.Classes.PowerCatalog]::SetField($power, 'cost', '5')
$null = [AoMDivineDataEditor.Classes.PowerCatalog]::AddRepeatedField($power, 'targettype', 'Second')
$powerAfterFields = Get-Order $power
$powerDocument = [System.Xml.Linq.XDocument]::Parse('<powers>' + (Get-Shape $power) + '</powers>')
$renames = [Collections.Generic.List[Collections.Generic.KeyValuePair[string,string]]]::new()
$updates = [Collections.Generic.Dictionary[string,string]]::new()
$null = [AoMDivineDataEditor.Classes.PowerSavePreparation]::Prepare($powerDocument, [string[]]@('Example'), $renames, $updates, [string[]]@())
$protectedEffect = Read-Xml '<effect xml:space="preserve">prefix<child/>suffix</effect>'
$protectedEffectBefore = Get-Shape $protectedEffect
[AoMDivineDataEditor.Classes.PowerCatalog]::NormalizeNestedElementIndentation($protectedEffect)

$god = Read-Xml '<civ><icon>x</icon><!-- culture note --><culture>Greek</culture><name>Example</name></civ>'
[AoMDivineDataEditor.Classes.MajorGodCatalog]::NormalizeEditorFieldOrder($god)
$comment = $god.Nodes() | Where-Object { $_ -is [System.Xml.Linq.XComment] } | Select-Object -First 1
$commentNext = $comment.NodesAfterSelf() | Where-Object { $_ -is [System.Xml.Linq.XElement] } | Select-Object -First 1
$opaqueGod = Read-Xml '<civ><icon>x</icon><extension/><name>Example</name></civ>'
[AoMDivineDataEditor.Classes.MajorGodCatalog]::NormalizeEditorFieldOrder($opaqueGod)
$protectedGod = Read-Xml '<civ xml:space="preserve"><icon>x</icon><name>Example</name></civ>'
$protectedGodBefore = Get-Shape $protectedGod
[AoMDivineDataEditor.Classes.MajorGodCatalog]::NormalizeEditorFieldOrder($protectedGod)
$specialGod = Read-Xml '<civ><shades><protounit>First</protounit></shades><startingunits><unit>First</unit><unit>Second</unit></startingunits><name>Example</name><terraincreeps/><icon>x</icon><startingunits mode="deathmatch"><unit>Third</unit></startingunits></civ>'
[AoMDivineDataEditor.Classes.MajorGodCatalog]::NormalizeEditorFieldOrder($specialGod)

$minorDocument = [System.Xml.Linq.XDocument]::Parse('<minorgodsmods/>')
$minor = [AoMDivineDataEditor.Classes.MinorGodCatalog]::AddDefinition($minorDocument, 'ClassicalAgeExample', $null)
$minorNewAttributes = @($minor.Attributes() | ForEach-Object { $_.Name.LocalName })
$template = Read-Xml '<ClassicalAgeSource future="keep" portraiticonfilename="icon.png" portraitfilename="portrait.png"><!-- keep --><extension/></ClassicalAgeSource>'
$copy = [AoMDivineDataEditor.Classes.MinorGodCatalog]::AddDefinition($minorDocument, 'HeroicAgeCopy', $template)
[AoMDivineDataEditor.Classes.MinorGodCatalog]::SetAttribute($copy, 'portraitfilename', 'changed.png')

[ordered]@{
    Scope = 'Detached helper replay, not full interactive editor coverage; no production files modified.'
    Technology = [ordered]@{
        BeforeAddingIcon = $technologyBefore
        AfterAddingIcon = Get-Order $technology
        ChangedEffectAttributeOrder = @($effect.Attributes() | ForEach-Object { $_.Name.LocalName })
        MixedEffectSequence = $effectSequence
        EffectEnumerationLeavesSourceUnchanged = ((Get-Shape $mixedEffects) -ceq $mixedBefore)
    }
    Power = [ordered]@{
        NewCreationSetterSequence = $powerNewOrder
        AfterAddingFields = $powerAfterFields
        AfterSavePreparation = Get-Order $powerDocument.Root.Element('power')
        RepeatedTargetValues = @($power.Elements('targettype') | ForEach-Object { $_.Value })
        ProtectedNestedFormattingUnchanged = ((Get-Shape $protectedEffect) -ceq $protectedEffectBefore)
        ProtectedNestedBefore = $protectedEffectBefore
        ProtectedNestedAfter = Get-Shape $protectedEffect
    }
    MajorGod = [ordered]@{
        CommentOriginallyBefore = 'culture'
        CommentNowBefore = $commentNext.Name.LocalName
        OpaqueBoundaryOrderAfter = Get-Order $opaqueGod
        XmlSpacePreserveUnchanged = ((Get-Shape $protectedGod) -ceq $protectedGodBefore)
        SpecialAndStartingOrderAfter = Get-Order $specialGod
        StartingGroupValues = @($specialGod.Elements('startingunits') | ForEach-Object { (@($_.Elements('unit') | ForEach-Object { $_.Value }) -join ',') })
    }
    MinorGod = [ordered]@{
        NewAttributeOrder = $minorNewAttributes
        DuplicateAttributeOrder = @($copy.Attributes() | ForEach-Object { $_.Name.LocalName })
        UnknownChildRetained = ($null -ne $copy.Element('extension'))
        CommentRetained = (@($copy.Nodes() | Where-Object { $_ -is [System.Xml.Linq.XComment] }).Count -eq 1)
        DocumentEntryOrder = Get-Order $minorDocument.Root
    }
} | ConvertTo-Json -Depth 8
