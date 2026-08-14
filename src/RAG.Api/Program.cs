using RAG.Api.Endpoints;
using RAG.Application;
using RAG.Application.Services;
using RAG.Infrastructure;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──

// Ollama AI services — same AI:Ollama:* config schema as the MVC host.
var ollamaBaseUrl = new Uri(builder.Configuration["AI:Ollama:BaseUrl"] ?? "http://localhost:11434");
var chatModel = builder.Configuration["AI:Ollama:ChatModel"] ?? "phi3:mini";
var embeddingModel = builder.Configuration["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text";

// Register manually — AddChatClient / AddEmbeddingGenerator extension methods
// are in Microsoft.Extensions.AI (not Abstractions) which we don't reference.
builder.Services.AddSingleton<IChatClient>(
    new OllamaChatClient(ollamaBaseUrl, chatModel));
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
