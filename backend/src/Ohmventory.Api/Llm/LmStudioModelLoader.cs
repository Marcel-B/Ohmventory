using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ohmventory.Api.Llm;

/// <summary>
/// Lädt das konfigurierte Modell über die LM-Studio-REST-API mit fester Kontextlänge, bevor es benutzt wird.
/// Ohne das würde JIT-Laden die maximale Kontextlänge des Modells nehmen und unnötig viel RAM belegen.
/// Andere Modelle werden nie angefasst und nichts wird entladen; das macht ausschließlich yue-guard.
/// </summary>
public class LmStudioModelLoader(HttpClient http, IOptions<LlmOptions> options, ILogger<LmStudioModelLoader> logger)
{
    // Statisch, weil der typisierte HttpClient pro Auflösung neu erzeugt wird.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _unsupported;

    public bool Enabled => !_unsupported && !string.Equals(options.Value.ManagementEndpoint, "off", StringComparison.OrdinalIgnoreCase);

    public static Uri ManagementBase(LlmOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.ManagementEndpoint))
            return new Uri(o.ManagementEndpoint.TrimEnd('/') + "/");
        var endpoint = o.Endpoint.TrimEnd('/');
        if (endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint[..^3];
        return new Uri(endpoint + "/");
    }

    public async Task<ModelInfo?> EnsureLoadedAsync(string model, CancellationToken ct)
    {
        if (!Enabled)
            return null;

        // Seriell, damit zwei gleichzeitige Anfragen nicht zwei Instanzen laden.
        await Gate.WaitAsync(ct);
        try
        {
            ModelList? list;
            try
            {
                list = await http.GetFromJsonAsync<ModelList>("api/v1/models", ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Kein LM Studio (oder eine alte Version): dann lädt der Server per JIT.
                logger.LogWarning("Keine LM-Studio-REST-API unter {Base}; Modell wird per JIT geladen", http.BaseAddress);
                _unsupported = true;
                return null;
            }
            var info = list?.Models.FirstOrDefault(m => string.Equals(m.Key, model, StringComparison.OrdinalIgnoreCase));
            if (info is null)
                throw new LlmUnavailableException($"Modell „{model}“ ist in LM Studio nicht vorhanden (lms ls).");
            if (info.LoadedInstances.Count > 0)
                return info;

            logger.LogInformation("Lade {Model} mit Kontextlänge {ContextLength}", model, options.Value.ContextLength);
            // Gepuffert statt gestreamt, damit der Request eine Content-Length hat.
            using var content = JsonContent.Create(new LoadRequest(info.Key, options.Value.ContextLength));
            await content.LoadIntoBufferAsync(ct);
            using var response = await http.PostAsync("api/v1/models/load", content, ct);
            response.EnsureSuccessStatusCode();
            return info;
        }
        finally
        {
            Gate.Release();
        }
    }

    public record ModelList([property: JsonPropertyName("models")] List<ModelInfo> Models);

    public record ModelInfo(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("loaded_instances")] List<LoadedInstance> LoadedInstances,
        [property: JsonPropertyName("capabilities")] Capabilities? Capabilities);

    public record LoadedInstance(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("config")] InstanceConfig? Config);

    public record InstanceConfig([property: JsonPropertyName("context_length")] int? ContextLength);

    public record Capabilities([property: JsonPropertyName("vision")] bool? Vision);

    private record LoadRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("context_length")] int ContextLength);
}
