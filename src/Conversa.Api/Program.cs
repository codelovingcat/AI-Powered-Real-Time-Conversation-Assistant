using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Conversa.Api.Identity;
using Conversa.Api.Infrastructure;
using Conversa.Api.Infrastructure.RateLimiting;
using Conversa.Api.Realtime;
using Conversa.Application.Abstractions.Identity;
using Conversa.Application.Conversations;
using Conversa.Application.Observability;
using Conversa.Application.Ai;
using Conversa.Api.Testing;
using Conversa.Infrastructure;
using Conversa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using StackExchange.Redis;

const string PersistentSessionScheme = "Conversa.Session";

var builder = WebApplication.CreateBuilder(args);

var observabilityOptions = new ObservabilityOptions();
builder.Configuration
    .GetSection(ObservabilityOptions.SectionName)
    .Bind(observabilityOptions);
observabilityOptions.Validate(
    requireSecureEndpoint: !builder.Environment.IsDevelopment()
        && !builder.Environment.IsEnvironment("Testing"));

if (observabilityOptions.Enabled)
{
    var openTelemetry = builder.Services
        .AddOpenTelemetry()
        .ConfigureResource(resource =>
            resource.AddService(observabilityOptions.ServiceName));

    openTelemetry.WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(ConversaTelemetry.ActivitySourceName);

        tracing.AddOtlpExporter(exporter =>
        {
            if (!string.IsNullOrWhiteSpace(observabilityOptions.OtlpEndpoint))
            {
                exporter.Endpoint = new Uri(observabilityOptions.OtlpEndpoint);
            }
        });
    });

    openTelemetry.WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddMeter(ConversaTelemetry.MeterName);

        metrics.AddOtlpExporter(exporter =>
        {
            if (!string.IsNullOrWhiteSpace(observabilityOptions.OtlpEndpoint))
            {
                exporter.Endpoint = new Uri(observabilityOptions.OtlpEndpoint);
            }
        });
    });
}

var enforceProductionRules = !builder.Environment.IsDevelopment()
    && !builder.Environment.IsEnvironment("Testing");

ProductionConfigurationValidator.Validate(
    builder.Configuration,
    enforceProductionRules);

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

var accessTokenLifetimeMinutes =
    builder.Configuration.GetValue("Authentication:AccessTokenLifetimeMinutes", 15);

var sessionLifetimeDays =
    builder.Configuration.GetValue("Authentication:SessionLifetimeDays", 7);

if (accessTokenLifetimeMinutes is < 5 or > 60)
{
    throw new InvalidOperationException(
        "Authentication:AccessTokenLifetimeMinutes must be between 5 and 60.");
}

if (sessionLifetimeDays is < 1 or > 30)
{
    throw new InvalidOperationException(
        "Authentication:SessionLifetimeDays must be between 1 and 30.");
}

var allowedCorsOrigins = (builder.Configuration["Cors:AllowedOrigins"] ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (allowedCorsOrigins.Length == 0 && builder.Environment.IsDevelopment())
{
    allowedCorsOrigins = ["http://localhost:5173"];
}

var signingKey = new SymmetricSecurityKey(signingKeyBytes);

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedCorsOrigins.Length > 0)
        {
            policy.WithOrigins(allowedCorsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authenticationIssuer,
            ValidateAudience = true,
            ValidAudience = authenticationAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.NameIdentifier
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Path.StartsWithSegments("/ws/conversations")
                    && WebSocketAuthentication.TryGetAccessToken(
                        context.Request.Headers,
                        out var token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    })
    .AddCookie(PersistentSessionScheme, options =>
    {
        options.Cookie.Name = "conversa_session";
        options.Cookie.HttpOnly = true;
        var useLocalCookiePolicy = builder.Environment.IsDevelopment()
            || builder.Environment.IsEnvironment("Testing");
        options.Cookie.SecurePolicy = useLocalCookiePolicy
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = useLocalCookiePolicy
            ? SameSiteMode.Lax
            : SameSiteMode.None;
        options.Cookie.Path = "/api/auth";
        options.ExpireTimeSpan = TimeSpan.FromDays(sessionLifetimeDays);
        options.SlidingExpiration = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

var rateLimitOptions = new RateLimitOptions();
builder.Configuration.GetSection(RateLimitOptions.SectionName).Bind(rateLimitOptions);
rateLimitOptions.Validate();

IDistributedRateLimitStore rateLimitStore;

if (!string.IsNullOrWhiteSpace(rateLimitOptions.RedisConnectionString))
{
    var redisOptions = RedisConnectionOptionsFactory.Create(
        rateLimitOptions.RedisConnectionString);

    var connectionMultiplexer = ConnectionMultiplexer.Connect(redisOptions);
    rateLimitStore = new RedisDistributedRateLimitStore(connectionMultiplexer);

    builder.Services.AddSingleton(connectionMultiplexer);
    builder.Services.AddSingleton(rateLimitStore);
    builder.Services.AddHealthChecks()
        .AddCheck<RedisRateLimitHealthCheck>(
            "rate-limiter",
            failureStatus: HealthStatus.Unhealthy,
            tags: new[] { "ready" });
}
else if (builder.Environment.IsDevelopment()
    || builder.Environment.IsEnvironment("Testing"))
{
    // Local development/test fallback only. Production and staging require shared state.
    rateLimitStore = new InMemoryRateLimitStore();
    builder.Services.AddSingleton(rateLimitStore);
}
else
{
    throw new InvalidOperationException(
        "RateLimiting:RedisConnectionString is required outside Development and Testing.");
}

builder.Services.AddInfrastructure(builder.Configuration);

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<IAiProvider, DeterministicAiProvider>();
    builder.Services.AddSingleton<
        Conversa.Application.Speech.ISpeechToTextSessionFactory,
        DeterministicSpeechToTextSessionFactory>();
}

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>(
    "database",
    failureStatus: HealthStatus.Unhealthy,
    tags: new[] { "ready" });
builder.Services.Configure<AudioWebSocketOptions>(
    builder.Configuration.GetSection(AudioWebSocketOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(
    builder.Configuration.GetSection(RateLimitOptions.SectionName));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, AuthenticatedCurrentUser>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IConversationAssistant, ConversationAssistant>();
builder.Services.AddScoped<ConversationInputValidator>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new JwtAccessTokenFactory(
    authenticationIssuer,
    authenticationAudience,
    signingKey,
    TimeSpan.FromMinutes(accessTokenLifetimeMinutes),
    TimeProvider.System));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(
            MetadataName.ReasonPhrase,
            out string? reason)
            && reason == DistributedRateLimitLease.StoreUnavailableReason)
        {
            context.HttpContext.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;
            context.HttpContext.Response.Headers.RetryAfter = "1";

            await context.HttpContext.Response.WriteAsJsonAsync(
                new
                {
                    error = "rate_limit_unavailable",
                    message = "Rate limiting service is temporarily unavailable."
                },
                cancellationToken);

            return;
        }

        if (context.Lease.TryGetMetadata(
            MetadataName.RetryAfter,
            out TimeSpan retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds))
                    .ToString(CultureInfo.InvariantCulture);
        }

        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                error = "rate_limit_exceeded",
                message = "Too many requests. Please try again later."
            },
            cancellationToken);
    };

    options.GlobalLimiter =
        PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            RateLimitPartition.Get(
                RateLimitPartitionKey.Create(httpContext),
                partitionKey => new DistributedFixedWindowRateLimiter(
                    "global",
                    partitionKey,
                    rateLimitStore,
                    rateLimitOptions.GlobalPermitLimit,
                    TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds))));

    options.AddPolicy("ai", httpContext =>
        RateLimitPartition.Get(
            RateLimitPartitionKey.Create(httpContext),
            partitionKey => new DistributedFixedWindowRateLimiter(
                "ai",
                partitionKey,
                rateLimitStore,
                rateLimitOptions.AiPermitLimit,
                TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds))));

    options.AddPolicy("audio", httpContext =>
        RateLimitPartition.Get(
            RateLimitPartitionKey.Create(httpContext),
            partitionKey => new DistributedFixedWindowRateLimiter(
                "audio",
                partitionKey,
                rateLimitStore,
                rateLimitOptions.AudioPermitLimit,
                TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds))));
});

builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });

var app = builder.Build();

app.UseExceptionHandler();
app.UseRouting();
app.UseCors("Frontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseWebSockets();
app.MapControllers();
app.MapHealthChecks(
    "/health",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
app.MapHealthChecks(
    "/health/ready",
    new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = async (context, report) =>
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = report.Status == HealthStatus.Healthy
                ? StatusCodes.Status200OK
                : StatusCodes.Status503ServiceUnavailable;

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(
                    new
                    {
                        status = report.Status.ToString().ToLowerInvariant(),
                        checks = report.Entries.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value.Status
                                .ToString()
                                .ToLowerInvariant())
                    }));
        }
    });
app.MapAudioWebSocket();
app.MapGet("/", () => Results.Json(new
{
    name = "Conversa",
    message = "Conversa API is running.",
    status = "ok",
    health = "/health",
    readiness = "/health/ready",
    conversations = "/api/conversations",
    assistant = "/api/conversations/{id}/assistant",
    speech = "/api/speech/provider",
    audioWebSocket = "/ws/conversations/{id}/audio"
}));

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program;
