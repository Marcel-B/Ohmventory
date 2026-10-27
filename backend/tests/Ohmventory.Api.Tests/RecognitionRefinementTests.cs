using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ohmventory.Api.Domain;
using Ohmventory.Api.Recognition;
using Ohmventory.Api.Search;

namespace Ohmventory.Api.Tests;

public class RecognitionRefinementTests
{
    private static readonly byte[] Image = [0xFF, 0xD8, 0xFF];

    // Das beobachtete Ergebnis: Breakout-Board erkannt, aber nicht welches, und nur „01" gelesen.
    private const string GenericAnalysis = """
        {"visibleText":["C1","C7","R1","01"],"observations":"Rückseite mit goldenem Flachbandkabel und vier Pins",
         "name":"Steckmodul/Breakout-Board","category":"BreakoutBoard","manufacturer":null,"partNumber":null,"package":null,
         "description":null,"isBreakoutBoard":true,"alternatives":["Erweiterungsplatine"],"confidence":0.7}
        """;

    private const string Reading = """{"text":["1.30\"IIC","V2.2","1(VDD)","2(GND)","C1"]}""";

    private const string Refinement = """
        {"matches":true,"explanation":"Mehrere Händler führen das 1,3-Zoll-OLED mit Aufdruck 1.30\" IIC V2.2 mit SH1106.",
         "name":"1,3-Zoll-OLED-Display 128×64 (I²C, SH1106)","category":"Display","manufacturer":null,"partNumber":"SH1106",
         "description":"Kleines monochromes Display für Mikrocontroller.","alternatives":["SSD1306","SH1106"],"sources":[1,2,9],"confidence":0.95}
        """;

    private static readonly WebSearchResult[] Results =
    [
        new("1.3 inch OLED I2C SH1106 128x64", "https://example.com/a", "1.30\" IIC V2.2 SH1106"),
        new("1.3\" OLED Display Modul", "https://example.com/b", "SH1106 Treiber"),
    ];

    [Fact]
    public async Task ReadsRotatedPrintsSearchesTheWebAndRefines()
    {
        var vision = new ScriptedChatClient(GenericAnalysis, Reading);
        var chat = new ScriptedChatClient(Refinement);
        var searx = new StubSearch(Results);

        var part = await Service(vision, chat, searx).RecognizeAsync(Image, "image/jpeg", [(Image, "image/jpeg")], null, default);

        Assert.Equal("1.30 inch IIC V2.2 module", searx.Queries.Single());
        Assert.Equal([1, 2], vision.Images); // Stufe 1: nur das Foto; Lese-Durchgang: Foto plus gedrehte Ansicht.
        Assert.Equal("SH1106", part.PartNumber);
        Assert.Equal(PartCategory.Display, part.Category);
        Assert.Equal(PartRecognitionService.MaxConfidenceFromWeb, part.Confidence);
        Assert.Equal(["https://example.com/a", "https://example.com/b"], part.Sources.Select(s => s.Url));
        Assert.Equal(["SSD1306"], part.Alternatives);
        Assert.Contains("1.30\"IIC", part.MarkingsRead);
        Assert.DoesNotContain("C1", part.MarkingsRead);
    }

    [Fact]
    public async Task KeepsFirstResultWhenSearchFails()
    {
        var vision = new ScriptedChatClient(GenericAnalysis, Reading);
        var searx = new StubSearch(null);

        var part = await Service(vision, new ScriptedChatClient(), searx).RecognizeAsync(Image, "image/jpeg", [(Image, "image/jpeg")], null, default);

        Assert.Equal("Steckmodul/Breakout-Board", part.Name);
        Assert.Empty(part.Sources);
        Assert.Contains("1.30\"IIC", part.MarkingsRead);
    }

    [Fact]
    public async Task SkipsSearchWhenPartNumberIsPrinted()
    {
        var vision = new ScriptedChatClient("""
            {"visibleText":["GY-BME280"],"observations":null,"name":"BME280","category":"Sensor","manufacturer":null,"partNumber":"BME280",
             "package":null,"description":null,"isBreakoutBoard":true,"alternatives":null,"confidence":0.95}
            """);
        var searx = new StubSearch(Results);

        var part = await Service(vision, new ScriptedChatClient(), searx).RecognizeAsync(Image, "image/jpeg", [], null, default);

        Assert.Equal(0.95, part.Confidence);
        Assert.Empty(searx.Queries);
    }

    [Fact]
    public void IgnoresRefinementThatDoesNotMatch()
    {
        var part = PartRecognitionService.Finalize(new VisionAnalysis(["1.30\"IIC"], null, "Board", PartCategory.BreakoutBoard,
            null, null, null, null, true, null, 0.6));
        var refinement = new WebRefinement(false, null, "Irgendwas", PartCategory.Sensor, null, "XYZ", null, null, [1], 0.9);

        Assert.Same(part, PartRecognitionService.Apply(part, refinement, Results));
    }

    [Fact]
    public void IgnoresRefinementWithoutRealSources()
    {
        var part = PartRecognitionService.Finalize(new VisionAnalysis(["1.30\"IIC"], null, "Board", PartCategory.BreakoutBoard,
            null, null, null, null, true, null, 0.6));
        var refinement = new WebRefinement(true, null, "OLED", PartCategory.Display, null, "SH1106", null, null, [7], 0.9);

        Assert.Same(part, PartRecognitionService.Apply(part, refinement, Results));
    }

    private static PartRecognitionService Service(IChatClient vision, IChatClient chat, StubSearch searx) =>
        new(vision, chat, searx.Client, NullLogger<PartRecognitionService>.Instance);

    private sealed class StubSearch
    {
        public List<string> Queries { get; } = [];
        public SearxngClient Client { get; }

        public StubSearch(WebSearchResult[]? results)
        {
            var handler = new Handler(request =>
            {
                Queries.Add(Uri.UnescapeDataString(request.RequestUri!.Query.Split("q=")[1]));
                if (results is null)
                    return new HttpResponseMessage(HttpStatusCode.BadGateway);
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    results = results.Select(r => new { title = r.Title, url = r.Url, content = r.Snippet }),
                });
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            });
            Client = new SearxngClient(new HttpClient(handler) { BaseAddress = new Uri("http://searx/") }, Options.Create(new SearxngOptions()));
        }

        private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(respond(request));
        }
    }

    private sealed class ScriptedChatClient(params string[] answers) : IChatClient
    {
        private readonly Queue<string> _answers = new(answers);
        public List<int> Images { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Images.Add(messages.SelectMany(m => m.Contents).OfType<DataContent>().Count());
            return _answers.TryDequeue(out var answer)
                ? Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, answer)))
                : Task.FromException<ChatResponse>(new InvalidOperationException("Keine Antwort vorbereitet"));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
