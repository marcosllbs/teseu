using System.Text.Json;

namespace Teseu.Api.Services.Jarvis;

/// <summary>
/// A tool that Jarvis can invoke to observe or act on the server.
/// Each tool has an explicit permission level that the orchestrator enforces.
/// </summary>
public interface IJarvisTool
{
    string Name { get; }
    string Description { get; }
    PermissionLevel Permission { get; }
    JsonElement ParameterSchema { get; }
    Task<ToolResult> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>Permission classification for tools.</summary>
public enum PermissionLevel
{
    /// <summary>Read-only queries. Automatically allowed.</summary>
    Read = 0,

    /// <summary>Reversible operational changes. Require validation/allowlist.</summary>
    Control = 1,

    /// <summary>Potentially destructive actions. Always require explicit confirmation.</summary>
    Dangerous = 2
}

/// <summary>Standardized result from tool execution.</summary>
public sealed record ToolResult
{
    public required bool Success { get; init; }
    public required string Tool { get; init; }
    public object? Data { get; init; }
    public string? Error { get; init; }
    public long ExecutionMs { get; init; }

    public static ToolResult Ok(string tool, object? data, long executionMs) =>
        new() { Success = true, Tool = tool, Data = data, ExecutionMs = executionMs };

    public static ToolResult Fail(string tool, string error, long executionMs = 0) =>
        new() { Success = false, Tool = tool, Error = error, ExecutionMs = executionMs };
}
