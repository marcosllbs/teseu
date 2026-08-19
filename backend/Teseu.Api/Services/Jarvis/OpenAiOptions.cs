using System.ComponentModel.DataAnnotations;

namespace Teseu.Api.Services.Jarvis;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Model { get; init; } = "gpt-4o-mini";

    [Range(5, 300)]
    public int TimeoutSeconds { get; init; } = 60;

    [Range(0.0, 2.0)]
    public double Temperature { get; init; } = 0.1;

    [Range(100, 16384)]
    public int MaxTokens { get; init; } = 1024;
}
