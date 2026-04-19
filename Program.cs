using Microsoft.SemanticKernel.Embeddings;
using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

var ollamaUrl = builder.Configuration["OLLAMA_ENDPOINT"] ?? "http://localhost:11434";

builder.Services.AddHttpClient("OllamaClient", client =>
{
    client.BaseAddress = new Uri(ollamaUrl);
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

var path = "/llm-api/v1";

app.MapGet(path, async (IOllamaApiClient ollama) =>
{
    var models = await ollama.ListLocalModelsAsync();

    var modelList = models.Any()
          ? models.Select(m => m.Name).ToList()
          : new List<string>();

    return Results.Ok(new { Models = modelList });
});

app.MapPost(path, async (PromptRequest request, IOllamaApiClient ollama, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(request.prompt) || string.IsNullOrWhiteSpace(request.model))
    {
        return Results.BadRequest("The 'model' and 'prompt' attributes are required.");
    }

    ollama.SelectedModel = request.model;

    try
    {
        var chat = new Chat(ollama);
        var fullResponse = string.Empty;

        await foreach (var answer in chat.SendAsync(request.prompt))
        {
            fullResponse += answer;
        }

        return Results.Ok(new
        {
            model = request.model,
            response = fullResponse
        });
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = $"Model '{request.model}' not found in Ollama server." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"An error occurred while processing the LLM request: {ex.Message}");
    }
});

app.MapPost($"{path}/stream", async (PromptRequest request, IOllamaApiClient ollama) =>
{
    if (string.IsNullOrWhiteSpace(request.prompt) || string.IsNullOrWhiteSpace(request.model))
        return Results.BadRequest("The 'model' and 'prompt' attributes are required.");

    ollama.SelectedModel = request.model;

    // Criamos o stream como uma função local
    async IAsyncEnumerable<string> GenerateStream()
    {
        var chat = new Chat(ollama);

        // O try-catch DEVE envolver o loop de consumo
        IAsyncEnumerator<string> enumerator = chat.SendAsync(request.prompt).GetAsyncEnumerator();

        try
        {
            while (await enumerator.MoveNextAsync())
            {
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
        // Se houver erro de conexão aqui, o cliente verá a conexão ser interrompida abruptamente,
        // o que é o comportamento correto para streams HTTP.
    }

    try
    {
        // Teste rápido: Verificamos se o modelo existe ANTES de começar o stream
        var models = await ollama.ListLocalModelsAsync();
        if (!models.Any(m => m.Name == request.model))
        {
            return Results.NotFound(new { error = $"Model '{request.model}' not found." });
        }

        return Results.Ok(GenerateStream());
    }
    catch (Exception ex)
    {
        return Results.Problem($"Erro ao iniciar o Ollama: {ex.Message}");
    }
});

app.MapPost($"{path}/embeddings", async (PromptRequest request, IOllamaApiClient ollama) =>
{
    if (string.IsNullOrWhiteSpace(request.prompt))
    {
        return Results.BadRequest("The 'prompt' attributes are required.");
    }
    
    var embeddingDefaultModel = "mxbai-embed-large";
    
    ollama.SelectedModel = string.IsNullOrEmpty(request.model) ? embeddingDefaultModel : request.model;
    
    try
    {
        var service = ((OllamaApiClient)ollama).AsTextEmbeddingGenerationService();
        var embeddings = await service.GenerateEmbeddingAsync(request.prompt);

        return Results.Ok(new
        {
            model = ollama.SelectedModel,
            embeddings = embeddings
        });
    }
    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        return Results.NotFound(new { error = $"Model '{request.model}' not found in Ollama server." });
    }
    catch (Exception ex)
    {
        return Results.Problem($"An error occurred while generating the embedding: {ex.Message}");
    }
});

app.Run();

public record PromptRequest(string model, string prompt);