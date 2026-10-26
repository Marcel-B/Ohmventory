using System.ComponentModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Ohmventory.Api.Llm;
using Ohmventory.Api.Search;

namespace Ohmventory.Api.Assistant;

public record SuggestedPart(string Name, string? PartNumber, int Quantity, bool Optional);

public record SuggestedProject(string Name, string Description, string Difficulty, List<SuggestedPart> Parts, List<string> Sources);

public record ProjectSuggestions(List<SuggestedProject> Projects);

public record PartAvailability(
    string Name,
    string? PartNumber,
    int QuantityRequired,
    bool Optional,
    Guid? MatchedPartId,
    string? MatchedPartName,
    int QuantityInStock,
    IReadOnlyList<string> Containers,
    int QuantityMissing);

public record ProjectProposal(
    string Name,
    string Description,
    string Difficulty,
    List<string> Sources,
    IReadOnlyList<PartAvailability> Parts);

public record AssistantAnswer(string Question, string Research, IReadOnlyList<ProjectProposal> Projects);

public class ProjectAssistantService(
    [FromKeyedServices(LlmServiceCollectionExtensions.ChatClientKey)] IChatClient chat,
    InventoryQueries inventory,
    SearxngClient search,
    IOptions<LlmOptions> llmOptions,
    ILogger<ProjectAssistantService> logger)
{
    private const int MaxOverviewEntries = 150;
    private const int MaxSnippetChars = 300;

    private const string ResearchPrompt = """
        Du bist ein Elektronik-Bastelassistent. Der Nutzer hat ein Lager mit Elektronikbauteilen.
        Nutze die Werkzeuge:
        - inventory_overview: was ist insgesamt im Lager (für offene Fragen wie "Was kann ich bauen?").
        - search_inventory: gezielt prüfen, ob ein Bauteil vorhanden ist.
        - web_search: Projektideen, Schaltungen und Stücklisten im Web recherchieren.
        Schlage 1 bis 3 konkrete, realistische Projekte vor und bevorzuge Bauteile, die schon im Lager sind.
        Nenne für jedes Projekt eine vollständige Stückliste mit Typbezeichnungen und Mengen
        (inkl. Kleinteile wie Widerstände, aber ohne Werkzeug, Kabel und Lötzinn) und die genutzten Quellen-URLs.
        Antworte auf Deutsch.
        """;

    private const string StructurePrompt = """
        Überführe die folgende Recherche in strukturierte Projektvorschläge.
        Übernimm nur Projekte und Bauteile, die in der Recherche stehen. difficulty ist "leicht", "mittel" oder "schwer".
        """;

    public async Task<AssistantAnswer> AskAsync(string question, CancellationToken ct)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(
                async ([Description("Suchbegriff, z. B. Typbezeichnung oder Bauteilart")] string query) =>
                    await inventory.SearchAsync(query, ct),
                "search_inventory",
                "Sucht im Bauteillager und liefert Treffer mit Menge und Container."),
            AIFunctionFactory.Create(
                async () => CompactOverview(await inventory.OverviewAsync(ct)),
                "inventory_overview",
                "Liefert alle Bauteile im Lager mit Menge."),
            AIFunctionFactory.Create(
                async ([Description("Suchanfrage für die Websuche")] string query) =>
                {
                    try
                    {
                        var results = await search.SearchAsync(query, ct);
                        return (object)results.Select(r => r with { Snippet = Shorten(r.Snippet, MaxSnippetChars) }).ToList();
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Websuche fehlgeschlagen");
                        return "Websuche nicht verfügbar.";
                    }
                },
                "web_search",
                "Durchsucht das Web und liefert Titel, URL und Ausschnitt der Treffer."),
        };

        var research = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, ResearchPrompt), new ChatMessage(ChatRole.User, question)],
            new ChatOptions { Tools = tools, Temperature = 0.4f },
            ct);

        // Die Recherche kann lang werden; für die Strukturierung muss sie samt Schema in den Kontext passen.
        var o = llmOptions.Value;
        var researchBudget = o.ContextLength - o.MaxOutputTokens - 1500;
        var researchText = ContextBudget.Truncate(research.Text, researchBudget);

        var structured = await chat.GetResponseAsync<ProjectSuggestions>(
            [new ChatMessage(ChatRole.System, StructurePrompt), new ChatMessage(ChatRole.User, researchText)],
            new ChatOptions { Temperature = 0.0f },
            cancellationToken: ct);

        if (!structured.TryGetResult(out var suggestions))
        {
            logger.LogWarning("Strukturierung der Projektvorschläge fehlgeschlagen: {Text}", structured.Text);
            return new AssistantAnswer(question, research.Text, []);
        }

        var parts = await inventory.LoadPartsWithStockAsync(ct);
        var proposals = suggestions.Projects
            .Select(p => new ProjectProposal(p.Name, p.Description, p.Difficulty, p.Sources,
                p.Parts.Select(sp => CheckAvailability(sp, parts)).ToList()))
            .ToList();

        return new AssistantAnswer(question, research.Text, proposals);
    }

    /// <summary>Kompakte Zeilen statt JSON, damit auch ein großes Lager in den Kontext passt.</summary>
    public static string CompactOverview(IReadOnlyList<InventoryEntry> entries)
    {
        var lines = entries.Take(MaxOverviewEntries)
            .Select(e => $"{e.Name}{(e.PartNumber is null || e.Name.Contains(e.PartNumber, StringComparison.OrdinalIgnoreCase) ? "" : $" ({e.PartNumber})")}: {e.Quantity}");
        var text = string.Join('\n', lines);
        return entries.Count > MaxOverviewEntries
            ? $"{text}\n… und {entries.Count - MaxOverviewEntries} weitere Bauteile (gezielt mit search_inventory suchen)"
            : text.Length == 0 ? "Das Lager ist leer." : text;
    }

    private static string? Shorten(string? text, int maxChars) =>
        text is null || text.Length <= maxChars ? text : text[..maxChars] + "…";

    public static PartAvailability CheckAvailability(SuggestedPart needed, IEnumerable<Domain.Part> parts)
    {
        var quantity = Math.Max(needed.Quantity, 1);
        var match = InventoryMatcher.FindMatches(parts, needed.Name, needed.PartNumber).FirstOrDefault();
        if (match.Part is null)
            return new PartAvailability(needed.Name, needed.PartNumber, quantity, needed.Optional, null, null, 0, [], quantity);

        var entry = InventoryQueries.ToEntry(match.Part);
        return new PartAvailability(
            needed.Name, needed.PartNumber, quantity, needed.Optional,
            entry.PartId, entry.Name, entry.Quantity, entry.Containers,
            Math.Max(quantity - entry.Quantity, 0));
    }
}
