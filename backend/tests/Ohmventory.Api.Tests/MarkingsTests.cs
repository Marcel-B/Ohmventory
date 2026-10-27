using Ohmventory.Api.Recognition;

namespace Ohmventory.Api.Tests;

public class MarkingsTests
{
    [Fact]
    public void KeepsModulePrintsAndDropsDesignatorsPinsAndNumbers()
    {
        var terms = Markings.SearchTerms(["C1", "R5", "D1", "01", "1(VDD)", "2(GND)", "VCC", "SDA", "1.30\"IIC", "V2.2", "QMEJ2864-17"]);

        Assert.Equal(["1.30\"IIC", "V2.2", "QMEJ2864-17"], terms);
    }

    [Theory]
    [InlineData(new[] { "C1", "R1", "01", "VDD", "GND" }, false)]
    [InlineData(new[] { "V2.2" }, false)]
    [InlineData(new[] { "1.30\"IIC", "V2.2" }, true)]
    [InlineData(new[] { "GY-521" }, true)]
    public void DetectsWhetherASearchIsWorthIt(string[] markings, bool expected) =>
        Assert.Equal(expected, Markings.HasDistinctive(markings));

    [Fact]
    public void BuildsQueryWithInchInsteadOfQuotes()
    {
        var query = Markings.BuildQuery(["C1", "V2.2", "1.30\" IIC", "GND"]);

        Assert.Equal("1.30 inch IIC V2.2 module", query);
    }

    [Fact]
    public void NoQueryWithoutDistinctiveMarkings() =>
        Assert.Null(Markings.BuildQuery(["C1", "01", "VDD", "GND", "SCK", "SDA"]));
}
