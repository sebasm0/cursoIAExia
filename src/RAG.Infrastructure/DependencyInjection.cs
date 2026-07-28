using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RAG.Domain.Abstractions;
using RAG.Infrastructure.Chunking;
using RAG.Infrastructure.Parsing;
using RAG.Infrastructure.Reranking;
using RAG.Infrastructure.VectorStore;

namespace RAG.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRagInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgreSQL is required");

        services.AddSingleton<IVectorStore>(_ => new PgVectorStore(connectionString));
        services.AddSingleton<IChunker, SemanticChunker>();
        services.AddSingleton<IReranker, OllamaReranker>();

        // Document parsers — register as a collection
        services.AddSingleton<IDocumentParser, MarkdownParser>();
        services.AddSingleton<IDocumentParser, CodeParser>();
        services.AddSingleton<IDocumentParser, PdfParser>();

        return services;
    }
}
