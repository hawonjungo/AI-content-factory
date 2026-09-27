namespace AiContentFactory.Application.ContentProjects;

public interface IContentProjectService
{
    Task<ContentProjectResponse> CreateAsync(CreateContentProjectRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ContentProjectResponse>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> UpdateAsync(Guid id, UpdateContentProjectRequest request, CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> ChangeStatusAsync(Guid id, ChangeContentProjectStatusRequest request, CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> SetAudioModeAsync(Guid id, SetAudioModeRequest request, CancellationToken cancellationToken = default);
    Task<ContentProjectResponse?> SetVoiceSettingsAsync(Guid id, SetVoiceSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a project and its own generation-pipeline data. Returns false
    /// if the project doesn't exist (caller returns 404). Throws
    /// <see cref="AiContentFactory.Domain.Exceptions.DomainException"/> if the
    /// project is currently generating, or has a publish job that's still in
    /// flight or could still be retried - deleting out from under either
    /// would break that operation.
    /// </summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
