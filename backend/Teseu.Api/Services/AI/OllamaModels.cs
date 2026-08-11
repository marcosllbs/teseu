using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teseu.Api.Services.AI;

public sealed record OllamaChatRequest(
    string Model,
    IReadOnlyList<OllamaMessage> Messages,
    bool Stream,
    bool Think,
    IReadOnlyList<OllamaTool>? Tools = null,
    OllamaChatOptions? Options = null);

public sealed record OllamaChatOptions(
    [property: JsonPropertyName("temperature")] double Temperature = 0.1,
    [property: JsonPropertyName("num_predict")] int NumPredict = 160);

public sealed record OllamaChatResponse(OllamaMessage? Message);

public sealed record OllamaMessage(
    string Role,
    string? Content = null,
    [property: JsonPropertyName("tool_calls")] IReadOnlyList<OllamaToolCall>? ToolCalls = null,
    [property: JsonPropertyName("tool_name")] string? ToolName = null);

public sealed record OllamaToolCall(OllamaFunctionCall Function);

public sealed record OllamaFunctionCall(string Name, JsonElement Arguments);

public sealed record OllamaTool(string Type, OllamaToolFunction Function);

public sealed record OllamaToolFunction(
    string Name,
    string Description,
    JsonElement Parameters);
