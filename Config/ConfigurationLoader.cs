using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Botello.Config;

/// <summary>
/// Merges configuration from three sources in ascending priority order:
/// appsettings.json → environment variables (BOTELLO__*) → Logger.Parameters (MSBuild CLI).
/// </summary>
internal static class ConfigurationLoader
{
    private const string SectionName = "Botello";
    private const string AppInsightsConnectionStringEnvVar = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>
    /// Loads and returns a <see cref="LoggerConfig"/> from all available sources.
    /// </summary>
    /// <param name="loggerParameters">Raw Logger.Parameters string from the MSBuild CLI. May be null.</param>
    /// <param name="loggerAssemblyDirectory">Directory containing the logger DLL; used to locate appsettings.json.</param>
    internal static LoggerConfig Load(string? loggerParameters, string loggerAssemblyDirectory)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(loggerAssemblyDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "BOTELLO__")
            .Build();

        var config = new LoggerConfig();
        configuration.GetSection(SectionName).Bind(config);

        // Also honour the standard App Insights env var as a fallback for ConnectionString.
        if (string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            var aiConnStr = Environment.GetEnvironmentVariable(AppInsightsConnectionStringEnvVar);
            if (!string.IsNullOrWhiteSpace(aiConnStr))
                config.ConnectionString = aiConnStr;
        }

        if (!string.IsNullOrWhiteSpace(loggerParameters))
            ApplyLoggerParameters(loggerParameters, config);

        return config;
    }

    private static void ApplyLoggerParameters(string parameters, LoggerConfig config)
    {
        foreach (var token in parameters.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eqIdx = token.IndexOf('=');
            if (eqIdx <= 0)
                continue;

            var key   = token[..eqIdx].Trim();
            var value = token[(eqIdx + 1)..].Trim();

            if (string.IsNullOrEmpty(value))
                continue;

            // ── Exporter selection ──────────────────────────────────────
            if (key.Equals("Exporter", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse(value, ignoreCase: true, out ExporterType exporter))
                    config.Exporter = exporter;
                else
                    throw new InvalidOperationException(
                        $"Botello: Unknown Exporter value '{value}'. " +
                        "Valid values: AzureMonitor, Otlp.");
            }
            // ── Azure Monitor ───────────────────────────────────────────
            else if (key.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase))
                config.ConnectionString = value;
            // ── OTLP ────────────────────────────────────────────────────
            else if (key.Equals("OtlpEndpoint", StringComparison.OrdinalIgnoreCase))
                config.OtlpEndpoint = value;
            else if (key.Equals("OtlpProtocol", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse(value, ignoreCase: true, out OtlpProtocolType protocol))
                    config.OtlpProtocol = protocol;
                else
                    throw new InvalidOperationException(
                        $"Botello: Unknown OtlpProtocol value '{value}'. " +
                        "Valid values: Grpc, HttpProtobuf.");
            }
            else if (key.Equals("OtlpHeaders", StringComparison.OrdinalIgnoreCase))
                config.OtlpHeaders = value;
            else if (key.Equals("OtlpTimeout", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(value, out var timeout) && timeout > 0)
                    config.OtlpTimeout = timeout;
                else
                    throw new InvalidOperationException(
                        $"Botello: Invalid OtlpTimeout value '{value}'. " +
                        "Provide a positive integer (milliseconds).");
            }
            // ── Common ──────────────────────────────────────────────────
            else if (key.Equals("ServiceName", StringComparison.OrdinalIgnoreCase))
                config.ServiceName = value;
            else if (key.Equals("MinimumLevel", StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse(value, ignoreCase: true, out LogLevel level))
                    config.MinimumLevel = level;
                else
                    throw new InvalidOperationException(
                        $"Botello: Unknown MinimumLevel value '{value}'. " +
                        "Valid values: Trace, Debug, Information, Warning, Error, Critical.");
            }
            else if (key.Equals("IncludeMessages", StringComparison.OrdinalIgnoreCase))
                config.IncludeMessages = ParseBool(key, value);
            else if (key.Equals("IncludeWarnings", StringComparison.OrdinalIgnoreCase))
                config.IncludeWarnings = ParseBool(key, value);
            else if (key.Equals("IncludeErrors", StringComparison.OrdinalIgnoreCase))
                config.IncludeErrors = ParseBool(key, value);
            else if (key.Equals("IncludeProjectEvents", StringComparison.OrdinalIgnoreCase))
                config.IncludeProjectEvents = ParseBool(key, value);
            else if (key.Equals("IncludeTargetEvents", StringComparison.OrdinalIgnoreCase))
                config.IncludeTargetEvents = ParseBool(key, value);
        }
    }

    private static bool ParseBool(string key, string value)
    {
        if (bool.TryParse(value, out var result))
            return result;

        if (value is "1" or "yes" or "on")        
            return true;

        if (value is "0" or "no"  or "off")
            return false;

        throw new InvalidOperationException(
            $"Botello: Invalid boolean value '{value}' for parameter '{key}'. " +
            "Use true/false, 1/0, yes/no, or on/off.");
    }
}
