using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using WindowsScreenRecorder.Core.Interfaces;
using WindowsScreenRecorder.Core.Models;

namespace WindowsScreenRecorder.Services;

public class DeviceEnumerationService : IDeviceEnumerationService
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);
    private delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref NativeRect lprcMonitor, IntPtr dwData);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX { public int cbSize; public NativeRect rcMonitor; public NativeRect rcWork; public int dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int left, top, right, bottom; }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int idx = 0;
        MonitorEnumDelegate del = (IntPtr hMon, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfoW(hMon, ref mi))
            {
                var bounds = new Rect(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top);
                monitors.Add(new MonitorInfo
                {
                    Index = idx++,
                    DeviceName = mi.szDevice,
                    FriendlyName = mi.szDevice,
                    Bounds = bounds,
                    WorkArea = new Rect(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right - mi.rcWork.left, mi.rcWork.bottom - mi.rcWork.top),
                    IsPrimary = (mi.dwFlags & 1) != 0,
                    Handle = hMon
                });
            }
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, del, IntPtr.Zero);
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
