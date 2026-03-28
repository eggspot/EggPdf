using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace EggPdf.AspNetCore;

/// <summary>
/// API Key authentication middleware for EggPdf.Service.
/// Checks X-Api-Key header against configured keys.
/// Enabled via configuration: EggPdf:Auth:Enabled = true, EggPdf:Auth:Mode = "ApiKey"
/// </summary>
public class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string[] _validKeys;
    private readonly bool _enabled;

    public ApiKeyAuthMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        var authSection = configuration.GetSection("EggPdf:Auth");
        _enabled = authSection.GetValue<bool>("Enabled", false);

        var mode = authSection.GetValue<string>("Mode") ?? "";
        if (!mode.Equals("ApiKey", StringComparison.OrdinalIgnoreCase))
            _enabled = false;

        var keys = authSection.GetValue<string>("ApiKeys") ?? "";
        _validKeys = keys.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Trim()).ToArray();

        if (_validKeys.Length == 0) _enabled = false;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        // Skip auth for health endpoints
        var path = context.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) ||
            !_validKeys.Contains(apiKey.ToString()))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid or missing API key" });
            return;
        }

        await _next(context);
    }
}

/// <summary>
/// Rate limiting middleware for EggPdf.Service.
/// Limits requests per minute per client IP.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly int _requestsPerMinute;
    private readonly bool _enabled;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int count, DateTime window)> _clients = new();

    public RateLimitingMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;

        var section = configuration.GetSection("EggPdf:RateLimiting");
        _enabled = section.GetValue<bool>("Enabled", false);
        _requestsPerMinute = section.GetValue<int>("RequestsPerMinute", 60);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_enabled)
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTime.UtcNow;

        var entry = _clients.GetOrAdd(clientIp, _ => (0, now));
        if ((now - entry.window).TotalMinutes >= 1)
        {
            entry = (1, now);
            _clients[clientIp] = entry;
        }
        else if (entry.count >= _requestsPerMinute)
        {
            context.Response.StatusCode = 429;
            context.Response.Headers["Retry-After"] = "60";
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }
        else
        {
            _clients[clientIp] = (entry.count + 1, entry.window);
        }

        await _next(context);
    }
}
