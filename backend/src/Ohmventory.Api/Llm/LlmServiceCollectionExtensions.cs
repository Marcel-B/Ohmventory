using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Ohmventory.Api.Llm;

public static partial class LlmServiceCollectionExtensions
{
    public const string VisionClientKey = "vision";
    public const string ChatClientKey = "chat";

    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.Section));
        services.AddOptions<LlmGateOptions>()
            .Bind(configuration.GetSection(LlmGateOptions.Section))
            .Validate(o => AppName().IsMatch(o.App), "LlmGate:App: Kleinbuchstaben, Ziffern, '.', '-' und '_', bis 64 Zeichen")
            .Validate(o => Uri.TryCreate(o.Url, UriKind.Absolute, out _), "LlmGate:Url ist keine gültige Adresse")
            .ValidateOnStart();
        services.AddSingleton<LlmPauseState>();

        services.AddHttpClient(LlmGateRegistration.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(15));
        services.AddHostedService<LlmGateRegistration>();

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            var gate = sp.GetRequiredService<IOptions<LlmGateOptions>>().Value;
            // Alle Anfragen gehen durch die LLM-Schranke, nie direkt an LM Studio; der App-Name ist der API-Key.
            return new OpenAIClient(new ApiKeyCredential(gate.App), new OpenAIClientOptions
            {
                Endpoint = gate.ApiEndpoint,
                NetworkTimeout = options.Timeout,
                // Wiederholungen übernimmt ResilientChatClient, abgestimmt auf die Antworten der Schranke.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            });
        });

        services.AddKeyedChatClient(VisionClientKey, sp => CreateClient(sp, o => o.VisionModel));

        services.AddKeyedChatClient(ChatClientKey, sp => CreateClient(sp, o => o.ChatModel))
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = 5);

        return services;
    }

    private static IChatClient CreateClient(IServiceProvider sp, Func<LlmOptions, string> model)
    {
        var options = sp.GetRequiredService<IOptions<LlmOptions>>();
        var modelKey = model(options.Value);
        var inner = sp.GetRequiredService<OpenAIClient>().GetChatClient(modelKey).AsIChatClient();
        return new ResilientChatClient(
            inner,
            modelKey,
            sp.GetRequiredService<LlmPauseState>(),
            options,
            sp.GetRequiredService<IOptions<LlmGateOptions>>(),
            sp.GetRequiredService<ILogger<ResilientChatClient>>());
    }

    [GeneratedRegex("^[a-z0-9._-]{1,64}$")]
    private static partial Regex AppName();
}
