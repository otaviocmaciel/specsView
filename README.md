# SpecsView

A lightweight Windows taskbar widget that shows real-time system metrics — CPU, memory, GPU, disk,
network, temperatures and more — right next to the notification area.

- **Configurable**: pick exactly which metrics to show, as text, text + bar or text + sparkline.
- **Lightweight**: ~22 MB RAM and ~0.05% CPU at a 1 s refresh rate (see [Performance](#performance)).
- **Readable**: follows the Windows light/dark theme, per-monitor DPI aware, threshold colors.
- **Customizable**: colors, font family and size, layout, refresh interval — hot-reloaded from `config.json`.

> Status: early development. Configuration is done by editing `config.json`; a settings window is on the [roadmap](#roadmap).

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- Optional, for CPU/motherboard temperatures: administrator rights and the [PawnIO](https://pawnio.eu) driver (see [Privileges](#privileges))

## Getting started

```powershell
dotnet build
dotnet test
dotnet run --project src/SpecsView.App
```

### Building the executable

```powershell
./publish.ps1                    # artifacts/publish/framework-dependent/SpecsView.exe (~5 MB)
./publish.ps1 -Install           # also installs to %LOCALAPPDATA%\Programs\SpecsView + Start Menu shortcut
./publish.ps1 -SelfContained     # bundles the .NET runtime, for machines without .NET 10
```

The output is a single `SpecsView.exe`. The default build requires the
[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Right-click the widget or its tray icon for the menu (UI strings are currently in Portuguese):

| Menu item | Action |
|---|---|
| Editar configurações | Open `config.json` |
| Ver métricas disponíveis | Generate a report of every metric id available on this machine |
| Abrir pasta de configurações | Open `%APPDATA%\SpecsView` |
| Pausar / Retomar | Pause or resume sampling |
| Reiniciar como administrador | Restart elevated (only shown when not elevated) |
| Sair | Exit |

## Configuration

`%APPDATA%\SpecsView\config.json` is created on first run and reloaded automatically when edited.
Comments and trailing commas are allowed; out-of-range values are clamped instead of rejected.

```jsonc
{
  "schemaVersion": 1,
  "refreshIntervalMs": 1000,                 // 250 – 60000
  "layout": {
    "rows": 2,                               // 1 or 2 (a standard 48 px taskbar fits two)
    "itemSpacing": 8,
    "sparklinePoints": 30,
    "position": "beforeTray",                // "beforeTray" | "left"
    "offsetX": 0                             // fine-tuning, in logical pixels
  },
  "theme": {
    "font": { "family": "Segoe UI", "size": 9, "bold": false },
    "followSystemTheme": true,               // true ignores "foreground" and follows light/dark mode
    "foreground": "#FFFFFF",
    "background": "#00000000",               // #AARRGGBB
    "accent": "#4CC2FF",
    "thresholds": [ { "above": 80, "color": "#FFB900" }, { "above": 95, "color": "#E81123" } ]
  },
  "items": [
    { "metric": "cpu.total", "label": "CPU", "style": "text+sparkline", "secondary": "hw.cpu.temp" },
    { "metric": "mem.usedPercent", "label": "RAM", "style": "text+bar" },
    { "metric": "gpu.total", "label": "GPU", "style": "text+sparkline", "secondary": "hw.gpu.temp" },
    { "metric": "disk.total.active", "label": "Disk", "style": "text+bar" },
    { "metric": "net.total.down", "label": "↓", "style": "text" },
    { "metric": "net.total.up", "label": "↑", "style": "text", "color": "#FF8C00" }
  ]
}
```

The example above renders as three compact columns:

```
CPU   6% ▁▂▅▃  69°C    GPU  1% ▁▁▂▁  56°C    ↓ 12.0 KB/s
RAM  93% ██████        Disk 0% ░░░░░░         ↑  2.6 KB/s
```

- Item styles: `text`, `text+bar`, `text+sparkline`. Items flow column by column (top, bottom, next column).
- `secondary` adds a second metric after the graphic (typically a temperature), so a component needs a single item. Secondary values are aligned within each column.
- Colors can be set per item; thresholds apply to percentage metrics.
- Hovering an item shows its full name, current value and, when it shows "—", the reason.

## Metrics

Use **Ver métricas disponíveis** (or `SpecsView.exe --list-metrics <file>`) to list every id on
your machine together with its current value.

| Prefix | Examples | Notes |
|---|---|---|
| `cpu` | `cpu.total`, `cpu.clock`, `cpu.core.N` | Same numbers as Task Manager |
| `mem` | `mem.usedPercent`, `mem.used`, `mem.commitPercent` | |
| `gpu` | `gpu.total`, `gpu.3d`, `gpu.memUsed`, `gpu.memUsedPercent`, `gpu.N.*` | `gpu.total` is the busiest GPU; memory refers to the GPU with the most VRAM |
| `disk` | `disk.total.active`, `disk.total.read`, `disk.N.write` | `total.active` is the busiest physical disk |
| `drive` | `drive.C.usedPercent`, `drive.D.free` | Fixed drives only |
| `net` | `net.total.down`, `net.total.up`, `net.<adapter>.down` | Totals exclude virtual adapters (Hyper-V, VPN, ...) |
| `battery` | `battery.percent`, `battery.timeRemaining` | "—" on desktops |
| `sys` | `sys.processes`, `sys.threads`, `sys.uptime` | |
| `hw` | `hw.cpu.temp`, `hw.cpu.power`, `hw.gpu.temp`, `hw.gpu.power`, `hw.gpu.fan`, `hw.<group>.<type>.<sensor>` | LibreHardwareMonitor; loaded only when an `hw.*` metric is used |

## Privileges

SpecsView runs **unelevated** by default; CPU, memory, GPU, disk, network and battery metrics need no special rights.

CPU and motherboard sensors (`hw.cpu.*`, `hw.mb.*`) are read through LibreHardwareMonitor, which
requires **administrator rights and the PawnIO kernel driver** (`winget install namazso.PawnIO`;
LibreHardwareMonitor 0.9.5+ no longer uses WinRing0). Without them, LibreHardwareMonitor reports
`0` instead of "no reading"; SpecsView discards those values and shows "—", with the reason in the tooltip.
GPU sensors (NVAPI/ADL) work without elevation.

## Architecture

```
┌──────────────────────────── SpecsView.App (WinForms) ─────────────────────────────────┐
│  TaskbarOverlayForm ◄── TaskbarLocator (taskbar/tray bounds, DPI, full-screen)       │
│        └── WidgetRenderer (text, bars, sparklines → 32bpp bitmap)                     │
│  NotifyIcon + context menu        MetricsReport (--list-metrics)                      │
└───────────────▲───────────────────────────────────────────────────────▲───────────────┘
                │ MetricSnapshot (marshalled to the UI thread)            │ AppConfig
┌───────────────┴──────────── SpecsView.Core ─────────────────────────────┴─────────────┐
│  MetricScheduler (PeriodicTimer, background thread)                                   │
│     └─► ProviderSet (active IMetricProvider[]) ─► MetricHistory (ring buffers)        │
│  ConfigService (JSON source-gen, FileSystemWatcher hot reload, validation)            │
└───────────────▲───────────────────────────────────────────────────────────────────────┘
                │
┌───────────────┴──────────── SpecsView.Sensors ────────────────────────────────────────┐
│  CpuProvider        PDH Processor Information: total/per-core utility, effective clock│
│  MemoryProvider     GlobalMemoryStatusEx (physical + commit)                          │
│  GpuProvider        DXGI (name/VRAM/LUID) + PDH GPU Engine / GPU Adapter Memory       │
│  DiskProvider       PDH PhysicalDisk(*): active time, read, write                     │
│  DriveProvider      DriveInfo: space per volume (every 10 s)                          │
│  NetworkProvider    PDH Network Interface(*): download/upload                         │
│  BatteryProvider    GetSystemPowerStatus                                              │
│  SystemProvider     PDH System: processes, threads; uptime                            │
│  HardwareProvider   LibreHardwareMonitorLib: temperatures, clocks, fans, power        │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

### Solution layout

```
SpecsView.slnx
├─ src/SpecsView.Core      models, contracts, scheduler, history, config (no UI dependency)
├─ src/SpecsView.Sensors   IMetricProvider implementations (PDH, Win32, DXGI, LibreHardwareMonitor)
├─ src/SpecsView.App       WinForms overlay, renderer, tray icon, metrics report
└─ tests/SpecsView.Tests   xUnit tests for Core and Sensors
```

### Core contracts

```csharp
public interface IMetricProvider : IDisposable
{
    string Id { get; }                               // "cpu", "gpu", "hw", ...
    IReadOnlyList<MetricDescriptor> Metrics { get; } // metrics known up front
    bool RequiresElevation { get; }
    void Sample(MetricSink sink);                    // called every tick on the scheduler thread
}

public sealed record MetricDescriptor(string Id, string DisplayName, MetricUnit Unit);

public sealed record MetricSnapshot(
    DateTime Timestamp,
    IReadOnlyDictionary<string, double> Values,
    IReadOnlyDictionary<string, MetricDescriptor> Descriptors);
```

### Design notes

- **On-demand providers**: only providers with at least one selected metric are created, off the UI thread (LibreHardwareMonitor takes about a second to open).
- **Isolated failures**: a provider that throws is logged once and skipped; the others keep working.
- **Runtime discovery**: instances that appear later (disks, adapters, drives) are announced by the provider together with their value (`MetricSink.Add(descriptor, value)`), so a USB drive plugged in later shows up without a restart.
- **One PDH query**: all PDH-based providers share a single query collected once per tick, using English counter paths (`PdhAddEnglishCounter`) so they work on any Windows display language. Wildcard counters are read without per-tick string allocations.
- **Overlay**: a borderless `WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE` window drawn with `UpdateLayeredWindow` (per-pixel alpha). It never steals focus, re-asserts topmost every frame (clicking the taskbar raises it), repositions on `TaskbarCreated` (Explorer restart), `WM_DISPLAYCHANGE` and `WM_DPICHANGED`, and hides while a full-screen app runs. Full screen means the foreground window covers its whole monitor (taskbar included); `SHQueryUserNotificationState` is only trusted for exclusive D3D and presentation mode, because its "busy" state is also reported for ordinary windows. A `EVENT_SYSTEM_FOREGROUND` hook updates visibility and z-order as soon as focus changes. Windows 11 removed DeskBands, so an overlay is the only way to sit on the taskbar.
- **Config**: System.Text.Json source generator; invalid JSON keeps the previous configuration; `schemaVersion` allows future migrations.
- **Logging**: `%LOCALAPPDATA%\SpecsView\logs\specsview.log`, rotated at 1 MB.

## Performance

Targets: under 40 MB of RAM and under 0.5% CPU at a 1 s interval.

Measured on a Ryzen 7 5800X at 1 s:

| Configuration | Private memory | CPU |
|---|---|---|
| PDH metrics only (CPU, RAM) | ~22 MB | ~0.04% |
| 8 items including `hw.*` sensors | ~43 MB | ~0.16% |

LibreHardwareMonitor accounts for about 20 MB, which is why it is only loaded when needed.

## Roadmap

- [x] Core: scheduler, history, hot-reloaded config, formatting, tests
- [x] Taskbar overlay: positioning, DPI, full-screen detection, Explorer restarts, light/dark theme
- [x] Providers: CPU (total, per core, clock), memory, GPU, disk, drives, network, battery, system
- [x] Hardware sensors via LibreHardwareMonitor, restart as administrator
- [x] Metrics report and hover tooltips
- [ ] Settings window with metric discovery
- [ ] Taskbars on secondary monitors
- [ ] Start with Windows (Run key, or an elevated scheduled task for hardware sensors) and app icon
- [x] Single-file executable and per-user install script
- [ ] Portable zip release
