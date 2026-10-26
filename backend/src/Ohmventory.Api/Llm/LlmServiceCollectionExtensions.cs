using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Ohmventory.Api.Llm;

public static class LlmServiceCollectionExtensions
{
    public const string VisionClientKey = "vision";
    public const string ChatClientKey = "chat";

    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.Section));
        services.AddSingleton<LlmPauseState>();
        services.AddHttpClient<LmStudioModelLoader>((sp, http) =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            http.BaseAddress = LmStudioModelLoader.ManagementBase(options);
            // Das erste Laden eines Modells kann dauern.
            http.Timeout = TimeSpan.FromMinutes(3);
        });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            return new OpenAIClient(new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(options.Endpoint),
                NetworkTimeout = options.Timeout,
                // Wiederholungen übernimmt ResilientChatClient, abgestimmt auf Pausen und Modellentladung.
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
            sp.GetRequiredService<LmStudioModelLoader>(),
            options,
            sp.GetRequiredService<ILogger<ResilientChatClient>>());
    }
}
