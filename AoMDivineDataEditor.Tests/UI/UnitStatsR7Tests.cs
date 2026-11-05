using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void UnitR7OptionalPickerStaysAnchoredWithoutDirtyingAndStillAddsTheChosenModule()
        => WithHost(async (fixture, host) =>
        {
            await RoleSettle(host);
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var picker = OpenUnitOptionalMenu(host); await RoleSettle(host);
            picker.BringIntoView(); await RoleSettle(host); picker.Focus(); picker.IsDropDownOpen = true; await RoleSettle(host);
            Assert.True(picker.IsDropDownOpen);
            var owner = picker.GetVisualAncestors().OfType<ScrollViewer>().First();
            var offset = owner.Offset;
            var point = owner.TranslatePoint(new Point(owner.Bounds.Width - 40, 100), host)!.Value;
            host.MouseMove(point); host.MouseWheel(point, new Vector(0, 1), RawInputModifiers.None);
            await RoleSettle(host); Assert.Equal(offset, owner.Offset); Assert.True(picker.IsDropDownOpen);
            owner.Offset = new Vector(0, Math.Max(0, offset.Y - 40)); await RoleSettle(host);
            Assert.False(picker.IsDropDownOpen);
            Assert.False(Field<bool>(host, "_isDirty")); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
            picker.BringIntoView(); await RoleSettle(host); picker.Focus(); picker.IsDropDownOpen = true; await RoleSettle(host);
            Assert.True(picker.IsDropDownOpen);
            picker.SelectedItem = "Height Bob"; await RoleSettle(host);
            Assert.NotNull(StatsModule(host, "heightbobdata")); Assert.True(Field<bool>(host, "_isDirty"));
            var oldPicker = picker;
            host.Call("BuildEditorPanel", "B", true); await RoleSettle(host);
            Assert.False(oldPicker.IsDropDownOpen); Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), RoleFixture);
}
