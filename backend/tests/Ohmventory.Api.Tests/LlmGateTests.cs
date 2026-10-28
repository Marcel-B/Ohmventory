using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ohmventory.Api.Llm;
using OpenAI;

namespace Ohmventory.Api.Tests;

/// <summary>
/// Spielt die Antworten der LLM-Schranke (docs/llm-gate.md im scripts-Repo) über den echten OpenAI-Client durch.
/// </summary>
public class LlmGateTests : IDisposable
{
    private const string Model = "gemma-4-e4b-it-mlx";
    private readonly string _stateFile = Path.Combine(Path.GetTempPath(), $"ohm-{Guid.NewGuid():N}", "llm-paused");
    private readonly LlmOptions _options;
    private readonly LlmGateOptions _gate = new() { Url = "http://gate.test:1235", App = "ohmventory" };
    private readonly FakeGate _handler = new();

    public LlmGateTests() => _options = new LlmOptions { StateFile = _stateFile };

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_stateFile)!;
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private ResilientChatClient Create(List<TimeSpan>? delays = null, Func<DateTimeOffset>? now = null)
    {
        var openAi = new OpenAIClient(new ApiKeyCredential(_gate.App), new OpenAIClientOptions
        {
            Endpoint = _gate.ApiEndpoint,
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            Transport = new HttpClientPipelineTransport(new HttpClient(_handler)),
        });
        return new ResilientChatClient(
            openAi.GetChatClient(Model).AsIChatClient(), Model,
            new LlmPauseState(Options.Create(_options)), Options.Create(_options), Options.Create(_gate),
            NullLogger<ResilientChatClient>.Instance)
        {
            Delay = (d, _) => { delays?.Add(d); return Task.CompletedTask; },
            Now = now ?? (() => DateTimeOffset.UtcNow),
        };
    }

    [Fact]
    public async Task SendsRequestsToTheGateWithAppNameAsKey()
    {
        _handler.Enqueue(FakeGate.Ok("Hallo zurück"));

        var response = await Create().GetResponseAsync("Hallo");

        Assert.Equal("Hallo zurück", response.Text);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("http://gate.test:1235/v1/chat/completions", request.Uri);
        Assert.Equal("Bearer ohmventory", request.Authorization);
        Assert.Contains($"\"model\":\"{Model}\"", request.Body);
    }

    [Fact]
    public async Task WaitsRetryAfterOn503()
    {
        _handler.Enqueue(FakeGate.Error(503, "zu lange gewartet", retryAfter: "120"));
        _handler.Enqueue(FakeGate.Ok("ok"));
        var delays = new List<TimeSpan>();

        var response = await Create(delays).GetResponseAsync("Hallo");

        Assert.Equal("ok", response.Text);
        Assert.Equal([TimeSpan.FromSeconds(120)], delays);
    }

    [Fact]
    public async Task GivesUpWhen503WouldExceedTimeout()
    {
        _options.Timeout = TimeSpan.FromMinutes(70);
        for (var i = 0; i < 3; i++)
            _handler.Enqueue(FakeGate.Error(503, "zu lange gewartet", retryAfter: "1800"));

        var error = await Assert.ThrowsAsync<LlmUnavailableException>(() => Create().GetResponseAsync("Hallo"));
        Assert.Contains("zu lange gewartet", error.Message);
        Assert.Equal(3, _handler.Requests.Count);
    }

    [Theory]
    [InlineData(403, "freigeben unter")]
    [InlineData(424, "nicht installiert")]
    [InlineData(507, "Speicherbudget")]
    public async Task ConfigurationErrorsAreNotRetried(int status, string hint)
    {
        _handler.Enqueue(FakeGate.Error(status, "Modell gemma-4-e4b-it-mlx ist für ohmventory nicht freigegeben"));
        var delays = new List<TimeSpan>();

        var error = await Assert.ThrowsAsync<LlmGateRejectedException>(() => Create(delays).GetResponseAsync("Hallo"));

        Assert.Equal(status, error.Status);
        Assert.Contains(hint, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nicht freigegeben", error.Message);
        Assert.Single(_handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task AsksAgainOnlyAfterCooldown()
    {
        var now = DateTimeOffset.UtcNow;
        var client = Create(now: () => now);
        _handler.Enqueue(FakeGate.Error(403, "nicht freigegeben"));
        _handler.Enqueue(FakeGate.Ok("ok"));

        await Assert.ThrowsAsync<LlmGateRejectedException>(() => client.GetResponseAsync("Hallo"));
        await Assert.ThrowsAsync<LlmGateRejectedException>(() => client.GetResponseAsync("Hallo"));
        Assert.Single(_handler.Requests);

        now += _options.ConfigErrorCooldown;
        Assert.Equal("ok", (await client.GetResponseAsync("Hallo")).Text);
        Assert.Equal(2, _handler.Requests.Count);
    }

    [Fact]
    public async Task RetriesBadGatewayWithBackoff()
    {
        _handler.Enqueue(FakeGate.Error(502, "LM Studio antwortet nicht"));
        _handler.Enqueue(FakeGate.Ok("ok"));
        var delays = new List<TimeSpan>();

        Assert.Equal("ok", (await Create(delays).GetResponseAsync("Hallo")).Text);
        Assert.Equal([TimeSpan.FromSeconds(5)], delays);
    }

    [Fact]
    public void RegistersConfiguredModelsAndAddsMissingVisionModel()
    {
        var gate = new LlmGateOptions
        {
            App = "ohmventory",
            Models = [new LlmGateModel { Model = Model, Source = "lmstudio-community/gemma-4-e4b-it-mlx", SizeGb = 6.9 }, new LlmGateModel()],
        };

        var models = gate.ModelsFor(new LlmOptions { ChatModel = Model, VisionModel = "gemma-4-26b-a4b-it-qat-mlx", ContextLength = 8192 });

        Assert.Equal([Model, "gemma-4-26b-a4b-it-qat-mlx"], models.Select(m => m.Model));
        Assert.All(models, m => Assert.Equal(8192, m.Context));
        Assert.Equal("lmstudio-community/gemma-4-e4b-it-mlx", models[0].Source);
    }

    private sealed class FakeGate : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<(string Uri, string? Authorization, string Body)> Requests { get; } = [];

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
            return _responses.Dequeue();
        }

        public static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK)
        {
            Content = new StringContent($$$"""
                {"id":"1","object":"chat.completion","created":0,"model":"{{{Model}}}",
                 "choices":[{"index":0,"message":{"role":"assistant","content":"{{{text}}}"},"finish_reason":"stop"}],
                 "usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
                """, Encoding.UTF8, "application/json"),
        };

        public static HttpResponseMessage Error(int status, string message, string? retryAfter = null)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = JsonContent.Create(new { error = new { message, type = "llm_gate", code = status } }),
            };
            if (retryAfter is not null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return response;
        }
    }
}
