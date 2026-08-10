using System.Text.Json;

namespace Teseu.Api.Services;

public class PrometheusService
{
    private readonly HttpClient _httpClient;

    public PrometheusService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string?> GetNodeNameAsync()
    {
        const string query = "node_uname_info";

        var response = await _httpClient.GetAsync(
            $"/api/v1/query?query={Uri.EscapeDataString(query)}"
        );

        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(content);

        var result = document.RootElement
            .GetProperty("data")
            .GetProperty("result");

        if (result.GetArrayLength() == 0)
            return null;

        return result[0]
            .GetProperty("metric")
            .GetProperty("nodename")
            .GetString();
    }
}