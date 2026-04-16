using OllamaSharp;

var builder = WebApplication.CreateBuilder(args);

// 1. Configurando o HttpClient e o OllamaSharp via Injeção de Dependência
builder.Services.AddHttpClient("OllamaClient", client =>
{
    client.BaseAddress = new Uri("http://localhost:11434");
});

// Registramos como "Scoped" (um por requisição) para podermos alterar o SelectedModel 
// sem causar conflitos entre requisições simultâneas.
builder.Services.AddScoped<IOllamaApiClient>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("OllamaClient");
    return new OllamaApiClient(httpClient);
});

var app = builder.Build();

// 2. Middleware Global de Validação da API_KEY (Mantido igual)
app.Use(async (context, next) =>
{
    var expectedApiKey = app.Configuration.GetValue<string>("API_KEY") ?? "chave-secreta-padrao";

    if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey) ||
        extractedApiKey != expectedApiKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized: API Key ausente ou inválida.");
        return;
    }

    await next(context);
});

// 3. Rota GET: Retornar modelos disponíveis usando OllamaSharp
app.MapGet("/api/models", async (IOllamaApiClient ollama) =>
{
    var models = await ollama.ListLocalModelsAsync();

    var availableModels = models.Any()
        ? string.Join(", ", models.Select(m => m.Name))
        : "Nenhum modelo encontrado.";

    return Results.Ok(availableModels);
});

// 4. Rota POST: Enviar o prompt ao modelo usando OllamaSharp
app.MapPost("/api/generate", async (PromptRequest request, IOllamaApiClient ollama, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(request.Prompt))
        return Results.BadRequest("O atributo 'prompt' é obrigatório.");

    // Define o modelo (usa o da sua PoC como padrão caso não tenha no appsettings.json)
    ollama.SelectedModel = config.GetValue<string>("OllamaModel") ?? "qwen2.5:0.5b";

    var chat = new Chat(ollama);
    var fullResponse = string.Empty;

    // A sua PoC retorna a resposta em formato de stream (pedaço por pedaço).
    // Como a nossa rota retorna uma string única no final, nós acumulamos o resultado aqui.
    await foreach (var answer in chat.SendAsync(request.Prompt))
    {
        fullResponse += answer;
    }

    return Results.Ok(new { response = fullResponse });
});

app.Run();

// --- Record para o Body da Requisição ---
public record PromptRequest(string Prompt);