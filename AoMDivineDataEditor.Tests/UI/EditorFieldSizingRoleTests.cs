using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class EditorFieldSizingRoleTests
{
    [Theory]
    [InlineData(EditorFieldWidthRole.Numeric, 90)]
    [InlineData(EditorFieldWidthRole.DenseNumeric, 60)]
    [InlineData(EditorFieldWidthRole.TacticsWeight, 48)]
    [InlineData(EditorFieldWidthRole.DamageBonusMultiplier, 80)]
    [InlineData(EditorFieldWidthRole.Reference, 200)]
    [InlineData(EditorFieldWidthRole.CompactReference, 150)]
    [InlineData(EditorFieldWidthRole.WideReference, 240)]
    [InlineData(EditorFieldWidthRole.ChipSearch, 50)]
    [InlineData(EditorFieldWidthRole.ShortEnum, 100)]
    [InlineData(EditorFieldWidthRole.LongEnum, 180)]
    [InlineData(EditorFieldWidthRole.ComparisonOperator, 62)]
    [InlineData(EditorFieldWidthRole.AssetPath, 240)]
    [InlineData(EditorFieldWidthRole.CompactAssetPath, 200)]
    [InlineData(EditorFieldWidthRole.AttachmentBone, 100)]
    [InlineData(EditorFieldWidthRole.SingleLineText, 300)]
    [InlineData(EditorFieldWidthRole.ShortRollover, 300)]
    [InlineData(EditorFieldWidthRole.StringIdReference, 200)]
    public void ExplicitRolesMeasureAtTheirContractWidthWithoutEditingTextOrInferringFromLength(
        EditorFieldWidthRole role, double width)
        => AvaloniaTestHost.Run(() =>
        {
            const string longText = "AbstractCloseCombatShipWithAVeryLongCustomSuffix.0000";
            var box = new TextBox
            {
                Text = longText, Width = 500, MinWidth = 500, MaxWidth = 500,
                VerticalAlignment = VerticalAlignment.Center
            };
            var window = new Window { Width = 650, Height = 180, Content = box };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var changes = 0;
                box.PropertyChanged += (_, args) => { if (args.Property == TextBox.TextProperty) changes++; };
                EditorTextFieldStyle.ConfigureTextBox(box, role);
                // A shared setup call later in construction must not restore 380.
                EditorTextFieldStyle.ConfigureTextBox(box);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(width, box.Bounds.Width);
                Assert.Equal(30, box.Bounds.Height);
                Assert.Equal(longText, box.Text); Assert.Equal(0, changes);
                box.Text = "A";
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(width, box.Bounds.Width);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void CompactSelectorAndBonusNumericFitTheirExistingHostsWithFourPixelCrossGaps()
        => AvaloniaTestHost.Run(() =>
        {
            var selector = new AutoCompleteBox { Text = "AbstractCloseCombatShip", MinWidth = 380 };
            EditorTextFieldStyle.ConfigureSelector(selector, EditorFieldWidthRole.CompactReference);
            EditorTextFieldStyle.ConfigureSelector(selector);
            var number = new TextBox { Text = "123456789.2500" };
            EditorNumericFieldStyle.ConfigureNumericTextBox(number, EditorFieldWidthRole.DamageBonusMultiplier);
            var selectorHost = new Grid { Width = 150, Children = { selector } };
            var numberHost = new Grid { Width = 80, Children = { number } };
            var selectorCross = EditorRemoveButtons.Field(); var numberCross = EditorRemoveButtons.Row();
            var rows = new StackPanel
            {
                Children =
                {
                    EditorFormLayout.WithCross(selectorHost, selectorCross),
                    EditorFormLayout.WithCross(numberHost, numberCross)
                }
            };
            var window = new Window { Width = 300, Height = 180, Content = rows };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(150, selector.Bounds.Width); Assert.Equal(80, number.Bounds.Width);
                Assert.Equal("123456789.25", number.Text);
                foreach (var (field, cross) in new (Control Field, Button Cross)[] { (selector, selectorCross), (number, numberCross) })
                {
                    var p = field.TranslatePoint(default, window)!.Value;
                    var c = cross.TranslatePoint(default, window)!.Value;
                    Assert.InRange(c.X - p.X - field.Bounds.Width, 3.9, 4.1);
                    Assert.InRange(Math.Abs(c.Y + cross.Bounds.Height / 2 - p.Y - field.Bounds.Height / 2), 0, .5);
                }
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(EditorFieldWidthRole.Numeric, 90)]
    [InlineData(EditorFieldWidthRole.DenseNumeric, 60)]
    [InlineData(EditorFieldWidthRole.TacticsWeight, 48)]
    [InlineData(EditorFieldWidthRole.DamageBonusMultiplier, 80)]
    public void NumericRoleSurvivesSharedSetupAndRetainsExistingFormatting(EditorFieldWidthRole role, double width)
        => AvaloniaTestHost.Run(() =>
        {
            var box = new TextBox { Text = "10.2500" };
            EditorNumericFieldStyle.ConfigureNumericTextBox(box, role);
            EditorTextFieldStyle.ConfigureTextBox(box);
            Assert.Equal(width, box.Width); Assert.Equal(width, box.MinWidth); Assert.Equal(width, box.MaxWidth);
            Assert.Equal("10.25", box.Text);
        });

    [Fact]
    public void ExplicitRoleCanBeChangedWithoutKeepingAnOldMinimumOrAffectingOtherControls()
        => AvaloniaTestHost.Run(() =>
        {
            var first = new AutoCompleteBox(); var second = new AutoCompleteBox();
            EditorTextFieldStyle.ConfigureSelector(first, EditorFieldWidthRole.WideReference);
            EditorTextFieldStyle.ConfigureSelector(first, EditorFieldWidthRole.ChipSearch);
            EditorTextFieldStyle.ConfigureSelector(second);
            Assert.Equal(50, first.MinWidth); Assert.Equal(50, first.MaxWidth);
            Assert.Equal(380, second.Width);
        });

    [Fact]
    public void EnumAndAssetRolesKeepOptionsFullPathAndInnerStretchBehavior()
        => AvaloniaTestHost.Run(() =>
        {
            var options = new[] { "Buildable", "UnbuildableEnemies" };
            var combo = new ComboBox { ItemsSource = options, SelectedItem = options[1] };
            var asset = new AssetPathEditor { HorizontalAlignment = HorizontalAlignment.Left };
            const string path = "resources\\deep\\folder\\long_custom_icon.png";
            var callbacks = 0;
            asset.Configure(path, [path], _ => { callbacks++; return Task.CompletedTask; });
            EditorFormLayout.ApplyWidth(combo, EditorFieldWidthRole.LongEnum);
            EditorFormLayout.ApplyWidth(asset, EditorFieldWidthRole.AssetPath);
            var window = new Window { Width = 500, Height = 180, Content = new StackPanel { Children = { combo, asset } } };
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(180, combo.Bounds.Width); Assert.Equal(options[1], combo.SelectedItem); Assert.Same(options, combo.ItemsSource);
                Assert.Equal(240, asset.Bounds.Width); Assert.Equal(240, asset.CompactPresenter.Bounds.Width);
                Assert.Equal(path, asset.FullValue); Assert.Equal(0, callbacks);
                Assert.True(double.IsNaN(asset.Editor.Width)); Assert.True(double.IsPositiveInfinity(asset.Editor.MaxWidth));
                Assert.Equal(HorizontalAlignment.Stretch, asset.Editor.HorizontalAlignment);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void UnmigratedHelpersKeepTheirDefaultsAndLocalCompactOverrides()
        => AvaloniaTestHost.Run(() =>
        {
            var text = EditorTextFieldStyle.ConfigureTextBox(new TextBox());
            var selector = EditorTextFieldStyle.ConfigureSelector(new AutoCompleteBox());
            var numeric = EditorNumericFieldStyle.ConfigureNumericTextBox(new TextBox { Text = "2.5000" });
            var asset = new AssetPathEditor();
            Assert.Equal(380, text.Width); Assert.Equal(380, selector.Width); Assert.Equal(90, numeric.Width); Assert.Equal(380, asset.Width);
            Assert.Equal("2.5", numeric.Text);
            selector.Width = selector.MaxWidth = 200;
            numeric.Width = numeric.MaxWidth = 80;
            Assert.Equal(200, selector.Width); Assert.Equal(80, numeric.Width);
        });

    [Fact]
    public void InvalidRolesAreRejectedBeforeChangingTheControl()
        => AvaloniaTestHost.Run(() =>
        {
            var box = new TextBox { Text = "10.2500", Width = 77, MinWidth = 22, MaxWidth = 99 };
            Assert.Throws<ArgumentOutOfRangeException>(() => EditorFormLayout.ApplyWidth(box, (EditorFieldWidthRole)999));
            Assert.Throws<ArgumentException>(() => EditorNumericFieldStyle.ConfigureNumericTextBox(box, EditorFieldWidthRole.Reference));
            Assert.Equal("10.2500", box.Text); Assert.Equal(77, box.Width); Assert.Equal(22, box.MinWidth); Assert.Equal(99, box.MaxWidth);
        });
}
