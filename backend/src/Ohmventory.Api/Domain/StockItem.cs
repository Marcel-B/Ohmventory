namespace Ohmventory.Api.Domain;

public class StockItem
{
    public Guid Id { get; set; }
    public Guid PartId { get; set; }
    public Part Part { get; set; } = null!;
    public Guid? ContainerId { get; set; }
    public Container? Container { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
