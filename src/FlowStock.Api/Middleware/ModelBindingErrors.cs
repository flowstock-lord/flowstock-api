using FlowStock.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace FlowStock.Api.Middleware;

/// <summary>
/// Model binding fails before <see cref="ValidationFilter"/> ever runs — a malformed body, a
/// non-numeric page, an unknown enum name. Those failures must still come back in the FlowStock
/// error envelope instead of ASP.NET Core's default ProblemDetails.
/// </summary>
public static class ModelBindingErrors
{
    public static IActionResult ToErrorResponse(ActionContext context)
    {
        var details = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .ToDictionary(
                entry => Key(entry.Key),
                entry => (object?)entry.Value!.Errors.Select(error => Describe(entry.Key, error)).ToArray());

        return new BadRequestObjectResult(
            new ErrorResponse(ErrorCodes.ValidationFailed, "Validation failed.", details));
    }

    /// <summary>The JSON reader reports paths as "$.sku"; clients only need the property name.</summary>
    private static string Key(string key) => key switch
    {
        "" => "request",
        _ when key.StartsWith("$.") => key[2..],
        "$" => "request",
        _ => key
    };

    /// <summary>
    /// Deserializer messages name internal CLR types, which must not reach clients. Binder
    /// messages ("The value 'x' is not valid.") are safe and useful, so they pass through.
    /// </summary>
    private static string Describe(string key, ModelError error) =>
        key.StartsWith('$') || error.Exception is not null || string.IsNullOrEmpty(error.ErrorMessage)
            ? "The value has an invalid format."
            : error.ErrorMessage;
}
