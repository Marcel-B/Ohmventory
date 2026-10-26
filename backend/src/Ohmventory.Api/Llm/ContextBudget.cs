using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Ohmventory.Api.Llm;

/// <summary>
/// Grobe Token-Schätzung und Kürzung, damit keine Anfrage den festen Kontext überschreitet.
/// LM Studio lehnt solche Anfragen ab, und Wiederholen würde nichts ändern.
/// </summary>
public static class ContextBudget
{
    // Deutsch mit vielen Typbezeichnungen: eher 3 als 4 Zeichen pro Token, lieber zu vorsichtig.
    public const double CharsPerToken = 3.0;
    public const int TokensPerImage = 300;
    public const int TokensPerMessage = 8;
    private const int ShortenedToolResultChars = 400;

    public static int EstimateTokens(string? text) => text is null ? 0 : (int)Math.Ceiling(text.Length / CharsPerToken);

    public static int EstimateTokens(IEnumerable<ChatMessage> messages) =>
        messages.Sum(m => TokensPerMessage + m.Contents.Sum(EstimateTokens));

    public static int EstimateTokens(ChatOptions? options) =>
        options?.Tools?.OfType<AIFunction>().Sum(t => EstimateTokens(t.Name + t.Description + t.JsonSchema.GetRawText())) ?? 0;

    private static int EstimateTokens(AIContent content) => content switch
    {
        TextContent t => EstimateTokens(t.Text),
        DataContent d when d.HasTopLevelMediaType("image") => TokensPerImage,
        FunctionCallContent c => EstimateTokens(c.Name) + EstimateTokens(JsonSerializer.Serialize(c.Arguments, AIJsonUtilities.DefaultOptions)),
        FunctionResultContent r => EstimateTokens(ResultText(r)),
        _ => 0,
    };

    /// <summary>Kürzt Text auf ungefähr <paramref name="maxTokens"/> Tokens.</summary>
    public static string Truncate(string text, int maxTokens)
    {
        var maxChars = (int)(maxTokens * CharsPerToken);
        return text.Length <= maxChars ? text : text[..Math.Max(maxChars - 1, 0)] + "…";
    }

    /// <summary>
    /// Passt die Nachrichten in das Budget ein, indem Werkzeugergebnisse von alt nach neu gekürzt werden.
    /// Reicht das nicht, wird <see cref="LlmInputTooLargeException"/> geworfen.
    /// </summary>
    public static List<ChatMessage> Fit(IEnumerable<ChatMessage> messages, int budgetTokens)
    {
        // Clone() kopiert flach; die Inhaltsliste wird eigens kopiert, damit der Aufrufer unverändert bleibt.
        var result = messages.Select(m =>
        {
            var copy = m.Clone();
            copy.Contents = [.. m.Contents];
            return copy;
        }).ToList();
        var total = EstimateTokens(result);
        if (total <= budgetTokens)
            return result;

        foreach (var message in result.Where(m => m.Role == ChatRole.Tool))
        {
            for (var i = 0; i < message.Contents.Count && total > budgetTokens; i++)
            {
                if (message.Contents[i] is not FunctionResultContent r)
                    continue;
                var text = ResultText(r);
                if (text.Length <= ShortenedToolResultChars)
                    continue;
                var shortened = text[..ShortenedToolResultChars] + "… (gekürzt)";
                total -= EstimateTokens(text) - EstimateTokens(shortened);
                message.Contents[i] = new FunctionResultContent(r.CallId, shortened);
            }
        }

        if (total > budgetTokens)
            throw new LlmInputTooLargeException(total, budgetTokens);
        return result;
    }

    private static string ResultText(FunctionResultContent r) =>
        r.Result as string ?? JsonSerializer.Serialize(r.Result, AIJsonUtilities.DefaultOptions);
}
