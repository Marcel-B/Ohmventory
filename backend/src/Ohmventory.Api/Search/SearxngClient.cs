using System.Text.Json.Serialization;

namespace Ohmventory.Api.Search;

public class SearxngOptions
{
    public const string Section = "Searxng";
    public string BaseUrl { get; set; } = "http://localhost:8888";
    public int MaxResults { get; set; } = 6;
}

public record WebSearchResult(string Title, string Url, string? Snippet);

public class SearxngClient(HttpClient http, Microsoft.Extensions.Options.IOptions<SearxngOptions> options)
{
    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"search?format=json&q={Uri.EscapeDataString(query)}";
        var response = await http.GetFromJsonAsync<SearxngResponse>(url, ct);
        return response?.Results
            .Take(options.Value.MaxResults)
            .Select(r => new WebSearchResult(r.Title, r.Url, r.Content))
            .ToList() ?? [];
    }

    private record SearxngResponse([property: JsonPropertyName("results")] List<SearxngResult> Results);

    private record SearxngResult(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("content")] string? Content);
}
