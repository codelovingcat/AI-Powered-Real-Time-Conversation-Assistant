namespace Conversa.Api.Infrastructure;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; init; }

    public string ServiceName { get; init; } = "conversa-api";

    public string? OtlpEndpoint { get; init; }

    public void Validate(bool requireSecureEndpoint)
    {
        if (string.IsNullOrWhiteSpace(ServiceName))
        {
            throw new InvalidOperationException(
                "Observability:ServiceName must be configured when observability is enabled.");
        }

        if (string.IsNullOrWhiteSpace(OtlpEndpoint))
            return;

        if (!Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException(
                "Observability:OtlpEndpoint must be an absolute HTTP or HTTPS URI without embedded credentials.");
        }

        if (requireSecureEndpoint && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Observability:OtlpEndpoint must use HTTPS outside Development and Testing.");
        }
    }
}

