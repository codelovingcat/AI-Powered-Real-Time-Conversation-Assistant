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
