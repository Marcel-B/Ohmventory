using Microsoft.EntityFrameworkCore;
using Ohmventory.Api.Data;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Assistant;

public record InventoryEntry(Guid PartId, string Name, string? PartNumber, string Category, int Quantity, IReadOnlyList<string> Containers);

public class InventoryQueries(OhmventoryDbContext db)
{
    public Task<List<Part>> LoadPartsWithStockAsync(CancellationToken ct) =>
        db.Parts.AsNoTracking()
            .Include(p => p.Stock).ThenInclude(s => s.Container)
            .ToListAsync(ct);

    public static InventoryEntry ToEntry(Part p) => new(
        p.Id,
        p.Name,
        p.PartNumber,
        p.Category.ToString(),
        p.Stock.Sum(s => s.Quantity),
        p.Stock.Where(s => s.Container is not null && s.Quantity > 0).Select(s => s.Container!.Code).Distinct().ToList());

    public async Task<IReadOnlyList<InventoryEntry>> SearchAsync(string query, CancellationToken ct)
    {
        var parts = await LoadPartsWithStockAsync(ct);
        var byMatcher = InventoryMatcher.FindMatches(parts, query, query).Select(m => m.Part);
        var byText = parts.Where(p =>
            Contains(p.Name, query) || Contains(p.PartNumber, query) || Contains(p.Description, query) ||
            p.Tags.Any(t => Contains(t, query)));
        return byMatcher.Concat(byText).DistinctBy(p => p.Id).Take(20).Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<InventoryEntry>> OverviewAsync(CancellationToken ct)
    {
        var parts = await LoadPartsWithStockAsync(ct);
        return parts.Select(ToEntry).Where(e => e.Quantity > 0).OrderBy(e => e.Category).ThenBy(e => e.Name).ToList();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
