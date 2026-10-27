using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Endpoints;

public record StockDto(Guid? ContainerId, string? ContainerCode, int Quantity, string? Note);

public record PartDto(
    Guid Id,
    string Name,
    PartCategory Category,
    string? Manufacturer,
    string? PartNumber,
    string? Package,
    string? Description,
    string? DatasheetUrl,
    List<string> Tags,
    int TotalQuantity,
    IReadOnlyList<StockDto> Stock,
    IReadOnlyList<Guid> ImageIds)
{
    public static PartDto From(Part p) => new(
        p.Id, p.Name, p.Category, p.Manufacturer, p.PartNumber, p.Package, p.Description, p.DatasheetUrl, p.Tags,
        p.Stock.Sum(s => s.Quantity),
        p.Stock.OrderBy(s => s.Container?.Code).Select(s => new StockDto(s.ContainerId, s.Container?.Code, s.Quantity, s.Note)).ToList(),
        p.Images.OrderBy(i => i.CreatedAt).Select(i => i.Id).ToList());
}

public record PartUpsertDto(
    string Name,
    PartCategory Category,
    string? Manufacturer,
    string? PartNumber,
    string? Package,
    string? Description,
    string? DatasheetUrl,
    List<string>? Tags);

/// <summary>Neues Bauteil anlegen, optional mit Startbestand und dem Foto aus der Erkennung.</summary>
public record PartCreateDto(PartUpsertDto Part, int Quantity, Guid? ContainerId, Guid? ImageId);

public record SetStockDto(Guid? ContainerId, int Quantity, string? Note);

public record ContainerDto(Guid Id, string Code, string? Name, ContainerType Type, string? Description, Guid? ParentId, string? ParentCode, int PartCount)
{
    public static ContainerDto From(Container c) => new(
        c.Id, c.Code, c.Name, c.Type, c.Description, c.ParentId, c.Parent?.Code, c.Stock.Count(s => s.Quantity > 0));
}

public record ContainerUpsertDto(string? Code, string? Name, ContainerType Type, string? Description, Guid? ParentId);

public record ContainerContentDto(Guid PartId, string PartName, string? PartNumber, int Quantity);

public record ContainerDetailDto(ContainerDto Container, IReadOnlyList<ContainerDto> Children, IReadOnlyList<ContainerContentDto> Contents);

public record ShoppingListItemDto(Guid Id, string Name, string? PartNumber, int Quantity, string? Url, Guid? ProjectId, string? ProjectName, bool Done)
{
    public static ShoppingListItemDto From(ShoppingListItem i) =>
        new(i.Id, i.Name, i.PartNumber, i.Quantity, i.Url, i.ProjectId, i.Project?.Name, i.Done);
}

public record ShoppingListUpsertDto(string Name, string? PartNumber, int Quantity, string? Url, bool Done);
