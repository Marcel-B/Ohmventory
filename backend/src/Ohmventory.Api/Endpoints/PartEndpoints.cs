using Microsoft.EntityFrameworkCore;
using Ohmventory.Api.Data;
using Ohmventory.Api.Domain;
using Ohmventory.Api.Storage;

namespace Ohmventory.Api.Endpoints;

public static class PartEndpoints
{
    public static void MapPartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/parts").WithTags("Bauteile");

        group.MapGet("/", async (OhmventoryDbContext db, string? q, PartCategory? category, CancellationToken ct) =>
        {
            var query = WithDetails(db.Parts.AsNoTracking());
            if (category is not null)
                query = query.Where(p => p.Category == category);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = $"%{q.Trim()}%";
                query = query.Where(p =>
                    EF.Functions.ILike(p.Name, pattern) ||
                    (p.PartNumber != null && EF.Functions.ILike(p.PartNumber, pattern)) ||
                    (p.Manufacturer != null && EF.Functions.ILike(p.Manufacturer, pattern)) ||
                    (p.Description != null && EF.Functions.ILike(p.Description, pattern)));
            }

            var parts = await query.OrderBy(p => p.Name).ToListAsync(ct);
            return parts.Select(PartDto.From);
        });

        group.MapGet("/{id:guid}", async Task<Results<Ok<PartDto>, NotFound>> (Guid id, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var part = await WithDetails(db.Parts.AsNoTracking()).FirstOrDefaultAsync(p => p.Id == id, ct);
            return part is null ? TypedResults.NotFound() : TypedResults.Ok(PartDto.From(part));
        });

        group.MapPost("/", async Task<Results<Created<PartDto>, ValidationProblem>> (PartCreateDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Part.Name))
                return Validation("name", "Name ist erforderlich.");
            if (dto.Quantity < 0)
                return Validation("quantity", "Menge darf nicht negativ sein.");

            var part = new Part { Name = dto.Part.Name.Trim() };
            Apply(part, dto.Part);
            db.Parts.Add(part);

            if (dto.Quantity > 0 || dto.ContainerId is not null)
                part.Stock.Add(new StockItem { ContainerId = dto.ContainerId, Quantity = dto.Quantity });

            if (dto.ImageId is { } imageId && await db.PartImages.FindAsync([imageId], ct) is { } image)
                image.Part = part;

            await db.SaveChangesAsync(ct);
            var created = await WithDetails(db.Parts.AsNoTracking()).FirstAsync(p => p.Id == part.Id, ct);
            return TypedResults.Created($"/api/parts/{part.Id}", PartDto.From(created));
        });

        group.MapPut("/{id:guid}", async Task<Results<Ok<PartDto>, NotFound, ValidationProblem>> (Guid id, PartUpsertDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return Validation("name", "Name ist erforderlich.");
            var part = await WithDetails(db.Parts).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (part is null)
                return TypedResults.NotFound();

            part.Name = dto.Name.Trim();
            Apply(part, dto);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(PartDto.From(part));
        });

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Parts.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        });

        // Setzt den Bestand eines Bauteils in einem Container absolut; Menge 0 entfernt den Eintrag.
        group.MapPut("/{id:guid}/stock", async Task<Results<Ok<PartDto>, NotFound, ValidationProblem>> (Guid id, SetStockDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            if (dto.Quantity < 0)
                return Validation("quantity", "Menge darf nicht negativ sein.");
            var part = await WithDetails(db.Parts).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (part is null)
                return TypedResults.NotFound();
            if (dto.ContainerId is { } containerId && !await db.Containers.AnyAsync(c => c.Id == containerId, ct))
                return Validation("containerId", "Container existiert nicht.");

            var stock = part.Stock.FirstOrDefault(s => s.ContainerId == dto.ContainerId);
            if (dto.Quantity == 0)
            {
                if (stock is not null)
                    db.StockItems.Remove(stock);
            }
            else if (stock is null)
            {
                db.StockItems.Add(new StockItem { PartId = part.Id, ContainerId = dto.ContainerId, Quantity = dto.Quantity, Note = dto.Note });
            }
            else
            {
                stock.Quantity = dto.Quantity;
                stock.Note = dto.Note;
                stock.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            var updated = await WithDetails(db.Parts.AsNoTracking()).FirstAsync(p => p.Id == id, ct);
            return TypedResults.Ok(PartDto.From(updated));
        });

        app.MapGet("/api/images/{id:guid}", async Task<Results<PhysicalFileHttpResult, NotFound>> (Guid id, OhmventoryDbContext db, ImageStorage storage, CancellationToken ct) =>
        {
            var image = await db.PartImages.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            var path = image is null ? null : storage.GetPath(image.FileName);
            return path is null || !File.Exists(path) ? TypedResults.NotFound() : TypedResults.PhysicalFile(path, image!.ContentType);
        }).WithTags("Bauteile");
    }

    private static IQueryable<Part> WithDetails(IQueryable<Part> query) =>
        query.Include(p => p.Stock).ThenInclude(s => s.Container).Include(p => p.Images).AsSplitQuery();

    private static void Apply(Part part, PartUpsertDto dto)
    {
        part.Category = dto.Category;
        part.Manufacturer = Clean(dto.Manufacturer);
        part.PartNumber = Clean(dto.PartNumber);
        part.Package = Clean(dto.Package);
        part.Description = Clean(dto.Description);
        part.DatasheetUrl = Clean(dto.DatasheetUrl);
        part.Tags = dto.Tags?.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList() ?? [];
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static ValidationProblem Validation(string field, string message) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}
