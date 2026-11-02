using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using POSApp.Services;
using Serilog.Core;
using Serilog.Events;

namespace POSApp.Logging;

public class RequestCorrelationEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RequestCorrelationEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null) return;

        // Pair a unique correlation ID to this request
        if (!context.Items.TryGetValue("CorrelationId", out var correlationObj) || correlationObj is not string correlationId || string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = context.TraceIdentifier;
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.NewGuid().ToString("N");
            }
            context.Items["CorrelationId"] = correlationId;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestId", context.TraceIdentifier));

        var ip = context.Connection.RemoteIpAddress?.ToString();
        if (!string.IsNullOrWhiteSpace(ip))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientIp", ip));
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserId", userId));
            }
            if (!string.IsNullOrWhiteSpace(context.User.Identity.Name))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserName", context.User.Identity.Name));
            }
        }

        if (WorkingDayRequest.Date.HasValue)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("WorkingDay", WorkingDayRequest.Date.Value.ToString("yyyy-MM-dd")));
        }
    }
}
