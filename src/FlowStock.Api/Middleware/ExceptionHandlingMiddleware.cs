using System.Net;
using FlowStock.Application.Common;
using FlowStock.Domain.Common;
using FluentValidation;

namespace FlowStock.Api.Middleware;

/// <summary>
/// Translates exceptions into the FlowStock error envelope so every endpoint fails the same way.
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning(ex, "Validation failed for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            var details = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => (object?)g.Select(e => e.ErrorMessage).ToArray());

            await WriteAsync(context, HttpStatusCode.BadRequest,
                new ErrorResponse(ErrorCodes.ValidationFailed, "Validation failed.", details));
        }
        catch (DomainException ex)
        {
            logger.LogWarning(ex, "Domain rule {Code} rejected {Method} {Path}",
                ex.Code, context.Request.Method, context.Request.Path);

            await WriteAsync(context, HttpStatusCode.BadRequest,
                new ErrorResponse(ex.Code, ex.Message, ex.Details));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception for {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteAsync(context, HttpStatusCode.InternalServerError,
                new ErrorResponse(ErrorCodes.InternalError, "An unexpected error occurred."));
        }
    }

    private static async Task WriteAsync(HttpContext context, HttpStatusCode statusCode, ErrorResponse error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json";

        await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
    }
}
