# dotnet-monitor REST API Reference

Complete API reference for `dotnet-monitor` endpoints. Default ports:

- **Collection API**: `http://localhost:52323` — processes, dumps, traces, logs, gcdumps
- **Metrics API**: `http://localhost:52325` — Prometheus-format metrics

## Port Configuration

Override defaults with CLI flags:

```bash
dotnet monitor collect --urls http://localhost:52323 --metricUrls http://localhost:52325
```

Or via environment variables:

```bash
export DOTNETMONITOR_URLS="http://localhost:52323"
export DOTNETMONITOR_METRICS_URLS="http://localhost:52325"
```

## Endpoints

### GET /processes

List all monitored .NET processes.

**Port**: 52323

```bash
curl -s http://localhost:52323/processes | jq '.'
```

**Response** (JSON array):

```json
[
  {
    "pid": 12345,
    "uid": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
    "name": "MyApp",
    "isDefault": true
  }
]
```

**Fields**:
- `pid` — Process ID (use in subsequent API calls)
- `uid` — Unique runtime instance identifier
- `name` — Process name
- `isDefault` — Whether this is the default process (when only one is monitored)

---

### GET /metrics

Retrieve Prometheus-format metrics from the monitored process.

**Port**: 52325 (separate from collection API)

```bash
curl -s http://localhost:52325/metrics
```

**Response** (text/plain, Prometheus format):

```
# HELP dotnet_gc_heap_size_bytes GC Heap Size
# TYPE dotnet_gc_heap_size_bytes gauge
dotnet_gc_heap_size_bytes{gc_generation="0"} 1048576
dotnet_gc_heap_size_bytes{gc_generation="1"} 2097152
dotnet_gc_heap_size_bytes{gc_generation="2"} 4194304

# HELP dotnet_threadpool_queue_length Thread Pool Queue Length
# TYPE dotnet_threadpool_queue_length gauge
dotnet_threadpool_queue_length 0

# HELP dotnet_requests_per_second HTTP Request Rate
# TYPE dotnet_requests_per_second gauge
dotnet_requests_per_second 42.5
```

**Key metrics to monitor**:

| Metric | Concern Threshold | Indicates |
|--------|------------------|-----------|
| `dotnet_gc_heap_size_bytes` | Growing over time | Memory leak |
| `dotnet_threadpool_queue_length` | > 0 sustained | Thread pool saturation |
| `dotnet_gc_collection_count` | Frequent Gen2 | GC pressure |
| `dotnet_requests_per_second` | Dropping | Performance degradation |
| `dotnet_exceptions_count` | Increasing | Runtime errors |

---

### POST /logs

Stream structured logs from the process.

**Port**: 52323

**Query Parameters**:
- `pid` (required) — Target process ID
- `durationSeconds` (required) — How long to collect logs

**Request Body** (JSON):

```json
{
  "filterSpecs": {
    "*": "Information"
  }
}
```

**Filter levels** (ordered): `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`

**Example — collect all logs at Information level for 30 seconds**:

```bash
curl -s -X POST "http://localhost:52323/logs?pid=12345&durationSeconds=30" \
  -H "Content-Type: application/json" \
  -d '{"filterSpecs": {"*": "Information"}}' \
  -o logs.txt
```

**Example — target specific namespaces**:

```bash
curl -s -X POST "http://localhost:52323/logs?pid=12345&durationSeconds=60" \
  -H "Content-Type: application/json" \
  -d '{
    "filterSpecs": {
      "Microsoft.EntityFrameworkCore": "Debug",
      "Microsoft.AspNetCore.Hosting": "Information",
      "*": "Warning"
    }
  }' \
  -o logs.txt
```

**Response**: NDJSON (newline-delimited JSON) stream of log entries:

```json
{"Timestamp":"2024-01-15T10:30:00Z","LogLevel":"Information","Category":"Microsoft.Hosting.Lifetime","Message":"Application started."}
{"Timestamp":"2024-01-15T10:30:01Z","LogLevel":"Warning","Category":"MyApp.Services","Message":"Cache miss for key: user-123"}
```

---

### GET /dump

Collect a process memory dump.

**Port**: 52323

**Query Parameters**:
- `pid` (required) — Target process ID
- `type` (optional) — Dump type (default: `WithHeap`)

**Dump Types**:

| Type | Size | Contains | Use Case |
|------|------|----------|----------|
| `Full` | Large | Everything | Complete analysis |
| `WithHeap` | Medium | Managed heap + stacks | Memory leak investigation |
| `Mini` | Small | Stacks only | Quick crash triage |
| `Triage` | Minimal | Summary info | Initial assessment |

**Example**:

```bash
curl -s -o dump.dmp "http://localhost:52323/dump?pid=12345&type=Full"
```

**Analyze with**:

```bash
dotnet-dump analyze dump.dmp
# Inside the analyzer:
> dumpheap -stat
> gcroot <address>
```

---

### GET /trace

Collect an EventPipe trace.

**Port**: 52323

**Query Parameters**:
- `pid` (required) — Target process ID
- `durationSeconds` (required) — Collection duration
- `profile` (optional) — Predefined profile: `Cpu`, `Http`, `Logs`, `Metrics`

**Example — 30-second CPU trace**:

```bash
curl -s -o trace.nettrace "http://localhost:52323/trace?pid=12345&durationSeconds=30&profile=Cpu"
```

**Convert for analysis**:

```bash
dotnet-trace convert trace.nettrace --format Speedscope
# Open in https://speedscope.app
```

**Profiles**:

| Profile | Events Captured | Use Case |
|---------|----------------|----------|
| `Cpu` | CPU sampling | Performance hotspots |
| `Http` | HTTP request/response | Latency analysis |
| `Logs` | Structured logging | Log-level debugging |
| `Metrics` | Event counters | Resource monitoring |

---

### GET /gcdump

Collect a GC heap dump.

**Port**: 52323

**Query Parameters**:
- `pid` (required) — Target process ID

**Example**:

```bash
curl -s -o gc.gcdump "http://localhost:52323/gcdump?pid=12345"
```

**Analyze with**:

```bash
dotnet-gcdump report gc.gcdump
```

Shows object type counts, sizes, and retention paths — useful for identifying memory leaks
without the overhead of a full process dump.

---

## Authentication (dotnet-monitor 8.x+)

Starting with .NET 8, `dotnet-monitor` requires authentication for collection endpoints
by default. The metrics endpoint (`/metrics`) remains unauthenticated for Prometheus scraping.

### Development (API Key)

Generate an API key:

```bash
dotnet monitor generatekey
```

This outputs a key and its hash. Configure:

```json
{
  "Authentication": {
    "MonitorApiKey": {
      "Subject": "localhost",
      "PublicKey": "<generated-key-hash>"
    }
  }
}
```

Pass the key in requests:

```bash
curl -s -H "Authorization: Bearer <API_KEY>" http://localhost:52323/processes
```

### Development (Disable Auth)

For local debugging, disable authentication:

```bash
dotnet monitor collect --no-auth -- dotnet run
```

> **Warning**: Never disable authentication in production or exposed environments.

---

## Troubleshooting

### Port Already in Use

```
System.IO.IOException: Failed to bind to address http://localhost:52323
```

**Fix**: Specify alternate ports:

```bash
dotnet monitor collect --urls http://localhost:52400 --metricUrls http://localhost:52401 -- dotnet run
```

### No Processes Found

`GET /processes` returns empty array.

**Causes**:
1. Target app hasn't started yet — wait and retry
2. Diagnostic pipe not established — ensure the app runs on .NET 6+
3. On Linux, check `/tmp` for `dotnet-diagnostic-*` socket files

**Fix**: Poll with retry:

```bash
for i in $(seq 1 10); do
  PROCS=$(curl -s http://localhost:52323/processes)
  if [ "$(echo "$PROCS" | jq length)" -gt 0 ]; then
    echo "$PROCS" | jq '.'
    break
  fi
  sleep 2
done
```

### Authentication Required (8.x+)

```
HTTP 401 Unauthorized
```

**Fix**: Either use `--no-auth` for local debugging or generate an API key (see Authentication section above).

### Windows Named Pipes

On Windows, dotnet-monitor connects via named pipes (`\\.\pipe\dotnet-diagnostic-<pid>`).
Ensure the user running dotnet-monitor has permissions to access the pipe.

### Large Dump Files

Full dumps can be very large (hundreds of MB to GB).

**Mitigations**:
- Use `Triage` or `Mini` dump types for initial assessment
- Use `WithHeap` only when investigating managed memory
- Reserve `Full` for deep debugging requiring native + managed state
- Ensure sufficient disk space before collecting

### Process Exits During Collection

If the target process exits while collecting a trace or dump:
- Partial artifacts may be saved — check the output file
- Logs and metrics collected before exit remain valid
- A dump collected at crash time captures the crash state

---

## Configuration via settings.json

For complex scenarios, use a `settings.json` configuration file:

```json
{
  "Urls": "http://localhost:52323",
  "MetricsUrls": "http://localhost:52325",
  "DiagnosticPort": {
    "ConnectionMode": "Connect"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.Diagnostics.Monitoring": "Information"
    }
  }
}
```

Pass to dotnet-monitor:

```bash
dotnet monitor collect --configuration-file-path ./settings.json -- dotnet run
```

### Listen Mode (Kubernetes/Sidecar)

For container sidecar deployments where the monitor must start before the app:

```json
{
  "DiagnosticPort": {
    "ConnectionMode": "Listen",
    "EndpointName": "/diag/port.sock"
  }
}
```

Set on the target app:

```bash
export DOTNET_DiagnosticPorts="/diag/port.sock,suspend"
```

This suspends the app at startup until dotnet-monitor connects, ensuring no early
diagnostics are missed.
