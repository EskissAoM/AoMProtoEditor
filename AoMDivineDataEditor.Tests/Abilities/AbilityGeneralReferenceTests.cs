using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class AbilityGeneralReferenceTests
{
    [Theory]
    [InlineData("abilitiesmods")]
    [InlineData("abilities")]
    public void GeneralAssociationDirectTextRenamesWithoutTouchingChildrenOrLayout(string root)
    {
        var document = XDocument.Parse($"<{root}><Owner><ability>  Old  <!-- keep --><bindtochargeaction>1</bindtochargeaction><future>Old</future></ability></Owner></{root}>", LoadOptions.PreserveWhitespace);
        var association = document.Descendants("ability").Single();
        var children = association.Elements().Select(child => child.ToString()).ToArray();
        Assert.Contains("<ability>", GameIdentifierReferencePolicy.FindRecognizedReferences(document, GameIdentifierKind.Ability, "Old"));
        Assert.Contains(GameIdentifierReferencePolicy.FindDeletionReferenceLocations(document, GameIdentifierKind.Ability, "Old"), location => location.IsRecognized);
        Assert.Equal(1, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.Ability, "Old", "New"));
        Assert.Equal("  New  ", association.Nodes().OfType<XText>().Single().Value);
        Assert.Equal(children, association.Elements().Select(child => child.ToString()));
        Assert.Equal(" keep ", association.Nodes().OfType<XComment>().Single().Value);
        Assert.Empty(GameIdentifierReferencePolicy.FindRecognizedReferences(document, GameIdentifierKind.Ability, "Old"));
    }

    [Fact]
    public void OtherMixedContentAndPartialNamesAreNotReinterpretedAsGeneralAssociations()
    {
        var document = XDocument.Parse("<root><Owner><ability>Old<future>Keep</future></ability></Owner><abilitiesmods><Owner><ability>OldSuffix<future/></ability></Owner></abilitiesmods></root>");
        var before = document.ToString();
        Assert.Equal(0, GameIdentifierReferencePolicy.RenameReferences(document, GameIdentifierKind.Ability, "Old", "New"));
        Assert.Equal(before, document.ToString());
    }
}
