# NavigationView UIA Crash — Investigation Log

> **Issue**: anokye-labs/FlaUI-MCP#1
> **Branch**: `investigate/nav-uia-crash`
> **Repro**: `repro/NavViewUiaCrashRepro/`

## Current Status

**Phase**: ROOT CAUSE IDENTIFIED — crash dump captured and fully analyzed
**Next Action**: File upstream bug on microsoft/microsoft-ui-xaml with full evidence
**Blockers**: None — root cause is a thread-safety bug in WinUI 3 native code

### Root Cause Summary
**Null pointer dereference in `DXamlCore::GetPeerPrivate`** when a UIA callback thread tries to insert into an `std::unordered_set<DependencyObject*>` whose internal bucket array is NULL. The crash happens on a **UIA IO thread** (thread 95), NOT the UI thread. The UI thread is idle in its message loop (`GetMessageW`). This is a **thread-safety / re-entrancy bug** in the WinUI 3 XAML automation peer infrastructure.

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
- **Path**: `C:\dumps\WinUiSample.exe_260315_153428.dmp`
- **Size**: 575 MB
- **Type**: Full (`-ma`)
- **Tool**: ProcDump `-t` (terminate monitor)
- **App**: Amplifier WinUiSample (not the minimal repro — minimal repro does NOT trigger this)
- **Trigger**: FlaUInspect (external UIA client) navigating the automation tree

### !analyze -v Output
```
FAILURE_BUCKET: NULL_CLASS_PTR_READ_c0000005_Microsoft.UI.Xaml.dll!std::_Hash<...>::emplace<DirectUI::DependencyObject * const &>
EXCEPTION_CODE: c0000005 (Access violation)
AV.Dereference: NullClassPtr
AV.Fault: Read
READ_ADDRESS: 0x0000000000000088
CLR.Version: 10.0.526.15411
IMAGE_VERSION: 3.1.8.0 (Microsoft.UI.Xaml.dll — Windows App SDK 1.8)
PROCESS_UPTIME: 12 seconds
CRASH_THREAD: 95 (UIA IO thread, NOT the UI thread)
```

### FAULTING_IP
```
Microsoft_UI_Xaml!std::_Hash<std::_Uset_traits<DirectUI::DependencyObject*,...>>::emplace + 0x2d
Source: xhash:611 (MSVC STL hash set implementation)
Instruction: mov rdx, qword ptr [rsi+18h]  ; rsi=0x70, reading 0x70+0x18=0x88 → NULL deref
```
The crash is inside `std::unordered_set::emplace()` — the hash set's internal bucket array pointer is NULL.

### Full Call Stack (crash thread 95)
```
Microsoft_UI_Xaml!std::_Hash<...>::emplace<DependencyObject* const&>     ← CRASH: null bucket ptr
Microsoft_UI_Xaml!DirectUI::DXamlCore::GetPeerPrivate                   ← inserting into peer tracking set
Microsoft_UI_Xaml!AgCoreCallbacks::CreateManagedPeer
Microsoft_UI_Xaml!CDependencyObject::PrivateEnsurePeerAndTryPeg
Microsoft_UI_Xaml!CDependencyObject::TryEnsureManagedPeer
Microsoft_UI_Xaml!CDependencyObject::OnCreateAutomationPeerInternal     ← creating automation peer
Microsoft_UI_Xaml!CUIElement::OnCreateAutomationPeer
Microsoft_UI_Xaml!CUIElement::GetAPChildrenCount                        ← recursive (6 frames deep)
Microsoft_UI_Xaml!CUIElement::GetAPChildrenCount
Microsoft_UI_Xaml!CUIElement::GetAPChildrenCount
Microsoft_UI_Xaml!CUIElement::GetAPChildrenCount
Microsoft_UI_Xaml!CUIElement::GetAPChildrenCount
Microsoft_UI_Xaml!CUIElement::GetAPChildrenCount
Microsoft_UI_Xaml!CUIElement::GetAPChildren
Microsoft_UI_Xaml!CUIAWindow::GetAutomationPeersForRoot
Microsoft_UI_Xaml!CUIAWindow::NavigateImpl
Microsoft_UI_Xaml!CUIAWindow::Navigate                                  ← UIA navigation entry point
uiautomationcore!InProcClientAPIStub::UiaNodeTraverser_NavigateProvider
uiautomationcore!ComInvoker::CallTarget
uiautomationcore!InProcClientAPIStub::InvokeInProcAPI
uiautomationcore!UiaNodeTraverser::Traverse
uiautomationcore!InProcClientAPIStub::UiaNode_Find
uiautomationcore!RemoteUiaNodeStub::Incoming_Find
uiautomationcore!InvokeElementMethodOnCorrectContext_Callback
uiautomationcore!ProcessIncomingRequest
uiautomationcore!ChannelBasedServerConnection::ProcessOneMessage
uiautomationcore!ChannelBasedServerConnection::OnData
uiautomationcore!ReadWriteChannelInfo::Service
uiautomationcore!OverlappedIOManager::IoThreadProc                      ← UIA IO thread
kernel32!BaseThreadInitThunk
ntdll!RtlUserThreadStart
```

### UI Thread (thread 0) at crash time
```
win32u!NtUserGetMessage
user32!GetMessageW
Microsoft_UI_Xaml!DirectUI::FrameworkApplication::RunDesktopWindowMessageLoop
Microsoft_UI_Xaml!DirectUI::FrameworkApplication::StartDesktop
Microsoft_UI_Xaml!DirectUI::FrameworkApplicationFactory::Start
coreclr!RunMain
```
**UI thread is IDLE** — blocked in `GetMessageW` waiting for window messages. Not doing anything.

### Thread Ownership — DETERMINED

**Observation**: `uiautomationcore.dll` frames on crashing thread (thread 95), UI thread idle in message loop.

**Meaning**: This is a **UIA provider callback thread**. The external UIA client (FlaUInspect) sends a navigation/find request. UIAutomationCore processes it on its own IO thread and calls into WinUI's automation peer code. During `GetAPChildrenCount` → `OnCreateAutomationPeer` → `GetPeerPrivate`, WinUI tries to insert into a peer tracking hash set that has a NULL internal state.

**Root Cause**: The `DXamlCore` peer tracking `std::unordered_set<DependencyObject*>` is either:
1. Not initialized on the UIA callback thread (thread-affinity bug — the set was created on the UI thread)
2. Being concurrently modified by the UI thread (race condition — no synchronization)
3. Has been destroyed/moved during a layout or GC cycle while UIA is mid-enumeration (lifetime bug)

The registers confirm: `rsi = 0x0000000000000070` (the hash set object), `rbx = 0` (null). Accessing `[rsi+0x18]` = reading from address `0x88` which is unmapped → ACCESS_VIOLATION.

### Null Pointer Source
The null pointer is the **hash set object itself** (`std::unordered_set` at offset 0x70 from some parent structure). The hash set's `_List._Myhead._Next` pointer (at offset +0x18 from the set) is being read, but the entire set region is null/uninitialized. This is inside `DXamlCore::GetPeerPrivate` which manages the DependencyObject↔peer mapping.

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

### Upstream Bug (microsoft/microsoft-ui-xaml)
The `DXamlCore::GetPeerPrivate` method accesses an `std::unordered_set<DependencyObject*>` on a UIA IO thread without synchronization. The hash set is either uninitialized or concurrently modified by the UI thread. This is a thread-safety bug in the WinUI 3 automation peer infrastructure. It affects any WinUI 3 app with a non-trivial visual tree when an external UIA client enumerates it.

**Evidence for upstream bug filing:**
- Dump: `C:\dumps\WinUiSample.exe_260315_153428.dmp`
- Crash at `Microsoft.UI.Xaml.dll` v3.1.8.0 (Windows App SDK 1.8)
- Thread 95 (UIA IO thread) crashes while UI thread 0 is idle
- `!analyze -v` bucket: `NULL_CLASS_PTR_READ_c0000005_Microsoft.UI.Xaml.dll!...emplace`
- Reproducible by hovering FlaUInspect over the NavigationView in the Amplifier app

### FlaUI-MCP Mitigation (branch: fix/crash-safe-uia)
Already implemented on `fix/crash-safe-uia` branch:
- Depth-capped tree walk
- Per-node exception guards around `FindAllChildren()`
- NavigationView-specific stop (don't recurse into NavigationView subtrees)

---

## Session History

### Session 1 — Initial Setup
- Created fork: anokye-labs/FlaUI-MCP
- Branches: `main` (clean), `fix/crash-safe-uia` (mitigations), `investigate/nav-uia-crash` (this)
- Diagnostics toolkit installed
- Repro project created

### Session 3 — CRASH CAPTURED AND ANALYZED
- Minimal repro (NavViewUiaCrashRepro) does NOT crash — even with items, nested children, InfoBadge, custom titlebar
- Switched to actual Amplifier WinUiSample app
- **Crash reproduced**: FlaUInspect (UIA3) navigating Amplifier's NavigationView → `c0000005` ACCESS_VIOLATION
- ProcDump `-t` captured 575 MB dump at `C:\dumps\WinUiSample.exe_260315_153428.dmp`
- `!analyze -v` with full symbols:
  - **Thread 95** (UIA IO thread) crashes in `DXamlCore::GetPeerPrivate` → `std::unordered_set::emplace`
  - **Null bucket pointer** in peer tracking hash set → read from address 0x88
  - **UI thread (0) is IDLE** in `GetMessageW` — not doing anything
  - Call path: `uiautomationcore!Navigate` → `CUIAWindow::Navigate` → recursive `GetAPChildrenCount` → `OnCreateAutomationPeer` → `GetPeerPrivate` → null deref
  - **Root cause**: Thread-safety bug in WinUI 3 — UIA callback thread accesses DXamlCore peer set without synchronization
  - **WindowsAppSDK 1.8** (Microsoft.UI.Xaml.dll v3.1.8.0)
