namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public sealed class QqMediaMetadata
{
    public int? RawMediaSubtype { get; init; }

    public string? FileName { get; init; }

    public string? LocalPath { get; init; }

    public long? FileSize { get; init; }

    public int? DurationSeconds { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string? FileExtension { get; init; }

    public string? PreviewPath { get; init; }

    public override string ToString() => $"{nameof(QqMediaMetadata)} {{ sensitive values omitted }}";
}
