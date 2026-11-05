using AoMDivineDataEditor.Classes;
using AoMDivineDataEditor.Controls;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ValidationNavigationTests
{
    [Theory]
    [InlineData("text")] [InlineData("catalog")] [InlineData("enum")]
    public void NativeReviewFailureFrameSurvivesPopupDismissalAndFocus(string kind)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            Control field = kind switch
            {
                "catalog" => new AutoCompleteBox { Text = "Missing", ItemsSource = new[] { "A", "B" } },
                "enum" => new ComboBox { ItemsSource = new[] { "A", "B" }, SelectedIndex = 0 },
                _ => new TextBox { Text = "Invalid" }
            };
            field.Width = 200;
            var window = new Window { Content = field, Width = 400, Height = 200 };
            try
            {
                window.Show(); await Settle(window);
                var failure = EditorValidationNavigation.Mark(window, field);
                var popup = new Window { Width = 100, Height = 100 };
                var showing = popup.ShowDialog(window);
                await Settle(window); popup.Close(); await showing;
                await failure.RevealAsync(); await Settle(window);
                Assert.Contains("invalid-field", field.Classes);
                var frames = field.GetVisualDescendants().OfType<Border>()
                    .Where(border => border.IsEffectivelyVisible && border.Bounds.Width >= 190 && border.Bounds.Height >= 20).ToArray();
                Assert.NotEmpty(frames);
                Assert.True(frames.Any(border => border.BorderThickness.Left >= 2 &&
                    border.BorderBrush is ISolidColorBrush brush && brush.Color == Color.Parse("#B55640")),
                    string.Join("; ", frames.Select(border => $"{border.Name}: {border.BorderBrush}, {border.BorderThickness}")));
                field.Focus(); await Settle(window);
                Assert.Contains("invalid-field", field.Classes);
                if (field is AutoCompleteBox auto)
                {
                    // Catalog selection synchronization without an edit is not a correction.
                    auto.SelectedItem = "Missing";
                    Assert.Contains("invalid-field", field.Classes);
                    auto.Text = "A";
                }
                else if (field is TextBox text) text.Text = "A";
                else ((ComboBox)field).SelectedIndex = 1;
                Assert.DoesNotContain("invalid-field", field.Classes);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void RepeatedAttributeChipGroupsDoNotClaimEachOthersErrors()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var source = XDocument.Parse("<powersmod><power name='A'><onhiteffect><target attacktype='Unit'/><target ignoretype='Missing'/></onhiteffect></power></powersmod>");
            var effect = source.Descendants("onhiteffect").Single();
            var attack = EditorValidationBindings.Bind(new AutoCompleteBox(), effect, "target/@attacktype", repeated: true);
            var ignore = EditorValidationBindings.Bind(new AutoCompleteBox(), effect, "target/@ignoretype", repeated: true);
            var body = new StackPanel { Children = { attack, ignore } };
            var window = new Window { Content = body, Width = 400, Height = 200 };
            try
            {
                window.Show(); await Settle(window);
                var target = effect.Elements("target").Last();
                Assert.Same(ignore, EditorValidationBindings.Find(window, new LocatedValidationIssue("source", ValidationSourceLocation.From(target, "@ignoretype"))));
                Assert.Null(EditorValidationBindings.Find(window, new LocatedValidationIssue("shape", ValidationSourceLocation.From(target, "@type"))));
                EditorValidationNavigation.Mark(window, ignore);
                effect.Elements("target").First().SetAttributeValue("attacktype", "Other");
                Assert.Contains("invalid-field", ignore.Classes);
                target.Remove();
                Assert.DoesNotContain("invalid-field", ignore.Classes);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void RemovingAnInvalidChipClearsOnlyItsBoundListAndUnsubscribesOnDiscard()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var source = XElement.Parse("<tech name='A'><Tech>Missing</Tech><Other>Keep</Other></tech>");
            var picker = EditorValidationBindings.Bind(new AutoCompleteBox(), source, "Tech", repeated: true);
            var other = new TextBox();
            var window = new Window { Content = new StackPanel { Children = { picker, other } }, Width = 400, Height = 200 };
            try
            {
                window.Show(); await Settle(window);
                var failure = EditorValidationNavigation.Mark(window, picker);
                source.Element("Other")!.Value = "Changed";
                Assert.Contains("invalid-field", picker.Classes);
                source.Element("Tech")!.Remove();
                Assert.DoesNotContain("invalid-field", picker.Classes);
                other.Focus();
                await failure.RevealAsync(); Assert.False(picker.IsKeyboardFocusWithin);
                EditorValidationNavigation.Mark(window, picker);
                EditorValidationNavigation.Clear(window);
                source.Add(new XElement("Tech", "Valid"));
                Assert.DoesNotContain("invalid-field", picker.Classes);
            }
            finally { window.Close(); }
        });
    private static async Task Settle(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(); window.UpdateLayout();
        await Task.Delay(25);
    }

    [Theory]
    [InlineData("text")] [InlineData("catalog")] [InlineData("enum")] [InlineData("asset")]
    public void FailureRevealsTheTabAndScrolledFieldThenClearsOnEdit(string kind)
        => AvaloniaTestHost.RunAsync(async () =>
        {
            Control field = kind switch
            {
                "catalog" => new AutoCompleteBox { Text = "Unknown" },
                "enum" => new ComboBox { ItemsSource = new[] { "A", "B" }, SelectedIndex = 0 },
                "asset" => new AssetPathEditor(),
                _ => new TextBox { Text = "Invalid" }
            };
            EditorFormLayout.ApplyWidth(field, EditorFieldWidthRole.Reference);
            if (field is AssetPathEditor path) path.Configure("Unknown.png", ["Unknown.png", "Corrected"], null);
            var body = new StackPanel { Children = { new Border { Height = 800 }, field, new Border { Height = 300 } } };
            var scroll = new ScrollViewer { Content = body };
            var tabs = new TabControl { Items = { new TabItem { Header = "Other", Content = new TextBlock { Text = "Other" } }, new TabItem { Header = "Details", Content = scroll } }, SelectedIndex = 0 };
            var window = new Window { Content = tabs, Width = 600, Height = 350 };
            try
            {
                window.Show(); await Settle(window);
                var target = field is AssetPathEditor asset ? asset.CompactPresenter : field;
                var failure = EditorValidationNavigation.Mark(window, field);
                await failure.RevealAsync(revealSection: () => tabs.SelectedIndex = 1); await Settle(window);
                Assert.Equal(1, tabs.SelectedIndex); Assert.True(scroll.Offset.Y > 0);
                Assert.Contains("invalid-field", field.Classes);
                Assert.Equal(2, ((TemplatedControl)target).BorderThickness.Left);
                Assert.IsAssignableFrom<ISolidColorBrush>(((TemplatedControl)target).BorderBrush);
                Assert.True(target.IsKeyboardFocusWithin);
                if (field is AssetPathEditor assetField)
                {
                    var point = assetField.CompactPresenter.TranslatePoint(new Point(15, 15), window)!.Value;
                    window.MouseMove(point);
                    window.MouseDown(point, Avalonia.Input.MouseButton.Left); window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                    await Settle(window);
                    Assert.True(assetField.Editor.IsVisible, $"Asset click did not enter edit mode: point={point}, bounds={assetField.Bounds}, offset={scroll.Offset}, window={window.ClientSize}");
                    Assert.True(assetField.Editor.IsKeyboardFocusWithin);
                    var input = assetField.Editor.GetVisualDescendants().OfType<TextBox>().First();
                    Assert.True(input.IsFocused, $"Asset input focus={window.FocusManager?.GetFocusedElement()?.GetType().Name}; editor text={assetField.Editor.Text}; input text={input.Text}; selected={input.SelectionStart}..{input.SelectionEnd}");
                    Assert.False(input.IsReadOnly, $"Asset inner input is read-only; name={input.Name}, enabled={input.IsEffectivelyEnabled}, window active={window.IsActive}");
                    window.KeyTextInput("Corrected");
                    await Settle(window);
                    Assert.Equal("Corrected", assetField.FullValue);
                    Assert.Equal("Corrected", input.Text);
                }
                else switch (target)
                {
                    case TextBox text: text.Text = "Corrected"; break;
                    case AutoCompleteBox auto: auto.Text = "Corrected"; break;
                    case ComboBox combo: combo.SelectedIndex = 1; break;
                }
                await Settle(window); Assert.DoesNotContain("invalid-field", field.Classes);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void AssetTypingWithoutValidationNavigationBaseline()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var asset = EditorFormLayout.ApplyWidth(new AssetPathEditor(), EditorFieldWidthRole.AssetPath);
            asset.Configure("Unknown.png", ["Unknown.png", "Corrected"], null);
            var window = new Window { Content = asset, Width = 600, Height = 350 };
            try
            {
                window.Show(); await Settle(window);
                var point = asset.CompactPresenter.TranslatePoint(new Point(15, 15), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, Avalonia.Input.MouseButton.Left); window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                await Settle(window);
                window.KeyTextInput("Corrected"); await Settle(window);
                Assert.Equal("Corrected", asset.FullValue);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void ErrorOverridesLocalChromeAndRestoresItWithoutRemovingTheOriginalValue()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var brush = Brush.Parse("#123456");
            var field = new TextBox { Text = "Invalid", BorderBrush = brush, BorderThickness = new Thickness(1) };
            var window = new Window { Content = field, Width = 400, Height = 200 };
            try
            {
                window.Show(); await Settle(window);
                EditorValidationNavigation.Mark(window, field);
                Assert.NotSame(brush, field.BorderBrush); Assert.Equal(2, field.BorderThickness.Left);
                EditorValidationNavigation.Clear(window);
                Assert.Same(brush, field.BorderBrush); Assert.Equal(1, field.BorderThickness.Left);
                Assert.Equal("Invalid", field.Text);
            }
            finally { window.Close(); }
        });

    [Fact]
    public void DismissedOrSupersededFailureCannotRefocusAnOldRecord()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var first = new TextBox { Text = "First" }; var second = new TextBox { Text = "Second" };
            var body = new StackPanel { Children = { first, second } };
            var window = new Window { Content = body, Width = 600, Height = 300 };
            try
            {
                window.Show(); await Settle(window);
                var failure = EditorValidationNavigation.Mark(window, first);
                EditorValidationNavigation.Clear(window); second.Focus();
                await failure.RevealAsync(); Assert.False(first.IsKeyboardFocusWithin);
                Assert.DoesNotContain("invalid-field", first.Classes);
                failure = EditorValidationNavigation.Mark(window, first);
                var current = EditorValidationNavigation.Mark(window, second);
                await failure.RevealAsync(); Assert.False(first.IsKeyboardFocusWithin);
                await current.RevealAsync(() => false); Assert.False(first.IsKeyboardFocusWithin);
                body.Children.Remove(second); await Settle(window);
                Assert.DoesNotContain("invalid-field", second.Classes);
                await current.RevealAsync();
            }
            finally { window.Close(); }
        });

    [Fact]
    public void FieldPredicateRetainsTheErrorUntilExistingValidationAcceptsTheValue()
        => AvaloniaTestHost.RunAsync(async () =>
        {
            var field = new TextBox { Text = "-" };
            var window = new Window { Content = field, Width = 400, Height = 200 };
            try
            {
                window.Show(); await Settle(window);
                EditorValidationNavigation.Mark(window, field, () => int.TryParse(field.Text, out var value) && value >= 0);
                field.Text = "-1"; await Settle(window); Assert.Contains("invalid-field", field.Classes);
                field.Text = "2"; await Settle(window); Assert.DoesNotContain("invalid-field", field.Classes);
            }
            finally { window.Close(); }
        });
}
