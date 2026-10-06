namespace Conversa.Application.Common;

public sealed record CursorPageDto<T>(
    IReadOnlyList<T> Items,
    string? NextCursor);
