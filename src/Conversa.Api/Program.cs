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

builder.Services.AddInfrastructure(builder.Configuration);
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
