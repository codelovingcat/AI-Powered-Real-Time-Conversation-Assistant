namespace Conversa.Application.Resumes;

public sealed record ResumeAnalysisRequest(
    string ResumeText,
    string? JobDescription,
    string Language = "en");
