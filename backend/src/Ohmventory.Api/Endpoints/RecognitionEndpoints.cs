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
    private const int MaxViews = 2;

    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    private const string AllowedText = "Erlaubt sind JPEG, PNG, WebP und HEIC.";

    /// <summary>
    /// Liest ein hochgeladenes Bild. HEIC/HEIF (erkannt am Inhalt, nicht am oft fehlenden Content-Type) wird in JPEG umgewandelt,
    /// JPEG, PNG und WebP bleiben, wie sie sind. Null bei allem anderen und bei kaputten HEIC-Dateien.
    /// </summary>
    private static async Task<(byte[] Data, string ContentType)?> ReadImageAsync(IFormFile file, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var data = buffer.ToArray();
        if (HeifConverter.IsHeif(data))
        {
            try
            {
                return (await Task.Run(() => HeifConverter.ToJpeg(data), ct), "image/jpeg");
            }
            catch (ImageMagick.MagickException)
            {
                return null;
            }
        }
        return AllowedTypes.ContainsKey(file.ContentType) ? (data, file.ContentType) : null;
    }

    public static void MapRecognitionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/recognition", async Task<Results<Ok<RecognitionResponse>, ValidationProblem, ProblemHttpResult>> (
            IFormFile file,
            [Microsoft.AspNetCore.Mvc.FromForm] string? hint,
            HttpRequest request,
            PartRecognitionService recognition,
            InventoryQueries inventory,
            ImageStorage storage,
            OhmventoryDbContext db,
            CancellationToken ct) =>
        {
            if (file.Length is 0 or > MaxImageBytes)
                return PartEndpoints.Validation("file", "Das Bild muss zwischen 1 Byte und 15 MB groß sein.");
            if (await ReadImageAsync(file, ct) is not (var bytes, var contentType))
                return PartEndpoints.Validation("file", AllowedText);
            var extension = AllowedTypes[contentType];

            // Gedrehte Ansichten desselben Fotos schickt die App mit, damit senkrechter Text lesbar wird. Sie werden nicht gespeichert.
            var views = new List<(byte[] Data, string MediaType)>();
            foreach (var view in request.Form.Files.GetFiles("views").Take(MaxViews))
            {
                if (view.Length is 0 or > MaxImageBytes || await ReadImageAsync(view, ct) is not { } viewImage)
                    return PartEndpoints.Validation("views", "Zusätzliche Ansichten müssen JPEG, PNG, WebP oder HEIC bis 15 MB sein.");
                views.Add(viewImage);
            }

            RecognizedPart recognized;
            try
            {
                recognized = await recognition.RecognizeAsync(bytes, contentType, views, hint, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                return LlmEndpoints.Problem("Bauteilerkennung fehlgeschlagen", ex);
            }

            var image = new PartImage
            {
                Id = Guid.NewGuid(),
                FileName = "",
                ContentType = contentType,
                RecognitionJson = PartRecognitionService.Serialize(recognized),
            };
            using var stored = new MemoryStream(bytes, writable: false);
            image.FileName = await storage.SaveAsync(image.Id, extension, stored, ct);
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

        // Wandelt ein HEIC-Foto in JPEG um. Die App braucht das in Browsern, die HEIC nicht selbst anzeigen können (alle außer Safari).
        app.MapPost("/api/images/convert", async Task<Results<FileContentHttpResult, ValidationProblem>> (IFormFile file, CancellationToken ct) =>
        {
            if (file.Length is 0 or > MaxImageBytes)
                return PartEndpoints.Validation("file", "Das Bild muss zwischen 1 Byte und 15 MB groß sein.");
            if (await ReadImageAsync(file, ct) is not (var bytes, var contentType))
                return PartEndpoints.Validation("file", AllowedText);
            return TypedResults.File(bytes, contentType);
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
