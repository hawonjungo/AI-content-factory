using System.Text.RegularExpressions;
using AiContentFactory.Domain.Stories;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// Scene-action-relevant behavioral clauses for an opted-in
/// <see cref="CharacterBehaviorProfile"/> (see <see cref="StoryCharacter.BehaviorProfile"/>).
/// Purely additive and purely deterministic - never inferred from a
/// character's name/niche/keywords (that opt-in already happened once, on the
/// Story "bible" entry itself) and never appended unconditionally to every
/// scene featuring a flagged character: a clause is only ever produced when
/// the scene's own action text actually mentions something the profile
/// reacts to. <see cref="For"/> is called once per named character per scene
/// by <see cref="ImagePromptComposer"/> and <see cref="VideoPromptBuilder"/>,
/// which are themselves responsible for only looking a character up here when
/// that specific character opted into a non-<see cref="CharacterBehaviorProfile.None"/>
/// profile - this type has no awareness of which characters are "in" a scene
/// at all.
/// </summary>
public static class CharacterBehaviorClauses
{
    /// <summary>Sitting/seated/upright/perched cues.</summary>
    private static readonly string[] SittingCues =
    {
        "sit", "sits", "sitting", "seated", "upright", "perch", "perches", "perched", "perching",
    };

    /// <summary>Holding/using/gripping-an-object cues (includes riding/driving a vehicle - predominantly a hands-on-controls action).</summary>
    private static readonly string[] HandlingCues =
    {
        "hold", "holds", "holding", "grip", "grips", "gripping", "grasp", "grasps", "grasping",
        "use", "uses", "using", "pick up", "picks up", "picking up",
        "stir", "stirs", "stirring", "pour", "pours", "pouring",
        "eat", "eats", "eating", "drink", "drinks", "drinking",
        "type", "types", "typing", "write", "writes", "writing",
        "paw at", "paws at", "reach for", "reaches for", "reaching for",
        "carry", "carries", "carrying",
        "ride", "rides", "riding", "drive", "drives", "driving", "pedal", "pedals", "pedaling",
    };

    /// <summary>
    /// Upright bipedal locomotion/sightseeing cues - a tourist walking,
    /// standing and exploring on foot rather than a cat's normal all-fours
    /// gait.
    /// </summary>
    private static readonly string[] UprightMobilityCues =
    {
        "walk", "walks", "walking", "stroll", "strolls", "strolling",
        "wander", "wanders", "wandering", "explore", "explores", "exploring",
        "stride", "strides", "striding", "stand", "stands", "standing",
        "hike", "hikes", "hiking", "tour", "tours", "touring",
        "sightsee", "sightseeing", "shop", "shops", "shopping", "browse", "browses", "browsing",
        "look around", "looking around", "glance around",
        "admire", "admires", "admiring", "gaze at", "gazes at", "gazing at",
    };

    /// <summary>
    /// Human-tourist styling/gesture cues - posing for photos, waving,
    /// pointing, wearing tourist gear - independent of any object being held.
    /// </summary>
    private static readonly string[] PosingCues =
    {
        "pose", "poses", "posing", "wave", "waves", "waving", "point", "points", "pointing",
        "wear", "wears", "wearing", "smile", "smiles", "smiling",
        "photograph", "photographs", "photographing", "selfie", "selfies",
        "take a photo", "takes a photo", "taking a photo", "take photos", "taking photos",
        "snap a photo", "snaps a photo", "snapping a photo",
        "pose for a photo", "poses for a photo", "posing for a photo",
    };

    /// <summary>
    /// Returns null when <paramref name="profile"/> is
    /// <see cref="CharacterBehaviorProfile.None"/>, or when the profile
    /// doesn't react to anything in <paramref name="action"/>. Trivially
    /// extensible for future profiles - only
    /// <see cref="CharacterBehaviorProfile.AnthropomorphicCat"/> produces
    /// anything today.
    /// </summary>
    public static string? For(CharacterBehaviorProfile profile, string? action) => profile switch
    {
        CharacterBehaviorProfile.AnthropomorphicCat => ForAnthropomorphicCat(action),
        _ => null,
    };

    private static string? ForAnthropomorphicCat(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            return null;
        }

        var text = action.ToLowerInvariant();

        var fragments = new List<string>();

        if (SittingCues.Any(cue => MatchesWholeWord(text, cue)))
        {
            fragments.Add("sitting upright like a person rather than on all fours");
        }

        if (UprightMobilityCues.Any(cue => MatchesWholeWord(text, cue)))
        {
            fragments.Add("walking or standing upright on its hind legs like a person, exploring on foot");
        }

        if (HandlingCues.Any(cue => MatchesWholeWord(text, cue)))
        {
            fragments.Add("using its front paw in a human-like grip on the object it is interacting with");
        }

        if (PosingCues.Any(cue => MatchesWholeWord(text, cue)))
        {
            fragments.Add("posing, gesturing, or dressed like a human tourist (e.g. waving, pointing, wearing tourist gear, posing for a photo)");
        }

        if (fragments.Count == 0)
        {
            return null;
        }

        return "The cat behaves anthropomorphically in this shot: "
            + string.Join(", while ", fragments)
            + ", while remaining a recognizable, otherwise ordinary photorealistic cat.";
    }

    /// <summary>
    /// Whole-word/whole-phrase match so a short cue like "sit" or "use" never
    /// fires on an unrelated word that merely contains it as a substring
    /// (e.g. "visit", "household", "because", "great", "prototype", "tourist", "boardwalk").
    /// </summary>
    private static bool MatchesWholeWord(string text, string cue) =>
        Regex.IsMatch(text, $@"\b{Regex.Escape(cue)}\b");
}
