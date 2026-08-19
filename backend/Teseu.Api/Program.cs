using Microsoft.Extensions.Options;
using Teseu.Api.Models;
using Teseu.Api.Services;
using Teseu.Api.Services.AI;
using Teseu.Api.Services.AI.Tools;
using Teseu.Api.Services.Jarvis;
using Teseu.Api.Services.Jarvis.Tools;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration binding ---

builder.Services
    .AddOptions<JarvisOptions>()
    .Bind(builder.Configuration.GetSection(JarvisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<OpenAiOptions>()
    .Bind(builder.Configuration.GetSection(OpenAiOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName))
    .ValidateDataAnnotations();

// --- HTTP Clients ---

builder.Services.AddHttpClient<PrometheusService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Prometheus:BaseUrl"]
        ?? throw new InvalidOperationException("Prometheus:BaseUrl is required."));
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Prometheus:TimeoutSeconds", 10));
});

// OpenAI HTTP client
builder.Services.AddHttpClient<OpenAiProvider>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<OpenAiOptions>>().Value;
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
});

// Ollama HTTP client (legacy)
builder.Services.AddHttpClient<IOllamaService, OllamaService>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

// --- AI Provider registration (based on config) ---

builder.Services.AddScoped<OpenAiProvider>();
builder.Services.AddScoped<OllamaLegacyProvider>();
builder.Services.AddScoped<IJarvisAiProvider>(sp =>
{
    var jarvisOptions = sp.GetRequiredService<IOptions<JarvisOptions>>().Value;
    return jarvisOptions.IsOpenAi
        ? sp.GetRequiredService<OpenAiProvider>()
        : sp.GetRequiredService<OllamaLegacyProvider>();
});

// --- Jarvis Tools (new interface with PermissionLevel) ---

builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetServerStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetCpuStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetMemoryStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetStorageStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetNetworkStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetTemperatureStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetUptimeStatusTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetContainersTool>();
builder.Services.AddScoped<IJarvisTool, Teseu.Api.Services.Jarvis.Tools.GetContainerStatusTool>();

// --- Orchestrator ---

builder.Services.AddScoped<JarvisOrchestrator>();

// --- Legacy (preserved for backward compatibility during migration) ---

builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetServerStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetCpuStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetMemoryStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetStorageStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetNetworkStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetTemperatureStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetUptimeStatusTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetContainersTool>();
builder.Services.AddScoped<IAiTool, Teseu.Api.Services.AI.Tools.GetContainerStatusTool>();
builder.Services.AddScoped<TeseuAiService>();

var app = builder.Build();

// ==================== Endpoints ====================

// Health check
app.MapGet("/", () => Results.Ok(new
{
    name = "Jarvis API",
    status = "online",
    version = "1.0.0"
}));

// Server status (direct Prometheus query, no AI)
app.MapGet("/api/server/status", async (PrometheusService prometheus, CancellationToken ct) =>
    Results.Ok(await prometheus.GetServerStatusAsync(ct)));

// --- NEW: Jarvis chat endpoint (OpenAI-based) ---
app.MapPost("/api/jarvis/chat", async (AiChatRequest request, JarvisOrchestrator jarvis, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new ApiError("A mensagem é obrigatória."));

    if (request.Message.Length > 2000)
        return Results.BadRequest(new ApiError("A mensagem deve ter no máximo 2000 caracteres."));

    try
    {
        var response = await jarvis.ChatAsync(request.Message, ct);
        return Results.Ok(response);
    }
    catch (AiTimeoutException ex)
    {
        return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (AiUnavailableException ex)
    {
        return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (AiInvalidResponseException ex)
    {
        return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status502BadGateway);
    }
});

// --- LEGACY: Original chat endpoint (Ollama-based, preserved during migration) ---
app.MapPost("/api/ai/chat", async (AiChatRequest request, TeseuAiService ai, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new ApiError("A mensagem é obrigatória."));

    if (request.Message.Length > 2000)
        return Results.BadRequest(new ApiError("A mensagem deve ter no máximo 2000 caracteres."));

    try
    {
        return Results.Ok(await ai.ChatAsync(request.Message, ct));
    }
    catch (AiTimeoutException ex)
    {
        return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (AiUnavailableException ex)
    {
        return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (AiInvalidResponseException ex)
    {
        return Results.Json(new ApiError(ex.Message), statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

// Make Program accessible for integration tests
public partial class Program { }
