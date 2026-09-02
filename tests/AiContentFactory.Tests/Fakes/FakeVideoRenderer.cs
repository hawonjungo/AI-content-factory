using AiContentFactory.Application.Rendering;

namespace AiContentFactory.Tests.Fakes;

public sealed class FakeVideoRenderer : IVideoRenderer
{
    public RenderRequest? LastRequest { get; private set; }
    public int Calls { get; private set; }
    public Exception? Throw { get; set; }
    public double ReportedDuration { get; set; } = 61;

    public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastRequest = request;
        if (Throw is not null)
        {
            throw Throw;
        }

        return Task.FromResult(new RenderResult(request.OutputAbsolutePath, ReportedDuration));
    }

    public Task<string> RenderCaptionPreviewAsync(CaptionPreviewRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(request.OutputAbsolutePath);
}
