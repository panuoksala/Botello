# Botello

An MSBuild custom logger that captures build events and forwards them to **Azure Application Insights** as distributed traces and structured logs via OpenTelemetry.

Drop `Botello.dll` onto any `dotnet build` or `msbuild` command and get full build observability in App Insights — no code changes to your projects required.

---

## Features

- **Distributed traces** — hierarchical spans for build → project → target with pass/fail status
- **Structured logs** — errors, warnings, messages, and lifecycle events with rich `customDimensions`
- **Zero-code-change** — attach as an external logger; nothing in your project files changes
- **Flexible configuration** — `appsettings.json`, environment variables, or inline CLI parameters
- **Parallel-build safe** — span keys are composite to avoid collisions across MSBuild nodes
- **Guaranteed flush** — all in-flight telemetry is flushed to Azure Monitor before the process exits

---

## Requirements

- .NET 10 SDK or later
- An Azure Application Insights resource (connection string)

---

## Installation

Build the project to produce `Botello.dll`:

```bash
dotnet build Botello.sln -c Release
```

The output DLL and its dependencies (including `appsettings.json`) are written to:

```
bin/Release/net10.0/Botello.dll
```

Copy the entire `net10.0/` output directory to a stable location (e.g. `~/.msbuild-loggers/Botello/`). MSBuild needs the DLL and all its NuGet dependencies present in the same folder.

---

## Quick Start

Set your Application Insights connection string as an environment variable and pass the logger path to `dotnet build`:

```bash
export APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=00000000-...;IngestionEndpoint=https://..."

dotnet build MyApp.sln -logger:/path/to/Botello.dll
```

On Windows:

```powershell
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = "InstrumentationKey=00000000-...;IngestionEndpoint=https://..."

dotnet build MyApp.sln -logger:"C:\tools\Botello\Botello.dll"
```

Within seconds of the build completing you will see:

- A **`build`** span in the App Insights Transaction Search / Application Map
- Child **`project: <name>`** spans for each `.csproj` built
- Log entries for warnings, errors, and messages under the corresponding categories

---

## Usage

### Minimal — connection string from environment

```bash
dotnet build -logger:Botello.dll
```

Botello reads the connection string from either of these environment variables (checked in order):

1. `BOTELLO__CONNECTIONSTRING`
2. `APPLICATIONINSIGHTS_CONNECTION_STRING` (the standard App Insights variable)

### Inline parameters

Pass all options directly on the command line using semicolon-separated `Key=Value` pairs after the DLL path:

```bash
dotnet build MyApp.sln \
  -logger:"Botello.dll;ConnectionString=InstrumentationKey=...;ServiceName=my-app;MinimumLevel=Debug"
```

### Override a single option while keeping appsettings.json

```bash
# Enable target-level spans (off by default) without touching appsettings.json
dotnet build -logger:"Botello.dll;IncludeTargetEvents=true"
```

### Use with msbuild.exe

```bash
msbuild MyApp.sln /logger:"C:\tools\Botello\Botello.dll;ConnectionString=...;ServiceName=MyApp"
```

### Suppress noisy telemetry on large solutions

```bash
dotnet build MyApp.sln \
  -logger:"Botello.dll;IncludeMessages=false;IncludeTargetEvents=false;MinimumLevel=Warning"
```

---

## Configuration

Configuration is merged from three sources in ascending priority (last wins):

| Priority | Source | Example |
|---|---|---|
| 1 (lowest) | `appsettings.json` next to the DLL | `"Botello": { "ServiceName": "my-app" }` |
| 2 | Environment variables (`BOTELLO__*`) | `BOTELLO__SERVICENAME=my-app` |
| 3 (highest) | MSBuild `Logger.Parameters` | `-logger:"Botello.dll;ServiceName=my-app"` |

### All options

| Key | Type | Default | Env var | Description |
|---|---|---|---|---|
| `ConnectionString` | `string` | *(none)* | `BOTELLO__CONNECTIONSTRING` | **Required.** Azure Application Insights connection string. Also accepts `APPLICATIONINSIGHTS_CONNECTION_STRING` as a fallback. |
| `ServiceName` | `string` | `Botello` | `BOTELLO__SERVICENAME` | Service name shown in Application Insights. Reported as the `service.name` OpenTelemetry resource attribute. |
| `MinimumLevel` | `LogLevel` | `Information` | `BOTELLO__MINIMUMLEVEL` | Minimum log level to emit. Accepted values: `Trace` `Debug` `Information` `Warning` `Error` `Critical`. |
| `IncludeMessages` | `bool` | `true` | `BOTELLO__INCLUDEMESSAGES` | Forward `MessageRaised` events. High-importance → `Information`, Normal → `Debug`, Low → `Trace`. |
| `IncludeWarnings` | `bool` | `true` | `BOTELLO__INCLUDEWARNINGS` | Forward `WarningRaised` events at `Warning` level. |
| `IncludeErrors` | `bool` | `true` | `BOTELLO__INCLUDEERRORS` | Forward `ErrorRaised` events at `Error` level and mark the active span as failed. |
| `IncludeProjectEvents` | `bool` | `true` | `BOTELLO__INCLUDEPROJECTEVENTS` | Emit spans and `Debug` log entries for `ProjectStarted` / `ProjectFinished`. |
| `IncludeTargetEvents` | `bool` | `false` | `BOTELLO__INCLUDETARGETEVENTS` | Emit spans and `Trace` log entries for `TargetStarted` / `TargetFinished`. Can be noisy on large builds. |

Boolean options in `Logger.Parameters` accept `true`/`false`, `1`/`0`, `yes`/`no`, and `on`/`off` (all case-insensitive).

### appsettings.json reference

```json
{
  "Botello": {
    "ConnectionString": "",
    "ServiceName": "Botello",
    "MinimumLevel": "Information",
    "IncludeMessages": true,
    "IncludeWarnings": true,
    "IncludeErrors": true,
    "IncludeProjectEvents": true,
    "IncludeTargetEvents": false
  }
}
```

Place this file in the same directory as `Botello.dll`. Values here are overridden by any environment variable or CLI parameter.

---

## Telemetry Reference

### Trace hierarchy

```
build
└── project: MyApp.csproj
    └── target: Build
    └── target: Compile
└── project: MyLib.csproj
    └── target: Build
```

Span names match the pattern `build`, `project: <filename>`, and `target: <name>`. All spans use `ActivityKind.Internal` and are created from the `Botello` activity source.

Spans are marked `Ok` on success and `Error` (with a description) on failure. When a `BuildErrorEventArgs` is raised, the nearest active span is also marked as failed.

### Log categories

Logs appear in Application Insights under the following category names (visible as the logger name / `customDimensions`):

| Category | Events |
|---|---|
| `MSBuild.Build` | Build start / finish |
| `MSBuild.Project` | Project start / finish |
| `MSBuild.Target` | Target start / finish |
| `MSBuild.Message` | Diagnostic messages |
| `MSBuild.Warning` | Build warnings |
| `MSBuild.Error` | Build errors |

### MSBuild message importance mapping

| `MessageImportance` | `LogLevel` |
|---|---|
| `High` | `Information` |
| `Normal` | `Debug` |
| `Low` | `Trace` |

### Resource attributes

Every telemetry item carries these OpenTelemetry resource attributes:

| Attribute | Value |
|---|---|
| `service.name` | Value of `ServiceName` config option |
| `service.instance.id` | `Environment.MachineName` |
| `telemetry.sdk.name` | `Botello` |

---

## CI/CD Integration

### GitHub Actions

```yaml
- name: Build
  env:
    APPLICATIONINSIGHTS_CONNECTION_STRING: ${{ secrets.APPINSIGHTS_CONNECTION_STRING }}
  run: |
    dotnet build MyApp.sln \
      -logger:"${{ github.workspace }}/tools/Botello/Botello.dll;ServiceName=my-app;MinimumLevel=Warning"
```

### Azure Pipelines

```yaml
- task: DotNetCoreCLI@2
  displayName: Build
  inputs:
    command: build
    arguments: >
      MyApp.sln
      -logger:"$(Agent.ToolsDirectory)/Botello/Botello.dll;ServiceName=$(Build.DefinitionName)"
  env:
    APPLICATIONINSIGHTS_CONNECTION_STRING: $(AppInsightsConnectionString)
```

---

## How It Works

1. MSBuild loads `Botello.dll` and calls `AppInsightsLogger.Initialize()`.
2. `ConfigurationLoader` merges settings from `appsettings.json`, `BOTELLO__*` environment variables, and the `Logger.Parameters` string.
3. `OtelPipelineManager` initialises a `TracerProvider` and an `ILoggerFactory`, both pointed at Azure Monitor.
4. `AppInsightsLogger` subscribes to the requested MSBuild event sources.
5. During the build, each event creates an OTel span and/or log entry.
6. When MSBuild calls `Shutdown()`, `OtelPipelineManager.Dispose()` flushes all in-flight batches — traces first, then logs — before returning, so no telemetry is dropped.

---

## Dependencies

| Package | Version |
|---|---|
| `Microsoft.Build.Framework` | 18.3.3 |
| `Microsoft.Build.Utilities.Core` | 18.3.3 |
| `Azure.Monitor.OpenTelemetry.Exporter` | 1.6.0 |
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 |
| `Microsoft.Extensions.Configuration.Json` | 10.0.3 |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 10.0.3 |
| `Microsoft.Extensions.Logging` | 10.0.3 |

---

## License

See [LICENSE](LICENSE).
