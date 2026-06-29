using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services.Hotkeys;

/// <summary>
/// Registers global system hotkeys via Win32 RegisterHotKey.
/// Uses a hidden message-only window (HWND_MESSAGE) to receive WM_HOTKEY messages
/// without interfering with the main application window.
/// 
/// Registered hotkeys work even when the application is minimized or behind
/// other windows, which is critical for recording workflows.
/// </summary>
public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly ILogger<GlobalHotkeyService> _logger;
    private HwndSource? _messageWindow;
    private readonly Dictionary<int, HotkeyAction> _registeredHotkeys = new();
    private int _hotkeyIdCounter = 0x1000;
    private bool _disposed;

    public event EventHandler? StartHotkeyPressed;
    public event EventHandler? PauseHotkeyPressed;
    public event EventHandler? StopHotkeyPressed;
    public event EventHandler? ScreenshotHotkeyPressed;

    private enum HotkeyAction { Start, Pause, Stop, Screenshot }

    public GlobalHotkeyService(ILogger<GlobalHotkeyService> logger)
    {
        _logger = logger;
    }

    public void RegisterAll(AppSettings settings)
    {
        EnsureMessageWindow();
        UnregisterAll();

        TryRegister(settings.HotkeyStart, HotkeyAction.Start);
        TryRegister(settings.HotkeyPause, HotkeyAction.Pause);
        TryRegister(settings.HotkeyStop, HotkeyAction.Stop);
        TryRegister(settings.HotkeyScreenshot, HotkeyAction.Screenshot);
    }

    public void UnregisterAll()
    {
        if (_messageWindow is null) return;

        foreach (var id in _registeredHotkeys.Keys)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, id);
        }
        _registeredHotkeys.Clear();
        _logger.LogDebug("All hotkeys unregistered");
    }

    public bool IsKeyAvailable(string key)
    {
        if (!TryParseKey(key, out _, out var vk)) return false;

        // Try to register temporarily to verify availability
        EnsureMessageWindow();
        int testId = 0x7FFF;
        bool ok = NativeMethods.RegisterHotKey(
            _messageWindow!.Handle, testId, 0, (uint)KeyInterop.VirtualKeyFromKey(vk));
        if (ok) NativeMethods.UnregisterHotKey(_messageWindow.Handle, testId);
        return ok;
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    private void EnsureMessageWindow()
    {
        if (_messageWindow is not null) return;

        // Create a message-only window (not visible, receives only messages)
        var parameters = new HwndSourceParameters("WSR_HotkeyWindow")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _messageWindow = new HwndSource(parameters);
        _messageWindow.AddHook(WndProc);
        _logger.LogDebug("Hotkey message window created");
    }

    private void TryRegister(string keyStr, HotkeyAction action)
    {
        if (!TryParseKey(keyStr, out uint modifiers, out Key key)) return;

        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        int id = ++_hotkeyIdCounter;

        bool ok = NativeMethods.RegisterHotKey(_messageWindow!.Handle, id, modifiers, vk);
        if (ok)
        {
            _registeredHotkeys[id] = action;
            _logger.LogInformation("Hotkey registered: {Key} → {Action}", keyStr, action);
        }
        else
        {
            int err = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "Failed to register hotkey {Key} (Win32 error {Error})", keyStr, err);
        }
    }

    private static bool TryParseKey(string keyStr, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;

        if (string.IsNullOrWhiteSpace(keyStr)) return false;

        var parts = keyStr.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 0x0002u,
                "ALT" => 0x0001u,
                "SHIFT" => 0x0004u,
                "WIN" => 0x0008u,
                _ => 0u
            };
        }

        string keyName = parts[^1];
        return Enum.TryParse(keyName, ignoreCase: true, out key) && key != Key.None;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (_registeredHotkeys.TryGetValue(id, out var action))
            {
                _logger.LogDebug("Hotkey fired: {Action}", action);
                switch (action)
                {
                    case HotkeyAction.Start: StartHotkeyPressed?.Invoke(this, EventArgs.Empty); break;
                    case HotkeyAction.Pause: PauseHotkeyPressed?.Invoke(this, EventArgs.Empty); break;
                    case HotkeyAction.Stop: StopHotkeyPressed?.Invoke(this, EventArgs.Empty); break;
                    case HotkeyAction.Screenshot: ScreenshotHotkeyPressed?.Invoke(this, EventArgs.Empty); break;
                }
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        _messageWindow?.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
