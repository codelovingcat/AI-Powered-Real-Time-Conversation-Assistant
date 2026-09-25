using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Infrastructure.Ai.Gemini;
using Conversa.Infrastructure.Persistence;
using Conversa.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Conversa.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required. Set ConnectionStrings__DefaultConnection.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        var geminiOptions = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
        geminiOptions.Validate();
        services.AddHttpClient<IAiProvider, GeminiAiProvider>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres");
        return services;
    }
}
