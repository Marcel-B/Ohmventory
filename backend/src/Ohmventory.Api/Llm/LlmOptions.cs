namespace Ohmventory.Api.Llm;

/// <summary>
/// Modelle und Grenzen für die LLM-Anfragen. Adresse und App-Name der LLM-Schranke stehen in <see cref="LlmGateOptions"/>.
/// </summary>
public class LlmOptions
{
    public const string Section = "Llm";

    /// <summary>LM-Studio-Schlüssel wie in <c>lms ls</c>; muss in /gate für Ohmventory freigegeben sein.</summary>
    public string VisionModel { get; set; } = "gemma-4-e4b-it-mlx";
    public string ChatModel { get; set; } = "gemma-4-e4b-it-mlx";

    /// <summary>Kontextlänge, mit der die Schranke das Modell lädt. Eingaben werden auf dieses Budget gekürzt.</summary>
    public int ContextLength { get; set; } = 8192;
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Wie lange eine Anfrage höchstens dauern darf, inklusive Warten in der Schlange der Schranke
    /// (dort bis <c>maxWaitMinutes</c>, Vorgabe 60 min) und erneuter Versuche nach 503.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(70);

    /// <summary>Wie lange bei abgebrochener Antwort oder nicht erreichbarer Schranke wiederholt wird.</summary>
    public TimeSpan RetryWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Nach 403, 424 oder 507 (nicht freigegeben, nicht installiert, zu groß) wird so lange nicht erneut gefragt,
    /// sondern sofort mit derselben Meldung abgelehnt.
    /// </summary>
    public TimeSpan ConfigErrorCooldown { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Datei, die den Pausenzustand über Neustarts hinweg festhält.</summary>
    public string StateFile { get; set; } = "data/llm-paused";

    /// <summary>Optionaler Schlüssel für /api/llm/pause und /resume (Header <c>X-Control-Token</c>).</summary>
    public string? ControlToken { get; set; }
}
