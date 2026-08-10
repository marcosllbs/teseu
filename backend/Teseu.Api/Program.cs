using Teseu.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<PrometheusService>(client =>
{
    client.BaseAddress = new Uri("http://prometheus:9090");
});

var app = builder.Build();

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        name = "Teseu API",
        status = "online",
        version = "0.1.0"
    });
});

app.MapGet("/api/server/status", async (PrometheusService prometheus) =>
{
    var status = await prometheus.GetServerStatusAsync();

    return Results.Ok(status);
});

app.Run();
