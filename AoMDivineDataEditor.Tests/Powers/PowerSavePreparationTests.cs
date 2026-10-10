using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class PowerSavePreparationTests
{
    [Fact]
    public void PrepareAppliesRecognizedRenamesAndFiltersOnlyUnreferencedStringRemovals()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Edited" type="Rain">
                <displaynameid>STR_KEEP</displaynameid>
                <effect power="OldPower"><target type="Player" /></effect>
              </power>
            </powersmod>
            """, LoadOptions.PreserveWhitespace);

        var result = PowerSavePreparation.Prepare(
            document,
            ["Edited"],
            [new KeyValuePair<string, string>("OldPower", "NewPower")],
            new Dictionary<string, string> { ["STR_UPDATE"] = "Updated" },
            ["STR_KEEP", "STR_REMOVE"]);

        Assert.Equal("NewPower", (string?)document.Descendants("effect").Single().Attribute("power"));
        Assert.Equal("Updated", result.StringUpdates["STR_UPDATE"]);
        Assert.DoesNotContain("STR_KEEP", result.StringRemovals);
        Assert.Contains("STR_REMOVE", result.StringRemovals);
        Assert.True(result.WritesStrings);
    }

    [Fact]
    public void PrepareValidatesEveryRenameBeforeApplyingAnyOfThem()
    {
        var document = XDocument.Parse("""
            <powersmod>
              <power name="Edited" type="Rain">
                <effect power="FirstOld" />
                <futurePowerReference>UnsupportedOld</futurePowerReference>
              </power>
            </powersmod>
            """);

        Assert.Throws<InvalidOperationException>(() => PowerSavePreparation.Prepare(
            document,
            ["Edited"],
            [
                new KeyValuePair<string, string>("FirstOld", "FirstNew"),
                new KeyValuePair<string, string>("UnsupportedOld", "UnsupportedNew")
            ],
            new Dictionary<string, string>(),
            []));

        Assert.Equal("FirstOld", (string?)document.Descendants("effect").Single().Attribute("power"));
    }
}
