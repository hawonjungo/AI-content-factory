namespace AiContentFactory.Application.AssetReferences;

/// <summary>
/// Builds the default Character reference prompt. Replaces the old
/// <c>BuildPrompt</c> logic that could produce non-human subjects (a hardcoded
/// "orange tabby cat" branch, plus wording that explicitly allowed
/// animal/creature/mascot protagonists). The default now enforces a
/// photorealistic single human portrait, and pairs it with a negative prompt
/// against anthro/furry/deformed output. Character-specific attributes
/// (gender/role, age, outfit) are left as bracket placeholders for the model
/// to fill from the story context - they are not hardcoded here.
/// </summary>
public static class CharacterReferencePromptBuilder
{
    /// <summary>
    /// Extra negative-prompt terms always applied to Character generation, on
    /// top of the visual-style preset's own negative prompt.
    /// </summary>
    public const string CharacterNegative =
        "animal, anthro, anthropomorphic, furry, fursona, mascot, cartoon animal, creature, monster, " +
        "deformed, distorted, disfigured, mutated, extra limbs, extra fingers, fused fingers, bad hands, " +
        "bad anatomy, asymmetrical face, blurry, lowres, text, watermark, logo";

    /// <summary>
    /// The default human-portrait prompt. <paramref name="storyContext"/> and
    /// the title/topic/niche lines give the model what it needs to choose the
    /// bracket placeholders; nothing about the person is fixed in code.
    /// </summary>
    public static string BuildDefault(
        string title,
        string? topic,
        string? niche,
        string storyContext,
        string styleGuidance)
    {
        var topicLine = string.IsNullOrWhiteSpace(topic) ? string.Empty : $"Topic: {topic.Trim()}\n";
        var nicheLine = string.IsNullOrWhiteSpace(niche) ? string.Empty : $"Niche: {niche.Trim()}\n";

        return
            "A highly photorealistic portrait of a human [Gender/Role], [Age], [Outfit/Style], confident expression, " +
            "natural lighting, cinematic 8k, detailed skin texture. NO animals, NO anthro, NO furries, NO distortion.\n\n" +
            "Fill [Gender/Role], [Age] and [Outfit/Style] from the Story Context below. Exactly one real human being, " +
            "a single person, head-to-waist framing, facing the camera, plain uncluttered background, correct human " +
            "anatomy and hands. This is the recurring main character - keep the face, build and wardrobe consistent so " +
            "it can be reused as the anchor for every later shot. If the Story Context does not describe a person, " +
            "portray a plausible human presenter for this topic. No text, no logos, no extra characters.\n\n" +
            $"Video title: \"{title}\"\n" +
            topicLine +
            nicheLine +
            "Story Context:\n" +
            storyContext + "\n\n" +
            $"Visual style: {styleGuidance}.";
    }

    /// <summary>Merges <see cref="CharacterNegative"/> with the style preset's negative prompt, de-duplicated.</summary>
    public static string MergeNegative(string? styleNegative)
    {
        var terms = new List<string>();

        void Add(string? source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            foreach (var term in source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!terms.Contains(term, StringComparer.OrdinalIgnoreCase))
                {
                    terms.Add(term);
                }
            }
        }

        Add(CharacterNegative);
        Add(styleNegative);
        return string.Join(", ", terms);
    }
}
