using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Teseu.Api.Services.AI;

namespace Teseu.Api.Services.Jarvis;

public sealed class OpenAiProvider(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiProvider> logger) : IJarvisAiProvider
{
    private readonly OpenAiOptions _options = options.Value;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AiCompletionResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        CancellationToken cancellationToken)
    {
        var request = BuildRequest(messages, tools);

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "v1/chat/completions", request, JsonOptions, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new AiUnavailableException("A chave da API OpenAI é inválida ou não foi configurada.");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new AiUnavailableException("Rate limit da OpenAI atingido. Tente novamente em alguns segundos.");

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning("OpenAI returned HTTP {StatusCode}: {Body}",
                    (int)response.StatusCode, errorBody[..Math.Min(errorBody.Length, 500)]);
                throw new AiUnavailableException("O serviço de IA não conseguiu processar a solicitação.");
            }

            var result = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(JsonOptions, cancellationToken);
            if (result?.Choices is not { Count: > 0 })
                throw new AiInvalidResponseException("O serviço de IA retornou uma resposta vazia.");

            var choice = result.Choices[0];
            var msg = choice.Message;

            var toolCalls = msg.ToolCalls?.Select(tc => new ToolCallRequest(
                tc.Id,
                tc.Function.Name,
                ParseArguments(tc.Function.Arguments)
            )).ToList();

            return new AiCompletionResult
            {
                Message = new ChatMessage(
                    msg.Role,
                    msg.Content,
                    toolCalls?.Count > 0 ? toolCalls : null),
                Usage = result.Usage is { } u ? new AiUsageMetrics
                {
                    InputTokens = u.PromptTokens,
                    OutputTokens = u.CompletionTokens,
                    CachedTokens = u.PromptTokensCached
                } : null,
                Model = result.Model
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiTimeoutException("O serviço de IA excedeu o tempo limite.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("O serviço de IA está indisponível.", ex);
        }
        catch (JsonException ex)
        {
            throw new AiInvalidResponseException("O serviço de IA retornou uma resposta inválida.", ex);
        }
    }

    private object BuildRequest(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition>? tools)
    {
        var openAiMessages = messages.Select(MapMessage).ToList();

        var request = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = openAiMessages,
            ["temperature"] = _options.Temperature,
            ["max_tokens"] = _options.MaxTokens
        };

        if (tools is { Count: > 0 })
        {
            request["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = t.Parameters
                }
            }).ToList();
            request["tool_choice"] = "auto";
        }

        return request;
    }

    private static object MapMessage(ChatMessage msg)
    {
        if (msg.ToolCallId is not null)
        {
            return new { role = "tool", content = msg.Content, tool_call_id = msg.ToolCallId };
        }

        if (msg.ToolCalls is { Count: > 0 })
        {
            return new
            {
                role = msg.Role,
                content = msg.Content,
                tool_calls = msg.ToolCalls.Select(tc => new
                {
                    id = tc.Id,
                    type = "function",
                    function = new { name = tc.Name, arguments = tc.Arguments.GetRawText() }
                }).ToList()
            };
        }

        return new { role = msg.Role, content = msg.Content };
    }

    private static JsonElement ParseArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return JsonSerializer.SerializeToElement(new { });

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(arguments);
        }
        catch
        {
            return JsonSerializer.SerializeToElement(new { });
        }
    }
}

// --- OpenAI API Response DTOs ---

internal sealed record OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public IReadOnlyList<OpenAiChoice>? Choices { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}

internal sealed record OpenAiChoice
{
    [JsonPropertyName("message")]
    public required OpenAiMessage Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed record OpenAiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "assistant";

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; init; }
}

internal sealed record OpenAiToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OpenAiFunction Function { get; init; }
}

internal sealed record OpenAiFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

internal sealed record OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("prompt_tokens_details")]
    public OpenAiPromptTokensDetails? PromptTokensDetails { get; init; }

    public int? PromptTokensCached => PromptTokensDetails?.CachedTokens;
}

internal sealed record OpenAiPromptTokensDetails
{
    [JsonPropertyName("cached_tokens")]
    public int? CachedTokens { get; init; }
}
