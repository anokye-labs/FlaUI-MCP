# NavigationView UIA Crash — Reproduction Summary

## Crash Identity
- **Exception**: Access violation (`0xc0000005`)
- **Module**: `Microsoft.UI.Xaml.dll`
- **Crash site 1**: `Microsoft.UI.Xaml.dll+0x35f9d` (access violation)
- **Crash site 2**: `Microsoft.UI.Xaml.dll+0x39dec5` (stowed exception)
- **Trigger**: Any UIA client enumerating the NavigationView automation subtree

## Affected
- FlaUIInspect (UIA3 mode)
- FlaUI-MCP tree dump
- Any UIA client that deeply enumerates the tree
- Amplifier WinUI 3 desktop app

## Known Non-Fixes
| Attempted Mitigation | Result |
|---------------------|--------|
| Remove InfoBadge | Resolves crash site 1 only; crash site 2 remains |
| Flatten NavigationView hierarchy (no nested children) | Still crashes |
| `AccessibilityView="Raw"` on NavigationView | Still crashes — native peer instantiated before filter applies |
| Custom `AutomationPeer` override with empty `GetChildrenCore()` | Still crashes — native peer construction precedes managed override |

## What Has NOT Been Tried
- [ ] Full symbol-resolved WinDbg analysis (currently only raw offsets)
- [ ] ProcDump capture with Microsoft symbol server configured
- [ ] Determining crash thread (UI thread vs UIA provider callback thread)
- [ ] Determining root cause category (re-entrancy, lifetime/teardown, or null peer)
- [ ] Time Travel Debugging (TTD) recording

## Reproduction Steps
1. Run the repro project: `dotnet run --project repro\NavViewUiaCrashRepro`
2. Click "Trigger UIA Enumeration" button
3. Observe process crash with `0xc0000005`

## Environment
- WinUI 3 (Windows App SDK) — version TBD from repro project
- .NET 8
- Windows 11

## Related Issues
- microsoft/microsoft-ui-xaml#9538 — NavigationView exception on destruct while debugging
