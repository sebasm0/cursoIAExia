using RAG.Api.Endpoints;
using RAG.Application;
using RAG.Infrastructure;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──

// Ollama AI services
var ollamaBaseUrl = new Uri(builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434");
var chatModel = builder.Configuration["Ollama:ChatModel"] ?? "llama3.2";
var embeddingModel = builder.Configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

// Register manually — AddChatClient / AddEmbeddingGenerator extension methods
// are in Microsoft.Extensions.AI (not Abstractions) which we don't reference.
builder.Services.AddSingleton<IChatClient>(
    new OllamaChatClient(ollamaBaseUrl, chatModel));
builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
    new OllamaEmbeddingGenerator(ollamaBaseUrl, embeddingModel));

// Application & Infrastructure
builder.Services.AddApplication();
builder.Services.AddRagInfrastructure(builder.Configuration);

// ── Pipeline ──

var app = builder.Build();

app.UseHttpsRedirection();

// ── Endpoints ──

app.MapRagEndpoints();

app.Run();
