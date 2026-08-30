namespace QQChatLocalReader.Infrastructure.Secrets;

public delegate bool KeyCandidateVisitor(ReadOnlySpan<byte> candidate);
