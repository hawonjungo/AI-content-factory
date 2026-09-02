using AiContentFactory.Domain.Generation;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// Maps the abstract Fast/Lite video tier onto concrete provider model ids,
/// bound from the "Flow" config section. Keeping this out of the providers is
/// what lets a model rename be a config edit and keeps the tier concept
/// provider-agnostic.
/// </summary>
public class FlowModelOptions
{
    public const string SectionName = "Flow";

    /// <summary>Provider label recorded on every attempt row.</summary>
    public string Provider { get; set; } = "veo";

    /// <summary>Model id for the Fast (hero / hook) tier.</summary>
    public string FastModel { get; set; } = "veo-3.1-fast-generate-preview";

    /// <summary>Model id for the Lite (story-beat) tier.</summary>
    public string LiteModel { get; set; } = "veo-3.1-generate-lite";

    public string ModelFor(VideoModelTier tier) => tier switch
    {
        VideoModelTier.Fast => FastModel,
        VideoModelTier.Lite => LiteModel,
        _ => LiteModel
    };
}
