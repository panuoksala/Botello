using System.Diagnostics;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.Logging;
using Botello.Config;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Botello.Telemetry;

/// <summary>
/// Owns the OTel pipeline lifetime: a <see cref="TracerProvider"/> for spans
/// and an <see cref="ILoggerFactory"/> for log records, exporting to either
/// Azure Monitor or any OTLP-compatible collector.
/// Call <see cref="Initialize"/> once before use and <see cref="Dispose"/> to flush.
/// </summary>
internal sealed class OtelPipelineManager : IDisposable
{
    internal const string ActivitySourceName = "Botello";

    /// <summary>Shared <see cref="ActivitySource"/> used to create all MSBuild spans.</summary>
    internal static readonly ActivitySource BuildActivitySource =
        new(ActivitySourceName, "1.0.0");

    private TracerProvider? _tracerProvider;
    private ILoggerFactory? _loggerFactory;
    private bool _disposed;

    internal void Initialize(LoggerConfig config)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var resourceBuilder = BuildResourceBuilder(config);
        _tracerProvider = BuildTracerProvider(config, resourceBuilder);
        _loggerFactory  = BuildLoggerFactory(config, resourceBuilder);
    }

    internal ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loggerFactory is null)
            throw new InvalidOperationException(
                "Botello: OtelPipelineManager.Initialize() must be called before CreateLogger().");

        return _loggerFactory.CreateLogger(categoryName);
    }

    /// <summary>Flushes and disposes both the tracer provider and the logger factory.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Traces first, then logs — preserves causality in the back-end.
        _tracerProvider?.Dispose();
        _loggerFactory?.Dispose();
    }

    // ── Resource ────────────────────────────────────────────────────────

    private static ResourceBuilder BuildResourceBuilder(LoggerConfig config) =>
        ResourceBuilder
            .CreateDefault()
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.name"]        = config.ServiceName,
                ["service.instance.id"] = Environment.MachineName,
                ["telemetry.sdk.name"]  = "Botello",
            });

    // ── Traces ──────────────────────────────────────────────────────────

    private static TracerProvider BuildTracerProvider(LoggerConfig config, ResourceBuilder resourceBuilder)
    {
        var builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddSource(ActivitySourceName);

        if (config.Exporter == ExporterType.Otlp)
            builder.AddOtlpExporter(o => ApplyOtlpOptions(config, o));
        else
            builder.AddAzureMonitorTraceExporter(o => o.ConnectionString = config.ConnectionString);

        return builder.Build()!;
    }

    // ── Logs ────────────────────────────────────────────────────────────

    private static ILoggerFactory BuildLoggerFactory(LoggerConfig config, ResourceBuilder resourceBuilder) =>
        LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(config.MinimumLevel);
            builder.AddOpenTelemetry(otel =>
            {
                otel.SetResourceBuilder(resourceBuilder);
                otel.IncludeFormattedMessage = true;
                otel.IncludeScopes = true;

                if (config.Exporter == ExporterType.Otlp)
                    otel.AddOtlpExporter(o => ApplyOtlpOptions(config, o));
                else
                    otel.AddAzureMonitorLogExporter(o => o.ConnectionString = config.ConnectionString);
            });
        });

    // ── OTLP shared configuration ──────────────────────────────────────

    private static void ApplyOtlpOptions(LoggerConfig config, OtlpExporterOptions options)
    {
        options.Protocol = config.OtlpProtocol switch
        {
            OtlpProtocolType.HttpProtobuf => OtlpExportProtocol.HttpProtobuf,
            _                             => OtlpExportProtocol.Grpc,
        };

        if (!string.IsNullOrWhiteSpace(config.OtlpEndpoint))
            options.Endpoint = new Uri(config.OtlpEndpoint);

        if (!string.IsNullOrWhiteSpace(config.OtlpHeaders))
            options.Headers = config.OtlpHeaders;

        options.TimeoutMilliseconds = config.OtlpTimeout;
    }
}
