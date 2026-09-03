using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TornadoEos.Core.Logging;
using TornadoEos.Core.Models;

namespace TornadoEos.Core.Abstractions;

/// <summary>
/// Low-level transport to a single camera body. Implementations encapsulate the
/// concrete wire protocol (a USB/PTP service-mode link on real hardware, or an
/// in-memory simulation for development and testing).
/// </summary>
/// <remarks>
/// This interface deliberately models the workflow used by Canon service software:
/// connect, enter service mode, then read/write firmware properties such as the
/// menu-language table and the regional language lock.
/// </remarks>
public interface ICameraBackend : IDisposable
{
    /// <summary>Stable identifier of the backend implementation (for diagnostics).</summary>
    string Name { get; }

    /// <summary>True when this backend can write the camera menu language.</summary>
    bool SupportsMenuLanguageWrite { get; }

    /// <summary>True when the backend operates entirely in memory without real hardware.</summary>
    bool IsSimulation { get; }

    bool IsConnected { get; }

    bool IsInServiceMode { get; }

    /// <summary>Raised when the backend wants to surface a diagnostic message.</summary>
    event EventHandler<LogEntry>? Log;

    /// <summary>Opens the link to the first available camera and reads its identity.</summary>
    Task<CameraInfo> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions the camera into service / factory mode, which is required before
    /// firmware properties (menu language, language lock, calibration, etc.) can be
    /// written.
    /// </summary>
    Task EnterServiceModeAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the menu languages currently selectable on the body.</summary>
    Task<IReadOnlyList<CameraLanguage>> GetAvailableLanguagesAsync(CancellationToken cancellationToken = default);

    Task<CameraLanguage> GetCurrentLanguageAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes the menu-language property and reports progress while doing so.</summary>
    Task SetMenuLanguageAsync(
        CameraLanguage language,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>True when only the locked language subset (English/Japanese) is exposed.</summary>
    Task<bool> GetLanguageLockAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables the regional language lock. Disabling it exposes every
    /// language stored in firmware.
    /// </summary>
    Task SetLanguageLockAsync(
        bool enabled,
        IProgress<ServiceProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
