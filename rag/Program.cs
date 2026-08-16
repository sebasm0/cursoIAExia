using RAG.Application;
using RAG.Application.Services;
using RAG.Infrastructure;
using RAG.Infrastructure.AI;
using RAG.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// ── AI services ──
//
// Provider selection (AI:Provider):
//   "Ollama" (default) — local models via http://localhost:11434.
//   "Gemini" — Google Gemini through its OpenAI-compatible endpoint
//     (https://generativelanguage.googleapis.com/v1beta/openai/). Activate with:
//       dotnet user-secrets set "AI:Gemini:ApiKey" "<your-key>"   (rag project)
//     then set AI:Provider=Gemini (appsettings or env var). NEVER commit the
//     key to appsettings.json or code. Optional overrides: AI:Gemini:ChatModel
//     (default "gemini-3.6-flash"), AI:Gemini:BaseUrl, AI:Gemini:Assistants.
//     Embeddings and reranking stay local (Ollama); only final answer
//     generation uses Gemini.

var aiProvider = builder.Configuration["AI:Provider"] ?? "Ollama";

switch (aiProvider)
{
    case "Ollama":
        var ollamaBaseUrl = new Uri(builder.Configuration["AI:Ollama:BaseUrl"] ?? "http://localhost:11434");
        var chatModel = builder.Configuration["AI:Ollama:ChatModel"] ?? "phi3:mini";
        var embeddingModel = builder.Configuration["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        // Long local-model generations routinely exceed the .NET default 100s
        // HttpClient timeout; AI:Ollama:TimeoutSeconds makes it configurable
        // (default 300s) so slow CPU inference does not get cancelled.
        var ollamaTimeout = TimeSpan.FromSeconds(
            builder.Configuration.GetValue<int>("AI:Ollama:TimeoutSeconds", 300));
        var ollamaHttp = new HttpClient { Timeout = ollamaTimeout };
        builder.Services.AddKeyedSingleton<HttpClient>("ollama", ollamaHttp);

        // Retry/backoff resilience (RETRY-1): wrap the Ollama chat client in a
        // decorator that retries transient failures (connection drops, HTTP
        // 5xx/429, timeouts) with exponential backoff before giving up, so a
        // temporarily unavailable model no longer fails the user's request.
        // Configurable via AI:Ollama:MaxRetries / AI:Ollama:RetryBaseDelayMs.
        var retryOptions = new RetryOptions
        {
            MaxRetries = builder.Configuration.GetValue("AI:Ollama:MaxRetries", 2),
            BaseDelay = TimeSpan.FromMilliseconds(
                builder.Configuration.GetValue("AI:Ollama:RetryBaseDelayMs", 500)),
        };
        builder.Services.AddSingleton<IChatClient>(
            new RetryingChatClient(new OllamaChatClient(ollamaBaseUrl, chatModel, ollamaHttp), retryOptions));
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new OllamaEmbeddingGenerator(ollamaBaseUrl, embeddingModel));

        // Assistant catalog (design D1, ASEL-1): config-driven allow-list from
        // AI:Ollama:Assistants; absent/empty config falls back to a single
        // default assistant derived from the chat model (backward compatible).
        var assistants = builder.Configuration
            .GetSection("AI:Ollama:Assistants")
            .Get<AssistantDefinition[]>() ?? [];
        builder.Services.AddSingleton(new AssistantCatalog(chatModel, assistants));
        break;

    case "Gemini":
    {
        // Fail fast at startup: without a key every request would fail later
        // with a confusing 502. The key must come from user-secrets / env
        // config — NEVER appsettings.json or code (see the switch doc above).
        // Empty/whitespace counts as missing: an empty key is as useless as an
        // absent one, and it keeps hosts honest when config merges key sources.
        var geminiApiKey = builder.Configuration["AI:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            throw new InvalidOperationException(
                "AI:Gemini:ApiKey is required when AI:Provider=Gemini. Set it via `dotnet user-secrets set \"AI:Gemini:ApiKey\" \"<key>\"` in the rag project.");
        }
        var geminiModel = builder.Configuration["AI:Gemini:ChatModel"] ?? "gemini-3.6-flash";
        var geminiBaseUrl = new Uri(builder.Configuration["AI:Gemini:BaseUrl"]
            ?? "https://generativelanguage.googleapis.com/v1beta/openai/");

        // Gemini exposes an OpenAI-compatible endpoint: the OpenAI SDK pointed at
        // it (OpenAIClientOptions.Endpoint) is adapted to IChatClient by
        // Microsoft.Extensions.AI.OpenAI (AsIChatClient). Streaming (SSE) is
        // supported natively, so the Documents chat keeps working unchanged.
        var geminiOpenAi = new OpenAIClient(
            new ApiKeyCredential(geminiApiKey),
            new OpenAIClientOptions { Endpoint = geminiBaseUrl });

        // Retry/backoff resilience (RETRY-1): same RetryingChatClient decorator
        // as Ollama, reusing the shared AI:Ollama:MaxRetries /
        // AI:Ollama:RetryBaseDelayMs keys so operators keep one retry schema.
        var geminiRetryOptions = new RetryOptions
        {
            MaxRetries = builder.Configuration.GetValue("AI:Ollama:MaxRetries", 2),
            BaseDelay = TimeSpan.FromMilliseconds(
                builder.Configuration.GetValue("AI:Ollama:RetryBaseDelayMs", 500)),
        };
        builder.Services.AddSingleton<IChatClient>(
            new RetryingChatClient(
                geminiOpenAi.GetChatClient(geminiModel).AsIChatClient(),
                geminiRetryOptions));

        // Embeddings stay LOCAL (Ollama) — only final generation uses Gemini;
        // keeping the embedding model stable preserves stored vectors (ASEL-5).
        var geminiOllamaBaseUrl = new Uri(builder.Configuration["AI:Ollama:BaseUrl"]
            ?? "http://localhost:11434");
        var geminiEmbeddingModel = builder.Configuration["AI:Ollama:EmbeddingModel"]
            ?? "nomic-embed-text";
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new OllamaEmbeddingGenerator(geminiOllamaBaseUrl, geminiEmbeddingModel));

        // Assistant catalog (design D1): AI:Gemini:Assistants allow-list; absent
        // or empty config falls back to a single default assistant using the
        // Gemini model (NOT the Ollama default model).
        var geminiAssistants = builder.Configuration
            .GetSection("AI:Gemini:Assistants")
            .Get<AssistantDefinition[]>() ?? [];
        builder.Services.AddSingleton(new AssistantCatalog(geminiModel, geminiAssistants));
        break;
    }

    default:
        throw new NotSupportedException(
            $"AI provider '{aiProvider}' is not supported. Supported providers: 'Ollama', 'Gemini'.");
}

// Application & Infrastructure
builder.Services.AddApplication();
builder.Services.AddRagInfrastructure(builder.Configuration);

// Identity + RBAC (design D6): cookie auth, permission policies, claims factory.
builder.Services.AddRagIdentity(builder.Configuration);

var app = builder.Build();

// ── Identity startup: migrate + idempotent seed (design D2) ──
// Guarded by Identity:ApplyMigrationsOnStartup (default true) so tests and
// local dev can opt out without touching a real PostgreSQL database.
if (builder.Configuration.GetValue<bool>("Identity:ApplyMigrationsOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    var identityContext = scope.ServiceProvider.GetRequiredService<AppIdentityDbContext>();
    await identityContext.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
    await seeder.SeedAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
