using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services;

public class DeviceEnumerationService : IDeviceEnumerationService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int idx = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, ref rect, data) =>
        {
            var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfo(hMon, ref mi))
                monitors.Add(new MonitorInfo { Index = idx++, DeviceName = mi.szDevice, Width = mi.rcMonitor.right - mi.rcMonitor.left, Height = mi.rcMonitor.bottom - mi.rcMonitor.top, X = mi.rcMonitor.left, Y = mi.rcMonitor.top, IsPrimary = (mi.dwFlags & 1) != 0, Handle = hMon });
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public IReadOnlyList<WindowInfo> GetCapturableWindows() => new List<WindowInfo>();

    public IReadOnlyList<AudioDeviceInfo> GetAudioOutputDevices() =>
        new List<AudioDeviceInfo> { new AudioDeviceInfo { Id = "default", Name = "Default Output", IsLoopback = true } };

    public IReadOnlyList<AudioDeviceInfo> GetAudioInputDevices() =>
        new List<AudioDeviceInfo> { new AudioDeviceInfo { Id = "mic", Name = "Default Microphone", IsLoopback = false } };

    public IReadOnlyList<WebcamInfo> GetWebcams() => new List<WebcamInfo>();

    public AudioDeviceInfo? GetDefaultOutputDevice() =>
        new AudioDeviceInfo { Id = "default", Name = "Default Output", IsLoopback = true };

    public AudioDeviceInfo? GetDefaultInputDevice() =>
        new AudioDeviceInfo { Id = "mic", Name = "Default Microphone", IsLoopback = false };
}
