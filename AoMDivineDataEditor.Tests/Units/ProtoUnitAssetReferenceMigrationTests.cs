using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class ProtoUnitAssetReferenceMigrationTests
{
    [Fact]
    public void Replace_UpdatesEveryMatchingProtoUnitReferenceCaseInsensitively()
    {
        var root = XElement.Parse("""
            <proto>
              <unit name="UnitA">
                <icon>resources/custom/old.png</icon>
                <icon culture="Greek">RESOURCES\CUSTOM\OLD.PNG</icon>
              </unit>
              <unit name="UnitB"><icon>resources\other.png</icon></unit>
            </proto>
            """);

        var result = ProtoUnitAssetReferenceMigration.Replace(
            root, "icon", "resources\\custom\\old.png", "resources\\heroes\\new.png");

        Assert.Equal(2, result.ReferenceCount);
        Assert.Equal(["UnitA"], result.UnitNames);
        Assert.Equal(2, root.Descendants("icon").Count(element => element.Value == "resources\\heroes\\new.png"));
        Assert.Contains(root.Descendants("icon"), element => element.Value == "resources\\other.png");
    }

    [Fact]
    public void Replace_UpdatesAnimFilesOnlyAndIgnoresNonUnitContent()
    {
        var root = XElement.Parse("""
            <proto>
              <unit name="UnitA"><animfile>custom\old.xml</animfile><icon>custom\old.xml</icon></unit>
              <metadata><animfile>custom\old.xml</animfile></metadata>
            </proto>
            """);

        var result = ProtoUnitAssetReferenceMigration.Replace(root, "animfile", "custom\\old.xml", "custom\\new.xml");

        Assert.Equal(1, result.ReferenceCount);
        Assert.Equal("custom\\new.xml", root.Element("unit")!.Element("animfile")!.Value);
        Assert.Equal("custom\\old.xml", root.Element("unit")!.Element("icon")!.Value);
        Assert.Equal("custom\\old.xml", root.Element("metadata")!.Element("animfile")!.Value);
    }
}
