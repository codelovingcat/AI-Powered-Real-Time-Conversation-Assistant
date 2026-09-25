using Conversa.Application.Ai;
using Conversa.Application.Common;
using Conversa.Application.Conversations;

namespace Conversa.Api.Tests.Security;

public sealed class ConversationInputValidationTests
{
    [Fact]
    public void Create_RejectsMissingInstruction()
    {
        var validator = new ConversationInputValidator();

        var exception = Assert.Throws<ValidationException>(() =>
            validator.ValidateCreate(new CreateConversationCommand(
                "Test",
                "",
                "en",
                "tr")));

        Assert.Contains("instruction", exception.Errors.Keys);
    }

    [Fact]
    public void Create_RejectsUnsupportedLanguage()
    {
        var validator = new ConversationInputValidator();

        var exception = Assert.Throws<ValidationException>(() =>
            validator.ValidateCreate(new CreateConversationCommand(
                "Test",
                "Translate naturally.",
                "xx-invalid",
                "tr")));

        Assert.Contains("sourceLanguage", exception.Errors.Keys);
    }

    [Fact]
    public void Process_RejectsUnsupportedInputKind()
    {
        var validator = new ConversationInputValidator();

        var exception = Assert.Throws<ValidationException>(() =>
            validator.ValidateProcess(new ProcessConversationInputCommand(
                Guid.NewGuid(),
                (AiInputKind)999,
                "hello")));

        Assert.Contains("inputKind", exception.Errors.Keys);
    }

    [Fact]
    public void Process_RejectsOversizedText()
    {
        var validator = new ConversationInputValidator();
        var oversizedText = new string('x', 20_001);

        var exception = Assert.Throws<ValidationException>(() =>
            validator.ValidateProcess(new ProcessConversationInputCommand(
                Guid.NewGuid(),
                AiInputKind.HeardSpeech,
                oversizedText)));

        Assert.Contains("text", exception.Errors.Keys);
    }
}
