using Microsoft.Extensions.DependencyInjection;
using RAG.Application.Services;

namespace RAG.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IngestionService>();
        services.AddSingleton<RagService>();
        services.AddSingleton<ChatHistoryService>();

        return services;
    }
}
