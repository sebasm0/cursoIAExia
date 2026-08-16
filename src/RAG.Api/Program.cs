using RAG.Api.Endpoints;
using RAG.Application;
using RAG.Application.Services;
using RAG.Infrastructure;
using RAG.Infrastructure.AI;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──

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

// Application & Infrastructure
builder.Services.AddApplication();
builder.Services.AddRagInfrastructure(builder.Configuration);

// ── Pipeline ──

var app = builder.Build();

app.UseHttpsRedirection();

// ── Endpoints ──

app.MapRagEndpoints();

app.Run();
