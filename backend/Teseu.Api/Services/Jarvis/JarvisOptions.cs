using System.ComponentModel.DataAnnotations;

namespace Teseu.Api.Services.Jarvis;

public sealed class JarvisOptions
{
    public const string SectionName = "Jarvis";

    /// <summary>AI provider to use: "openai" or "ollama"</summary>
    [Required]
    public string Provider { get; init; } = "openai";

    /// <summary>Maximum tool-calling iterations before stopping.</summary>
    [Range(1, 20)]
    public int MaxToolIterations { get; init; } = 5;

    public bool IsOpenAi => Provider.Equals("openai", StringComparison.OrdinalIgnoreCase);
}
