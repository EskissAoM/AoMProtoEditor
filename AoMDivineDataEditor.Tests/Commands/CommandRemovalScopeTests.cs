using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed partial class CommandXmlEditingTests
{
    [Fact]
    public void CommandOptionalRemovalRestoresAddAndRetainsTheReaddedValueOnDisk()
        => WithCommand(async (window, path, _) =>
        {
            var add = window.GetLogicalDescendants().OfType<Button>().Single(button => Equals(button.Content, "Value Text"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var fields = Field<Dictionary<string, Control>>(window, "_valueEditors");
            var value = Assert.IsType<TextBox>(fields["valuetext"]);
            value.Text = "42";
            var row = Assert.IsType<StackPanel>(value.Parent);
            var remove = Assert.Single(row.Children.OfType<Button>());
            Assert.Contains("remove-field", remove.Classes); Assert.Equal("×", remove.Content);
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(fields.ContainsKey("valuetext")); Assert.True(add.IsVisible);
            Assert.Null(XElement.Parse(Raw(window).Text).Element("valuetext"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsType<TextBox>(fields["valuetext"]).Text = "43";
            Assert.Empty(await Save(window));
            var saved = XDocument.Load(path).Root!.Element("protounitcommand")!;
            Assert.Equal("43", saved.Element("valuetext")!.Value);
            Assert.Equal("Do", saved.Element("command")!.Value);
            Assert.Equal("yes", (string?)saved.Element("future")!.Attribute("keep"));
        });
}
