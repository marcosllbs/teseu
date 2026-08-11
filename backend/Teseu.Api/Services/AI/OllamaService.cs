using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Teseu.Api.Services.AI;

public sealed class OllamaService(
    HttpClient httpClient,
    IOptions<OllamaOptions> options,
    ILogger<OllamaService> logger) : IOllamaService
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<OllamaMessage> ChatAsync(
        IReadOnlyList<OllamaMessage> messages,
        IReadOnlyList<OllamaTool>? tools,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = new OllamaChatRequest(
                _options.Model,
                messages,
                Stream: false,
                Think: _options.EnableThinking,
                Tools: tools,
                Options: new OllamaChatOptions());

            using var response = await httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new AiUnavailableException($"O modelo Ollama configurado ('{_options.Model}') não está instalado.");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama returned HTTP {StatusCode}", (int)response.StatusCode);
                throw new AiUnavailableException("O serviço local de IA não conseguiu processar a solicitação.");
            }

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: cancellationToken);
            return result?.Message ?? throw new AiInvalidResponseException("O serviço local de IA retornou uma resposta inválida.");
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiTimeoutException("O serviço local de IA excedeu o tempo limite.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiUnavailableException("O serviço local de IA está indisponível.", exception);
        }
        catch (JsonException exception)
        {
            throw new AiInvalidResponseException("O serviço local de IA retornou uma resposta inválida.", exception);
        }
    }

}
