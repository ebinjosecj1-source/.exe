# Windows Screen Recorder

A professional, production-grade Windows screen recording application built with C# / .NET 8 / WPF / MVVM.

## Feature Overview

- **Screen Capture**: Full desktop, single monitor, selected window, or custom region
- **Audio Recording**: Windows system audio (WASAPI loopback), microphone, or both simultaneously
- **Video Encoding**: H.264, H.265, AV1 with hardware acceleration (NVENC, AMF, QuickSync) and software fallback
- **Output Formats**: MP4, MKV, AVI
- **Quality Presets**: Lossless, High, Medium, Low (CRF-based)
- **Webcam Overlay**: Optional resizable/movable webcam PiP with opacity and border style
- **Global Hotkeys**: Customizable F8/F9/F10/F11 defaults
- **Screenshot**: Single-frame capture from current session
- **Recording History**: Browse, open, and manage past recordings
- **Auto-cleanup**: Configurable auto-delete of recordings older than N days
- **Live Stats**: CPU, memory, file size, remaining disk space, VU meters
- **Update Checker**: GitHub Releases API-based automatic update detection
- **Dark UI**: Windows 11-style dark theme with rounded corners

## Requirements

| Component | Minimum |
|-----------|---------|
| OS | Windows 10 (19041) or Windows 11 |
| Architecture | x64 only |
| .NET Runtime | Bundled (self-contained) |
| Visual Studio | 2022 17.8+ (for development) |
| Windows SDK | 10.0.22621.0 |

## Development Setup

### 1. Prerequisites

Install the following via Visual Studio Installer or standalone:

- Visual Studio 2022 with:
  - .NET Desktop Development workload
  - Windows App SDK (Microsoft.WindowsAppSDK)
- .NET 8 SDK (x64): https://dotnet.microsoft.com/download/dotnet/8.0
- Windows SDK 10.0.22621.0

### 2. Clone and Restore

```powershell
git clone <repository-url>
cd WindowsScreenRecorder
dotnet restore src/WindowsScreenRecorder/WindowsScreenRecorder.csproj
```

### 3. Build (Debug)

```powershell
dotnet build src/WindowsScreenRecorder/WindowsScreenRecorder.csproj `
    -c Debug -r win-x64
```

Output: `src/WindowsScreenRecorder/bin/Debug/net8.0-windows10.0.22621.0/win-x64/`

### 4. Build (Release)

```powershell
dotnet build src/WindowsScreenRecorder/WindowsScreenRecorder.csproj `
    -c Release -r win-x64
```

### 5. Publish — Single-File Executable

```powershell
dotnet publish src/WindowsScreenRecorder/WindowsScreenRecorder.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish/
```

Output: `publish/WindowsScreenRecorder.exe` (~150–200 MB, all dependencies bundled)

### 6. Open in Visual Studio

Double-click `WindowsScreenRecorder.sln` to open the full solution.

## Project Structure

```
WindowsScreenRecorder/
├── src/
│   └── WindowsScreenRecorder/
│       ├── Core/
│       │   ├── Enums/          # All application enums
│       │   ├── Interfaces/     # Service contracts (IRecordingService, etc.)
│       │   └── Models/         # Domain models (AppSettings, RecordingStats, etc.)
│       ├── Services/
│       │   ├── Audio/          # WASAPI loopback + microphone capture
│       │   ├── Capture/        # WGC + GDI BitBlt screen capture
│       │   ├── Encoding/       # FFmpeg encoder + hardware detection
│       │   ├── FileManagement/ # File ops, notifications, update checker
│       │   ├── Hotkeys/        # Global Win32 hotkey registration
│       │   └── Video/          # Webcam overlay (AForge)
│       ├── ViewModels/         # MainViewModel, SettingsViewModel
│       ├── Views/
│       │   ├── Controls/       # SettingsPanel UserControl
│       │   └── MainWindow.xaml
│       ├── Converters/         # WPF value converters
│       └── Resources/
│           ├── Icons/          # app.ico
│           └── Styles/         # Colors, Typography, Controls, Animations XAML
├── installer/                  # Inno Setup script (Phase 11)
├── docs/                       # Architecture notes
└── WindowsScreenRecorder.sln
```

## Architecture

The application follows clean MVVM with constructor-based dependency injection:

```
UI Layer (Views + ViewModels)
    ↓ ICommand / ObservableProperty bindings
Service Layer (IRecordingService, ISettingsService, ...)
    ↓ Async interfaces
Infrastructure (FFmpeg encoder, WGC capture, WASAPI audio)
```

**Key design decisions:**

- `RecordingService` orchestrates capture, encoding, and muxing via `Task`-based async pipelines
- `ScreenCaptureService` prefers Windows Graphics Capture API; falls back to GDI `BitBlt` on older builds
- `VideoEncoderService` attempts hardware codec initialization; falls back to `libx264` / `libx265` if unavailable
- All services are registered in `App.xaml.cs` with `Microsoft.Extensions.DI`
- Serilog writes structured logs to `%AppData%\WindowsScreenRecorder\Logs\wsr-[date].log`
- Settings persist as JSON to `%AppData%\WindowsScreenRecorder\settings.json`
- Default output folder: `%USERPROFILE%\Videos\Screen Recordings`

## Libraries Used

| Library | Purpose |
|---------|---------|
| FFmpeg.AutoGen + Sdcb.FFmpeg | Video/audio encoding, muxing, hardware codec APIs |
| NAudio / NAudio.Wasapi | WASAPI loopback (system audio) and microphone capture |
| Windows Graphics Capture API | Primary screen capture (WGC) |
| GDI BitBlt (P/Invoke) | Fallback screen capture |
| CommunityToolkit.Mvvm | MVVM source generators (RelayCommand, ObservableProperty) |
| Microsoft.Extensions.DI | Dependency injection container |
| Serilog | Structured rolling-file logging |
| Newtonsoft.Json | Settings JSON serialization |
| LibreHardwareMonitorLib | GPU name detection |
| AForge.Video.DirectShow | Webcam enumeration and frame capture |
| PInvoke.User32/Kernel32 | Win32 API interop (hotkeys, monitors, windows) |

## Hotkeys (Default)

| Action | Key |
|--------|-----|
| Start recording | F8 |
| Pause / Resume | F9 |
| Stop recording | F10 |
| Take screenshot | F11 |

All hotkeys are customizable from the Settings panel.

## Performance Targets

| Metric | Target (hardware encoding) |
|--------|---------------------------|
| CPU usage | < 10–15% |
| Memory usage | < 300 MB |
| Frame latency | < 33 ms @ 30 FPS |

## Development Phases

| Phase | Status | Description |
|-------|--------|-------------|
| 1 | Complete | Project architecture, DI, all service interfaces, UI shell |
| 2 | Planned | Recording engine integration |
| 3 | Planned | Screen capture (WGC + GDI) |
| 4 | Planned | System audio (WASAPI loopback) |
| 5 | Planned | Microphone recording |
| 6 | Planned | Audio stream merging |
| 7 | Planned | FFmpeg video encoding pipeline |
| 8 | Planned | UI polish and animations |
| 9 | Planned | Full settings wiring |
| 10 | Planned | Testing and profiling |
| 11 | Planned | Inno Setup installer |

## Logging

Logs are written to:
```
%AppData%\WindowsScreenRecorder\Logs\wsr-YYYY-MM-DD.log
```

Rotated daily, retained for 7 days.

## License

MIT License. See `LICENSE` file.
