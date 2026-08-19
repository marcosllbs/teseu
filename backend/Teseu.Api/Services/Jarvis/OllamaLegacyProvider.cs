using System.Text.Json;
using Teseu.Api.Services.AI;

namespace Teseu.Api.Services.Jarvis;

/// <summary>
/// Legacy Ollama provider. Adapts the existing IOllamaService to the IJarvisAiProvider interface.
/// This allows keeping Ollama as a fallback during the migration.
/// </summary>
public sealed class OllamaLegacyProvider(IOllamaService ollama) : IJarvisAiProvider
{
    public async Task<AiCompletionResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        var ollamaMessages = messages.Select(MapToOllama).ToList();
        var ollamaTools = tools?.Select(t =>
            new OllamaTool("function", new OllamaToolFunction(t.Name, t.Description, t.Parameters))).ToList();

        var result = await ollama.ChatAsync(ollamaMessages, ollamaTools, cancellationToken);

        var toolCalls = result.ToolCalls?.Select((tc, i) => new ToolCallRequest(
            $"call_{i}",
            tc.Function.Name,
            tc.Function.Arguments
        )).ToList();

        return new AiCompletionResult
        {
            Message = new ChatMessage(
                result.Role,
                result.Content,
                toolCalls?.Count > 0 ? toolCalls : null),
            Usage = null, // Ollama does not return token usage in the same format
            Model = null
        };
    }

    private static OllamaMessage MapToOllama(ChatMessage msg)
    {
        if (msg.ToolCallId is not null)
            return new OllamaMessage("tool", msg.Content, ToolName: msg.ToolCallId);

        if (msg.ToolCalls is { Count: > 0 })
        {
            var calls = msg.ToolCalls.Select(tc =>
                new OllamaToolCall(new OllamaFunctionCall(tc.Name, tc.Arguments))).ToList();
            return new OllamaMessage("assistant", msg.Content, ToolCalls: calls);
        }

        return new OllamaMessage(msg.Role, msg.Content);
    }
}
