using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conversa.Api.Identity;
using Conversa.Api.Infrastructure;
using Conversa.Api.Realtime;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Conversations;
using Conversa.Infrastructure;
using Conversa.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var authenticationIssuer = builder.Configuration["Authentication:Issuer"];
var authenticationAudience = builder.Configuration["Authentication:Audience"];
var authenticationSigningKey = builder.Configuration["Authentication:SigningKey"];

if (string.IsNullOrWhiteSpace(authenticationIssuer)
    || string.IsNullOrWhiteSpace(authenticationAudience)
    || string.IsNullOrWhiteSpace(authenticationSigningKey))
{
    throw new InvalidOperationException(
        "Authentication:Issuer, Authentication:Audience, and Authentication:SigningKey must be configured outside source control.");
}

byte[] signingKeyBytes;
try
{
    signingKeyBytes = Convert.FromBase64String(authenticationSigningKey);
}
catch (FormatException exception)
{
    throw new InvalidOperationException(
        "Authentication:SigningKey must be a valid base64 value.",
        exception);
}

if (signingKeyBytes.Length < 32)
{
    throw new InvalidOperationException(
        "Authentication:SigningKey must contain at least 32 bytes.");
}

var rateLimitOptions = new RateLimitOptions();
builder.Configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimitOptions);
rateLimitOptions.Validate();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.Configure<AudioWebSocketOptions>(builder.Configuration.GetSection(AudioWebSocketOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HeaderCurrentUser>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IConversationAssistant, ConversationAssistant>();
builder.Services.AddScoped<ConversationInputValidator>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "rate_limit_exceeded",
                message = "Too many requests. Please try again later."
            },
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimitOptions.GlobalPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds)
            }));

    options.AddPolicy("ai", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimitOptions.AiPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds)
            }));

    options.AddPolicy("audio", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetRateLimitPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = rateLimitOptions.AudioPermitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds)
            }));
});

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseRouting();
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

static string GetRateLimitPartitionKey(HttpContext context)
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.User.FindFirstValue("sub");

    if (!string.IsNullOrWhiteSpace(userId))
        return $"user:{userId}";

    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

public partial class Program;
