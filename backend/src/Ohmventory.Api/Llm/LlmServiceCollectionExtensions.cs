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

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            return new OpenAIClient(new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions
            {
                Endpoint = new Uri(options.Endpoint),
                NetworkTimeout = options.Timeout,
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
            });
        });

        services.AddKeyedChatClient(VisionClientKey, sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            return sp.GetRequiredService<OpenAIClient>().GetChatClient(options.VisionModel).AsIChatClient();
        });

        services.AddKeyedChatClient(ChatClientKey, sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            return sp.GetRequiredService<OpenAIClient>().GetChatClient(options.ChatModel).AsIChatClient();
        }).UseFunctionInvocation();

        return services;
    }
}
