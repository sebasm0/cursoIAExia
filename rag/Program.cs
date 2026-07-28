using RAG.Application;
using RAG.Infrastructure;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// ── AI services ──

var aiProvider = builder.Configuration["AI:Provider"] ?? "Ollama";

switch (aiProvider)
{
    case "Ollama":
        var ollamaBaseUrl = new Uri(builder.Configuration["AI:Ollama:BaseUrl"] ?? "http://localhost:11434");
        var chatModel = builder.Configuration["AI:Ollama:ChatModel"] ?? "llama3.2";
        var embeddingModel = builder.Configuration["AI:Ollama:EmbeddingModel"] ?? "nomic-embed-text";

        builder.Services.AddSingleton<IChatClient>(
            new OllamaChatClient(ollamaBaseUrl, chatModel));
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(
            new OllamaEmbeddingGenerator(ollamaBaseUrl, embeddingModel));
        break;

    default:
        throw new NotSupportedException(
            $"AI provider '{aiProvider}' is not supported. Supported providers: 'Ollama'.");
}

// Application & Infrastructure
builder.Services.AddApplication();
builder.Services.AddRagInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
