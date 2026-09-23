using System.Diagnostics;
using Conversa.Application.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Conversa.Api.Infrastructure;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var mapped = Map(exception);
        if (mapped is null)
        {
            return false;
        }

        if (mapped.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Request failed with status {StatusCode}.", mapped.Status);
        }
        else
        {
            logger.LogInformation(exception, "Request rejected with status {StatusCode}.", mapped.Status);
        }

        var problem = new ProblemDetails
        {
            Status = mapped.Status,
            Title = mapped.Title,
            Detail = mapped.Detail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        if (mapped.Errors is not null)
        {
            httpContext.Response.StatusCode = mapped.Status;
            await httpContext.Response.WriteAsJsonAsync(
                new HttpValidationProblemDetails(mapped.Errors)
                {
                    Status = mapped.Status,
                    Title = mapped.Title,
                    Detail = mapped.Detail,
                    Instance = httpContext.Request.Path
                },
                cancellationToken);
            return true;
        }

        httpContext.Response.StatusCode = mapped.Status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }

    private static MappedException? Map(Exception exception) => exception switch
    {
        NotFoundException notFound => new(StatusCodes.Status404NotFound, "Not found", notFound.Message, null),
        ValidationException validation => new(
            StatusCodes.Status400BadRequest,
            "Validation failed",
            validation.Message,
            validation.Errors),
        AiProviderNotConfiguredException notConfigured => new(
            StatusCodes.Status503ServiceUnavailable,
            "AI provider is not configured",
            notConfigured.Message,
            null),
        AiProviderException provider => new(
            StatusCodes.Status502BadGateway,
            "AI provider request failed",
            provider.Message,
            null),
        SpeechToTextNotConfiguredException speech => new(
            StatusCodes.Status501NotImplemented,
            "Speech-to-text is not configured",
            speech.Message,
            null),
        _ => null
    };

    private sealed record MappedException(
        int Status,
        string Title,
        string Detail,
        IReadOnlyDictionary<string, string[]>? Errors);
}
