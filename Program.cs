using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("OllamaClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
});

builder.Services.AddScoped<IOllamaApiClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("OllamaClient");
    return new OllamaApiClient(httpClient);
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var expectedApiKey = app.Configuration.GetValue<string>("API_KEY");

    if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey) ||
        extractedApiKey != expectedApiKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized: The API Key is missing or invalid.");
        return;
    }

    await next(context);
});

app.MapGet("/llm-api/v1", async (IOllamaApiClient ollama) =>
{
    var models = await ollama.ListLocalModelsAsync();

    var modelList = models.Any()
          ? models.Select(m => m.Name).ToList()
          : new List<string>();

    return Results.Ok(new { Models = modelList });
});

app.MapPost("/llm-api/v1", async (PromptRequest request, IOllamaApiClient ollama, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Model))
    {
        return Results.BadRequest("The 'model' and 'prompt' attributes are required.");
    }

    ollama.SelectedModel = request.Model;

    try
    {
        var chat = new Chat(ollama);
        var fullResponse = string.Empty;

        await foreach (var answer in chat.SendAsync(request.Prompt))
        {
            fullResponse += answer;
        }

        return Results.Ok(new { response = fullResponse });
    }
    catch (Exception ex)
    {
        return Results.Problem($"An error occurred while processing the LLM request: {ex.Message}");
    }
});

app.Run();

public record PromptRequest(string Model, string Prompt);