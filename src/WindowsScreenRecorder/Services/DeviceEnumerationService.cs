using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
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
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    public List<MonitorInfo> EnumerateMonitors()
    {
        var monitors = new List<MonitorInfo>();
        int index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMon, hdc, ref rect, data) =>
        {
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(mi);
            if (GetMonitorInfo(hMon, ref mi))
            {
                monitors.Add(new MonitorInfo
                {
                    Index = index++,
                    DeviceName = mi.szDevice,
                    Width = mi.rcMonitor.right - mi.rcMonitor.left,
                    Height = mi.rcMonitor.bottom - mi.rcMonitor.top,
                    X = mi.rcMonitor.left,
                    Y = mi.rcMonitor.top,
                    IsPrimary = (mi.dwFlags & 1) != 0,
                    Handle = hMon
                });
            }
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    public List<AudioDeviceInfo> EnumerateAudioDevices()
    {
        return new List<AudioDeviceInfo>
        {
            new AudioDeviceInfo { Id = "default", Name = "Default Audio Device", IsLoopback = false },
            new AudioDeviceInfo { Id = "loopback", Name = "System Audio (Loopback)", IsLoopback = true }
        };
    }

    public List<WindowInfo> EnumerateWindows()
    {
        return new List<WindowInfo>();
    }
}
