using System.Collections.Concurrent;

namespace AiContentFactory.Application.Stories;

/// <summary>
/// Per-character in-flight guard for the operations that write a reference image candidate
/// (a billable generation, or an upload). A second concurrent attempt for the same character is
/// refused instead of spending a second paid call or racing the first one's write.
/// </summary>
public interface IStoryReferenceGenerationGate
{
    /// <summary>Returns a lease to dispose when the operation ends, or null when one is already running for this character.</summary>
    IDisposable? TryEnter(Guid characterId);
}

/// <summary>
/// Process-local (register as a singleton). Guards concurrent requests inside this API
/// instance - the deployment target - not several instances behind a load balancer.
/// </summary>
public sealed class StoryReferenceGenerationGate : IStoryReferenceGenerationGate
{
    private readonly ConcurrentDictionary<Guid, byte> _inFlight = new();

    public IDisposable? TryEnter(Guid characterId) =>
        _inFlight.TryAdd(characterId, 0) ? new Lease(this, characterId) : null;

    private sealed class Lease : IDisposable
    {
        private readonly StoryReferenceGenerationGate _gate;
        private readonly Guid _characterId;
        private int _released;

        public Lease(StoryReferenceGenerationGate gate, Guid characterId)
        {
            _gate = gate;
            _characterId = characterId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate._inFlight.TryRemove(_characterId, out _);
            }
        }
    }
}
