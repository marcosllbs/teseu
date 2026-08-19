namespace Teseu.Api.Services.Jarvis;

/// <summary>Response from the Jarvis orchestrator to the client.</summary>
public sealed record JarvisResponse
{
    /// <summary>Natural language answer from the assistant.</summary>
    public required string Message { get; init; }

    /// <summary>Tools invoked during this request.</summary>
    public required IReadOnlyList<string> ToolsUsed { get; init; }

    /// <summary>Model that generated the response.</summary>
    public string? Model { get; init; }

    /// <summary>Unique identifier for this request (for debugging/auditing).</summary>
    public required string RequestId { get; init; }

    /// <summary>Token usage metrics (null if provider doesn't report).</summary>
    public AiUsageMetrics? Usage { get; init; }

    /// <summary>Total latency in milliseconds.</summary>
    public long LatencyMs { get; init; }
}
