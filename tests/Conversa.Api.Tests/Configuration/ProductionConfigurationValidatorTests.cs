using Conversa.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Conversa.Api.Tests.Configuration;

public sealed class ProductionConfigurationValidatorTests
{
    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["ConnectionStrings:DefaultConnection"] =
            "Host=db.example.internal;Database=conversa;Username=conversa;Password=production-secret",
        ["Authentication:Issuer"] = "https://identity.example.com",
        ["Authentication:Audience"] = "conversa-api",
        ["Authentication:SigningKey"] =
            Convert.ToBase64String(Encoding.UTF8.GetBytes("01234567890123456789012345678901")),
        ["Gemini:ApiKey"] = "gemini-test-secret",
        ["Gemini:BaseUrl"] = "https://generativelanguage.googleapis.com/",
        ["Deepgram:ApiKey"] = "deepgram-test-secret",
        ["Deepgram:BaseUrl"] = "https://api.deepgram.com/",
        ["RateLimiting:RedisConnectionString"] = "rediss://redis.example.com:6379",
        ["Cors:AllowedOrigins"] = "https://app.example.com"
    };

    [Fact]
    public void Development_does_not_require_production_secrets()
    {
        var configuration = new ConfigurationBuilder().Build();

        ProductionConfigurationValidator.Validate(configuration, enforceProductionRules: false);
    }

    [Fact]
    public void Missing_production_secret_fails_without_revealing_a_secret_value()
    {
        var settings = ValidSettings();
        settings.Remove("Gemini:ApiKey");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, enforceProductionRules: true));

        Assert.Contains("Gemini:ApiKey", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("production-secret", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("gemini-test-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_or_weak_signing_key_fails()
    {
        var settings = ValidSettings();
        settings["Authentication:SigningKey"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("too-short"));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, enforceProductionRules: true));

        Assert.Contains("Authentication:SigningKey", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Insecure_cors_origin_fails()
    {
        var settings = ValidSettings();
        settings["Cors:AllowedOrigins"] = "http://localhost:5173";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, enforceProductionRules: true));

        Assert.Contains("Cors:AllowedOrigins", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_local_database_defaults_are_rejected()
    {
        var settings = ValidSettings();
        settings["ConnectionStrings:DefaultConnection"] =
            "Host=localhost;Database=conversa;Username=conversa;Password=conversa";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, enforceProductionRules: true));

        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=conversa", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_endpoints_must_not_embed_credentials()
    {
        var settings = ValidSettings();
        settings["Gemini:BaseUrl"] = "https://user:password@generativelanguage.googleapis.com/";

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, enforceProductionRules: true));

        Assert.Contains("Gemini:BaseUrl", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
