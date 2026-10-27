namespace Ohmventory.Api.Domain;

/// <summary>
/// Physischer Lagerort (z. B. gedrucktes Catch-all-Tray), adressiert über einen kurzen Code wie "T-012".
/// </summary>
public class Container
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public string? Name { get; set; }
    public ContainerType Type { get; set; } = ContainerType.Tray;
    public string? Description { get; set; }
    public Guid? ParentId { get; set; }
    public Container? Parent { get; set; }
    public List<Container> Children { get; set; } = [];
    public List<StockItem> Stock { get; set; } = [];
}

public enum ContainerType
{
    Tray,
    Drawer,
    Box,
    Bag,
    Shelf,
    Other,
}
