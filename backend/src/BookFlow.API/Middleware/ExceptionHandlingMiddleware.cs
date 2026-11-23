using System.Diagnostics;
using BookFlow.Application.Common.Models;
using BookFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookFlow.API.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _log;
    private readonly IHostEnvironment _env;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> log, IHostEnvironment env)
    {
        _next = next; _log = log; _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            var (status, title, type, extensions) = Map(ex);
            if (status >= 500) _log.LogError(ex, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
            else _log.LogInformation("Request failed with {Status}: {Message}", status, ex.Message);

            var problem = new ProblemDetails
            {
                Status = status,
                Title = title,
                Type = type,
                Detail = status >= 500 && !_env.IsDevelopment() ? "An unexpected error occurred." : ex.Message,
                Instance = context.Request.Path
            };
            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;
            foreach (var (k, v) in extensions) problem.Extensions[k] = v;

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    private static (int status, string title, string type, Dictionary<string, object?> ext) Map(Exception ex)
    {
        var ext = new Dictionary<string, object?>();
        switch (ex)
        {
            case AppValidationException v:
                ext["errors"] = v.Errors;
                return (StatusCodes.Status400BadRequest, "Validation failed", "validation", ext);
            case NotFoundException:
                return (StatusCodes.Status404NotFound, "Not found", "not-found", ext);
            case ForbiddenException:
                return (StatusCodes.Status403Forbidden, "Forbidden", "forbidden", ext);
            case InsufficientStockException s:
                ext["code"] = s.Code; ext["bookIds"] = s.BookIds;
                return (StatusCodes.Status409Conflict, "Insufficient stock", "insufficient-stock", ext);
            case ConflictException c:
                ext["code"] = c.Code;
                return (StatusCodes.Status409Conflict, "Conflict", c.Code, ext);
            case BusinessRuleException b:
                ext["code"] = b.Code;
                return (StatusCodes.Status422UnprocessableEntity, "Business rule violated", b.Code, ext);
            case DbUpdateConcurrencyException:
                ext["code"] = "concurrency";
                return (StatusCodes.Status409Conflict, "The record was modified by someone else", "concurrency", ext);
            case DbUpdateException dbe when dbe.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
                                           || dbe.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true:
                ext["code"] = "duplicate";
                return (StatusCodes.Status409Conflict, "Duplicate record", "duplicate", ext);
            case OperationCanceledException:
                return (StatusCodes.Status499ClientClosedRequest, "Request cancelled", "cancelled", ext);
            case DomainException:
                return (StatusCodes.Status400BadRequest, "Bad request", "domain", ext);
            default:
                return (StatusCodes.Status500InternalServerError, "Server error", "server-error", ext);
        }
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var h = context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        h["Cache-Control"] = "no-store";
        return _next(context);
    }
}
