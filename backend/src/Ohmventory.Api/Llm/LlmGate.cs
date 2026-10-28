// Nach dem Adapter aus Marcel-B/scripts (examples/adapters/LlmGate.cs, docs/llm-gate.md).
// Meldet Ohmventory beim Start mit seinen Modellen bei der LLM-Schranke auf oi-o-mat an und versucht es weiter,
// solange die Schranke nicht antwortet. Die Modell-Anfragen gehen an Url + "/v1" mit dem App-Namen als API-Key.
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Ohmventory.Api.Llm;

public sealed class LlmGateOptions
{
    public const string Section = "LlmGate";

    /// <summary>Aus dem Heimnetz http://192.168.2.122:1235, auf oi-o-mat selbst http://127.0.0.1:1235.</summary>
    public string Url { get; set; } = "http://127.0.0.1:1235";

    /// <summary>Fester Name der App; an ihm hängen die Freigaben in /gate. Zugleich der API-Key.</summary>
    public string App { get; set; } = "";
    public string? Note { get; set; }
    public int? Priority { get; set; }
    public string? HoldFor { get; set; }
    public List<LlmGateModel> Models { get; set; } = [];

    public Uri ApiEndpoint => new(Url.TrimEnd('/') + "/v1");

    /// <summary>
    /// Die anzumeldenden Modelle: die konfigurierten, dazu Chat- und Vision-Modell, falls sie dort fehlen.
    /// Ohne eigene Kontextlänge gilt <see cref="LlmOptions.ContextLength"/>, auf die das Backend die Eingaben kürzt.
    /// </summary>
    public List<LlmGateModel> ModelsFor(LlmOptions llm)
    {
        var models = Models.Where(m => !string.IsNullOrWhiteSpace(m.Model)).ToList();
        foreach (var key in new[] { llm.ChatModel, llm.VisionModel })
        {
            if (!string.IsNullOrWhiteSpace(key) && !models.Any(m => string.Equals(m.Model, key, StringComparison.OrdinalIgnoreCase)))
                models.Add(new LlmGateModel { Model = key });
        }
        return models.Select(m => m with { Context = m.Context ?? llm.ContextLength }).ToList();
    }
}

public sealed record LlmGateModel
{
    public string Model { get; init; } = "";
    public string? Source { get; init; }
    public int? Context { get; init; }
    public double? RamGb { get; init; }
    public double? SizeGb { get; init; }
}

public sealed class LlmGateRegistration(
    IHttpClientFactory clients,
    IOptions<LlmGateOptions> gateOptions,
    IOptions<LlmOptions> llmOptions,
    ILogger<LlmGateRegistration> logger) : BackgroundService
{
    public const string HttpClientName = "llm-gate";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var gate = gateOptions.Value;
        var delay = TimeSpan.FromSeconds(10);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Null-Felder lässt die Schranke weg; was Marcel in der Oberfläche festgelegt hat, bleibt ohnehin.
                var body = new
                {
                    note = gate.Note,
                    priority = gate.Priority,
                    holdFor = gate.HoldFor,
                    models = gate.ModelsFor(llmOptions.Value)
                        .Select(m => new { model = m.Model, source = m.Source, context = m.Context, ramGb = m.RamGb, sizeGb = m.SizeGb }),
                };
                var response = await clients.CreateClient(HttpClientName)
                    .PutAsJsonAsync($"{gate.Url.TrimEnd('/')}/gate/apps/{Uri.EscapeDataString(gate.App)}", body, stoppingToken);
                response.EnsureSuccessStatusCode();
                logger.LogInformation("Bei der LLM-Schranke als {App} angemeldet; Modelle freigeben unter {Url}/gate", gate.App, gate.Url);
                return;
            }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("LLM-Schranke nicht erreichbar ({Message}); neuer Versuch in {Delay}", error.Message, delay);
                await Task.Delay(delay, stoppingToken);
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 600));
            }
        }
    }
}
