using RAG.Application;
using RAG.Application.Services;
using RAG.Infrastructure;
using RAG.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
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

        // Assistant catalog (design D1, ASEL-1): config-driven allow-list from
        // AI:Ollama:Assistants; absent/empty config falls back to a single
        // default assistant derived from the chat model (backward compatible).
        var assistants = builder.Configuration
            .GetSection("AI:Ollama:Assistants")
            .Get<AssistantDefinition[]>() ?? [];
        builder.Services.AddSingleton(new AssistantCatalog(chatModel, assistants));
        break;

    default:
        throw new NotSupportedException(
            $"AI provider '{aiProvider}' is not supported. Supported providers: 'Ollama'.");
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
