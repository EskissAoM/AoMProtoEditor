using System.Xml.Linq;
using AoMDivineDataEditor.Classes;
using Xunit;

namespace AoMDivineDataEditor.Tests;

public sealed class TechnologyDocumentSafetyTests
{
    [Fact]
    public void ValidDocumentAllowsUnknownImportedXmlWithoutChangingIt()
    {
        var document = XDocument.Parse("""
            <techtreemods custom="preserved">
              <tech name="CustomTech" legacy="yes">
                <unknown value="1" />
                <effect type="Data" subtype="CustomSubtype" relativity="Unexpected" amount="-2.5" />
              </tech>
            </techtreemods>
            """, LoadOptions.PreserveWhitespace);
        var before = document.ToString(SaveOptions.DisableFormatting);

        var result = TechnologyDocumentSafety.ValidateModDocument(document);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Equal(before, document.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void WrongRootIsRejected()
    {
        var result = TechnologyDocumentSafety.ValidateModDocument(
            XDocument.Parse("<techtree><tech name='A' /></techtree>"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("<techtreemods>", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateNamesAreRejectedCaseInsensitively()
    {
        var result = TechnologyDocumentSafety.ValidateModDocument(
            XDocument.Parse("<techtreemods><tech name='Example' /><tech name='example' /></techtreemods>"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("occurs 2 times", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingNameAndNestedTechnologyAreRejected()
    {
        var result = TechnologyDocumentSafety.ValidateModDocument(
            XDocument.Parse("<techtreemods><tech /><group><tech name='Nested' /></group></techtreemods>"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("without a non-empty name", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("outside the root technology list", StringComparison.Ordinal));
    }

    [Fact]
    public void AmbiguousOwnedContainersAreRejected()
    {
        var result = TechnologyDocumentSafety.ValidateModDocument(XDocument.Parse("""
            <techtreemods>
              <tech name="Ambiguous">
                <effects><effect type="Data" /></effects>
                <effects />
                <prereqs />
                <prereqs />
              </tech>
            </techtreemods>
            """));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("2 <effects> containers", StringComparison.Ordinal));
        Assert.Contains(result.Errors, error => error.Contains("2 <prereqs> containers", StringComparison.Ordinal));
        Assert.All(result.Errors.Where(error => error.Contains("containers", StringComparison.Ordinal)),
            error => Assert.Contains("game accepts at most one", error, StringComparison.Ordinal));
    }

    [Fact]
    public void MixedDirectAndContainedEffectsRemainReadable()
    {
        var result = TechnologyDocumentSafety.ValidateModDocument(XDocument.Parse("""
            <techtreemods>
              <tech name="Mixed">
                <effect type="First" />
                <effects><effect type="Second" /></effects>
              </tech>
            </techtreemods>
            """));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DuplicateCostsForTheSameResourceAreRejectedCaseInsensitively()
    {
        var result = TechnologyDocumentSafety.ValidateModDocument(XDocument.Parse("""
            <techtreemods>
              <tech name="DuplicateCost">
                <cost resourcetype="Food">20</cost>
                <cost resourcetype="food">30</cost>
              </tech>
            </techtreemods>
            """));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.Contains("2 <cost> entries", StringComparison.Ordinal) &&
            error.Contains("Food", StringComparison.OrdinalIgnoreCase));
    }
}
