using AiContentFactory.Application.Assets;
using AiContentFactory.Application.Presets;
using AiContentFactory.Application.Publishing;
using AiContentFactory.Domain.Assets;
using AiContentFactory.Domain.ContentProjects;
using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Publishing;

namespace AiContentFactory.Application.ContentProjects;

public class ContentProjectService : IContentProjectService
{
    private readonly IContentProjectRepository _repository;
    private readonly IAssetService _assetService;
    private readonly IPublishJobRepository _publishJobs;

    public ContentProjectService(IContentProjectRepository repository, IAssetService assetService, IPublishJobRepository publishJobs)
    {
        _repository = repository;
        _assetService = assetService;
        _publishJobs = publishJobs;
    }

    public async Task<ContentProjectResponse> CreateAsync(CreateContentProjectRequest request, CancellationToken cancellationToken = default)
    {
        var template = PresetCatalog.FindTemplate(request.TemplateId);

        var project = ContentProject.Create(
            request.Title,
            request.Topic,
            // A template's niche is a sensible default but never overrides one
            // the user typed.
            request.Niche ?? template?.Niche,
            request.TargetDurationSeconds,
            request.AspectRatio ?? template?.DefaultAspectRatio ?? "9:16",
            request.Language ?? "en");

        if (template is not null || request.StylePresetId is not null || request.VoicePresetId is not null || request.CaptionPresetId is not null)
        {
            var captionPresetId = request.CaptionPresetId ?? template?.DefaultCaptionPresetId;

            project.ApplyPresets(
                template?.Id,
                request.StylePresetId ?? template?.DefaultStylePresetId,
                request.VoicePresetId ?? template?.DefaultVoicePresetId,
                captionPresetId,
                PresetCatalog.FindCaption(captionPresetId)?.Settings);
        }

        if (request.IdeaConfig is not null)
        {
            project.UpdateIdeaConfig(request.IdeaConfig.ToDomain());
        }

        await _repository.AddAsync(project, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<IReadOnlyList<ContentProjectResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var projects = await _repository.GetAllAsync(cancellationToken);
        return projects.Select(ContentProjectResponse.FromDomain).ToList();
    }

    public async Task<ContentProjectResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        return project is null ? null : ContentProjectResponse.FromDomain(project);
    }

    public async Task<ContentProjectResponse?> UpdateAsync(Guid id, UpdateContentProjectRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.UpdateDetails(request.Title, request.Topic, request.Niche, request.TargetDurationSeconds);
        if (request.IdeaConfig is not null)
        {
            project.UpdateIdeaConfig(request.IdeaConfig.ToDomain());
        }
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<ContentProjectResponse?> ChangeStatusAsync(Guid id, ChangeContentProjectStatusRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.TransitionTo(request.Status);
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<ContentProjectResponse?> SetAudioModeAsync(Guid id, SetAudioModeRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.SetAudioMode(request.AudioMode);
        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<ContentProjectResponse?> SetVoiceSettingsAsync(Guid id, SetVoiceSettingsRequest request, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        project.SetVoicePreset(request.VoicePresetId);

        var cfg = project.IdeaConfig;
        project.UpdateIdeaConfig(ContentIdeaConfig.Create(
            cfg.ContentPillar, cfg.TargetAudience, cfg.StoryType, cfg.HookStyle, cfg.Emotion,
            request.VoiceGender, request.VoiceStyle, request.SpeakingRate, request.NarrationLanguage,
            cfg.CreditStrategy));

        // The existing per-scene TTS tracks were made with the old voice - drop
        // them so the next render regenerates each narrated scene with the new
        // one. Original clip audio and non-narrated scenes are untouched.
        var assets = await _assetService.GetByContentProjectIdAsync(id, cancellationToken);
        var narratedSceneIds = assets
            .Where(a => a.Type == nameof(AssetType.Voice)
                        && a.Status == nameof(AssetStatus.Ready)
                        && a.SceneId is not null)
            .Select(a => a.SceneId!.Value)
            .Distinct();

        foreach (var sceneId in narratedSceneIds)
        {
            await _assetService.SupersedeSceneAssetsAsync(id, sceneId, AssetType.Voice, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return ContentProjectResponse.FromDomain(project);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _repository.GetByIdAsync(id, cancellationToken);
        if (project is null)
        {
            return false;
        }

        if (!project.Progress.IsIdle)
        {
            throw new DomainException("Dự án đang được xử lý (tạo nội dung/render), không thể xóa lúc này - hãy đợi xong hoặc huỷ trước.");
        }

        // A publish job still queued/running, or a failed one the user could
        // still retry, may need this project's rendered file - deleting the
        // project (and its data) out from under it would break that operation.
        // A Published/Cancelled/permanently-Failed job needs nothing further
        // from the project and is left alone either way (see
        // IContentProjectRepository.DeleteWithProjectDataAsync).
        var publishJobs = await _publishJobs.GetByProjectAsync(id, cancellationToken);
        var blocking = publishJobs.FirstOrDefault(j =>
            j.Status is PublishJobStatus.Pending or PublishJobStatus.Scheduled or PublishJobStatus.Publishing
            || j.CanRetry);
        if (blocking is not null)
        {
            throw new DomainException(
                $"Dự án có tác vụ đăng ({blocking.Platform}) đang chờ hoặc có thể thử lại - hãy xử lý xong ở Bước 7 trước khi xóa.");
        }

        await _repository.DeleteWithProjectDataAsync(id, cancellationToken);
        return true;
    }
}
