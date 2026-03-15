# NavigationView UIA Crash — Investigation Log

> **Issue**: anokye-labs/FlaUI-MCP#1
> **Branch**: `investigate/nav-uia-crash`
> **Repro**: `repro/NavViewUiaCrashRepro/`

## Current Status

**Phase**: Binary search complete — minimal repro does NOT crash
**Next Action**: Compare Amplifier's exact NavigationView setup (SDK version, item binding pattern, data templates) to identify the specific trigger
**Blockers**: The self-contained repro with items + nested children + InfoBadge does not crash. The bug may require: (a) a specific older WindowsAppSDK version, (b) data-bound items vs static XAML items, (c) custom DataTemplates, or (d) external UIA client rather than self-triggered walk.

### Key Findings
1. **Stowed exceptions (`c000027b`) bypass ProcDump's SEH hooks** — use `-t` (terminate monitor) not `-e 1`
2. **Initial startup crash** was a project setup issue (missing `XamlControlsResources`, `WindowsPackageType`, wrong SDK) — not the UIA bug
3. **Empty NavigationView**: no crash on self-triggered UIA walk
4. **Items + nested children + InfoBadge**: no crash on self-triggered UIA walk (WindowsAppSDK 1.8)
5. The UIA crash may be specific to: an older WindowsAppSDK version, data-bound items, external UIA client, or the Amplifier app's specific control composition

---

## Binary Search: Minimum Crash Configuration

| Step | Configuration | Crashes? | Notes |
|------|--------------|----------|-------|
| 1 | NavigationView, no items | NO | Empty NavView, self-triggered UIA walk completes fine |
| 2 | Static XAML items (3 items) | NO | Home, Browse, Settings — UIA walk completes |
| 3 | Nested child items | NO | Browse has 2 sub-items — UIA walk completes |
| 4 | Multiple programmatic items | SKIPPED | Covered by static XAML test |
| 5 | Nested child items | NO | Included in step 3 |
| 6 | InfoBadge on an item | NO | InfoBadge Value=3 on Browse item — UIA walk completes (WinAppSDK 1.8) |

---

## Dump Analysis Findings

### Dump File
- **Path**: `C:\dumps\NavViewUiaCrashRepro.exe_260315_140209.dmp`
- **Size**: 332 MB
- **Type**: Full (`-ma`)
- **Tool**: ProcDump `-t` (terminate monitor) — needed because stowed exceptions bypass SEH
- **Note**: ProcDump `-e 1 -f "C0000005"` does NOT capture this crash. The exception is `c000027b` (STATUS_STOWED_EXCEPTION) raised via `RoFailFastWithErrorContext`, not a regular SEH access violation.

### !analyze -v Output
```
FAILURE_BUCKET: STOWED_EXCEPTION_800f1000_Microsoft.UI.Xaml.dll!DirectUI::ErrorHelper::OriginateError
EXCEPTION_CODE: 800f1000 (stowed), c000027b (fail-fast wrapper)
CLR.Version: 8.0.2526.11203
IMAGE_VERSION: 3.1.6.0 (Microsoft.UI.Xaml.dll — Windows App SDK 1.6)
FAULTING_SOURCE: C:\__w\1\s\dxaml\xcp\dxaml\lib\ErrorHelper.cpp line 647
PROCESS_UPTIME: 1 second (crashed during startup)
```

### FAULTING_IP
```
Microsoft_UI_Xaml!DirectUI::ErrorHelper::OriginateError+0xf4
```
Source: `ErrorHelper.cpp:647` in the WinUI 3 XAML runtime.

### Full Call Stack (kP 100)
```
KERNELBASE!RaiseFailFastException+0x188
combase!RoFailFastWithErrorContextInternal2
Microsoft_UI_Xaml!FailFastWithStowedExceptions
Microsoft_UI_Xaml!DirectUI::DefaultStyles::GetDefaultStyleByTypeName    ← CRASH ORIGIN
Microsoft_UI_Xaml!DirectUI::DefaultStyles::GetDefaultStyleByKey
Microsoft_UI_Xaml!FxCallbacks::Control_GetBuiltInStyle
Microsoft_UI_Xaml!CControl::GetBuiltInStyle
Microsoft_UI_Xaml!CControl::ApplyBuiltInStyle
Microsoft_UI_Xaml!CControl::CreationComplete
Microsoft_UI_Xaml!XamlManagedRuntime::InitializationGuard
Microsoft_UI_Xaml!ObjectWriterRuntime::SetGuardImplHelper
Microsoft_UI_Xaml!ObjectWriterRuntime::EndInitImpl
Microsoft_UI_Xaml!BinaryFormatObjectWriter::EndInitOnCurrentInstance
Microsoft_UI_Xaml!BinaryFormatObjectWriter::WriteNode
Microsoft_UI_Xaml!CParser::LoadXamlCore                                ← XAML PARSING
Microsoft_UI_Xaml!CCoreServices::ParseXamlWithExistingFrameworkRoot
Microsoft_UI_Xaml!CApplication::LoadComponent                          ← APP STARTUP
Microsoft_UI_Xaml!DirectUI::FrameworkApplication::StartDesktop
coreclr!RunMain
```

### All Thread Stacks (~*kb)
- **Thread 0 (UI/main)**: Crash thread — the full stack above
- **Threads 1-9**: .NET runtime threads (EventPipe, Debugger, Finalizer, Tiered Compilation) — all idle/waiting
- **No UIAutomationCore.dll frames** on any thread — UIA never ran

### Thread Ownership

| Observation | Meaning | Fix Direction |
|-------------|---------|---------------|
| UIAutomationCore.dll frames on crashing thread | UIA provider callback thread — re-entrancy | Thread-marshal UIA callbacks; guard peer methods with dispatcher check |
| Crash on UI thread, no UIAutomationCore frames | Lifecycle/teardown — peer accesses partially-destructed control | Peer lifetime guard; null-check Owner |
| Crash on background thread, no UIA frames | Race condition — data binding vs UIA enumeration | Lock or defer item mutations during UIA walks |

**Determined**: Crash is on **UI thread (thread 0)** during XAML startup. No UIAutomationCore frames anywhere. This is a **style resolution failure**, not a UIA crash.

### Root Cause (This Dump)
**`GetDefaultStyleByTypeName` fails** → `800f1000` → The NavigationView control cannot find its built-in WinUI 3 style resources during XAML instantiation. This is likely because the repro project is missing the WinUI 3 resource dictionary initialization (the `<XamlControlsResources>` in App.xaml).

### What This Means for the UIA Investigation
This startup crash must be fixed FIRST before we can test the actual UIA enumeration crash. The repro app needs:
1. `<XamlControlsResources>` added to `App.xaml` `<Application.Resources>`
2. Possibly a `microsoft-ui-xaml` package reference or `<ItemGroup>` for WinUI resources

---

## Fix Strategy

### Immediate: Fix Repro Startup Crash
The repro project is missing `<XamlControlsResources>` in `App.xaml`, causing NavigationView style lookup to fail at startup. Fix this, then re-run to test the actual UIA enumeration crash.

### Pending: UIA Investigation
Once the repro runs successfully, proceed with the binary search table to find the minimum NavigationView configuration that triggers the UIA crash.

---

## Session History

### Session 1 — Initial Setup
- Created fork: anokye-labs/FlaUI-MCP
- Branches: `main` (clean), `fix/crash-safe-uia` (mitigations), `investigate/nav-uia-crash` (this)
- Diagnostics toolkit installed
- Repro project created

### Session 2 — Dump Analysis & Binary Search
- Installed diagnostic tools via `Setup-DiagTools.ps1`
- **Key discovery**: ProcDump `-e 1 -f "C0000005"` does NOT capture WinUI stowed exceptions — use `-t` instead
- **First dump** (339 MB): Startup crash — `GetDefaultStyleByTypeName` fails because `<XamlControlsResources>` was missing from App.xaml + project was missing `WindowsPackageType=None` + wrong SDK
- Fixed project: retargeted to net10.0 + WindowsAppSDK 1.8 + XamlControlsResources + WindowsPackageType=None + global.json pinning SDK to 10.0.201
- **Binary search**: All 6 configurations (empty → items → nested → InfoBadge) pass without UIA crash
- **Conclusion**: Minimal repro does NOT trigger the UIA crash. The bug likely requires the Amplifier app's specific setup (data binding, templates, older SDK version, or external UIA client)
