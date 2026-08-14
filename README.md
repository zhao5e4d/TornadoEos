# Tornado EOS — Menu Language Utility

A Windows desktop app (C# / .NET 8 / WPF) that reproduces the **core menu‑language
workflow** of the Canon service tool *Tornado EOS*:

1. **Connect** to a Canon EOS camera (modelled here as an **EOS R50**).
2. **Switch the menu (UI) language** to a language you choose.
3. **Enable / disable the regional language lock** that restricts grey‑market /
   Japan‑region bodies to English + Japanese only.

> Pick any language in the dropdown and click **Apply to camera** — the app enters
> service mode, lifts the language lock automatically if the language is hidden, and
> writes the new menu language.

## Backends — what is real vs. not

The app **defaults to a real backend** (`WpdCameraBackend`). Clicking **Connect**
performs a genuine USB enumeration via the Windows Portable Devices (WPD) API and,
when a Canon body is found, reads **real** device data: model, serial number,
firmware version and battery/power level. If no camera is connected it reports a
clear error (no fallback to simulation).

What is **not** possible over WPD: **setting / unlocking the on‑camera menu
language**. That is **not exposed by Canon's public EDSDK either** (no menu‑language
property). Real service tools do it through an **undocumented, reverse‑engineered
service/factory‑mode protocol** over USB/PTP, writing internal firmware property
addresses that are **model specific and not published** for the EOS R50 — guessing
them risks corrupting firmware. So the language‑set operations throw a clearly worded
`NotSupportedException` on the real backend.

Backends available (all behind the same `ICameraBackend` contract):

| Backend | Connect + read data | Set menu language |
| --- | --- | --- |
| `WpdCameraBackend` (default) | ✅ real, over USB/WPD | ❌ `NotSupportedException` |
| `SimulatedCameraBackend` | ✅ simulated EOS R50 | ✅ simulated (safe demo) |
| `ServiceModeCameraBackend` | stub | stub — where a real service‑mode protocol would go |

To run the safe end‑to‑end language demo, construct the view model / service with
`SimulatedCameraBackend`. The unit tests use it for deterministic coverage.

## Read-only property probe (recon)

`tools/WpdProbe` is a **completely safe, read-only** diagnostic. It enumerates every
connected portable device and dumps the full property set of each device's root
object — model, serial, firmware, power level, plus any **vendor / MTP-specific
properties** Windows surfaces. Vendor/unknown keys are printed with their full
`{GUID}/PID` so you can spot candidate region/language fields.

```powershell
dotnet run --project tools/WpdProbe
```

It **never writes** anything and **never** enters service mode. It is the safe first
step of investigating what a camera exposes over standard USB (PTP/MTP → WPD).

> Scope limit: this shows the Windows (WPD) view of the device. It does **not** send
> raw vendor PTP opcodes (e.g. Canon `0x9xxx` operations) and cannot read firmware
> property addresses like the menu-language lock — those live behind Canon's
> undocumented service-mode protocol, which is not exposed through WPD.

## Project layout

```
TornadoEos.sln
├─ src/
│  ├─ TornadoEos.Core/            Hardware-agnostic core (no UI dependencies)
│  │  ├─ Abstractions/            ICameraBackend transport contract
│  │  ├─ Backends/                SimulatedCameraBackend, ServiceModeCameraBackend (stub)
│  │  ├─ Models/                  CameraLanguage(+catalog), CameraInfo, state, progress
│  │  ├─ Services/                CameraService (connect + set-language orchestration)
│  │  ├─ Logging/                 LogEntry / LogLevel
│  │  └─ Exceptions/
│  ├─ TornadoEos.Hardware/        Real Windows backend (net8.0-windows)
│  │  ├─ WpdInterop.cs            Hand-written WPD COM interop
│  │  └─ WpdCameraBackend.cs      Real USB connect + device-data read
│  └─ TornadoEos.App/             WPF (MVVM) desktop UI
│     ├─ ViewModels/MainViewModel.cs
│     ├─ Mvvm/                    ObservableObject, AsyncRelayCommand
│     ├─ Converters/
│     ├─ App.xaml                 Dark theme + styles
│     └─ MainWindow.xaml          Connect / language / lock / activity log
├─ tools/
│  └─ WpdProbe/                   Console diagnostic: read-only PTP/WPD property probe
└─ tests/
   └─ TornadoEos.Tests/          xUnit tests for the core logic
```

## Architecture

```
MainWindow (XAML)
      │ bindings / commands
MainViewModel  ──────────────►  CameraService  ──────────────►  ICameraBackend
 (UI state, async commands)      (lifecycle, state machine,       ├─ SimulatedCameraBackend  (default, safe)
                                  auto-unlock, events, logging)    └─ ServiceModeCameraBackend (real HW stub)
```

`CameraService.SetMenuLanguageAsync(language, autoUnlock)` is the headline feature:
it ensures a connection, enters service mode, disables the language lock when the
target language is hidden (if `autoUnlock` is true), then writes the language and
reports progress.

## Requirements

- .NET 8 SDK
- Windows (WPF; `TornadoEos.App` targets `net8.0-windows`). The core library and
  tests target `net8.0` and are cross‑platform.

## Build & run

```powershell
dotnet build                                 # build everything
dotnet test                                  # run unit tests
dotnet run --project src/TornadoEos.App      # launch the desktop app
dotnet run --project tools/WpdProbe          # console: read-only PTP/WPD property probe
```

> **Note:** If the .NET 8 SDK is installed to a per‑user location (not the global
> `C:\Program Files\dotnet`), launching the built `.exe` directly may prompt to
> install the desktop runtime. Either run via `dotnet run` (recommended), or set
> `DOTNET_ROOT` to your SDK folder before starting the exe, e.g.
> `$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"`.

## Using the app

1. Click **Connect** — a simulated EOS R50 is detected (battery, S/N, firmware).
   It starts with the language lock **enabled** and the menu language set to Japanese.
2. Choose a language under **Set menu language to** (the full Canon catalog is shown).
3. Leave **Automatically disable the language lock…** checked.
4. Click **Apply to camera** and watch the activity log: it enters service mode,
   disables the lock if needed, and writes the chosen menu language.

## Swapping in real hardware later

Implement `ICameraBackend` (or fill in `ServiceModeCameraBackend`) with a verified
USB/PTP service‑mode protocol for your target body, then construct
`new CameraService(new YourBackend())`. The UI, view model and tests are untouched.
