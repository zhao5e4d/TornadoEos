using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TornadoEos.Core.Abstractions;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;

namespace TornadoEos.Core.Backends;

/// <summary>
/// Placeholder for a real hardware backend that talks to a Canon body over USB.
///
/// IMPORTANT: This is intentionally NOT implemented.
///
/// Changing the on-camera menu language / regional language lock is NOT possible
/// through Canon's public EDSDK — that SDK exposes remote-shooting and a fixed set
/// of properties, but no menu-language property. Service tools such as Tornado EOS
/// achieve it through an UNDOCUMENTED, reverse-engineered service/factory mode:
///
///   1. Open a USB/PTP session with the body.
///   2. Issue vendor-specific PTP operations to enter service ("factory") mode.
///   3. Read/write internal firmware property addresses. On older bodies the
///      language-lock property was reported as 0x01000012 (0 = Japan-limited,
///      -1 = no restriction); the address and language table differ per model and
///      are not published for the EOS R50.
///
/// Implementing this for real hardware requires that model-specific protocol
/// knowledge. Guessing addresses risks corrupting firmware / bricking the camera,
/// so this class throws instead of sending fabricated commands. Drop a verified
/// implementation in here behind the same <see cref="ICameraBackend"/> contract and
/// the rest of the application (UI, service layer, tests) works unchanged.
/// </summary>
public sealed class ServiceModeCameraBackend : ICameraBackend
{
    private const string NotImplemented =
        "Real-hardware service-mode protocol for the EOS R50 is not bundled with this " +
        "project (it is proprietary/undocumented). Use the simulated backend, or supply " +
        "a verified protocol implementation in ServiceModeCameraBackend.";

    public string Name => "Canon Service Mode (USB/PTP) — not implemented";

    public bool IsConnected => false;

    public bool IsInServiceMode => false;

    public event EventHandler<LogEntry>? Log;

    public Task<CameraInfo> ConnectAsync(CancellationToken cancellationToken = default) => Fail<CameraInfo>();

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task EnterServiceModeAsync(CancellationToken cancellationToken = default) => Fail<object>();

    public Task<IReadOnlyList<CameraLanguage>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default)
        => Fail<IReadOnlyList<CameraLanguage>>();

    public Task<CameraLanguage> GetCurrentLanguageAsync(CancellationToken cancellationToken = default)
        => Fail<CameraLanguage>();

    public Task SetMenuLanguageAsync(CameraLanguage language, IProgress<ServiceProgress>? progress = null, CancellationToken cancellationToken = default)
        => Fail<object>();

    public Task<bool> GetLanguageLockAsync(CancellationToken cancellationToken = default) => Fail<bool>();

    public Task SetLanguageLockAsync(bool enabled, IProgress<ServiceProgress>? progress = null, CancellationToken cancellationToken = default)
        => Fail<object>();

    public void Dispose() { }

    private Task<T> Fail<T>()
    {
        // Keep the event referenced so the compiler does not warn about it being unused.
        _ = Log;
        throw new NotSupportedException(NotImplemented);
    }
}
