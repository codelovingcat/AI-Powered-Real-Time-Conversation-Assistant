using Conversa.Application.Common;
using Conversa.Application.Speech;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Conversa.Api.Controllers;

[ApiController]
[Route("api/speech")]
public sealed class SpeechController(IServiceProvider services) : ControllerBase
{
    [HttpGet("provider")]
    public IActionResult Provider()
    {
        var streaming = services.GetService<ISpeechToTextSessionFactory>();
        var oneShot = services.GetService<ISpeechToTextProvider>();
        return Ok(new
        {
            configured = streaming is not null || oneShot is not null,
            streamingProvider = streaming?.ProviderName,
            transcriptionProvider = oneShot?.ProviderName
        });
    }

    [HttpPost("transcriptions")]
    public IActionResult Transcribe()
    {
        if (services.GetService<ISpeechToTextProvider>() is null)
        {
            throw new SpeechToTextNotConfiguredException();
        }

        return StatusCode(StatusCodes.Status501NotImplemented, new ProblemDetails
        {
            Status = StatusCodes.Status501NotImplemented,
            Title = "Batch transcription is not implemented",
            Detail = "Continuous listening uses the audio WebSocket once a speech-to-text provider is registered."
        });
    }
}
