var builder = WebApplication.CreateBuilder(args);

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

app.Run();