using System.Diagnostics;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using SerilogContext = Serilog.Context.LogContext;
using Serilog.Formatting.Compact;

namespace MaltaShop.Shared;

/// <summary>
/// The handful of things every MaltaShop service does identically: structured
/// logging with a correlation ID, the broker connection, and the two health probes.
/// Keeping it in one package is deliberate - fourteen people maintaining five
/// copies of correlation handling is a larger risk than the coupling this creates.
/// </summary>
public static class Platform
{
    public static string ServiceName => Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "unnamed-service";
    public static string Version => Environment.GetEnvironmentVariable("SERVICE_VERSION") ?? "0.0.0";
    public static string EnvironmentName => Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "local";

    /// <summary>One JSON object per line on stdout, which is the only log destination
    /// a container should know about (12-factor XI).</summary>
    public static void UseStructuredLogging(this WebApplicationBuilder builder)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            // The framework's own chatter - EF's SQL, ASP.NET's routing, MassTransit's
            // topology - is useful when debugging and noise the rest of the time. It is
            // raised to Warning so that an operator reading the log sees MaltaShop's
            // events, not the framework's.
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("MassTransit", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", ServiceName)
            .Enrich.WithProperty("version", Version)
            .Enrich.WithProperty("environment", EnvironmentName)
            .WriteTo.Console(new CompactJsonFormatter())
            .CreateLogger();
        builder.Host.UseSerilog();
    }

    /// <summary>Puts the caller's correlation ID (or a new one) on every log line
    /// written while the request is handled, and echoes it to the client.</summary>
    public static void UseCorrelationId(this WebApplication app)
    {
        app.Use(async (HttpContext context, Func<Task> next) =>
        {
            var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(correlationId))
                correlationId = Guid.NewGuid().ToString();
            context.Items["CorrelationId"] = correlationId;
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            using (SerilogContext.PushProperty("correlation_id", correlationId))
                await next();
        });
    }

    public static string CorrelationId(this HttpContext context) =>
        context.Items.TryGetValue("CorrelationId", out var value) ? value as string ?? "none" : "none";

    /// <summary>Liveness answers while the process runs; readiness answers only while
    /// the things it needs can be reached, which is what Kubernetes needs to tell
    /// "restart this" apart from "stop sending it traffic".</summary>
    public static void MapHealthProbes(this WebApplication app, Func<Task<bool>> ready)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "alive", service = ServiceName, version = Version }));
        app.MapGet("/health/ready", async () => await ready()
            ? Results.Ok(new { status = "ready", service = ServiceName })
            : Results.Json(new { status = "not-ready", service = ServiceName }, statusCode: 503));
    }

    /// <summary>Carries the correlation ID across the broker, so one customer action
    /// keeps one identifier through every service it touches.</summary>
    public static void SetCorrelation(this SendContext context, string correlationId)
    {
        context.Headers.Set("X-Correlation-Id", correlationId);
        Activity.Current?.SetTag("maltashop.correlation_id", correlationId);
    }
}
