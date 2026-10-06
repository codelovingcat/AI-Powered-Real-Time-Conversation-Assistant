using Conversa.Api.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Conversa.Api.Tests.Configuration;

public sealed class ObservabilityOptionsTests
{
    [Fact]
    public void Disabled_observability_accepts_missing_endpoint()
    {
        var options = new ObservabilityOptions
        {
            Enabled = false,
            ServiceName = string.Empty
        };

        options.Validate(requireSecureEndpoint: true);
    }

    [Fact]
    public void Enabled_observability_requires_service_name()
    {
        var options = new ObservabilityOptions
        {
            Enabled = true,
            ServiceName = string.Empty
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.Validate(requireSecureEndpoint: true));

        Assert.Contains("Observability:ServiceName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Enabled_observability_accepts_https_otlp_endpoint()
    {
        var options = new ObservabilityOptions
        {
            Enabled = true,
            ServiceName = "conversa-api",
            OtlpEndpoint = "https://otel.example.com:4317"
        };

        options.Validate(requireSecureEndpoint: true);
    }

    [Fact]
    public void Production_rejects_http_otlp_endpoint()
    {
        var options = new ObservabilityOptions
        {
            Enabled = true,
            ServiceName = "conversa-api",
            OtlpEndpoint = "http://otel.example.com:4317"
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.Validate(requireSecureEndpoint: true));

        Assert.Contains("Observability:OtlpEndpoint", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_can_use_http_otlp_endpoint()
    {
        var options = new ObservabilityOptions
        {
            Enabled = true,
            ServiceName = "conversa-api",
            OtlpEndpoint = "http://localhost:4317"
        };

        options.Validate(requireSecureEndpoint: false);
    }

    [Fact]
    public void Otlp_endpoint_cannot_embed_credentials()
    {
        var options = new ObservabilityOptions
        {
            Enabled = true,
            ServiceName = "conversa-api",
            OtlpEndpoint = "https://user:secret@otel.example.com:4317"
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => options.Validate(requireSecureEndpoint: true));

        Assert.Contains("Observability:OtlpEndpoint", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
