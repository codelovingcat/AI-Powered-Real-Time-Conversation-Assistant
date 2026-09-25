namespace Conversa.Api.Infrastructure;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int GlobalPermitLimit { get; init; } = 120;
    public int AiPermitLimit { get; init; } = 20;
    public int AudioPermitLimit { get; init; } = 10;
    public int WindowSeconds { get; init; } = 60;

    public void Validate()
    {
        if (GlobalPermitLimit <= 0)
            throw new InvalidOperationException("RateLimiting:GlobalPermitLimit must be greater than zero.");

        if (AiPermitLimit <= 0 || AiPermitLimit >= GlobalPermitLimit)
            throw new InvalidOperationException("RateLimiting:AiPermitLimit must be greater than zero and below GlobalPermitLimit.");

        if (AudioPermitLimit <= 0 || AudioPermitLimit >= AiPermitLimit)
            throw new InvalidOperationException("RateLimiting:AudioPermitLimit must be greater than zero and below AiPermitLimit.");

        if (WindowSeconds <= 0)
            throw new InvalidOperationException("RateLimiting:WindowSeconds must be greater than zero.");
    }
}
