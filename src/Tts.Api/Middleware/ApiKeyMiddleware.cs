using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;

namespace Tts.Api.Middleware;

/// <summary>
/// Security gate: every request to the events API must carry a valid <c>X-API-Key</c> header.
/// Fails closed — when no key is configured, all guarded requests are rejected.
/// Health-check endpoints are intentionally left open.
/// </summary>
public sealed class ApiKeyMiddleware
{
    public const string HeaderName = "X-API-Key";

    private readonly RequestDelegate _next;
    private readonly string _configuredKey;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, IOptions<TelemetryOptions> options, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _configuredKey = options.Value.ApiKey;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only the events API is protected; health checks must stay reachable for orchestrators.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        var presented = context.Request.Headers[HeaderName].ToString();
        if (!IsAuthorized(presented))
        {
            _logger.LogWarning("Rejected {Method} {Path}: missing or invalid {Header}",
                context.Request.Method, context.Request.Path, HeaderName);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "invalid or missing API key" });
            return;
        }

        await _next(context);
    }

    /// <summary>Constant-time comparison so the check does not leak the key via timing.</summary>
    private bool IsAuthorized(string presented)
    {
        if (string.IsNullOrEmpty(_configuredKey) || string.IsNullOrEmpty(presented))
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(_configuredKey),
            Encoding.UTF8.GetBytes(presented));
    }
}
