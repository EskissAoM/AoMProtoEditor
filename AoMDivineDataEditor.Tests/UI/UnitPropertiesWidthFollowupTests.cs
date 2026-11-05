using System.Collections;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class UnitXmlEditingTests
{
    [Fact]
    public void UnitPhysicsAndMovementUseAvailableWidthWithoutExpandingIdentityOrDirtyingData()
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            var fields = Field<Dictionary<string, Control>>(host, "_fieldControls");
            foreach (var width in new[] { 1900d, 1000d, 1900d })
            {
                host.Width = width; await RoleSettle(host);
                IdentityPreviewAssertions.Compact((TextBox)fields["rollovertextid"]);
                foreach (var keys in new[]
                         {
                             new[] { "obstructionradiusx", "obstructionradiusz", "turnrate" },
                             new[] { "movementtype", "maxvelocity", "maxrunvelocity" }
                         })
                {
                    var controls = keys.Select(key => fields[key]).ToArray();
                    Assert.All(controls, control => Assert.Empty(control.GetLogicalAncestors().OfType<IdentityFieldPanel>()));
                    var wrap = Assert.IsType<WrapPanel>(controls[0].Parent!.Parent);
                    FormLayoutAssertions.NoOverlaps(wrap, keys[0]);
                    if (width == 1900)
                    {
                        var y = controls[0].TranslatePoint(default, host)!.Value.Y;
                        Assert.All(controls, control => Assert.InRange(Math.Abs(control.TranslatePoint(default, host)!.Value.Y - y), 0, .5));
                    }
                    foreach (var group in wrap.Children.Where(child => child.IsVisible))
                        Assert.True(group.Bounds.Right <= wrap.Bounds.Width + .5, $"{keys[0]}: {group.Bounds} outside {wrap.Bounds}");
                }
            }
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument(), RoleFixture);

    [Theory]
    [InlineData(2, "_unitCommandRows")]
    [InlineData(4, "_trainCommandRows")]
    [InlineData(4, "_techCommandRows")]
    public void UnitAssignmentRowsHaveOneEightPixelGapAndKeepHeadersAndValues(int tab, string rowsField)
        => WithHost(async (fixture, host) =>
        {
            var before = File.ReadAllBytes(fixture.ProtoPath);
            Field<TabControl>(host, "_editorTabs").SelectedIndex = tab;
            host.Width = 1500; await RoleSettle(host);
            var rows = Field<IList>(host, rowsField).Cast<object>().ToArray();
            Assert.Equal(3, rows.Length);
            var inputs = rows.Select(row => Assignment<Control>(row, "RowEditor")).ToArray();
            for (var i = 1; i < inputs.Length; i++)
            {
                var previousBottom = inputs[i - 1].TranslatePoint(default, host)!.Value.Y + inputs[i - 1].Bounds.Height;
                var nextTop = inputs[i].TranslatePoint(default, host)!.Value.Y;
                Assert.InRange(nextTop - previousBottom, 7.9, 8.1);
            }
            for (var i = 0; i < rows.Length; i++)
            {
                var row = Assignment<WrapPanel>(rows[i], "RowPanel");
                var headers = row.GetLogicalDescendants().OfType<TextBlock>().Where(label => label.Classes.Contains("assignment-column-header")).ToArray();
                Assert.Equal(3, headers.Length);
                Assert.All(headers, header => Assert.Equal(i == 0, header.IsVisible));
            }
            Assert.False(Field<bool>(host, "_isDirty"));
            Assert.Equal(before, File.ReadAllBytes(fixture.ProtoPath));
        }, RoleDocument("<command row='1' column='1'>Eject</command><command row='2' column='1'>TestCommand</command><train row='1' column='1'>B</train><train row='2' column='1'>A</train><tech row='1' column='1'>OtherTech</tech><tech row='2' column='1'>ThirdTech</tech>"), RoleFixture);
}
