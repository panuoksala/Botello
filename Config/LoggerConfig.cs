using Microsoft.Extensions.Logging;

namespace Botello.Config;

/// <summary>
/// Configuration for the Botello logger.
/// Loaded from appsettings.json, environment variables (BOTELLO__*), and Logger.Parameters.
/// </summary>
public sealed class LoggerConfig
{
    /// <summary>Azure Application Insights connection string. Required.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Service name reported in Application Insights. Defaults to "Botello".</summary>
    public string ServiceName { get; set; } = "Botello";

    /// <summary>Minimum log level to emit. Defaults to <see cref="LogLevel.Information"/>.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>Forward MSBuild messages (High→Information, Normal→Debug, Low→Trace). Default: true.</summary>
    public bool IncludeMessages { get; set; } = true;

    /// <summary>Forward MSBuild warnings. Default: true.</summary>
    public bool IncludeWarnings { get; set; } = true;

    /// <summary>Forward MSBuild errors. Default: true.</summary>
    public bool IncludeErrors { get; set; } = true;

    /// <summary>Emit spans and logs for project start/finish events. Default: true.</summary>
    public bool IncludeProjectEvents { get; set; } = true;

    /// <summary>Emit spans and logs for target start/finish events. Can be noisy. Default: false.</summary>
    public bool IncludeTargetEvents { get; set; } = false;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
            throw new InvalidOperationException(
                "Botello: A connection string for Application Insights is required. " +
                "Provide it via appsettings.json (Botello:ConnectionString), " +
                "the APPLICATIONINSIGHTS_CONNECTION_STRING or BOTELLO__CONNECTIONSTRING " +
                "environment variable, or the Logger.Parameters CLI argument " +
                "(-logger:Botello.dll;ConnectionString=...).");

        if (string.IsNullOrWhiteSpace(ServiceName))
            ServiceName = "Botello";
    }
}
