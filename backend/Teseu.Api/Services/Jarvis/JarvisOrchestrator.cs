using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Teseu.Api.Services.AI;

namespace Teseu.Api.Services.Jarvis;

/// <summary>
/// Core orchestrator for the Jarvis assistant.
/// Handles: message construction, AI provider calls, tool-calling loop, permission enforcement,
/// usage metrics collection, and structured logging.
/// </summary>
public sealed class JarvisOrchestrator
{
    private readonly IJarvisAiProvider _provider;
    private readonly IReadOnlyDictionary<string, IJarvisTool> _tools;
    private readonly JarvisOptions _options;
    private readonly ILogger<JarvisOrchestrator> _logger;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public JarvisOrchestrator(
        IJarvisAiProvider provider,
        IEnumerable<IJarvisTool> tools,
        IOptions<JarvisOptions> options,
        ILogger<JarvisOrchestrator> logger)
    {
        _provider = provider;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JarvisResponse> ChatAsync(string userMessage, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var requestSw = Stopwatch.StartNew();

        _logger.LogInformation("[{RequestId}] Jarvis request started", requestId);

        var messages = new List<ChatMessage>
        {
            new("system", JarvisPrompt.System),
            new("user", userMessage.Trim())
        };

        var definitions = _tools.Values.Select(t =>
            new ToolDefinition(t.Name, t.Description, t.ParameterSchema)).ToList();

        var toolsUsed = new List<string>();
        var totalUsage = new UsageAccumulator();
        string? modelUsed = null;
        int iteration = 0;

        while (iteration < _options.MaxToolIterations)
        {
            iteration++;
            _logger.LogDebug("[{RequestId}] AI call iteration {Iteration}", requestId, iteration);

            var result = await _provider.ChatAsync(messages, definitions, cancellationToken);
            modelUsed ??= result.Model;
            totalUsage.Add(result.Usage);

            var assistantMsg = result.Message;

            if (assistantMsg.ToolCalls is not { Count: > 0 })
            {
                // No tool calls — we have the final answer
                var answer = assistantMsg.Content?.Trim();
                if (string.IsNullOrWhiteSpace(answer))
                    throw new AiInvalidResponseException("O assistente de IA não produziu uma resposta válida.");

                requestSw.Stop();
                _logger.LogInformation(
                    "[{RequestId}] Jarvis completed in {ElapsedMs}ms ({Iterations} iterations, {ToolCount} tools, {TotalTokens} tokens)",
                    requestId, requestSw.ElapsedMilliseconds, iteration, toolsUsed.Count, totalUsage.Total);

                return new JarvisResponse
                {
                    Message = answer,
                    ToolsUsed = toolsUsed.Distinct().ToList(),
                    Model = modelUsed,
                    RequestId = requestId,
                    Usage = totalUsage.ToMetrics(),
                    LatencyMs = requestSw.ElapsedMilliseconds
                };
            }

            // Add assistant message with tool calls to conversation
            messages.Add(assistantMsg);

            // Execute each tool call
            foreach (var call in assistantMsg.ToolCalls)
            {
                var toolResult = await ExecuteToolAsync(call, requestId, cancellationToken);
                toolsUsed.Add(call.Name);

                // Add tool result as a message
                var resultJson = JsonSerializer.Serialize(toolResult, _jsonOptions);
                messages.Add(new ChatMessage("tool", resultJson, ToolCallId: call.Id));
            }
        }

        // Max iterations reached
        _logger.LogWarning("[{RequestId}] Max tool iterations ({Max}) reached", requestId, _options.MaxToolIterations);
        throw new AiInvalidResponseException(
            $"O assistente atingiu o limite de {_options.MaxToolIterations} iterações sem produzir uma resposta final.");
    }

    private async Task<ToolResult> ExecuteToolAsync(ToolCallRequest call, string requestId, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
        {
            _logger.LogWarning("[{RequestId}] Unknown tool requested: {Tool}", requestId, call.Name);
            return ToolResult.Fail(call.Name, $"Tool '{call.Name}' does not exist.");
        }

        // Permission enforcement
        if (tool.Permission > PermissionLevel.Read)
        {
            _logger.LogWarning("[{RequestId}] Blocked tool {Tool} (requires {Permission}, only Read allowed)",
                requestId, call.Name, tool.Permission);
            return ToolResult.Fail(call.Name,
                $"Tool '{call.Name}' requires {tool.Permission} permission which is not currently allowed.");
        }

        _logger.LogDebug("[{RequestId}] Executing tool {Tool}", requestId, call.Name);

        var result = await tool.ExecuteAsync(call.Arguments, cancellationToken);

        _logger.LogInformation("[{RequestId}] Tool {Tool} executed in {Ms}ms (success={Success})",
            requestId, result.Tool, result.ExecutionMs, result.Success);

        return result;
    }

    /// <summary>Returns available tool names and their permission levels.</summary>
    public IReadOnlyDictionary<string, PermissionLevel> GetToolPermissions() =>
        _tools.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Permission);

    private sealed class UsageAccumulator
    {
        public int Input { get; private set; }
        public int Output { get; private set; }
        public int? Cached { get; private set; }
        public int Total => Input + Output;

        public void Add(AiUsageMetrics? metrics)
        {
            if (metrics is null) return;
            Input += metrics.InputTokens;
            Output += metrics.OutputTokens;
            if (metrics.CachedTokens.HasValue)
                Cached = (Cached ?? 0) + metrics.CachedTokens.Value;
        }

        public AiUsageMetrics ToMetrics() => new()
        {
            InputTokens = Input,
            OutputTokens = Output,
            CachedTokens = Cached
        };
    }
}
