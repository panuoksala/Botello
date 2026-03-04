using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using Botello.Config;
using Botello.Telemetry;

// Both Microsoft.Build.Framework and Microsoft.Extensions.Logging define ILogger.
// Alias the MEL types to avoid ambiguous references.
using MelILogger = Microsoft.Extensions.Logging.ILogger;
using MelLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Botello.Logging;

/// <summary>
/// MSBuild custom logger that forwards build events to Azure Application Insights via OpenTelemetry.
/// Register with: <c>dotnet build -logger:Botello.dll</c>
/// </summary>
public sealed class AppInsightsLogger : Logger
{
    private const string BuildCategory = "MSBuild.Build";
    private const string ProjectCategory = "MSBuild.Project";
    private const string TargetCategory = "MSBuild.Target";
    private const string MessageCategory = "MSBuild.Message";
    private const string WarningCategory = "MSBuild.Warning";
    private const string ErrorCategory = "MSBuild.Error";

    private LoggerConfig? _config;
    private OtelPipelineManager? _pipeline;

    private MelILogger? _buildLogger;
    private MelILogger? _projectLogger;
    private MelILogger? _targetLogger;
    private MelILogger? _messageLogger;
    private MelILogger? _warningLogger;
    private MelILogger? _errorLogger;

    // Tracks open spans keyed by a stable identifier so lifecycle events can be correlated.
    private readonly ConcurrentDictionary<string, Activity> _activeSpans = new();
    private Activity? _buildActivity;

    /// <inheritdoc/>
    public override void Initialize(IEventSource eventSource)
    {
        var assemblyDir = Path.GetDirectoryName(typeof(AppInsightsLogger).Assembly.Location)
                          ?? AppContext.BaseDirectory;

        _config = ConfigurationLoader.Load(Parameters, assemblyDir);
        _config.Validate();

        _pipeline = new OtelPipelineManager();
        _pipeline.Initialize(_config);

        _buildLogger   = _pipeline.CreateLogger(BuildCategory);
        _projectLogger = _pipeline.CreateLogger(ProjectCategory);
        _targetLogger  = _pipeline.CreateLogger(TargetCategory);
        _messageLogger = _pipeline.CreateLogger(MessageCategory);
        _warningLogger = _pipeline.CreateLogger(WarningCategory);
        _errorLogger   = _pipeline.CreateLogger(ErrorCategory);

        eventSource.BuildStarted  += OnBuildStarted;
        eventSource.BuildFinished += OnBuildFinished;

        if (_config.IncludeErrors)
            eventSource.ErrorRaised += OnErrorRaised;

        if (_config.IncludeWarnings)
            eventSource.WarningRaised += OnWarningRaised;

        if (_config.IncludeMessages)
            eventSource.MessageRaised += OnMessageRaised;

        if (_config.IncludeProjectEvents)
        {
            eventSource.ProjectStarted  += OnProjectStarted;
            eventSource.ProjectFinished += OnProjectFinished;
        }

        if (_config.IncludeTargetEvents)
        {
            eventSource.TargetStarted  += OnTargetStarted;
            eventSource.TargetFinished += OnTargetFinished;
        }
    }

    /// <inheritdoc/>
    public override void Shutdown()
    {
        _buildActivity?.Dispose();
        _buildActivity = null;

        // Disposing the pipeline flushes all in-flight batches to Application Insights.
        _pipeline?.Dispose();
        _pipeline = null;
    }

    private void OnBuildStarted(object sender, BuildStartedEventArgs e)
    {
        _buildActivity = OtelPipelineManager.BuildActivitySource
            .StartActivity("build", ActivityKind.Internal);

        _buildLogger!.LogInformation("Build started at {Timestamp}", e.Timestamp);
    }

    private void OnBuildFinished(object sender, BuildFinishedEventArgs e)
    {
        if (_buildActivity is not null)
        {
            _buildActivity.SetStatus(
                e.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                e.Succeeded ? null : "Build failed");
            _buildActivity.Dispose();
            _buildActivity = null;
        }

        _buildLogger!.LogInformation(
            "Build finished at {Timestamp}. Succeeded: {Succeeded}",
            e.Timestamp,
            e.Succeeded);
    }

    private void OnProjectStarted(object sender, ProjectStartedEventArgs e)
    {
        var spanKey  = ProjectSpanKey(e.ProjectFile, e.BuildEventContext);
        var activity = OtelPipelineManager.BuildActivitySource
            .StartActivity(
                $"project: {Path.GetFileName(e.ProjectFile)}",
                ActivityKind.Internal,
                parentContext: _buildActivity?.Context ?? default);

        if (activity is not null)
        {
            activity.SetTag("project.file",    e.ProjectFile);
            activity.SetTag("project.targets", e.TargetNames);
            _activeSpans[spanKey] = activity;
        }

        using (_projectLogger!.BeginScope(new Dictionary<string, object?>
        {
            ["ProjectFile"]    = e.ProjectFile,
            ["TargetNames"]    = e.TargetNames,
            ["BuildContextId"] = e.BuildEventContext?.ProjectInstanceId,
        }))
        {
            _projectLogger.LogDebug(
                "Project started: {ProjectFile} [{TargetNames}]",
                e.ProjectFile,
                e.TargetNames);
        }
    }

    private void OnProjectFinished(object sender, ProjectFinishedEventArgs e)
    {
        var spanKey = ProjectSpanKey(e.ProjectFile, e.BuildEventContext);

        if (_activeSpans.TryRemove(spanKey, out var activity))
        {
            activity.SetStatus(
                e.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                e.Succeeded ? null : $"Project failed: {e.ProjectFile}");
            activity.Dispose();
        }

        using (_projectLogger!.BeginScope(new Dictionary<string, object?>
        {
            ["ProjectFile"]    = e.ProjectFile,
            ["BuildContextId"] = e.BuildEventContext?.ProjectInstanceId,
        }))
        {
            _projectLogger.LogDebug(
                "Project finished: {ProjectFile}. Succeeded: {Succeeded}",
                e.ProjectFile,
                e.Succeeded);
        }
    }

    private void OnTargetStarted(object sender, TargetStartedEventArgs e)
    {
        var spanKey        = TargetSpanKey(e.TargetName, e.BuildEventContext);
        var projectSpanKey = ProjectSpanKey(e.ProjectFile, e.BuildEventContext);
        _activeSpans.TryGetValue(projectSpanKey, out var parentActivity);

        var activity = OtelPipelineManager.BuildActivitySource
            .StartActivity(
                $"target: {e.TargetName}",
                ActivityKind.Internal,
                parentContext: parentActivity?.Context
                               ?? _buildActivity?.Context
                               ?? default);

        if (activity is not null)
        {
            activity.SetTag("target.name",   e.TargetName);
            activity.SetTag("project.file",  e.ProjectFile);
            activity.SetTag("parent.target", e.ParentTarget);
            _activeSpans[spanKey] = activity;
        }

        _targetLogger!.LogTrace(
            "Target started: {TargetName} in {ProjectFile}",
            e.TargetName,
            e.ProjectFile);
    }

    private void OnTargetFinished(object sender, TargetFinishedEventArgs e)
    {
        var spanKey = TargetSpanKey(e.TargetName, e.BuildEventContext);

        if (_activeSpans.TryRemove(spanKey, out var activity))
        {
            activity.SetStatus(
                e.Succeeded ? ActivityStatusCode.Ok : ActivityStatusCode.Error,
                e.Succeeded ? null : $"Target failed: {e.TargetName}");
            activity.Dispose();
        }

        _targetLogger!.LogTrace(
            "Target finished: {TargetName} in {ProjectFile}. Succeeded: {Succeeded}",
            e.TargetName,
            e.ProjectFile,
            e.Succeeded);
    }

    private void OnErrorRaised(object sender, BuildErrorEventArgs e)
    {
        using (_errorLogger!.BeginScope(BuildErrorScope(e.Code, e.File, e.LineNumber, e.ColumnNumber, e.ProjectFile)))
        {
            _errorLogger.LogError(
                "MSBuild error {Code}: {Message}  [{File}({Line},{Column})]",
                e.Code, e.Message, e.File, e.LineNumber, e.ColumnNumber);
        }

        MarkCurrentSpanAsError($"MSBuild error {e.Code}: {e.Message}", e.BuildEventContext);
    }

    private void OnWarningRaised(object sender, BuildWarningEventArgs e)
    {
        using (_warningLogger!.BeginScope(BuildWarningScope(e.Code, e.File, e.LineNumber, e.ColumnNumber, e.ProjectFile)))
        {
            _warningLogger.LogWarning(
                "MSBuild warning {Code}: {Message}  [{File}({Line},{Column})]",
                e.Code, e.Message, e.File, e.LineNumber, e.ColumnNumber);
        }
    }

    private void OnMessageRaised(object sender, BuildMessageEventArgs e)
    {
        var level = MapImportanceToLogLevel(e.Importance);

        if (!_messageLogger!.IsEnabled(level))
            return;

        using (_messageLogger.BeginScope(new Dictionary<string, object?>
        {
            ["ProjectFile"] = e.ProjectFile,
            ["Importance"]  = e.Importance.ToString(),
        }))
        {
            _messageLogger.Log(level, 0, e.Message, null, (s, _) => s ?? string.Empty);
        }
    }

    private static MelLogLevel MapImportanceToLogLevel(MessageImportance importance) =>
        importance switch
        {
            MessageImportance.High   => MelLogLevel.Information,
            MessageImportance.Normal => MelLogLevel.Debug,
            MessageImportance.Low    => MelLogLevel.Trace,
            _                        => MelLogLevel.Debug,
        };

    private void MarkCurrentSpanAsError(string description, BuildEventContext? ctx)
    {
        if (ctx is null) return;

        foreach (var key in _activeSpans.Keys)
        {
            if (_activeSpans.TryGetValue(key, out var a))
            {
                a.SetStatus(ActivityStatusCode.Error, description);
                return;
            }
        }
    }

    // Span keys are composite to avoid collisions during parallel builds on multiple nodes.
    private static string ProjectSpanKey(string? projectFile, BuildEventContext? ctx) =>
        $"project|{projectFile}|{ctx?.ProjectInstanceId}|{ctx?.NodeId}";

    private static string TargetSpanKey(string? targetName, BuildEventContext? ctx) =>
        $"target|{targetName}|{ctx?.ProjectInstanceId}|{ctx?.TargetId}|{ctx?.NodeId}";

    private static Dictionary<string, object?> BuildErrorScope(
        string? code, string? file, int line, int col, string? projectFile) =>
        new()
        {
            ["ErrorCode"]   = code,
            ["File"]        = file,
            ["Line"]        = line,
            ["Column"]      = col,
            ["ProjectFile"] = projectFile,
        };

    private static Dictionary<string, object?> BuildWarningScope(
        string? code, string? file, int line, int col, string? projectFile) =>
        new()
        {
            ["WarningCode"] = code,
            ["File"]        = file,
            ["Line"]        = line,
            ["Column"]      = col,
            ["ProjectFile"] = projectFile,
        };
}
