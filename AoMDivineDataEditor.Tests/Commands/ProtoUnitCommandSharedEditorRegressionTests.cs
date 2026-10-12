using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitCommandSharedEditorRegressionTests
{
    [Fact]
    public void TransformEditors_StandaloneAndInlineUseTheSameSharedControl()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));
        var shared = File.ReadAllText(Path.Combine(root, "Controls", "ProtoUnitCommandTransformEditor.cs"));

        Assert.Contains("new ProtoUnitCommandTransformEditor(", standalone, StringComparison.Ordinal);
        Assert.Contains("new ProtoUnitCommandTransformEditor(", inline, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"115,240,Auto,240,*\")", shared, StringComparison.Ordinal);
        Assert.Contains("\"Transform\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"To\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Full heal\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Revert others to\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Prereq Tech\"", shared, StringComparison.Ordinal);
        Assert.Contains("\"Associated Tech\"", shared, StringComparison.Ordinal);
        Assert.Contains("public Grid AssociatedTechRow { get; }", shared, StringComparison.Ordinal);
        Assert.Contains("Spacing = 8", shared, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"115,50,*\")", shared, StringComparison.Ordinal);
        Assert.Contains("Width = 50", shared, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(_chipRows, editable ? 2 : 1)", shared, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(_chipRows, editable ? 1 : 2)", shared, StringComparison.Ordinal);
        Assert.Contains("\"forbidtech\"", shared, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"associatedpower\"", shared, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransformFlags_KeepSelectorCompactAndGiveReadOnlyChipsTheRemainingRow()
    {
        AvaloniaTestHost.Run(() =>
        {
            var flags = new HashSet<string>(["displayontarget", "transformselected", "unitcommand"], StringComparer.OrdinalIgnoreCase);
            var readOnly = new ProtoUnitCommandFlagsEditor(flags, false, () => null, () => { });
            var label = readOnly.Children.OfType<TextBlock>().Single();
            var readOnlyChips = readOnly.Children.OfType<WrapPanel>().Single();

            Assert.Equal(VerticalAlignment.Top, label.VerticalAlignment);
            Assert.Equal(7, label.Margin.Top);
            Assert.Equal(1, Grid.GetColumn(readOnlyChips));
            Assert.Equal(2, Grid.GetColumnSpan(readOnlyChips));
            Assert.Equal(3, readOnlyChips.Children.Count);

            var editable = new ProtoUnitCommandFlagsEditor(flags, true, () => null, () => { });
            var picker = editable.Children.OfType<AutoCompleteBox>().Single();
            var editableChips = editable.Children.OfType<WrapPanel>().Single();

            Assert.Equal(50, picker.Width);
            Assert.Equal(50, picker.MinWidth);
            Assert.Equal(50, picker.MaxWidth);
            Assert.Equal(2, Grid.GetColumn(editableChips));
        });
    }

    [Fact]
    public void TransformEditor_AttachingAnExistingDefinitionDoesNotReportAChange()
    {
        AvaloniaTestHost.Run(() =>
        {
            var changes = 0;
            var transform = new ProtoUnitTransformDefinition
            {
                From = "CalpulliDuplicated",
                To = "CalpulliCraftWorkshop",
                Tech = "ArchaicAgeAztec",
                Command = "CalpulliDuplicatedToCalpulliCraftWorkshop",
                RevertOthersTo = "Calpulli"
            };
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["associatedtech"] = "CalpulliDuplicatedToCalpulliCraftWorkshop"
            };
            var editor = new ProtoUnitCommandTransformEditor(
                transform,
                values,
                ["Calpulli", "CalpulliDuplicated", "CalpulliCraftWorkshop"],
                ["ArchaicAgeAztec", "CalpulliDuplicatedToCalpulliCraftWorkshop"],
                [],
                editable: true,
                lockFrom: true,
                changed: () => changes++);
            var window = new Window { Content = editor };

            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, changes);
            window.Close();
        });
    }

    [Fact]
    public void InlineTransformEconomy_UsesCompactDisplayValuesAndGoldSelection()
    {
        var root = ProjectSource.RootPath;
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("sharedTransformEditor.AssociatedTechRow.Children.Add(durationLabel)", inline, StringComparison.Ordinal);
        Assert.Contains("EditorNumericFieldStyle.FormatDisplay(economy.ResearchPoints)", inline, StringComparison.Ordinal);
        Assert.Contains("EditorNumericFieldStyle.FormatDisplay(economy.Costs.GetValueOrDefault(resource, \"0\"))", inline, StringComparison.Ordinal);
        Assert.Contains("Brush.Parse(\"#393125\") : Brush.Parse(\"#252526\")", inline, StringComparison.Ordinal);
        Assert.DoesNotContain("This associated technology is from Data.bar", inline, StringComparison.Ordinal);
        Assert.DoesNotContain("Brush.Parse(\"#28466f\")", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineTransformTabNavigation_SuppressesDelayedInitializationChanges()
    {
        var inline = File.ReadAllText(Path.Combine(ProjectSource.RootPath, "Windows", "ProtoEditorWindow.axaml.cs"));
        var dirtyHelperStart = inline.IndexOf("void MarkTransformDefinitionDirty", StringComparison.Ordinal);
        var renderStart = inline.IndexOf("async Task RenderTransformAssignmentAsync", dirtyHelperStart, StringComparison.Ordinal);
        var dirtyHelper = inline[dirtyHelperStart..renderStart];
        var tabsStart = inline.IndexOf("void RenderTransformTabs()", renderStart, StringComparison.Ordinal);
        var addAreaStart = inline.IndexOf("void ShowTransformTypePicker()", tabsStart, StringComparison.Ordinal);
        var tabs = inline[tabsStart..addAreaStart];

        Assert.Contains("_isReadOnly || TransformEditorBusy()", dirtyHelper, StringComparison.Ordinal);
        Assert.Contains("!ReferenceEquals(state, activeTransformState)", tabs, StringComparison.Ordinal);
        Assert.Contains("SuppressEditorChangeTrackingWhileControlsSettle();", tabs, StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_NoLongerOwnsDuplicateTransformRowBuilders()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));

        Assert.DoesNotContain("BuildTransformDefinitionRows", standalone, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildTransformTechRows", standalone, StringComparison.Ordinal);
    }


    [Fact]
    public void TransformFlags_UseSharedEditorInBothHosts()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("new ProtoUnitCommandFlagsEditor(", standalone, StringComparison.Ordinal);
        Assert.Contains("new ProtoUnitCommandFlagsEditor(", inline, StringComparison.Ordinal);
        Assert.DoesNotContain("RequiredTransformFlag(string flag)", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void TransformValidation_IsDelegatedToSharedEditorInBothHosts()
    {
        var root = ProjectSource.RootPath;
        var standalone = File.ReadAllText(Path.Combine(root, "Windows", "ProtoUnitCommandEditorWindow.cs"));
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("_sharedTransformEditor?.ValidateRequired()", standalone, StringComparison.Ordinal);
        Assert.Contains("sharedTransformEditor.ValidateRequired(currentTransformUnitName)", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void AllCommandPositions_UseStrictNonNegativeIntegerFields()
    {
        var root = ProjectSource.RootPath;
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("EditorNumericInputBehavior.AttachRule(textBox, ProtoUnitNumericKind.UnsignedInteger)", inline, StringComparison.Ordinal);
        Assert.Contains("_trainCommandRows, useNumericPositionFields: true", inline, StringComparison.Ordinal);
        Assert.Contains("_techCommandRows, useNumericPositionFields: true", inline, StringComparison.Ordinal);
        Assert.Contains("regularCommandNames, _unitCommandRows, useNumericPositionFields: true);", inline, StringComparison.Ordinal);
        Assert.Contains("regularCommandNames, _optionalCommandRows, useNumericPositionFields: true);", inline, StringComparison.Ordinal);
    }

    [Fact]
    public void InlineTransformRemoval_IsPendingUntilTheMainDocumentSave()
    {
        var root = ProjectSource.RootPath;
        var inline = File.ReadAllText(Path.Combine(root, "Windows", "ProtoEditorWindow.axaml.cs"));

        Assert.Contains("PreflightTransformCommandRemovalAsync", inline, StringComparison.Ordinal);
        Assert.Contains("Transform removal pending. Save to apply or discard to restore it.", inline, StringComparison.Ordinal);
        Assert.Contains("SavePendingOwnedTransformBundleDeletions", inline, StringComparison.Ordinal);
        Assert.Contains("_pendingOwnedTransformBundleDeletions.Remove(currentName);", inline, StringComparison.Ordinal);
        Assert.DoesNotContain("PersistTransformAssignmentsNowAsync", inline, StringComparison.Ordinal);
        Assert.Contains("var approvedStringIds = deletedStringIds;", inline, StringComparison.Ordinal);
    }

}
