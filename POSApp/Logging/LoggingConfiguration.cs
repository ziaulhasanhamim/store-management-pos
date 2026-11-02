using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.OpenTelemetry;

namespace POSApp.Logging;

public static class LoggingConfiguration
{
    public static void Configure(
        LoggerConfiguration loggerConfig,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var httpContextAccessor = serviceProvider.GetService<IHttpContextAccessor>();

        loggerConfig
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore.Routing", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithProperty("Application", "POSApp")
            .Enrich.WithProperty("Environment", environment.EnvironmentName);

        if (httpContextAccessor is not null)
        {
            loggerConfig.Enrich.With(new RequestCorrelationEnricher(httpContextAccessor));
        }

        // 1. Console Sink: formatted and colored with CorrelationId
        loggerConfig.WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}] {SourceContext}: {Message:lj}{NewLine}{Exception}"
        );

        // 2. Axiom Cloud Sink (Development & Production)
        var axiomToken = configuration["AXIOM_API_TOKEN"] ?? configuration["Axiom:ApiToken"];
        var defaultDataset = environment.IsDevelopment() ? "posapp-dev-logs" : "posapp-logs";
        var axiomDataset = configuration["AXIOM_DATASET"] ?? configuration["Axiom:Dataset"] ?? defaultDataset;

        if (!string.IsNullOrWhiteSpace(axiomToken))
        {
            loggerConfig.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = "https://api.axiom.co/v1/logs";
                options.Protocol = OtlpProtocol.HttpProtobuf;
                options.Headers = new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {axiomToken}",
                    ["X-Axiom-Dataset"] = axiomDataset
                };
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = "posapp",
                    ["deployment.environment"] = environment.EnvironmentName
                };
            });
        }
    }
}
