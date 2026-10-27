using Microsoft.EntityFrameworkCore;
using Ohmventory.Api.Assistant;
using Ohmventory.Api.Data;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Endpoints;

public record AskDto(string Question);

public record SaveProjectDto(string Name, string? Description, string? Question, List<string>? Sources, List<SuggestedPart> Parts);

public record ProjectDto(Guid Id, string Name, string? Description, string? Question, List<string> Sources, DateTimeOffset CreatedAt, IReadOnlyList<PartAvailability> Parts);

public static class ProjectEndpoints
{
    public static void MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assistant/ask", async Task<Results<Ok<AssistantAnswer>, ValidationProblem, ProblemHttpResult>> (AskDto dto, ProjectAssistantService assistant, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Question))
                return PartEndpoints.Validation("question", "Bitte eine Frage stellen.");
            try
            {
                return TypedResults.Ok(await assistant.AskAsync(dto.Question.Trim(), ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return LlmEndpoints.Problem("Projektassistent fehlgeschlagen", ex);
            }
        }).WithTags("Projekte");

        var group = app.MapGroup("/api/projects").WithTags("Projekte");

        group.MapGet("/", async (OhmventoryDbContext db, InventoryQueries inventory, CancellationToken ct) =>
        {
            var projects = await db.Projects.AsNoTracking().Include(p => p.Items).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
            var parts = await inventory.LoadPartsWithStockAsync(ct);
            return projects.Select(p => ToDto(p, parts));
        });

        group.MapGet("/{id:guid}", async Task<Results<Ok<ProjectDto>, NotFound>> (Guid id, OhmventoryDbContext db, InventoryQueries inventory, CancellationToken ct) =>
        {
            var project = await db.Projects.AsNoTracking().Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return TypedResults.NotFound();
            return TypedResults.Ok(ToDto(project, await inventory.LoadPartsWithStockAsync(ct)));
        });

        group.MapPost("/", async Task<Results<Created<ProjectDto>, ValidationProblem>> (SaveProjectDto dto, OhmventoryDbContext db, InventoryQueries inventory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return PartEndpoints.Validation("name", "Name ist erforderlich.");

            var project = new Project
            {
                Name = dto.Name.Trim(),
                Description = PartEndpoints.Clean(dto.Description),
                Question = PartEndpoints.Clean(dto.Question),
                Sources = dto.Sources ?? [],
                Items = dto.Parts.Select(p => new ProjectItem
                {
                    Name = p.Name,
                    PartNumber = PartEndpoints.Clean(p.PartNumber),
                    QuantityRequired = Math.Max(p.Quantity, 1),
                }).ToList(),
            };
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/projects/{project.Id}", ToDto(project, await inventory.LoadPartsWithStockAsync(ct)));
        });

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Projects.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        });

        // Übernimmt alle fehlenden Bauteile eines Projekts in die Einkaufsliste (bereits offene Einträge werden nicht doppelt angelegt).
        group.MapPost("/{id:guid}/shopping-list", async Task<Results<Ok<List<ShoppingListItemDto>>, NotFound>> (Guid id, OhmventoryDbContext db, InventoryQueries inventory, CancellationToken ct) =>
        {
            var project = await db.Projects.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct);
            if (project is null)
                return TypedResults.NotFound();

            var parts = await inventory.LoadPartsWithStockAsync(ct);
            var open = await db.ShoppingListItems.Where(i => i.ProjectId == id && !i.Done).Select(i => i.Name).ToListAsync(ct);
            var added = new List<ShoppingListItem>();
            foreach (var item in project.Items)
            {
                var availability = ProjectAssistantService.CheckAvailability(ToSuggested(item), parts);
                if (availability.QuantityMissing == 0 || open.Contains(item.Name))
                    continue;
                added.Add(new ShoppingListItem
                {
                    Name = item.Name,
                    PartNumber = item.PartNumber,
                    Quantity = availability.QuantityMissing,
                    ProjectId = project.Id,
                    Project = project,
                });
            }

            db.ShoppingListItems.AddRange(added);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(added.Select(ShoppingListItemDto.From).ToList());
        });
    }

    private static SuggestedPart ToSuggested(ProjectItem item) => new(item.Name, item.PartNumber, item.QuantityRequired, false);

    private static ProjectDto ToDto(Project p, List<Part> parts) => new(
        p.Id, p.Name, p.Description, p.Question, p.Sources, p.CreatedAt,
        p.Items.Select(i => ProjectAssistantService.CheckAvailability(ToSuggested(i), parts)).ToList());
}
