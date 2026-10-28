using System.ClientModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Ohmventory.Api.Llm;

/// <summary>
/// Sitzt direkt vor dem Client für die LLM-Schranke: prüft den Pausenzustand, hält das Kontextbudget ein und
/// behandelt die Antworten der Schranke (docs/llm-gate.md im scripts-Repo):
/// 503 wird nach <c>Retry-After</c> wiederholt, 403/424/507 sind Konfigurationsfehler und werden nicht wiederholt,
/// abgebrochene Antworten (Modell mitten in der Antwort entladen) und 502 werden mit Backoff wiederholt.
/// Laden und Entladen übernimmt allein die Schranke.
/// </summary>
public class ResilientChatClient(
    IChatClient inner,
    string model,
    LlmPauseState pause,
    IOptions<LlmOptions> options,
    IOptions<LlmGateOptions> gate,
    ILogger<ResilientChatClient> logger) : DelegatingChatClient(inner)
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(60)];

    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(1);

    private (DateTimeOffset Until, LlmGateRejectedException Error)? _rejected;

    /// <summary>Für Tests überschreibbar.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    /// <summary>Für Tests überschreibbar.</summary>
    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        chatOptions = chatOptions?.Clone() ?? new ChatOptions();
        chatOptions.MaxOutputTokens ??= o.MaxOutputTokens;

        var budget = o.ContextLength - chatOptions.MaxOutputTokens.Value - ContextBudget.EstimateTokens(chatOptions);
        var fitted = ContextBudget.Fit(messages, budget);

        // Nach einer Ablehnung wegen Konfiguration nicht gleich wieder fragen; die Meldung steht schon im Log.
        if (_rejected is { } rejected && Now() < rejected.Until)
            throw rejected.Error;

        var stopwatch = Stopwatch.StartNew();
        var waited = TimeSpan.Zero;
        for (var attempt = 0; ; attempt++)
        {
            pause.ThrowIfPaused();
            try
            {
                var response = await base.GetResponseAsync(fitted, chatOptions, cancellationToken);
                _rejected = null;
                return response;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && StatusOf(ex) is 403 or 424 or 507)
            {
                var error = new LlmGateRejectedException(StatusOf(ex)!.Value, model, gate.Value.Url.TrimEnd('/'), GateMessage(ex), ex);
                _rejected = (Now() + o.ConfigErrorCooldown, error);
                logger.LogError("LLM-Schranke lehnt ab: {Message}", error.Message);
                throw error;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && StatusOf(ex) == 503)
            {
                // Zu lange in der Schlange, verworfen oder gerade kein Platz: nach Retry-After neu einreihen.
                var delay = RetryAfter(ex) ?? DefaultRetryAfter;
                var elapsed = stopwatch.Elapsed + waited;
                if (elapsed + delay > o.Timeout)
                    throw new LlmUnavailableException(
                        $"Die LLM-Schranke hat die Anfrage nach {elapsed.TotalMinutes:0.#} Minuten nicht durchgelassen: {GateMessage(ex)}", ex);

                logger.LogInformation("LLM-Schranke: {Message}; neuer Versuch in {Delay}", GateMessage(ex), delay);
                await Delay(delay, cancellationToken);
                waited += delay;
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken))
            {
                // Pausiert yue-guard uns mitten in der Anfrage, bricht die Anfrage ab, statt auf das Modell zu warten.
                pause.ThrowIfPaused();
                var delay = Backoff[Math.Min(attempt, Backoff.Length - 1)];
                if (stopwatch.Elapsed + waited + delay > o.RetryWindow)
                    throw new LlmUnavailableException($"Das lokale LLM ist seit {(stopwatch.Elapsed + waited).TotalMinutes:0.#} Minuten nicht erreichbar: {ex.Message}", ex);

                logger.LogWarning("LLM-Anfrage fehlgeschlagen ({Error}), neuer Versuch in {Delay}", ex.Message, delay);
                await Delay(delay, cancellationToken);
                waited += delay;
            }
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? chatOptions = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Streaming wird nicht verwendet.");

    /// <summary>
    /// Verbindungsfehler, Timeouts und Serverfehler gelten als vorübergehend (Antwort abgebrochen, Schranke oder LM Studio startet neu).
    /// 4xx außer 408/429 nicht: Eine zu große oder ungültige Anfrage wird durch Wiederholen nicht besser.
    /// </summary>
    public static bool IsTransient(Exception ex, CancellationToken requestToken) => ex switch
    {
        _ when requestToken.IsCancellationRequested => false,
        LlmUnavailableException or LlmGateRejectedException => false,
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: var s } => IsTransientStatus((int)s!),
        ClientResultException { Status: var s } => s == 0 || IsTransientStatus(s),
        TaskCanceledException or TimeoutException or IOException => true,
        _ => ex.InnerException is not null && IsTransient(ex.InnerException, requestToken),
    };

    private static bool IsTransientStatus(int status) => status is 408 or 429 or >= 500;

    private static int? StatusOf(Exception? ex) => ex switch
    {
        null => null,
        ClientResultException { Status: > 0 and var s } => s,
        HttpRequestException { StatusCode: { } s } => (int)s,
        _ => StatusOf(ex.InnerException),
    };

    private static ClientResultException? ClientError(Exception? ex) =>
        ex as ClientResultException ?? (ex?.InnerException is { } inner ? ClientError(inner) : null);

    private static TimeSpan? RetryAfter(Exception ex)
    {
        if (ClientError(ex)?.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) != true || value is null)
            return null;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(Math.Max(seconds, 1));
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            return at - DateTimeOffset.UtcNow is { } d && d > TimeSpan.Zero ? d : TimeSpan.FromSeconds(1);
        return null;
    }

    /// <summary>Die Meldung der Schranke (<c>{"error": {"message": …}}</c>), sonst die der Ausnahme.</summary>
    private static string GateMessage(Exception ex)
    {
        try
        {
            var body = ClientError(ex)?.GetRawResponse()?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                    && message.GetString() is { Length: > 0 } text)
                    return text;
            }
        }
        catch (Exception parse) when (parse is JsonException or InvalidOperationException)
        {
            // Kein JSON von der Schranke; dann die Meldung der Ausnahme.
        }
        return ex.Message;
    }
}
