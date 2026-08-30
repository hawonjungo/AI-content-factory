namespace AiContentFactory.Domain.Exceptions;

/// <summary>
/// Raised when an operation would violate a domain invariant
/// (e.g. an illegal status transition). Callers in the API layer
/// should map this to HTTP 400/409 rather than a generic 500.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
