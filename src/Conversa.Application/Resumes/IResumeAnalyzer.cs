namespace Conversa.Application.Resumes;

public interface IResumeAnalyzer
{
    Task<ResumeAnalysisResult> AnalyzeAsync(
        ResumeAnalysisRequest request,
        CancellationToken cancellationToken);
}
