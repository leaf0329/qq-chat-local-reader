namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public enum QqMessageContentType
{
    Unknown = 0,
    Text = 1,
    Image = 2,
    File = 3,
    Voice = 4,
    Video = 5,
    QqFace = 6,
    Reply = 7,
    GrayTip = 8,
    RedPacket = 9,
    Ark = 10,
    MarketFace = 11,
    Markdown = 14,
    LegacyForward = 16,
    MarkdownButton = 17,
    LiveRecord = 21,
}
