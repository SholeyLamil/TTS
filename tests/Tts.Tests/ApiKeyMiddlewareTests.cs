using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tts.Api.Configuration;
using Tts.Api.Middleware;
using Xunit;

namespace Tts.Tests;

public class ApiKeyMiddlewareTests
{
    private static (ApiKeyMiddleware mw, Func<bool> nextCalled) Build(string configuredKey)
    {
        var called = false;
        RequestDelegate next = _ => { called = true; return Task.CompletedTask; };
        var mw = new ApiKeyMiddleware(
            next,
            Options.Create(new TelemetryOptions { ApiKey = configuredKey }),
            NullLogger<ApiKeyMiddleware>.Instance);
        return (mw, () => called);
    }

    private static DefaultHttpContext Request(string path, string? apiKey)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        if (apiKey is not null)
            ctx.Request.Headers[ApiKeyMiddleware.HeaderName] = apiKey;
        return ctx;
    }

    [Fact]
    public async Task Rejects_api_request_without_key()
    {
        var (mw, nextCalled) = Build("secret");
        var ctx = Request("/api/events", apiKey: null);

        await mw.InvokeAsync(ctx);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Rejects_api_request_with_wrong_key()
    {
        var (mw, nextCalled) = Build("secret");
        var ctx = Request("/api/events", apiKey: "nope");

        await mw.InvokeAsync(ctx);

        Assert.False(nextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Allows_api_request_with_valid_key()
    {
        var (mw, nextCalled) = Build("secret");
        var ctx = Request("/api/events", apiKey: "secret");

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled());
    }

    [Fact]
    public async Task Leaves_health_endpoints_open()
    {
        var (mw, nextCalled) = Build("secret");
        var ctx = Request("/health/ready", apiKey: null);

        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled());
    }
}
