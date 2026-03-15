# NavViewUiaCrashRepro

Minimal repro for WinUI 3 NavigationView UIA crash (0xc0000005).

## Run
```
dotnet run --project repro\NavViewUiaCrashRepro -r win-x64
```

## What It Does
- Shows a NavigationView (initially empty)
- "Trigger UIA Enumeration" button walks the UIA tree using FlaUI's UIA3Automation
- If the crash occurs, the process exits with 0xc0000005

## Binary Search
Modify `MainWindow.xaml.cs` to add items incrementally:
1. Empty NavigationView (default)
2. Add one static XAML NavigationViewItem
3. Add one programmatic item
4. Add multiple items
5. Add nested child items
6. Add InfoBadge

Record which configuration triggers the crash in `INVESTIGATION.md`.

## Capture Dump
```
procdump -ma -e 1 -f "C0000005" NavViewUiaCrashRepro.exe C:\dumps\
```
Then run the app and click the button.
