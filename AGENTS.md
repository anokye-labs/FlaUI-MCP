## FlaUI-MCP — Agent Instructions

This repository is a fork of `shanselman/FlaUI-MCP` extended with crash diagnostics tooling for investigating a WinUI 3 `NavigationView` UIA crash.

## Active Investigation
- Branch: `investigate/nav-uia-crash`
- Issue: `anokye-labs/FlaUI-MCP#1`
- Status: See `INVESTIGATION.md` for current state

## Shell Environment
- Shell: `pwsh` (PowerShell 7+)
- Path separator: backslash `\`
- Environment variables: `$env:VAR_NAME` syntax
- Prefer PowerShell cmdlets: `Get-ChildItem`, `Copy-Item`, `Remove-Item`

## Package Management
- System tools: `winget install <package>`
- .NET packages: `dotnet add package <name>`
- Do not use: `apt`, `brew`, `yum`, `sudo`
- Common tools: ProcDump, WinDbg, Git

## Build & Run
- Build: `dotnet build --no-restore`
- Run: `dotnet run --project <ProjectName>`
- Test: `dotnet test`
- WinUI 3 apps require an interactive desktop session — do not run headlessly

## Crash Diagnostics
Set the symbol server before any crash analysis:
```powershell
$env:_NT_SYMBOL_PATH = "SRV*C:\Symbols*https://msdl.microsoft.com/download/symbols"
```

Capture a crash dump:
```powershell
procdump -ma -e 1 -f "C0000005" <ProcessName>.exe C:\dumps\
```

WinDbg analysis sequence:
```text
.symfix C:\Symbols
.reload /f
!analyze -v
kb 100
~*kb
.ecxr
kP 100
```

Check the event log for recent crashes:
```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; Level=1,2; StartTime=(Get-Date).AddMinutes(-10)} -MaxEvents 10
```

### Key WinDbg Commands
| Command | Purpose |
|---------|---------|
| `!analyze -v` | Automated fault analysis with resolved function names |
| `kb 100` / `kp 100` | Full call stack with/without parameters |
| `~*kb` | All thread stacks — determines crash thread ownership |
| `.ecxr` | Switch context to exception record |
| `!clrstack` | Managed-only stack trace |
| `!dumpheap -stat` | Heap object type statistics |
| `!printexception` | Last managed exception |
| `!dumpasync` | Async state machine inspection |

## WinUI 3 UI Automation
Every interactive XAML control needs explicit `AutomationProperties`. `x:Name` does **not** become `AutomationId` in WinUI 3.

```xml
<Button AutomationProperties.AutomationId="btn_submit"
        AutomationProperties.Name="Submit Order" />
```

Custom controls must implement `OnCreateAutomationPeer()` or they are invisible to FlaUI.

Known bugs (under investigation):
- NavigationView UIA crash: `0xc0000005` in `Microsoft.UI.Xaml.dll` on tree enumeration
- InfoBadge peer crash at `Microsoft.UI.Xaml.dll+0x35f9d`
- Custom title bar buttons have no `AutomationId` when `ExtendsContentIntoTitleBar=true`

Validation: use FlaUIInspect (UIA3 mode) to audit the live tree before writing tests.

## FlaUI Testing Conventions
Never use `Thread.Sleep`. Always wrap post-navigation lookups in `Retry`:
```csharp
var btn = Retry.WhileNull(
    () => window.FindFirstDescendant(cf => cf.ByAutomationId("btn_submit")),
    TimeSpan.FromSeconds(5)
).Result;
btn.WaitUntilEnabled(TimeSpan.FromSeconds(3));
btn.ScrollIntoView();
btn.Click();
```

Screenshot capture: do **not** use `Capture.Element()` — DPI scaling bug. Use Win32 `GetWindowRect` + `Graphics.CopyFromScreen` keyed by `HWND`.

Stale COM proxy (`0x80040201`): WinUI 3 invalidates UIA COM proxies after any tree-changing operation. Never cache `AutomationElement` references across test steps.

## Diagnostic Workflow (for future sessions)
1. Read `INVESTIGATION.md` to check Current Status
2. Run repro: `dotnet run --project repro\NavViewUiaCrashRepro`
3. If needed, capture dump: `procdump -ma -e 1 -f "C0000005" NavViewUiaCrashRepro.exe C:\dumps\`
4. Analyze via mcp-windbg or manual WinDbg
5. Record findings in `INVESTIGATION.md`
6. Iterate

When done, update the SQL todo:
```sql
UPDATE todos SET status = 'done' WHERE id = 'agents-md';
```
