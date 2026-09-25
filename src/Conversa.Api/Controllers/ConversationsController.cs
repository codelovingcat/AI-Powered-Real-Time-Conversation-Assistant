using Conversa.Application.Common;
using Conversa.Application.Conversations;
using Conversa.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Conversa.Api.Controllers;

[ApiController]
[Route("api/conversations")]
public sealed class ConversationsController(
    IConversationService conversations,
    IConversationAssistant assistant) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = ConversationService.DefaultListSize, CancellationToken cancellationToken = default)
        => Ok(await conversations.ListAsync(take, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateConversationRequest request, CancellationToken cancellationToken)
    {
        var created = await conversations.CreateAsync(
            new CreateConversationCommand(
                request.Title ?? string.Empty,
                request.Instruction,
                request.SourceLanguage ?? string.Empty,
                request.TargetLanguage ?? string.Empty),
            cancellationToken);

        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
        => Ok(await conversations.GetAsync(id, cancellationToken));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateConversationRequest request, CancellationToken cancellationToken)
        => Ok(await conversations.UpdateAsync(
            new UpdateConversationCommand(id, request.Title, request.Instruction, request.SourceLanguage, request.TargetLanguage),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await conversations.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/messages")]
    public async Task<IActionResult> ListMessages(Guid id, CancellationToken cancellationToken)
        => Ok(await conversations.ListMessagesAsync(id, cancellationToken));

    [HttpPost("{id:guid}/assistant")]
    [EnableRateLimiting("ai")]
    public async Task<IActionResult> Process(Guid id, [FromBody] ProcessInputRequest request, CancellationToken cancellationToken)
    {
        if (request.InputKind is null)
        {
            throw ValidationException.For("inputKind", "inputKind is required. Use heardSpeech or userFormulationRequest.");
        }

        var message = await assistant.ProcessAsync(
            new ProcessConversationInputCommand(id, request.InputKind.Value, request.Text),
            cancellationToken);

        return Ok(message);
    }
}
