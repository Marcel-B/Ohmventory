using Microsoft.EntityFrameworkCore;
using Ohmventory.Api.Data;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Endpoints;

public static class ShoppingListEndpoints
{
    public static void MapShoppingListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/shopping-list").WithTags("Einkaufsliste");

        group.MapGet("/", async (OhmventoryDbContext db, CancellationToken ct) =>
        {
            var items = await db.ShoppingListItems.AsNoTracking().Include(i => i.Project)
                .OrderBy(i => i.Done).ThenByDescending(i => i.CreatedAt).ToListAsync(ct);
            return items.Select(ShoppingListItemDto.From);
        });

        group.MapPost("/", async Task<Results<Created<ShoppingListItemDto>, ValidationProblem>> (ShoppingListUpsertDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return PartEndpoints.Validation("name", "Name ist erforderlich.");
            var item = new ShoppingListItem { Name = dto.Name.Trim() };
            Apply(item, dto);
            db.ShoppingListItems.Add(item);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/shopping-list/{item.Id}", ShoppingListItemDto.From(item));
        });

        group.MapPut("/{id:guid}", async Task<Results<Ok<ShoppingListItemDto>, NotFound, ValidationProblem>> (Guid id, ShoppingListUpsertDto dto, OhmventoryDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return PartEndpoints.Validation("name", "Name ist erforderlich.");
            var item = await db.ShoppingListItems.Include(i => i.Project).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (item is null)
                return TypedResults.NotFound();
            item.Name = dto.Name.Trim();
            Apply(item, dto);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ShoppingListItemDto.From(item));
        });

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.ShoppingListItems.Where(i => i.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        });
    }

    private static void Apply(ShoppingListItem item, ShoppingListUpsertDto dto)
    {
        item.PartNumber = PartEndpoints.Clean(dto.PartNumber);
        item.Quantity = Math.Max(dto.Quantity, 1);
        item.Url = PartEndpoints.Clean(dto.Url);
        item.Done = dto.Done;
    }
}
