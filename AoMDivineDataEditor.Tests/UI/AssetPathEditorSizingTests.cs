using AoMDivineDataEditor.Controls;
using Xunit;

namespace AoMDivineDataEditor.Tests.UI;

public sealed class AssetPathEditorSizingTests
{
    [Fact]
    public void Editing_selector_stretches_with_narrow_paired_asset_field()
    {
        AvaloniaTestHost.Run(() =>
        {
            var editor = new AssetPathEditor { Width = 250 };

            Assert.True(double.IsNaN(editor.Editor.Width));
            Assert.True(double.IsPositiveInfinity(editor.Editor.MaxWidth));
            Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, editor.Editor.HorizontalAlignment);
        });
    }

    [Fact]
    public void Strict_asset_field_clears_text_that_is_not_in_its_catalog()
    {
        AvaloniaTestHost.Run(() =>
        {
            var changedValue = "not-called";
            var editor = new AssetPathEditor { RequireSuggestionMatch = true };
            editor.Configure(
                "resources\\invalid.png",
                ["resources\\valid.png"],
                value =>
                {
                    changedValue = value;
                    return Task.CompletedTask;
                });

            editor.CommitEditing();

            Assert.Equal("", editor.FullValue);
            Assert.Equal("", changedValue);
        });
    }

    [Fact]
    public void Strict_asset_field_preserves_an_exact_catalog_path()
    {
        AvaloniaTestHost.Run(() =>
        {
            var editor = new AssetPathEditor { RequireSuggestionMatch = true };
            editor.Configure("resources\\valid.png", ["resources\\valid.png"], null);

            editor.CommitEditing();

            Assert.Equal("resources\\valid.png", editor.FullValue);
        });
    }
}
