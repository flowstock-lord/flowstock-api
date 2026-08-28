namespace FlowStock.Domain.Common;

/// <summary>
/// Base class for domain rule violations. Every domain exception carries a stable error code
/// that is part of the public API contract (see docs/PLAN.md, section 23).
/// </summary>
public abstract class DomainException(string code, string message, IReadOnlyDictionary<string, object?>? details = null)
    : Exception(message)
{
    public string Code { get; } = code;

    public IReadOnlyDictionary<string, object?>? Details { get; } = details;
}
