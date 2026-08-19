using System.Text.Json;

namespace Teseu.Api.Services.Jarvis;

/// <summary>
/// Abstraction over the AI provider (OpenAI, Ollama, etc.)
/// The orchestrator communicates through this interface regardless of the underlying LLM.
/// </summary>
public interface IJarvisAiProvider
{
    Task<AiCompletionResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken);
}

/// <summary>Represents a message in the conversation.</summary>
public sealed record ChatMessage(
    string Role,
    string? Content = null,
    IReadOnlyList<ToolCallRequest>? ToolCalls = null,
    string? ToolCallId = null);

/// <summary>A tool call requested by the AI model.</summary>
public sealed record ToolCallRequest(string Id, string Name, JsonElement Arguments);

/// <summary>Tool definition sent to the AI provider for function calling.</summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);

/// <summary>Result from an AI provider chat completion.</summary>
public sealed record AiCompletionResult
{
    public required ChatMessage Message { get; init; }
    public AiUsageMetrics? Usage { get; init; }
    public string? Model { get; init; }
}

/// <summary>Token usage metrics from the AI provider.</summary>
public sealed record AiUsageMetrics
{
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int? CachedTokens { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
}
