namespace Conversa.Application.Resumes;

public sealed record ResumeAnalysisResult(
    int AtsScore,
    bool AtsFriendly,
    string Summary,
    IReadOnlyList<string> MatchedKeywords,
    IReadOnlyList<string> MissingKeywords,
    IReadOnlyList<string> FormattingIssues,
    IReadOnlyList<string> ContentSuggestions);
