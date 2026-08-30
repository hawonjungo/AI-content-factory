namespace AiContentFactory.Domain.Assets;

public enum AssetType
{
    Video = 0,
    Image = 1,
    Audio = 2,
    Voice = 3,
    Music = 4,
    Subtitle = 5,
    Thumbnail = 6
}

public enum AssetStatus
{
    Pending = 0,
    Generating = 1,
    Ready = 2,
    Failed = 3
}
