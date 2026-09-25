using System.Text.Json;
using System.Text.Json.Serialization;
using Conversa.Api.Identity;
using Conversa.Api.Infrastructure;
using Conversa.Api.Realtime;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Conversations;
using Conversa.Infrastructure;
using Conversa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var rateLimitOptions = builder.Configuration
    .GetSection(RateLimitOptions.SectionName)
    .Get<RateLimitOptions>() ?? new RateLimitOptions();

rateLimitOptions.Validate();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = rateLimitOptions.WindowSeconds.ToString();
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "rate_limit_exceeded",
            message = "Too many requests. Please try again later."
        }, cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var userId = httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var key = !string.IsNullOrWhiteSpace(userId)
            ? $"user:{userId}"
            : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

        return RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });

    options.AddPolicy("ai", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.AiPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("audio", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.AudioPermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

static string GetRateLimitKey(HttpContext httpContext)
{
    var userId = httpContext.User.FindFirst("sub")?.Value
        ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    return !string.IsNullOrWhiteSpace(userId)
        ? $"user:{userId}"
        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HeaderCurrentUser>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IConversationAssistant, ConversationAssistant>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseRateLimiter();
app.UseWebSockets();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapAudioWebSocket();
app.MapGet("/", () => Results.Json(new
{
    name = "Conversa",
    message = "Conversa API is running.",
    status = "ok",
    health = "/health",
    conversations = "/api/conversations",
    assistant = "/api/conversations/{id}/assistant",
    speech = "/api/speech/provider",
    audioWebSocket = "/ws/conversations/{id}/audio"
}));

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program;
