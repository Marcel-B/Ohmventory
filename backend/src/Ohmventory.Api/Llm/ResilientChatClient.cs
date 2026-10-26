using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ohmventory.Api.Llm;

/// <summary>
/// Sitzt direkt vor dem LM-Studio-Client: prüft den Pausenzustand, lädt das Modell mit fester Kontextlänge,
/// hält das Kontextbudget ein und wiederholt mit Backoff, wenn das Modell zwischendurch entladen wurde.
/// </summary>
public class ResilientChatClient(
    IChatClient inner,
    string model,
    LlmPauseState pause,
    LmStudioModelLoader loader,
    IOptions<LlmOptions> options,
    ILogger<ResilientChatClient> logger) : DelegatingChatClient(inner)
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)];

    /// <summary>Für Tests überschreibbar.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        chatOptions = chatOptions?.Clone() ?? new ChatOptions();
        chatOptions.MaxOutputTokens ??= o.MaxOutputTokens;

        var budget = o.ContextLength - chatOptions.MaxOutputTokens.Value - ContextBudget.EstimateTokens(chatOptions);
        var fitted = ContextBudget.Fit(messages, budget);

        var stopwatch = Stopwatch.StartNew();
        for (var attempt = 0; ; attempt++)
        {
            pause.ThrowIfPaused();
            try
            {
                await loader.EnsureLoadedAsync(model, cancellationToken);
                return await base.GetResponseAsync(fitted, chatOptions, cancellationToken);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                // Pausiert yue-guard uns mitten in der Anfrage, bricht die Anfrage ab, statt auf das Modell zu warten.
                pause.ThrowIfPaused();
                var delay = Backoff[Math.Min(attempt, Backoff.Length - 1)];
                if (stopwatch.Elapsed + delay > o.RetryWindow)
                    throw new LlmUnavailableException($"Das lokale LLM ist seit {stopwatch.Elapsed.TotalMinutes:0.#} Minuten nicht erreichbar: {ex.Message}", ex);

                logger.LogWarning("LLM-Anfrage fehlgeschlagen ({Error}), neuer Versuch in {Delay}", ex.Message, delay);
                await Delay(delay, cancellationToken);
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming wird nicht verwendet.");

    /// <summary>
    /// Verbindungsfehler, Timeouts und Serverfehler gelten als vorübergehend (Modell entladen oder LM Studio startet neu).
    /// 4xx außer 404/408/429 nicht: Eine zu große oder ungültige Anfrage wird durch Wiederholen nicht besser.
    /// </summary>
    public static bool IsTransient(Exception ex, CancellationToken requestToken) => ex switch
    {
        _ when requestToken.IsCancellationRequested => false,
        LlmUnavailableException => false,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: var s } => IsTransientStatus((int)s!),
        ClientResultException { Status: var s } => s == 0 || IsTransientStatus(s),
        TaskCanceledException or TimeoutException or IOException => true,
        _ => ex.InnerException is not null && IsTransient(ex.InnerException, requestToken),
    };

    private static bool IsTransientStatus(int status) => status is 404 or 408 or 429 or >= 500;
}
