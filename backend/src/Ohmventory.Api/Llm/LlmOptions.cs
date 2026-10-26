namespace Ohmventory.Api.Llm;

/// <summary>
/// Anbindung an einen OpenAI-kompatiblen Server (LM Studio, Ollama, llama.cpp, ...).
/// </summary>
public class LlmOptions
{
    public const string Section = "Llm";

    public string Endpoint { get; set; } = "http://localhost:1234/v1";
    public string ApiKey { get; set; } = "lm-studio";
    public string VisionModel { get; set; } = "gemma-4-26b-a4b-it-qat-mlx";
    public string ChatModel { get; set; } = "gemma-4-26b-a4b-it-qat-mlx";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);
}
