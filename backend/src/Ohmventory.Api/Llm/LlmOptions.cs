namespace Ohmventory.Api.Llm;

/// <summary>
/// Anbindung an einen OpenAI-kompatiblen Server, standardmäßig LM Studio auf oi-o-mat.
/// </summary>
public class LlmOptions
{
    public const string Section = "Llm";

    public string Endpoint { get; set; } = "http://127.0.0.1:1234/v1";
    public string ApiKey { get; set; } = "lm-studio";

    /// <summary>LM-Studio-Schlüssel wie in <c>lms ls</c>; derselbe Schlüssel steht in MODELS der yue-guard-Konfiguration.</summary>
    public string VisionModel { get; set; } = "gemma-4-e4b-it-mlx";
    public string ChatModel { get; set; } = "gemma-4-e4b-it-mlx";

    /// <summary>Kontextlänge, mit der das Modell geladen wird. Eingaben werden auf dieses Budget gekürzt.</summary>
    public int ContextLength { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Basisadresse der LM-Studio-REST-API (<c>/api/v1/models</c>). Leer: aus <see cref="Endpoint"/> abgeleitet.
    /// "off" schaltet das eigene Laden ab, dann lädt LM Studio per JIT.
    /// </summary>
    public string? ManagementEndpoint { get; set; }

    /// <summary>Timeout einer einzelnen Anfrage, inklusive Laden des Modells.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Wie lange bei entladenem oder nicht erreichbarem Modell wiederholt wird.</summary>
    public TimeSpan RetryWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Datei, die den Pausenzustand über Neustarts hinweg festhält.</summary>
    public string StateFile { get; set; } = "data/llm-paused";

    /// <summary>Optionaler Schlüssel für /api/llm/pause und /resume (Header <c>X-Control-Token</c>).</summary>
    public string? ControlToken { get; set; }
}
