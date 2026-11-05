using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorDisplayTextTests
{
    [Theory]
    [InlineData("displaynameid", "Display Name")]
    [InlineData("rollovertextid", "Rollover")]
    [InlineData("shortrollovertextid", "Short Rollover")]
    public void UnitCaptionsUseTheCommonVocabularyWithoutChangingTags(string tag, string caption)
        => Assert.Equal(caption, ProtoConstants.SimpleFields.Single(field => field.Tag == tag).Label);

    [Theory]
    [InlineData("displaynameid", "Display Name")]
    [InlineData("rollovertextid", "Rollover")]
    [InlineData("advancedrollovertextoverrideid", "Advanced Rollover")]
    public void TechnologyUsesTheSameIdentityCaptions(string tag, string caption)
        => Assert.Equal(caption, Windows.TechnologyEditorView.HumanizeLabel(tag));

    [Theory]
    [InlineData(0, 0, 0, "0 items: 0 original, 0 custom. Rename hint.")]
    [InlineData(1, 0, 1, "1 item: 0 original, 1 custom. Rename hint.")]
    [InlineData(5, 2, 3, "5 items: 2 original, 3 custom. Rename hint.")]
    public void SharedManagerCountsKeepTheHintAndUseCustomAsAnAdjective(int total, int original, int custom, string expected)
        => Assert.Equal(expected, Controls.ManagerListShell.FormatEntityCountFooter(total, original, custom, "Rename hint."));
}

public sealed partial class PowerFieldPresentationTests
{
    [Theory]
    [InlineData("Shockwave", "<stunlength>1</stunlength>", "_shockwaveTopRow", "Stun length (s)", false)]
    [InlineData("Shockwave", "<stunlength>1</stunlength>", "_shockwaveTopRow", "Stun length (s)", true)]
    [InlineData("Stampede", "<fulllength/>", "_stampedeFullLengthRow", "Full length", false)]
    [InlineData("Stampede", "<fulllength/>", "_stampedeFullLengthRow", "Full length", true)]
    [InlineData("ForestFire", "<burnlength>1</burnlength><burnfadelength>2</burnfadelength>", "_forestFireBurnRow", "Fade Length", false)]
    [InlineData("ForestFire", "<burnlength>1</burnlength><burnfadelength>2</burnfadelength>", "_forestFireBurnRow", "Fade Length", true)]
    public void CorrectedPowerCaptionsRemainVisibleAndDoNotDirtyRecords(string type, string body, string rowName, string caption, bool original)
        => WithPower(type, body, original, async (view, window, path) =>
        {
            var before = File.ReadAllBytes(path);
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            var row = Field<Grid>(view, rowName);
            Assert.True(row.IsEffectivelyVisible);
            var labels = row.GetLogicalDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible).Select(label => label.Text).ToArray();
            Assert.Contains(caption, labels);
            Assert.DoesNotContain(labels, label => label?.Contains("lenght", StringComparison.OrdinalIgnoreCase) == true);
            if (type == "ForestFire") Assert.Contains("Length", labels);
            Assert.False(view.IsDirty); Assert.Equal(before, File.ReadAllBytes(path));
        });

    [Fact]
    public void CorrectedPowerPickerCaptionSearchesAndSavesThroughTheLegacyKey()
        => WithPower("Bolt", "<future keep='yes'/>", false, async (view, window, path) =>
        {
            Field<TabControl>(view, "_powerTabs").SelectedIndex = 1; await Settle(window);
            const string legacy = "Stun lenght";
            Assert.Contains(legacy, PowerSchemaRegistry.GetSpecificGroups("Shockwave"));
            Field<Button>(view, "_otherSpecificAttributesButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = Field<AutoCompleteBox>(view, "_otherSpecificAttributePicker");
            Assert.Contains(legacy, picker.ItemsSource!.Cast<string>());
            Assert.Equal("Stun length", Assert.IsType<TextBlock>(picker.ItemTemplate!.Build(legacy)).Text);
            Assert.Equal("Stun length", picker.ItemSelector!("", legacy));
            Assert.True(picker.ItemFilter!("length", legacy));
            Assert.True(picker.ItemFilter!("lenght", legacy));
            Assert.Equal("Shockwave VFX", picker.ItemSelector!("", "Shockwave VFX"));
            picker.SelectedItem = legacy;
            await Settle(window);
            Assert.True(Field<TextBox>(view, "_stunLengthBox").IsEffectivelyVisible);
            Assert.Equal("Stun length (s)", Field<TextBlock>(view, "_stunLengthLabel").Text);
            Assert.Equal(legacy, Field<Button>(view, "_removeStunLengthButton").Tag);
            Field<TextBox>(view, "_stunLengthBox").Text = "2";
            await Settle(window);
            Assert.True(view.IsDirty, "Editing Stun length must mark the Power dirty.");
            Assert.True(await view.SaveAsync(), "The corrected caption must still dispatch Save through the existing schema key.");
            var power = XDocument.Load(path).Descendants("power").Single(element => (string?)element.Attribute("name") == "A");
            Assert.Equal("2", power.Element("stunlength")?.Value);
            Assert.Equal("yes", (string?)power.Element("future")?.Attribute("keep"));
            Assert.False(view.IsDirty);
        });
}
