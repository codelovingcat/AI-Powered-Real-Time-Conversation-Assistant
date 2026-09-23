namespace Conversa.Application.Audio;

public sealed record AudioChunk(int Sequence, string? ContentType, ReadOnlyMemory<byte> Data);
