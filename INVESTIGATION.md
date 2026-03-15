# NavigationView UIA Crash — Investigation Log

> **Issue**: anokye-labs/FlaUI-MCP#1
> **Branch**: `investigate/nav-uia-crash`
> **Repro**: `repro/NavViewUiaCrashRepro/`

## Current Status

**Phase**: Setup — diagnostics toolkit and repro project being prepared
**Next Action**: Run repro project, confirm crash, capture dump
**Blockers**: None

---

## Binary Search: Minimum Crash Configuration

| Step | Configuration | Crashes? | Notes |
|------|--------------|----------|-------|
| 1 | NavigationView, no items | | |
| 2 | One static XAML item | | |
| 3 | One programmatic item (code-behind) | | |
| 4 | Multiple programmatic items | | |
| 5 | One nested child item | | |
| 6 | InfoBadge on an item | | |

---

## Dump Analysis Findings

### Dump File
- **Path**: (not yet captured)
- **Type**: Full (`-ma`)
- **Tool**: ProcDump

### !analyze -v Output
(pending)

### FAULTING_IP
(pending)

### Full Call Stack (kb 100)
(pending)

### All Thread Stacks (~*kb)
(pending)

### Thread Ownership

| Observation | Meaning | Fix Direction |
|-------------|---------|---------------|
| UIAutomationCore.dll frames on crashing thread | UIA provider callback thread — re-entrancy | Thread-marshal UIA callbacks; guard peer methods with dispatcher check |
| Crash on UI thread, no UIAutomationCore frames | Lifecycle/teardown — peer accesses partially-destructed control | Peer lifetime guard; null-check Owner |
| Crash on background thread, no UIA frames | Race condition — data binding vs UIA enumeration | Lock or defer item mutations during UIA walks |

**Determined**: (pending)

### Null Pointer Source (kP 100)
(pending — which pointer is null: peer object, control reference, layout slot, or COM interface pointer?)

---

## Fix Strategy
(pending — to be determined after dump analysis)

---

## Session History

### Session 1 — Initial Setup
- Created fork: anokye-labs/FlaUI-MCP
- Branches: `main` (clean), `fix/crash-safe-uia` (mitigations), `investigate/nav-uia-crash` (this)
- Diagnostics toolkit installed
- Repro project created
