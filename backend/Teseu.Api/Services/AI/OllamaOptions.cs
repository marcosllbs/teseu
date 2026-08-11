using System.ComponentModel.DataAnnotations;

namespace Teseu.Api.Services.AI;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    [Required, Url]
    public string BaseUrl { get; init; } = string.Empty;

    [Required]
    public string Model { get; init; } = string.Empty;

    [Range(5, 300)]
    public int TimeoutSeconds { get; init; } = 180;

    public bool EnableThinking { get; init; }
}
