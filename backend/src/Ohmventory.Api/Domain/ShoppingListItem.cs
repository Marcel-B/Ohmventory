namespace Ohmventory.Api.Domain;

public class ShoppingListItem
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? PartNumber { get; set; }
    public int Quantity { get; set; } = 1;
    public string? Url { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public bool Done { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
