using System.Text.Json;
using System.Text.Json.Serialization;
using AiContentFactory.Application.Assets;
using AiContentFactory.Application.ContentProjects;
using AiContentFactory.Application.Costs;
using AiContentFactory.Application.Providers;
using AiContentFactory.Application.Rendering;
using AiContentFactory.Application.Storage;
using AiContentFactory.Application.Storyboards;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiContentFactory.Application.Generation;

/// <summary>
/// The result of one AI clip check, stored on the scene (see
/// <c>Scene.ClipCheckJson</c>) together with the clip it judged.
/// </summary>
/// <param name="AssetId">The video Asset that was checked - the result only applies while it is the scene's current clip.</param>
/// <param name="Verdict">"pass" / "warn" / "fail".</param>
/// <param name="Score">0-100 overall match.</param>
/// <param name="CharacterMatches">Null when no recurring character is expected in the shot.</param>
/// <param name="Issues">Short Vietnamese problem descriptions, empty when none.</param>
/// <param name="Summary">One short Vietnamese sentence.</param>
/// <param name="EstimatedCostUsd">What this check was estimated to cost (0 when served from the stored result).</param>
public record ClipCheckResult(
    Guid AssetId,
    DateTimeOffset CheckedAt,
    string Verdict,
    int Score,
    bool? CharacterMatches,
    bool ActionMatches,
    IReadOnlyList<string> Issues,
    string Summary,
    string Model,
    decimal EstimatedCostUsd)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>The stored result, or null when absent/unreadable.</summary>
    public static ClipCheckResult? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ClipCheckResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public interface IClipCheckService
{
    /// <summary>
    /// Checks the scene's current clip against its references and intended
    /// shot with a vision model (ONE billable call). A result already stored
    /// for this exact clip is returned for free unless <paramref name="force"/>.
    /// </summary>
    Task<ClipCheckResult> CheckAsync(Guid contentProjectId, Guid sceneId, bool force, CancellationToken cancellationToken = default);
}

public class ClipCheckService : IClipCheckService
{
    public const string Stage = "clip-check";

    /// <summary>Where in the clip frames are sampled (fractions of its length): start, middle, end.</summary>
    private static readonly double[] SamplePoints = { 0.15, 0.5, 0.9 };
    private const int FrameMaxHeight = 768;

    private const string Instruction = """
        You are a strict QA reviewer for short vertical video clips made by an AI video model for a faceless
        short-video series. You receive: optional reference images of recurring characters or places (identity
        anchors), three frames sampled from the clip (start, middle, end) and the intended shot description.

        Judge only what you can SEE:
        1. If a recurring character is expected: is it the same character as its reference (species or kind,
           colours, markings, clothing and accessories)? Does it stay the same across the three frames?
        2. Does the clip show the intended subject, action and setting?
        3. Visible defects: extra or missing limbs, melted or distorted faces, garbled text or watermarks,
           the plain white studio background of a reference image copied into the scene, sudden identity changes.

        Never invent problems you cannot see. Write every issue and the summary in Vietnamese, short and concrete.
        Respond with ONLY one JSON object, exactly:
        {"verdict":"pass"|"warn"|"fail","score":0-100,"characterMatches":true|false|null,"actionMatches":true|false,
         "issues":[string],"summary":string}
        characterMatches is null when no recurring character is expected in this shot.
        """;

    private readonly IStoryboardRepository _storyboardRepository;
    private readonly IContentProjectRepository _projectRepository;
    private readonly IProjectJobReservationService _jobReservation;
    private readonly ISceneAssetGenerator _sceneAssetGenerator;
    private readonly IAssetService _assetService;
    private readonly IFileStorage _fileStorage;
    private readonly IMediaProbe _mediaProbe;
    private readonly IVideoFrameExtractor _frameExtractor;
    private readonly IVisionReviewProvider _visionProvider;
    private readonly IAiUsageTracker _usageTracker;
    private readonly PricingOptions _pricing;
    private readonly ILogger<ClipCheckService> _logger;

    public ClipCheckService(
        IStoryboardRepository storyboardRepository,
        IContentProjectRepository projectRepository,
        IProjectJobReservationService jobReservation,
        ISceneAssetGenerator sceneAssetGenerator,
        IAssetService assetService,
        IFileStorage fileStorage,
        IMediaProbe mediaProbe,
        IVideoFrameExtractor frameExtractor,
        IVisionReviewProvider visionProvider,
        IAiUsageTracker usageTracker,
        IOptions<PricingOptions> pricing,
        ILogger<ClipCheckService> logger)
    {
        _storyboardRepository = storyboardRepository;
        _projectRepository = projectRepository;
        _jobReservation = jobReservation;
        _sceneAssetGenerator = sceneAssetGenerator;
        _assetService = assetService;
        _fileStorage = fileStorage;
        _mediaProbe = mediaProbe;
        _frameExtractor = frameExtractor;
        _visionProvider = visionProvider;
        _usageTracker = usageTracker;
        _pricing = pricing.Value;
        _logger = logger;
    }

    public async Task<ClipCheckResult> CheckAsync(Guid contentProjectId, Guid sceneId, bool force, CancellationToken cancellationToken = default)
    {
        // Cheap checks first - a request that is going to be answered from the
        // stored result (or rejected) never takes the project lock.
        var (scene, clip) = await LoadSceneAndClipAsync(contentProjectId, sceneId, cancellationToken);
        if (!force && ClipCheckResult.FromJson(scene.ClipCheckJson) is { } stored && stored.AssetId == clip.Id)
        {
            return stored with { EstimatedCostUsd = 0 };
        }

        if (!_visionProvider.IsConfigured)
        {
            throw new DomainException("Chưa cấu hình khóa Gemini API nên không kiểm tra clip bằng AI được.");
        }

        // One paid call in flight per project: a double-click or second tab cannot bill twice.
        var project = await _jobReservation.ReserveAsync(contentProjectId, Stage, "Đang kiểm tra clip bằng AI", cancellationToken)
            ?? throw new DomainException("Dự án đang bận với một thao tác khác - vui lòng đợi rồi thử lại.");

        try
        {
            (scene, clip) = await LoadSceneAndClipAsync(contentProjectId, sceneId, cancellationToken);
            var images = new List<VisionImage>();

            var context = await _sceneAssetGenerator.BuildContextAsync(project, cancellationToken);
            var sceneResponse = SceneResponse.FromDomain(scene);
            var selection = SceneAssetGenerator.SelectReferencesForScene(context, sceneResponse);
            foreach (var reference in selection.Images)
            {
                images.Add(new VisionImage(reference.ImageBytes, reference.MimeType, $"Reference image: {reference.Label ?? "recurring character or place"}"));
            }

            var clipPath = _fileStorage.GetAbsolutePath(clip.FilePath!);
            var media = await _mediaProbe.ProbeAsync(clipPath, cancellationToken);
            if (!media.Ok || !media.HasVideo || media.DurationSeconds <= 0)
            {
                throw new DomainException("Không đọc được file clip của cảnh này.");
            }

            for (var i = 0; i < SamplePoints.Length; i++)
            {
                byte[] frame;
                try
                {
                    frame = await _frameExtractor.ExtractFrameAsync(clipPath, media.DurationSeconds * SamplePoints[i], cancellationToken, FrameMaxHeight);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogWarning(ex, "Frame extraction failed for the clip check of Scene {SceneId}", sceneId);
                    throw new DomainException("Không trích được khung hình từ clip.");
                }

                images.Add(new VisionImage(frame, "image/png", $"Clip frame {i + 1} of {SamplePoints.Length} ({SamplePoints[i]:P0} into the clip)"));
            }

            var expectedAction = FirstNonBlank(scene.GenerationPrompt, scene.VisualDescription) ?? "(no written shot description)";
            // A character is only expected when the shot is meant to show one AND one of its references is attached.
            var characterExpected = sceneResponse.CharacterRequired
                && (selection.CharacterLabels.Count > 0 || selection.Images.Any(i => ReferenceEquals(i, context.CharacterReference)));
            var question = $"""
                Intended shot: {expectedAction}
                Recurring character expected on screen: {(characterExpected ? "yes" + (selection.CharacterLabels.Count > 0 ? $" ({string.Join(", ", selection.CharacterLabels)})" : string.Empty) : "no")}
                """;

            await _usageTracker.EnsureBudgetAvailableAsync(cancellationToken);

            var review = await _visionProvider.ReviewAsync(new VisionReviewRequest(Instruction, question, images), cancellationToken);

            // Billed as soon as the provider answered, whether or not the answer parses.
            await _usageTracker.RecordAsync(
                new RecordUsageInput("gemini", review.Model, "clip_check", _pricing.ClipCheckUsd, contentProjectId, sceneId),
                cancellationToken);

            var result = Parse(review.Json, clip.Id, review.Model, _pricing.ClipCheckUsd, characterExpected);
            scene.SetClipCheck(result.ToJson());
            await _storyboardRepository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Clip check for Scene {SceneId} (ContentProject {ContentProjectId}): {Verdict} {Score}",
                sceneId, contentProjectId, result.Verdict, result.Score);
            return result;
        }
        finally
        {
            project.ClearProgress();
            await _projectRepository.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<(Domain.Storyboards.Scene Scene, AssetResponse Clip)> LoadSceneAndClipAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken)
    {
        var storyboard = await _storyboardRepository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");
        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        var assets = await _assetService.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        var clip = assets
            .Where(a => a.SceneId == sceneId
                && a.Type == nameof(AssetType.Video)
                && a.Status == nameof(AssetStatus.Ready)
                && !string.IsNullOrWhiteSpace(a.FilePath))
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefault()
            ?? throw new DomainException("Cảnh này chưa có clip video để kiểm tra.");

        return (scene, clip);
    }

    private sealed record RawVerdict(
        string? Verdict,
        int? Score,
        bool? CharacterMatches,
        bool? ActionMatches,
        List<string>? Issues,
        string? Summary);

    /// <summary>
    /// Defensive parse of the model's JSON: unknown verdicts become "warn",
    /// the score is clamped, and a character verdict is dropped when no
    /// character was expected. Unreadable JSON is a clear error (the call was
    /// still billed and recorded above).
    /// </summary>
    public static ClipCheckResult Parse(string json, Guid assetId, string model, decimal cost, bool characterExpected)
    {
        RawVerdict? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawVerdict>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                NumberHandling = JsonNumberHandling.AllowReadingFromString,
            });
        }
        catch (JsonException)
        {
            raw = null;
        }

        if (raw is null)
        {
            throw new DomainException("AI trả về kết quả kiểm tra không đọc được - hãy thử lại.");
        }

        var verdict = raw.Verdict?.Trim().ToLowerInvariant() switch
        {
            "pass" => "pass",
            "fail" => "fail",
            _ => "warn",
        };

        var issues = (raw.Issues ?? new List<string>())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .Take(8)
            .ToList();

        return new ClipCheckResult(
            assetId,
            DateTimeOffset.UtcNow,
            verdict,
            Math.Clamp(raw.Score ?? 0, 0, 100),
            characterExpected ? raw.CharacterMatches : null,
            raw.ActionMatches ?? false,
            issues,
            string.IsNullOrWhiteSpace(raw.Summary) ? "(AI không ghi tóm tắt)" : raw.Summary.Trim(),
            model,
            cost);
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
