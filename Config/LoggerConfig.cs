using Microsoft.Extensions.Logging;

namespace Botello.Config;

/// <summary>
/// Selects the telemetry export back-end.
/// </summary>
public enum ExporterType
{
    /// <summary>Azure Application Insights via the Azure Monitor exporter.</summary>
    AzureMonitor,

    /// <summary>Any OTLP-compatible collector (Jaeger, Grafana, Seq, Aspire Dashboard, etc.).</summary>
    Otlp,
}

/// <summary>
/// Selects the OTLP transport protocol. Only relevant when <see cref="LoggerConfig.Exporter"/>
/// is set to <see cref="ExporterType.Otlp"/>.
/// </summary>
public enum OtlpProtocolType
{
    /// <summary>gRPC transport (default OTLP port 4317).</summary>
    Grpc,

    /// <summary>HTTP/protobuf transport (default OTLP port 4318).</summary>
    HttpProtobuf,
}

/// <summary>
/// Configuration for the Botello logger.
/// Loaded from appsettings.json, environment variables (BOTELLO__*), and Logger.Parameters.
/// </summary>
public sealed class LoggerConfig
{
    // ── Exporter selection ──────────────────────────────────────────────

    /// <summary>
    /// Selects the telemetry back-end. Defaults to <see cref="ExporterType.AzureMonitor"/>.
    /// Set to <see cref="ExporterType.Otlp"/> to export to any OTLP-compatible collector.
    /// </summary>
    public ExporterType Exporter { get; set; } = ExporterType.AzureMonitor;

    // ── Azure Monitor settings ──────────────────────────────────────────

    /// <summary>Azure Application Insights connection string. Required when Exporter is AzureMonitor.</summary>
    public string? ConnectionString { get; set; }

    // ── OTLP settings ───────────────────────────────────────────────────

    /// <summary>
    /// OTLP collector endpoint. Defaults to <c>http://localhost:4317</c> for gRPC
    /// or <c>http://localhost:4318</c> for HTTP/protobuf. Only used when Exporter is Otlp.
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// OTLP transport protocol. Defaults to <see cref="OtlpProtocolType.Grpc"/>.
    /// Only used when Exporter is Otlp.
    /// </summary>
    public OtlpProtocolType OtlpProtocol { get; set; } = OtlpProtocolType.Grpc;

    /// <summary>
    /// Optional headers sent with every OTLP export request (comma-separated key=value pairs).
    /// Example: <c>Authorization=Bearer token123,X-Custom=value</c>.
    /// Only used when Exporter is Otlp.
    /// </summary>
    public string? OtlpHeaders { get; set; }

    /// <summary>
    /// Timeout in milliseconds for OTLP export requests. Defaults to 10 000 (10 s).
    /// Only used when Exporter is Otlp.
    /// </summary>
    public int OtlpTimeout { get; set; } = 10_000;

    // ── Common settings ─────────────────────────────────────────────────

    /// <summary>Service name reported in telemetry. Defaults to "Botello".</summary>
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
        if (Exporter == ExporterType.AzureMonitor)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                throw new InvalidOperationException(
                    "Botello: A connection string for Application Insights is required when " +
                    "Exporter is AzureMonitor. Provide it via appsettings.json (Botello:ConnectionString), " +
                    "the APPLICATIONINSIGHTS_CONNECTION_STRING or BOTELLO__CONNECTIONSTRING " +
                    "environment variable, or the Logger.Parameters CLI argument " +
                    "(-logger:Botello.dll;ConnectionString=...).");
        }

        // OTLP does not strictly require an endpoint — it falls back to localhost defaults.

        if (string.IsNullOrWhiteSpace(ServiceName))
            ServiceName = "Botello";
    }
}
