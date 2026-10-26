using Microsoft.Extensions.AI;
using Ohmventory.Api.Llm;

namespace Ohmventory.Api.Tests;

public class ContextBudgetTests
{
    [Fact]
    public void LeavesSmallRequestsUntouched()
    {
        ChatMessage[] messages = [new(ChatRole.User, "Kann ich ein Thermometer bauen?")];

        var fitted = ContextBudget.Fit(messages, 1000);

        Assert.Equal("Kann ich ein Thermometer bauen?", fitted.Single().Text);
    }

    [Fact]
    public void ShortensOldToolResultsFirst()
    {
        var big = new string('x', 6000);
        ChatMessage[] messages =
        [
            new(ChatRole.User, "Frage"),
            new(ChatRole.Tool, [new FunctionResultContent("a", big)]),
            new(ChatRole.Tool, [new FunctionResultContent("b", big)]),
        ];

        var fitted = ContextBudget.Fit(messages, 2500);

        var first = (string)((FunctionResultContent)fitted[1].Contents[0]).Result!;
        var second = (string)((FunctionResultContent)fitted[2].Contents[0]).Result!;
        Assert.EndsWith("(gekürzt)", first);
        Assert.Equal(big, second);
        Assert.True(ContextBudget.EstimateTokens(fitted) <= 2500);
    }

    [Fact]
    public void DoesNotModifyTheCallersMessages()
    {
        var big = new string('x', 6000);
        var tool = new ChatMessage(ChatRole.Tool, [new FunctionResultContent("a", big)]);

        ContextBudget.Fit([tool], 500);

        Assert.Equal(big, ((FunctionResultContent)tool.Contents[0]).Result);
    }

    [Fact]
    public void ThrowsWhenUserInputAloneIsTooLarge()
    {
        ChatMessage[] messages = [new(ChatRole.User, new string('x', 30000))];

        Assert.Throws<LlmInputTooLargeException>(() => ContextBudget.Fit(messages, 8192));
    }

    [Fact]
    public void CountsImagesWithFixedCost()
    {
        ChatMessage[] messages = [new(ChatRole.User, [new DataContent(new byte[500_000], "image/jpeg")])];

        Assert.Equal(ContextBudget.TokensPerMessage + ContextBudget.TokensPerImage, ContextBudget.EstimateTokens(messages));
    }

    [Fact]
    public void TruncateKeepsShortTextAndCutsLongText()
    {
        Assert.Equal("kurz", ContextBudget.Truncate("kurz", 100));
        Assert.True(ContextBudget.Truncate(new string('x', 1000), 100).Length <= 300);
    }
}
