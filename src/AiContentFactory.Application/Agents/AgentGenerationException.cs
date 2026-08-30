namespace AiContentFactory.Application.Agents;

/// <summary>
/// Raised when an agent's LLM output could not be turned into valid,
/// usable structured data - either the provider failed, or the JSON was
/// invalid/failed validation even after one repair attempt. Callers (the
/// pipeline orchestrator, background jobs) should catch this and transition
/// the ContentProject to Failed rather than letting it bubble as a 500.
/// </summary>
public class AgentGenerationException : Exception
{
    public AgentGenerationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
