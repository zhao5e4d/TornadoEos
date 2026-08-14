using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TornadoEos.Core.Abstractions;
using TornadoEos.Core.Exceptions;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;

namespace TornadoEos.Core.Backends;

/// <summary>
/// In-memory camera backend that emulates a Canon EOS R50 for development, demos
/// and unit tests. It reproduces the observable behaviour of the real service flow
/// (connect, enter service mode, read/write the language table) without touching
/// any hardware, so it is completely safe to run.
/// </summary>
public sealed class SimulatedCameraBackend : ICameraBackend
{
    private readonly object _gate = new();
    private readonly TimeProfile _timing;
    private bool _connected;
    private bool _serviceMode;
    private bool _languageLock = true; // grey-market R50 ships locked to EN/JA
    private CameraLanguage _current = CameraLanguages.FindByIso("ja")!;

    public SimulatedCameraBackend(TimeProfile? timing = null)
    {
        _timing = timing ?? TimeProfile.Realistic;
    }

    public string Name => "Simulated EOS R50";

    public bool IsConnected { get { lock (_gate) return _connected; } }

    public bool IsInServiceMode { get { lock (_gate) return _serviceMode; } }

    public event EventHandler<LogEntry>? Log;

    public async Task<CameraInfo> ConnectAsync(CancellationToken cancellationToken = default)
    {
        EmitLog(LogLevel.Info, "Scanning USB bus for Canon EOS devices...");
        await Delay(_timing.Connect, cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            _connected = true;
            _serviceMode = false;
        }

        var info = new CameraInfo(
            ModelName: "Canon EOS R50",
            SerialNumber: "31" + Random.Shared.Next(0, 999999).ToString("D6") + "8",
            FirmwareVersion: "1.1.0",
            BatteryPercent: Random.Shared.Next(45, 100),
            PortDescription: "USB (simulated)");

        EmitLog(LogLevel.Success, $"Connected to {info.ModelName}, S/N {info.SerialNumber}, FW {info.FirmwareVersion}.");
        return info;
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await Delay(_timing.Quick, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _connected = false;
            _serviceMode = false;
        }
        EmitLog(LogLevel.Info, "Camera disconnected.");
    }

    public async Task EnterServiceModeAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        EmitLog(LogLevel.Info, "Requesting service mode (vendor PTP handshake)...");
        await Delay(_timing.ServiceMode, cancellationToken).ConfigureAwait(false);
        lock (_gate) _serviceMode = true;
        EmitLog(LogLevel.Success, "Service mode active. Firmware properties are now writable.");
    }

    public async Task<IReadOnlyList<CameraLanguage>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await Delay(_timing.Quick, cancellationToken).ConfigureAwait(false);
        bool locked;
        lock (_gate) locked = _languageLock;
        return locked ? CameraLanguages.LockedSet : CameraLanguages.All.ToList();
    }

    public async Task<CameraLanguage> GetCurrentLanguageAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await Delay(_timing.Quick, cancellationToken).ConfigureAwait(false);
        lock (_gate) return _current;
    }

    public async Task SetMenuLanguageAsync(
        CameraLanguage language,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        EnsureConnected();
        EnsureServiceMode();

        bool available;
        lock (_gate)
        {
            available = _languageLock
                ? CameraLanguages.LockedSet.Any(l => l.IsoCode == language.IsoCode)
                : true;
        }
        if (!available)
            throw new UnsupportedLanguageException(language.IsoCode);

        EmitLog(LogLevel.Info, $"Writing menu language = {language.EnglishName} (id 0x{language.Id:X2})...");
        await RunWrite(progress, "Writing language property", cancellationToken).ConfigureAwait(false);

        lock (_gate) _current = language;
        EmitLog(LogLevel.Success, $"Menu language set to {language.EnglishName}.");
    }

    public async Task<bool> GetLanguageLockAsync(CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        await Delay(_timing.Quick, cancellationToken).ConfigureAwait(false);
        lock (_gate) return _languageLock;
    }

    public async Task SetLanguageLockAsync(
        bool enabled,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        EnsureServiceMode();
        EmitLog(LogLevel.Info, $"{(enabled ? "Enabling" : "Disabling")} regional language lock...");
        await RunWrite(progress, "Writing language-lock property", cancellationToken).ConfigureAwait(false);
        lock (_gate) _languageLock = enabled;
        EmitLog(
            LogLevel.Success,
            enabled
                ? "Language lock enabled. Only English and Japanese are exposed."
                : "Language lock disabled. All firmware languages are now available.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connected = false;
            _serviceMode = false;
        }
    }

    private async Task RunWrite(IProgress<ServiceProgress>? progress, string label, CancellationToken cancellationToken)
    {
        const int steps = 10;
        for (int i = 1; i <= steps; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Delay(_timing.WriteStep, cancellationToken).ConfigureAwait(false);
            progress?.Report(new ServiceProgress(i * 100 / steps, $"{label} ({i * 100 / steps}%)"));
        }
    }

    private void EnsureConnected()
    {
        lock (_gate)
            if (!_connected) throw new CameraNotConnectedException();
    }

    private void EnsureServiceMode()
    {
        lock (_gate)
            if (!_serviceMode) throw new ServiceModeRequiredException();
    }

    private void EmitLog(LogLevel level, string message) =>
        Log?.Invoke(this, LogEntry.Now(level, message));

    private static Task Delay(TimeSpan span, CancellationToken cancellationToken) =>
        span <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(span, cancellationToken);

    /// <summary>Controls how long simulated operations take.</summary>
    public sealed record TimeProfile(TimeSpan Quick, TimeSpan Connect, TimeSpan ServiceMode, TimeSpan WriteStep)
    {
        /// <summary>Human-perceptible delays suitable for the desktop UI.</summary>
        public static readonly TimeProfile Realistic = new(
            Quick: TimeSpan.FromMilliseconds(150),
            Connect: TimeSpan.FromMilliseconds(900),
            ServiceMode: TimeSpan.FromMilliseconds(700),
            WriteStep: TimeSpan.FromMilliseconds(120));

        /// <summary>Zero delays for fast, deterministic unit tests.</summary>
        public static readonly TimeProfile Instant = new(
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
    }
}
