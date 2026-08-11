using Microsoft.Extensions.Options;
using Teseu.Api.Models;
using Teseu.Api.Services;
using Teseu.Api.Services.AI;
using Teseu.Api.Services.AI.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<PrometheusService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Prometheus:BaseUrl"]
        ?? throw new InvalidOperationException("Prometheus:BaseUrl is required."));
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Prometheus:TimeoutSeconds", 10));
});

builder.Services.AddHttpClient<IOllamaService, OllamaService>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddScoped<IAiTool, GetServerStatusTool>();
builder.Services.AddScoped<IAiTool, GetCpuStatusTool>();
builder.Services.AddScoped<IAiTool, GetMemoryStatusTool>();
builder.Services.AddScoped<IAiTool, GetStorageStatusTool>();
builder.Services.AddScoped<IAiTool, GetNetworkStatusTool>();
builder.Services.AddScoped<IAiTool, GetTemperatureStatusTool>();
builder.Services.AddScoped<IAiTool, GetUptimeStatusTool>();
builder.Services.AddScoped<IAiTool, GetContainersTool>();
builder.Services.AddScoped<IAiTool, GetContainerStatusTool>();
builder.Services.AddScoped<TeseuAiService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    name = "Teseu API",
    status = "online",
    version = "0.2.0"
}));

app.MapGet("/api/server/status", async (PrometheusService prometheus, CancellationToken cancellationToken) =>
    Results.Ok(await prometheus.GetServerStatusAsync(cancellationToken)));

app.MapPost("/api/ai/chat", async (AiChatRequest request, TeseuAiService ai, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new ApiError("A mensagem é obrigatória."));

    if (request.Message.Length > 2000)
        return Results.BadRequest(new ApiError("A mensagem deve ter no máximo 2000 caracteres."));

    try
    {
        return Results.Ok(await ai.ChatAsync(request.Message, cancellationToken));
    }
    catch (AiTimeoutException exception)
    {
        return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (AiUnavailableException exception)
    {
        return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (AiInvalidResponseException exception)
    {
        return Results.Json(new ApiError(exception.Message), statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
