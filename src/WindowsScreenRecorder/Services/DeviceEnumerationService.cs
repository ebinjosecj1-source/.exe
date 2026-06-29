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
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate cb, IntPtr dwData);
    private delegate bool MonitorEnumDelegate(IntPtr hMon, IntPtr hdc, ref NativeRect rect, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int left, top, right, bottom; }

    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int idx = 0;
        bool Callback(IntPtr hMon, IntPtr hdc, ref NativeRect rect, IntPtr data)
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfoW(hMon, ref mi))
            {
                monitors.Add(new MonitorInfo
                {
                    Index = idx++,
                    DeviceName = mi.szDevice,
                    FriendlyName = mi.szDevice,
                    Bounds = new Rect(mi.rcMonitor.left, mi.rcMonitor.top, mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top),
                    WorkArea = new Rect(mi.rcWork.left, mi.rcWork.top, mi.rcWork.right - mi.rcWork.left, mi.rcWork.bottom - mi.rcWork.top),
                    IsPrimary = (mi.dwFlags & 1) != 0
                });
            }
            return true;
        }
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
        return monitors;
    }

    public IReadOnlyList<WindowInfo> GetCapturableWindows() => new List<WindowInfo>();

    public IReadOnlyList<AudioDeviceInfo> GetAudioOutputDevices() =>
        new List<AudioDeviceInfo> { new AudioDeviceInfo { Id = "default", FriendlyName = "Default Output", IsCapture = false } };

    public IReadOnlyList<AudioDeviceInfo> GetAudioInputDevices() =>
        new List<AudioDeviceInfo> { new AudioDeviceInfo { Id = "mic", FriendlyName = "Default Microphone", IsCapture = true } };

    public IReadOnlyList<WebcamInfo> GetWebcams() => new List<WebcamInfo>();

    public AudioDeviceInfo? GetDefaultOutputDevice() =>
        new AudioDeviceInfo { Id = "default", FriendlyName = "Default Output", IsCapture = false };

    public AudioDeviceInfo? GetDefaultInputDevice() =>
        new AudioDeviceInfo { Id = "mic", FriendlyName = "Default Microphone", IsCapture = true };
}
