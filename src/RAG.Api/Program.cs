using RAG.Api.Endpoints;
using RAG.Application;
using RAG.Application.Services;
using RAG.Infrastructure;
using RAG.Infrastructure.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──
//
// Provider selection (AI:Provider) — same schema and semantics as the MVC host:
//   "Ollama" (default) — local models via http://localhost:11434.
//   "Gemini" — Google Gemini through its OpenAI-compatible endpoint
//     (https://generativelanguage.googleapis.com/v1beta/openai/). Activate with
//     AI:Provider=Gemini plus the API key via env config (AI:Gemini:ApiKey) —
//     NEVER commit the key to appsettings.json or code. Optional overrides:
//     AI:Gemini:ChatModel (default "gemini-2.5-flash"), AI:Gemini:BaseUrl,
//     AI:Gemini:Assistants. Embeddings and reranking stay local (Ollama); only
//     final answer generation uses Gemini.

var aiProvider = builder.Configuration["AI:Provider"] ?? "Ollama";

switch (aiProvider)
{
    case "Ollama":
    {
        // Ollama AI services — same AI:Ollama:* config schema as the MVC host.
        var ollamaBaseUrl = new Uri(builder.Configuration["AI:Ollama:BaseUrl"] ?? "http://localhost:11434");
        var chatModel = builder.Configuration["AI:Ollama:ChatModel"] ?? "phi3:mini";
        var embeddingModel = builder.Configuration["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        // Register manually — AddChatClient / AddEmbeddingGenerator extension methods
        // are in Microsoft.Extensions.AI (not Abstractions) which we don't reference.
        // The chat client is wrapped in the RetryingChatClient decorator (same
        // AI:Ollama:MaxRetries / AI:Ollama:RetryBaseDelayMs schema as the MVC host) so
        // transient Ollama failures retry with exponential backoff instead of failing
        // the request.
        var retryOptions = new RetryOptions
        {
            MaxRetries = builder.Configuration.GetValue("AI:Ollama:MaxRetries", 2),
            BaseDelay = TimeSpan.FromMilliseconds(
                builder.Configuration.GetValue("AI:Ollama:RetryBaseDelayMs", 500)),
        };
        builder.Services.AddSingleton<IChatClient>(
            new RetryingChatClient(new OllamaChatClient(ollamaBaseUrl, chatModel), retryOptions));
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new OllamaEmbeddingGenerator(ollamaBaseUrl, embeddingModel));

        // Assistant catalog (design D1, ASEL-1): config-driven allow-list from
        // AI:Ollama:Assistants — same section the MVC host reads. Absent/empty config
        // falls back to a single default assistant derived from the chat model
        // (backward compatible).
        var assistants = builder.Configuration
            .GetSection("AI:Ollama:Assistants")
            .Get<AssistantDefinition[]>() ?? [];
        builder.Services.AddSingleton(new AssistantCatalog(chatModel, assistants));
        break;
    }

    case "Gemini":
    {
        // Fail fast at startup: without a key every request would fail later
        // with a confusing 502 (same contract as the MVC host). The key must
        // come from env config — NEVER appsettings.json or code. Empty/
        // whitespace counts as missing (an empty key is as useless as an absent
        // one, and it keeps hosts honest when config merges key sources).
        var geminiApiKey = builder.Configuration["AI:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(geminiApiKey))
        {
            throw new InvalidOperationException(
                "AI:Gemini:ApiKey is required when AI:Provider=Gemini. Set it via the AI:Gemini:ApiKey environment variable (never commit the key to appsettings.json).");
        }
        var geminiModel = builder.Configuration["AI:Gemini:ChatModel"] ?? "gemini-2.5-flash";
        var geminiBaseUrl = new Uri(builder.Configuration["AI:Gemini:BaseUrl"]
            ?? "https://generativelanguage.googleapis.com/v1beta/openai/");

        // Gemini exposes an OpenAI-compatible endpoint: the OpenAI SDK pointed at
        // it (OpenAIClientOptions.Endpoint) is adapted to IChatClient by
        // Microsoft.Extensions.AI.OpenAI (AsIChatClient).
        var geminiOpenAi = new OpenAIClient(
            new ApiKeyCredential(geminiApiKey),
            new OpenAIClientOptions { Endpoint = geminiBaseUrl });

        // Retry/backoff resilience (RETRY-1): same decorator and retry schema
        // (AI:Ollama:MaxRetries / AI:Ollama:RetryBaseDelayMs) as the Ollama path.
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

        // Assistant catalog: single Gemini default assistant (AI:Gemini:Assistants
        // may provide more; absent/empty → one "default" entry using geminiModel).
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

// ── Pipeline ──

var app = builder.Build();

app.UseHttpsRedirection();

// ── Endpoints ──

app.MapRagEndpoints();

app.Run();