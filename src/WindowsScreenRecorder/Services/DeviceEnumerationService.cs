using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using AForge.Video.DirectShow;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services;

/// <summary>
/// Enumerates all system devices using Win32, NAudio (WASAPI), and AForge (DirectShow).
/// Runs synchronously since the underlying APIs are blocking.
/// </summary>
public sealed class DeviceEnumerationService : IDeviceEnumerationService
{
    private readonly ILogger<DeviceEnumerationService> _logger;

    public DeviceEnumerationService(ILogger<DeviceEnumerationService> logger)
    {
        _logger = logger;
    }

    // ─── Monitors ─────────────────────────────────────────────────────────────

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int index = 0;

        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (hMonitor, _, _, _) =>
            {
                var info = new NativeMethods.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(info);

                if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    return true;

                var bounds = new Rect(
                    info.rcMonitor.left, info.rcMonitor.top,
                    info.rcMonitor.right - info.rcMonitor.left,
                    info.rcMonitor.bottom - info.rcMonitor.top);

                var workArea = new Rect(
                    info.rcWork.left, info.rcWork.top,
                    info.rcWork.right - info.rcWork.left,
                    info.rcWork.bottom - info.rcWork.top);

                bool isPrimary = (info.dwFlags & 0x01) != 0;
                string deviceName = new string(info.szDevice).TrimEnd('\0');

                // Get DPI scale via Win32
                NativeMethods.GetDpiForMonitor(hMonitor, 0, out uint dpiX, out _);
                double scale = dpiX / 96.0;

                monitors.Add(new MonitorInfo
                {
                    DeviceName = deviceName,
                    FriendlyName = $"Monitor {index + 1}",
                    Bounds = bounds,
                    WorkArea = workArea,
                    IsPrimary = isPrimary,
                    Index = index,
                    ScaleFactor = scale > 0 ? scale : 1.0
                });

                index++;
                return true;
            },
            IntPtr.Zero);

        _logger.LogDebug("Found {Count} monitors", monitors.Count);
        return monitors;
    }

    // ─── Application Windows ──────────────────────────────────────────────────

    public IReadOnlyList<WindowInfo> GetCapturableWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            // Skip invisible, cloaked, tool windows
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            int style = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            if ((style & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;

            NativeMethods.DwmGetWindowAttribute(
                hWnd,
                NativeMethods.DWMWA_CLOAKED,
                out int cloaked, sizeof(int));
            if (cloaked != 0) return true;

            var titleBuilder = new StringBuilder(512);
            NativeMethods.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);

            string processName = "(unknown)";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { /* process may have exited */ }

            NativeMethods.GetWindowRect(hWnd, out var rect);
            var bounds = new Rect(rect.left, rect.top,
                rect.right - rect.left, rect.bottom - rect.top);

            bool isMinimized = NativeMethods.IsIconic(hWnd);

            windows.Add(new WindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
                ProcessId = (int)pid,
                Bounds = bounds,
                IsMinimized = isMinimized
            });

            return true;
        }, IntPtr.Zero);

        _logger.LogDebug("Found {Count} capturable windows", windows.Count);
        return windows;
    }

    // ─── Audio Devices ────────────────────────────────────────────────────────

    public IReadOnlyList<AudioDeviceInfo> GetAudioOutputDevices()
    {
        return GetAudioDevices(DataFlow.Render);
    }

    public IReadOnlyList<AudioDeviceInfo> GetAudioInputDevices()
    {
        return GetAudioDevices(DataFlow.Capture);
    }

    private IReadOnlyList<AudioDeviceInfo> GetAudioDevices(DataFlow flow)
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                var def = enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                defaultId = def.ID;
            }
            catch { /* no default device */ }

            var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active);
            foreach (var device in devices)
            {
                result.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    FriendlyName = device.FriendlyName,
                    IsDefault = device.ID == defaultId,
                    IsCapture = flow == DataFlow.Capture
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate audio devices (flow={Flow})", flow);
        }

        return result;
    }

    public AudioDeviceInfo? GetDefaultOutputDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new AudioDeviceInfo
            {
                Id = device.ID,
                FriendlyName = device.FriendlyName,
                IsDefault = true,
                IsCapture = false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get default audio output device");
            return null;
        }
    }

    public AudioDeviceInfo? GetDefaultInputDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            return new AudioDeviceInfo
            {
                Id = device.ID,
                FriendlyName = device.FriendlyName,
                IsDefault = true,
                IsCapture = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get default audio input device");
            return null;
        }
    }

    // ─── Webcams ──────────────────────────────────────────────────────────────

    public IReadOnlyList<WebcamInfo> GetWebcams()
    {
        var result = new List<WebcamInfo>();
        try
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            for (int i = 0; i < videoDevices.Count; i++)
            {
                result.Add(new WebcamInfo
                {
                    MonikerString = videoDevices[i].MonikerString,
                    FriendlyName = videoDevices[i].Name,
                    Index = i
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate webcams");
        }

        _logger.LogDebug("Found {Count} webcams", result.Count);
        return result;
    }
}

// ─── P/Invoke declarations ────────────────────────────────────────────────────

internal static partial class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int DWMWA_CLOAKED = 14;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool EnumMonitorsProc(IntPtr hMonitor, IntPtr hDC, IntPtr lpRect, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(
        IntPtr hDC, IntPtr lprcClip,
        EnumMonitorsProc lpfnEnum, IntPtr dwData);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(
        IntPtr hwnd, int dwAttribute,
        out int pvAttribute, int cbAttribute);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
