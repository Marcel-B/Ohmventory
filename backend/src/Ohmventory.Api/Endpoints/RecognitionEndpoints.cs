using Ohmventory.Api.Assistant;
using Ohmventory.Api.Data;
using Ohmventory.Api.Domain;
using Ohmventory.Api.Recognition;
using Ohmventory.Api.Storage;

namespace Ohmventory.Api.Endpoints;

public record InventoryMatchDto(Guid PartId, string Name, string? PartNumber, int TotalQuantity, double Score);

public record RecognitionResponse(Guid ImageId, RecognizedPart Recognized, IReadOnlyList<InventoryMatchDto> Matches);

public static class RecognitionEndpoints
{
    private const long MaxImageBytes = 15 * 1024 * 1024;

    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public static void MapRecognitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/recognition", async Task<Results<Ok<RecognitionResponse>, ValidationProblem, ProblemHttpResult>> (
            IFormFile file,
            [Microsoft.AspNetCore.Mvc.FromForm] string? hint,
            PartRecognitionService recognition,
            InventoryQueries inventory,
            ImageStorage storage,
            OhmventoryDbContext db,
            CancellationToken ct) =>
        {
            if (!AllowedTypes.TryGetValue(file.ContentType, out var extension))
                return PartEndpoints.Validation("file", "Erlaubt sind JPEG, PNG und WebP.");
            if (file.Length is 0 or > MaxImageBytes)
                return PartEndpoints.Validation("file", "Das Bild muss zwischen 1 Byte und 15 MB groß sein.");

            using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();

            RecognizedPart recognized;
            try
            {
                recognized = await recognition.RecognizeAsync(bytes, file.ContentType, hint, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return TypedResults.Problem(
                    title: "Bauteilerkennung fehlgeschlagen",
                    detail: $"Das lokale LLM war nicht erreichbar oder lieferte keine gültige Antwort: {ex.Message}",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var image = new PartImage
            {
                Id = Guid.NewGuid(),
                FileName = "",
                ContentType = file.ContentType,
                RecognitionJson = PartRecognitionService.Serialize(recognized),
            };
            buffer.Position = 0;
            image.FileName = await storage.SaveAsync(image.Id, extension, buffer, ct);
            db.PartImages.Add(image);
            await db.SaveChangesAsync(ct);

            var parts = await inventory.LoadPartsWithStockAsync(ct);
            var matches = InventoryMatcher.FindMatches(parts, recognized.Name, recognized.PartNumber)
                .Take(5)
                .Select(m => new InventoryMatchDto(m.Part.Id, m.Part.Name, m.Part.PartNumber, m.Part.Stock.Sum(s => s.Quantity), m.Score))
                .ToList();

            return TypedResults.Ok(new RecognitionResponse(image.Id, recognized, matches));
        })
        .DisableAntiforgery()
        .WithTags("Erkennung");

        // Foto nachträglich einem bestehenden Bauteil zuordnen (z. B. wenn die Erkennung ein vorhandenes Teil gefunden hat).
        app.MapPost("/api/images/{id:guid}/assign/{partId:guid}", async Task<Results<NoContent, NotFound>> (Guid id, Guid partId, OhmventoryDbContext db, CancellationToken ct) =>
        {
            var image = await db.PartImages.FindAsync([id], ct);
            if (image is null || await db.Parts.FindAsync([partId], ct) is null)
                return TypedResults.NotFound();
            image.PartId = partId;
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }).WithTags("Erkennung");
    }
}
