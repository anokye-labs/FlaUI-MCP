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

### Root Cause — CONFIRMED

**`DXamlCore::GetCurrent()` returns NULL on the UIA IO thread.**

The crash is NOT a race condition. It's deterministic:

1. **UIAutomationCore** receives a navigation/find request from an external UIA client (FlaUInspect)
2. UIAutomationCore dispatches the request on its own **IO thread** (thread 95)
3. The IO thread calls into `CUIAWindow::Navigate` → `GetAPChildren` → recursive `GetAPChildrenCount` → `OnCreateAutomationPeer` → `GetPeerPrivate`
4. `GetPeerPrivate` calls `DXamlCore::GetCurrent()` to get the DXamlCore instance
5. **`DXamlCore::GetCurrent()` uses thread-local storage (TLS) to look up the core for the current thread**
6. The UIA IO thread was **never initialized** with a DXamlCore — `GetCurrent()` returns `NULL`
7. `GetPeerPrivate` dereferences `NULL->m_Peers` → `m_Peers` is at offset `0x70` → reads address `0x88` → **ACCESS VIOLATION**

**Proof from registers:**
- `rsi = 0x70` = `&((DXamlCore*)NULL)->m_Peers` → m_Peers is at offset 0x70 from DXamlCore
- The disassembly shows: `mov rsi, rcx` (rsi = this = the hash set), then `mov rdx, [rsi+18h]` (read from 0x88)
- `rdi = 0x0`, `rbp = 0x0`, `rbx = 0x0` — the DXamlCore pointer is NULL throughout the frame

**Why our repro doesn't crash:**
- Our `UiaCrashTrigger` uses `FindAllChildren`/`TreeWalker` from a separate process
- These go through a **different code path** in UIAutomationCore that may marshal to the UI thread
- FlaUInspect uses `IUIAutomationTreeWalker::Navigate` with specific provider-side navigation that hits the `CUIAWindow::Navigate` → `GetAPChildrenCount` path directly on the IO thread
- The Amplifier's tree may also have a specific depth/structure that triggers deeper recursion through `GetAPChildrenCount` (6 recursive frames in the crash stack)

**Why the current Amplifier code doesn't crash but the old code did:**
- The new code (PR #17) restructured the visual tree: different NavigationView configuration, new `TitleBar` control, `Frame` content navigation
- The new tree returns `E_UNEXPECTED` at the `DesktopChildSiteBridge` level, preventing UIA from descending into the content where the crash occurs
- The old code's tree was structured such that UIA could descend deep enough to trigger `OnCreateAutomationPeer` for a nested element on the IO thread

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

### The Fix (for microsoft/microsoft-ui-xaml PR)
`GetPeerPrivate` must null-check `DXamlCore::GetCurrent()` before dereferencing. When called from a UIA IO thread where no DXamlCore is initialized, it should either:

1. **Return E_FAIL / null** — the safest fix. If there's no DXamlCore on this thread, peer creation is impossible. Return gracefully instead of crashing.
2. **Marshal to the UI thread** via `DispatcherQueue` — correct but higher risk/complexity. DXamlCore knows its thread ID (`m_threadId`).

Option 1 is the minimal fix:
```cpp
// In DXamlCore::GetPeerPrivate, before accessing m_Peers:
DXamlCore* pCore = DXamlCore::GetCurrent();
if (pCore == nullptr)
{
    // UIA callback on a thread with no DXamlCore — cannot create peers
    IFC_RETURN(E_FAIL);
}
```

The same null check is needed in `AgCoreCallbacks::CreateManagedPeer` which calls `GetPeerPrivate`.

### FlaUI-MCP Mitigation (branch: fix/crash-safe-uia)
Already implemented:
- Depth-capped tree walk
- Per-node exception guards around `FindAllChildren()`
- NavigationView-specific stop

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
