using System.Data.Common;
using System.Text.Json;
using AttendanceSystem.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AttendanceSystem.Api.Middleware;

/// <summary>
/// Catches all unhandled exceptions and emits RFC 7807 ProblemDetails
/// (Content-Type: application/problem+json). Domain exceptions map to specific status codes.
/// </summary>
public class GlobalExceptionMiddleware
{
    private static readonly Dictionary<Type, (int StatusCode, string Title)> ExceptionMap = new()
    {
        [typeof(AlreadyClockedInException)]       = (409, "Already Clocked In"),
        [typeof(NotClockedInException)]           = (400, "Not Clocked In"),
        [typeof(TimeServiceUnavailableException)] = (503, "Time Service Unavailable"),
        [typeof(EmployeeNotFoundException)]       = (404, "Employee Not Found"),
        [typeof(InvalidShiftException)]           = (422, "Invalid Shift"),
        [typeof(ValidationException)]             = (400, "Validation Error"),
        [typeof(KeyNotFoundException)]            = (404, "Not Found"),
        [typeof(UnauthorizedAccessException)]     = (401, "Unauthorized"),
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger, IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        int statusCode;
        string title;
        string? detailOverride = null;

        if (ex is DbUpdateException dbEx && IsUniqueViolation(dbEx))
        {
            // The UX_OneActiveSession unique filtered index rejected a concurrent second clock-in.
            statusCode = StatusCodes.Status409Conflict;
            title = "Already Clocked In";
            detailOverride = "You already have an active clock-in session.";
        }
        else if (ex is DbUpdateException or DbException)
        {
            // Any other persistence failure (e.g. database unavailable) is a 500 — NOT a 503.
            statusCode = StatusCodes.Status500InternalServerError;
            title = "Database Error";
        }
        else if (ExceptionMap.TryGetValue(ex.GetType(), out var mapped))
        {
            (statusCode, title) = mapped;
        }
        else
        {
            statusCode = StatusCodes.Status500InternalServerError;
            title = "An unexpected error occurred";
        }

        // Map known domain exceptions at the expected level; log unexpected ones as errors with stack trace.
        if (statusCode >= 500)
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
        else
            _logger.LogWarning("Handled {ExceptionType}: {Message}", ex.GetType().Name, ex.Message);

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Detail = detailOverride ?? (statusCode >= 500 && !_env.IsDevelopment()
                ? "An internal error occurred. Please try again later." // never leak internals in prod
                : ex.Message),
            Instance = context.Request.Path
        };

        // ValidationException → include all errors under 'errors'
        if (ex is ValidationException validationException)
        {
            problem.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        // Only expose stack traces in Development.
        if (_env.IsDevelopment() && statusCode >= 500)
            problem.Extensions["stackTrace"] = ex.StackTrace;

        // 503 from the time service → ask the client to retry after the circuit-breaker window.
        if (ex is TimeServiceUnavailableException)
            context.Response.Headers.RetryAfter = "30";

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    // SQL Server unique-constraint violations: 2627 (PK/unique constraint), 2601 (unique index).
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sql && (sql.Number == 2601 || sql.Number == 2627);
}
