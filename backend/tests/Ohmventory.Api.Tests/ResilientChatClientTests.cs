using System.Net;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ohmventory.Api.Llm;

namespace Ohmventory.Api.Tests;

public class ResilientChatClientTests : IDisposable
{
    private readonly string _stateFile = Path.Combine(Path.GetTempPath(), $"ohm-{Guid.NewGuid():N}", "llm-paused");
    private readonly LlmOptions _options;
    private readonly LlmPauseState _pause;

    public ResilientChatClientTests()
    {
        _options = new LlmOptions { ManagementEndpoint = "off", StateFile = _stateFile, RetryWindow = TimeSpan.FromMinutes(5) };
        _pause = new LlmPauseState(Options.Create(_options));
    }

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_stateFile)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private ResilientChatClient Create(FakeChatClient inner, List<TimeSpan>? delays = null) =>
        new(inner, "gemma-4-e4b-it-mlx", _pause,
            new LmStudioModelLoader(new HttpClient(), Options.Create(_options), NullLogger<LmStudioModelLoader>.Instance),
            Options.Create(_options), NullLogger<ResilientChatClient>.Instance)
        {
            Delay = (d, _) => { delays?.Add(d); return Task.CompletedTask; },
        };

    [Fact]
    public async Task RetriesWithBackoffWhenModelWasUnloaded()
    {
        var inner = new FakeChatClient(new HttpRequestException("connection reset"), new HttpRequestException("500", null, HttpStatusCode.InternalServerError));
        var delays = new List<TimeSpan>();

        var response = await Create(inner, delays).GetResponseAsync("Hallo");

        Assert.Equal("ok", response.Text);
        Assert.Equal(3, inner.Calls);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], delays);
    }

    [Fact]
    public async Task DoesNotRetryBadRequests()
    {
        var inner = new FakeChatClient(new HttpRequestException("context too long", null, HttpStatusCode.BadRequest));

        await Assert.ThrowsAsync<HttpRequestException>(() => Create(inner).GetResponseAsync("Hallo"));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task GivesUpAfterRetryWindow()
    {
        _options.RetryWindow = TimeSpan.FromSeconds(30);
        var inner = new FakeChatClient(Enumerable.Repeat<Exception>(new HttpRequestException("down"), 20).ToArray());

        await Assert.ThrowsAsync<LlmUnavailableException>(() => Create(inner).GetResponseAsync("Hallo"));
    }

    [Fact]
    public async Task FailsFastWhilePaused()
    {
        _pause.Pause();
        var inner = new FakeChatClient();

        await Assert.ThrowsAsync<LlmPausedException>(() => Create(inner).GetResponseAsync("Hallo"));
        Assert.Equal(0, inner.Calls);
    }

    [Fact]
    public async Task StopsRetryingWhenPausedDuringRequest()
    {
        var inner = new FakeChatClient(new HttpRequestException("model unloaded")) { OnCall = () => _pause.Pause() };

        await Assert.ThrowsAsync<LlmPausedException>(() => Create(inner).GetResponseAsync("Hallo"));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task SetsOutputLimitWhenCallerDidNot()
    {
        var inner = new FakeChatClient();

        await Create(inner).GetResponseAsync("Hallo");

        Assert.Equal(_options.MaxOutputTokens, inner.LastOptions?.MaxOutputTokens);
    }

    [Fact]
    public void PauseAndResumeAreIdempotentAndSurviveRestart()
    {
        _pause.Pause();
        var since = _pause.PausedSince;
        _pause.Pause();
        Assert.Equal(since, _pause.PausedSince);
        Assert.True(new LlmPauseState(Options.Create(_options)).IsPaused);

        _pause.Resume();
        _pause.Resume();
        Assert.False(new LlmPauseState(Options.Create(_options)).IsPaused);
    }

    private sealed class FakeChatClient(params Exception[] failures) : IChatClient
    {
        private readonly Queue<Exception> _failures = new(failures);
        public int Calls { get; private set; }
        public ChatOptions? LastOptions { get; private set; }
        public Action? OnCall { get; init; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastOptions = options;
            OnCall?.Invoke();
            return _failures.TryDequeue(out var ex)
                ? Task.FromException<ChatResponse>(ex)
                : Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
