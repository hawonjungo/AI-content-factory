using AiContentFactory.Domain.ContentProjects;

namespace AiContentFactory.Application.Presets;

/// <summary>
/// The preset catalog lives in code, not in the database, on purpose: adding
/// a template or style is a one-record edit here with no migration, no seed
/// script, and no risk of dev/prod catalogs drifting. Projects persist only
/// the preset *id*, so a wording fix to a style's guidance reaches existing
/// projects on their next generation.
///
/// The one exception is caption presets - those are snapshotted onto the
/// project as CaptionSettings when picked, because the user edits them
/// afterwards and those edits must not be overwritten by a catalog change.
///
/// User-visible names/descriptions are Vietnamese (that's the wizard's
/// language); the guidance strings are English because they are fed straight
/// into the Gemini/Veo prompts.
/// </summary>
public static class PresetCatalog
{
    public const string FallbackTemplateId = "faceless-story";
    public const string FallbackStyleId = "cinematic-dark";
    public const string FallbackVoiceId = "narrator-deep";
    public const string FallbackCaptionId = "tiktok-karaoke";

    public static IReadOnlyList<ContentTemplate> Templates { get; } = new[]
    {
        new ContentTemplate(
            Id: "faceless-story",
            Name: "Kể chuyện giấu mặt",
            Niche: "Storytelling",
            Description: "Một câu chuyện ngắn có cao trào, giọng kể xuyên suốt, không cần lộ mặt.",
            DefaultDurationSeconds: 40,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Write a single self-contained story with a clear narrative arc. Open on the
                most surprising moment, not on setup. Keep one narrator voice throughout.
                Escalate tension in the middle third, and land the payoff before the CTA.
                """,
            DefaultStylePresetId: "cinematic-dark",
            DefaultVoicePresetId: "narrator-deep",
            DefaultCaptionPresetId: "tiktok-karaoke"),

        new ContentTemplate(
            Id: "listicle-top5",
            Name: "Top 5 / Danh sách",
            Niche: "Listicle",
            Description: "Đếm ngược danh sách, mỗi mục một clip - dễ giữ chân người xem.",
            DefaultDurationSeconds: 45,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Structure the body as a countdown of exactly 5 numbered items, strongest
                item last. Each item gets one tight sentence - state the item, then the one
                reason it matters. Tease the #1 item in the hook without revealing it.
                """,
            DefaultStylePresetId: "photoreal-doc",
            DefaultVoicePresetId: "energetic-host",
            DefaultCaptionPresetId: "bold-yellow-pop"),

        new ContentTemplate(
            Id: "motivation",
            Name: "Truyền động lực",
            Niche: "Motivation",
            Description: "Lời thoại ngắn, dồn dập, kết bằng một câu chốt đáng lưu lại.",
            DefaultDurationSeconds: 30,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Short, punchy, second-person sentences. Address the viewer directly as
                "you". Build rhythm through repetition and escalating stakes. End on one
                quotable line that works as a standalone caption. No hedging, no filler.
                """,
            DefaultStylePresetId: "cinematic-dark",
            DefaultVoicePresetId: "narrator-deep",
            DefaultCaptionPresetId: "big-center-impact"),

        new ContentTemplate(
            Id: "explainer",
            Name: "Giải thích kiến thức",
            Niche: "Education",
            Description: "Giải thích một khái niệm khó bằng ngôn ngữ đời thường.",
            DefaultDurationSeconds: 50,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Explain exactly one concept. Open with the counter-intuitive part. Use a
                concrete everyday analogy before any abstraction. Define every term you
                introduce. Prefer accuracy over punchiness where the two conflict, and do
                not state numbers or dates you are not confident are correct.
                """,
            DefaultStylePresetId: "minimal-flat",
            DefaultVoicePresetId: "documentary-calm",
            DefaultCaptionPresetId: "clean-documentary"),

        new ContentTemplate(
            Id: "product-review",
            Name: "Review sản phẩm",
            Niche: "Affiliate / Review",
            Description: "Nêu vấn đề, giới thiệu sản phẩm, chốt bằng lời kêu gọi mua.",
            DefaultDurationSeconds: 40,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Lead with the problem the viewer already has, not with the product. Give
                two concrete benefits and one honest limitation - the limitation is what
                makes the rest credible. Close with a specific action, not "check the link".
                Never invent specifications, prices, or test results.
                """,
            DefaultStylePresetId: "photoreal-doc",
            DefaultVoicePresetId: "energetic-host",
            DefaultCaptionPresetId: "bold-yellow-pop"),

        new ContentTemplate(
            Id: "horror",
            Name: "Kinh dị / Creepypasta",
            Niche: "Horror",
            Description: "Không khí rùng rợn, tiết lộ dần, kết thúc lửng khiến người xem ám ảnh.",
            DefaultDurationSeconds: 45,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Slow-burn dread rather than jump scares. Withhold the explanation as long
                as possible; reveal in fragments. Use plain, matter-of-fact language - the
                horror comes from what is implied, not from adjectives. End unresolved.
                Keep it fictional and avoid real named people or places.
                """,
            DefaultStylePresetId: "horror-grain",
            DefaultVoicePresetId: "mysterious-whisper",
            DefaultCaptionPresetId: "minimal-bottom"),

        new ContentTemplate(
            Id: "history-facts",
            Name: "Sự thật lịch sử",
            Niche: "History",
            Description: "Một sự kiện lịch sử ít người biết, kể như phim tài liệu.",
            DefaultDurationSeconds: 50,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                One historical episode, told chronologically after a cold open on its most
                striking detail. Only assert facts you are confident are accurate; if a
                date or number is uncertain, phrase it approximately rather than inventing
                precision. Avoid sensationalising contested history.
                """,
            DefaultStylePresetId: "photoreal-doc",
            DefaultVoicePresetId: "documentary-calm",
            DefaultCaptionPresetId: "clean-documentary"),

        new ContentTemplate(
            Id: "finance-tips",
            Name: "Mẹo tài chính",
            Niche: "Finance",
            Description: "Một mẹo tài chính cá nhân áp dụng được ngay.",
            DefaultDurationSeconds: 40,
            DefaultAspectRatio: "9:16",
            ScriptGuidance: """
                Exactly one actionable tip the viewer can apply this week. Use round,
                clearly illustrative numbers and label them as examples. Include a one-line
                caveat that this is general information, not personalised financial advice.
                Never promise returns or guaranteed outcomes.
                """,
            DefaultStylePresetId: "minimal-flat",
            DefaultVoicePresetId: "energetic-host",
            DefaultCaptionPresetId: "bold-yellow-pop")
    };

    public static IReadOnlyList<StylePreset> Styles { get; } = new[]
    {
        new StylePreset(
            "cinematic-dark",
            "Điện ảnh tối",
            "Ánh sáng ngược, tương phản cao, tông lạnh - hợp kể chuyện và truyền động lực.",
            "cinematic film still, dramatic low-key lighting, deep shadows, cool teal and amber grade, shallow depth of field, anamorphic lens, 35mm film grain",
            "flat lighting, oversaturated colors, text, watermark, logo",
            ReferenceLookGuidance: "cinematic live-action film look, realistic textures and materials, cool teal and amber color grade, fine 35mm film grain",
            ReferenceNegativePrompt: "cartoon, anime, illustration, oversaturated colors"),

        new StylePreset(
            "photoreal-doc",
            "Tài liệu chân thực",
            "Trông như quay thật bằng máy ảnh - hợp review, lịch sử, danh sách.",
            "photorealistic documentary footage, natural daylight, handheld camera feel, realistic skin texture and materials, neutral color grade, 50mm lens",
            "illustration, cartoon, cgi look, plastic skin, text, watermark, distorted hands",
            ReferenceLookGuidance: "photorealistic, realistic skin texture and materials, neutral color grade",
            ReferenceNegativePrompt: "illustration, cartoon, cgi look, plastic skin"),

        new StylePreset(
            "cat-travel-stylized-realism",
            "Cat-Travel-Stylized-Realism",
            "Dành riêng cho chuỗi video mèo du lịch (Milo & Mimi): tả thực điện ảnh góc POV/selfie hoặc quay ngang người, mèo đi 2 chân dùng tay như người, luôn đeo phụ kiện du lịch, biểu cảm rạng rỡ dễ thương kiểu viral TikTok/Facebook.",
            "cinematic stylized realism blended with radiant, charming expressiveness; fluffy, finely detailed fur texture; natural, vibrant, colorful lighting; photorealistic as if shot on a 50mm lens, framed as a first-person POV/selfie-style shot or a side-angle handheld shot. The cat naturally stands and walks upright on its hind legs, using its front paws dexterously like human hands (holding a camera, gripping a scooter/e-bike throttle, waving hello, holding food). Always wearing travel accessories (a backwards baseball cap or a beret, a small crossbody bag). Eyes wide open with curiosity, mouth open in a big bright smile, extremely charming and adorable - like a viral Facebook/TikTok pet video",
            "flat documentary staging, dull neutral or bored expression, closed mouth, sleepy expression, stiff static pose, cartoon, anime, illustration, plastic skin, text, watermark, distorted hands, extra fingers",
            ReferenceLookGuidance: "cinematic stylized realism, photorealistic finish, fluffy finely detailed fur texture, natural vibrant colors",
            ReferenceNegativePrompt: "cartoon, anime, illustration, plastic skin"),

        new StylePreset(
            "anime",
            "Anime",
            "Nét vẽ anime hiện đại, màu rực, chuyển động mượt.",
            "modern anime key visual, clean cel shading, vibrant saturated palette, expressive character design, detailed painted background, studio-quality animation",
            "photorealistic, 3d render, text, watermark, extra fingers",
            ReferenceLookGuidance: "modern anime key visual style, clean cel shading, vibrant saturated palette",
            ReferenceNegativePrompt: "photorealistic, 3d render, live-action photograph"),

        new StylePreset(
            "pixar-3d",
            "Hoạt hình 3D",
            "Phong cách 3D dễ thương, ánh sáng mềm - hợp nội dung nhẹ nhàng, giáo dục.",
            "stylized 3d animated film still, soft global illumination, rounded appealing character design, subsurface scattering, warm inviting palette, high quality render",
            "photorealistic, horror, harsh shadows, text, watermark",
            ReferenceLookGuidance: "stylized 3d animated film look, rounded appealing character design, subsurface scattering skin and fur materials, warm inviting palette, high quality render",
            ReferenceNegativePrompt: "photorealistic, live-action photograph"),

        new StylePreset(
            "horror-grain",
            "Kinh dị nhiễu hạt",
            "Nhiễu hạt, tối, ám ảnh - dành riêng cho nội dung kinh dị.",
            "found-footage horror still, heavy film grain, desaturated sickly green-grey palette, deep crushed blacks, unsettling negative space, dim practical light sources",
            "bright cheerful lighting, saturated colors, cartoon, text, watermark",
            ReferenceLookGuidance: "found-footage horror film look, heavy film grain, desaturated sickly green-grey palette",
            ReferenceNegativePrompt: "cartoon, oversaturated colors"),

        new StylePreset(
            "retro-vhs",
            "Retro VHS",
            "Cảm giác băng VHS thập niên 90 - hoài niệm, gây chú ý.",
            "1990s VHS home video aesthetic, scanlines, chromatic aberration, slight tape warping, warm faded colors, low dynamic range, timestamp-free",
            "modern 4k clarity, clean digital look, text, watermark",
            ReferenceLookGuidance: "1990s VHS home video look, scanlines, chromatic aberration, slight tape warping, warm faded colors, low dynamic range",
            ReferenceNegativePrompt: "modern 4k clarity, clean digital look"),

        new StylePreset(
            "minimal-flat",
            "Đồ hoạ tối giản",
            "Hình khối phẳng, nền sạch - hợp giải thích kiến thức và tài chính.",
            "clean minimal motion-graphic style, flat vector shapes, generous negative space, limited two-accent palette on a light background, crisp geometric composition",
            "photorealistic, cluttered, gradients, noise, text, watermark",
            ReferenceLookGuidance: "clean minimal motion-graphic style, flat vector shapes, limited two-accent color palette",
            ReferenceNegativePrompt: "photorealistic, gradients, noise"),

        new StylePreset(
            "neon-cyberpunk",
            "Neon cyberpunk",
            "Đèn neon, mưa, thành phố tương lai - rất bắt mắt trên feed.",
            "cyberpunk city at night, neon signage reflections on wet asphalt, volumetric fog, magenta and cyan rim lighting, dense futuristic architecture, cinematic wide shot",
            "daylight, rural, muted colors, text, watermark",
            ReferenceLookGuidance: "cyberpunk aesthetic, magenta and cyan color grade, sleek futuristic materials",
            ReferenceNegativePrompt: "muted colors, desaturated")
    };

    public static IReadOnlyList<VoicePreset> Voices { get; } = new[]
    {
        new VoicePreset(
            "narrator-deep",
            "Người kể trầm",
            "Giọng trầm, chắc - hợp kể chuyện và truyền động lực.",
            "Kore",
            "Read in a steady, grounded tone with deliberate pacing and confident pauses.",
            VoiceGender.Female),

        new VoicePreset(
            "energetic-host",
            "MC năng lượng cao",
            "Nhanh, hào hứng - hợp danh sách và review.",
            "Puck",
            "Read with high energy and forward momentum, upbeat but never rushed to the point of slurring.",
            VoiceGender.Male),

        new VoicePreset(
            "documentary-calm",
            "Tài liệu điềm tĩnh",
            "Rõ ràng, trung tính - hợp giải thích và lịch sử.",
            "Charon",
            "Read clearly and evenly, like a documentary narrator: informative, unhurried, no theatrics.",
            VoiceGender.Male),

        new VoicePreset(
            "warm-storyteller",
            "Kể chuyện ấm",
            "Ấm áp, gần gũi - hợp nội dung cảm xúc.",
            "Aoede",
            "Read warmly and conversationally, as if telling the story to one person across a table.",
            VoiceGender.Female),

        new VoicePreset(
            "mysterious-whisper",
            "Bí ẩn thì thầm",
            "Nhẹ, nhiều hơi - dành cho kinh dị.",
            "Enceladus",
            "Read quietly and breathily, holding tension, with long pauses before revelations.",
            VoiceGender.Male),

        new VoicePreset(
            "youthful-bright",
            "Trẻ trung tươi sáng",
            "Trẻ, sáng, thân thiện - hợp nội dung đời sống.",
            "Leda",
            "Read brightly and casually, friendly and light, like talking to a friend.",
            VoiceGender.Female),

        // Free, self-hosted (Kokoro-82M via docker compose profile "free-tts").
        // English only; the style instruction is ignored (Kokoro cannot be steered).
        // Kept AFTER the Gemini voices so the gender fallback of a Gemini
        // project never lands on one of these.
        new VoicePreset(
            "free-en-female-us",
            "🆓 Nữ Mỹ (miễn phí)",
            "Kokoro chạy trên máy bạn - $0, chỉ tiếng Anh.",
            "kokoro:af_heart",
            string.Empty,
            VoiceGender.Female,
            IsFree: true),

        new VoicePreset(
            "free-en-male-us",
            "🆓 Nam Mỹ (miễn phí)",
            "Kokoro chạy trên máy bạn - $0, chỉ tiếng Anh.",
            "kokoro:am_michael",
            string.Empty,
            VoiceGender.Male,
            IsFree: true),

        new VoicePreset(
            "free-en-female-gb",
            "🆓 Nữ Anh (miễn phí)",
            "Kokoro chạy trên máy bạn - $0, chỉ tiếng Anh.",
            "kokoro:bf_emma",
            string.Empty,
            VoiceGender.Female,
            IsFree: true),

        new VoicePreset(
            "free-en-male-gb",
            "🆓 Nam Anh (miễn phí)",
            "Kokoro chạy trên máy bạn - $0, chỉ tiếng Anh.",
            "kokoro:bm_george",
            string.Empty,
            VoiceGender.Male,
            IsFree: true)
    };

    public static IReadOnlyList<CaptionPreset> Captions { get; } = new[]
    {
        new CaptionPreset(
            "tiktok-karaoke",
            "TikTok karaoke",
            "Từng chữ sáng lên theo lời đọc - kiểu phổ biến nhất trên TikTok.",
            Caption(fontSize: 82, primary: "#FFFFFF", highlight: "#FFD400", outlineWidth: 4, bold: true,
                uppercase: true, position: CaptionPosition.Bottom, marginV: 260, wordsPerCue: 3,
                animation: CaptionAnimation.PopIn, karaoke: true)),

        new CaptionPreset(
            "bold-yellow-pop",
            "Vàng đậm nảy chữ",
            "Chữ vàng viền dày, nảy lên mỗi cụm - rất bắt mắt.",
            Caption(fontSize: 88, primary: "#FFD400", highlight: "#FFFFFF", outlineWidth: 5, bold: true,
                uppercase: true, position: CaptionPosition.Bottom, marginV: 280, wordsPerCue: 3,
                animation: CaptionAnimation.PopIn, karaoke: false)),

        new CaptionPreset(
            "big-center-impact",
            "Chữ lớn giữa màn",
            "Cụm từ lớn ngay giữa khung hình - hợp câu chốt truyền động lực.",
            Caption(fontSize: 110, primary: "#FFFFFF", highlight: "#FF3B30", outlineWidth: 6, bold: true,
                uppercase: true, position: CaptionPosition.Center, marginV: 0, wordsPerCue: 2,
                animation: CaptionAnimation.PopIn, karaoke: true)),

        new CaptionPreset(
            "clean-documentary",
            "Tài liệu sạch",
            "Chữ trắng nhỏ, mờ dần - không cướp sự chú ý khỏi hình.",
            Caption(fontSize: 56, primary: "#FFFFFF", highlight: "#FFFFFF", outlineWidth: 2, bold: false,
                uppercase: false, position: CaptionPosition.Bottom, marginV: 180, wordsPerCue: 7,
                animation: CaptionAnimation.FadeIn, karaoke: false)),

        new CaptionPreset(
            "minimal-bottom",
            "Tối giản đáy khung",
            "Một dòng nhỏ sát đáy, gần như vô hình - hợp kinh dị.",
            Caption(fontSize: 50, primary: "#E8E8E8", highlight: "#E8E8E8", outlineWidth: 2, bold: false,
                uppercase: false, position: CaptionPosition.Bottom, marginV: 120, wordsPerCue: 6,
                animation: CaptionAnimation.FadeIn, karaoke: false)),

        new CaptionPreset(
            "none",
            "Không phụ đề",
            "Video sạch, chỉ có giọng đọc và nhạc nền.",
            Caption(fontSize: 60, primary: "#FFFFFF", highlight: "#FFFFFF", outlineWidth: 3, bold: true,
                uppercase: false, position: CaptionPosition.Bottom, marginV: 220, wordsPerCue: 5,
                animation: CaptionAnimation.None, karaoke: false, enabled: false))
    };

    public static ContentTemplate? FindTemplate(string? id) => Find(Templates, id, t => t.Id);

    public static StylePreset? FindStyle(string? id) => Find(Styles, id, s => s.Id);

    public static VoicePreset? FindVoice(string? id) => Find(Voices, id, v => v.Id);

    public static CaptionPreset? FindCaption(string? id) => Find(Captions, id, c => c.Id);

    /// <summary>
    /// Resolution used at generation time. Falls back to the catalog default
    /// rather than throwing: a project created before a preset was renamed
    /// should still generate, just with the house style.
    /// </summary>
    public static StylePreset ResolveStyle(string? id) => FindStyle(id) ?? FindStyle(FallbackStyleId)!;

    public static VoicePreset ResolveVoice(string? id) => FindVoice(id) ?? FindVoice(FallbackVoiceId)!;

    /// <summary>First catalog voice with the given gender, or null if none is tagged that way.</summary>
    public static VoicePreset? FindVoiceByGender(VoiceGender gender) =>
        gender == VoiceGender.Unspecified ? null : Voices.FirstOrDefault(v => v.Gender == gender && !v.IsFree);

    /// <summary>
    /// Resolves the voice to speak in: the named preset if it exists and either
    /// no gender was asked for or it already matches; otherwise the first
    /// catalog voice of the requested gender; otherwise the house default.
    /// </summary>
    public static VoicePreset ResolveVoice(string? id, VoiceGender preferredGender)
    {
        var byId = FindVoice(id);

        if (preferredGender == VoiceGender.Unspecified)
        {
            return byId ?? FindVoice(FallbackVoiceId)!;
        }

        if (byId is not null && byId.Gender == preferredGender)
        {
            return byId;
        }

        // Stay in the chosen voice's family: a free voice must never turn into
        // a paid one (or the other way round) just because of a gender switch.
        var sameFamily = byId is null
            ? null
            : Voices.FirstOrDefault(v => v.Gender == preferredGender && v.IsFree == byId.IsFree);

        return sameFamily ?? (byId?.IsFree == true ? byId : null) ?? FindVoiceByGender(preferredGender) ?? byId ?? FindVoice(FallbackVoiceId)!;
    }

    public static ContentTemplate ResolveTemplate(string? id) => FindTemplate(id) ?? FindTemplate(FallbackTemplateId)!;

    private static T? Find<T>(IReadOnlyList<T> source, string? id, Func<T, string> idSelector) where T : class =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : source.FirstOrDefault(item => string.Equals(idSelector(item), id.Trim(), StringComparison.OrdinalIgnoreCase));

    private static CaptionSettings Caption(
        int fontSize,
        string primary,
        string highlight,
        double outlineWidth,
        bool bold,
        bool uppercase,
        CaptionPosition position,
        int marginV,
        int wordsPerCue,
        CaptionAnimation animation,
        bool karaoke,
        bool enabled = true) =>
        CaptionSettings.Create(
            enabled: enabled,
            // DejaVu Sans ships with fonts-dejavu-core in the API image and
            // covers Vietnamese diacritics - see the Dockerfile.
            fontFamily: "DejaVu Sans",
            fontSizePt: fontSize,
            primaryColor: primary,
            highlightColor: highlight,
            outlineColor: "#000000",
            outlineWidth: outlineWidth,
            shadowDepth: 0,
            bold: bold,
            uppercase: uppercase,
            position: position,
            marginVerticalPx: marginV,
            maxWordsPerCue: wordsPerCue,
            animation: animation,
            karaoke: karaoke);
}
