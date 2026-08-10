using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using AoMProtoEditor;
using CryBarEditor.Classes;
using Xunit;

namespace AoMProtoEditor.Tests;

public sealed class EditorNumericFieldStyleTests
{
    private static bool _avaloniaInitialized;

    public static IEnumerable<object[]> FormattingCases =>
    [
        ["10.0000", "10"],
        ["10.2500", "10.25"],
        ["0.000", "0"],
        ["-2.5000", "-2.5"],
        ["1.2300400", "1.23004"],
        ["123456789.25", "123456789.25"],
        ["not a number", "not a number"],
    ];

    [Theory]
    [MemberData(nameof(FormattingCases))]
    public void FormatDisplay_NormalizesOnlyValidInvariantDecimalText(string source, string expected)
    {
        Assert.Equal(expected, EditorNumericFieldStyle.FormatDisplay(source));
    }

    [Fact]
    public void FormatDisplay_LeavesXmlPreviewTextUntouched()
    {
        const string xmlPreview = "<unit name=\"Example\"><maxhitpoints>10.0000</maxhitpoints></unit>";

        Assert.Equal(xmlPreview, EditorNumericFieldStyle.FormatDisplay(xmlPreview));
    }

    [Theory]
    [InlineData("0.4", "0.40")]
    [InlineData("1", "1.00")]
    [InlineData("0.400", "0.40")]
    public void FormatDisplay_WithMinimumFractionDigits_PadsOnlyTheNumericDisplay(string source, string expected)
    {
        Assert.Equal(expected, EditorNumericFieldStyle.FormatDisplay(source, minimumFractionDigits: 2));
    }

    [Fact]
    public void ConfigureNumericTextBox_IsCompact_PreservesLongText_AndFormatsOnLostFocus()
    {
        EnsureAvalonia();
        var textBox = new TextBox { Text = "123456789.2500" };

        EditorNumericFieldStyle.ConfigureNumericTextBox(textBox);

        Assert.Equal(EditorNumericFieldStyle.CompactWidth, textBox.Width);
        Assert.Equal("123456789.25", textBox.Text);

        textBox.Text = "10.2500";
        textBox.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));

        Assert.Equal("10.25", textBox.Text);
    }

    private static void EnsureAvalonia()
    {
        if (_avaloniaInitialized || Application.Current != null)
        {
            _avaloniaInitialized = true;
            return;
        }

        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();
        _avaloniaInitialized = true;
    }
}
