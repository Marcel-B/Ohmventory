namespace Ohmventory.Api.Domain;

public class Part
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public PartCategory Category { get; set; } = PartCategory.Other;
    public string? Manufacturer { get; set; }
    public string? PartNumber { get; set; }
    public string? Package { get; set; }
    public string? Description { get; set; }
    public string? DatasheetUrl { get; set; }
    public List<string> Tags { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<StockItem> Stock { get; set; } = [];
    public List<PartImage> Images { get; set; } = [];
}

public enum PartCategory
{
    Other,
    Microcontroller,
    BreakoutBoard,
    Sensor,
    Display,
    Actuator,
    PowerSupply,
    Ic,
    Passive,
    Semiconductor,
    Connector,
    Module,
}
