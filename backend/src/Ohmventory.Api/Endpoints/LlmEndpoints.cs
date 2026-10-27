using Microsoft.Extensions.Options;
using Ohmventory.Api.Llm;

namespace Ohmventory.Api.Endpoints;

public record LlmStatusDto(bool Paused, DateTimeOffset? PausedSince, string VisionModel, string ChatModel, int ContextLength);

public static class LlmEndpoints
{
    public static void MapLlmEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/llm").WithTags("LLM");

        group.MapGet("/status", (LlmPauseState pause, IOptions<LlmOptions> options) =>
            new LlmStatusDto(pause.IsPaused, pause.PausedSince, options.Value.VisionModel, options.Value.ChatModel, options.Value.ContextLength));

        // Für yue-guard: beide Aufrufe sind idempotent und dürfen beliebig oft wiederholt werden.
        group.MapPost("/pause", (HttpRequest request, LlmPauseState pause, IOptions<LlmOptions> options) =>
        {
            if (!Authorized(request, options.Value))
                return Results.Unauthorized();
            pause.Pause();
            return Results.NoContent();
        });

        group.MapPost("/resume", (HttpRequest request, LlmPauseState pause, IOptions<LlmOptions> options) =>
        {
            if (!Authorized(request, options.Value))
                return Results.Unauthorized();
            pause.Resume();
            return Results.NoContent();
        });
    }

    private static bool Authorized(HttpRequest request, LlmOptions options) =>
        string.IsNullOrEmpty(options.ControlToken) || request.Headers["X-Control-Token"] == options.ControlToken;

    /// <summary>Übersetzt LLM-Fehler in eine verständliche Antwort für die App.</summary>
    public static ProblemHttpResult Problem(string title, Exception ex) => ex switch
    {
        LlmPausedException => TypedResults.Problem(title: "LLM pausiert", detail: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable),
        LlmInputTooLargeException => TypedResults.Problem(title: title, detail: ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity),
        _ => TypedResults.Problem(
            title: title,
            detail: $"Das lokale LLM war nicht erreichbar oder lieferte keine gültige Antwort: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway),
    };
}
