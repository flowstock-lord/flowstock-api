namespace FlowStock.Application.Common;

/// <summary>
/// The single error envelope returned by every API endpoint (see docs/PLAN.md, section 23).
/// </summary>
/// <param name="Code">Stable error code, e.g. INSUFFICIENT_STOCK.</param>
/// <param name="Message">Human readable message.</param>
/// <param name="Details">Optional structured context.</param>
public record ErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null);

/// <summary>
/// Error codes that are not tied to a single domain rule.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InternalError = "INTERNAL_ERROR";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string UserInactive = "USER_INACTIVE";
}
