---
name: dotnet-monitor-debug
description: >-
  This skill should be used when the user asks to "debug a .NET app",
  "collect diagnostics from a running process", "gather logs and metrics",
  "use dotnet-monitor", "capture a dump or trace", "observe a .NET process",
  "run with diagnostic sidecar", "troubleshoot a .NET process",
  "diagnose a crash", or needs to launch a .NET application with
  dotnet-monitor collecting logs, metrics, dumps, or traces.
---

# dotnet-monitor Debug Skill

Debug and observe running .NET applications using `dotnet-monitor` as a diagnostic sidecar.
The sidecar exposes a REST API for collecting logs, metrics, memory dumps, traces, and GC dumps
from a live .NET process — without restarting or redeploying the application.

## When to Use

Use this skill when a .NET process is running (or about to be launched) and can be
observed live. It is not suitable for post-mortem analysis of existing dump files —
use `dotnet-dump analyze` or the `dotnet-diag` tools directly for that.

## Prerequisites

Verify `dotnet-monitor` is installed as a global tool. Install if missing:

```bash
dotnet tool list -g | grep dotnet-monitor || dotnet tool install -g dotnet-monitor
dotnet monitor --version
```

Alternatively, run the setup script:

```powershell
pwsh .github/skills/dotnet-monitor-debug/scripts/Monitor-Setup.ps1
```

Requires .NET SDK 8.0 or later.

## Environment Setup

Set the EventSource log level to `Trace` before launching the application.
This ensures maximum diagnostic telemetry is available from the start:

```bash
export Logging__EventSource__LogLevel__Default=Trace
```

On Windows PowerShell:

```powershell
$env:Logging__EventSource__LogLevel__Default = "Trace"
```

> **Note:** Without Trace level, many internal runtime events are suppressed.
> Setting it before launch avoids missing early startup diagnostics.

## Launch with Sidecar

Start the application with `dotnet-monitor` as a sidecar using the `collect` command.
The `--` separator passes remaining arguments to the target process:

```bash
dotnet monitor collect -- dotnet run --project ./src/MyApp
```

On startup, `dotnet-monitor` prints the listening URLs. Capture the ports:

- **Collection API**: `http://localhost:52323` — dumps, traces, logs, processes
- **Metrics API**: `http://localhost:52325` — Prometheus-format metrics

To specify custom ports:

```bash
dotnet monitor collect --urls http://localhost:52323 --metricUrls http://localhost:52325 -- dotnet run
```

Alternatively, use the collect script which handles port detection and health polling:

```powershell
pwsh .github/skills/dotnet-monitor-debug/scripts/Monitor-Collect.ps1 -ProjectPath ./src/MyApp
```

## Observation Workflow

### Step 1: Discover Processes

Query the `/processes` endpoint to list monitored .NET processes.
Note the `pid` from the response — all subsequent API calls require it.

### Step 2: Poll Metrics

Check Prometheus-format metrics on the metrics port (52325).
Key indicators: GC heap size, thread pool queue length, request throughput.

### Step 3: Collect Logs

POST to `/logs` with `filterSpecs` to stream structured logs for a specified duration.
Start at `Information` level and increase to `Debug` or `Trace` if needed.

### Step 4: Summarize Findings

After collecting metrics and logs:
1. Identify error-level log entries and recurring warning patterns
2. Check GC heap trends for memory pressure
3. Check thread pool queue length for saturation
4. Report findings with specific metric values and log excerpts

> For full endpoint syntax, parameters, and response formats, see
> **`references/api-reference.md`**.

## Runtime Log Tuning

Adjust log verbosity at runtime without restarting the application.
POST a new `filterSpecs` body to the `/logs` endpoint with the desired log levels.

Use `"*": "Trace"` for maximum verbosity, or target specific namespaces
(e.g., `"Microsoft.EntityFrameworkCore": "Debug"`) while keeping others at `Warning`.

This enables granular diagnostic logging on a live process — no restart required.

> See **`references/api-reference.md`** § POST /logs for full syntax and namespace examples.

## Crash and Anomaly Collection

On anomaly or crash, collect diagnostic artifacts immediately via the collection API:

- **Memory Dump** (`/dump`) — capture process memory. Types: `Full`, `WithHeap`, `Mini`, `Triage`
- **Event Trace** (`/trace`) — capture EventPipe events for a specified duration. Open with `dotnet-trace convert` or PerfView
- **GC Dump** (`/gcdump`) — capture GC heap snapshot. Analyze with `dotnet-gcdump report`

Save artifacts to a timestamped directory under `./diagnostics/` for later analysis.
The teardown script handles artifact collection automatically.

> See **`references/api-reference.md`** for complete endpoint parameters and analysis commands.

## Teardown

Kill the monitor sidecar and clean up. Use the teardown script or manually:

```powershell
pwsh .github/skills/dotnet-monitor-debug/scripts/Monitor-Teardown.ps1
```

Manual teardown:

```powershell
Get-Process -Name 'dotnet-monitor' -ErrorAction SilentlyContinue | Stop-Process -Force
Write-Host 'dotnet-monitor sidecar stopped'
```

## Additional Resources

### Reference Files

For detailed API documentation, endpoint parameters, authentication, and troubleshooting:

- **`references/api-reference.md`** — Complete dotnet-monitor REST API reference with all endpoints, query parameters, response formats, authentication notes for 8.x+, and common troubleshooting scenarios

### Scripts

Executable helpers in `scripts/`:

- **`scripts/Monitor-Setup.ps1`** — Verify and install dotnet-monitor global tool
- **`scripts/Monitor-Collect.ps1`** — Launch sidecar, detect ports, poll until ready
- **`scripts/Monitor-Teardown.ps1`** — Kill sidecar, list collected diagnostic artifacts

### Related Skills

- [`../benchmark-designer/SKILL.md`](../benchmark-designer/SKILL.md) — Performance benchmarking with BenchmarkDotNet
- [`../experiment-evaluator/SKILL.md`](../experiment-evaluator/SKILL.md) — Comparing experimental implementations across branches
