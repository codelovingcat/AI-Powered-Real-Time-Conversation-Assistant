using Conversa.Api.Contracts;
using Conversa.Application.Common;
using Conversa.Application.Preferences;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Conversa.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/assistant-preferences")]
public sealed class AssistantPreferencesController(
    IAssistantPreferencesService preferencesService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(await preferencesService.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateAssistantPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ResponseLength))
        {
            throw ValidationException.For("responseLength", "responseLength is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Tone))
        {
            throw ValidationException.For("tone", "tone is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ExplanationLevel))
        {
            throw ValidationException.For("explanationLevel", "explanationLevel is required.");
        }

        if (request.SuggestReplies is null)
        {
            throw ValidationException.For("suggestReplies", "suggestReplies is required.");
        }

        var result = await preferencesService.UpdateAsync(
            new UpdateAssistantPreferencesCommand(
                request.ResponseLength,
                request.Tone,
                request.ExplanationLevel,
                request.SuggestReplies.Value),
            cancellationToken);

        return Ok(result);
    }
}
