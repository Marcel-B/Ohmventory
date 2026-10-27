using Ohmventory.Api.Assistant;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Tests;

public class AvailabilityTests
{
    private static Part PartInStock(string name, string partNumber, params (string Code, int Quantity)[] stock) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        PartNumber = partNumber,
        Stock = stock.Select(s => new StockItem { Quantity = s.Quantity, Container = new Container { Code = s.Code } }).ToList(),
    };

    [Fact]
    public void SumsStockAcrossContainers()
    {
        var part = PartInStock("BME280", "BME280", ("T-001", 2), ("T-004", 1));

        var result = ProjectAssistantService.CheckAvailability(new SuggestedPart("BME280 Breakout", "BME280", 2, false), [part]);

        Assert.Equal(3, result.QuantityInStock);
        Assert.Equal(0, result.QuantityMissing);
        Assert.Equal(["T-001", "T-004"], result.Containers);
    }

    [Fact]
    public void ReportsMissingQuantityWhenStockIsShort()
    {
        var part = PartInStock("10k Widerstand", "R-10K", ("S-002", 3));

        var result = ProjectAssistantService.CheckAvailability(new SuggestedPart("Widerstand 10k", "R-10K", 5, false), [part]);

        Assert.Equal(2, result.QuantityMissing);
    }

    [Fact]
    public void UnknownPartIsFullyMissing()
    {
        var result = ProjectAssistantService.CheckAvailability(new SuggestedPart("ESP32 DevKit", "ESP32", 1, false), []);

        Assert.Null(result.MatchedPartId);
        Assert.Equal(1, result.QuantityMissing);
    }

    [Fact]
    public void QuantityBelowOneCountsAsOne()
    {
        var result = ProjectAssistantService.CheckAvailability(new SuggestedPart("ESP32", "ESP32", 0, false), []);

        Assert.Equal(1, result.QuantityRequired);
    }
}
