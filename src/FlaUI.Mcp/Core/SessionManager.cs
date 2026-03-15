using System.Drawing;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FlaUIApplication = FlaUI.Core.Application;

namespace PlaywrightWindows.Mcp.Core;

/// <summary>
/// Manages UI Automation sessions and launched applications.
/// Stores HWNDs (stable) instead of AutomationElement refs (stale after UIA tree changes).
/// </summary>
public class SessionManager : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly Dictionary<string, FlaUIApplication> _applications = new();
    private readonly Dictionary<string, IntPtr> _windowHandles = new();
    private readonly Dictionary<string, string> _windowTitles = new();
    private int _appCounter = 0;
    private int _windowCounter = 0;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const int SW_RESTORE = 9;

    public SessionManager()
    {
        _automation = new UIA3Automation();
    }

    public UIA3Automation Automation => _automation;

    public (string handle, Window window) LaunchApp(string appPath, string[]? args = null)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = appPath,
            Arguments = args != null ? string.Join(" ", args) : "",
            UseShellExecute = true
        };
        
        var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
        {
            throw new Exception($"Failed to start process: {appPath}");
        }
        
        try
        {
            process.WaitForInputIdle(5000);
        }
        catch { /* Some processes don't support this */ }
        
        Thread.Sleep(1000);
        
        var desktop = _automation.GetDesktop();
        Window? window = null;
        
        var element = desktop.FindFirstDescendant(cf => cf.ByProcessId(process.Id));
        if (element != null)
        {
            window = element.AsWindow();
        }
        
        if (window == null)
        {
            var existingTitles = new HashSet<string>(
                _windowTitles.Values.Where(t => !string.IsNullOrEmpty(t))
            );
            
            for (int i = 0; i < 10 && window == null; i++)
            {
                Thread.Sleep(500);
                var windows = desktop.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
                foreach (var w in windows)
                {
                    var win = w.AsWindow();
                    if (win != null && !string.IsNullOrEmpty(win.Title))
                    {
                        var title = win.Title.ToLowerInvariant();
                        var appName = Path.GetFileNameWithoutExtension(appPath).ToLowerInvariant();
                        if (title.Contains(appName) || !existingTitles.Contains(win.Title))
                        {
                            window = win;
                            break;
                        }
                    }
                }
            }
        }
        
        if (window == null)
        {
            throw new Exception($"Could not find window for {appPath}. Try using windows_list_windows and windows_focus instead.");
        }

        var windowHandle = RegisterWindow(window);
        return (windowHandle, window);
    }

    public (string handle, Window window) AttachToWindow(string title)
    {
        var desktop = _automation.GetDesktop();
        var window = desktop.FindFirstDescendant(cf => cf.ByName(title))?.AsWindow();
        
        if (window == null)
        {
            throw new Exception($"Window not found: {title}");
        }

        var handle = RegisterWindow(window);
        return (handle, window);
    }

    /// <summary>
    /// Register a window by extracting and storing its HWND (stable) and title.
    /// </summary>
    public string RegisterWindow(Window window)
    {
        var handle = $"w{++_windowCounter}";
        var hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
        _windowHandles[handle] = new IntPtr(hwnd);
        try { _windowTitles[handle] = window.Title ?? ""; } catch { _windowTitles[handle] = ""; }
        return handle;
    }

    /// <summary>
    /// Get a fresh AutomationElement Window from the stored HWND.
    /// Never returns a cached AutomationElement — always re-fetches.
    /// </summary>
    public Window? GetWindow(string handle)
    {
        if (!_windowHandles.TryGetValue(handle, out var hwnd))
            return null;

        if (!IsWindow(hwnd))
            return null;

        try
        {
            var element = _automation.FromHandle(hwnd);
            return element?.AsWindow();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Get the stored HWND for a window handle. Always stable.
    /// </summary>
    public IntPtr? GetWindowHwnd(string handle)
    {
        return _windowHandles.TryGetValue(handle, out var hwnd) ? hwnd : null;
    }

    /// <summary>
    /// Capture a screenshot of a window using its HWND directly.
    /// Uses GetWindowRect + Graphics.CopyFromScreen — no UIA dependency.
    /// </summary>
    public Bitmap? CaptureWindowByHwnd(string handle)
    {
        if (!_windowHandles.TryGetValue(handle, out var hwnd))
            return null;

        if (!IsWindow(hwnd))
            return null;

        // Restore if minimized
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        if (!GetWindowRect(hwnd, out RECT rect))
            return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
            return null;

        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));
        return bitmap;
    }

    /// <summary>
    /// Capture a screenshot of the entire screen.
    /// </summary>
    public static Bitmap CaptureScreen()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        return bitmap;
    }

    public List<(string handle, string title, string? processName)> ListWindows()
    {
        var desktop = _automation.GetDesktop();
        var windows = desktop.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
        
        var result = new List<(string, string, string?)>();
        foreach (var w in windows)
        {
            var window = w.AsWindow();
            if (window != null && !string.IsNullOrEmpty(window.Title))
            {
                var handle = RegisterWindow(window);
                string? processName = null;
                try 
                { 
                    processName = window.Properties.ProcessId.TryGetValue(out var pid) 
                        ? System.Diagnostics.Process.GetProcessById(pid).ProcessName 
                        : null; 
                }
                catch { }
                
                result.Add((handle, window.Title, processName));
            }
        }
        return result;
    }

    public void FocusWindow(string handle)
    {
        if (!_windowHandles.TryGetValue(handle, out var hwnd))
            throw new Exception($"Window not found: {handle}");

        if (!IsWindow(hwnd))
            throw new Exception($"Window no longer exists: {handle}");

        // Restore if minimized
        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);

        SetForegroundWindow(hwnd);
    }

    public void CloseWindow(string handle)
    {
        var window = GetWindow(handle);
        if (window == null)
        {
            throw new Exception($"Window not found: {handle}");
        }
        window.Close();
        _windowHandles.Remove(handle);
        _windowTitles.Remove(handle);
    }

    public void Dispose()
    {
        foreach (var app in _applications.Values)
        {
            try { app.Close(); } catch { }
        }
        _applications.Clear();
        _windowHandles.Clear();
        _windowTitles.Clear();
        _automation.Dispose();
    }
}
