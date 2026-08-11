using System.Text.Json;

namespace Teseu.Api.Services.AI.Tools;

public interface IAiTool
{
    string Name { get; }
    string Description { get; }
    JsonElement Parameters { get; }
    Task<object?> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
