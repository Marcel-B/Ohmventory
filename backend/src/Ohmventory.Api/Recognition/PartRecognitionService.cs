using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
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
    double Confidence,
    string? Observations);

/// <summary>
/// Antwort des Vision-Modells. Die Reihenfolge ist Absicht: Das Modell schreibt erst auf, was es liest und sieht,
/// und legt sich erst danach fest. Bei kleinen Modellen verhindert das voreilige Schlüsse.
/// </summary>
public record VisionAnalysis(
    [property: Description("Jeder lesbare Aufdruck auf Chips und Platine, wörtlich, auch Pin-Beschriftungen")] List<string>? VisibleText,
    [property: Description("Sichtbare Merkmale: Chips, Steckverbinder, Flachbandkabel, Antenne, Abschirmblech, Vorder- oder Rückseite")] string? Observations,
    string Name,
    PartCategory Category,
    string? Manufacturer,
    string? PartNumber,
    string? Package,
    string? Description,
    bool IsBreakoutBoard,
    List<string>? Alternatives,
    double Confidence);

public partial class PartRecognitionService(
    [FromKeyedServices(LlmServiceCollectionExtensions.VisionClientKey)] IChatClient vision,
    ILogger<PartRecognitionService> logger)
{
    /// <summary>Höchste Sicherheit, wenn keine gelesene Typbezeichnung die Antwort stützt.</summary>
    public const double MaxConfidenceWithoutPartNumber = 0.7;

    private const string SystemPrompt = """
        Du bist Experte für Elektronikbauteile und Bastelmodule (Arduino, ESP, Raspberry Pi).
        Du bekommst ein Foto eines einzelnen Bauteils oder eines Moduls/Breakout-Boards. Es kann die Vorder- oder die Rückseite zeigen.

        Gehe in dieser Reihenfolge vor:
        1. visibleText: Lies jeden Aufdruck wörtlich ab, auch kleinen und gedrehten Text.
        2. observations: Beschreibe kurz, was du siehst und welche Seite das Foto zeigt.
        3. Erst dann: Bestimme das Bauteil aus diesen Hinweisen.

        Wichtige Regeln:
        - Bestückungsdrucke wie R1, C3, D1, U2, Q1 sind nur Positionsnummern und sagen nichts über das Modul.
        - Die stärksten Hinweise sind Modulaufdrucke (z. B. "GY-521", "HW-201", "1.30\" IIC V2.2", "ESP32-WROOM-32")
          und Pin-Beschriftungen. VCC/VDD, GND, SCL/SCK, SDA heißt I²C-Modul; MOSI/MISO/CS heißt SPI.
        - Eine Zoll-Angabe wie 0.96" oder 1.3" auf der Platine plus ein breites goldenes Flachbandkabel (FPC)
          mit vielen feinen Leiterbahnen bedeutet ein Display-Modul (meist OLED), fotografiert von der Rückseite.
        - Nenne ein ESP32/ESP8266 nur, wenn "ESP" lesbar ist oder das typische Metallmodul mit Leiterbahn-Antenne sichtbar ist.
        - partNumber nur, wenn die Typbezeichnung lesbar ist. Sonst null, und typische Kandidaten (z. B. SSD1306, SH1106)
          gehören in alternatives.
        - confidence: 0.9 oder mehr nur, wenn eine Typbezeichnung lesbar ist; 0.6 bis 0.8 bei eindeutigen Merkmalen ohne
          Typbezeichnung; unter 0.5, wenn du rätst.
        - name: kurze, gängige Bezeichnung auf Deutsch, z. B. "1,3-Zoll-OLED-Display 128×64 (I²C)".
        - description: ein Satz, wofür das Bauteil typischerweise verwendet wird.
        """;

    public async Task<RecognizedPart> RecognizeAsync(byte[] image, string mediaType, string? hint, CancellationToken ct)
    {
        var user = new ChatMessage(ChatRole.User, [
            new TextContent(string.IsNullOrWhiteSpace(hint) ? "Was ist das für ein Bauteil?" : $"Was ist das für ein Bauteil? Hinweis von mir: {hint}"),
            new DataContent(image, mediaType),
        ]);

        var response = await vision.GetResponseAsync<VisionAnalysis>(
            [new ChatMessage(ChatRole.System, SystemPrompt), user],
            new ChatOptions { Temperature = 0.1f },
            cancellationToken: ct);

        if (response.TryGetResult(out var result))
            return Finalize(result);

        logger.LogWarning("Bauteilerkennung lieferte kein gültiges JSON: {Text}", response.Text);
        throw new InvalidOperationException("Das Modell hat keine auswertbare Antwort geliefert.");
    }

    /// <summary>
    /// Räumt die Modellantwort auf: Positionsnummern raus, und ohne gelesene Typbezeichnung keine hohe Sicherheit.
    /// </summary>
    public static RecognizedPart Finalize(VisionAnalysis a)
    {
        var markings = (a.VisibleText ?? [])
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && !Designator().IsMatch(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var partNumber = string.IsNullOrWhiteSpace(a.PartNumber) ? null : a.PartNumber.Trim();
        var partNumberRead = partNumber is not null && markings.Any(m => Normalize(m).Contains(Normalize(partNumber)));
        var confidence = Math.Clamp(a.Confidence, 0, 1);
        if (!partNumberRead)
            confidence = Math.Min(confidence, MaxConfidenceWithoutPartNumber);

        return new RecognizedPart(
            a.Name, a.Category, a.Manufacturer, partNumber, a.Package, a.Description, a.IsBreakoutBoard,
            markings, a.Alternatives ?? [], confidence, a.Observations);
    }

    public static string Serialize(RecognizedPart part) => JsonSerializer.Serialize(part, AIJsonUtilities.DefaultOptions);

    private static string Normalize(string value) => NonAlphanumeric().Replace(value.ToLowerInvariant(), "");

    // R1, C12, D1, U3, Q2, L1, J1, SW1, LED1 ...
    [GeneratedRegex(@"^(R|C|D|U|Q|L|J|P|SW|LED|IC|Y|F|K|T)\d{1,3}$", RegexOptions.IgnoreCase)]
    private static partial Regex Designator();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
