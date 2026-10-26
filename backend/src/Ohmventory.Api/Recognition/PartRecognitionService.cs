using System.Text.Json;
using Microsoft.Extensions.AI;
using Ohmventory.Api.Assistant;
using Ohmventory.Api.Domain;
using Ohmventory.Api.Llm;

namespace Ohmventory.Api.Recognition;

public record RecognizedPart(
    string Name,
    PartCategory Category,
    string? Manufacturer,
    string? PartNumber,
    string? Package,
    string? Description,
    bool IsBreakoutBoard,
    List<string> MarkingsRead,
    List<string> Alternatives,
    double Confidence);

public class PartRecognitionService(
    [FromKeyedServices(LlmServiceCollectionExtensions.VisionClientKey)] IChatClient vision,
    ILogger<PartRecognitionService> logger)
{
    private const string SystemPrompt = """
        Du bist Experte für Elektronikbauteile. Du bekommst ein Foto von einem einzelnen Bauteil
        oder einem Breakout-Board / Modul. Bestimme, worum es sich handelt.
        - Lies alle Beschriftungen auf Chips und Platine (z. B. "BME280", "ESP32-WROOM-32", "GY-521") und gib sie in markingsRead an.
        - name: kurze, gängige Bezeichnung auf Deutsch, z. B. "BME280 Temperatur-/Feuchte-/Drucksensor (Breakout)".
        - partNumber: die Typbezeichnung des Hauptbauteils, sofern erkennbar, sonst null.
        - isBreakoutBoard: true, wenn es ein Modul/Breakout-Board ist, false bei einem einzelnen Bauteil.
        - alternatives: andere plausible Kandidaten, falls du dir nicht sicher bist.
        - confidence: 0.0 bis 1.0. Rate nicht: Wenn nichts lesbar ist, gib eine niedrige Confidence an.
        - description: ein Satz, wofür das Bauteil typischerweise verwendet wird.
        """;

    public async Task<RecognizedPart> RecognizeAsync(byte[] image, string mediaType, string? hint, CancellationToken ct)
    {
        var user = new ChatMessage(ChatRole.User, [
            new TextContent(string.IsNullOrWhiteSpace(hint) ? "Was ist das für ein Bauteil?" : $"Was ist das für ein Bauteil? Hinweis: {hint}"),
            new DataContent(image, mediaType),
        ]);

        var response = await vision.GetResponseAsync<RecognizedPart>(
            [new ChatMessage(ChatRole.System, SystemPrompt), user],
            new ChatOptions { Temperature = 0.1f },
            cancellationToken: ct);

        if (response.TryGetResult(out var result))
            return result;

        logger.LogWarning("Bauteilerkennung lieferte kein gültiges JSON: {Text}", response.Text);
        throw new InvalidOperationException("Das Modell hat keine auswertbare Antwort geliefert.");
    }

    public static string Serialize(RecognizedPart part) => JsonSerializer.Serialize(part, AIJsonUtilities.DefaultOptions);
}
