using AiContentFactory.Domain.Exceptions;
using AiContentFactory.Domain.Storyboards;

namespace AiContentFactory.Application.Storyboards;

public interface IStoryboardService
{
    Task<StoryboardResponse> GetOrCreateAsync(Guid contentProjectId, CancellationToken cancellationToken = default);
    Task<StoryboardResponse> AddSceneAsync(Guid contentProjectId, CreateSceneRequest request, CancellationToken cancellationToken = default);
    Task<StoryboardResponse> UpdateSceneAsync(Guid contentProjectId, Guid sceneId, UpdateSceneRequest request, CancellationToken cancellationToken = default);
    Task<StoryboardResponse> RemoveSceneAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default);
    Task SetScenePromptAsync(Guid contentProjectId, Guid sceneId, string prompt, string? negativePrompt, string? visualStyle, string provider, CancellationToken cancellationToken = default);
}

public class StoryboardService : IStoryboardService
{
    private readonly IStoryboardRepository _repository;

    public StoryboardService(IStoryboardRepository repository)
    {
        _repository = repository;
    }

    public async Task<StoryboardResponse> GetOrCreateAsync(Guid contentProjectId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);

        if (storyboard is null)
        {
            storyboard = Storyboard.Create(contentProjectId);
            await _repository.AddAsync(storyboard, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }

        return StoryboardResponse.FromDomain(storyboard);
    }

    public async Task<StoryboardResponse> AddSceneAsync(Guid contentProjectId, CreateSceneRequest request, CancellationToken cancellationToken = default)
    {
        var storyboard = await GetOrCreateStoryboardEntityAsync(contentProjectId, cancellationToken);

        storyboard.AddScene(request.DurationSeconds, request.Narration, request.VisualDescription, request.CameraDirection, request.VisualType);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryboardResponse.FromDomain(storyboard);
    }

    public async Task<StoryboardResponse> UpdateSceneAsync(Guid contentProjectId, Guid sceneId, UpdateSceneRequest request, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        scene.UpdateContent(request.DurationSeconds, request.Narration, request.VisualDescription, request.CameraDirection, request.VisualType);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryboardResponse.FromDomain(storyboard);
    }

    public async Task<StoryboardResponse> RemoveSceneAsync(Guid contentProjectId, Guid sceneId, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        storyboard.RemoveScene(sceneId);
        await _repository.SaveChangesAsync(cancellationToken);

        return StoryboardResponse.FromDomain(storyboard);
    }

    public async Task SetScenePromptAsync(Guid contentProjectId, Guid sceneId, string prompt, string? negativePrompt, string? visualStyle, string provider, CancellationToken cancellationToken = default)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken)
            ?? throw new DomainException("Storyboard not found for this content project.");

        var scene = storyboard.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new DomainException($"Scene '{sceneId}' was not found on this storyboard.");

        scene.SetGenerationPrompt(prompt, negativePrompt, visualStyle, provider);
        await _repository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Storyboard> GetOrCreateStoryboardEntityAsync(Guid contentProjectId, CancellationToken cancellationToken)
    {
        var storyboard = await _repository.GetByContentProjectIdAsync(contentProjectId, cancellationToken);
        if (storyboard is not null)
        {
            return storyboard;
        }

        storyboard = Storyboard.Create(contentProjectId);
        await _repository.AddAsync(storyboard, cancellationToken);
        return storyboard;
    }
}
