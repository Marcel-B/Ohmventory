using System.Text.RegularExpressions;
using Ohmventory.Api.Domain;

namespace Ohmventory.Api.Assistant;

/// <summary>
/// Gleicht eine vom LLM genannte Bauteilbezeichnung mit dem Bestand ab.
/// Bewusst deterministisch, damit "habe ich" / "fehlt" nicht vom Modell abhängt.
/// </summary>
public static partial class InventoryMatcher
{
    public const double Threshold = 0.5;

    public static double Score(Part part, string name, string? partNumber)
    {
        var partName = Normalize(part.Name);
        var partPn = Normalize(part.PartNumber);
        var queryName = Normalize(name);
        var queryPn = Normalize(partNumber);

        if (queryPn.Length >= 3 && (queryPn == partPn || partName.Contains(queryPn)))
            return 1.0;
        if (partPn.Length >= 3 && (queryName.Contains(partPn) || queryPn.Contains(partPn)))
            return 0.95;
        if (queryName.Length > 0 && queryName == partName)
            return 0.9;

        var queryTokens = Tokens(name, partNumber);
        var partTokens = Tokens([part.Name, part.PartNumber, ..part.Tags]);
        if (queryTokens.Count == 0 || partTokens.Count == 0)
            return 0;

        var overlap = queryTokens.Intersect(partTokens).Count();
        var jaccard = (double)overlap / queryTokens.Union(partTokens).Count();
        return jaccard * 0.8;
    }

    public static IReadOnlyList<(Part Part, double Score)> FindMatches(IEnumerable<Part> parts, string name, string? partNumber) =>
        parts.Select(p => (Part: p, Score: Score(p, name, partNumber)))
            .Where(m => m.Score >= Threshold)
            .OrderByDescending(m => m.Score)
            .ToList();

    private static string Normalize(string? value) =>
        value is null ? "" : NonAlphanumeric().Replace(value.ToLowerInvariant(), "");

    private static HashSet<string> Tokens(params string?[] values) =>
        values.Where(v => v is not null)
            .SelectMany(v => TokenSplit().Split(v!.ToLowerInvariant()))
            .Where(t => t.Length >= 2)
            .ToHashSet();

    [GeneratedRegex("[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("[^a-z0-9äöüß]+")]
    private static partial Regex TokenSplit();
}
