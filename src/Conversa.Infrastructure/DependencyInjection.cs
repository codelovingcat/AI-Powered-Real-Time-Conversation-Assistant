using Conversa.Application.Abstractions.Persistence;
using Conversa.Application.Ai;
using Conversa.Application.Speech;
using Conversa.Infrastructure.Ai.Gemini;
using Conversa.Infrastructure.Persistence;
using Conversa.Infrastructure.Persistence.Repositories;
using Conversa.Infrastructure.Speech;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Conversa.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = ConnectionStringNormalizer.Normalize(connectionString);
        }
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is required. Set ConnectionStrings__DefaultConnection.");
        }

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        services.AddDataProtection()
            .PersistKeysToDbContext<AppDbContext>();

        services.Configure<GeminiOptions>(configuration.GetSection(GeminiOptions.SectionName));
        var geminiOptions = configuration.GetSection(GeminiOptions.SectionName).Get<GeminiOptions>() ?? new GeminiOptions();
        geminiOptions.Validate();
        services.AddHttpClient<IAiProvider, GeminiAiProvider>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        });

        services.Configure<DeepgramOptions>(configuration.GetSection(DeepgramOptions.SectionName));
        var deepgramOptions =
            configuration.GetSection(DeepgramOptions.SectionName).Get<DeepgramOptions>()
            ?? new DeepgramOptions();

        if (!string.IsNullOrWhiteSpace(deepgramOptions.ApiKey))
        {
            services.AddHttpClient<DeepgramSpeechToTextProvider>(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
            });

            services.AddScoped<ISpeechToTextProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<DeepgramSpeechToTextProvider>());

            services.AddSingleton<ISpeechToTextSessionFactory, DeepgramSpeechToTextSessionFactory>();
        }

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("postgres");
        return services;
    }
}
