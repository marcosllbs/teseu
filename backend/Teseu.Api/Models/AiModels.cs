using System.ComponentModel.DataAnnotations;

namespace Teseu.Api.Models;

public sealed record AiChatRequest
{
    [Required, MinLength(1), MaxLength(2000)]
    public required string Message { get; init; }
}

public sealed record AiChatResponse(string Answer, IReadOnlyList<string> ToolsUsed);

public sealed record ApiError(string Error);
