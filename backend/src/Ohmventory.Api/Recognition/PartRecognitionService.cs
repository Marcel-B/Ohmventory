using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Ohmventory.Api.Domain;
using Ohmventory.Api.Llm;
using Ohmventory.Api.Search;

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
    string? Observations,
    string? WebFinding,
    List<WebSource> Sources);

public record WebSource(string Title, string Url);

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

/// <summary>Nur die Aufdrucke, für einen eigenen Lese-Durchgang mit gedrehten Ansichten.</summary>
public record MarkingReading(
    [property: Description("Jeder lesbare Aufdruck, wörtlich, in jeder Ansicht und jeder Drehung")] List<string>? Text);

/// <summary>Antwort des Textmodells nach der Websuche.</summary>
public record WebRefinement(
    [property: Description("Ob die Suchergebnisse zu den Aufdrucken und Merkmalen passen")] bool Matches,
    [property: Description("Ein bis zwei Sätze: Welches Ergebnis passt, und warum")] string? Explanation,
    string Name,
    PartCategory Category,
    string? Manufacturer,
    [property: Description("Typbezeichnung des Moduls oder seines Haupt-Chips, z. B. SH1106; null, wenn die Ergebnisse uneinig sind")] string? PartNumber,
    string? Description,
    List<string>? Alternatives,
    [property: Description("Nummern (1-basiert) der Suchergebnisse, die die Antwort stützen")] List<int>? Sources,
    double Confidence);

public partial class PartRecognitionService(
    [FromKeyedServices(LlmServiceCollectionExtensions.VisionClientKey)] IChatClient vision,
    [FromKeyedServices(LlmServiceCollectionExtensions.ChatClientKey)] IChatClient chat,
    SearxngClient search,
    ILogger<PartRecognitionService> logger)
{
    /// <summary>Höchste Sicherheit, wenn keine gelesene Typbezeichnung die Antwort stützt.</summary>
    public const double MaxConfidenceWithoutPartNumber = 0.7;

    /// <summary>Höchste Sicherheit, wenn nur die Websuche (nicht der Aufdruck selbst) die Typbezeichnung liefert.</summary>
    public const double MaxConfidenceFromWeb = 0.85;

    private const int SnippetChars = 300;

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

    private const string ReadPrompt = """
        Du liest Aufdrucke auf Platinen. Du bekommst dasselbe Foto in mehreren Ansichten (original und gedreht),
        damit auch senkrecht gedruckter Text waagerecht zu lesen ist.
        Schreibe jeden lesbaren Aufdruck wörtlich in text, zum Beispiel Modulnamen, Versionsnummern, Zoll-Angaben,
        Chip-Bezeichnungen und Pin-Beschriftungen. Text, der in mehreren Ansichten vorkommt, nur einmal.
        Bestückungsdrucke wie R1, C3, D1 kannst du weglassen. Nichts erfinden: Was du nicht sicher lesen kannst, lass weg.
        """;

    private const string RefinePrompt = """
        Du bestimmst ein Elektronikmodul anhand der Aufdrucke auf seiner Platine und den Ergebnissen einer Websuche.
        Die erste Einschätzung stammt von einem Bildmodell und kann zu allgemein oder falsch sein.

        Regeln:
        - matches nur true, wenn mindestens ein Suchergebnis zu den Aufdrucken UND zu den sichtbaren Merkmalen passt.
          Sonst false, und übernimm die erste Einschätzung unverändert.
        - partNumber: die Typbezeichnung, die die passenden Ergebnisse nennen (bei Displays der Controller, z. B. SH1106).
          Nennen die Ergebnisse verschiedene Chips, partNumber null und die Kandidaten in alternatives.
        - sources: die Nummern der Ergebnisse, auf die du dich stützt.
        - confidence: 0.8 bis 0.85, wenn mehrere Ergebnisse übereinstimmen; 0.6 bis 0.75 bei einem passenden Ergebnis.
        - name auf Deutsch, z. B. "1,3-Zoll-OLED-Display 128×64 (I²C, SH1106)". description: ein Satz zum Einsatzzweck.
        """;

    /// <param name="views">Weitere Ansichten desselben Fotos (z. B. um 90° gedreht) für den Lese-Durchgang.</param>
    public async Task<RecognizedPart> RecognizeAsync(byte[] image, string mediaType, IReadOnlyList<(byte[] Data, string MediaType)> views, string? hint, CancellationToken ct)
    {
        var user = new ChatMessage(ChatRole.User, [
            new TextContent(string.IsNullOrWhiteSpace(hint) ? "Was ist das für ein Bauteil?" : $"Was ist das für ein Bauteil? Hinweis von mir: {hint}"),
            new DataContent(image, mediaType),
        ]);

        var response = await vision.GetResponseAsync<VisionAnalysis>(
            [new ChatMessage(ChatRole.System, SystemPrompt), user],
            new ChatOptions { Temperature = 0.1f },
            cancellationToken: ct);

        if (!response.TryGetResult(out var analysis))
        {
            logger.LogWarning("Bauteilerkennung lieferte kein gültiges JSON: {Text}", response.Text);
            throw new InvalidOperationException("Das Modell hat keine auswertbare Antwort geliefert.");
        }

        var part = Finalize(analysis);
        if (PartNumberWasRead(part))
            return part;

        // Ab hier ist alles eine Verbesserung: Scheitert ein Schritt, bleibt das Ergebnis aus Stufe 1 stehen.
        try
        {
            if (!Markings.HasDistinctive(part.MarkingsRead) && views.Count > 0)
                part = part with { MarkingsRead = MergeMarkings(part.MarkingsRead, await ReadMarkingsAsync(image, mediaType, views, ct)) };

            return await RefineWithWebAsync(part, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Zweite Erkennungsstufe übersprungen");
            return part;
        }
    }

    private async Task<List<string>> ReadMarkingsAsync(byte[] image, string mediaType, IReadOnlyList<(byte[] Data, string MediaType)> views, CancellationToken ct)
    {
        List<AIContent> contents = [new TextContent("Lies alle Aufdrucke."), new DataContent(image, mediaType)];
        contents.AddRange(views.Select(v => new DataContent(v.Data, v.MediaType)));

        var response = await vision.GetResponseAsync<MarkingReading>(
            [new ChatMessage(ChatRole.System, ReadPrompt), new ChatMessage(ChatRole.User, contents)],
            new ChatOptions { Temperature = 0.0f },
            cancellationToken: ct);

        return response.TryGetResult(out var reading) ? reading.Text ?? [] : [];
    }

    private async Task<RecognizedPart> RefineWithWebAsync(RecognizedPart part, CancellationToken ct)
    {
        var query = Markings.BuildQuery(part.MarkingsRead);
        if (query is null)
            return part;

        var results = await search.SearchAsync(query, ct);
        if (results.Count == 0)
            return part;

        var listing = string.Join("\n", results.Select((r, i) =>
            $"[{i + 1}] {r.Title}\n{r.Url}\n{ContextBudget.Truncate(r.Snippet ?? "", SnippetChars / 3)}"));

        var prompt = $"""
            Aufdrucke auf der Platine: {string.Join(" | ", part.MarkingsRead)}
            Sichtbare Merkmale: {part.Observations}
            Erste Einschätzung: {part.Name} ({part.Category}), Kandidaten: {string.Join(", ", part.Alternatives)}

            Websuche nach "{query}":
            {listing}
            """;

        var response = await chat.GetResponseAsync<WebRefinement>(
            [new ChatMessage(ChatRole.System, RefinePrompt), new ChatMessage(ChatRole.User, prompt)],
            new ChatOptions { Temperature = 0.1f },
            cancellationToken: ct);

        return response.TryGetResult(out var refinement) ? Apply(part, refinement, results) : part;
    }

    /// <summary>
    /// Übernimmt das Ergebnis der Websuche, wenn es passt. Quellen nur aus den echten Suchergebnissen,
    /// und eine Typbezeichnung aus dem Web ist nie so sicher wie eine gelesene.
    /// </summary>
    public static RecognizedPart Apply(RecognizedPart part, WebRefinement r, IReadOnlyList<WebSearchResult> results)
    {
        var sources = (r.Sources ?? [])
            .Where(i => i >= 1 && i <= results.Count)
            .Distinct()
            .Select(i => new WebSource(results[i - 1].Title, results[i - 1].Url))
            .ToList();

        if (!r.Matches || sources.Count == 0 || string.IsNullOrWhiteSpace(r.Name))
            return part;

        // Liefert die Suche keine eindeutige Typbezeichnung, ist eine ungelesene aus Stufe 1 erst recht nicht belastbar.
        var partNumber = string.IsNullOrWhiteSpace(r.PartNumber) ? null : r.PartNumber.Trim();
        var alternatives = (r.Alternatives ?? [])
            .Where(a => !string.IsNullOrWhiteSpace(a) && !string.Equals(a, partNumber, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return part with
        {
            Name = r.Name.Trim(),
            Category = r.Category == PartCategory.Other ? part.Category : r.Category,
            Manufacturer = r.Manufacturer ?? part.Manufacturer,
            PartNumber = partNumber,
            Description = r.Description ?? part.Description,
            Alternatives = alternatives,
            Confidence = Math.Max(part.Confidence, Math.Clamp(r.Confidence, 0, MaxConfidenceFromWeb)),
            WebFinding = r.Explanation,
            Sources = sources,
        };
    }

    /// <summary>
    /// Räumt die Modellantwort auf: Positionsnummern raus, und ohne gelesene Typbezeichnung keine hohe Sicherheit.
    /// </summary>
    public static RecognizedPart Finalize(VisionAnalysis a)
    {
        var markings = MergeMarkings([], a.VisibleText ?? []);

        var partNumber = string.IsNullOrWhiteSpace(a.PartNumber) ? null : a.PartNumber.Trim();
        var confidence = Math.Clamp(a.Confidence, 0, 1);
        var part = new RecognizedPart(
            a.Name, a.Category, a.Manufacturer, partNumber, a.Package, a.Description, a.IsBreakoutBoard,
            markings, a.Alternatives ?? [], confidence, a.Observations, null, []);

        return PartNumberWasRead(part) ? part : part with { Confidence = Math.Min(confidence, MaxConfidenceWithoutPartNumber) };
    }

    public static List<string> MergeMarkings(IEnumerable<string> existing, IEnumerable<string> additional) =>
        existing.Concat(additional)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && !Markings.IsDesignator(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool PartNumberWasRead(RecognizedPart part) =>
        part.PartNumber is { } number && part.MarkingsRead.Any(m => Normalize(m).Contains(Normalize(number)));

    public static string Serialize(RecognizedPart part) => JsonSerializer.Serialize(part, AIJsonUtilities.DefaultOptions);

    private static string Normalize(string value) => NonAlphanumeric().Replace(value.ToLowerInvariant(), "");

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();
}
