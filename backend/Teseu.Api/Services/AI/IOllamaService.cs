namespace Teseu.Api.Services.AI;

public interface IOllamaService
{
    Task<OllamaMessage> ChatAsync(
        IReadOnlyList<OllamaMessage> messages,
        IReadOnlyList<OllamaTool>? tools,
        CancellationToken cancellationToken);
}
