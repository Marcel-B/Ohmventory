namespace Ohmventory.Api.Llm;

public class LlmPausedException() : Exception("Das lokale LLM ist gerade pausiert, weil der Rechner für andere Arbeit gebraucht wird. Bitte später noch einmal versuchen.");

public class LlmUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public class LlmInputTooLargeException(int estimatedTokens, int budget)
    : Exception($"Die Anfrage ist mit geschätzt {estimatedTokens} Tokens zu groß für den Kontext des Modells ({budget} Tokens).");
