[![.NET](https://github.com/panuoksala/Botello/actions/workflows/dotnet.yml/badge.svg)](https://github.com/panuoksala/Botello/actions/workflows/dotnet.yml)

# Botello📝

A custom logger that captures build events and forwards them to any OpenTelemetry-compatible backend — **Azure Application Insights**, **Jaeger**, **Grafana Tempo**, **Seq**, **.NET Aspire Dashboard**, or any **OTLP collector** — as distributed traces and structured logs.

Drop `Botello.dll` onto any `dotnet build` or `msbuild` command and get full build observability — no code changes to your projects required.

---

## Features⭐

- **Distributed traces** — hierarchical spans for build → project → target with pass/fail status
- **Structured logs** — errors, warnings, messages, and lifecycle events with rich `customDimensions`
- **Two exporters** — Azure Application Insights or any OTLP-compatible backend (gRPC / HTTP)
- **Zero-code-change** — attach as an external logger; nothing in your project files changes
- **Flexible configuration** — `appsettings.json`, environment variables, or inline CLI parameters
- **Parallel-build safe** — span keys are composite to avoid collisions across MSBuild nodes
- **Guaranteed flush** — all in-flight telemetry is flushed before the process exits

---

## Requirements🔗

- .NET 10 SDK or later
- One of the following:
  - An Azure Application Insights resource (connection string), **or**
  - An OTLP-compatible collector endpoint (e.g. Jaeger, Grafana Tempo, Seq, Aspire Dashboard, OTel Collector)

---

## Installation💻

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

## Quick Start🚀

### Azure Application Insights

Set your connection string as an environment variable and pass the logger path to `dotnet build`:

```bash
export APPLICATIONINSIGHTS_CONNECTION_STRING="InstrumentationKey=00000000-...;IngestionEndpoint=https://..."

dotnet build MyApp.sln -logger:/path/to/Botello.dll
```

On Windows:

```powershell
$env:APPLICATIONINSIGHTS_CONNECTION_STRING = "InstrumentationKey=00000000-...;IngestionEndpoint=https://..."

dotnet build MyApp.sln -logger:"C:\tools\Botello\Botello.dll"
```

### OTLP (Jaeger, Grafana, Aspire Dashboard, etc.)

Point the logger at any OTLP collector — no Azure account needed:

```bash
dotnet build MyApp.sln \
  -logger:"Botello.dll;Exporter=Otlp;OtlpEndpoint=http://localhost:4317"
```

Within seconds of the build completing you will see:

- A **`build`** span in your tracing UI
- Child **`project: <name>`** spans for each `.csproj` built
- Log entries for warnings, errors, and messages under the corresponding categories

---

## Usage📝

### Azure Monitor — connection string from environment

```bash
dotnet build -logger:Botello.dll
```

Botello reads the connection string from either of these environment variables (checked in order):

1. `BOTELLO__CONNECTIONSTRING`
2. `APPLICATIONINSIGHTS_CONNECTION_STRING` (the standard App Insights variable)

### Azure Monitor — inline parameters

```bash
dotnet build MyApp.sln \
  -logger:"Botello.dll;ConnectionString=InstrumentationKey=...;ServiceName=my-app;MinimumLevel=Debug"
```

### OTLP — gRPC (default protocol)

```bash
# Jaeger with OTLP gRPC on default port 4317
dotnet build MyApp.sln \
  -logger:"Botello.dll;Exporter=Otlp;OtlpEndpoint=http://localhost:4317;ServiceName=my-app"
```

### OTLP — HTTP/protobuf

```bash
# Grafana Tempo or OTel Collector with HTTP/protobuf on default port 4318
dotnet build MyApp.sln \
  -logger:"Botello.dll;Exporter=Otlp;OtlpProtocol=HttpProtobuf;OtlpEndpoint=http://localhost:4318"
```

### OTLP — with authentication headers

```bash
# Grafana Cloud, Honeycomb, or any backend requiring auth headers
dotnet build MyApp.sln \
  -logger:"Botello.dll;Exporter=Otlp;OtlpEndpoint=https://otlp.example.com:4317;OtlpHeaders=Authorization=Bearer mytoken123"
```

### OTLP — .NET Aspire Dashboard

```bash
# The Aspire Dashboard listens on OTLP gRPC port 4317 by default
docker run -d -p 18888:18888 -p 4317:18889 mcr.microsoft.com/dotnet/aspire-dashboard:latest

dotnet build MyApp.sln \
  -logger:"Botello.dll;Exporter=Otlp;OtlpEndpoint=http://localhost:4317;ServiceName=my-app"
```

Then open `http://localhost:18888` to see your build traces and logs.

### OTLP — localhost defaults

When using OTLP with no endpoint specified, Botello uses the OTel SDK defaults:
- gRPC → `http://localhost:4317`
- HTTP/protobuf → `http://localhost:4318`

```bash
# If your collector is on default ports, just set the exporter
dotnet build -logger:"Botello.dll;Exporter=Otlp"
```

### OTLP — via environment variables

```bash
export BOTELLO__EXPORTER=Otlp
export BOTELLO__OTLPENDPOINT=http://localhost:4317
export BOTELLO__SERVICENAME=my-app

dotnet build MyApp.sln -logger:Botello.dll
```

### Use with msbuild.exe

```bash
msbuild MyApp.sln /logger:"C:\tools\Botello\Botello.dll;Exporter=Otlp;OtlpEndpoint=http://localhost:4317"
```

### Override a single option while keeping appsettings.json

```bash
# Enable target-level spans (off by default) without touching appsettings.json
dotnet build -logger:"Botello.dll;IncludeTargetEvents=true"
```

### Suppress noisy telemetry on large solutions

```bash
dotnet build MyApp.sln \
  -logger:"Botello.dll;IncludeMessages=false;IncludeTargetEvents=false;MinimumLevel=Warning"
```

---

## Configuration🛠️

Configuration is merged from three sources in ascending priority (last wins):

| Priority | Source | Example |
|---|---|---|
| 1 (lowest) | `appsettings.json` next to the DLL | `"Botello": { "Exporter": "Otlp" }` |
| 2 | Environment variables (`BOTELLO__*`) | `BOTELLO__EXPORTER=Otlp` |
| 3 (highest) | MSBuild `Logger.Parameters` | `-logger:"Botello.dll;Exporter=Otlp"` |

### All options

#### Exporter selection

| Key | Type | Default | Env var | Description |
|---|---|---|---|---|
| `Exporter` | `ExporterType` | `AzureMonitor` | `BOTELLO__EXPORTER` | Selects the telemetry back-end. Values: `AzureMonitor`, `Otlp`. |

#### Azure Monitor options (used when Exporter = AzureMonitor)

| Key | Type | Default | Env var | Description |
|---|---|---|---|---|
| `ConnectionString` | `string` | *(none)* | `BOTELLO__CONNECTIONSTRING` | **Required.** App Insights connection string. Also accepts `APPLICATIONINSIGHTS_CONNECTION_STRING` as a fallback. |

#### OTLP options (used when Exporter = Otlp)

| Key | Type | Default | Env var | Description |
|---|---|---|---|---|
| `OtlpEndpoint` | `string` | *(SDK default)* | `BOTELLO__OTLPENDPOINT` | Collector endpoint URL. Defaults to `http://localhost:4317` (gRPC) or `http://localhost:4318` (HTTP/protobuf). |
| `OtlpProtocol` | `OtlpProtocolType` | `Grpc` | `BOTELLO__OTLPPROTOCOL` | Transport protocol. Values: `Grpc`, `HttpProtobuf`. |
| `OtlpHeaders` | `string` | *(none)* | `BOTELLO__OTLPHEADERS` | Comma-separated `key=value` headers sent with every export request. |
| `OtlpTimeout` | `int` | `10000` | `BOTELLO__OTLPTIMEOUT` | Export request timeout in milliseconds. |

#### Common options

| Key | Type | Default | Env var | Description |
|---|---|---|---|---|
| `ServiceName` | `string` | `Botello` | `BOTELLO__SERVICENAME` | Service name reported as the `service.name` OTel resource attribute. |
| `MinimumLevel` | `LogLevel` | `Information` | `BOTELLO__MINIMUMLEVEL` | Minimum log level to emit. Values: `Trace` `Debug` `Information` `Warning` `Error` `Critical`. |
| `IncludeMessages` | `bool` | `true` | `BOTELLO__INCLUDEMESSAGES` | Forward `MessageRaised` events. High → `Information`, Normal → `Debug`, Low → `Trace`. |
| `IncludeWarnings` | `bool` | `true` | `BOTELLO__INCLUDEWARNINGS` | Forward `WarningRaised` events at `Warning` level. |
| `IncludeErrors` | `bool` | `true` | `BOTELLO__INCLUDEERRORS` | Forward `ErrorRaised` events at `Error` level and mark the active span as failed. |
| `IncludeProjectEvents` | `bool` | `true` | `BOTELLO__INCLUDEPROJECTEVENTS` | Emit spans and `Debug` log entries for `ProjectStarted` / `ProjectFinished`. |
| `IncludeTargetEvents` | `bool` | `false` | `BOTELLO__INCLUDETARGETEVENTS` | Emit spans and `Trace` log entries for `TargetStarted` / `TargetFinished`. Can be noisy on large builds. |

Boolean options in `Logger.Parameters` accept `true`/`false`, `1`/`0`, `yes`/`no`, and `on`/`off` (all case-insensitive).

### appsettings.json reference

#### Azure Monitor

```json
{
  "Botello": {
    "Exporter": "AzureMonitor",
    "ConnectionString": "InstrumentationKey=...",
    "ServiceName": "my-app",
    "MinimumLevel": "Information",
    "IncludeMessages": true,
    "IncludeWarnings": true,
    "IncludeErrors": true,
    "IncludeProjectEvents": true,
    "IncludeTargetEvents": false
  }
}
```

#### OTLP

```json
{
  "Botello": {
    "Exporter": "Otlp",
    "OtlpEndpoint": "http://localhost:4317",
    "OtlpProtocol": "Grpc",
    "OtlpHeaders": "",
    "OtlpTimeout": 10000,
    "ServiceName": "my-app",
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

Logs appear under the following category names (visible as the logger name / `customDimensions`):

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

### GitHub Actions — Azure Monitor

```yaml
- name: Build
  env:
    APPLICATIONINSIGHTS_CONNECTION_STRING: ${{ secrets.APPINSIGHTS_CONNECTION_STRING }}
  run: |
    dotnet build MyApp.sln \
      -logger:"${{ github.workspace }}/tools/Botello/Botello.dll;ServiceName=my-app;MinimumLevel=Warning"
```

### GitHub Actions — OTLP

```yaml
- name: Build
  env:
    BOTELLO__EXPORTER: Otlp
    BOTELLO__OTLPENDPOINT: ${{ secrets.OTLP_ENDPOINT }}
    BOTELLO__OTLPHEADERS: "Authorization=Bearer ${{ secrets.OTLP_TOKEN }}"
    BOTELLO__SERVICENAME: my-app
  run: |
    dotnet build MyApp.sln \
      -logger:"${{ github.workspace }}/tools/Botello/Botello.dll"
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
3. `OtelPipelineManager` initialises a `TracerProvider` and an `ILoggerFactory`, routing to either Azure Monitor or an OTLP collector based on the `Exporter` setting.
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
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.0 |
| `OpenTelemetry.Extensions.Hosting` | 1.15.0 |
| `Microsoft.Extensions.Configuration.Json` | 10.0.3 |
| `Microsoft.Extensions.Configuration.EnvironmentVariables` | 10.0.3 |
| `Microsoft.Extensions.Logging` | 10.0.3 |

---

## License

See [LICENSE](LICENSE).
