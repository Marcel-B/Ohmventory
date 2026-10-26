using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ohmventory.Api.Data;
using Ohmventory.Api.Domain;
using QRCoder;

namespace Ohmventory.Api.Endpoints;

public class LabelOptions
{
    public const string Section = "Labels";
    /// <summary>Adresse der Web-App; der QR-Code auf einem Container verweist auf {PublicBaseUrl}/containers/{code}.</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}

public static class ContainerEndpoints
{
    public static void MapContainerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/containers").WithTags("Container");

        group.MapGet("/", async (OhmventoryDbContext db, CancellationToken ct) =>
        {
            var containers = await db.Containers.AsNoTracking().Include(c => c.Parent).Include(c => c.Stock)
                .OrderBy(c => c.Code).ToListAsync(ct);
            return containers.Select(ContainerDto.From);
        });

        group.MapGet("/{code}", async Task<Results<Ok<ContainerDetailDto>, NotFound>> (string code, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var container = await db.Containers.AsNoTracking()
                .Include(c => c.Parent)
                .Include(c => c.Children).ThenInclude(c => c.Stock)
                .Include(c => c.Stock).ThenInclude(s => s.Part)
                .AsSplitQuery()
                .FirstOrDefaultAsync(c => c.Code == NormalizeCode(code), ct);
            if (container is null)
                return TypedResults.NotFound();

            var contents = container.Stock.Where(s => s.Quantity > 0)
                .OrderBy(s => s.Part.Name)
                .Select(s => new ContainerContentDto(s.PartId, s.Part.Name, s.Part.PartNumber, s.Quantity))
                .ToList();
            var children = container.Children.OrderBy(c => c.Code).Select(ContainerDto.From).ToList();
            return TypedResults.Ok(new ContainerDetailDto(ContainerDto.From(container), children, contents));
        });

        group.MapPost("/", async Task<Results<Created<ContainerDto>, ValidationProblem>> (ContainerUpsertDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var code = string.IsNullOrWhiteSpace(dto.Code)
                ? await NextCodeAsync(db, dto.Type, ct)
                : NormalizeCode(dto.Code);
            if (await db.Containers.AnyAsync(c => c.Code == code, ct))
                return PartEndpoints.Validation("code", $"Code {code} ist bereits vergeben.");
            if (dto.ParentId is { } parentId && !await db.Containers.AnyAsync(c => c.Id == parentId, ct))
                return PartEndpoints.Validation("parentId", "Übergeordneter Container existiert nicht.");

            var container = new Container
            {
                Code = code,
                Name = PartEndpoints.Clean(dto.Name),
                Type = dto.Type,
                Description = PartEndpoints.Clean(dto.Description),
                ParentId = dto.ParentId,
            };
            db.Containers.Add(container);
            await db.SaveChangesAsync(ct);
            await db.Entry(container).Reference(c => c.Parent).LoadAsync(ct);
            return TypedResults.Created($"/api/containers/{container.Code}", ContainerDto.From(container));
        });

        group.MapPut("/{id:guid}", async Task<Results<Ok<ContainerDto>, NotFound, ValidationProblem>> (Guid id, ContainerUpsertDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var container = await db.Containers.Include(c => c.Stock).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (container is null)
                return TypedResults.NotFound();

            if (!string.IsNullOrWhiteSpace(dto.Code))
            {
                var code = NormalizeCode(dto.Code);
                if (code != container.Code && await db.Containers.AnyAsync(c => c.Code == code, ct))
                    return PartEndpoints.Validation("code", $"Code {code} ist bereits vergeben.");
                container.Code = code;
            }
            if (dto.ParentId == id || (dto.ParentId is { } parentId && await IsDescendantAsync(db, parentId, id, ct)))
                return PartEndpoints.Validation("parentId", "Ein Container kann nicht in sich selbst liegen.");

            container.Name = PartEndpoints.Clean(dto.Name);
            container.Type = dto.Type;
            container.Description = PartEndpoints.Clean(dto.Description);
            container.ParentId = dto.ParentId;
            await db.SaveChangesAsync(ct);
            await db.Entry(container).Reference(c => c.Parent).LoadAsync(ct);
            return TypedResults.Ok(ContainerDto.From(container));
        });

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound, Conflict<string>>> (Guid id, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var container = await db.Containers.Include(c => c.Stock).Include(c => c.Children).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (container is null)
                return TypedResults.NotFound();
            if (container.Stock.Any(s => s.Quantity > 0) || container.Children.Count > 0)
                return TypedResults.Conflict("Container ist nicht leer.");

            db.StockItems.RemoveRange(container.Stock);
            db.Containers.Remove(container);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        });

        group.MapGet("/{code}/label.svg", (string code, IOptions<LabelOptions> options) =>
        {
            var normalized = NormalizeCode(code);
            var url = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/containers/{Uri.EscapeDataString(normalized)}";
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M);
            var svg = new SvgQRCode(data).GetGraphic(8);
            return TypedResults.Text(svg, "image/svg+xml");
        });
    }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static string CodePrefix(ContainerType type) => type switch
    {
        ContainerType.Tray => "T",
        ContainerType.Drawer => "S",
        ContainerType.Box => "B",
        ContainerType.Bag => "TU",
        ContainerType.Shelf => "R",
        _ => "C",
    };

    private static async Task<string> NextCodeAsync(OhmventoryDbContext db, ContainerType type, CancellationToken ct)
    {
        var prefix = CodePrefix(type) + "-";
        var existing = await db.Containers.Where(c => c.Code.StartsWith(prefix)).Select(c => c.Code).ToListAsync(ct);
        var next = existing.Select(c => int.TryParse(c[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        return $"{prefix}{next:D3}";
    }

    private static async Task<bool> IsDescendantAsync(OhmventoryDbContext db, Guid candidateId, Guid ancestorId, CancellationToken ct)
    {
        Guid? current = candidateId;
        var seen = new HashSet<Guid>();
        while (current is { } id && seen.Add(id))
        {
            if (id == ancestorId)
                return true;
            current = await db.Containers.Where(c => c.Id == id).Select(c => c.ParentId).FirstOrDefaultAsync(ct);
        }
        return false;
    }
}
