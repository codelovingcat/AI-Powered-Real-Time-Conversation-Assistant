using System.Text;
using Microsoft.Extensions.Configuration;

namespace Conversa.Api.Infrastructure;

public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, bool enforceProductionRules)
    {
        if (!enforceProductionRules)
            return;

        Require(configuration, "ConnectionStrings:DefaultConnection");
        Require(configuration, "Authentication:Issuer");
        Require(configuration, "Authentication:Audience");
        Require(configuration, "Authentication:SigningKey");
        Require(configuration, "Gemini:ApiKey");
        Require(configuration, "Deepgram:ApiKey");
        Require(configuration, "RateLimiting:RedisConnectionString");

        ValidateSigningKey(configuration["Authentication:SigningKey"]!);
        ValidateHttpsEndpoint(configuration["Gemini:BaseUrl"], "Gemini:BaseUrl");
        ValidateHttpsEndpoint(configuration["Deepgram:BaseUrl"], "Deepgram:BaseUrl");
        ValidateCorsOrigins(configuration["Cors:AllowedOrigins"]);
        RejectKnownLocalDatabaseDefaults(configuration["ConnectionStrings:DefaultConnection"]);
    }

    private static void Require(IConfiguration configuration, string key)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
        {
            throw new InvalidOperationException(
                $"{key} must be configured outside Development and Testing.");
        }
    }

    private static void ValidateSigningKey(string value)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Authentication:SigningKey must be a valid base64 value.");
        }

        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Authentication:SigningKey must contain at least 32 bytes.");
        }

        CryptographicOperations.ZeroMemory(bytes);
    }

    private static void ValidateHttpsEndpoint(string? value, string key)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"{key} must be an absolute HTTPS URI without embedded credentials.");
        }
    }

    private static void ValidateCorsOrigins(string? value)
    {
        var origins = (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (origins.Length == 0)
        {
            throw new InvalidOperationException(
                "Cors:AllowedOrigins must contain at least one exact HTTPS origin.");
        }

        foreach (var origin in origins)
        {
            if (origin == "*"
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(uri.UserInfo)
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException(
                    "Cors:AllowedOrigins must contain only exact HTTPS origins.");
            }
        }
    }

    private static void RejectKnownLocalDatabaseDefaults(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalized = value.Trim();

        if (normalized.Contains("Password=conversa", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Password=build-only", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Password=<local-dev-password>", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection must not use a local development database credential.");
        }
    }
}
