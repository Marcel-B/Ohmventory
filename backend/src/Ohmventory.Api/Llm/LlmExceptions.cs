namespace Ohmventory.Api.Llm;

public class LlmPausedException() : Exception("Das lokale LLM ist gerade pausiert, weil der Rechner für andere Arbeit gebraucht wird. Bitte später noch einmal versuchen.");

public class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Die LLM-Schranke lehnt die Anfrage aus einem Grund ab, den Wiederholen nicht behebt:
/// 403 (nicht angemeldet oder Modell nicht freigegeben), 424 (Modell nicht installiert), 507 (Modell zu groß).
/// </summary>
public class LlmGateRejectedException(int status, string model, string gateUrl, string reason, Exception? inner = null)
    : Exception(Describe(status, model, gateUrl, reason), inner)
{
    public int Status { get; } = status;

    private static string Describe(int status, string model, string gateUrl, string reason) => status switch
    {
        403 => $"Das Modell „{model}“ ist für Ohmventory noch nicht freigegeben. Freigeben unter {gateUrl}/gate. ({reason})",
        424 => $"Das Modell „{model}“ ist in LM Studio nicht installiert oder lädt noch herunter, siehe {gateUrl}/gate. ({reason})",
        507 => $"Das Modell „{model}“ ist größer als das Speicherbudget der LLM-Schranke. ({reason})",
        _ => $"Die LLM-Schranke lehnt die Anfrage ab (HTTP {status}): {reason}",
    };
}

public class LlmInputTooLargeException(int estimatedTokens, int budget)
    : Exception($"Die Anfrage ist mit geschätzt {estimatedTokens} Tokens zu groß für den Kontext des Modells ({budget} Tokens).");
